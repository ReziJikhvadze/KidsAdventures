namespace AdventurePacks.Api.Services.Interfaces;

public interface ISeriesMemoryService
{
    /// <summary>
    /// The series memory rendered for a story prompt, or <c>null</c> for a first book. Never
    /// throws: a book must still be writable when memory is unavailable.
    /// </summary>
    Task<string?> GetPromptMemoryAsync(Guid seriesId, CancellationToken cancellationToken);

    /// <summary>
    /// Folds a finished book into its series memory. Called after the story text exists, so a
    /// later book can reuse the companions and moments this one introduced. Best-effort — a
    /// failure here must never fail the book that triggered it.
    /// </summary>
    Task RecordBookAsync(
        AdventurePack book,
        string storyJson,
        string heroName,
        CancellationToken cancellationToken);
}
