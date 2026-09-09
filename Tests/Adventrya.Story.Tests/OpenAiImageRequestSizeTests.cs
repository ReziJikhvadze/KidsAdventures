using System.Net;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// What frame and quality actually leave this machine, and what the receipt says came back.
///
/// Two things had no test and one of them had no answer at all. The multipart body's <c>size</c>
/// and <c>quality</c> fields decide what every picture in every book costs and how sharp the
/// printed sheet can be, and nothing asserted them; and the model, route, size, quality and
/// returned pixels of a paid call were recorded nowhere, so a book could not say what drew it.
///
/// The startup validation is the other half. A size the model refuses is a 400 in the middle of a
/// paid book — after the story, nine or ten pictures in — and the rules are worth stating as tests
/// because they are not all the same rule: the shape rules are this product's (a frame it cannot
/// crop to its printed ratio is not a frame), the allowlist is this application's (what the book
/// has a reason to buy, which is narrower than what the API supports), and the experimental gate
/// is OpenAI's.
///
/// Stubbed HTTP throughout, in the same shape <see cref="OpenAiImageRetryTests"/> uses. No live
/// call is ever made and none may be.
/// </summary>
public class OpenAiImageRequestSizeTests
{
    // -- the wire ----------------------------------------------------------

    /// <summary>
    /// A page under the shipped defaults: 1536×1024 at medium, on the edit route, to gpt-image-2.
    ///
    /// The values are the point. They are what every stored book was drawn at and what the
    /// accepted physical proof was made from, so a change to any of them is a change to the
    /// product and has to be somebody's decision rather than a side effect.
    /// </summary>
    [Fact]
    public async Task A_page_under_the_defaults_asks_for_the_proven_frame_at_medium()
    {
        var handler = new CapturingHandler(() => Picture(Png(1536, 1024)));
        var service = Service(handler);
        var beki = new BekiOptions();

        await service.GenerateStoryImageAsync(
            "draw", HeroPhoto(), CancellationToken.None,
            beki.SpreadImageSize, requireReferences: true, imageQuality: beki.PageImageQuality);

        Assert.Equal("images/edits", handler.Path);
        Assert.Equal("1536x1024", handler.Field("size"));
        Assert.Equal("medium", handler.Field("quality"));
        Assert.Equal("gpt-image-2", handler.Field("model"));
    }

    /// <summary>The caller's own frame and quality, when it passes them — the opt-in path.</summary>
    [Fact]
    public async Task An_opted_in_larger_frame_is_what_goes_on_the_wire()
    {
        var handler = new CapturingHandler(() => Picture(Png(2048, 1152)));
        var service = Service(handler);

        await service.GenerateStoryImageAsync(
            "draw", HeroPhoto(), CancellationToken.None,
            "2048x1152", requireReferences: true, imageQuality: "high");

        Assert.Equal("2048x1152", handler.Field("size"));
        Assert.Equal("high", handler.Field("quality"));
    }

    /// <summary>
    /// The receipt: the model actually sent, the route it went to, the frame and quality asked
    /// for, the pixels that came back, and the provider's own echo of what it did.
    ///
    /// The returned pixels are measured from the bytes rather than copied from the request, which
    /// is the whole value of the record: a provider that answered at a different frame than the
    /// one asked for is invisible unless both numbers are written down.
    /// </summary>
    [Fact]
    public async Task The_receipt_names_the_model_the_route_and_the_pixels_that_came_back()
    {
        var handler = new CapturingHandler(() => Picture(
            Png(2048, 1152),
            echoedSize: "2048x1152",
            echoedQuality: "high",
            outputFormat: "png"));

        var generated = await Service(handler).GenerateStoryImageWithProvenanceAsync(
            "draw", HeroPhoto(), CancellationToken.None,
            "2048x1152", requireReferences: true, imageQuality: "high");

        Assert.Equal("openai", generated.Provider);
        Assert.Equal("gpt-image-2", generated.Model);
        Assert.Equal("images/edits", generated.Endpoint);
        Assert.Equal("2048x1152", generated.RequestedSize);
        Assert.Equal("high", generated.RequestedQuality);
        Assert.Equal(2048, generated.ReturnedWidthPx);
        Assert.Equal(1152, generated.ReturnedHeightPx);
        Assert.Equal("2048x1152", generated.ResponseSize);
        Assert.Equal("high", generated.ResponseQuality);
        Assert.Equal("png", generated.OutputFormat);
    }

