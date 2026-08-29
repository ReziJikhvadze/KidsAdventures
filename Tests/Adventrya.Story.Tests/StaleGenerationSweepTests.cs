using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// The sweep that closes a case nothing else can.
///
/// Pack a9f342cc-780f-4b59-ba5b-35f964ec869e stalled after one spread of eight and sat in
/// GeneratingStory permanently, paid for. Every guard in the system was inside the process that had
/// stopped existing: the terminal status is written by the job's own catch, the budget stops a job
/// that is still running, Hangfire requeues a worker that died. None of them applies to a row whose
/// job simply is not there any more, and the stalled-order sweep could not help either — orders are
/// marked fulfilled when generation is enqueued.
///
/// The fakes here answer the way the SQL does, on purpose: the list applies the same predicate
/// (working statuses only, heartbeat falling back to CreatedAt) and the write is a real
/// compare-and-set against in-memory state. A fake that simply returned whatever it was handed
/// would make every one of these tests pass without testing anything.
/// </summary>
public class StaleGenerationSweepTests
{
    private static readonly DateTime Now = new(2026, 8, 29, 12, 00, 00, DateTimeKind.Utc);

    [Fact]
    public async Task A_pack_that_has_been_silent_past_the_budget_and_its_grace_is_failed()
    {
        // Thirty-minute budget, ten-minute grace: forty-five minutes of silence is past both.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-45)));

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(packId));
        Assert.Contains(GenerationBudget.StalledCode, packs.ErrorOf(packId));

        // The number in the message is the silence, not the budget: it is what an operator needs
        // to tell "just over the line" from "this has been dead since yesterday".
        Assert.Contains("45", packs.ErrorOf(packId));

