namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IStoryPathRepository
{
    Task<AdventurePack?> GetLatestReadablePackAsync(Guid childId, ThemeType theme, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoryPathNodeProgress>> GetNodeProgressAsync(Guid childId, Guid adventurePackId, CancellationToken cancellationToken);
    Task CreateNodeProgressBatchAsync(IReadOnlyList<StoryPathNodeProgress> rows, CancellationToken cancellationToken);
    Task<bool> UpdateNodeStatusAsync(Guid childId, Guid adventurePackId, int nodeIndex, StoryPathNodeStatus status, DateTime? parentConfirmedAt, CancellationToken cancellationToken);
    Task<string?> GetActiveCampfirePromptAsync(ThemeType theme, int nodeIndex, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoryPathAchievement>> GetAchievementsAsync(Guid childId, CancellationToken cancellationToken);
    Task<StoryPathAchievement?> TryAwardAchievementAsync(Guid childId, ThemeType theme, string achievementKey, CancellationToken cancellationToken);
    Task<bool> HasReadablePackForThemeAsync(Guid childId, ThemeType theme, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoryPathChapter>> GetChaptersAsync(Guid childId, ThemeType theme, CancellationToken cancellationToken);
    Task CreateChaptersBatchAsync(IReadOnlyList<StoryPathChapter> chapters, CancellationToken cancellationToken);
    Task<bool> SetChapterPackAsync(Guid childId, ThemeType theme, int chapterIndex, Guid adventurePackId, CancellationToken cancellationToken);
    Task<bool> UpdateChapterStatusAsync(Guid childId, ThemeType theme, int chapterIndex, StoryPathNodeStatus status, DateTime? parentConfirmedAt, CancellationToken cancellationToken);
}