    /// <summary>
    /// A provider that says nothing about how it drew leaves nulls, not guesses. "It did not say"
    /// and "it said this" are different facts and a receipt that conflated them would be worthless
    /// exactly when it is being read to settle a dispute.
    /// </summary>
    [Fact]
    public async Task An_unecho_ing_response_leaves_the_provider_s_own_fields_null()
    {
        var generated = await Service(new CapturingHandler(() => Picture(Png(1536, 1024))))
            .GenerateStoryImageWithProvenanceAsync(
                "draw", HeroPhoto(), CancellationToken.None, "1536x1024", requireReferences: true);

        Assert.Null(generated.ResponseSize);
        Assert.Null(generated.ResponseQuality);
        Assert.Null(generated.OutputFormat);
        Assert.Equal(1536, generated.ReturnedWidthPx);
    }

    /// <summary>
    /// Neither configured model is a gpt-image model, so the edit route sends the mini fallback —
    /// and the receipt says so.
    ///
    /// This is the case the validator exists for: a configuration reading
    /// <c>ImageEditModel: "dall-e-3"</c> would have been validated against gpt-image-2's size list
    /// while sending a model that accepts one landscape frame.
    /// </summary>
    [Fact]
    public async Task An_unrecognised_model_falls_back_to_the_mini_and_the_receipt_records_it()
    {
        var options = Defaults();
        options.ImageEditModel = "dall-e-3";
        options.ImageModel = "dall-e-3";

        var handler = new CapturingHandler(() => Picture(Png(1536, 1024)));

        var generated = await Service(handler, options).GenerateStoryImageWithProvenanceAsync(
            "draw", HeroPhoto(), CancellationToken.None, "1536x1024", requireReferences: true);

        Assert.Equal("gpt-image-1-mini", handler.Field("model"));
        Assert.Equal("gpt-image-1-mini", generated.Model);
        Assert.Equal(OpenAiService.FallbackImageModel, OpenAiService.ResolveImageEditModel(options));
    }

    /// <summary>Snapshots are the family: gpt-image-2-2026-… gets gpt-image-2's rules.</summary>
    [Theory]
    [InlineData("gpt-image-2", true)]
    [InlineData("GPT-IMAGE-2", true)]
    [InlineData("gpt-image-2-2026-03-01", true)]
    [InlineData("gpt-image-1.5", false)]
    [InlineData("gpt-image-1-mini", false)]
    [InlineData("dall-e-3", false)]
    public void The_gpt_image_2_family_is_matched_by_prefix(string model, bool expected) =>
        Assert.Equal(expected, OpenAiService.IsGptImage2Family(model));

    // -- the validation ----------------------------------------------------

    [Fact]
    public void The_shipped_defaults_are_valid() =>
        Assert.Null(BekiImageRequestValidation.Validate(
            new BekiOptions(), Defaults(), new AiProviderOptions()));

    /// <summary>
    /// The recommended opt-in is accepted on gpt-image-2 and refused on the mini, naming both the
    /// key and the model it was judged against — which is the whole job of the message.
    /// </summary>
    [Fact]
    public void The_largest_non_experimental_landscape_is_valid_only_where_the_model_takes_it()
    {
        var beki = new BekiOptions { SpreadImageSize = "2048x1152", CoverWrapImageSize = "2048x1152" };

        Assert.Null(BekiImageRequestValidation.Validate(beki, Defaults(), new AiProviderOptions()));

        var mini = Defaults();
        mini.ImageEditModel = "gpt-image-1-mini";
        mini.ImageModel = "gpt-image-1-mini";

        var message = BekiImageRequestValidation.Validate(beki, mini, new AiProviderOptions());

        Assert.NotNull(message);
        Assert.Contains(BekiImageRequestValidation.SpreadSizeKey, message);
        Assert.Contains("gpt-image-1-mini", message);
        Assert.Contains("1536x1024", message);
    }

