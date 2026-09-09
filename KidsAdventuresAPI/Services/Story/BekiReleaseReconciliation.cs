using System.Text.Json;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Story;

/// <summary>What one attempt at reviving a buried book did, and why.</summary>
/// <param name="Restored">Whether the pack is Completed now because of this call.</param>
/// <param name="Outcome">
/// One of <see cref="BekiReconcileOutcomes"/> — a word, so that a caller can branch and a log line
/// can be grepped without reading prose.
/// </param>
public sealed record BekiReconcileResult(bool Restored, string Outcome, string Detail)
{
    public static BekiReconcileResult No(string outcome, string detail) => new(false, outcome, detail);
}

/// <summary>The vocabulary <see cref="BekiReconcileResult.Outcome"/> speaks.</summary>
public static class BekiReconcileOutcomes
{
    public const string Restored = "restored";

    public const string NotFound = "not_found";

    /// <summary>The pack is not Failed, so there is nothing to revive.</summary>
    public const string NotFailed = "not_failed";

    /// <summary>Failed by something other than the sweep. Only the sweep's verdict is reversible.</summary>
    public const string NotTheSweep = "not_the_sweep";

    /// <summary>The book really is incomplete: the sweep killed it mid-run.</summary>
    public const string Incomplete = "incomplete";

    /// <summary>Somebody moved the row between the read and the write.</summary>
    public const string Raced = "raced";

    /// <summary>
    /// Another operation holds the book's lock — a re-preparation, a recovery or a redraw — so this
    /// call looked at nothing and wrote nothing. Not a failure: the next pass will find the book.
    /// </summary>
    public const string Locked = "locked";
}

/// <summary>
/// Which deliverable columns one publication attempt actually wrote.
/// </summary>
/// <param name="CustomerPdf">The parent's download — what an approval is about.</param>
/// <param name="PressFiles">The printer's interior — what loosening a press gate is about.</param>
public readonly record struct BekiPublishOutcome(bool CustomerPdf, bool PressFiles)
{
    /// <summary>Whether this book moved at all, which is what a sweep counts.</summary>
    public bool Anything => CustomerPdf || PressFiles;

    public static BekiPublishOutcome Nothing => default;
}

public interface IBekiReleaseReconciliation
{
    /// <summary>
    /// Revives one book the stale-generation sweep buried, when its artifacts prove it had in fact
    /// finished — amendment B6.
    /// </summary>
    Task<BekiReconcileResult> ReconcilePackAsync(Guid packId, string reason, CancellationToken ct);

    /// <summary>
    /// The same revival, for a caller that is ALREADY holding this book's lock.
    ///
    /// The fulfilment job is one: its <c>DisableConcurrentExecution</c> attribute takes the very
    /// resource <see cref="IBekiPackLock"/> takes, so a revival called from inside the job that
    /// tried to take it again would be told the book is busy — by itself. Passing
    /// <paramref name="lockHeld"/> is how that caller says "the gate is already shut behind me",
    /// and it is the only difference between the two: everything past the acquisition is one body.
    ///
    /// A default implementation so that the hand-written doubles in the suites, which implement the
    /// three-argument form, keep compiling — and so that a double that has no lock at all behaves
    /// exactly as it did.
    /// </summary>
    Task<BekiReconcileResult> ReconcilePackAsync(
        Guid packId, string reason, bool lockHeld, CancellationToken ct) =>
        ReconcilePackAsync(packId, reason, ct);

    /// <summary>
    /// Re-evaluates every Completed Beki book whose download is withheld, under the policy in force
    /// now, and publishes what unlocks — amendment B7. Returns how many were published.
    ///
    /// Each book is visited under its own lock. One that another operation is holding is skipped
    /// and not counted; the next pass will find it.
    /// </summary>
    Task<int> ReconcileWithheldAsync(CancellationToken ct);

