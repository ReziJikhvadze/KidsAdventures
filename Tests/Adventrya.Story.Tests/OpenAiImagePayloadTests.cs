using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// What the OpenAI image request actually says on the wire, asserted against a stub of the
/// endpoint rather than the endpoint.
///
/// These exist because of one production failure. On 2026-09-01 the pictures moved to
/// gpt-image-2 and the first composite book failed CHILD_IDENTITY QA over and over: the drawn
/// child was not the child in the uploaded photograph. The images/edits request — the one route
/// in this service that carries reference photos — was sending no <c>input_fidelity</c> at all,
/// and on the gpt-image-1 family that field defaults to LOW, the setting that discards exactly
/// the facial detail the whole product rests on.
///
/// The awkward part, and the reason these tests are shaped the way they are: gpt-image-2 refuses
/// the field. It reads every input at high fidelity by itself and answers a request carrying
/// input_fidelity with a 400 (<c>invalid_input_fidelity_model</c>). So the fix is not "always
/// send high" — it is "send high wherever it can be asked for, and never where asking is an
/// outage", and both halves of that are worth a test.
/// </summary>
public class OpenAiImagePayloadTests
{
    [Fact]
    public async Task An_anchored_illustration_asks_for_high_fidelity_where_the_model_allows_it()
    {
        // gpt-image-1.5, not the configured gpt-image-2: this is the case the field exists for.
        var handler = new CapturingHandler();
        var service = Service(handler, options => options.ImageEditModel = "gpt-image-1.5");

        await service.GenerateStoryImageAsync("draw the spread", HeroPhoto(), CancellationToken.None);

        Assert.EndsWith("images/edits", handler.LastUri!.ToString(), StringComparison.Ordinal);

        // A form field, because this route is multipart — not a JSON property.
        Assert.Equal("high", FormField(handler.LastBody!, "input_fidelity"));
        Assert.Equal("gpt-image-1.5", FormField(handler.LastBody!, "model"));
    }

    [Fact]
    public async Task A_picture_drawn_from_the_prompt_alone_never_mentions_fidelity()
    {
        // No references means images/generations, where there is no input to be faithful to. The
        // field would be meaningless there and the endpoint does not take it.
        var handler = new CapturingHandler();
        var service = Service(handler, options => options.ImageEditModel = "gpt-image-1.5");

        await service.GenerateStoryImageAsync("draw the spread", null, CancellationToken.None);

        Assert.EndsWith("images/generations", handler.LastUri!.ToString(), StringComparison.Ordinal);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.False(body.RootElement.TryGetProperty("input_fidelity", out _));
    }

    [Fact]
    public async Task An_empty_setting_leaves_the_field_out_entirely()
    {
        // The escape hatch. A deployment that meets a model this code has not been taught about
        // can switch the field off rather than wait for a release.
        var handler = new CapturingHandler();
        var service = Service(handler, options =>
        {
            options.ImageEditModel = "gpt-image-1.5";
            options.ImageInputFidelity = string.Empty;
        });

        await service.GenerateStoryImageAsync("draw the spread", HeroPhoto(), CancellationToken.None);

        Assert.Null(FormField(handler.LastBody!, "input_fidelity"));
    }

    [Fact]
    public async Task Gpt_image_2_is_never_told_to_do_what_it_already_does()
    {
        // The live configuration. gpt-image-2 processes every reference at high fidelity on its
        // own and rejects the instruction with a 400, so sending the setting here would trade a
        // book that comes out slightly wrong for a book that does not come out at all.
        var handler = new CapturingHandler();
        var service = Service(handler, options => options.ImageEditModel = "gpt-image-2");

        await service.GenerateStoryImageAsync("draw the spread", HeroPhoto(), CancellationToken.None);

        Assert.Equal("gpt-image-2", FormField(handler.LastBody!, "model"));
        Assert.Null(FormField(handler.LastBody!, "input_fidelity"));
    }

