using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class SeriesMemoryRepository(ISqlConnectionFactory connectionFactory) : ISeriesMemoryRepository
{
    public async Task<SeriesMemory?> GetBySeriesIdAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP 1 SeriesId, UserId, MemoryJson, MemoryText, LastBookId, BookCount, CreatedAt, UpdatedAt
                           FROM dbo.SeriesMemories
                           WHERE SeriesId = @SeriesId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<SeriesMemory>(
            new CommandDefinition(sql, new { SeriesId = seriesId }, cancellationToken: cancellationToken));
    }

    public async Task UpsertAsync(SeriesMemory memory, CancellationToken cancellationToken)
    {
        // MERGE on the primary key: the first book in a series creates the row, every book
        // after it rewrites the same one.
        const string sql = """
                           MERGE dbo.SeriesMemories AS target
                           USING (SELECT @SeriesId AS SeriesId) AS source
                           ON target.SeriesId = source.SeriesId
                           WHEN MATCHED THEN
                               UPDATE SET MemoryJson = @MemoryJson,
                                          MemoryText = @MemoryText,
                                          LastBookId = @LastBookId,
                                          BookCount  = @BookCount,
                                          UpdatedAt  = SYSUTCDATETIME()
                           WHEN NOT MATCHED THEN
                               INSERT (SeriesId, UserId, MemoryJson, MemoryText, LastBookId, BookCount)
                               VALUES (@SeriesId, @UserId, @MemoryJson, @MemoryText, @LastBookId, @BookCount);
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                memory.SeriesId,
                memory.UserId,
                memory.MemoryJson,
                memory.MemoryText,
                memory.LastBookId,
                memory.BookCount
            },
            cancellationToken: cancellationToken));
    }
}
