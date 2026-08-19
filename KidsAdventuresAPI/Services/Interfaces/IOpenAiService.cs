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
    Task<byte[]> GenerateStoryImageAsync(
        string imagePrompt,
        StoryImageReference? reference,
        CancellationToken cancellationToken,
        string? imageSize = null);

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
