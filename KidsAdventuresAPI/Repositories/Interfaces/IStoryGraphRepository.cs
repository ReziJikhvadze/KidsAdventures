namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IStoryGraphRepository
{
    Task<StoryPathGraph?> GetActivePathAsync(ThemeType theme, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoryPathGraph>> ListPathsAsync(ThemeType? theme, CancellationToken cancellationToken);
    Task<StoryPathGraph?> GetPathByIdAsync(Guid pathId, CancellationToken cancellationToken);
    Task<Guid> CreatePathAsync(StoryPathGraph path, CancellationToken cancellationToken);
    Task<bool> UpdatePathAsync(StoryPathGraph path, CancellationToken cancellationToken);
    Task<bool> SetStartNodeAsync(Guid pathId, Guid startNodeId, CancellationToken cancellationToken);
    Task<bool> PublishPathAsync(Guid pathId, ThemeType theme, CancellationToken cancellationToken);
    Task<bool> DeactivatePathsForThemeAsync(ThemeType theme, Guid exceptPathId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoryGraphNode>> GetNodesAsync(Guid pathId, CancellationToken cancellationToken);
    Task<StoryGraphNode?> GetNodeByIdAsync(Guid pathId, Guid nodeId, CancellationToken cancellationToken);
    Task<Guid> CreateNodeAsync(StoryGraphNode node, CancellationToken cancellationToken);
    Task<bool> UpdateNodeAsync(StoryGraphNode node, CancellationToken cancellationToken);
    Task<bool> DeleteNodeAsync(Guid pathId, Guid nodeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoryGraphChoice>> GetChoicesAsync(Guid pathId, CancellationToken cancellationToken);
    Task<StoryGraphChoice?> GetChoiceByIdAsync(Guid pathId, Guid choiceId, CancellationToken cancellationToken);
    Task<Guid> CreateChoiceAsync(StoryGraphChoice choice, CancellationToken cancellationToken);
    Task<bool> UpdateChoiceAsync(StoryGraphChoice choice, CancellationToken cancellationToken);
    Task<bool> DeleteChoiceAsync(Guid pathId, Guid choiceId, CancellationToken cancellationToken);

    Task<StoryPathGraphProgress?> GetProgressAsync(Guid childId, Guid pathId, CancellationToken cancellationToken);
    Task UpsertProgressAsync(StoryPathGraphProgress progress, CancellationToken cancellationToken);
}
