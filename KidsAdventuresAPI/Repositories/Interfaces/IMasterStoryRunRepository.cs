using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IMasterStoryRunRepository
{
    Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken);

    Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Status, progress and the cover, without the story.
    ///
    /// The browser asks this every few seconds for the several minutes a book takes. GetById
    /// reads the whole row, and two of its columns are NVARCHAR(MAX) holding the entire book —
    /// so every poll was dragging a finished story out of SQL to look at one short string.
    /// </summary>
    Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Records progress copy without touching anything the job has already written.</summary>
    Task SetProgressAsync(Guid id, string status, string? progressMessage, CancellationToken cancellationToken);

    /// <summary>
    /// Records the prompts before the call is made, so a run that fails or times out still says
    /// what it asked for. Reconstructing them afterwards would only show what we would ask now.
    /// </summary>
    Task SavePromptsAsync(
        Guid id,
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken);

    /// <summary>Stores what the model returned, together with what it cost.</summary>
    Task SaveStoryAsync(
        Guid id,
        string storyJson,
        string contentJson,
        int promptTokens,
        int completionTokens,
        CancellationToken cancellationToken);

    Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken);

    Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken);

    Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken);

    /// <summary>
    /// Attaches a finished run to the account that signed up for it, and clears the expiry so it
    /// is no longer treated as a guest's temporary row.
    /// </summary>
    Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken);

    /// <summary>
    /// Guest runs whose expiry has passed, with the blobs they own.
    ///
    /// Listed before deleting rather than deleted outright, because a row is not the only thing
    /// an expired run leaves behind: the portrait it was given is a photograph of a named child,
    /// and dropping the row is what makes it unreachable rather than gone.
    /// </summary>
    Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(int limit, CancellationToken cancellationToken);

    Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken);
}
