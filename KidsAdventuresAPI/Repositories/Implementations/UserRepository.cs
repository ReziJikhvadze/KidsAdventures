using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class UserRepository(ISqlConnectionFactory connectionFactory) : IUserRepository
{
    private const string UserColumns = """
        Id, Email, PasswordHash, SubscriptionType, BookCredits, WelcomeStoryRemaining, EmailConfirmed,
        EmailConfirmationToken, EmailConfirmationExpiresAt, CreatedAt
        """;

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var sql = $"""
                     SELECT TOP 1 {UserColumns}
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
        var sql = $"""
                     SELECT TOP 1 {UserColumns}
                     FROM Users
                     WHERE Email = @Email;
                     """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<UserRow>(
            new CommandDefinition(sql, new { Email = email }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<User?> GetByConfirmationTokenAsync(string token, CancellationToken cancellationToken)
    {
        var sql = $"""
                     SELECT TOP 1 {UserColumns}
                     FROM Users
                     WHERE EmailConfirmationToken = @Token;
                     """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<UserRow>(
            new CommandDefinition(sql, new { Token = token }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<Guid> CreateAsync(User user, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO Users (
                               Id, Email, PasswordHash, SubscriptionType, BookCredits, WelcomeStoryRemaining,
                               EmailConfirmed, EmailConfirmationToken, EmailConfirmationExpiresAt, CreatedAt)
                           VALUES (
                               @Id, @Email, @PasswordHash, @SubscriptionType, @BookCredits, @WelcomeStoryRemaining,
                               @EmailConfirmed, @EmailConfirmationToken, @EmailConfirmationExpiresAt, @CreatedAt);
                           """;
        user.Id = user.Id == Guid.Empty ? Guid.NewGuid() : user.Id;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            user.Id,
            user.Email,
            user.PasswordHash,
            SubscriptionType = user.SubscriptionType.ToString(),
            user.BookCredits,
            user.WelcomeStoryRemaining,
            user.EmailConfirmed,
            user.EmailConfirmationToken,
            user.EmailConfirmationExpiresAt,
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

    public async Task<bool> ConfirmEmailAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE Users
                           SET EmailConfirmed = 1
                           WHERE Id = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task AddBookCreditsAsync(Guid userId, int credits, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE Users
                           SET BookCredits = BookCredits + @Credits
                           WHERE Id = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            UserId = userId,
            Credits = credits
        }, cancellationToken: cancellationToken));
    }

    public async Task<bool> TryConsumeBookCreditAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE Users
                           SET BookCredits = BookCredits - 1
                           WHERE Id = @UserId
                             AND BookCredits > 0;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task RefundBookCreditAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE Users
                           SET BookCredits = BookCredits + 1
                           WHERE Id = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> TryConsumeWelcomeStoryAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE Users
                           SET WelcomeStoryRemaining = WelcomeStoryRemaining - 1
                           WHERE Id = @UserId
                             AND WelcomeStoryRemaining > 0;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task RefundWelcomeStoryAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE Users
                           SET WelcomeStoryRemaining = WelcomeStoryRemaining + 1
                           WHERE Id = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
    }

    private static User Map(UserRow row) => new()
    {
        Id = row.Id,
        Email = row.Email,
        PasswordHash = row.PasswordHash,
        SubscriptionType = Enum.Parse<SubscriptionType>(row.SubscriptionType),
        BookCredits = row.BookCredits,
        WelcomeStoryRemaining = row.WelcomeStoryRemaining,
        EmailConfirmed = row.EmailConfirmed,
        EmailConfirmationToken = row.EmailConfirmationToken,
        EmailConfirmationExpiresAt = row.EmailConfirmationExpiresAt,
        CreatedAt = row.CreatedAt
    };

    private sealed class UserRow
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string SubscriptionType { get; set; } = string.Empty;
        public int BookCredits { get; set; }
        public int WelcomeStoryRemaining { get; set; }
        public bool EmailConfirmed { get; set; }
        public string? EmailConfirmationToken { get; set; }
        public DateTime? EmailConfirmationExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
