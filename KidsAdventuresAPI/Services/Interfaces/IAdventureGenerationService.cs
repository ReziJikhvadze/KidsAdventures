using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IAdventureGenerationService
{
    Task<Guid> QueueGenerationAsync(Guid userId, GenerateAdventurePackRequest request, CancellationToken cancellationToken);

    /// <summary>Free, no-login teaser: writes the full story text + a cover image. Runs inline, persists nothing.</summary>
    Task<GuestPreviewResult> GenerateGuestPreviewAsync(GuestPreviewInput input, CancellationToken cancellationToken);

    /// <summary>Saves a teaser story (generated while logged out) to the now-signed-in parent as a text-ready pack.</summary>
    Task<Guid> ImportGuestStoryAsync(Guid userId, ImportGuestStoryRequest request, CancellationToken cancellationToken);

    /// <summary>Consumes one $4.99 book credit and starts illustrating an existing, text-ready pack.</summary>
    Task QueueIllustrationAsync(Guid userId, Guid packId, CancellationToken cancellationToken);

    Task QueuePdfGenerationAsync(Guid userId, Guid packId, CancellationToken cancellationToken);
    Task ProcessStoryGenerationAsync(Guid adventurePackId, CancellationToken cancellationToken);
    Task EnsurePreviewIllustrationQueuedAsync(Guid adventurePackId, CancellationToken cancellationToken);
    Task ProcessPreviewIllustrationAsync(Guid adventurePackId, CancellationToken cancellationToken);

    /// <summary>Paints the first page for free (the one-time welcome perk); charges no credit.</summary>
    Task ProcessFreeSampleIllustrationAsync(Guid adventurePackId, CancellationToken cancellationToken);
    Task ProcessPdfGenerationAsync(Guid adventurePackId, CancellationToken cancellationToken);

    /// <summary>
    /// One-time Pixar-style "traveler" portrait for the child (Story Path map avatar), generated from
    /// this pack's story text alone. No-op if the child already has one or none is needed.
    /// </summary>
    Task ProcessHeroPortraitAsync(Guid adventurePackId, CancellationToken cancellationToken);
}
