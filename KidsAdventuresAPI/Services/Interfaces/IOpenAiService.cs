using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using SixLabors.ImageSharp;

namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>
/// One generated picture together with the request that actually produced it.
///
/// It exists because the book's evidence package could not answer the simplest question anyone
/// asks of a printed spread: what was this drawn at? The pipeline logged <c>Beki:ImageModel</c> —
/// a configuration value that the image routes do not necessarily send — and nothing anywhere
/// recorded the size or quality asked for, let alone the pixels that came back. So a book whose
/// bases measured 1536×717 could not be told apart from a book whose provider had quietly
/// answered at a smaller frame, and a size or quality change could not be shown to have taken
/// effect on a real order.
///
/// Every field is what happened rather than what was configured: <see cref="Model"/> is the model
/// name put in the request, <see cref="Endpoint"/> the route it went to, and the three
/// <c>Response*</c> fields are the provider's own echo of what it did, present only when it sent
/// them (gpt-image responses carry <c>size</c>, <c>quality</c> and <c>output_format</c>).
/// </summary>
/// <param name="ReturnedWidthPx">
/// Measured from the bytes, not read from the request — a provider that ignored the size asked
/// for is exactly the case this record has to be able to show. Zero when the bytes will not decode.
/// </param>
public sealed record GeneratedStoryImage(
    byte[] Png,
    string Provider,
    string Model,
    string Endpoint,
    string RequestedSize,
    string RequestedQuality,
    int ReturnedWidthPx,
    int ReturnedHeightPx,
    string? ResponseSize,
    string? ResponseQuality,
    string? OutputFormat)
{
    /// <summary>What a provenance-unaware implementation truthfully knows about itself.</summary>
    public const string Unknown = "unknown";

    /// <summary>The requested value when the caller passed none and the deployment's own applied.</summary>
    public const string Default = "default";

    /// <summary>
    /// The honest record for an implementation that only has the bytes: the picture is real and
    /// measured, everything else says so rather than guessing. Used by the interface default
    /// members, which is what lets a new member arrive without every existing double changing.
    /// </summary>
    public static GeneratedStoryImage Unattributed(
        byte[] png, string? requestedSize, string? requestedQuality)
    {
        var (width, height) = MeasurePixels(png);

        return new GeneratedStoryImage(
            png, Unknown, Unknown, Unknown,
            string.IsNullOrWhiteSpace(requestedSize) ? Default : requestedSize.Trim(),
            string.IsNullOrWhiteSpace(requestedQuality) ? Default : requestedQuality.Trim(),
            width, height, null, null, null);
    }

    /// <summary>
    /// The picture's own dimensions, or 0×0 when the bytes are not an image this build can read.
    ///
    /// Never throws: this is a receipt, and a receipt that takes down the book it is describing
    /// would be worse than a receipt with a gap in it. The undecodable case is already the
    /// pipeline's own deterministic check to refuse, with a far better message than a header read.
    /// </summary>
    public static (int Width, int Height) MeasurePixels(byte[]? png)
    {
        if (png is not { Length: > 0 })
        {
            return (0, 0);
        }

        try
        {
            var info = Image.Identify(png);
            return (info.Width, info.Height);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }
}

public interface IOpenAiService
{
    Task<GeneratedStoryImage> GeneratePreviewCoverImageAsync(string prompt, StoryImageReference reference,
        CancellationToken cancellationToken, string size, string quality) =>
        GenerateStoryImageWithProvenanceAsync(prompt, reference, cancellationToken, size, true, quality);

    Task<AdventureContentDto> GenerateAdventureContentAsync(
        AdventureGenerationInput input,
        Guid adventureId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Draws one illustration. <paramref name="imageSize"/> is for callers that need a shape other
    /// than the configured one — the Beki format's landscape spread — and every existing caller
    /// omits it, so the global setting stays the answer for every book in production.
    /// </summary>
    /// <param name="requireReferences">
    /// Whether a picture drawn without the attached references is acceptable.
    ///
    /// False, and false is what every existing caller gets: the OpenAI path retries the edit route
    /// and, when it still fails, draws from the prompt alone rather than returning nothing. For the
    /// A5 flow that is the right trade — a book with a slightly-off hero beats a failed job, and
    /// the warning in the log says the likeness may be lost.
    ///
    /// True refuses that trade. It exists for the composite pipeline, where the references are not
    /// an aid to the prompt but the substance of it: the child's likeness is *only* in the attached
    /// photograph — the composite plan carries no appearance description at all — the world comes
    /// only from the approved theme reference, and a recurring creature's design comes only from
    /// the continuity image. A silent fallback there does not produce a slightly-off picture; it
    /// produces a stranger in a generic world, which is then composited, reviewed and printed.
    ///
    /// With it set, a caller either gets a picture the references were actually sent for, or an
    /// exception. It never gets one and is told the other.
    /// </param>
    /// <param name="imageQuality">
    /// How hard the image model works on this one picture: <c>low</c>, <c>medium</c> or
    /// <c>high</c> in gpt-image's vocabulary, or null for the configured default
    /// (<c>OpenAI:ImageQuality</c>).
    ///
    /// Per call rather than per deployment because the pictures in a book are not worth the same
    /// money: the hero anchor and the cover set the standard every other page is matched against,
    /// and <c>BekiOptions</c> has carried separate qualities for them since the format was
    /// designed — settings that, until this parameter existed, nothing read. A provider with no
    /// per-call notion of quality ignores it.
    /// </param>
    Task<byte[]> GenerateStoryImageAsync(
        string imagePrompt,
        StoryImageReference? reference,
        CancellationToken cancellationToken,
        string? imageSize = null,
        bool requireReferences = false,
        string? imageQuality = null);

    /// <summary>
    /// The same call, answering with what was actually sent and what actually came back.
    ///
    /// A default implementation rather than a new obligation, and deliberately so: every caller
    /// that only wants the picture keeps the method above, and every existing implementation —
    /// the test doubles most of all — compiles unchanged and reports itself honestly as
    /// <see cref="GeneratedStoryImage.Unknown"/> instead of inventing a provider it never called.
    /// The real implementations override it; see <c>OpenAiService</c> and
    /// <c>GeminiIllustrationClient</c>.
    /// </summary>
    async Task<GeneratedStoryImage> GenerateStoryImageWithProvenanceAsync(
        string imagePrompt,
        StoryImageReference? reference,
        CancellationToken cancellationToken,
        string? imageSize = null,
        bool requireReferences = false,
        string? imageQuality = null) =>
        GeneratedStoryImage.Unattributed(
            await GenerateStoryImageAsync(
                imagePrompt, reference, cancellationToken, imageSize, requireReferences, imageQuality),
            imageSize,
            imageQuality);

    /// <summary>
    /// Looks at a finished illustration and says whether it is usable. Returns the model's raw
    /// JSON verdict; the caller decides what a failure is worth.
    /// </summary>
    Task<string> ReviewIllustrationAsync(
        byte[] imageBytes,
        string reviewPrompt,
        IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
        CancellationToken cancellationToken);

    Task<string> DescribeCharacterFromPhotoAsync(
        byte[] imageBytes,
        string contentType,
        string promptText,
        CancellationToken cancellationToken);

    /// <summary>
    /// Plain text-in, text-out against the cheap text model. Used for short side-jobs such as
    /// distilling a finished book into series memory, where no images are involved.
    /// </summary>
    Task<string> CompleteTextAsync(string promptText, CancellationToken cancellationToken);
}
