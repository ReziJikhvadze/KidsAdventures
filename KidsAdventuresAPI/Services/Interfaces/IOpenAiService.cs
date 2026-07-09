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

    /// <summary>Locate a subject in an illustration; returns normalized 0–100 bbox or null.</summary>
    Task<HotspotRegionDto?> LocateRegionInIllustrationAsync(
        byte[] imageBytes,
        string contentType,
        string subjectDescription,
        CancellationToken cancellationToken);
}