    /// <summary>
    /// Writes the URL columns a verdict unlocks, and only those.
    ///
    /// Shared rather than duplicated: the admin approval endpoint, the withheld sweep above and the
    /// fulfilment job's own late publication all have to write the same two columns under the same
    /// compare-and-set, and three copies of that would be three places for the guard to drift out of.
    ///
    /// The answer is per class rather than one boolean because the two callers ask different
    /// questions of it: the approval endpoint wants to know whether the PARENT's file went out (a
    /// press column written for a book nobody signed off is not the thing an approval was about),
    /// and the withheld sweep wants to know whether anything at all moved.
    /// </summary>
    Task<BekiPublishOutcome> PublishUnlockedFilesAsync(
        Domain.Entities.AdventurePack pack, BekiReleaseGateReport report, CancellationToken ct);

    /// <summary>
    /// The same writer, for a caller that already holds the book's lock.
    ///
    /// <see cref="PublishUnlockedFilesAsync"/> takes the lock itself, re-reads the row and the
    /// stored verdict under it, and skips a book somebody else is operating on — which is what an
    /// admin approval and the withheld sweep want. A caller that is already inside the gate must
    /// NOT ask for it a second time: the lock is not re-entrant, and a re-entrant acquisition on a
    /// distributed lock is a deadlock rather than an error.
    ///
    /// A default implementation, so an existing double keeps behaving exactly as it did.
    /// </summary>
    Task<BekiPublishOutcome> PublishUnlockedFilesLockedAsync(
        Domain.Entities.AdventurePack pack, BekiReleaseGateReport report, CancellationToken ct) =>
        PublishUnlockedFilesAsync(pack, report, ct);

    /// <summary>
    /// Raises one alarm per gate the policy waived on a stored verdict — amendment B4. Idempotent by
    /// the alarms' own deduplication, so re-evaluating a book does not multiply its rows.
    /// </summary>
    Task RaiseWaiverAlarmsAsync(
        Guid packId, Guid userId, Guid? orderId, BekiReleaseGateReport report, CancellationToken ct);
}

/// <summary>
/// Why a parent's download is not there, in one word.
///
/// It exists because the alternative was the fault the audit called the download lie: a Completed
/// book with no PDF url fell into "Story must be ready before creating a PDF" — an English sentence,
/// about a state the book is not in, shown raw to somebody who had paid 79 GEL. Answering "review"
/// or "gates" is what lets the download route say a true thing in Georgian instead.
/// </summary>
public interface IBekiDownloadStatusService
{
    /// <summary>
    /// <c>"review"</c> when a person still has to sign the book off, <c>"gates"</c> when a check is
    /// withholding it, and null when nothing here is holding it — including for a legacy book, which
    /// has no gates at all.
    /// </summary>
    Task<string?> DownloadHeldReasonAsync(Guid userId, Guid packId, CancellationToken ct);
}

/// <summary>The two words <see cref="IBekiDownloadStatusService"/> answers with.</summary>
public static class BekiDownloadHeld
{
    public const string Review = "review";

    public const string Gates = "gates";
}

