using AdventurePacks.Api.DTOs.StoryPath;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IStoryGraphAuthoringService
{
    Task<IReadOnlyList<StoryGraphPathDto>> ListPathsAsync(string? theme, CancellationToken cancellationToken);
    Task<StoryGraphDetailResponse?> GetPathDetailAsync(Guid pathId, CancellationToken cancellationToken);
    Task<StoryGraphPlayResponse?> GetActiveGraphForPlayAsync(ThemeType theme, Guid? childId, CancellationToken cancellationToken);
    Task<StoryGraphPathDto> CreatePathAsync(CreateStoryGraphPathRequest request, CancellationToken cancellationToken);
    Task<StoryGraphPathDto?> UpdatePathAsync(Guid pathId, UpdateStoryGraphPathRequest request, CancellationToken cancellationToken);
    Task<bool> PublishPathAsync(Guid pathId, CancellationToken cancellationToken);
    Task<StoryGraphNodeDto> CreateNodeAsync(Guid pathId, UpsertStoryGraphNodeRequest request, CancellationToken cancellationToken);
    Task<StoryGraphNodeDto?> UpdateNodeAsync(Guid pathId, Guid nodeId, UpsertStoryGraphNodeRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteNodeAsync(Guid pathId, Guid nodeId, CancellationToken cancellationToken);
    Task<StoryGraphChoiceDto> CreateChoiceAsync(Guid pathId, UpsertStoryGraphChoiceRequest request, CancellationToken cancellationToken);
    Task<StoryGraphChoiceDto?> UpdateChoiceAsync(Guid pathId, Guid choiceId, UpsertStoryGraphChoiceRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteChoiceAsync(Guid pathId, Guid choiceId, CancellationToken cancellationToken);
    Task<StoryGraphDetailResponse> SeedLinearGraphAsync(ThemeType theme, CancellationToken cancellationToken);
}
