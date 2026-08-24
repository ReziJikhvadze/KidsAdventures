using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Services.Ai;
using AdventurePacks.Api.Services.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// The Gemini clients against the real API, once.
///
/// The stubbed tests prove the code does what this repository believes the API is; only these
/// prove the belief. That distinction earned its keep the first time they ran: the request shape
/// was right, and two things the documentation did not say were not — the reply opens with a
/// <c>thought</c> step carrying no content at all, and image responses refuse
/// <c>image/png</c> outright. Both were found here and fixed in the client.
///
/// Skipped unless ADVENTRYA_GEMINI_KEY is set, like the other live tests, because it spends
/// money. The image test additionally needs a key on a billed project: image models are capped
/// at zero on the free tier, which arrives as a 429 rather than a refusal to draw.
/// </summary>
public class LiveGeminiProviderTests(ITestOutputHelper output)
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("ADVENTRYA_GEMINI_KEY");

    [SkippableFact]
    public async Task Writes_structured_georgian_against_a_schema()
    {
        Skip.If(string.IsNullOrWhiteSpace(ApiKey), "ADVENTRYA_GEMINI_KEY is not set.");

        var client = new GeminiStoryModelClient(
            Transport(),
            Options.Create(Gemini()),
            Options.Create(new OpenAiOptions { LogPrompts = false }),
            NullLogger<GeminiStoryModelClient>.Instance);

        var schema = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": { "title": { "type": "string" }, "hero": { "type": "string" } },
              "required": ["title", "hero"],
              "additionalProperties": false
            }
            """).RootElement;

        var result = await client.CompleteAsync<Titled>(
            "ignored-openai-name",
            "You write children's books in Georgian.",
            "Invent a title for a book about a girl named თამარი and a flying machine.",
            "titled",
            schema,
            CancellationToken.None);

        output.WriteLine($"title: {result.Value.Title}");
        output.WriteLine($"hero:  {result.Value.Hero}");
        output.WriteLine($"tokens: in={result.PromptTokens} out={result.CompletionTokens}");

        Assert.False(string.IsNullOrWhiteSpace(result.Value.Title));

        // The schema is the contract, and Georgian is the product: a model that answered in
        // English would satisfy the schema and still be useless here.
        Assert.Contains(result.Value.Title, c => c is >= 'ა' and <= 'ჰ');

        // Thinking is billed and counted; a book that reported zero output tokens would be
        // under-reporting its own cost.
        Assert.True(result.CompletionTokens > 0);
    }

    [SkippableFact]
    public async Task Judges_a_picture_and_returns_a_verdict_the_pipeline_can_read()
    {
        Skip.If(string.IsNullOrWhiteSpace(ApiKey), "ADVENTRYA_GEMINI_KEY is not set.");

        var client = new GeminiIllustrationClient(
            Transport(),
            new ReferenceImageNormalizer(NullLogger<ReferenceImageNormalizer>.Instance),
            Options.Create(Gemini()),
            Options.Create(new OpenAiOptions { LogPrompts = false, EnableStoryImages = true }),
            NullLogger<GeminiIllustrationClient>.Instance);

        // A picture whose one correct verdict is known in advance: it is nothing but lettering.
        var verdict = await client.ReviewIllustrationAsync(
            LetteringPng(),
            "Reply with JSON only: {\"status\":\"PASS\"|\"FAIL\",\"issues\":[\"...\"]}. "
            + "FAIL if the image contains any legible lettering.",
            [],
            CancellationToken.None);

        output.WriteLine(verdict);

        // Straight through the sanitizer the pipeline uses, because the live answer arrives
        // inside a ```json fence — which is exactly why the reviewer is not given a schema.
        var json = ModelJsonSanitizer.ExtractJsonObject(verdict);
        Assert.False(string.IsNullOrWhiteSpace(json));

        using var document = JsonDocument.Parse(json);
        Assert.Equal("FAIL", document.RootElement.GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Draws_a_spread_shaped_picture()
    {
        Skip.If(string.IsNullOrWhiteSpace(ApiKey), "ADVENTRYA_GEMINI_KEY is not set.");

        var client = new GeminiIllustrationClient(
            Transport(),
            new ReferenceImageNormalizer(NullLogger<ReferenceImageNormalizer>.Instance),
            Options.Create(Gemini()),
            Options.Create(new OpenAiOptions { LogPrompts = false, EnableStoryImages = true }),
            NullLogger<GeminiIllustrationClient>.Instance);

        byte[] png;
        try
        {
            png = await client.GenerateStoryImageAsync(
                "A small blue toy aeroplane resting on a white cloud, soft 3D children's book "
                + "illustration, bright morning sky, no text anywhere.",
                null,
                CancellationToken.None,
                "1536x1024");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("429"))
        {
            // Image models are capped at zero on the free tier. That is a billing fact about the
            // key, not a fault in the client, and failing the suite for it would teach nobody
            // anything.
            Skip.If(true, $"The Gemini key has no image quota: {ex.Message}");
            throw;
        }

        using var image = SixLabors.ImageSharp.Image.Load(png);
        output.WriteLine($"{image.Width}x{image.Height}, {png.Length} bytes");

        // PNG on the way out, whatever the API returned, because everything downstream stores and
        // serves these bytes as one.
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);

        // 3:2, because that is the shape a Beki spread is cropped from.
        Assert.InRange((double)image.Width / image.Height, 1.4, 1.6);
    }

    private GeminiInteractionsClient Transport() =>
        new(new SingleClientFactory(), Options.Create(Gemini()),
            NullLogger<GeminiInteractionsClient>.Instance);

    private static GeminiOptions Gemini() => new()
    {
        ApiKey = ApiKey ?? string.Empty,
        StoryModel = Environment.GetEnvironmentVariable("ADVENTRYA_GEMINI_STORY_MODEL")
                     ?? "gemini-3.6-flash",
        VisionModel = Environment.GetEnvironmentVariable("ADVENTRYA_GEMINI_VISION_MODEL")
                      ?? "gemini-3.6-flash",
        ImageModel = Environment.GetEnvironmentVariable("ADVENTRYA_GEMINI_IMAGE_MODEL")
                     ?? "gemini-3.1-flash-image",
        ImageSize = "1K",
    };

    /// <summary>
    /// A picture that is nothing but words, drawn with the same typesetter the books use — so the
    /// one correct verdict is known before the model is asked, and a PASS would mean the reviewer
    /// is not looking rather than that the picture is fine.
    /// </summary>
    private static byte[] LetteringPng()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        AdventurePacks.Api.Services.Pdf.PdfFontBootstrap.EnsureRegistered();

        var pages = Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(160, 60, Unit.Millimetre);
                page.PageColor(QuestPDF.Helpers.Colors.White);
                page.Content().AlignMiddle().AlignCenter()
                    .Text("HELLO BOOK — გამარჯობა")
                    .FontFamily(AdventurePacks.Api.Services.Pdf.PdfFontBootstrap.BodyFamily)
                    .FontSize(28)
                    .FontColor(QuestPDF.Helpers.Colors.Black);
            });
        }).GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = ImageFormat.Png,
            RasterDpi = 96
        });

        return pages.First();
    }

    private sealed record Titled
    {
        public string Title { get; init; } = string.Empty;
        public string Hero { get; init; } = string.Empty;
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
