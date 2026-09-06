using System.Collections.Concurrent;
using Hangfire;
using Hangfire.Storage;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// One book, one operation at a time — whoever is asking, and whichever way in they came.
///
/// Three things mutate a finished book's live blobs: the fulfilment job, print re-preparation
/// (with stored-art recovery inside it) and an operator's redraw. Hangfire's
/// <see cref="DisableConcurrentExecutionAttribute"/> serialises the first against itself, but it
/// is a server filter: it runs when a Hangfire worker performs a job, and it sees nothing at all
/// of the other two, which the admin controller calls straight through on the request thread. So
/// a re-preparation could be halfway through building a press candidate while a redraw deleted
/// the very spreads it was laying out, and whichever finished last would publish files describing
/// a book that no longer existed — or restore a print URL for artwork that had just been deleted.
///
/// This is the seam the two inline callers take so that all three end up behind ONE gate. The
/// production implementation deliberately locks the same resource name the job attribute locks,
/// so "the job is running" and "an operator is re-preparing" are the same fact to everybody who
/// asks.
/// </summary>
public interface IBekiPackLock
{
    /// <summary>
    /// Takes the book's lock, or returns null when somebody else holds it.
    ///
    /// Null rather than an exception because every caller has its own way of saying no — one
    /// throws a sentence at an admin endpoint, the other returns a refusal record — and a shared
    /// exception type would only be caught and translated at both ends.
    /// </summary>
    /// <param name="wait">
    /// How long to wait for a holder to finish. <see cref="TimeSpan.Zero"/> is what the inline
    /// callers pass: a person who clicked a button wants to be told the book is busy, not to hold
    /// a request thread open for the several minutes a press stage takes.
    /// </param>
    Task<IAsyncDisposable?> TryAcquireAsync(Guid packId, TimeSpan wait, CancellationToken cancellationToken);
}

/// <summary>The name every holder of a book's lock agrees on.</summary>
public static class BekiPackLockResource
{
    /// <summary>
    /// The pattern <see cref="DisableConcurrentExecutionAttribute"/> is given on
    /// <see cref="IBekiPackFulfillment.ProcessAsync"/>. Hangfire formats it with the job's
    /// arguments — <c>String.Format(pattern, job.Args)</c> — so <c>{0}</c> is the pack id.
    /// </summary>
    public const string Pattern = "beki-pack:{0}";

    /// <summary>
    /// The resource an inline caller must lock to be visible to the job, and vice versa:
    /// <c>beki-pack:&lt;pack id&gt;</c>, the pack id in Guid's default hyphenated form — exactly
    /// what <see cref="Pattern"/> formats to for the same book.
    ///
    /// Written here rather than inlined at the two call sites because a lock whose name drifts by
    /// one character stops being a lock and becomes an expensive no-op that nothing reports.
    /// </summary>
    public static string For(Guid packId) => string.Format(
        System.Globalization.CultureInfo.InvariantCulture, Pattern, packId);
}

/// <summary>
/// The deployed lock: Hangfire's own distributed lock, on the same row the job's
/// <see cref="DisableConcurrentExecutionAttribute"/> takes.
///
/// Sharing the resource name is the whole point. The attribute acquires it through the job's
/// storage connection; this acquires it through a connection of its own, and the storage backend
/// sees two contenders for one named lock — so the job waits its stated sixty seconds for an
/// operator's re-preparation to finish, and an operator is refused instantly while a worker is
/// drawing. It also survives more than one web process, which an in-process semaphore cannot:
/// two app-service instances behind one load balancer are two admins' clicks in two memories.
/// </summary>
public sealed class HangfireBekiPackLock : IBekiPackLock
{
    /// <inheritdoc />
    public Task<IAsyncDisposable?> TryAcquireAsync(
        Guid packId, TimeSpan wait, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A connection per holder, disposed with the lock: Hangfire's locks are owned by the
        // connection that took them, and returning one to the pool while its lock is still held is
        // how a lock outlives the operation it was protecting.
        var connection = JobStorage.Current.GetConnection();

        try
        {
            var held = connection.AcquireDistributedLock(BekiPackLockResource.For(packId), wait);
            return Task.FromResult<IAsyncDisposable?>(new Holder(held, connection));
        }
        catch (DistributedLockTimeoutException)
        {
            // Somebody else has the book. Not an error: it is the answer.
            connection.Dispose();
            return Task.FromResult<IAsyncDisposable?>(null);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private sealed class Holder(IDisposable held, IStorageConnection connection) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            try
            {
                held.Dispose();
            }
            finally
            {
                connection.Dispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// The fallback: one semaphore per book, in this process.
///
/// It is what a test harness and a single-process run get, and it is honest about its limit — it
/// serialises the callers inside one process and knows nothing about a second one. That is enough
/// for the suites, which have no Hangfire storage to lock against, and enough for a single-instance
/// deployment; the registered implementation is <see cref="HangfireBekiPackLock"/>.
///
/// The dictionary is static so that two callers constructing their own default — the fulfilment
/// service and the regeneration service each fall back to one — still meet on the same semaphore.
/// A per-instance dictionary would give each of them a private gate and quietly restore exactly
/// the race this type exists to close.
///
/// Entries are never removed, deliberately. Removing one on the way out races a caller that has
/// already taken the same instance: a third arrival would build its own semaphore and run beside
/// the second. A dictionary of idle semaphores keyed by the books somebody has operated on is a
/// few hundred bytes per book and is bounded by how often a person clicks a button.
/// </summary>
public sealed class InProcessBekiPackLock : IBekiPackLock
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    /// <inheritdoc />
    public async Task<IAsyncDisposable?> TryAcquireAsync(
        Guid packId, TimeSpan wait, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd(packId, _ => new SemaphoreSlim(1, 1));

        return await gate.WaitAsync(wait, cancellationToken).ConfigureAwait(false)
            ? new Holder(gate)
            : null;
    }

    /// <summary>Releases once however many times it is disposed — a double release would let two
    /// callers in at once, which is worse than the leak it would be covering.</summary>
    private sealed class Holder(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
