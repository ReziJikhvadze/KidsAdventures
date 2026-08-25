using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Ai;

/// <summary>
/// Sends the three picture calls to whichever vendor the configuration named, and everything
/// else to OpenAI.
///
/// A decorator rather than a second implementation of the whole interface, because only three of
/// these methods are about images. The others are the legacy A5 text path and a utility
/// completion, and a class that reimplemented them for Gemini would be claiming a swap nobody
/// asked for and nobody had tested.
///
/// It exists at all so that no caller has to know: <see cref="IOpenAiService"/> is injected in
/// four places, and the alternative was four sites each asking which provider is on. The name is
/// now a lie in one direction — this may not be OpenAI — but a truthful rename would touch every
/// one of those call sites for nothing, and the lie is confined to a type name that this class
/// exists to explain.
/// </summary>
public sealed class AiServiceRouter(
    IOpenAiService openAi,
    IIllustrationClient illustrations,
    ILogger<AiServiceRouter> logger) : IOpenAiService
{
    public Task<byte[]> GenerateStoryImageAsync(
        string imagePrompt,
        StoryImageReference? reference,
        CancellationToken cancellationToken,
        string? imageSize = null) =>
        illustrations.GenerateStoryImageAsync(imagePrompt, reference, cancellationToken, imageSize);

    public Task<string> ReviewIllustrationAsync(
        byte[] imageBytes,
        string reviewPrompt,
        IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
        CancellationToken cancellationToken) =>
        illustrations.ReviewIllustrationAsync(imageBytes, reviewPrompt, references, cancellationToken);

    public Task<string> DescribeCharacterFromPhotoAsync(
        byte[] imageBytes,
        string contentType,
        string promptText,
        CancellationToken cancellationToken) =>
        illustrations.DescribeCharacterFromPhotoAsync(
            imageBytes, contentType, promptText, cancellationToken);

    /// <summary>
    /// The old A5 flow, which writes its story and draws its pictures in one call to OpenAI.
    ///
    /// Not routed. Splitting it would mean unpicking that call into two vendors' halves, and it
    /// is the path this product is leaving rather than the one it is tuning. Logged once at
    /// warning level so that a book which came out looking like the old vendor's work has an
    /// entry explaining why, instead of an afternoon spent doubting the switch.
    /// </summary>
    public Task<AdventureContentDto> GenerateAdventureContentAsync(
        AdventureGenerationInput input,
        Guid adventureId,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Adventure {AdventureId} is being generated through the legacy A5 flow, which always "
            + "uses OpenAI — the image provider setting does not apply to it.",
            adventureId);

        return openAi.GenerateAdventureContentAsync(input, adventureId, cancellationToken);
    }

    /// <summary>Short text side-jobs — series memory, and the like. No picture, nothing to route.</summary>
    public Task<string> CompleteTextAsync(string promptText, CancellationToken cancellationToken) =>
        openAi.CompleteTextAsync(promptText, cancellationToken);
}
