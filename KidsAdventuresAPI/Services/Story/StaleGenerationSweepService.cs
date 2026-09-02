using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Story;

public interface IStaleGenerationSweepService
{
    /// <summary>
    /// Closes the cases nothing else will. Hangfire calls it on a schedule, so it takes no
    /// arguments — same shape as the three sweeps beside it.
    /// </summary>
    Task SweepAsync();
}

/// <summary>
/// The backstop for a generation job that stopped existing.
///
/// Every other guard in this system is inside a running process: the job's own catch writes the
/// terminal status, the wall-clock budget stops a job that has hung, Hangfire requeues a job whose
/// worker died. All of them assume a process. Pack a9f342cc-780f-4b59-ba5b-35f964ec869e is what
/// happens when that assumption fails — it stopped after one spread of eight and sat in
/// GeneratingStory permanently, paid for, with a progress bar frozen at 20%, because the only
/// thing that ever wrote Failed was a catch block in a process that no longer existed. The
/// stalled-order sweep could not reach it either: orders are marked fulfilled when generation is
/// enqueued, not when it finishes.
///
/// So this reads the rows themselves and asks one question of each: has anything at all been
/// written to this book since it should have been finished? A pack answers with its generation
/// heartbeat — refreshed by the claim, by every status write and by every delivered spread — or,
/// when that column is null because the row predates it, with CreatedAt, which is the only reason
/// the books that are already stuck can be reached at all. A preview run answers with UpdatedAt,
/// which it has always had.
///
/// What it does about it is deliberately small. It fails the row, with a reason, tells the family
/// whose book it was, and stops. No requeue: a book that has been silent for the whole budget plus
/// a grace period has already spent forty minutes and real money, and starting it again on a timer
/// — with nobody having looked at why — is how one broken input becomes an unbounded bill. A person
/// decides whether to retry.
///
/// The letter is new, and it closes the gap this class used to open. Every other terminal write is
/// made by a job that also pages an operator and now writes to the parent; the sweep's was made
/// from outside, silently, so the one book nobody was watching was also the one nobody was told
/// about. It says only what <see cref="ParentFacingFailure"/> says — never the stored code.
///
/// Every write is compare-and-set. The sweep is by definition operating on stale information: the
/// job it is about to declare dead may answer between the read and the write, and if it does, the
/// job's own verdict is the true one and this one loses quietly.
/// </summary>
public sealed class StaleGenerationSweepService(
    IAdventurePackRepository packRepository,
    IMasterStoryRunSweepStore runStore,
    IEmailService emailService,
    IUserRepository userRepository,
    IOptions<BekiOptions> bekiOptions,
    ILogger<StaleGenerationSweepService> logger,
    TimeProvider? timeProvider = null,
    IBekiAlarmService? alarms = null,
    IAdminNotifier? adminNotifier = null) : IStaleGenerationSweepService
{
    /// <summary>
    /// How many rows one pass will close. A cap rather than a page: if there are ever more than
    /// this many stalled books at once, something is wrong that a sweep should not be papering
    /// over at speed, and the next pass is five minutes away.
    /// </summary>
    private const int BatchLimit = 50;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task SweepAsync()
    {
        var silenceLimit = GenerationBudget.SweepSilenceLimit(bekiOptions.Value);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cutoffUtc = now - silenceLimit;

        var packs = await SweepPacksAsync(cutoffUtc, now);
        var runs = await SweepRunsAsync(cutoffUtc, now);
        var unclaimed = await ReportUnclaimedAsync(cutoffUtc, now);

        if (packs > 0 || runs > 0 || unclaimed > 0)
        {
            logger.LogWarning(
                "Stale-generation sweep: failed {Packs} pack(s) and {Runs} preview run(s) that had "
                + "been silent for more than {Minutes} minutes, and reported {Unclaimed} pack(s) no "
                + "job has claimed in that time. Nothing was requeued.",
                packs, runs, silenceLimit.TotalMinutes, unclaimed);
        }
    }

    /// <summary>The alarm a book raises for having no job, as opposed to a job that died.</summary>
    public const string UnclaimedCode = "GENERATION_UNCLAIMED";

    /// <summary>
    /// The books nothing has claimed: still Pending, or a Beki pack still at StoryReady, past the
    /// same silence limit — and this pass writes nothing to them.
    ///
    /// Not buried, deliberately, and the reason is the queue. Those two statuses are where a pack
    /// waits for Hangfire to reach it, and that wait is unbounded by design: eight paid books
    /// drawing at eleven minutes each put the ninth well past any silence limit while it is
    /// perfectly healthy. Failing it would tell a family their book broke because other families'
    /// books were ahead of it. But a job that was never posted, or that died before its claim,
    /// leaves exactly the same row — and until now that row was invisible to everything: not
    /// Failed, so the console's retry refused it; not a working status, so the pass above never
    /// saw it; and the order already said Fulfilled. A paid book, silently lost.
    ///
    /// So the sweep says so, once. It raises a flag-severity alarm naming the pack, which puts it
    /// on the list an operator works through, and the console's retry accepts precisely this
    /// state now. Once rather than every pass, because a reviewed alarm that is raised again
    /// reopens — and an operator who looked and chose to wait for the queue should not be
    /// re-paged every five minutes for the same book. A person decides; the row is untouched.
    /// </summary>
    private async Task<int> ReportUnclaimedAsync(DateTime cutoffUtc, DateTime now)
    {
        IReadOnlyList<StaleGenerationPack> unclaimed;
        try
        {
            unclaimed = await packRepository.ListUnclaimedGenerationAsync(
                cutoffUtc, BatchLimit, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stale-generation sweep could not read the unclaimed packs.");
            return 0;
        }

        var reported = 0;
        foreach (var pack in unclaimed)
        {
            var silence = now - pack.LastSignalUtc;

            try
            {
                logger.LogWarning(
                    "Stale-generation sweep: pack {PackId} has sat {Status} for {Minutes:0} minutes "
                    + "with no generation job claiming it. It is not failed — it may only be queued — "
                    + "but a paid book with no job needs a person to look; the retry button accepts it.",
                    pack.Id, pack.Status, silence.TotalMinutes);

                if (await RaiseUnclaimedOnceAsync(pack, silence))
                {
                    reported++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex, "Stale-generation sweep could not report unclaimed pack {PackId}.", pack.Id);
            }
        }

        return reported;
    }

    private async Task<bool> RaiseUnclaimedOnceAsync(StaleGenerationPack pack, TimeSpan silence)
    {
        if (alarms is null)
        {
            return false;
        }

        var existing = await alarms.ListForPackAsync(pack.Id, CancellationToken.None);
        if (existing.Any(alarm => string.Equals(alarm.CheckId, UnclaimedCode, StringComparison.Ordinal)))
        {
            return false;
        }

        var row = await packRepository.GetByIdNoOwnershipAsync(pack.Id, CancellationToken.None);
        if (row is null)
        {
            return false;
        }

        await alarms.RaiseAsync(
            new BekiAlarmRaise(
                pack.Id,
                null,
                row.UserId,
                UnclaimedCode,
                // A flag, not a blocker: nothing about the book is wrong, and it may yet be drawn
                // by a job that is merely behind others. What is wrong is that nobody would know
                // if it were not.
                BekiReleaseSeverity.Flag,
                $"No generation job has claimed this book: it has been {pack.Status} for "
                + $"{silence.TotalMinutes:0} minutes. Nothing was written to it and nothing was "
                + "requeued — it may still be queued behind other books, or the job may be gone. "
                + "The order's retry re-queues it if a person decides it should be.",
                BekiPackBlobs.ManifestName(row.UserId, pack.Id),
                BekiAlarmEvidence.ForAttempt("sweep-unclaimed", pack.Id)),
            CancellationToken.None);

        return true;
    }

    private async Task<int> SweepPacksAsync(DateTime cutoffUtc, DateTime now)
    {
        IReadOnlyList<StaleGenerationPack> stale;
        try
        {
            stale = await packRepository.ListStaleGenerationAsync(
                cutoffUtc, BatchLimit, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A sweep that cannot read is a sweep that does nothing this pass, not a job that
            // fails loudly every five minutes forever.
            logger.LogError(ex, "Stale-generation sweep could not read the packs.");
            return 0;
        }

        var failed = 0;
        foreach (var pack in stale)
        {
            var silence = now - pack.LastSignalUtc;
            var reason = GenerationBudget.StalledReason(silence);

            try
            {
                // The cutoff goes with it. The write repeats the staleness test this listing
                // applied, so a job that delivered a spread in between — refreshing the heartbeat
                // without changing the status — keeps the book it is plainly still drawing.
                var won = await packRepository.TryFailStaleGenerationAsync(
                    pack.Id, pack.Status, cutoffUtc, reason, CancellationToken.None);

                if (won)
                {
                    failed++;
                    logger.LogError(
                        "Stale-generation sweep failed pack {PackId}: it was {Status} and nothing "
                        + "had been written to it for {Minutes:0} minutes (measured from "
                        + "{Source}). A parent paid for this book; it needs a person.",
                        pack.Id, pack.Status, silence.TotalMinutes,
                        pack.HeartbeatMissing ? "CreatedAt, as it has no heartbeat" : "its heartbeat");

                    // "It tells nobody" was the last true thing in this class's own doc comment.
                    // The sweep is the only writer of a terminal status that runs outside the job,
                    // so a book it buries is a book whose owner would otherwise never be told
                    // anything at all — their screen simply stops changing. It still requeues
                    // nothing and decides nothing; it only says so.
                    await TellTheParentAsync(pack.Id, reason);

                    // And the operator, which is the other half of the same gap. A buried book is a
                    // paid book with nothing running behind it; until now it reached a log line and
                    // stopped there, which is the audit's admin blindness in its purest form.
                    await RaiseBurialAsync(pack.Id, reason, silence);
                }
                else
                {
                    // The job answered between the read and the write — by changing the status, or
                    // simply by writing something and refreshing its heartbeat. Either way it is
                    // alive, its verdict is the informed one, and this pass defers to it.
                    logger.LogInformation(
                        "Stale-generation sweep left pack {PackId} alone: it is no longer both "
                        + "{Status} and silent, so the job that owns it is alive after all.",
                        pack.Id, pack.Status);
                }
            }
            catch (Exception ex)
            {
                // One row that will not write must not stop the rest of the batch: the next book
                // in the list is a different parent.
                logger.LogError(ex, "Stale-generation sweep could not fail pack {PackId}.", pack.Id);
            }
        }

        return failed;
    }

    /// <summary>
    /// Tells the owner of a book this sweep just buried, in Georgian and without the code.
    ///
    /// Wrapped whole, and deliberately after the write rather than before it: the verdict is the
    /// sweep's job and the letter is a courtesy, so a mail server that is down must not cost the
    /// row its terminal status — nor stop the next pack in the batch, which belongs to a different
    /// parent. The same reason every method on <see cref="Services.Interfaces.IAdminNotifier"/>
    /// swallows everything.
    /// </summary>
    /// <summary>
    /// Records a burial where an operator will see it, and pages one.
    ///
    /// Both, because they answer different questions. The alarm is the durable record — it names the
    /// pack, survives the log's retention, and sits in a list somebody works through; the page is
    /// what makes a burial something a person hears about today. A burial is a paid book with
    /// nothing running behind it, which is as strong a reason to wake somebody as a failed one.
    ///
    /// The page goes out through the alarm when there is one, and directly when there is not. A
    /// blocker-severity alarm already notifies on its first sighting and on a reopen, and doing it
    /// again here would send two emails about one book — while an alarm service that is absent must
    /// not mean a burial nobody hears about.
    ///
    /// Wrapped whole and after the write, exactly as the letter to the parent is: a monitoring
    /// system that is down must not cost the row its verdict, nor stop the next pack in the batch,
    /// which belongs to a different family.
    /// </summary>
    private async Task RaiseBurialAsync(Guid packId, string reason, TimeSpan silence)
    {
        try
        {
            var pack = await packRepository.GetByIdNoOwnershipAsync(packId, CancellationToken.None);

            if (alarms is not null && pack is not null)
            {
                await alarms.RaiseAsync(
                    new BekiAlarmRaise(
                        packId,
                        null,
                        pack.UserId,
                        GenerationBudget.StalledCode,
                        // A blocker: nothing about this is acceptable-and-recorded. A family paid
                        // for a book and there is no process making it any more.
                        BekiReleaseSeverity.Blocker,
                        $"The stale-generation sweep failed this book: it was {pack.Status} and "
                        + $"nothing had been written to it for {silence.TotalMinutes:0} minutes. "
                        + "Nothing was requeued — a person decides whether it is retried. "
                        + $"Stored reason: {reason}",
                        BekiPackBlobs.ManifestName(pack.UserId, packId),
                        BekiAlarmEvidence.ForAttempt("sweep-burial", packId)),
                    CancellationToken.None);
            }

            if (adminNotifier is not null && (alarms is null || pack is null))
            {
                await adminNotifier.BookFailedAsync(packId, reason, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Stale-generation sweep buried pack {PackId} but could not raise the alarm.",
                packId);
        }
    }

    private async Task TellTheParentAsync(Guid packId, string reason)
    {
        try
        {
            var pack = await packRepository.GetByIdNoOwnershipAsync(packId, CancellationToken.None);
            if (pack is null)
            {
                return;
            }

            var user = await userRepository.GetByIdAsync(pack.UserId, CancellationToken.None);
            if (user is null || string.IsNullOrWhiteSpace(user.Email))
            {
                logger.LogWarning(
                    "Stale-generation sweep failed pack {PackId} but its owner has no email "
                    + "address on file, so nobody outside the logs has been told.", packId);
                return;
            }

            await emailService.SendBookFailedAsync(
                user.Email,
                null,
                pack.Title,
                ParentFacingFailure.ToParentMessage(reason),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Stale-generation sweep could not tell the owner of pack {PackId}.", packId);
        }
    }

    private async Task<int> SweepRunsAsync(DateTime cutoffUtc, DateTime now)
    {
        IReadOnlyList<StaleMasterStoryRun> stale;
        try
        {
            stale = await runStore.ListStaleAsync(cutoffUtc, BatchLimit, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stale-generation sweep could not read the preview runs.");
            return 0;
        }

        var failed = 0;
        foreach (var run in stale)
        {
            var silence = now - run.UpdatedAt;
            var reason = GenerationBudget.StalledReason(silence);

            try
            {
                var won = await runStore.TryFailStaleAsync(
                    run.Id, run.Status, cutoffUtc, reason, CancellationToken.None);

                if (won)
                {
                    failed++;

                    // A preview costs nobody money, so this is a warning rather than an error —
                    // but it is still a browser that has been told to keep waiting, and telling it
                    // the truth is the whole point.
                    logger.LogWarning(
                        "Stale-generation sweep failed preview run {RunId}: it was {Status} and "
                        + "had not been touched for {Minutes:0} minutes.",
                        run.Id, run.Status, silence.TotalMinutes);
                }
                else
                {
                    logger.LogInformation(
                        "Stale-generation sweep left preview run {RunId} alone: it is no longer "
                        + "both {Status} and silent.", run.Id, run.Status);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Stale-generation sweep could not fail preview run {RunId}.", run.Id);
            }
        }

        return failed;
    }
}
