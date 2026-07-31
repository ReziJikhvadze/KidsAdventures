using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IOpenAiService
{
    Task<AdventureContentDto> GenerateAdventureContentAsync(
        AdventureGenerationInput input,
        Guid adventureId,
        CancellationToken cancellationToken);

    Task<byte[]> GenerateStoryImageAsync(
        string imagePrompt,
        StoryImageReference? reference,
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
