using System.Net;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services.Ai;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// What a finished book can say about the pictures it is made of.
///
/// Before this, nothing: the pipeline logged <c>Beki:ImageModel</c> — a setting the image routes
/// need not send — and no stored document named the frame, the quality, the route or the pixels
/// that came back. A printed spread's stored base could be measured, and everything else about how
/// it was bought was gone. That was tolerable while the frame was a constant. It is not now that it
/// is a setting somebody may change between two books.
///
/// So each accepted base carries a receipt, and the receipt describes the picture that was KEPT —
/// a page that spent its regeneration carries the second picture's numbers, because the second is
/// what gets printed.
/// </summary>
public class CompositeGenerationProvenanceTests : CompositePipelineTestBase
{
    /// <summary>
    /// The receipt on a spread, from a provider double that says nothing about itself.
    ///
    /// This is the honest floor: the default interface member measures the picture and reports the
    /// provider as unknown rather than claiming one. What it must still get right is the part it
    /// can actually observe — the pixels that came back and the crop that was applied to them —
    /// because that pair is what proves a stored 1536×717 base came from a 1536×1024 frame.
    /// </summary>
    [Fact]
    public async Task Every_drawn_spread_carries_a_receipt_with_its_pixels_and_its_crop()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        var artifacts = result.Artifacts.Spreads;
        Assert.Equal(8, artifacts.Count);

