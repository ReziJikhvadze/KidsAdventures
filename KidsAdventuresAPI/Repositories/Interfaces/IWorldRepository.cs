namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IWorldRepository
{
    /// <summary>The world catalogue in map order.</summary>
    Task<IReadOnlyList<World>> GetActiveAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string worldId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserWorldProgress>> GetProgressAsync(Guid characterId, CancellationToken cancellationToken);

    /// <summary>Progress for several characters at once, for the dashboard's multi-child view.</summary>
    Task<IReadOnlyList<UserWorldProgress>> GetProgressForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a world to Unlocked unless it is already Completed. Idempotent, so the
    /// journey can call it every time a book is started without checking first.
    /// </summary>
    Task UnlockAsync(Guid userId, Guid characterId, string worldId, CancellationToken cancellationToken);

    /// <summary>Records the paid book that finished a world. Idempotent per (character, world).</summary>
    Task CompleteAsync(Guid userId, Guid characterId, string worldId, Guid bookId, CancellationToken cancellationToken);
}
