using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class ChildRepository(ISqlConnectionFactory connectionFactory) : IChildRepository
{
    private const string ChildColumns =
        "Id, UserId, Name, Age, PhotoUrl, AppearanceDescription, AppearancePhotoUrl, CreatedAt";

    public async Task<IReadOnlyList<Child>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT {ChildColumns}
                   FROM Children
                   WHERE UserId = @UserId
                   ORDER BY CreatedAt DESC;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Child>(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<Child?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP 1 {ChildColumns}
                   FROM Children
                   WHERE Id = @Id AND UserId = @UserId;
                   """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Child>(new CommandDefinition(sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<Guid> CreateAsync(Child child, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO Children (Id, UserId, Name, Age, PhotoUrl, CreatedAt)
                           VALUES (@Id, @UserId, @Name, @Age, @PhotoUrl, @CreatedAt);
                           """;
        child.Id = child.Id == Guid.Empty ? Guid.NewGuid() : child.Id;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, child, cancellationToken: cancellationToken));
        return child.Id;
    }

    public async Task<bool> UpdateAsync(Child child, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE Children
                           SET Name = @Name,
                               Age = @Age,
                               PhotoUrl = @PhotoUrl,
                               AppearanceDescription = CASE
                                   WHEN @PhotoUrl IS NULL OR @PhotoUrl <> ISNULL(AppearancePhotoUrl, N'')
                                       THEN NULL
                                   ELSE AppearanceDescription
                               END,
                               AppearancePhotoUrl = CASE
                                   WHEN @PhotoUrl IS NULL OR @PhotoUrl <> ISNULL(AppearancePhotoUrl, N'')
                                       THEN NULL
                                   ELSE AppearancePhotoUrl
                               END
                           WHERE Id = @Id AND UserId = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, child, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task UpdateAppearanceCacheAsync(
        Guid id,
        Guid userId,
        string? appearanceDescription,
        string? appearancePhotoUrl,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE Children
                           SET AppearanceDescription = @AppearanceDescription,
                               AppearancePhotoUrl = @AppearancePhotoUrl
                           WHERE Id = @Id AND UserId = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            UserId = userId,
            AppearanceDescription = appearanceDescription,
            AppearancePhotoUrl = appearancePhotoUrl
        }, cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           DELETE FROM Children
                           WHERE Id = @Id AND UserId = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
        return affected > 0;
    }
}
