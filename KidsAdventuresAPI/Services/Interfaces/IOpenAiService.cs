using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IOpenAiService
{
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