    /// <summary>
    /// 4K is above OpenAI's experimental threshold, so it needs the switch that says somebody
    /// meant it — and the refusal points at the switch rather than merely saying no.
    /// </summary>
    [Fact]
    public void An_experimental_frame_needs_the_opt_in_switch()
    {
        var beki = new BekiOptions { SpreadImageSize = "3840x2160", CoverWrapImageSize = "3840x2160" };

        var refused = BekiImageRequestValidation.Validate(beki, Defaults(), new AiProviderOptions());

        Assert.NotNull(refused);
        Assert.Contains(BekiImageRequestValidation.AllowExperimentalKey, refused);

        beki.AllowExperimentalImageSizes = true;

        Assert.Null(BekiImageRequestValidation.Validate(beki, Defaults(), new AiProviderOptions()));
    }

    /// <summary>
    /// Portrait, square and "auto" are all refused, and for the product's own reason rather than
    /// the API's: both printed frames are landscape, and the crop needs a known frame before a
    /// pixel exists.
    /// </summary>
    [Theory]
    [InlineData("1024x1536", "not landscape")]
    [InlineData("1024x1024", "not landscape")]
    [InlineData("auto", "not a WxH pixel size")]
    [InlineData("", "not a WxH pixel size")]
    public void A_frame_the_book_cannot_print_is_refused(string size, string because)
    {
        var message = BekiImageRequestValidation.Validate(
            new BekiOptions { SpreadImageSize = size }, Defaults(), new AiProviderOptions());

        Assert.NotNull(message);
        Assert.Contains(because, message);
        Assert.Contains(BekiImageRequestValidation.SpreadSizeKey, message);
    }

    /// <summary>The cover's own key is checked too, and named when it is the one at fault.</summary>
    [Fact]
    public void The_cover_wrap_frame_is_validated_under_its_own_key()
    {
        var message = BekiImageRequestValidation.Validate(
            new BekiOptions { CoverWrapImageSize = "1024x1536" },
            Defaults(),
            new AiProviderOptions());

        Assert.NotNull(message);
        Assert.Contains(BekiImageRequestValidation.CoverWrapSizeKey, message);
    }

    /// <summary>
    /// A frame so wide that cropping it to the printed ratio would throw half of it away is
    /// refused whichever vendor draws — the picture would be assembled from a strip of a picture.
    /// </summary>
    [Fact]
    public void A_frame_that_would_be_cropped_to_a_strip_is_refused()
    {
        var beki = new BekiOptions { SpreadImageSize = "6144x1024" };

        var message = BekiImageRequestValidation.Validate(beki, Defaults(), Gemini());

        Assert.NotNull(message);
        Assert.Contains("15:7", message);
        Assert.Contains(BekiImageRequestValidation.SpreadSizeKey, message);
    }

    /// <summary>
    /// With Gemini drawing, the OpenAI allowlist has no authority: Gemini is sent an aspect ratio
    /// and its own resolution class, never these pixels. The shape rules still apply, because they
    /// are about the book rather than about a vendor.
    /// </summary>
    [Fact]
    public void Gemini_takes_any_printable_landscape_frame()
    {
        var beki = new BekiOptions { SpreadImageSize = "2048x1152", CoverWrapImageSize = "2048x1152" };

        // The model that would be refused under OpenAI, to prove the allowlist is skipped rather
        // than accidentally satisfied.
        var mini = Defaults();
        mini.ImageEditModel = "gpt-image-1-mini";
        mini.ImageModel = "gpt-image-1-mini";

        Assert.Null(BekiImageRequestValidation.Validate(beki, mini, Gemini()));

        Assert.NotNull(BekiImageRequestValidation.Validate(
            new BekiOptions { SpreadImageSize = "1024x1536" }, mini, Gemini()));
    }

    /// <summary>
    /// The validator's crop arithmetic and the pipeline's are the same arithmetic.
    ///
    /// They are written twice — the pipeline's is fixed to 15:7, the validator's takes the ratio
    /// because the cover is 512:245 — so this is what stops the pair drifting into two different
    /// answers to "how much would this frame lose".
    /// </summary>
    [Theory]
    [InlineData(1536, 1024)]
    [InlineData(2048, 1152)]
    [InlineData(3840, 2160)]
    [InlineData(1024, 1536)]
    public void The_crop_rule_agrees_with_the_pipeline_s_own(int width, int height) =>
        Assert.Equal(
            CompositeDeterministicChecks.NormalizationCropFraction(width, height),
            BekiImageRequestValidation.CropFraction(
                width, height, CompositeDeterministicChecks.TargetAspect),
            6);

