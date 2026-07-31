using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class StoryRuleRepository(ISqlConnectionFactory connectionFactory) : IStoryRuleRepository
{
    private const string Columns = """
        Id, AgeBand, Theme, MaxWordsPerPage, MaxSentenceWords, VocabularyLevel,
        ScarinessLimit, ExtraGuidance, IsActive, UpdatedByUserId, CreatedAt, UpdatedAt
        """;

    public async Task<IReadOnlyList<StoryRule>> GetAllAsync(CancellationToken cancellationToken)
    {
        var sql = $"SELECT {Columns} FROM dbo.StoryRules ORDER BY AgeBand, Theme;";
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<StoryRule>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<StoryRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var sql = $"SELECT TOP 1 {Columns} FROM dbo.StoryRules WHERE Id = @Id;";
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<StoryRule>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<StoryRule?> ResolveAsync(
        string ageBand,
        string theme,
        CancellationToken cancellationToken)
    {
        // Exact cell wins; the theme-wide row for the band is the fallback. Ordering by
        // "Theme IS NULL" puts the specific row first without a second round trip.
        var sql = $"""
                   SELECT TOP 1 {Columns}
                   FROM dbo.StoryRules
                   WHERE IsActive = 1
                     AND AgeBand = @AgeBand
                     AND (Theme = @Theme OR Theme IS NULL)
                   ORDER BY CASE WHEN Theme IS NULL THEN 1 ELSE 0 END;
                   """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<StoryRule>(new CommandDefinition(
            sql, new { AgeBand = ageBand, Theme = theme }, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(StoryRule rule, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.StoryRules
                           SET MaxWordsPerPage  = @MaxWordsPerPage,
                               MaxSentenceWords = @MaxSentenceWords,
                               VocabularyLevel  = @VocabularyLevel,
                               ScarinessLimit   = @ScarinessLimit,
                               ExtraGuidance    = @ExtraGuidance,
                               IsActive         = @IsActive,
                               UpdatedByUserId  = @UpdatedByUserId,
                               UpdatedAt        = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                rule.Id,
                rule.MaxWordsPerPage,
                rule.MaxSentenceWords,
                rule.VocabularyLevel,
                rule.ScarinessLimit,
                rule.ExtraGuidance,
                rule.IsActive,
                rule.UpdatedByUserId
            },
            cancellationToken: cancellationToken));
        return affected > 0;
    }
}
