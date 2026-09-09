using Microsoft.Extensions.Options;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Story.Composite;

namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// Whether the frame this book will ask the image provider for is one it can actually print.
///
/// It exists because the request was previously unvalidated and unrecorded at once: a size typed
/// into configuration went straight onto the wire, and a size the model refuses came back as a 400
/// in the middle of a paid book — nine or ten pictures in, after the story had been written. The
/// cost of finding out at boot is nothing; the cost of finding out at spread six is an order.
///
/// The three rules are separate on purpose, because they fail for different reasons:
///
/// - the SHAPE rules are this product's own and apply whichever vendor draws. A frame the book
///   cannot crop to its printed ratio without throwing half of it away is not a frame, and "auto"
///   is not a frame at all — the crop needs a known one before a pixel exists.
/// - the ALLOWLIST is an application rule, not the API's. OpenAI's gpt-image-2 accepts more sizes
///   than appear here; what appears here is what this book has a reason to buy, which keeps a
///   typo from quietly tripling the image bill of every order.
/// - the EXPERIMENTAL gate is OpenAI's own: outputs above
///   <see cref="ExperimentalPixelCount"/> pixels are documented as experimental, so this build
///   will send one only when somebody has said so in configuration.
///
/// A pure function returning a message rather than throwing, so the options pipeline can wire it
/// as one <c>.Validate(...)</c> line and the suite can ask it questions directly.
/// </summary>
public static class BekiImageRequestValidation
{
    /// <summary>The configuration keys, spelled once so a message can name the thing to change.</summary>
    public const string SpreadSizeKey = "Beki:SpreadImageSize";

    /// <inheritdoc cref="SpreadSizeKey"/>
    public const string CoverWrapSizeKey = "Beki:CoverWrapImageSize";

    /// <inheritdoc cref="SpreadSizeKey"/>
    public const string AllowExperimentalKey = "Beki:AllowExperimentalImageSizes";

    /// <summary>
    /// OpenAI's own line between a documented output and an experimental one: 3,686,400 pixels,
    /// which 2048×1152 sits comfortably under and 3840×2160 (8,294,400) is more than twice.
    /// </summary>
    public const int ExperimentalPixelCount = 3_686_400;

    /// <summary>
    /// The landscape frames this book accepts from the gpt-image-2 family.
    ///
    /// Two, and the second is the recommendation: 1536×1024 is today's proven request — every
    /// stored book was drawn at it — and 2048×1152 is the largest non-experimental landscape the
    /// family offers, so it is the one opt-in worth validating on a real book. It is also a better
    /// SHAPE for this product: 16:9 loses 17% of its height to the 15:7 crop where 3:2 loses 30%.
    /// </summary>
    public static readonly IReadOnlyList<string> GptImage2LandscapeSizes = ["1536x1024", "2048x1152"];

    /// <summary>
    /// The one experimental frame this book will accept, and only with
    /// <see cref="AllowExperimentalKey"/> set. 4K is a real per-picture price and latency rise —
    /// <c>OpenAI:ImageTimeoutMinutes</c> should be raised with it — and OpenAI reserves the right
    /// to change what an experimental output does.
    /// </summary>
    public static readonly IReadOnlyList<string> GptImage2ExperimentalLandscapeSizes = ["3840x2160"];

    /// <summary>
    /// The gpt-image-1 family's landscape frame. One, because the family offers exactly
    /// 1024×1024, 1536×1024 and 1024×1536 and only the middle one is landscape. This list is what
    /// catches the configuration that asks for 2048×1152 while
    /// <see cref="OpenAiService.ResolveImageEditModel"/> is going to send
    /// <see cref="OpenAiService.FallbackImageModel"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> GptImage1LandscapeSizes = ["1536x1024"];

    /// <summary>
    /// Null when both configured frames are ones this book can ask for and print; otherwise one
    /// message naming the key, the value, and the provider and model the value was judged against.
    /// </summary>
    public static string? Validate(
        BekiOptions beki, OpenAiOptions openAi, AiProviderOptions providers)
    {
        ArgumentNullException.ThrowIfNull(beki);
        ArgumentNullException.ThrowIfNull(openAi);
        ArgumentNullException.ThrowIfNull(providers);

        /*
          The model the request will ACTUALLY carry, resolved by the same code that builds it.

          The edit route rather than images/generations, and that is not a simplification: the
          composite pipeline attaches references to every picture it buys and passes
          requireReferences, so the edit route is the only one a book ever reaches. Validating the
          other one would be validating a path this product does not take.
        */
        var model = OpenAiService.ResolveImageEditModel(openAi);
        var openAiDraws = !providers.UsesGeminiForImages;

        return Check(
                   SpreadSizeKey, beki.SpreadImageSize,
                   CompositeDeterministicChecks.TargetAspect, "15:7",
                   beki, model, openAiDraws)
               ?? Check(
                   CoverWrapSizeKey, beki.CoverWrapImageSize,
                   BekiCoverDieline.AspectRatio, "512:245",
                   beki, model, openAiDraws);
    }