/// <summary>
/// The two things that happen to a book after the job that made it has gone: it is revived, or it is
/// published.
///
/// Both are corrections to the same shape of fault. A finished book was being buried because the
/// only writer that could say "this is finished" had lost a race; a finished book was being withheld
/// because the only reader of the policy was the job that had already ended. Neither had anybody to
/// notice, and the audit's word for that was admin blindness.
///
/// Every write here is compare-and-set for the reason the fulfilment job's are: this runs minutes or
/// months after the job, against a row anything may have touched, and a reconciliation that
/// overwrote a status somebody else wrote would be a worse fault than the one it fixes.
/// </summary>
public sealed class BekiReleaseReconciliation(
    IAdventurePackRepository packRepository,
    IBlobStorageService blobStorage,
    BekiReleaseGates releaseGates,
    IBekiAlarmService alarms,
    ILogger<BekiReleaseReconciliation> logger,
    IBekiReleasePolicyService? policyService = null,
    IBekiPackLock? packLock = null) : IBekiReleaseReconciliation, IBekiDownloadStatusService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The book's one gate, the same one the fulfilment job, a re-preparation and a redraw take.
    ///
    /// This service was outside it, and that was the hole. While a re-preparation runs, the pack
    /// stays Completed and its <c>PrintPdfUrl</c> has been revoked — which is precisely the shape
    /// <c>ListWithheldBekiPacksAsync</c> looks for. So the sweep could pick the book up mid-write,
    /// judge it on evidence that was half the old book and half a candidate about to be thrown
    /// away, and re-publish printing over a re-preparation that had just withheld it. Optional and
    /// defaulted like the other two services' so that every existing construction still compiles.
    /// </summary>
    private readonly IBekiPackLock _packLock = packLock ?? new InProcessBekiPackLock();

    /// <summary>What a log line says about a book somebody else is operating on.</summary>
    private const string SkippedMessage =
        "Beki pack {PackId} skipped: another operation holds its lock.";

    /// <summary>How many withheld books are read from the database at a time.</summary>
    private const int WithheldBatchLimit = 200;

    /// <summary>
    /// How many batches one invocation will walk before it stops — 10,000 books.
    ///
    /// A bound rather than a cap on the WORK, which is the distinction that had gone missing. There
    /// was one batch and no loop, so the two hundredth withheld book was the last one a policy
    /// change could ever reach: the scan re-read the same newest two hundred every time it ran, and
    /// a deployment with three hundred withheld books had a hundred that no operator action would
    /// touch. Now the cursor walks the whole set once and this stops a runaway — it is the number
    /// that must never be hit, not the number that is expected. (Review finding 7.)
    /// </summary>
    private const int WithheldMaxBatches = 50;

    public Task<BekiReconcileResult> ReconcilePackAsync(
        Guid packId, string reason, CancellationToken ct) =>
        ReconcilePackAsync(packId, reason, lockHeld: false, ct);

    public async Task<BekiReconcileResult> ReconcilePackAsync(
        Guid packId, string reason, bool lockHeld, CancellationToken ct)
    {
        /*
          The book's lock, unless the caller is already standing inside it.

          Everything below reads the pack row and the stored evidence and then writes both URL
          columns, which is exactly what a re-preparation is doing to the same book at the same
          moment — and a revival that read the candidate's release-gates.json and then wrote a print
          URL from it would publish files a re-preparation was about to roll back. Zero wait: this
          runs from a Hangfire job and from an operator's click, and neither wants to be queued
          behind a press stage that takes minutes.
        */
        await using var held = lockHeld
            ? null
            : await _packLock.TryAcquireAsync(packId, TimeSpan.Zero, ct);

        if (!lockHeld && held is null)
        {
            logger.LogInformation(SkippedMessage, packId);

            return BekiReconcileResult.No(
                BekiReconcileOutcomes.Locked,
                "another operation is running on this book (print re-preparation, recovery or "
                + "regeneration); nothing was read and nothing was written.");
        }

        var pack = await packRepository.GetByIdNoOwnershipAsync(packId, ct);

        if (pack is null)
        {
            return BekiReconcileResult.No(BekiReconcileOutcomes.NotFound, "no such pack.");
        }

        if (pack.Status != AdventurePackStatus.Failed)
        {
            return BekiReconcileResult.No(
                BekiReconcileOutcomes.NotFailed,
                $"the pack is {pack.Status}, so there is nothing to revive.");
        }

        /*
          ONLY the sweep's verdict is reversible, and it is identified by its own code.

          This is the guard that keeps a Failed→Completed transition from being a way out of every
          failure. A book that stopped on IMAGE_QA_FAILED, or on the asset lock, or on a provider
          exception, failed for a reason a person recorded; reviving it would be claiming a book
          exists because some of its files do. The sweep's failure is different in kind: it says
          "nothing has written to this row for forty minutes", which is a statement about a process,
          and a row whose artifacts are all present is a row where that statement was wrong.
        */
        if (pack.ErrorMessage is not { Length: > 0 } stored
            || !stored.Contains(GenerationBudget.StalledCode, StringComparison.Ordinal))
        {
            return BekiReconcileResult.No(
                BekiReconcileOutcomes.NotTheSweep,
                "this book was failed by something other than the stale-generation sweep; only the "
                + "sweep's verdict is reversible.");
        }

        var missing = await MissingArtifactsAsync(pack, ct);

        if (missing.Count > 0)
        {
            // The sweep killed it mid-run, which is exactly what the sweep is for. It stays failed.
            return BekiReconcileResult.No(
                BekiReconcileOutcomes.Incomplete,
                "the book is genuinely unfinished: " + string.Join(", ", missing) + ".");
        }

        var content = await RebuildContentAsync(pack, ct);

        if (content is null)
        {
            return BekiReconcileResult.No(
                BekiReconcileOutcomes.Incomplete,
                "the stored story could not be rebuilt into a readable book, so there is nothing to "
                + "restore the reader to.");
        }

        var report = await ReadReportAsync(pack.UserId, pack.Id, ct);
        var pdfUrl = report is { CustomerPdfMayPublish: true }
            ? await StoredUrlAsync(BekiPackBlobs.ReadingPdfName(pack.UserId, pack.Id), ct)
            : null;

        if (string.IsNullOrWhiteSpace(pdfUrl))
        {
            return BekiReconcileResult.No(BekiReconcileOutcomes.Incomplete,
                "the canonical PDF is withheld; reconciliation cannot mark the book Completed.");
        }

        var contentJson = JsonSerializer.Serialize(content, JsonOptions);

        var restored = await packRepository.TryUpdateStatusAsync(
            pack.Id,
            AdventurePackStatus.Failed,
            AdventurePackStatus.Completed,
            contentJson,
            pdfUrl,
            // The failure message goes with the failure. The alarm below is what keeps the burial on
            // the record; leaving the sentence on a row that is Completed would put an error on a
            // book the parent can read.
            null,
            ct);

        if (!restored)
        {
            return BekiReconcileResult.No(
                BekiReconcileOutcomes.Raced,
                "the pack is no longer Failed; whoever moved it decides next.");
        }

        await packRepository.UpdateProgressAsync(
            pack.Id, "მზადაა! წიგნი ბიბლიოთეკაშია.", 100, ct);

        if (report is not null)
        {
            // The in-memory row is brought up to date before the shared publisher reads it: it
            // decides by what the pack says, and handing it a stale copy would have it re-publish a
            // column this call has already written, or skip one it has not.
            pack.Status = AdventurePackStatus.Completed;
            pack.PdfUrl = pdfUrl;
            pack.GeneratedJson = contentJson;

            // The locked form: this method is holding the gate already, and asking for it again
            // would be this call telling itself the book is busy.
            await PublishUnlockedFilesLockedAsync(pack, report, ct);
            await RaiseWaiverAlarmsAsync(pack.Id, pack.UserId, null, report, ct);
        }

        /*
          The burial stays on the record — B6 is explicit about it.

          A book that was declared lost and then turned out to be finished is not a non-event. It
          means the fulfilment job took longer than the whole budget plus the grace period and then
          succeeded, which is a real fault in the making of books even though this particular family
          got theirs. So the alarm names the sweep, not the reconciliation.
        */
        await alarms.RaiseAsync(
            new BekiAlarmRaise(
                pack.Id,
                null,
                pack.UserId,
                GenerationBudget.StalledCode,
                BekiReleaseSeverity.Flag,
                "The stale-generation sweep buried this book, and every artifact it needs turned out "
                + $"to be in storage. It has been restored to Completed ({reason}). The job that "
                + "made it went silent for longer than the whole generation budget and then finished.",
                BekiPackBlobs.ManifestName(pack.UserId, pack.Id),
                BekiAlarmEvidence.ForAttempt("sweep-burial", pack.Id)),
            ct);

        logger.LogWarning(
            "Beki pack {PackId} was revived from the stale-generation sweep's verdict: every "
            + "artifact was in storage ({Reason}). The burial is recorded as an alarm.",
            pack.Id, reason);

        return new BekiReconcileResult(true, BekiReconcileOutcomes.Restored, reason);
    }

    public async Task<int> ReconcileWithheldAsync(CancellationToken ct)
    {
        // ONE reading of the policy for the whole scan — B4. A pass that re-read it per book could
        // publish the first fifty under one policy and withhold the rest under another, and a scan
        // that now walks thousands of books has that much more room to be caught mid-change.
        var policy = policyService is null
            ? BekiReleasePolicySnapshot.Defaults
            : await policyService.SnapshotAsync(ct);

        BekiWithheldCursor? cursor = null;
        var published = 0;
        var scanned = 0;
        var batches = 0;

        while (batches < WithheldMaxBatches && !ct.IsCancellationRequested)
        {
            IReadOnlyList<Domain.Entities.AdventurePack> withheld;

            try
            {
                withheld = await packRepository.ListWithheldBekiPacksAsync(
                    WithheldBatchLimit, cursor, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex, "The withheld-book reconciliation could not read the packs; {Published} "
                        + "book(s) had been published by then.", published);
                return published;
            }

            if (withheld.Count == 0)
            {
                break;
            }

            batches++;
            scanned += withheld.Count;

            foreach (var pack in withheld)
            {
                try
                {
                    /*
                      One book's lock, for as long as this book takes, and no waiting.

                      The row was read a moment ago and the sweep is about to judge the book's
                      stored evidence and write its URL columns from the verdict. A re-preparation
                      running on the same book is doing exactly that in the other direction — it
                      revokes the print URL, replaces the evidence, and puts everything back if the
                      verdict refuses — while the pack still reads Completed and therefore still
                      answers the withheld query. Without this the sweep judged whatever half of the
                      replacement had landed and re-published printing over a withholding.

                      Skipping rather than waiting: a book that is busy now will be idle by the next
                      pass, and a pass that queued behind a press stage would hold this scan open
                      for minutes on behalf of one book.
                    */
                    await using var held = await _packLock.TryAcquireAsync(pack.Id, TimeSpan.Zero, ct);

                    if (held is null)
                    {
                        logger.LogInformation(SkippedMessage, pack.Id);
                        continue;
                    }

                    // Re-read under the lock. The row in hand came out of a batch read before the
                    // gate was shut, so whoever was holding the book may have finished writing to
                    // it since — including the very columns this is about to decide on.
                    var current = await packRepository.GetByIdNoOwnershipAsync(pack.Id, ct);

                    if (current is null)
                    {
                        continue;
                    }

                    Adopt(pack, current);

                    var report = await releaseGates.EvaluateAsync(
                        pack.UserId, pack.Id, ct, policy);

                    await blobStorage.UploadAsync(
                        BekiPackBlobs.ReleaseGatesName(pack.UserId, pack.Id),
                        Encoding.UTF8.GetBytes(report.ToJson()),
                        "application/json",
                        ct);

                    await RaiseWaiverAlarmsAsync(pack.Id, pack.UserId, null, report, ct);

                    // Either column counts. A press gate loosened to a flag releases the printer's
                    // interior on a book whose reading copy went out months ago, and reporting that
                    // as "nothing was published" would tell the operator their switch reached nobody.
                    //
                    // The locked form, because the gate above is this loop's own.
                    if ((await PublishUnlockedFilesLockedAsync(pack, report, ct)).Anything)
                    {
                        published++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One book that will not re-evaluate must not stop the pass: the next one belongs
                    // to a different family.
                    logger.LogError(
                        ex, "The withheld-book reconciliation could not re-judge pack {PackId}.",
                        pack.Id);
                }
            }

            // A short batch is the end of the set. A full one leaves the cursor on the last row read,
            // which is a row that may well still be withheld — that is exactly why the next batch is
            // addressed by key and not by offset.
            if (withheld.Count < WithheldBatchLimit)
            {
                break;
            }

            var last = withheld[^1];
            cursor = new BekiWithheldCursor(last.CreatedAt, last.Id);
        }

        if (batches >= WithheldMaxBatches)
        {
            logger.LogWarning(
                "The withheld-book reconciliation stopped at its {Batches}-batch bound with books "
                + "still unread. Something is holding far more books than this deployment should "
                + "have; the next pass resumes from the newest.", WithheldMaxBatches);
        }

        if (scanned > 0)
        {
            logger.LogInformation(
                "The withheld-book reconciliation drained {Scanned} withheld book(s) in {Batches} "
                + "batch(es) and published {Published} of them.", scanned, batches, published);
        }

        return published;
    }

    public async Task<BekiPublishOutcome> PublishUnlockedFilesAsync(
        Domain.Entities.AdventurePack pack, BekiReleaseGateReport report, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(report);

        /*
          The door for callers who are NOT already holding the book: the admin approval endpoint,
          and anything else that judges a book on a request thread.

          The verdict such a caller hands in was computed before this line, outside the gate. If a
          re-preparation finished in between, that verdict describes a book that no longer exists —
          so the row and the stored verdict are read again in here, and the newer one wins.
        */
        await using var held = await _packLock.TryAcquireAsync(pack.Id, TimeSpan.Zero, ct);

        if (held is null)
        {
            logger.LogInformation(SkippedMessage, pack.Id);
            return BekiPublishOutcome.Nothing;
        }

        var current = await packRepository.GetByIdNoOwnershipAsync(pack.Id, ct);

        if (current is null)
        {
            logger.LogWarning(
                "Beki pack {PackId}: the row is gone, so there is nothing to publish to.", pack.Id);
            return BekiPublishOutcome.Nothing;
        }

        Adopt(pack, current);

        // The stored verdict, re-read under the gate. Falling back to the caller's own when there
        // is nothing readable in storage: a book with no release-gates.json is a book judged by
        // whoever is asking, which is what it was before this method took a lock at all.
        var stored = await ReadReportAsync(pack.UserId, pack.Id, ct);

        return await PublishUnlockedFilesLockedAsync(pack, stored ?? report, ct);
    }

    public async Task<BekiPublishOutcome> PublishUnlockedFilesLockedAsync(
        Domain.Entities.AdventurePack pack, BekiReleaseGateReport report, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(report);

        var press = false;

        if (!report.PrintReady && !string.IsNullOrWhiteSpace(pack.PrintPdfUrl))
        {
            await packRepository.UpdatePrintPdfUrlAsync(pack.Id, null, ct);
            pack.PrintPdfUrl = null;
        }

        if (report.PrintReady && report.CustomerPdfMayPublish
            && string.IsNullOrWhiteSpace(pack.PrintPdfUrl))
        {
            var pressName = await PressPdfNameAsync(pack, ct);

            if (await blobStorage.ExistsAsync(pressName, ct)
                && await StoredUrlAsync(pressName, ct) is { } pressUrl)
            {
                await packRepository.UpdatePrintPdfUrlAsync(pack.Id, pressUrl, ct);

                // The in-memory row follows the write, for the reason the revival path does it: a
                // caller that consults this pack again — or calls here twice — must not see a column
                // that has already been written as still empty.
                pack.PrintPdfUrl = pressUrl;
                press = true;
            }
            else
            {
                logger.LogWarning(
                    "Beki pack {PackId}: the printer's file is unlocked but {PressBlob} is not in "
                    + "storage, so the print download stays held.", pack.Id, pressName);
            }
        }

        if (!report.CustomerPdfMayPublish
            || !string.IsNullOrWhiteSpace(pack.PdfUrl)
            || pack.Status != AdventurePackStatus.Completed)
        {
            return new BekiPublishOutcome(false, press);
        }

        var url = await StoredUrlAsync(BekiPackBlobs.ReadingPdfName(pack.UserId, pack.Id), ct);

        if (url is null)
        {
            logger.LogWarning(
                "Beki pack {PackId}: the customer PDF is unlocked but is not in storage, so there "
                + "is nothing to publish.", pack.Id);
            return new BekiPublishOutcome(false, press);
        }

        var published = await packRepository.TryUpdateStatusAsync(
            pack.Id,
            AdventurePackStatus.Completed,
            AdventurePackStatus.Completed,
            pack.GeneratedJson,
            url,
            null,
            ct);

        if (published)
        {
            pack.PdfUrl = url;
        }
        else
        {
            logger.LogWarning(
                "Beki pack {PackId}: the customer PDF was unlocked but the pack is no longer "
                + "Completed, so nothing was published. Whoever moved it decides next.", pack.Id);
        }

        return new BekiPublishOutcome(published, press);
    }

    public async Task<string?> DownloadHeldReasonAsync(
        Guid userId, Guid packId, CancellationToken ct)
    {
        var report = await ReadReportAsync(userId, packId, ct);

        if (report is null)
        {
            // No evaluation: a legacy book, or a Beki book fulfilled before the gates existed.
            // Neither is being held by anything here, and saying so is more honest than inventing a
            // reason for a parent to read.
            return null;
        }

        if (report.CustomerPdfMayPublish)
        {
            return null;
        }

        // "Review" before "gates", because it is the one a parent's wait has an end to: somebody is
        // going to look at the book. A failing gate is a longer story and gets the vaguer sentence.
        return report.AwaitingHumanReview ? BekiDownloadHeld.Review : BekiDownloadHeld.Gates;
    }

    /// <summary>
    /// One alarm per waived gate at publication — amendment B4's second half.
    ///
    /// The pipeline raises its own as it goes; these are the ones nothing else would ever record,
    /// because a gate that fails and does not withhold produces no exception, no failed status and
    /// no log line anybody has a reason to read. Keyed on the gate and the deliverable so that a
    /// book re-evaluated four times leaves four last-seen stamps and not four rows.
    /// </summary>
    public async Task RaiseWaiverAlarmsAsync(
        Guid packId, Guid userId, Guid? orderId, BekiReleaseGateReport report, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);

        foreach (var waiver in report.PolicyWaivers)
        {
            await alarms.RaiseAsync(
                new BekiAlarmRaise(
                    packId,
                    orderId,
                    userId,
                    waiver.CheckId,
                    BekiReleaseSeverity.Flag,
                    $"{waiver.Status} on the {waiver.DeliverableClass} deliverable; "
                    + "this check is waived by policy, not proof of publication. "
                    + $"Customer availability and print readiness are evaluated separately: {waiver.Detail}",
                    BekiPackBlobs.ReleaseGatesName(userId, packId),
                    BekiAlarmEvidence.ForAttempt(waiver.CheckId, waiver.DeliverableClass)),
                ct);
        }
    }

    // ==============================================================================================
    // Reading what is stored
    // ==============================================================================================

    /// <summary>
    /// Brings the caller's copy of the row up to date with the one just read under the lock.
    ///
    /// The caller's instance is updated rather than replaced because it is the object the caller
    /// keeps looking at after this returns — the approval endpoint reads the columns back off it,
    /// and the sweep's batch holds it. Handing back a different object would leave both of them
    /// reading the row as it was before the gate was shut, which is the fault this is closing.
    ///
    /// Only the four columns anything here decides on, and nothing when the repository handed back
    /// the very same instance, which a tracked context and every test double do.
    /// </summary>
    private static void Adopt(
        Domain.Entities.AdventurePack pack, Domain.Entities.AdventurePack current)
    {
        if (ReferenceEquals(pack, current))
        {
            return;
        }

        pack.Status = current.Status;
        pack.PdfUrl = current.PdfUrl;
        pack.PrintPdfUrl = current.PrintPdfUrl;
        pack.GeneratedJson = current.GeneratedJson;
    }

    /// <summary>
    /// Which of a finished book's artifacts are not there. Empty means the book is complete in the
    /// only sense that matters here: every spread the reader serves, and the file the parent
    /// downloads.
    /// </summary>
    private async Task<IReadOnlyList<string>> MissingArtifactsAsync(
        Domain.Entities.AdventurePack pack, CancellationToken ct)
    {
        var missing = new List<string>();

        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            if (!await blobStorage.ExistsAsync(
                    BekiPackBlobs.SpreadName(pack.UserId, pack.Id, spread), ct))
            {
                missing.Add($"spread {spread}");
            }
        }

        if (!await blobStorage.ExistsAsync(BekiPackBlobs.ReadingPdfName(pack.UserId, pack.Id), ct))
        {
            missing.Add("the reading PDF");
        }

        return missing;
    }

    /// <summary>
    /// The reader's book, rebuilt from what the row and storage still hold.
    ///
    /// The story is the one the row already carries — adopted from the preview the parent read and
    /// bought, so it is the right words by construction. What it does not carry is where the
    /// pictures went, because the Beki job writes that only in the same statement that writes
    /// Completed, and this is the case where that statement lost. So each picture page is pointed at
    /// its stored spread, which is exactly what the job's own projection does.
    /// </summary>
    private async Task<AdventureContentDto?> RebuildContentAsync(
        Domain.Entities.AdventurePack pack, CancellationToken ct)
    {
        if (pack.GeneratedJson is not { Length: > 0 } json)
        {
            return null;
        }

        AdventureContentDto? content;

        try
        {
            content = JsonSerializer.Deserialize<AdventureContentDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Beki pack {PackId}: the stored story could not be read.", pack.Id);
            return null;
        }

        if (content is null || content.StoryPages.Count == 0)
        {
            return null;
        }

        var spreadNumber = 0;

        foreach (var page in content.StoryPages)
        {
            if (page.IsTextOnlyPage)
            {
                continue;
            }

            spreadNumber++;

            if (await StoredUrlAsync(
                    BekiPackBlobs.SpreadName(pack.UserId, pack.Id, spreadNumber), ct) is { } url)
            {
                page.IllustrationUrl = url;
            }
        }

        return spreadNumber == BookFormat.SpreadCount ? content : null;
    }

    private async Task<BekiReleaseGateReport?> ReadReportAsync(
        Guid userId, Guid packId, CancellationToken ct)
    {
        var name = BekiPackBlobs.ReleaseGatesName(userId, packId);

        try
        {
            if (!await blobStorage.ExistsAsync(name, ct))
            {
                return null;
            }

            await using var stream = await blobStorage.DownloadAsync(name, ct);
            using var reader = new StreamReader(stream);

            return BekiReleaseGateReport.TryParse(await reader.ReadToEndAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Release gates for pack {PackId} could not be read.", packId);
            return null;
        }
    }

    /// <summary>
    /// Resolve the separate admin-prepared print PDF. Historical unified books retain their
    /// reading artifact only when its integrity record explicitly names print as a consumer.
    /// </summary>
    private async Task<string> PressPdfNameAsync(
        Domain.Entities.AdventurePack pack, CancellationToken ct)
    {
        var interior = BekiPackBlobs.InteriorPdfName(pack.UserId, pack.Id);
        if (await blobStorage.ExistsAsync(interior, ct)) return interior;

        // Only historical unified PDFs may serve both roles. New reading copies explicitly
        // omit print from their consumer list and must never be published by approval alone.
        var integrity = BekiPackBlobs.CanonicalIntegrityName(pack.UserId, pack.Id);
        if (await blobStorage.ExistsAsync(integrity, ct))
        {
            await using var stream = await blobStorage.DownloadAsync(integrity, ct);
            using var record = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (record.RootElement.TryGetProperty("consumers", out var consumers)
                && consumers.ValueKind == JsonValueKind.Array
                && consumers.EnumerateArray().Any(c => c.GetString() == "print"))
                return BekiPackBlobs.ReadingPdfName(pack.UserId, pack.Id);
        }
        return interior;
    }

    /// <summary>
    /// The blob's own stored URL, which is whatever upload returned for it.
    ///
    /// Re-uploading the same bytes is the only way this storage account hands that string back, and
    /// it is cheap next to being wrong: a key assembled by hand reads on one backend and 404s on the
    /// other. The cost is real — nine round trips for a revived book — and it is paid only on the
    /// paths where a book is being rescued or unlocked, which are rare by construction.
    /// </summary>
    private async Task<string?> StoredUrlAsync(string blobName, CancellationToken ct)
    {
        try
        {
            if (!await blobStorage.ExistsAsync(blobName, ct))
            {
                return null;
            }

            await using var stream = await blobStorage.DownloadAsync(blobName, ct);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);

            return await blobStorage.UploadAsync(
                blobName, buffer.ToArray(), ContentTypeFor(blobName), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The stored URL for {BlobName} could not be recovered.", blobName);
            return null;
        }
    }

    private static string ContentTypeFor(string blobName) =>
        blobName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "image/png";
}
