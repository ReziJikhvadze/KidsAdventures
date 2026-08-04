using AdventurePacks.Api.Domain.Beki;
using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class BekiStoryRepository(ISqlConnectionFactory connectionFactory) : IBekiStoryRepository
{
    private const string Columns = """
        Id, RequestId, UserId, CharacterId, BookNumber, ChildName, AgeBand, Theme, TitleKa,
        Status, FinalStoryJson, RawGeneratorOutputJson, StoryInputJson, ReviewStatus,
        ValidationErrorsJson, FailureReason, CreativeSeedId, GeneratorPromptVersion,
        ReviewerPromptVersion, RepairPromptVersion, GeneratorModel, ReviewerModel,
        InputSchemaVersion, OutputSchemaVersion, CreatedAt, CompletedAt
        """;

    public async Task<Guid> CreateAsync(BekiStoryRecord record, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO dbo.BekiStories (
                               Id, RequestId, UserId, CharacterId, BookNumber, ChildName, AgeBand, Theme,
                               Status, StoryInputJson, InputSchemaVersion, OutputSchemaVersion, CreatedAt)
                           VALUES (
                               @Id, @RequestId, @UserId, @CharacterId, @BookNumber, @ChildName, @AgeBand, @Theme,
                               @Status, @StoryInputJson, @InputSchemaVersion, @OutputSchemaVersion, @CreatedAt);
                           """;

        record.Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, record, cancellationToken: cancellationToken));
        return record.Id;
    }

    public async Task<BekiStoryRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var sql = $"SELECT TOP 1 {Columns} FROM dbo.BekiStories WHERE Id = @Id;";
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<BekiStoryRecord>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<BekiStoryRecord?> GetForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var sql = $"SELECT TOP 1 {Columns} FROM dbo.BekiStories WHERE Id = @Id AND UserId = @UserId;";
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<BekiStoryRecord>(
            new CommandDefinition(sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<BekiStoryRecord?> GetByRequestIdAsync(string requestId, CancellationToken cancellationToken)
    {
        var sql = $"SELECT TOP 1 {Columns} FROM dbo.BekiStories WHERE RequestId = @RequestId;";
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<BekiStoryRecord>(
            new CommandDefinition(sql, new { RequestId = requestId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<BekiStoryRecord>> ListForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        // The list view never needs the book bodies; omitting them keeps a shelf of twelve
        // books from pulling megabytes of JSON across the wire.
        const string sql = """
                           SELECT Id, RequestId, UserId, CharacterId, BookNumber, ChildName, AgeBand, Theme,
                                  TitleKa, Status, ReviewStatus, FailureReason, CreatedAt, CompletedAt
                           FROM dbo.BekiStories
                           WHERE UserId = @UserId
                           ORDER BY CreatedAt DESC;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<BekiStoryRecord>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<bool> TryMarkGeneratingAsync(Guid id, CancellationToken cancellationToken)
    {
        // The Status = 'pending' predicate is the whole idempotency story: Hangfire can
        // deliver a job more than once, and only the first worker sees a row to update.
        const string sql = """
                           UPDATE dbo.BekiStories
                           SET Status = N'generating'
                           WHERE Id = @Id AND Status = N'pending';
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task SaveApprovedAsync(
        BekiStoryRecord record,
        BekiMemoryRecord memory,
        CancellationToken cancellationToken)
    {
        const string storySql = """
                                UPDATE dbo.BekiStories
                                SET Status = @Status,
                                    TitleKa = @TitleKa,
                                    FinalStoryJson = @FinalStoryJson,
                                    RawGeneratorOutputJson = @RawGeneratorOutputJson,
                                    ReviewStatus = @ReviewStatus,
                                    ValidationErrorsJson = NULL,
                                    FailureReason = NULL,
                                    CreativeSeedId = @CreativeSeedId,
                                    GeneratorPromptVersion = @GeneratorPromptVersion,
                                    ReviewerPromptVersion = @ReviewerPromptVersion,
                                    RepairPromptVersion = @RepairPromptVersion,
                                    GeneratorModel = @GeneratorModel,
                                    ReviewerModel = @ReviewerModel,
                                    CompletedAt = SYSUTCDATETIME()
                                WHERE Id = @Id;
                                """;

        // MERGE rather than INSERT: a regenerated book must replace its memory, or the next
        // chapter would follow a hook the child never actually read.
        const string memorySql = """
                                 MERGE dbo.BekiContinuationMemory AS target
                                 USING (SELECT @StoryId AS StoryId) AS source
                                    ON target.StoryId = source.StoryId
                                 WHEN MATCHED THEN
                                     UPDATE SET MemoryJson = @MemoryJson,
                                                NextChapterHookKa = @NextChapterHookKa,
                                                BookNumber = @BookNumber,
                                                CharacterId = @CharacterId
                                 WHEN NOT MATCHED THEN
                                     INSERT (Id, StoryId, CharacterId, BookNumber, MemoryJson, NextChapterHookKa, CreatedAt)
                                     VALUES (@Id, @StoryId, @CharacterId, @BookNumber, @MemoryJson, @NextChapterHookKa, SYSUTCDATETIME());
                                 """;

        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                storySql, record, transaction, cancellationToken: cancellationToken));

            memory.Id = memory.Id == Guid.Empty ? Guid.NewGuid() : memory.Id;
            await connection.ExecuteAsync(new CommandDefinition(
                memorySql, memory, transaction, cancellationToken: cancellationToken));

            transaction.Commit();
        }
        catch
        {
            // A book saved without its memory would silently break the next chapter, so the
            // two either land together or not at all.
            transaction.Rollback();
            throw;
        }
    }

    public async Task MarkFailedAsync(
        Guid id,
        string failureReason,
        string? validationErrorsJson,
        string? rawGeneratorOutputJson,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.BekiStories
                           SET Status = N'failed',
                               FailureReason = @FailureReason,
                               ValidationErrorsJson = @ValidationErrorsJson,
                               RawGeneratorOutputJson = COALESCE(@RawGeneratorOutputJson, RawGeneratorOutputJson),
                               CompletedAt = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = id,
                FailureReason = Truncate(failureReason, 200),
                ValidationErrorsJson = validationErrorsJson,
                RawGeneratorOutputJson = rawGeneratorOutputJson,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<BekiMemoryRecord?> GetLatestMemoryForCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken)
    {
        // Joined to the story so memory from a failed or superseded book can never be used
        // as the basis for the next one.
        const string sql = """
                           SELECT TOP 1 m.Id, m.StoryId, m.CharacterId, m.BookNumber, m.MemoryJson,
                                  m.NextChapterHookKa, m.CreatedAt
                           FROM dbo.BekiContinuationMemory m
                           INNER JOIN dbo.BekiStories s ON s.Id = m.StoryId
                           WHERE m.CharacterId = @CharacterId
                             AND s.Status IN (N'approved', N'needs_human_review')
                           ORDER BY m.BookNumber DESC, m.CreatedAt DESC;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<BekiMemoryRecord>(
            new CommandDefinition(sql, new { CharacterId = characterId }, cancellationToken: cancellationToken));
    }

    public async Task<int> GetLatestBookNumberAsync(Guid characterId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT ISNULL(MAX(BookNumber), 0)
                           FROM dbo.BekiStories
                           WHERE CharacterId = @CharacterId
                             AND Status IN (N'approved', N'needs_human_review');
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { CharacterId = characterId }, cancellationToken: cancellationToken));
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
