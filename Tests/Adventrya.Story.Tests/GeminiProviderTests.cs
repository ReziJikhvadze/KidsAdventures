using System.Net;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Services.Ai;
using AdventurePacks.Api.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// The Gemini side, tested against a stub of the wire rather than the vendor.
///
/// What can be proven without a key is everything that has ever actually broken in this kind of
/// integration: that the key travels in the header and not the URL, that the pictures reach the
/// request in the order the reviewer is told to expect, that the reply is read out of a structure
/// the provider is free to add steps to, and that a refusal fails loudly instead of returning
/// zero bytes. What cannot be proven here is that Google agrees with the shape — only a live call
/// settles that, and it is the first thing to run once a key exists.
/// </summary>
public class GeminiProviderTests
{
    [Fact]
    public async Task Story_call_sends_the_schema_and_the_key_header()
    {
        var handler = new CapturingHandler(TextResponse("{\"title\":\"ტესტი\"}"));
        var client = StoryClient(handler, out _);

        var schema = JsonDocument.Parse(
            "{\"type\":\"object\",\"properties\":{\"title\":{\"type\":\"string\"}}}").RootElement;

        var result = await client.CompleteAsync<Plan>(
            "gpt-5.6-luna", "system rules", "write a book", "plan", schema, CancellationToken.None);

        Assert.Equal("ტესტი", result.Value.Title);

        var request = handler.LastRequest!;
        Assert.Equal(
            "https://gemini.test/v1beta/interactions", request.RequestUri!.ToString());
        Assert.Equal("test-key", request.Headers.GetValues("x-goog-api-key").Single());

        // The key must never ride in the URL, where every proxy log would keep a copy.
        Assert.DoesNotContain("test-key", request.RequestUri.ToString());

        using var body = JsonDocument.Parse(handler.LastBody!);

        // The configured model, not the OpenAI product name the caller passed and not whatever
        // the default happens to be this month — asserting the default made this test fail the
        // day the default moved, which measured nothing about the client.
        Assert.Equal("gemini-under-test", body.RootElement.GetProperty("model").GetString());

        var format = body.RootElement.GetProperty("response_format");
        Assert.Equal("application/json", format.GetProperty("mime_type").GetString());
        Assert.True(format.TryGetProperty("schema", out _));

        // One prompt carrying both halves, in reading order.
        var prompt = body.RootElement.GetProperty("input")[0].GetProperty("text").GetString()!;
        Assert.StartsWith("system rules", prompt);
        Assert.Contains("write a book", prompt);
    }

    [Fact]
    public async Task Story_call_reports_thinking_tokens_as_output()
    {
        // Thinking is billed as output, so a Gemini book's cost line has to mean what an OpenAI
        // book's does — otherwise the two are not comparable, which is the point of the switch.
        var handler = new CapturingHandler(TextResponse(
            "{\"title\":\"x\"}", inputTokens: 11, outputTokens: 20, thoughtTokens: 22));
        var client = StoryClient(handler, out _);

        var schema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement;
        var result = await client.CompleteAsync<Plan>(
            "any", "s", "u", "plan", schema, CancellationToken.None);

        Assert.Equal(11, result.PromptTokens);
        Assert.Equal(42, result.CompletionTokens);
    }

