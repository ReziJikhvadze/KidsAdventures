using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class AuthChallengeRepository(ISqlConnectionFactory connectionFactory) : IAuthChallengeRepository
{
    private const string Columns = """
        Id, Purpose, Destination, SecretHash, UserId, AttemptCount, MaxAttempts,
        ExpiresAt, ConsumedAt, IpAddress, CreatedAt
        """;

    public async Task InsertAsync(AuthChallenge challenge, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO dbo.AuthChallenges (
                               Id, Purpose, Destination, SecretHash, UserId, AttemptCount, MaxAttempts,
                               ExpiresAt, ConsumedAt, IpAddress, CreatedAt)
                           VALUES (
                               @Id, @Purpose, @Destination, @SecretHash, @UserId, @AttemptCount, @MaxAttempts,
                               @ExpiresAt, @ConsumedAt, @IpAddress, @CreatedAt);
                           """;

        challenge.Id = challenge.Id == Guid.Empty ? Guid.NewGuid() : challenge.Id;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            challenge.Id,
            Purpose = challenge.Purpose.ToString(),
            challenge.Destination,
            challenge.SecretHash,
            challenge.UserId,
            challenge.AttemptCount,
            challenge.MaxAttempts,
            challenge.ExpiresAt,
            challenge.ConsumedAt,
            challenge.IpAddress,
            challenge.CreatedAt
        }, cancellationToken: cancellationToken));
    }

    public async Task<AuthChallenge?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP 1 {Columns}
                   FROM dbo.AuthChallenges
                   WHERE Id = @Id;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<AuthChallengeRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<AuthChallenge?> GetLatestAsync(
        AuthChallengePurpose purpose,
        string destination,
        CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP 1 {Columns}
                   FROM dbo.AuthChallenges
                   WHERE Purpose = @Purpose
                     AND Destination = @Destination
                   ORDER BY CreatedAt DESC;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<AuthChallengeRow>(
            new CommandDefinition(
                sql,
                new { Purpose = purpose.ToString(), Destination = destination },
                cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<AuthChallenge?> GetLatestPendingAsync(
        AuthChallengePurpose purpose,
        string destination,
        CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP 1 {Columns}
                   FROM dbo.AuthChallenges
                   WHERE Purpose = @Purpose
                     AND Destination = @Destination
                     AND ConsumedAt IS NULL
                     AND ExpiresAt > SYSUTCDATETIME()
                     AND AttemptCount < MaxAttempts
                   ORDER BY CreatedAt DESC;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<AuthChallengeRow>(
            new CommandDefinition(
                sql,
                new { Purpose = purpose.ToString(), Destination = destination },
                cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<int> CountByDestinationSinceAsync(
        AuthChallengePurpose purpose,
        string destination,
        DateTime since,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT COUNT(1)
                           FROM dbo.AuthChallenges
                           WHERE Purpose = @Purpose
                             AND Destination = @Destination
                             AND CreatedAt >= @Since;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { Purpose = purpose.ToString(), Destination = destination, Since = since },
            cancellationToken: cancellationToken));
    }

    public async Task<int> CountByIpSinceAsync(string ipAddress, DateTime since, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT COUNT(1)
                           FROM dbo.AuthChallenges
                           WHERE IpAddress = @IpAddress
                             AND CreatedAt >= @Since;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { IpAddress = ipAddress, Since = since },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> TryConsumeAsync(Guid id, CancellationToken cancellationToken)
    {
        // The ConsumedAt IS NULL predicate is what makes a magic link single-use even
        // when an email client prefetches the URL at the same moment the parent taps it.
        const string sql = """
                           UPDATE dbo.AuthChallenges
                           SET ConsumedAt = SYSUTCDATETIME()
                           WHERE Id = @Id
                             AND ConsumedAt IS NULL
                             AND ExpiresAt > SYSUTCDATETIME();
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<int> RecordFailedAttemptAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.AuthChallenges
                           SET AttemptCount = AttemptCount + 1
                           OUTPUT INSERTED.AttemptCount
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task InvalidatePendingAsync(
        AuthChallengePurpose purpose,
        string destination,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.AuthChallenges
                           SET ConsumedAt = SYSUTCDATETIME()
                           WHERE Purpose = @Purpose
                             AND Destination = @Destination
                             AND ConsumedAt IS NULL;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Purpose = purpose.ToString(), Destination = destination },
            cancellationToken: cancellationToken));
    }

    public async Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken cancellationToken)
    {
        const string sql = """
                           DELETE FROM dbo.AuthChallenges
                           WHERE ExpiresAt < @OlderThan;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { OlderThan = olderThanUtc },
            cancellationToken: cancellationToken));
    }

    private static AuthChallenge Map(AuthChallengeRow row) => new()
    {
        Id = row.Id,
        Purpose = Enum.Parse<AuthChallengePurpose>(row.Purpose),
        Destination = row.Destination,
        SecretHash = row.SecretHash,
        UserId = row.UserId,
        AttemptCount = row.AttemptCount,
        MaxAttempts = row.MaxAttempts,
        ExpiresAt = row.ExpiresAt,
        ConsumedAt = row.ConsumedAt,
        IpAddress = row.IpAddress,
        CreatedAt = row.CreatedAt
    };

    private sealed class AuthChallengeRow
    {
        public Guid Id { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string SecretHash { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public int AttemptCount { get; set; }
        public int MaxAttempts { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? ConsumedAt { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
