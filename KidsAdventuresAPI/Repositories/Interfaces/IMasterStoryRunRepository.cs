using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IMasterStoryRunRepository
{
    Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken);

    Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

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

    /// <summary>Removes guest runs whose expiry has passed. Returns how many went.</summary>
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken);
}