        // Never a requeue. A book that has already spent forty-five minutes and real money is a
        // decision for a person.
        Assert.Equal(1, packs.Writes);
    }

    [Fact]
    public async Task A_pack_still_being_drawn_is_left_alone()
    {
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-12)));

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.GeneratingStory, packs.StatusOf(packId));
        Assert.Equal(0, packs.Writes);
    }

    [Fact]
    public async Task A_pack_that_is_slower_than_the_budget_but_inside_the_grace_is_left_alone()
    {
        // Thirty-five minutes: over its own deadline, under the sweep's. The job is entitled to
        // fail itself first, with a reason of its own — the sweep's verdict is the coarse one and
        // only arrives when nothing else did.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-35)));

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.GeneratingStory, packs.StatusOf(packId));
    }

    [Fact]
    public async Task A_pack_with_no_heartbeat_is_judged_by_when_it_was_created()
    {
        /*
          The whole reason the sweep can reach today's stuck book.

          GenerationHeartbeatUtc arrived after the rows that are already stalled, so every one of
          them carries NULL. Reading that as "no news is good news" would leave exactly the books
          this was written for untouchable, forever.
        */
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(new AdventurePack
        {
            Id = packId,
            Status = AdventurePackStatus.GeneratingStory,
            CreatedAt = Now.AddHours(-3),
            GenerationHeartbeatUtc = null
        });

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(packId));
        Assert.Contains("180", packs.ErrorOf(packId));
    }

    [Fact]
    public async Task A_pack_created_long_ago_but_beating_recently_is_left_alone()
    {
        // The fallback is a fallback. A resumed book can be hours old and perfectly alive, and
        // CreatedAt would condemn it.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(new AdventurePack
        {
            Id = packId,
            Status = AdventurePackStatus.GeneratingStory,
            CreatedAt = Now.AddHours(-3),
            GenerationHeartbeatUtc = Now.AddMinutes(-2)
        });

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.GeneratingStory, packs.StatusOf(packId));
    }

    [Theory]
    [InlineData(AdventurePackStatus.Completed)]
    [InlineData(AdventurePackStatus.Failed)]
    [InlineData(AdventurePackStatus.Pending)]
    [InlineData(AdventurePackStatus.StoryReady)]
    public async Task A_pack_that_is_not_mid_generation_is_never_touched(AdventurePackStatus status)
    {
        // Terminal rows are already answered; Pending has no job yet to be stalled; StoryReady is
        // a book waiting on a person rather than on a process. Failing any of them would be the
        // sweep inventing a problem.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, status, heartbeat: Now.AddDays(-1)));

        await Sweep(packs).SweepAsync();

        Assert.Equal(status, packs.StatusOf(packId));
        Assert.Equal(0, packs.Writes);
    }

    [Fact]
    public async Task A_pack_stuck_halfway_through_its_pdf_is_swept_too()
    {
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingPdf,
            heartbeat: Now.AddMinutes(-90)));

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(packId));
    }

    [Fact]
    public async Task A_job_that_answers_between_the_read_and_the_write_keeps_its_own_verdict()
    {
        /*
          The race the compare-and-set exists for.

          The sweep reads a row, decides it is dead, and in the meantime the job it was about to
          bury finishes and writes Completed. Without the compare-and-set the sweep's Failed lands
          on a finished book — a parent with a PDF in storage, told their book could not be made.
        */
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-45)));

        packs.OnBeforeWrite = () => packs.Force(packId, AdventurePackStatus.Completed);

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.Completed, packs.StatusOf(packId));
        Assert.Null(packs.ErrorOf(packId));
    }

    [Fact]
    public async Task A_job_that_delivers_a_spread_between_the_read_and_the_write_is_not_buried()
    {
        /*
          The race a status-only compare-and-set cannot see.

          The sweep reads a batch and then writes each row in turn. In between, a job that was slow
          rather than dead finishes a spread: that write refreshes the heartbeat and leaves the
          status exactly where it was, so a check on status alone still matches and the sweep fails
          a book that had just proved it was alive — and, with the claim guard on the fulfilment
          job, that book can no longer claim its way back out.

          The write therefore re-tests the silence that justified the verdict.
        */
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-45)));

        // Still GeneratingStory — only the heartbeat moved, which is exactly what delivering a
        // spread does.
        packs.OnBeforeWrite = () => packs.Force(
            packId, AdventurePackStatus.GeneratingStory, heartbeat: Now.AddSeconds(-3));

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.GeneratingStory, packs.StatusOf(packId));
        Assert.Null(packs.ErrorOf(packId));
        Assert.Equal(0, packs.Writes);
    }

    [Fact]
    public async Task A_run_that_writes_something_between_the_read_and_the_write_is_not_buried()
    {
        // The same race on the run table, where every write stamps UpdatedAt: saving the prompts or
        // the story moves the row forward without changing its status.
        var runId = Guid.NewGuid();
        var runs = new FakeRunStore((runId, MasterStoryRunStatus.Writing, Now.AddMinutes(-60)));
        runs.OnBeforeWrite = () => runs.Force(
            runId, MasterStoryRunStatus.Writing, updatedAt: Now.AddSeconds(-3));

        await Sweep(new FakePackStore(), runs).SweepAsync();

        Assert.Equal(MasterStoryRunStatus.Writing, runs.StatusOf(runId));
        Assert.Null(runs.ErrorOf(runId));
    }

    [Fact]
    public async Task The_losing_writer_is_the_job_when_the_sweep_gets_there_first()
    {
        /*
          The same race from the other side, and the reason the fulfilment job's terminal write is
          a compare-and-set as well: once the sweep has recorded why a book was abandoned, a job
          that comes back to life must not quietly overwrite that with Completed and leave nothing
          anywhere saying it took three quarters of an hour.
        */
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-45)));

        await Sweep(packs).SweepAsync();

        var revived = await packs.TryUpdateStatusAsync(
            packId,
            AdventurePackStatus.GeneratingStory,
            AdventurePackStatus.Completed,
            "{}", "pdf", null, CancellationToken.None);

        Assert.False(revived);
        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(packId));
        Assert.Contains(GenerationBudget.StalledCode, packs.ErrorOf(packId));
    }

    [Fact]
    public async Task The_cutoff_follows_the_configured_budget()
    {
        // A deployment that raises the budget raises the sweep's patience with it, in one setting.
        // At sixty minutes, fifty of silence is not yet stale.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-50)));

        await Sweep(packs, budgetMinutes: 60).SweepAsync();
        Assert.Equal(AdventurePackStatus.GeneratingStory, packs.StatusOf(packId));

        // Seventy-one is: sixty plus the ten-minute grace.
        packs.Force(packId, AdventurePackStatus.GeneratingStory, heartbeat: Now.AddMinutes(-71));
        await Sweep(packs, budgetMinutes: 60).SweepAsync();
        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(packId));
    }

    [Fact]
    public async Task Every_stale_pack_in_the_batch_is_closed()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var healthy = Guid.NewGuid();

        var packs = new FakePackStore(
            Pack(first, AdventurePackStatus.GeneratingStory, Now.AddMinutes(-45)),
            Pack(second, AdventurePackStatus.GeneratingPdf, Now.AddMinutes(-200)),
            Pack(healthy, AdventurePackStatus.GeneratingStory, Now.AddMinutes(-1)));

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(first));
        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(second));
        Assert.Equal(AdventurePackStatus.GeneratingStory, packs.StatusOf(healthy));
    }

    [Fact]
    public async Task One_pack_that_will_not_write_does_not_strand_the_rest_of_the_batch()
    {
        // The next book in the list belongs to a different parent.
        var broken = Guid.NewGuid();
        var other = Guid.NewGuid();

        var packs = new FakePackStore(
            Pack(broken, AdventurePackStatus.GeneratingStory, Now.AddMinutes(-45)),
            Pack(other, AdventurePackStatus.GeneratingStory, Now.AddMinutes(-45)))
        {
            ThrowFor = broken
        };

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(other));
    }

    [Fact]
    public async Task A_preview_run_that_stopped_writing_is_failed_by_its_own_timestamp()
    {
        // Runs need no new column: MasterStoryRuns has carried UpdatedAt since it was created and
        // every write in its repository sets it.
        var runId = Guid.NewGuid();
        var runs = new FakeRunStore(
            (runId, MasterStoryRunStatus.Writing, Now.AddMinutes(-60)));

        await Sweep(new FakePackStore(), runs).SweepAsync();

        Assert.Equal(MasterStoryRunStatus.Failed, runs.StatusOf(runId));
        Assert.Contains(GenerationBudget.StalledCode, runs.ErrorOf(runId));
    }

    [Theory]
    [InlineData(MasterStoryRunStatus.Ready)]
    [InlineData(MasterStoryRunStatus.Failed)]
    [InlineData(MasterStoryRunStatus.Pending)]
    public async Task A_run_that_is_not_mid_flight_is_never_touched(string status)
    {
        var runId = Guid.NewGuid();
        var runs = new FakeRunStore((runId, status, Now.AddDays(-2)));

        await Sweep(new FakePackStore(), runs).SweepAsync();

        Assert.Equal(status, runs.StatusOf(runId));
    }

    [Fact]
    public async Task A_run_whose_job_answers_first_keeps_its_own_status()
    {
        var runId = Guid.NewGuid();
        var runs = new FakeRunStore((runId, MasterStoryRunStatus.Writing, Now.AddMinutes(-60)));
        runs.OnBeforeWrite = () => runs.Force(runId, MasterStoryRunStatus.Ready);

        await Sweep(new FakePackStore(), runs).SweepAsync();

        Assert.Equal(MasterStoryRunStatus.Ready, runs.StatusOf(runId));
    }

    [Fact]
    public void The_silence_limit_is_the_budget_plus_a_grace_period()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(40),
            GenerationBudget.SweepSilenceLimit(new BekiOptions { GenerationBudgetMinutes = 30 }));

        // A budget somebody typed wrong falls back to the default rather than sweeping everything
        // in flight on the next pass.
        Assert.Equal(
            TimeSpan.FromMinutes(40),
            GenerationBudget.SweepSilenceLimit(new BekiOptions { GenerationBudgetMinutes = 0 }));
    }

    // -- harness ---------------------------------------------------------

    private static StaleGenerationSweepService Sweep(
        FakePackStore packs,
        FakeRunStore? runs = null,
        int budgetMinutes = 30) =>
        new(packs,
            runs ?? new FakeRunStore(),
            Options.Create(new BekiOptions { GenerationBudgetMinutes = budgetMinutes }),
            NullLogger<StaleGenerationSweepService>.Instance,
            new FixedTimeProvider(Now));

    private static AdventurePack Pack(Guid id, AdventurePackStatus status, DateTime heartbeat) => new()
    {
        Id = id,
        Status = status,
        CreatedAt = heartbeat.AddMinutes(-5),
        GenerationHeartbeatUtc = heartbeat
    };

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    /// <summary>
    /// An in-memory AdventurePacks table that answers the two sweep calls exactly as the SQL does:
    /// the same status predicate, the same NULL-heartbeat fallback, and a real compare-and-set.
    /// Everything else on the interface throws, because the sweep must not be reaching for it.
    /// </summary>
    private sealed class FakePackStore(params AdventurePack[] seed) : IAdventurePackRepository
    {
        private readonly Dictionary<Guid, AdventurePack> _packs = seed.ToDictionary(pack => pack.Id);

        public int Writes { get; private set; }
        public Action? OnBeforeWrite { get; set; }
        public Guid? ThrowFor { get; set; }

        public AdventurePackStatus StatusOf(Guid id) => _packs[id].Status;
        public string? ErrorOf(Guid id) => _packs[id].ErrorMessage;

        public void Force(Guid id, AdventurePackStatus status, DateTime? heartbeat = null)
        {
            _packs[id].Status = status;
            if (heartbeat is { } stamp)
            {
                _packs[id].GenerationHeartbeatUtc = stamp;
            }
        }

        public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(
            DateTime cutoffUtc, int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<StaleGenerationPack> stale = _packs.Values
                .Where(pack => pack.Status is AdventurePackStatus.GeneratingStory
                                          or AdventurePackStatus.GeneratingPdf)
                .Where(pack => (pack.GenerationHeartbeatUtc ?? pack.CreatedAt) < cutoffUtc)
                .OrderBy(pack => pack.GenerationHeartbeatUtc ?? pack.CreatedAt)
                .Take(limit)
                .Select(pack => new StaleGenerationPack(
                    pack.Id, pack.Status, pack.CreatedAt, pack.GenerationHeartbeatUtc))
                .ToList();

            return Task.FromResult(stale);
        }

        /// <summary>
        /// Both halves of the real predicate: the status the sweep saw, and the silence it judged.
        /// A fake that checked only the status would let the interleaved-heartbeat test pass while
        /// the SQL still had the hole.
        /// </summary>
        public Task<bool> TryFailStaleGenerationAsync(
            Guid id, AdventurePackStatus expectedStatus, DateTime cutoffUtc, string errorMessage,
            CancellationToken cancellationToken)
        {
            if (ThrowFor == id)
            {
                throw new InvalidOperationException("the database said no");
            }

            OnBeforeWrite?.Invoke();

            var pack = _packs[id];
            if (pack.Status != expectedStatus
                || (pack.GenerationHeartbeatUtc ?? pack.CreatedAt) >= cutoffUtc)
            {
                return Task.FromResult(false);
            }

            pack.Status = AdventurePackStatus.Failed;
            pack.ErrorMessage = errorMessage;
            Writes++;
            return Task.FromResult(true);
        }

        public Task<bool> TryFailAsync(
            Guid id, AdventurePackStatus expectedStatus, string errorMessage,
            CancellationToken cancellationToken)
        {
            var pack = _packs[id];
            if (pack.Status != expectedStatus)
            {
                return Task.FromResult(false);
            }

            pack.Status = AdventurePackStatus.Failed;
            pack.ErrorMessage = errorMessage;
            return Task.FromResult(true);
        }

        public Task<bool> TryUpdateStatusAsync(
            Guid id, AdventurePackStatus expectedStatus, AdventurePackStatus status,
            string? generatedJson, string? pdfUrl, string? errorMessage,
            CancellationToken cancellationToken)
        {
            var pack = _packs[id];
            if (pack.Status != expectedStatus)
            {
                return Task.FromResult(false);
            }

            pack.Status = status;
            pack.ErrorMessage = errorMessage;
            return Task.FromResult(true);
        }

        public Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(Guid characterId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetAccessLevelAsync(Guid id, BookAccessLevel accessLevel, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateBookPresentationAsync(Guid id, string? title, string? coverImageUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressAsync(Guid id, string? progressMessage, int? progressPercent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdatePreviewIllustrationAsync(Guid id, PreviewIllustrationStatus status, string? illustrationUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryClaimPreviewIllustrationGenerationAsync(Guid id, int staleAfterMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateGeneratedJsonAsync(Guid id, string generatedJson, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>The same, for the two questions the sweep asks of MasterStoryRuns.</summary>
    private sealed class FakeRunStore : IMasterStoryRunSweepStore
    {
        private readonly Dictionary<Guid, (string Status, DateTime UpdatedAt, string? Error)> _runs;

        public FakeRunStore(params (Guid Id, string Status, DateTime UpdatedAt)[] seed) =>
            _runs = seed.ToDictionary(run => run.Id, run => (run.Status, run.UpdatedAt, (string?)null));

        public Action? OnBeforeWrite { get; set; }

        public string StatusOf(Guid id) => _runs[id].Status;
        public string? ErrorOf(Guid id) => _runs[id].Error;

        public void Force(Guid id, string status, DateTime? updatedAt = null) =>
            _runs[id] = (status, updatedAt ?? _runs[id].UpdatedAt, _runs[id].Error);

        public Task<IReadOnlyList<StaleMasterStoryRun>> ListStaleAsync(
            DateTime cutoffUtc, int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<StaleMasterStoryRun> stale = _runs
                .Where(entry => entry.Value.Status is MasterStoryRunStatus.Writing
                                                   or MasterStoryRunStatus.Illustrating)
                .Where(entry => entry.Value.UpdatedAt < cutoffUtc)
                .OrderBy(entry => entry.Value.UpdatedAt)
                .Take(limit)
                .Select(entry => new StaleMasterStoryRun(entry.Key, entry.Value.Status, entry.Value.UpdatedAt))
                .ToList();

            return Task.FromResult(stale);
        }

        public Task<bool> TryFailStaleAsync(
            Guid id, string expectedStatus, DateTime cutoffUtc, string errorMessage,
            CancellationToken cancellationToken)
        {
            OnBeforeWrite?.Invoke();

            if (_runs[id].Status != expectedStatus || _runs[id].UpdatedAt >= cutoffUtc)
            {
                return Task.FromResult(false);
            }

            _runs[id] = (MasterStoryRunStatus.Failed, _runs[id].UpdatedAt, errorMessage);
            return Task.FromResult(true);
        }
    }
}
