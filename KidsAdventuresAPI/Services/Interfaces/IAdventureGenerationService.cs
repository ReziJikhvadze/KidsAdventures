using AdventurePacks.Api.DTOs.AdventurePacks;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IAdventureGenerationService
{
    Task<Guid> QueueGenerationAsync(Guid userId, GenerateAdventurePackRequest request, CancellationToken cancellationToken);

    /// <summary>Consumes one $4.99 book credit and starts illustrating an existing, text-ready pack.</summary>
    Task QueueIllustrationAsync(Guid userId, Guid packId, CancellationToken cancellationToken);

    Task QueuePdfGenerationAsync(Guid userId, Guid packId, CancellationToken cancellationToken);
    Task ProcessStoryGenerationAsync(Guid adventurePackId, CancellationToken cancellationToken);
    Task EnsurePreviewIllustrationQueuedAsync(Guid adventurePackId, CancellationToken cancellationToken);
    Task ProcessPreviewIllustrationAsync(Guid adventurePackId, CancellationToken cancellationToken);
    Task ProcessPdfGenerationAsync(Guid adventurePackId, CancellationToken cancellationToken);
}
