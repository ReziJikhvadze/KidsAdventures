using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class CharacterRepository(ISqlConnectionFactory connectionFactory) : ICharacterRepository
{
    /// <summary>Matches <c>CK_BookCharacters_Position</c>.</summary>
    public const int MaxCharactersPerBook = 3;

    private const string Columns = """
        Id, UserId, Name, BirthDate, Gender, EyeColor, CharacterType, Relationship, IsPrimary,
        PhotoUrl, AppearanceDescription, AppearancePhotoUrl, LegacyChildId, LegacyFamilyMemberId,
        CreatedAt, UpdatedAt
        """;

    public async Task<IReadOnlyList<Character>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT {Columns}
                   FROM dbo.Characters
                   WHERE UserId = @UserId
                   ORDER BY IsPrimary DESC, CreatedAt ASC;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Character>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<Character>> GetHeroesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT {Columns}
                   FROM dbo.Characters
                   WHERE UserId = @UserId AND IsPrimary = 1
                   ORDER BY CreatedAt ASC;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Character>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<Character?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP 1 {Columns}
                   FROM dbo.Characters
                   WHERE Id = @Id AND UserId = @UserId;
                   """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Character>(
            new CommandDefinition(sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Character>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var sql = $"""
                   SELECT {Columns}
                   FROM dbo.Characters
                   WHERE UserId = @UserId AND Id IN @Ids;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Character>(
            new CommandDefinition(sql, new { UserId = userId, Ids = ids }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT COUNT(1) FROM dbo.Characters WHERE UserId = @UserId;";
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<Guid> CreateAsync(Character character, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO dbo.Characters (
                               Id, UserId, Name, BirthDate, Gender, EyeColor, CharacterType, Relationship,
                               IsPrimary, PhotoUrl, AppearanceDescription, AppearancePhotoUrl,
                               LegacyChildId, LegacyFamilyMemberId, CreatedAt, UpdatedAt)
                           VALUES (
                               @Id, @UserId, @Name, @BirthDate, @Gender, @EyeColor, @CharacterType, @Relationship,
                               @IsPrimary, @PhotoUrl, @AppearanceDescription, @AppearancePhotoUrl,
                               @LegacyChildId, @LegacyFamilyMemberId, @CreatedAt, @UpdatedAt);
                           """;
        character.Id = character.Id == Guid.Empty ? Guid.NewGuid() : character.Id;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, character, cancellationToken: cancellationToken));
        return character.Id;
    }

    public async Task<bool> UpdateAsync(Character character, CancellationToken cancellationToken)
    {
        // A new portrait invalidates the cached appearance in the same statement that
        // stores it: leaving the two out of step for even one request would let the next
        // illustration render the old face.
        const string sql = """
                           UPDATE dbo.Characters
                           SET Name = @Name,
                               BirthDate = @BirthDate,
                               Gender = @Gender,
                               EyeColor = @EyeColor,
                               CharacterType = @CharacterType,
                               Relationship = @Relationship,
                               IsPrimary = @IsPrimary,
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
                               END,
                               UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id AND UserId = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(sql, character, cancellationToken: cancellationToken));
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
                           UPDATE dbo.Characters
                           SET AppearanceDescription = @AppearanceDescription,
                               AppearancePhotoUrl = @AppearancePhotoUrl,
                               UpdatedAt = SYSUTCDATETIME()
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
                           DELETE FROM dbo.Characters
                           WHERE Id = @Id AND UserId = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> IsCastInAnyBookAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT CASE WHEN EXISTS (
                               SELECT 1 FROM dbo.BookCharacters WHERE CharacterId = @Id
                               UNION ALL
                               SELECT 1 FROM dbo.AdventurePacks WHERE PrimaryCharacterId = @Id
                           ) THEN 1 ELSE 0 END;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlySet<Guid>> GetCastCharacterIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT bc.CharacterId
                           FROM dbo.BookCharacters AS bc
                           INNER JOIN dbo.Characters AS c ON c.Id = bc.CharacterId
                           WHERE c.UserId = @UserId
                           UNION
                           SELECT p.PrimaryCharacterId
                           FROM dbo.AdventurePacks AS p
                           WHERE p.UserId = @UserId AND p.PrimaryCharacterId IS NOT NULL;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var ids = await connection.QueryAsync<Guid>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return ids.ToHashSet();
    }

    public async Task<IReadOnlyList<Character>> GetByBookIdAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT {PrefixColumns("c")}
                   FROM dbo.BookCharacters AS bc
                   INNER JOIN dbo.Characters AS c ON c.Id = bc.CharacterId
                   WHERE bc.BookId = @BookId
                   ORDER BY bc.Position ASC;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Character>(
            new CommandDefinition(sql, new { BookId = bookId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task SetBookCastAsync(
        Guid bookId,
        IReadOnlyList<Guid> characterIds,
        CancellationToken cancellationToken)
    {
        var distinct = characterIds.Distinct().ToList();
        if (distinct.Count > MaxCharactersPerBook)
        {
            throw new InvalidOperationException($"წიგნში მაქსიმუმ {MaxCharactersPerBook} პერსონაჟია.");
        }

        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.BookCharacters WHERE BookId = @BookId;",
            new { BookId = bookId },
            transaction,
            cancellationToken: cancellationToken));

        if (distinct.Count > 0)
        {
            var rows = distinct
                .Select((characterId, index) => new
                {
                    BookId = bookId,
                    CharacterId = characterId,
                    Position = (byte)(index + 1)
                })
                .ToList();

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.BookCharacters (BookId, CharacterId, Position)
                VALUES (@BookId, @CharacterId, @Position);
                """,
                rows,
                transaction,
                cancellationToken: cancellationToken));
        }

        transaction.Commit();
    }

    private static string PrefixColumns(string alias) =>
        string.Join(", ", Columns
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(column => $"{alias}.{column}"));
}
