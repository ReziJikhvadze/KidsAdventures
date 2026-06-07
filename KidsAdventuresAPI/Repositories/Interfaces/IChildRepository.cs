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
}
