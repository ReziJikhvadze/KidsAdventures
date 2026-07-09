namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IChildRepository
{
    Task<IReadOnlyList<Child>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<Child?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(Child child, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(Child child, CancellationToken cancellationToken);
    Task UpdateAppearanceCacheAsync(
        Guid id,
        Guid userId,
        string? appearanceDescription,
        string? appearancePhotoUrl,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    /// <summary>Atomically claims the right to generate this child's one-time hero portrait; false if already claimed/generated.</summary>
    Task<bool> TryClaimHeroPortraitGenerationAsync(Guid childId, CancellationToken cancellationToken);

    /// <summary>Releases a claim after a failed generation attempt so a later story can retry.</summary>
    Task ClearHeroPortraitClaimAsync(Guid childId, CancellationToken cancellationToken);

    Task SetHeroPortraitUrlAsync(Guid childId, string heroPortraitUrl, CancellationToken cancellationToken);
}
