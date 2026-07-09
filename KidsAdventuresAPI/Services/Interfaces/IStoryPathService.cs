using AdventurePacks.Api.DTOs.StoryPath;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IStoryPathService
{
    Task<StoryPathOverviewResponse> GetOverviewAsync(Guid userId, Guid childId, CancellationToken cancellationToken);
    Task<StoryPathWorldResponse?> GetWorldAsync(Guid userId, Guid childId, ThemeType theme, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoryPathAchievementDto>> GetAchievementsAsync(Guid userId, Guid childId, CancellationToken cancellationToken);
    Task<ConfirmCampfireResponse?> ConfirmCampfireAsync(Guid userId, ConfirmCampfireRequest request, CancellationToken cancellationToken);
    Task<GenerateChapterResponse> GenerateChapterAsync(Guid userId, ThemeType theme, int chapterIndex, GenerateChapterRequest request, CancellationToken cancellationToken);
    Task<CompleteChapterResponse?> CompleteChapterAsync(Guid userId, ThemeType theme, int chapterIndex, CompleteChapterRequest request, CancellationToken cancellationToken);
}