    /// <summary>
    /// The decision itself, including the models the edit route cannot be made to reach — it
    /// resolves anything unrecognised to a GPT Image model before it sends, so a DALL·E name can
    /// never appear in a multipart body no matter what the configuration says. The rule still has
    /// to be right for the day some other caller asks.
    /// </summary>
    [Theory]
    [InlineData("gpt-image-1.5", "high", "high")]
    [InlineData("gpt-image-1", "high", "high")]
    [InlineData("gpt-image-1-mini", "low", "low")]
    [InlineData("gpt-image-1.5", "HIGH", "high")]
    [InlineData("gpt-image-1.5", " high ", "high")]
    [InlineData("gpt-image-2", "high", null)]
    [InlineData("gpt-image-2-mini", "high", null)]
    [InlineData("dall-e-3", "high", null)]
    [InlineData("gpt-image-1.5", "", null)]
    [InlineData("gpt-image-1.5", "   ", null)]
    // A typo is a 400 waiting to happen, and a page drawn at the default beats a page not drawn.
    [InlineData("gpt-image-1.5", "hgih", null)]
    public void The_fidelity_rule_covers_every_model_and_every_setting(
        string model, string configured, string? expected) =>
        Assert.Equal(expected, OpenAiService.ResolveImageInputFidelity(model, configured));

    // ---- harness ---------------------------------------------------------

    private static OpenAiService Service(CapturingHandler handler, Action<OpenAiOptions>? configure = null)
    {
        var options = new OpenAiOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://openai.test/v1/",
            EnableStoryImages = true,
            ImageModel = "gpt-image-2",
            ImageEditModel = "gpt-image-2",
            ImageQuality = "medium",
            LogPrompts = false,
        };

        configure?.Invoke(options);

        return new OpenAiService(
            new SingleClientFactory(handler),
            new PassThroughNormalizer(),
            Options.Create(options),
            NullLogger<OpenAiService>.Instance);
    }

    private static StoryImageReference HeroPhoto() => new()
    {
        CastPhotos =
        [
            new CastPhotoReference
            {
                Name = "ნინი",
                Relationship = "hero",
                IsHero = true,
                Bytes = [0x01, 0x02, 0x03, 0x04],
                ContentType = "image/png",
            },
        ],
    };

    /// <summary>
    /// Reads one multipart text field back out of the raw body. Crude on purpose: the point of
    /// these tests is the bytes OpenAI would receive, and a parser that reconstructed them from
    /// the same objects the service built would be asserting against itself.
    ///
    /// Both spellings of the disposition are accepted because .NET writes both: a plain token
    /// like <c>name=model</c> goes out bare, while <c>name="image[]"</c> gets quotes. Matching
    /// only the quoted form is how the first run of these tests failed against a request that was
    /// in fact correct.
    /// </summary>
    private static string? FormField(string body, string name)
    {
        var header = Regex.Match(body, $"name=\"?{Regex.Escape(name)}\"?(;|\r\n)");
        if (!header.Success)
        {
            return null;
        }

        var at = header.Index;
        var start = body.IndexOf("\r\n\r\n", at, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += 4;
        var end = body.IndexOf("\r\n--", start, StringComparison.Ordinal);
        return end < 0 ? body[start..] : body[start..end];
    }

    /// <summary>
    /// Answers every image call with one tiny picture and keeps what was asked. The body is read
    /// as Latin-1 so that the PNG part of a multipart body cannot throw on its way to a string —
    /// the text fields either side of it survive byte for byte, which is all these read.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : Encoding.Latin1.GetString(await request.Content.ReadAsByteArrayAsync(cancellationToken));

            var payload = JsonSerializer.Serialize(new
            {
                data = new[] { new { b64_json = Convert.ToBase64String(new byte[] { 9, 9, 9, 9 }) } },
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        // The service asks for a named client per call and posts relative paths, so the base
        // address has to come from here the way the DI registration supplies it in production.
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://openai.test/v1/"),
        };
    }

    private sealed class PassThroughNormalizer : IReferenceImageNormalizer
    {
        public NormalizedReferenceImage NormalizeForOpenAi(byte[] bytes, string? hintContentType = null) =>
            new(bytes, hintContentType ?? "image/png", "reference.png");

        public NormalizedReferenceImage NormalizeForStorageWebp(byte[] bytes, string? hintContentType = null) =>
            new(bytes, "image/webp", "illustration.webp");
    }
}
