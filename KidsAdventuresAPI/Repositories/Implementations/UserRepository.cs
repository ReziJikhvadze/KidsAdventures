using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class UserRepository(ISqlConnectionFactory connectionFactory) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP 1 Id, Email, PasswordHash, SubscriptionType, CreatedAt
                           FROM Users
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<UserRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP 1 Id, Email, PasswordHash, SubscriptionType, CreatedAt
                           FROM Users
                           WHERE Email = @Email;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<UserRow>(
            new CommandDefinition(sql, new { Email = email }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<Guid> CreateAsync(User user, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO Users (Id, Email, PasswordHash, SubscriptionType, CreatedAt)
                           VALUES (@Id, @Email, @PasswordHash, @SubscriptionType, @CreatedAt);
                           """;
        user.Id = user.Id == Guid.Empty ? Guid.NewGuid() : user.Id;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            user.Id,
            user.Email,
            user.PasswordHash,
            SubscriptionType = user.SubscriptionType.ToString(),
            user.CreatedAt
        }, cancellationToken: cancellationToken));

        return user.Id;
    }

    public async Task<bool> UpdateSubscriptionTypeAsync(Guid userId, SubscriptionType subscriptionType, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE Users
                           SET SubscriptionType = @SubscriptionType
                           WHERE Id = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            UserId = userId,
            SubscriptionType = subscriptionType.ToString()
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    private static User Map(UserRow row) => new()
    {
        Id = row.Id,
        Email = row.Email,
        PasswordHash = row.PasswordHash,
        SubscriptionType = Enum.Parse<SubscriptionType>(row.SubscriptionType),
        CreatedAt = row.CreatedAt
    };

    private sealed class UserRow
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string SubscriptionType { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
