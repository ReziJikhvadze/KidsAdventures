using AdventurePacks.Api.Domain.Beki;

namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IBekiVisualRepository
{
    /// <summary>Stores a new identity spec, versioned per character rather than overwritten.</summary>
    Task<Guid> SaveIdentityAsync(BekiIdentityRecord record, CancellationToken cancellationToken);

    Task<BekiIdentityRecord?> GetLatestIdentityAsync(Guid characterId, CancellationToken cancellationToken);

    Task<Guid> SaveVisualBibleAsync(BekiVisualBibleRecord record, CancellationToken cancellationToken);

    Task<BekiVisualBibleRecord?> GetVisualBibleAsync(Guid storyId, CancellationToken cancellationToken);

    /// <summary>
    /// Claims an asset slot, returning its id, or null when the slot already exists. This
    /// is the idempotency point that stops a retried job paying to draw a page twice.
    /// </summary>
    Task<Guid?> TryClaimAssetAsync(Guid storyId, string assetType, int? pageNumber, CancellationToken cancellationToken);

    Task CompleteAssetAsync(BekiVisualAssetRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<BekiVisualAssetRecord>> GetAssetsAsync(Guid storyId, CancellationToken cancellationToken);

    Task<BekiVisualAssetRecord?> GetAssetAsync(
        Guid storyId,
        string assetType,
        int? pageNumber,
        CancellationToken cancellationToken);
}
