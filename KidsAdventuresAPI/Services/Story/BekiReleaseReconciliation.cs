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
    /// Re-evaluates every Completed Beki book whose download is withheld, under the policy in force
    /// now, and publishes what unlocks — amendment B7. Returns how many were published.
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
    IBekiReleasePolicyService? policyService = null) : IBekiReleaseReconciliation, IBekiDownloadStatusService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    public async Task<BekiReconcileResult> ReconcilePackAsync(
        Guid packId, string reason, CancellationToken ct)
    {
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
        var pdfUrl = report is { CustomerPdfMayPublish: true, PressFilesMayPublish: true }
            ? await StoredUrlAsync(BekiPackBlobs.ReadingPdfName(pack.UserId, pack.Id), ct)
            : null;

        if (string.IsNullOrWhiteSpace(pdfUrl) || report is { PressFilesMayPublish: false })
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

            await PublishUnlockedFilesAsync(pack, report, ct);
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
                    if ((await PublishUnlockedFilesAsync(pack, report, ct)).Anything)
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

        var press = false;

        if (report.PressFilesMayPublish && report.CustomerPdfMayPublish
            && string.IsNullOrWhiteSpace(pack.PrintPdfUrl)
            && await blobStorage.ExistsAsync(
                BekiPackBlobs.InteriorPdfName(pack.UserId, pack.Id), ct)
            && await StoredUrlAsync(BekiPackBlobs.InteriorPdfName(pack.UserId, pack.Id), ct)
                is { } interiorUrl)
        {
            await packRepository.UpdatePrintPdfUrlAsync(pack.Id, interiorUrl, ct);

            // The in-memory row follows the write, for the reason the revival path does it: a caller
            // that consults this pack again — or calls here twice — must not see a column that has
            // already been written as still empty.
            pack.PrintPdfUrl = interiorUrl;
            press = true;
        }

        if (!report.CustomerPdfMayPublish || !report.PressFilesMayPublish
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
                    $"{waiver.Status} on the {waiver.DeliverableClass} deliverable, published anyway "
                    + $"because the policy flags this check: {waiver.Detail}",
                    BekiPackBlobs.ReleaseGatesName(userId, packId),
                    BekiAlarmEvidence.ForAttempt(waiver.CheckId, waiver.DeliverableClass)),
                ct);
        }
    }

    // ==============================================================================================
    // Reading what is stored
    // ==============================================================================================

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
