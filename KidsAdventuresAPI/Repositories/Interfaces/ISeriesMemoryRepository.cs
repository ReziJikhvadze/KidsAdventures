namespace AdventurePacks.Api.Repositories.Interfaces;

public interface ISeriesMemoryRepository
{
    Task<SeriesMemory?> GetBySeriesIdAsync(Guid seriesId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the snapshot for a series, creating the row on first use. <c>LastBookId</c> travels
    /// with it so a retried distillation can tell it has already folded that book in.
    /// </summary>
    Task UpsertAsync(SeriesMemory memory, CancellationToken cancellationToken);
}
