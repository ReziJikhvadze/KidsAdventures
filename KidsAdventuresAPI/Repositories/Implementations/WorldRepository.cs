using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class WorldRepository(ISqlConnectionFactory connectionFactory) : IWorldRepository
{
    private const string ProgressColumns =
        "Id, UserId, CharacterId, WorldId, State, BookId, UnlockedAt, CompletedAt, CreatedAt";

    public async Task<IReadOnlyList<World>> GetActiveAsync(CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT Id, Name, SortOrder, IsActive
                           FROM dbo.Worlds
                           WHERE IsActive = 1
                           ORDER BY SortOrder ASC;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<World>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<bool> ExistsAsync(string worldId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT CASE WHEN EXISTS (
                               SELECT 1 FROM dbo.Worlds WHERE Id = @WorldId AND IsActive = 1
                           ) THEN 1 ELSE 0 END;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { WorldId = worldId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<UserWorldProgress>> GetProgressAsync(
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT {ProgressColumns}
                   FROM dbo.UserWorldProgress
                   WHERE CharacterId = @CharacterId;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProgressRow>(
            new CommandDefinition(sql, new { CharacterId = characterId }, cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<UserWorldProgress>> GetProgressForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT {ProgressColumns}
                   FROM dbo.UserWorldProgress
                   WHERE UserId = @UserId;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProgressRow>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

    public async Task UnlockAsync(
        Guid userId,
        Guid characterId,
        string worldId,
        CancellationToken cancellationToken)
    {
        // MERGE against the unique (CharacterId, WorldId) index rather than a
        // read-then-write, so two concurrent book starts cannot both insert.
        const string sql = """
                           MERGE dbo.UserWorldProgress WITH (HOLDLOCK) AS target
                           USING (SELECT @CharacterId AS CharacterId, @WorldId AS WorldId) AS source
                              ON target.CharacterId = source.CharacterId
                             AND target.WorldId = source.WorldId
                           WHEN MATCHED AND target.State = N'Locked' THEN
                               UPDATE SET State = N'Unlocked', UnlockedAt = SYSUTCDATETIME()
                           WHEN NOT MATCHED BY TARGET THEN
                               INSERT (Id, UserId, CharacterId, WorldId, State, UnlockedAt)
                               VALUES (NEWID(), @UserId, @CharacterId, @WorldId, N'Unlocked', SYSUTCDATETIME());
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { UserId = userId, CharacterId = characterId, WorldId = worldId },
            cancellationToken: cancellationToken));
    }

    public async Task CompleteAsync(
        Guid userId,
        Guid characterId,
        string worldId,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        // The first paid book in a world is the one credited with completing it; a
        // second book in the same world does not overwrite that history.
        const string sql = """
                           MERGE dbo.UserWorldProgress WITH (HOLDLOCK) AS target
                           USING (SELECT @CharacterId AS CharacterId, @WorldId AS WorldId) AS source
                              ON target.CharacterId = source.CharacterId
                             AND target.WorldId = source.WorldId
                           WHEN MATCHED AND target.State <> N'Completed' THEN
                               UPDATE SET State = N'Completed',
                                          BookId = @BookId,
                                          UnlockedAt = ISNULL(target.UnlockedAt, SYSUTCDATETIME()),
                                          CompletedAt = SYSUTCDATETIME()
                           WHEN NOT MATCHED BY TARGET THEN
                               INSERT (Id, UserId, CharacterId, WorldId, State, BookId, UnlockedAt, CompletedAt)
                               VALUES (NEWID(), @UserId, @CharacterId, @WorldId, N'Completed', @BookId,
                                       SYSUTCDATETIME(), SYSUTCDATETIME());
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { UserId = userId, CharacterId = characterId, WorldId = worldId, BookId = bookId },
            cancellationToken: cancellationToken));
    }

    private static UserWorldProgress Map(ProgressRow row) => new()
    {
        Id = row.Id,
        UserId = row.UserId,
        CharacterId = row.CharacterId,
        WorldId = row.WorldId,
        State = Enum.TryParse<WorldState>(row.State, out var state) ? state : WorldState.Locked,
        BookId = row.BookId,
        UnlockedAt = row.UnlockedAt,
        CompletedAt = row.CompletedAt,
        CreatedAt = row.CreatedAt
    };

    private sealed class ProgressRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid CharacterId { get; set; }
        public string WorldId { get; set; } = string.Empty;
        public string State { get; set; } = "Locked";
        public Guid? BookId { get; set; }
        public DateTime? UnlockedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
