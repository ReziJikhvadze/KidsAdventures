namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IAdventurePackRepository
{
    Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken);
    Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken);
    Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken);
}
