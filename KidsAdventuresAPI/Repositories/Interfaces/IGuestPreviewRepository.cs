namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IGuestPreviewRepository
{
    Task CreateAsync(GuestPreview preview, CancellationToken cancellationToken);

    Task<GuestPreview?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<GuestPreview?> GetByStoryIdAsync(Guid storyId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically flips <c>Redeemed</c> from 0 → 1. Returns <c>true</c> only for the single caller that wins the
    /// race, so the welcome gift can be granted exactly once per preview even under concurrent sign-ups.
    /// </summary>
    Task<bool> TryRedeemAsync(Guid id, Guid userId, CancellationToken cancellationToken);
}