    [Fact]
    public async Task Image_call_asks_for_jpeg_and_hands_back_png()
    {
        // The API refuses image/png outright — "Supported values: 'image/jpeg'" — while every
        // consumer downstream of here names, stores and serves the result as a PNG. The client
        // absorbs the difference so nothing else has to know which vendor drew the picture.
        var handler = new CapturingHandler(ImageResponse(Jpeg()));
        var client = IllustrationClient(handler);

        var bytes = await client.GenerateStoryImageAsync(
            "draw a spread", null, CancellationToken.None, "1536x1024");

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var format = body.RootElement.GetProperty("response_format");
        Assert.Equal("image", format.GetProperty("type").GetString());
        Assert.Equal("image/jpeg", format.GetProperty("mime_type").GetString());
        Assert.Equal("3:2", format.GetProperty("aspect_ratio").GetString());
        Assert.Equal("gemini-3.1-flash-image", body.RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData("1536x1024", "3:2")]
    [InlineData("1024x1536", "2:3")]
    [InlineData("1024x1024", "1:1")]
    [InlineData("1792x1024", "7:4")]
    [InlineData("nonsense", "1:1")]
    public void Aspect_ratios_reduce(string size, string expected) =>
        Assert.Equal(expected, GeminiIllustrationClient.AspectRatioFor(size));

    [Fact]
    public async Task Review_sends_the_verdict_prompt_then_the_picture_then_each_labelled_reference()
    {
        var handler = new CapturingHandler(TextResponse("{\"status\":\"PASS\",\"issues\":[]}"));
        var client = IllustrationClient(handler);

        var verdict = await client.ReviewIllustrationAsync(
            Png(), "judge this",
            [(Png(), "image/png", "Beki master reference")],
            CancellationToken.None);

        Assert.Contains("PASS", verdict);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var input = body.RootElement.GetProperty("input").EnumerateArray().ToList();

        Assert.Equal("judge this", input[0].GetProperty("text").GetString());
        Assert.Equal("image", input[2].GetProperty("type").GetString());
        Assert.Contains("Beki master reference", input[3].GetProperty("text").GetString());
        Assert.Equal("image", input[4].GetProperty("type").GetString());

        // A verdict is prose the caller sanitizes; sending a schema too would be a second
        // contract to keep in step with the prompt.
        Assert.False(body.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task A_reply_with_no_picture_fails_and_carries_what_the_model_said()
    {
        // A refusal arrives as a perfectly successful response containing prose. Returning empty
        // bytes here would put a blank page in a paid book.
        var handler = new CapturingHandler(TextResponse("I can't draw children."));
        var client = IllustrationClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GenerateStoryImageAsync("draw", null, CancellationToken.None, "1024x1024"));

        Assert.Contains("can't draw children", error.Message);
    }

    [Fact]
    public async Task The_answer_is_found_even_when_the_provider_adds_steps_in_front_of_it()
    {
        // A thinking step before the answer is exactly the shape that breaks a client which
        // reads steps[0].
        const string reply = """
        {
          "steps": [
            { "type": "thought", "content": [ { "type": "text", "text": "" } ] },
            { "type": "model_output", "content": [ { "type": "text", "text": "{\"title\":\"ok\"}" } ] }
          ],
          "usage": { "total_input_tokens": 1, "total_output_tokens": 2 }
        }
        """;

        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(reply, Encoding.UTF8, "application/json")
        });

        var schema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement;
        var result = await StoryClient(handler, out _)
            .CompleteAsync<Plan>("any", "s", "u", "plan", schema, CancellationToken.None);

        Assert.Equal("ok", result.Value.Title);
    }

    [Fact]
    public async Task A_missing_key_fails_before_anything_is_sent()
    {
        var handler = new CapturingHandler(TextResponse("{}"));
        var client = IllustrationClient(handler, apiKey: "");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GenerateStoryImageAsync("draw", null, CancellationToken.None, "1024x1024"));

        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public void Provider_names_are_recognised_case_insensitively_and_typos_are_not()
    {
        Assert.True(new AiProviderOptions { Story = "gemini" }.UsesGeminiForStory);
        Assert.True(new AiProviderOptions { Images = "GEMINI" }.UsesGeminiForImages);
        Assert.False(new AiProviderOptions().UsesGeminiForImages);
        Assert.True(AiProvider.IsKnown("OpenAI"));
        Assert.False(AiProvider.IsKnown("gemeni"));
    }

    [Fact]
    public async Task The_router_sends_pictures_to_gemini_and_leaves_the_legacy_text_path_alone()
    {
        var illustrations = new RecordingIllustrationClient();
        var openAi = new RecordingOpenAiService();
        var router = new AiServiceRouter(openAi, illustrations, NullLogger<AiServiceRouter>.Instance);

        await router.GenerateStoryImageAsync("p", null, CancellationToken.None);
        await router.ReviewIllustrationAsync([1], "r", [], CancellationToken.None);
        await router.DescribeCharacterFromPhotoAsync([1], "image/png", "d", CancellationToken.None);
        await router.CompleteTextAsync("t", CancellationToken.None);

        Assert.Equal(3, illustrations.Calls);
        Assert.Equal(0, openAi.ImageCalls);
        Assert.Equal(1, openAi.TextCalls);
    }

    // ---- harness ---------------------------------------------------------

    private sealed record Plan
    {
        public string Title { get; init; } = string.Empty;
    }