    /// <summary>
    /// The option default and the frozen constant are the same string. The constant stays because
    /// a legacy test pins the request to it; the option is what the code reads. If they part, one
    /// of the two paths starts drawing at a different frame than the other.
    /// </summary>
    [Fact]
    public void The_option_default_is_the_frame_the_constant_names()
    {
        Assert.Equal(
            AdventurePacks.Api.Services.Story.BekiBookGenerator.SpreadImageSize,
            new BekiOptions().SpreadImageSize);

        Assert.Equal("1200x576", new BekiOptions().CoverWrapImageSize);
        Assert.False(new BekiOptions().AllowExperimentalImageSizes);
        Assert.Equal("medium", new BekiOptions().PageImageQuality);
    }

    [Theory]
    [InlineData("gpt-image-2.5-flare")]
    [InlineData("gpt-image-2.5-flare-2026-09-08")]
    public async Task Flare_cover_request_sends_fast_size_and_reference_edit_parameters(string model)
    {
        var handler = new CapturingHandler(() => Picture(Png(1024, 672)));
        var options = Defaults();
        options.ImageModel = options.ImageEditModel = model;
        var beki = new BekiOptions();
        await Service(handler, options).GenerateStoryImageAsync("draw", HeroPhoto(), CancellationToken.None,
            beki.CoverWrapImageSize, requireReferences: true, imageQuality: beki.CoverImageQuality);
        Assert.Equal("images/edits", handler.Path);
        Assert.Equal(model, handler.Field("model"));
        Assert.Equal("1200x576", handler.Field("size"));
        Assert.Equal("medium", handler.Field("quality"));
        Assert.Null(handler.Field("input_fidelity"));
    }

    // ---- harness ---------------------------------------------------------

    private static OpenAiOptions Defaults() => new()
    {
        ApiKey = "test-key",
        BaseUrl = "https://openai.test/v1/",
        EnableStoryImages = true,
        ImageModel = "gpt-image-2",
        ImageEditModel = "gpt-image-2",
        ImageRetryAttempts = 1,
        LogPrompts = false,
    };

    private static AiProviderOptions Gemini() => new() { Images = AiProvider.Gemini };

    private static OpenAiService Service(CapturingHandler handler, OpenAiOptions? options = null) =>
        new(new SingleClientFactory(handler),
            new PassThroughNormalizer(),
            Options.Create(options ?? Defaults()),
            NullLogger<OpenAiService>.Instance);

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

    private static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(30, 90, 140, 255));
        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
        return buffer.ToArray();
    }

    private static HttpResponseMessage Picture(
        byte[] png,
        string? echoedSize = null,
        string? echoedQuality = null,
        string? outputFormat = null)
    {
        var item = new Dictionary<string, object?> { ["b64_json"] = Convert.ToBase64String(png) };
        var body = new Dictionary<string, object?> { ["data"] = new[] { item } };

        if (echoedSize is not null) body["size"] = echoedSize;
        if (echoedQuality is not null) body["quality"] = echoedQuality;
        if (outputFormat is not null) body["output_format"] = outputFormat;

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Answers with one scripted response and keeps the multipart fields it was sent.
    ///
    /// The form is read here rather than asserted through a mock's expectations because the
    /// question is what the PROVIDER would see: a field named wrongly, or added twice, or
    /// serialized as something other than a plain string, is a 400 on a paid book and would pass
    /// any assertion made against our own intent.
    /// </summary>
    private sealed class CapturingHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal);

        public string? Path { get; private set; }

        public string? Field(string name) => _fields.GetValueOrDefault(name);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath.TrimStart('/').Replace("v1/", string.Empty);

            if (request.Content is MultipartFormDataContent form)
            {
                foreach (var part in form)
                {
                    var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                    if (name is { Length: > 0 } && part is StringContent)
                    {
                        _fields[name] = await part.ReadAsStringAsync(cancellationToken);
                    }
                }
            }

            return respond();
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
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

        public NormalizedReferenceImage NormalizeForDisplayWebp(byte[] bytes, string? hintContentType = null) =>
            new(bytes, "image/webp", "portrait.webp");

        public NormalizedReferenceImage NormalizeForPortraitStorage(byte[] bytes, string? hintContentType = null) =>
            new(bytes, "image/webp", "portrait.webp");
    }
}
