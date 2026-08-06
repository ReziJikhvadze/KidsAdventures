using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class MasterStoryRunRepository(ISqlConnectionFactory connectionFactory) : IMasterStoryRunRepository
{
    private const string Columns = """
        Id, UserId, PackId, Status, ProgressMessage, ChildName, BirthDate, Age, Gender, Theme, EyeColor,
        ExtraWishes, AppearanceDescription, PhotoBlobUrl, StoryLanguage, SpreadCount, Model, SystemPrompt,
        UserPrompt, PromptTokens, CompletionTokens, StoryJson, ContentJson, CoverImageUrl,
        ErrorMessage, CreatedAt, UpdatedAt, ExpiresAt
        """;

    public async Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO dbo.MasterStoryRuns (
                               Id, UserId, Status, ProgressMessage, ChildName, BirthDate, Age, Gender, Theme, EyeColor,
                               ExtraWishes, AppearanceDescription, PhotoBlobUrl, StoryLanguage, SpreadCount, Model,
                               SystemPrompt, UserPrompt, CreatedAt, UpdatedAt, ExpiresAt)
                           VALUES (
                               @Id, @UserId, @Status, @ProgressMessage, @ChildName, @BirthDate, @Age, @Gender, @Theme, @EyeColor,
                               @ExtraWishes, @AppearanceDescription, @PhotoBlobUrl, @StoryLanguage, @SpreadCount, @Model,
                               @SystemPrompt, @UserPrompt, @CreatedAt, @UpdatedAt, @ExpiresAt);
                           """;

        run.Id = run.Id == Guid.Empty ? Guid.NewGuid() : run.Id;
        run.CreatedAt = run.CreatedAt == default ? DateTime.UtcNow : run.CreatedAt;
        run.UpdatedAt = DateTime.UtcNow;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, run, cancellationToken: cancellationToken));
    }

    public async Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var sql = $"SELECT TOP 1 {Columns} FROM dbo.MasterStoryRuns WHERE Id = @Id;";
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<MasterStoryRun>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP 1 Id, Status, ProgressMessage, ErrorMessage, CoverImageUrl
                           FROM dbo.MasterStoryRuns WHERE Id = @Id;
                           """;

        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<MasterStoryRunProgress>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task SetProgressAsync(Guid id, string status, string? progressMessage, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.MasterStoryRuns
                           SET Status = @Status, ProgressMessage = @ProgressMessage, UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = id, Status = status, ProgressMessage = progressMessage }, cancellationToken: cancellationToken));
    }

    public async Task SavePromptsAsync(
        Guid id,
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.MasterStoryRuns
                           SET Model = @Model, SystemPrompt = @SystemPrompt, UserPrompt = @UserPrompt,
                               UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, Model = model, SystemPrompt = systemPrompt, UserPrompt = userPrompt },
            cancellationToken: cancellationToken));
    }

    public async Task SaveStoryAsync(
        Guid id,
        string storyJson,
        string contentJson,
        int promptTokens,
        int completionTokens,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.MasterStoryRuns
                           SET StoryJson = @StoryJson,
                               ContentJson = @ContentJson,
                               PromptTokens = @PromptTokens,
                               CompletionTokens = @CompletionTokens,
                               UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, StoryJson = storyJson, ContentJson = contentJson, PromptTokens = promptTokens, CompletionTokens = completionTokens },
            cancellationToken: cancellationToken));
    }

    public async Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.MasterStoryRuns
                           SET CoverImageUrl = @CoverImageUrl, UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = id, CoverImageUrl = coverImageUrl }, cancellationToken: cancellationToken));
    }

    public async Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.MasterStoryRuns
                           SET Status = @Status, ContentJson = @ContentJson, ProgressMessage = NULL,
                               UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, Status = MasterStoryRunStatus.Ready, ContentJson = contentJson },
            cancellationToken: cancellationToken));
    }

    public async Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.MasterStoryRuns
                           SET Status = @Status, ErrorMessage = @ErrorMessage, UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, Status = MasterStoryRunStatus.Failed, ErrorMessage = Truncate(error, 1000) },
            cancellationToken: cancellationToken));
    }

    public async Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken)
    {
        // ExpiresAt is cleared here rather than in a separate call: a run that belongs to an
        // account is no longer a guest's temporary row, and leaving the expiry behind would let
        // the cleanup job delete a book somebody had already paid for.
        const string sql = """
                           UPDATE dbo.MasterStoryRuns
                           SET UserId = @UserId, PackId = @PackId, ExpiresAt = NULL, UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = id, UserId = userId, PackId = packId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP (@Limit) Id, PhotoBlobUrl, CoverImageUrl
                           FROM dbo.MasterStoryRuns
                           WHERE ExpiresAt IS NOT NULL AND ExpiresAt < SYSUTCDATETIME()
                           ORDER BY ExpiresAt;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ExpiredMasterStoryRun>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        const string sql = "DELETE FROM dbo.MasterStoryRuns WHERE Id IN @Ids;";
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(
            new CommandDefinition(sql, new { Ids = ids }, cancellationToken: cancellationToken));
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
