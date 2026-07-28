using AdventurePacks.Api.DTOs.Worlds;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IWorldProgressService
{
    Task<IReadOnlyList<WorldResponse>> GetCatalogueAsync(CancellationToken cancellationToken);

    /// <summary>The adventure map for one hero.</summary>
    Task<AdventureMapResponse> GetMapAsync(Guid userId, Guid characterId, CancellationToken cancellationToken);

    /// <summary>One map per hero, for the parent dashboard.</summary>
    Task<IReadOnlyList<AdventureMapResponse>> GetMapsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Throws unless a book may be started in this world for this hero. Called before a
    /// preview is generated, so a locked world cannot be reached by crafting a request.
    /// </summary>
    Task EnsureCanStartAsync(Guid userId, Guid characterId, string worldId, CancellationToken cancellationToken);

    /// <summary>Marks the world reached when a book is started there.</summary>
    Task MarkStartedAsync(Guid userId, Guid characterId, string worldId, CancellationToken cancellationToken);

    /// <summary>Marks the world finished once its book is paid for and generated.</summary>
    Task MarkCompletedAsync(
        Guid userId,
        Guid characterId,
        string worldId,
        Guid bookId,
        CancellationToken cancellationToken);
}