    private static string? Check(
        string key,
        string? value,
        double targetAspect,
        string targetName,
        BekiOptions beki,
        string model,
        bool openAiDraws)
    {
        var where = openAiDraws
            ? $"provider {AiProvider.OpenAi}, model {model}"
            : $"provider {AiProvider.Gemini}, which is sent this frame's aspect ratio and its own "
              + "configured resolution class rather than these pixels";

        if (!TryParse(value, out var width, out var height))
        {
            return $"{key} is \"{value}\", which is not a WxH pixel size such as 1536x1024. "
                   + "The book crops a known frame to its printed ratio, so \"auto\" and anything "
                   + $"else the provider decides for itself is refused ({where}).";
        }

        if (width <= height)
        {
            return $"{key} is {width}x{height}, which is not landscape. Both of this book's frames "
                   + $"are wider than they are tall - the spread prints at 15:7 and the cover wrap "
                   + $"at 512:245 - so a square or portrait request would be cropped to a sliver "
                   + $"({where}).";
        }

        var crop = CropFraction(width, height, targetAspect);
        if (crop > CompositeDeterministicChecks.MaxNormalizationCropFraction)
        {
            return $"{key} is {width}x{height}; cropping it to {targetName} would discard "
                   + $"{crop:P0} of one dimension, past the "
                   + $"{CompositeDeterministicChecks.MaxNormalizationCropFraction:P0} this pipeline "
                   + $"allows. The printed page would be assembled from part of a picture ({where}).";
        }

        // Gemini is sent a ratio and a resolution class, never these pixels
        // (GeminiIllustrationClient.AspectRatioFor), so an OpenAI model's size list has no
        // authority over it. The shape rules above still do: they are about the book.
        if (!openAiDraws)
        {
            return null;
        }

        var normalized = $"{width}x{height}";
        var family = OpenAiService.IsGptImage2Family(model);
        var accepted = family ? GptImage2LandscapeSizes : GptImage1LandscapeSizes;

        if ((family && key == CoverWrapSizeKey && normalized is "1200x576" or "1024x672")
            || accepted.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (family && GptImage2ExperimentalLandscapeSizes.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return beki.AllowExperimentalImageSizes
                ? null
                : $"{key} is {normalized}, which is above OpenAI's {ExperimentalPixelCount:N0} px "
                  + $"experimental threshold ({where}). Set {AllowExperimentalKey} to true to opt "
                  + "in deliberately, and raise OpenAI:ImageTimeoutMinutes with it.";
        }

        var suggestions = family
            ? string.Join(", ", GptImage2LandscapeSizes.Concat(
                [$"{GptImage2ExperimentalLandscapeSizes[0]} with {AllowExperimentalKey}"]))
            : string.Join(", ", GptImage1LandscapeSizes);

        return $"{key} is {normalized}, which is not a frame this book accepts from {model} "
               + $"({where}). Accepted: {suggestions}.";
    }

    /// <summary>
    /// <c>WxH</c> in positive whole pixels, spelled exactly the way the API receives it, or false.
    ///
    /// Strict on purpose, and stricter than a parser needs to be: the accepted string is later sent
    /// verbatim as the request's <c>size</c>, so a value this method waves through with whitespace
    /// or a leading zero would pass startup and then fail the first paid picture. "1536x1024"
    /// parses; "1536 x 1024 ", "01536x1024", "auto" and "1536x0" do not.
    /// </summary>
    public static bool TryParse(string? value, out int width, out int height)
    {
        width = 0;
        height = 0;

        var parts = value?.Split('x');

        return parts is { Length: 2 }
               && int.TryParse(parts[0], System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out width)
               && int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out height)
               && width > 0
               && height > 0
               && string.Equals(value, $"{width}x{height}", StringComparison.Ordinal);
    }

    /// <summary>
    /// The fraction of the over-long dimension a centred crop to <paramref name="targetAspect"/>
    /// removes.
    ///
    /// The same arithmetic as
    /// <see cref="CompositeDeterministicChecks.NormalizationCropFraction"/>, which is fixed to the
    /// spread's 15:7 — this one takes the ratio because the cover wrap is 512:245 and a validator
    /// that judged the wrap by the spread's shape would pass a frame the wrap cannot use. The two
    /// are pinned to each other by a test rather than by a shared call, so neither file has to
    /// depend on the other's shape.
    /// </summary>
    public static double CropFraction(int width, int height, double targetAspect)
    {
        if (width <= 0 || height <= 0 || targetAspect <= 0)
        {
            return 1;
        }

        var aspect = (double)width / height;

        return aspect >= targetAspect
            // Too wide: the sides go.
            ? 1 - (height * targetAspect / width)
            // Too tall: the top and bottom go.
            : 1 - (width / targetAspect / height);
    }
}

/// <summary>
/// The startup seam for <see cref="BekiImageRequestValidation"/>: an <see cref="IValidateOptions{TOptions}"/>
/// so the message can name the offending key and the model it was judged against, which a static
/// <c>.Validate(predicate, message)</c> cannot. Registered beside the Beki options so
/// <c>ValidateOnStart</c> runs it at boot — a frame the configured model cannot draw is a deployment
/// that refuses to come up, not a book that fails at its first picture.
/// </summary>
public sealed class BekiImageRequestOptionsValidator(
    IOptions<OpenAiOptions> openAi,
    IOptions<AiProviderOptions> providers) : IValidateOptions<BekiOptions>
{
    public ValidateOptionsResult Validate(string? name, BekiOptions options)
    {
        var error = BekiImageRequestValidation.Validate(options, openAi.Value, providers.Value);
        return error is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(error);
    }
}
