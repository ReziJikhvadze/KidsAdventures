using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IAdventureGenerationService
{
    /// <summary>Free, no-login teaser: writes the full story text + a cover image. Runs inline, persists nothing.</summary>
    Task<GuestPreviewResult> GenerateGuestPreviewAsync(GuestPreviewInput input, CancellationToken cancellationToken);

    /// <summary>
    /// Starts illustrating a book that has already been paid for. There is no credit to
    /// spend: the order that created the book is what authorises this.
    /// </summary>
    Task QueueIllustrationAsync(Guid userId, Guid packId, CancellationToken cancellationToken);

    Task QueuePdfGenerationAsync(Guid userId, Guid packId, CancellationToken cancellationToken);
    Task ProcessStoryGenerationAsync(Guid adventurePackId, CancellationToken cancellationToken);
    Task EnsurePreviewIllustrationQueuedAsync(Guid adventurePackId, CancellationToken cancellationToken);
    Task ProcessPreviewIllustrationAsync(Guid adventurePackId, CancellationToken cancellationToken);

    /// <summary>Paints the first page for free (the one-time welcome perk); charges no credit.</summary>
    Task ProcessFreeSampleIllustrationAsync(Guid adventurePackId, CancellationToken cancellationToken);
    Task ProcessPdfGenerationAsync(Guid adventurePackId, CancellationToken cancellationToken);
}
