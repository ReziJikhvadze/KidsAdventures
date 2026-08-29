namespace AdventurePacks.Api.Repositories.Interfaces;

/// <summary>One preview run whose writing job has stopped saying anything.</summary>
public sealed record StaleMasterStoryRun(Guid Id, string Status, DateTime UpdatedAt);

/// <summary>
/// The two questions the stale-generation sweep asks of the run table, and nothing else.
///
/// Deliberately not part of <see cref="IMasterStoryRunRepository"/>. That interface is the run's
/// request-path contract — start a run, poll it, claim it, finish it — and it is implemented by
/// test doubles that have no business growing an operational sweep's methods. The sweep is the
/// only caller either method will ever have, and it needs exactly two things the request path
/// never asks for: which rows have gone quiet, and a status write that loses rather than wins when
/// somebody else moved the row first.
///
/// <see cref="MasterStoryRun.UpdatedAt"/> is what "quiet" is measured by. The runs already carry
/// it and every write in the repository sets it, so unlike a pack there is no new column here.
/// </summary>
public interface IMasterStoryRunSweepStore
{
    /// <summary>
    /// Runs still in a working status whose <c>UpdatedAt</c> is older than the cutoff.
    /// </summary>
    Task<IReadOnlyList<StaleMasterStoryRun>> ListStaleAsync(
        DateTime cutoffUtc,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Compare-and-set: fails the run only while it is still in <paramref name="expectedStatus"/>
    /// and its <c>UpdatedAt</c> is still older than <paramref name="cutoffUtc"/> — the same cutoff
    /// the listing used. False means it moved on under the sweep's feet: either its status changed,
    /// or the job it was about to declare dead wrote something and is plainly alive. Both mean the
    /// sweep defers to what is stored.
    /// </summary>
    Task<bool> TryFailStaleAsync(
        Guid id,
        string expectedStatus,
        DateTime cutoffUtc,
        string errorMessage,
        CancellationToken cancellationToken);
}
