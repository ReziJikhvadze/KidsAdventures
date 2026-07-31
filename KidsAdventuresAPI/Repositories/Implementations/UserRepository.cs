using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class UserRepository(ISqlConnectionFactory connectionFactory) : IUserRepository
{
    private const string UserColumns = """
        Id, Email, PasswordHash, PhoneNumber, PhoneConfirmed, PreferredLanguage, DisplayName, IsAdmin,
        SubscriptionType, BookCredits, WelcomeStoryRemaining, EmailConfirmed,
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

    public async Task<User?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        var sql = $"""
                     SELECT TOP 1 {UserColumns}
                     FROM Users
                     WHERE PhoneNumber = @PhoneNumber;
                     """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<UserRow>(
            new CommandDefinition(sql, new { PhoneNumber = phoneNumber }, cancellationToken: cancellationToken));
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
                               Id, Email, PasswordHash, PhoneNumber, PhoneConfirmed, PreferredLanguage,
                               DisplayName, IsAdmin, SubscriptionType, BookCredits, WelcomeStoryRemaining,
                               EmailConfirmed, EmailConfirmationToken, EmailConfirmationExpiresAt, CreatedAt)
                           VALUES (
                               @Id, @Email, @PasswordHash, @PhoneNumber, @PhoneConfirmed, @PreferredLanguage,
                               @DisplayName, @IsAdmin, @SubscriptionType, @BookCredits, @WelcomeStoryRemaining,
                               @EmailConfirmed, @EmailConfirmationToken, @EmailConfirmationExpiresAt, @CreatedAt);
                           """;
        user.Id = user.Id == Guid.Empty ? Guid.NewGuid() : user.Id;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            user.Id,
            Email = string.IsNullOrWhiteSpace(user.Email) ? null : user.Email,
            user.PasswordHash,
            user.PhoneNumber,
            user.PhoneConfirmed,
            user.PreferredLanguage,
            user.DisplayName,
            user.IsAdmin,
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

    public async Task<bool> AttachPhoneNumberAsync(Guid userId, string phoneNumber, CancellationToken cancellationToken)
    {
        // Guarded so a verified number can never be silently moved off the account
        // that already proved ownership of it.
        const string sql = """
                           UPDATE Users
                           SET PhoneNumber = @PhoneNumber,
                               PhoneConfirmed = 1
                           WHERE Id = @UserId
                             AND (PhoneNumber IS NULL OR PhoneNumber = @PhoneNumber);
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { UserId = userId, PhoneNumber = phoneNumber },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> AttachEmailAsync(Guid userId, string email, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE Users
                           SET Email = @Email,
                               EmailConfirmed = 1
                           WHERE Id = @UserId
                             AND (Email IS NULL OR Email = @Email);
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { UserId = userId, Email = email },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? preferredLanguage,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE Users
                           SET DisplayName = COALESCE(@DisplayName, DisplayName),
                               PreferredLanguage = COALESCE(@PreferredLanguage, PreferredLanguage)
                           WHERE Id = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { UserId = userId, DisplayName = displayName, PreferredLanguage = preferredLanguage },
            cancellationToken: cancellationToken));
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
        Email = row.Email ?? string.Empty,
        PasswordHash = row.PasswordHash,
        PhoneNumber = row.PhoneNumber,
        PhoneConfirmed = row.PhoneConfirmed,
        PreferredLanguage = string.IsNullOrWhiteSpace(row.PreferredLanguage) ? "ka" : row.PreferredLanguage,
        DisplayName = row.DisplayName,
        IsAdmin = row.IsAdmin,
        SubscriptionType = Enum.Parse<SubscriptionType>(row.SubscriptionType),
        BookCredits = row.BookCredits,
        WelcomeStoryRemaining = row.WelcomeStoryRemaining,
        EmailConfirmed = row.EmailConfirmed,
        EmailConfirmationToken = row.EmailConfirmationToken,
        EmailConfirmationExpiresAt = row.EmailConfirmationExpiresAt,
        CreatedAt = row.CreatedAt
    };


    public async Task<int> PurgeDemoAccountsAsync(string emailSuffix, CancellationToken cancellationToken)
    {
        // Children first, then the accounts. Ordered by foreign key so the deletes succeed
        // without disabling constraints, which is exactly the kind of thing that must never
        // become a habit against a real database.
        const string sql = """
            DECLARE @Ids TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
            INSERT INTO @Ids (Id) SELECT Id FROM dbo.Users WHERE Email LIKE @Pattern;

            DELETE FROM dbo.PromoRedemptions WHERE UserId IN (SELECT Id FROM @Ids);
            DELETE FROM dbo.PrintOrders      WHERE OrderId IN
                (SELECT Id FROM dbo.Orders WHERE UserId IN (SELECT Id FROM @Ids));
            DELETE FROM dbo.Orders           WHERE UserId IN (SELECT Id FROM @Ids);
            DELETE FROM dbo.SeriesMemories   WHERE UserId IN (SELECT Id FROM @Ids);
            DELETE FROM dbo.BookCharacters   WHERE BookId IN
                (SELECT Id FROM dbo.AdventurePacks WHERE UserId IN (SELECT Id FROM @Ids));
            DELETE FROM dbo.AdventurePacks   WHERE UserId IN (SELECT Id FROM @Ids);
            DELETE FROM dbo.Characters       WHERE UserId IN (SELECT Id FROM @Ids);
            DELETE FROM dbo.Users            WHERE Id IN (SELECT Id FROM @Ids);

            SELECT COUNT(*) FROM @Ids;
            """;

        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { Pattern = "%" + emailSuffix }, cancellationToken: cancellationToken));
    }

    private sealed class UserRow
    {
        public Guid Id { get; set; }
        public string? Email { get; set; }
        public string? PasswordHash { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneConfirmed { get; set; }
        public string? PreferredLanguage { get; set; }
        public string? DisplayName { get; set; }
        public bool IsAdmin { get; set; }
        public string SubscriptionType { get; set; } = string.Empty;
        public int BookCredits { get; set; }
        public int WelcomeStoryRemaining { get; set; }
        public bool EmailConfirmed { get; set; }
        public string? EmailConfirmationToken { get; set; }
        public DateTime? EmailConfirmationExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