    private static GeminiStoryModelClient StoryClient(CapturingHandler handler, out GeminiOptions options)
    {
        options = new GeminiOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://gemini.test/v1beta",
            StoryModel = "gemini-under-test",
        };
        return new GeminiStoryModelClient(
            Transport(handler, options),
            Options.Create(options),
            Options.Create(new OpenAiOptions { LogPrompts = false }),
            NullLogger<GeminiStoryModelClient>.Instance);
    }

    private static GeminiIllustrationClient IllustrationClient(
        CapturingHandler handler, string apiKey = "test-key")
    {
        var options = new GeminiOptions { ApiKey = apiKey, BaseUrl = "https://gemini.test/v1beta" };
        return new GeminiIllustrationClient(
            Transport(handler, options),
            new PassThroughNormalizer(),
            Options.Create(options),
            Options.Create(new OpenAiOptions { LogPrompts = false, EnableStoryImages = true }),
            NullLogger<GeminiIllustrationClient>.Instance);
    }

    private static GeminiInteractionsClient Transport(CapturingHandler handler, GeminiOptions options) =>
        new(new StubHttpClientFactory(handler),
            Options.Create(options),
            NullLogger<GeminiInteractionsClient>.Instance);

    private static HttpResponseMessage TextResponse(
        string text, int inputTokens = 1, int outputTokens = 1, int thoughtTokens = 0)
    {
        var payload = new
        {
            steps = new[]
            {
                new { type = "model_output", content = new[] { new { type = "text", text } } }
            },
            usage = new
            {
                total_input_tokens = inputTokens,
                total_output_tokens = outputTokens,
                total_thought_tokens = thoughtTokens
            }
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage ImageResponse(byte[] bytes)
    {
        var payload = new
        {
            steps = new[]
            {
                new
                {
                    type = "model_output",
                    content = new[]
                    {
                        new { type = "image", mime_type = "image/png", data = Convert.ToBase64String(bytes) }
                    }
                }
            },
            usage = new { total_input_tokens = 1, total_output_tokens = 1 }
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
    }

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47];

    /// <summary>A real one-pixel JPEG: the client decodes what comes back, so a stub will not do.</summary>
    private static byte[] Jpeg()
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(8, 8);
        using var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
        return buffer.ToArray();
    }

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>
    /// Normalizing decodes and re-encodes real images, which these tests do not have and do not
    /// need: what is under test is which bytes reach the request, not what they look like.
    /// </summary>
    private sealed class PassThroughNormalizer : IReferenceImageNormalizer
    {
        public NormalizedReferenceImage NormalizeForOpenAi(byte[] bytes, string? hintContentType = null) =>
            new(bytes, hintContentType ?? "image/png", "reference.png");

        public NormalizedReferenceImage NormalizeForStorageWebp(byte[] bytes, string? hintContentType = null) =>
            new(bytes, "image/webp", "illustration.webp");
    }

    private sealed class RecordingIllustrationClient : IIllustrationClient
    {
        public int Calls { get; private set; }

        public Task<byte[]> GenerateStoryImageAsync(
            string imagePrompt, StoryImageReference? reference,
            CancellationToken cancellationToken, string? imageSize = null)
        {
            Calls++;
            return Task.FromResult<byte[]>([1]);
        }

        public Task<string> ReviewIllustrationAsync(
            byte[] imageBytes, string reviewPrompt,
            IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult("{}");
        }

        public Task<string> DescribeCharacterFromPhotoAsync(
            byte[] imageBytes, string contentType, string promptText,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult("a child");
        }
    }

    private sealed class RecordingOpenAiService : IOpenAiService
    {
        public int ImageCalls { get; private set; }
        public int TextCalls { get; private set; }

        public Task<AdventurePacks.Api.DTOs.AdventurePacks.AdventureContentDto> GenerateAdventureContentAsync(
            AdventureGenerationInput input, Guid adventureId, CancellationToken cancellationToken)
        {
            TextCalls++;
            return Task.FromResult(new AdventurePacks.Api.DTOs.AdventurePacks.AdventureContentDto());
        }

        public Task<byte[]> GenerateStoryImageAsync(
            string imagePrompt, StoryImageReference? reference,
            CancellationToken cancellationToken, string? imageSize = null)
        {
            ImageCalls++;
            return Task.FromResult<byte[]>([1]);
        }

        public Task<string> ReviewIllustrationAsync(
            byte[] imageBytes, string reviewPrompt,
            IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
            CancellationToken cancellationToken)
        {
            ImageCalls++;
            return Task.FromResult("{}");
        }

        public Task<string> DescribeCharacterFromPhotoAsync(
            byte[] imageBytes, string contentType, string promptText, CancellationToken cancellationToken)
        {
            ImageCalls++;
            return Task.FromResult(string.Empty);
        }

        public Task<string> CompleteTextAsync(string promptText, CancellationToken cancellationToken)
        {
            TextCalls++;
            return Task.FromResult(string.Empty);
        }
    }
}
