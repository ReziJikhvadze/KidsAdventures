using AdventurePacks.Api.Domain.Beki;

namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IBekiStoryRepository
{
    Task<Guid> CreateAsync(BekiStoryRecord record, CancellationToken cancellationToken);

    Task<BekiStoryRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Owner-scoped read, so one parent can never fetch another's book.</summary>
    Task<BekiStoryRecord?> GetForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    /// <summary>Idempotency: a retried request returns the original book rather than billing twice.</summary>
    Task<BekiStoryRecord?> GetByRequestIdAsync(string requestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BekiStoryRecord>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Claims a pending story for generation. Returns false when another worker got there
    /// first, which is what stops a duplicated Hangfire job paying for the same book twice.
    /// </summary>
    Task<bool> TryMarkGeneratingAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Writes the approved story and its continuation memory in one transaction.</summary>
    Task SaveApprovedAsync(
        BekiStoryRecord record,
        BekiMemoryRecord memory,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        Guid id,
        string failureReason,
        string? validationErrorsJson,
        string? rawGeneratorOutputJson,
        CancellationToken cancellationToken);

    /// <summary>
    /// The memory of this child's most recent approved book — the input that makes book N+1
    /// a continuation rather than a reset.
    /// </summary>
    Task<BekiMemoryRecord?> GetLatestMemoryForCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken);

    /// <summary>Highest book number this child has reached, so the next one numbers correctly.</summary>
    Task<int> GetLatestBookNumberAsync(Guid characterId, CancellationToken cancellationToken);
}
