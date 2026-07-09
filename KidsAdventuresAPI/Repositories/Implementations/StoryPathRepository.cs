using System.Text.Json;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class StoryPathRepository(ISqlConnectionFactory connectionFactory) : IStoryPathRepository
{
    private static readonly string[] ReadableStatuses =
    [
        AdventurePackStatus.StoryReady.ToString(),
        AdventurePackStatus.Completed.ToString()
    ];

    public async Task<AdventurePack?> GetLatestReadablePackAsync(Guid childId, ThemeType theme, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP 20
                               Id, UserId, ChildId, Theme, Status, GeneratedJson, PdfUrl, ErrorMessage,
                               OptionalStoryNotes, StoryLanguage, ProgressMessage, PdfCreditCharged,
                               PreviewIllustrationUrl, PreviewIllustrationStatus, PreviewIllustrationUpdatedAt,
                               StoryPageCount, IsWelcomeGiftStory, ChapterIndex, PreviousChapterPackId, CreatedAt
                           FROM AdventurePacks
                           WHERE ChildId = @ChildId
                             AND Theme = @Theme
                             AND Status IN @ReadableStatuses
                           ORDER BY CreatedAt DESC;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AdventurePackRow>(
            new CommandDefinition(
                sql,
                new { ChildId = childId, Theme = theme.ToString(), ReadableStatuses },
                cancellationToken: cancellationToken));

        foreach (var row in rows)
        {
            var pack = MapPack(row);
            if (IsPackReadable(pack))
            {
                return pack;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<StoryPathNodeProgress>> GetNodeProgressAsync(
        Guid childId,
        Guid adventurePackId,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT Id, ChildId, AdventurePackId, Theme, NodeIndex, Status,
                                  CampfirePromptShownAt, ParentConfirmedAt, UpdatedAt
                           FROM StoryPathNodeProgress
                           WHERE ChildId = @ChildId AND AdventurePackId = @AdventurePackId
                           ORDER BY NodeIndex;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<StoryPathNodeProgressRow>(
            new CommandDefinition(sql, new { ChildId = childId, AdventurePackId = adventurePackId }, cancellationToken: cancellationToken));
        return rows.Select(MapProgress).ToList();
    }

    public async Task CreateNodeProgressBatchAsync(IReadOnlyList<StoryPathNodeProgress> rows, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO StoryPathNodeProgress (
                               Id, ChildId, AdventurePackId, Theme, NodeIndex, Status,
                               CampfirePromptShownAt, ParentConfirmedAt, UpdatedAt)
                           VALUES (
                               @Id, @ChildId, @AdventurePackId, @Theme, @NodeIndex, @Status,
                               @CampfirePromptShownAt, @ParentConfirmedAt, @UpdatedAt);
                           """;

        using var connection = connectionFactory.CreateConnection();
        foreach (var row in rows)
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                row.Id,
                row.ChildId,
                row.AdventurePackId,
                Theme = row.Theme.ToString(),
                row.NodeIndex,
                Status = row.Status.ToString(),
                row.CampfirePromptShownAt,
                row.ParentConfirmedAt,
                row.UpdatedAt
            }, cancellationToken: cancellationToken));
        }
    }

    public async Task<bool> UpdateNodeStatusAsync(
        Guid childId,
        Guid adventurePackId,
        int nodeIndex,
        StoryPathNodeStatus status,
        DateTime? parentConfirmedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE StoryPathNodeProgress
                           SET Status = @Status,
                               ParentConfirmedAt = COALESCE(@ParentConfirmedAt, ParentConfirmedAt),
                               CampfirePromptShownAt = COALESCE(CampfirePromptShownAt, SYSUTCDATETIME()),
                               UpdatedAt = SYSUTCDATETIME()
                           WHERE ChildId = @ChildId
                             AND AdventurePackId = @AdventurePackId
                             AND NodeIndex = @NodeIndex;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            ChildId = childId,
            AdventurePackId = adventurePackId,
            NodeIndex = nodeIndex,
            Status = status.ToString(),
            ParentConfirmedAt = parentConfirmedAt
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<string?> GetActiveCampfirePromptAsync(ThemeType theme, int nodeIndex, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP 1 PromptText
                           FROM CampfirePrompts
                           WHERE Theme = @Theme
                             AND NodeIndex = @NodeIndex
                             AND IsActive = 1
                           ORDER BY Version DESC;
                           """;

        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(sql, new { Theme = theme.ToString(), NodeIndex = nodeIndex }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<StoryPathAchievement>> GetAchievementsAsync(Guid childId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT Id, ChildId, Theme, AchievementKey, EarnedAt
                           FROM StoryPathAchievements
                           WHERE ChildId = @ChildId
                           ORDER BY EarnedAt;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<StoryPathAchievementRow>(
            new CommandDefinition(sql, new { ChildId = childId }, cancellationToken: cancellationToken));
        return rows.Select(MapAchievement).ToList();
    }

    public async Task<StoryPathAchievement?> TryAwardAchievementAsync(
        Guid childId,
        ThemeType theme,
        string achievementKey,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           IF NOT EXISTS (
                               SELECT 1 FROM StoryPathAchievements
                               WHERE ChildId = @ChildId AND Theme = @Theme)
                           BEGIN
                               INSERT INTO StoryPathAchievements (Id, ChildId, Theme, AchievementKey, EarnedAt)
                               VALUES (@Id, @ChildId, @Theme, @AchievementKey, @EarnedAt);
                           END

                           SELECT TOP 1 Id, ChildId, Theme, AchievementKey, EarnedAt
                           FROM StoryPathAchievements
                           WHERE ChildId = @ChildId AND Theme = @Theme;
                           """;

        var id = Guid.NewGuid();
        var earnedAt = DateTime.UtcNow;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<StoryPathAchievementRow>(
            new CommandDefinition(sql, new
            {
                Id = id,
                ChildId = childId,
                Theme = theme.ToString(),
                AchievementKey = achievementKey,
                EarnedAt = earnedAt
            }, cancellationToken: cancellationToken));
        return row is null ? null : MapAchievement(row);
    }

    public async Task<bool> HasReadablePackForThemeAsync(Guid childId, ThemeType theme, CancellationToken cancellationToken)
    {
        var pack = await GetLatestReadablePackAsync(childId, theme, cancellationToken);
        return pack is not null;
    }

    public async Task<IReadOnlyList<StoryPathChapter>> GetChaptersAsync(Guid childId, ThemeType theme, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT Id, ChildId, Theme, ChapterIndex, AdventurePackId, Status,
                                  ParentConfirmedAt, CreatedAt, UpdatedAt
                           FROM StoryPathChapters
                           WHERE ChildId = @ChildId AND Theme = @Theme
                           ORDER BY ChapterIndex;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<StoryPathChapterRow>(
            new CommandDefinition(sql, new { ChildId = childId, Theme = theme.ToString() }, cancellationToken: cancellationToken));
        return rows.Select(MapChapter).ToList();
    }

    public async Task CreateChaptersBatchAsync(IReadOnlyList<StoryPathChapter> chapters, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO StoryPathChapters (
                               Id, ChildId, Theme, ChapterIndex, AdventurePackId, Status,
                               ParentConfirmedAt, CreatedAt, UpdatedAt)
                           VALUES (
                               @Id, @ChildId, @Theme, @ChapterIndex, @AdventurePackId, @Status,
                               @ParentConfirmedAt, @CreatedAt, @UpdatedAt);
                           """;
        using var connection = connectionFactory.CreateConnection();
        foreach (var chapter in chapters)
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                chapter.Id,
                chapter.ChildId,
                Theme = chapter.Theme.ToString(),
                chapter.ChapterIndex,
                chapter.AdventurePackId,
                Status = chapter.Status.ToString(),
                chapter.ParentConfirmedAt,
                chapter.CreatedAt,
                chapter.UpdatedAt
            }, cancellationToken: cancellationToken));
        }
    }

    public async Task<bool> SetChapterPackAsync(Guid childId, ThemeType theme, int chapterIndex, Guid adventurePackId, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE StoryPathChapters
                           SET AdventurePackId = @AdventurePackId,
                               UpdatedAt = SYSUTCDATETIME()
                           WHERE ChildId = @ChildId AND Theme = @Theme AND ChapterIndex = @ChapterIndex;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            ChildId = childId,
            Theme = theme.ToString(),
            ChapterIndex = chapterIndex,
            AdventurePackId = adventurePackId
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> UpdateChapterStatusAsync(
        Guid childId,
        ThemeType theme,
        int chapterIndex,
        StoryPathNodeStatus status,
        DateTime? parentConfirmedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE StoryPathChapters
                           SET Status = @Status,
                               ParentConfirmedAt = COALESCE(@ParentConfirmedAt, ParentConfirmedAt),
                               UpdatedAt = SYSUTCDATETIME()
                           WHERE ChildId = @ChildId AND Theme = @Theme AND ChapterIndex = @ChapterIndex;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            ChildId = childId,
            Theme = theme.ToString(),
            ChapterIndex = chapterIndex,
            Status = status.ToString(),
            ParentConfirmedAt = parentConfirmedAt
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    private static bool IsPackReadable(AdventurePack pack)
    {
        if (pack.Status == AdventurePackStatus.Failed)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(pack.GeneratedJson))
        {
            return false;
        }

        AdventureContentDto? content;
        try
        {
            content = JsonSerializer.Deserialize<AdventureContentDto>(pack.GeneratedJson);
        }
        catch
        {
            return false;
        }

        var pages = content?.StoryPages ?? [];
        if (pages.Count == 0)
        {
            return false;
        }

        if (pack.Status == AdventurePackStatus.Completed)
        {
            return true;
        }

        return pack.Status == AdventurePackStatus.StoryReady &&
               pages.All(p => !string.IsNullOrWhiteSpace(p.IllustrationUrl));
    }

    private static AdventurePack MapPack(AdventurePackRow row) => new()
    {
        Id = row.Id,
        UserId = row.UserId,
        ChildId = row.ChildId,
        Theme = Enum.Parse<ThemeType>(row.Theme),
        Status = Enum.Parse<AdventurePackStatus>(row.Status),
        GeneratedJson = row.GeneratedJson,
        PdfUrl = row.PdfUrl,
        ErrorMessage = row.ErrorMessage,
        OptionalStoryNotes = row.OptionalStoryNotes,
        StoryLanguage = row.StoryLanguage,
        ProgressMessage = row.ProgressMessage,
        PdfCreditCharged = row.PdfCreditCharged,
        PreviewIllustrationUrl = row.PreviewIllustrationUrl,
        PreviewIllustrationStatus = Enum.Parse<PreviewIllustrationStatus>(row.PreviewIllustrationStatus),
        PreviewIllustrationUpdatedAt = row.PreviewIllustrationUpdatedAt,
        StoryPageCount = row.StoryPageCount,
        IsWelcomeGiftStory = row.IsWelcomeGiftStory,
        ChapterIndex = row.ChapterIndex,
        PreviousChapterPackId = row.PreviousChapterPackId,
        CreatedAt = row.CreatedAt
    };

    private static StoryPathNodeProgress MapProgress(StoryPathNodeProgressRow row) => new()
    {
        Id = row.Id,
        ChildId = row.ChildId,
        AdventurePackId = row.AdventurePackId,
        Theme = Enum.Parse<ThemeType>(row.Theme),
        NodeIndex = row.NodeIndex,
        Status = Enum.Parse<StoryPathNodeStatus>(row.Status),
        CampfirePromptShownAt = row.CampfirePromptShownAt,
        ParentConfirmedAt = row.ParentConfirmedAt,
        UpdatedAt = row.UpdatedAt
    };

    private static StoryPathAchievement MapAchievement(StoryPathAchievementRow row) => new()
    {
        Id = row.Id,
        ChildId = row.ChildId,
        Theme = Enum.Parse<ThemeType>(row.Theme),
        AchievementKey = row.AchievementKey,
        EarnedAt = row.EarnedAt
    };

    private static StoryPathChapter MapChapter(StoryPathChapterRow row) => new()
    {
        Id = row.Id,
        ChildId = row.ChildId,
        Theme = Enum.Parse<ThemeType>(row.Theme),
        ChapterIndex = row.ChapterIndex,
        AdventurePackId = row.AdventurePackId,
        Status = Enum.Parse<StoryPathNodeStatus>(row.Status),
        ParentConfirmedAt = row.ParentConfirmedAt,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };

    private sealed class AdventurePackRow
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public Guid ChildId { get; init; }
        public string Theme { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? GeneratedJson { get; init; }
        public string? PdfUrl { get; init; }
        public string? ErrorMessage { get; init; }
        public string? OptionalStoryNotes { get; init; }
        public string? StoryLanguage { get; init; }
        public string? ProgressMessage { get; init; }
        public bool PdfCreditCharged { get; init; }
        public string? PreviewIllustrationUrl { get; init; }
        public string PreviewIllustrationStatus { get; init; } = "None";
        public DateTime? PreviewIllustrationUpdatedAt { get; init; }
        public int StoryPageCount { get; init; }
        public bool IsWelcomeGiftStory { get; init; }
        public int? ChapterIndex { get; init; }
        public Guid? PreviousChapterPackId { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    private sealed class StoryPathChapterRow
    {
        public Guid Id { get; init; }
        public Guid ChildId { get; init; }
        public string Theme { get; init; } = string.Empty;
        public int ChapterIndex { get; init; }
        public Guid? AdventurePackId { get; init; }
        public string Status { get; init; } = string.Empty;
        public DateTime? ParentConfirmedAt { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    private sealed class StoryPathNodeProgressRow
    {
        public Guid Id { get; init; }
        public Guid ChildId { get; init; }
        public Guid AdventurePackId { get; init; }
        public string Theme { get; init; } = string.Empty;
        public int NodeIndex { get; init; }
        public string Status { get; init; } = string.Empty;
        public DateTime? CampfirePromptShownAt { get; init; }
        public DateTime? ParentConfirmedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    private sealed class StoryPathAchievementRow
    {
        public Guid Id { get; init; }
        public Guid ChildId { get; init; }
        public string Theme { get; init; } = string.Empty;
        public string AchievementKey { get; init; } = string.Empty;
        public DateTime EarnedAt { get; init; }
    }
}