        foreach (var artifact in artifacts)
        {
            using var receipt = JsonDocument.Parse(
                Assert.IsType<string>(artifact.GenerationReceiptJson));
            var root = receipt.RootElement;

            // Honest rather than invented: this double implements only the picture-returning
            // method, so the provenance member's default fills in what it cannot know.
            Assert.Equal("unknown", root.GetProperty("provider").GetString());
            Assert.Equal("unknown", root.GetProperty("model").GetString());
            Assert.Equal("unknown", root.GetProperty("endpoint").GetString());

            // The frame asked for is known to the caller whatever the provider says.
            Assert.Equal("1536x1024", root.GetProperty("requested_size").GetString());

            Assert.Equal(
                [ProviderWidth, ProviderHeight],
                root.GetProperty("returned_px").EnumerateArray()
                    .Select(value => value.GetInt32()).ToArray());

            var crop = root.GetProperty("crop");
            Assert.Equal("15:7", crop.GetProperty("ratio").GetString());
            Assert.Equal(
                [ProviderWidth, ProviderHeight],
                crop.GetProperty("before_px").EnumerateArray()
                    .Select(value => value.GetInt32()).ToArray());
            Assert.Equal(
                [SpreadWidth, SpreadHeight],
                crop.GetProperty("after_px").EnumerateArray()
                    .Select(value => value.GetInt32()).ToArray());

            Assert.Equal(
                CompositeIllustrationPrompt.Version,
                root.GetProperty("prompt_version").GetString());
            Assert.True(root.GetProperty("elapsed_ms").GetInt64() >= 0);
        }
    }

    /// <summary>
    /// And with a provider that does describe itself, the real values travel all the way to the
    /// artifact the fulfilment layer stores — including the provider's own echo of what it did,
    /// which is the only independent evidence that the size asked for is the size charged for.
    /// </summary>
    [Fact]
    public async Task A_provider_that_reports_itself_reaches_the_stored_receipt_intact()
    {
        var images = new ProvenanceImageService
        {
            Draw = _ => Png(2048, 1152),
            Provider = "openai",
            Model = "gpt-image-2",
            Endpoint = "images/edits",
            ResponseSize = "2048x1152",
            ResponseQuality = "high",
            OutputFormat = "png",
        };

        var beki = new BekiOptions
        {
            CompositePipelineEnabled = true,
            SpreadConcurrency = 1,
            SpreadImageSize = "2048x1152",
            CoverWrapImageSize = "2048x1152",
            PageImageQuality = "high",
        };

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images, beki)
            .RunAsync(Request(), CancellationToken.None);

        using var receipt = JsonDocument.Parse(
            Assert.IsType<string>(result.Artifacts.Spreads[0].GenerationReceiptJson));
        var root = receipt.RootElement;

        Assert.Equal("openai", root.GetProperty("provider").GetString());
        Assert.Equal("gpt-image-2", root.GetProperty("model").GetString());
        Assert.Equal("images/edits", root.GetProperty("endpoint").GetString());
        Assert.Equal("2048x1152", root.GetProperty("requested_size").GetString());
        Assert.Equal("2048x1152", root.GetProperty("response_size").GetString());
        Assert.Equal("high", root.GetProperty("response_quality").GetString());
        Assert.Equal("png", root.GetProperty("output_format").GetString());

        // The opted-in frame is what the pipeline actually asked for — the spread's key for a
        // spread — and the anchor still buys its own quality.
        Assert.All(images.Sizes, size => Assert.Equal("2048x1152", size));
        Assert.Equal("high", images.Qualities[0]);

        // 2048 wide at 15:7 is 956 high; the receipt records both ends of that crop.
        var crop = root.GetProperty("crop");
        Assert.Equal(
            [2048, 1152],
            crop.GetProperty("before_px").EnumerateArray().Select(v => v.GetInt32()).ToArray());
        Assert.Equal(
            2048,
            crop.GetProperty("after_px").EnumerateArray().First().GetInt32());
    }

    /// <summary>
    /// The cover wrap's receipt records its own crop — 512:245, not the interior's 15:7 — and its
    /// own configured frame.
    ///
    /// The two are separate keys because the wrap is one picture per book and is cropped to a
    /// different shape; a receipt that reported the spread's ratio here would describe a crop that
    /// never happened.
    /// </summary>
    [Fact]
    public async Task The_cover_wrap_receipt_records_the_wrap_s_own_crop()
    {
        var images = new ProvenanceImageService
        {
            Draw = _ => Png(ProviderWidth, ProviderHeight),
            Provider = "openai",
            Model = "gpt-image-2",
            Endpoint = "images/edits",
        };

        var pipeline = Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images);
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        var wrap = await pipeline.DrawCoverWrapAsync(
            Context(), scenario, Photo(), "image/png", IdentityFixture, childAnchor: null,
            CancellationToken.None);

        using var receipt = JsonDocument.Parse(
            Assert.IsType<string>(wrap.GenerationReceiptJson));
        var crop = receipt.RootElement.GetProperty("crop");

        Assert.Equal("512:245", crop.GetProperty("ratio").GetString());
        Assert.Equal(
            [ProviderWidth, ProviderHeight],
            crop.GetProperty("before_px").EnumerateArray().Select(v => v.GetInt32()).ToArray());

        // 1536 ÷ (512/245) = 735 — the height of every stored wrap base this product has made.
        Assert.Equal(
            [ProviderWidth, 735],
            crop.GetProperty("after_px").EnumerateArray().Select(v => v.GetInt32()).ToArray());

        Assert.Equal(
            CompositeIllustrationPrompt.CoverVersion,
            receipt.RootElement.GetProperty("prompt_version").GetString());

        // The wrap is bought at the wrap's key, and there is exactly one of it.
        Assert.Equal(["1536x1024"], images.Sizes);
    }

    /// <summary>
    /// A book drawn by Gemini says Gemini.
    ///
    /// The router forwards the provenance call to whichever client it actually routes to and never
    /// answers for it. That is not pedantry: <see cref="IOpenAiService"/> is the interface name on
    /// both paths, and a receipt that read the interface rather than the client would say "openai"
    /// on every picture Google ever drew — the exact failure the receipt exists to end.
    /// </summary>
    [Fact]
    public async Task The_router_reports_the_vendor_that_actually_drew_the_picture()
    {
        var gemini = GeminiClient(Jpeg(1536, 1024));

        var router = new AiServiceRouter(
            new ProvenanceImageService { Draw = _ => Png(8, 8), Provider = "openai" },
            gemini,
            NullLogger<AiServiceRouter>.Instance);

        var generated = await router.GenerateStoryImageWithProvenanceAsync(
            "draw", HeroPhoto(), CancellationToken.None, "1536x1024",
            requireReferences: true, imageQuality: "medium");

        Assert.Equal("gemini", generated.Provider);
        Assert.Equal("gemini-3.1-flash-image", generated.Model);
        Assert.Equal("interactions", generated.Endpoint);

        // Gemini is sent a ratio and a resolution class, so that — not a pixel size it never saw —
        // is what its receipt claims to have requested.
        Assert.Equal("3:2@2K", generated.RequestedSize);
        Assert.Equal("n/a", generated.RequestedQuality);
        Assert.Equal(1536, generated.ReturnedWidthPx);
        Assert.Equal(1024, generated.ReturnedHeightPx);
        Assert.Equal("png", generated.OutputFormat);
    }

    // ---- harness ---------------------------------------------------------

    private static CompositeBookPipeline Pipeline(
        IStoryModelClient storyClient, IOpenAiService images, BekiOptions? beki = null) =>
        new(storyClient,
            images,
            new StubMasterStoryService(),
            Options.Create(beki ?? new BekiOptions
            {
                CompositePipelineEnabled = true,
                SpreadConcurrency = 1,
            }),
            Options.Create(new BekiPrintLayoutOptions()),
            NullLogger<CompositeBookPipeline>.Instance);

    private static GeminiIllustrationClient GeminiClient(byte[] jpeg)
    {
        var options = new GeminiOptions { ApiKey = "test-key", BaseUrl = "https://gemini.test/v1beta" };

        return new GeminiIllustrationClient(
            new GeminiInteractionsClient(
                new StubHttpClientFactory(new ImageHandler(jpeg)),
                Options.Create(options),
                NullLogger<GeminiInteractionsClient>.Instance),
            new PassThroughNormalizer(),
            Options.Create(options),
            Options.Create(new OpenAiOptions { LogPrompts = false, EnableStoryImages = true }),
            NullLogger<GeminiIllustrationClient>.Instance);
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

    /// <summary>A real JPEG at a known size: the Gemini client decodes what comes back.</summary>
    private static byte[] Jpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(120, 80, 40, 255));
        using var buffer = new MemoryStream();
        image.Save(buffer, new JpegEncoder());
        return buffer.ToArray();
    }

    /// <summary>
    /// An image provider that describes itself, which the shared stub deliberately does not.
    ///
    /// Both are needed. The shared one proves the default interface member keeps every existing
    /// double compiling and reporting honestly; this one proves the real values survive the whole
    /// journey from the provider to the artifact the fulfilment layer stores. Reviews are
    /// delegated to the shared stub rather than reimplemented — the QA conversation is not what
    /// this file is about, and a second copy of it would be a second thing to keep in step.
    /// </summary>
    private sealed class ProvenanceImageService : IOpenAiService
    {
        private readonly StubImageService _reviews = new();

        public required Func<int, byte[]> Draw { get; init; }

        public string Provider { get; init; } = "openai";
        public string Model { get; init; } = "gpt-image-2";
        public string Endpoint { get; init; } = "images/edits";
        public string? ResponseSize { get; init; }
        public string? ResponseQuality { get; init; }
        public string? OutputFormat { get; init; }

        public List<string?> Sizes { get; } = [];
        public List<string?> Qualities { get; } = [];

        public async Task<byte[]> GenerateStoryImageAsync(
            string imagePrompt, StoryImageReference? reference,
            CancellationToken cancellationToken, string? imageSize = null,
            bool requireReferences = false, string? imageQuality = null) =>
            (await GenerateStoryImageWithProvenanceAsync(
                imagePrompt, reference, cancellationToken, imageSize, requireReferences,
                imageQuality)).Png;

        public Task<GeneratedStoryImage> GenerateStoryImageWithProvenanceAsync(
            string imagePrompt, StoryImageReference? reference,
            CancellationToken cancellationToken, string? imageSize = null,
            bool requireReferences = false, string? imageQuality = null)
        {
            Sizes.Add(imageSize);
            Qualities.Add(imageQuality);

            var png = Draw(Sizes.Count);
            var (width, height) = GeneratedStoryImage.MeasurePixels(png);

            return Task.FromResult(new GeneratedStoryImage(
                png, Provider, Model, Endpoint,
                imageSize ?? "default", imageQuality ?? "default",
                width, height, ResponseSize, ResponseQuality, OutputFormat));
        }

        public Task<string> ReviewIllustrationAsync(
            byte[] imageBytes, string reviewPrompt,
            IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
            CancellationToken cancellationToken) =>
            _reviews.ReviewIllustrationAsync(
                imageBytes, reviewPrompt, references, cancellationToken);

        public Task<AdventureContentDto> GenerateAdventureContentAsync(
            AdventureGenerationInput input, Guid adventureId, CancellationToken cancellationToken) =>
            Task.FromResult(new AdventureContentDto());

        public Task<string> DescribeCharacterFromPhotoAsync(
            byte[] imageBytes, string contentType, string promptText,
            CancellationToken cancellationToken) =>
            Task.FromResult("a child");

        public Task<string> CompleteTextAsync(
            string promptText, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
    }

    private sealed class ImageHandler(byte[] jpeg) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
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
                            new
                            {
                                type = "image",
                                mime_type = "image/jpeg",
                                data = Convert.ToBase64String(jpeg),
                            },
                        },
                    },
                },
                usage = new { total_input_tokens = 1, total_output_tokens = 1 },
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class PassThroughNormalizer : IReferenceImageNormalizer
    {
        public NormalizedReferenceImage NormalizeForOpenAi(byte[] bytes, string? hintContentType = null) =>
            new(bytes, hintContentType ?? "image/png", "reference.png");

        public NormalizedReferenceImage NormalizeForStorageWebp(byte[] bytes, string? hintContentType = null) =>
            new(bytes, "image/webp", "illustration.webp");

        public NormalizedReferenceImage NormalizeForDisplayWebp(byte[] bytes, string? hintContentType = null) =>
            new(bytes, "image/webp", "portrait.webp");

        public NormalizedReferenceImage NormalizeForPortraitStorage(byte[] bytes, string? hintContentType = null) =>
            new(bytes, "image/webp", "portrait.webp");
    }
}
