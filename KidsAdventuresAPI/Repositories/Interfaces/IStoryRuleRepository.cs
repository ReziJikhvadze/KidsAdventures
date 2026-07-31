namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IStoryRuleRepository
{
    Task<IReadOnlyList<StoryRule>> GetAllAsync(CancellationToken cancellationToken);

    Task<StoryRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The rule that applies to a book: the exact age-band x theme cell when one is set,
    /// otherwise the theme-wide row for that band, otherwise null.
    /// </summary>
    Task<StoryRule?> ResolveAsync(string ageBand, string theme, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(StoryRule rule, CancellationToken cancellationToken);
}
