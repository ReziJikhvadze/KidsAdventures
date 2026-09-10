using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class MasterStoryRunRepository(ISqlConnectionFactory connectionFactory)
    : IMasterStoryRunRepository, IMasterStoryRunSweepStore
{
    /// <summary>
    /// The statuses a writing job leaves behind while it works, and the only ones the sweep will
    /// fail. Pending has no job yet; Ready and Failed are already terminal.
    /// </summary>
    public static readonly IReadOnlyList<string> StaleStatuses =
    [
        MasterStoryRunStatus.Writing,
        MasterStoryRunStatus.Illustrating
    ];

    private const string Columns = """
        Id, UserId, PackId, CharacterId, Status, ProgressMessage, ChildName, BirthDate, Age, Gender, Theme, EyeColor,
        ExtraWishes, AppearanceDescription, PhotoBlobUrl, StoryLanguage, SpreadCount, Model, SystemPrompt,
        UserPrompt, PromptTokens, CompletionTokens, StoryJson, ContentJson, CoverImageUrl,
        ErrorMessage, PromptVersion, CreatedAt, UpdatedAt, ExpiresAt
        """;

    public async Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO dbo.MasterStoryRuns (
                               Id, UserId, CharacterId, Status, ProgressMessage, ChildName, BirthDate, Age, Gender, Theme, EyeColor,
                               ExtraWishes, AppearanceDescription, PhotoBlobUrl, StoryLanguage, SpreadCount, Model,
                               SystemPrompt, UserPrompt, CreatedAt, UpdatedAt, ExpiresAt)
                           VALUES (
                               @Id, @UserId, @CharacterId, @Status, @ProgressMessage, @ChildName, @BirthDate, @Age, @Gender, @Theme, @EyeColor,
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
        string promptVersion,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.MasterStoryRuns
                           SET Model = @Model, PromptVersion = @PromptVersion,
                               SystemPrompt = @SystemPrompt, UserPrompt = @UserPrompt,
                               UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = id,
                Model = model,
                PromptVersion = promptVersion,
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt
            },
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

    public async Task SaveAppearanceDescriptionAsync(
        Guid id,
        string appearanceDescription,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.MasterStoryRuns
                           SET AppearanceDescription = @AppearanceDescription, UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, AppearanceDescription = appearanceDescription },
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

    public async Task<int> AttachToUserAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        /*
          Only a run nobody owns, and the expiry is left exactly where it is.

          Two things this deliberately is not. It is not ClaimAsync: that one belongs to a paid
          book and clears the expiry, and a preview that has been looked at is still a preview —
          keeping the row forever would keep a child's photograph forever with it. And it is not
          an update by id alone: the id is a bearer token, so the WHERE is what stops a second
          account taking over a run the first one is already watching. A run that is already this
          parent's matches too, which makes a repeated call free rather than a failure.
        */
        const string sql = """
                           UPDATE dbo.MasterStoryRuns
                           SET UserId = @UserId, UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id AND (UserId IS NULL OR UserId = @UserId);
                           """;

        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<MasterStoryRunSummary>> ListUnboughtForUserAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        /*
          The previews this parent has not bought, newest first.

          `PackId IS NULL` is the whole definition of unbought: a run that became a book is that
          book now, and the shelf already shows it. The expiry filter is not decoration either —
          the purge runs on a timer, so a run whose hour has passed can still be sitting in the
          table, and listing it would offer a card that leads to a story already being deleted.

          The title comes out through JSON_VALUE rather than by reading ContentJson: that column
          holds the whole book, and this is a list. Guarded by ISJSON because JSON_VALUE does not
          merely return null on text it cannot parse - it raises, and one malformed row would
          take the whole of a parent's shelf with it.
        */
        const string sql = """
                           SELECT TOP (@Limit)
                               Id, CharacterId, Status, ProgressMessage, ErrorMessage, ChildName, Theme,
                               CoverImageUrl,
                               CASE WHEN ISJSON(ContentJson) = 1
                                    THEN JSON_VALUE(ContentJson, '$.title') END AS Title,
                               CreatedAt, ExpiresAt
                           FROM dbo.MasterStoryRuns
                           WHERE UserId = @UserId
                             AND PackId IS NULL
                             AND (ExpiresAt IS NULL OR ExpiresAt > SYSUTCDATETIME())
                           ORDER BY CreatedAt DESC;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<MasterStoryRunSummary>(new CommandDefinition(
            sql, new { UserId = userId, Limit = limit }, cancellationToken: cancellationToken));
        return rows.AsList();
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

    public async Task<IReadOnlyList<StaleMasterStoryRun>> ListStaleAsync(
        DateTime cutoffUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        // Three columns rather than the row: a run carries two whole books in StoryJson and
        // ContentJson, and the sweep reads every quiet row on the table every five minutes.
        const string sql = """
                           SELECT TOP (@Limit) Id, Status, UpdatedAt
                           FROM dbo.MasterStoryRuns
                           WHERE Status IN @Statuses
                             AND UpdatedAt < @CutoffUtc
                           ORDER BY UpdatedAt;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<StaleMasterStoryRun>(new CommandDefinition(
            sql,
            new { Limit = limit, CutoffUtc = cutoffUtc, Statuses = StaleStatuses },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    /// <summary>
    /// The run half of the same rule: still in the status the sweep saw, and still as quiet.
    ///
    /// Every write in this repository stamps UpdatedAt, so a job that saved its prompts or its
    /// story between the sweep's read and its write moves the row forward without changing its
    /// status — and a status-only compare-and-set would fail a run that had just proved it was
    /// alive. Repeating the cutoff in the predicate is what makes the verdict conditional on the
    /// silence that justified it.
    /// </summary>
    public async Task<bool> TryFailStaleAsync(
        Guid id,
        string expectedStatus,
        DateTime cutoffUtc,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.MasterStoryRuns
                           SET Status = @Status, ErrorMessage = @ErrorMessage,
                               UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id
                             AND Status = @ExpectedStatus
                             AND UpdatedAt < @CutoffUtc;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = id,
                ExpectedStatus = expectedStatus,
                CutoffUtc = cutoffUtc,
                Status = MasterStoryRunStatus.Failed,
                ErrorMessage = Truncate(errorMessage, 1000)
            },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
