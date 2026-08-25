using System.Diagnostics;
using System.Text.Json;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using Hangfire;

namespace AdventurePacks.Api.Services.Story;

public interface IBekiPackFulfillment
{
    /// <summary>
    /// Hangfire entry point: draws the book, lays it out, and completes the pack.
    ///
    /// <see cref="DisableConcurrentExecutionAttribute"/> is declared here rather than only on the
    /// implementation because jobs enqueued via <c>Enqueue&lt;IBekiPackFulfillment&gt;</c> carry
    /// the interface's <c>MethodInfo</c>, and Hangfire's filter provider reads attributes off
    /// exactly the method and type recorded on the job — the implementation's own copy of the
    /// attribute is for a reader looking at that file, not for Hangfire. One pack, one worker at a
    /// time: a book that costs nine images must never be drawn twice because a second worker
    /// picked up the same job while the first was still running.
    ///
    /// The lock is keyed by the pack id ({0} is formatted from the first job argument), not by
    /// the method alone. A method-keyed lock would serialize every Beki book in the system behind
    /// whichever one is currently drawing — many minutes each — and unrelated paid orders would
    /// queue up against the 1800-second timeout for no reason.
    /// </summary>
    [DisableConcurrentExecution("beki-pack:{0}", 1800)]
    Task ProcessAsync(Guid packId, Guid runId, CancellationToken cancellationToken);
}

/// <summary>
/// The one place a Beki pack's blob names are written down. The fulfilment job uploads under
/// these names and the making-of endpoint probes them; a name assembled anywhere else is a name
/// the two can disagree about.
/// </summary>
public static class BekiPackBlobs
{
    public static string SpreadName(Guid userId, Guid packId, int spreadNumber) =>
        $"{userId}/{packId}/spread-{spreadNumber:00}.png";

    /// <summary>
    /// Where a resumable job's progress lives between attempts. Read and written by its bare
    /// name, the way <see cref="IBlobStorageService.ExistsAsync"/> and
    /// <see cref="IBlobStorageService.DownloadAsync"/> both address a blob — never by a stored
    /// URL, which is exactly the mistake this whole product avoids: a key built by hand reads on
    /// one storage backend and 404s on the other.
    /// </summary>
    public static string ManifestName(Guid userId, Guid packId) => $"{userId}/{packId}/fulfilment.json";
}

/// <summary>
/// Fulfils a purchased book in the Beki format: eight continuous spreads drawn from the plan the
/// parent previewed, laid out by <see cref="BekiPdfComposer"/>, ending as a completed pack with
/// a PDF — the same shape of result the legacy flow produces, reached by a different pipeline.
///
/// The story is never rewritten here. The preview run already holds the plan the parent read and
/// the cover they judged the book by; this job draws the eight spreads that were never shown,
/// which is the only part of the book that still costs a generation.
///
/// The reader is fed through the same projection the legacy flow uses: each spread's picture
/// page points at the stored spread image, so the existing reader and illustration endpoint
/// serve the book without knowing which pipeline made it.
/// </summary>
public sealed class BekiPackFulfillment(
    IAdventurePackRepository packRepository,
    IMasterStoryRunRepository masterStoryRunRepository,
    IBlobStorageService blobStorage,
    IBekiBookGenerator generator,
    IBekiPdfComposer composer,
    IAdminNotifier adminNotifier,
    ILogger<BekiPackFulfillment> logger) : IBekiPackFulfillment
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [DisableConcurrentExecution("beki-pack:{0}", 1800)]
    public async Task ProcessAsync(Guid packId, Guid runId, CancellationToken cancellationToken)
    {
        // The spec's telemetry mandate (§27): before any performance work, measure where the
        // twenty minutes actually go. Total spans the whole job; uploads accumulate across the
        // mid-run callback, the catch-up pass and the PDF, which run one at a time by design.
        var totalStopwatch = Stopwatch.StartNew();
        long uploadMs = 0;

        var pack = await packRepository.GetByIdNoOwnershipAsync(packId, cancellationToken);
        if (pack is null)
        {
            return;
        }

        // A completed pack is not reprocessed. The lease above stops two workers overlapping on
        // the same run; this stops a job that reaches this point a second time after the pack had
        // already finished — a stalled-order sweep re-enqueuing generation whose first attempt
        // had, in fact, already succeeded.
        if (pack.Status == AdventurePackStatus.Completed)
        {
            logger.LogInformation("Beki pack {PackId} is already completed; skipping.", packId);
            return;
        }

        try
        {
            // Claimed before any work: the stalled-order sweep re-enqueues generation for a pack
            // still Pending, and a book that costs nine images must never be drawn twice because
            // it was slow. Same move the legacy job opens with, for the same reason.
            await packRepository.UpdateStatusAsync(
                packId,
                AdventurePackStatus.GeneratingStory,
                pack.GeneratedJson,
                null,
                null,
                cancellationToken);

            var run = await masterStoryRunRepository.GetByIdAsync(runId, cancellationToken)
                      ?? throw new InvalidOperationException($"Preview run {runId} is gone.");

            if (string.IsNullOrWhiteSpace(run.StoryJson) || string.IsNullOrWhiteSpace(run.PhotoBlobUrl))
            {
                throw new InvalidOperationException(
                    $"Run {runId} is missing its plan or its portrait; the Beki format needs both.");
            }

            var plan = JsonSerializer.Deserialize<MasterStory>(run.StoryJson, JsonOptions)
                       ?? throw new InvalidOperationException($"Run {runId} has an unreadable plan.");

            // Validated defensively, warnings only: the parent already read and bought this plan,
            // so a problem here is not this job's to refuse over. It is worth knowing about all
            // the same — the same checks that would have triggered a retry at preview time.
            foreach (var problem in BekiPlanValidator.Validate(plan, BookFormat.SpreadCount, run.Age))
            {
                logger.LogWarning("Beki pack {PackId}: plan validation problem — {Problem}", packId, problem);
            }

            await packRepository.UpdateProgressAsync(
                packId, "ბეკის წიგნის გვერდებს ვხატავთ…", 10, cancellationToken);

            var photo = await blobStorage.DownloadBytesFromStoredUrlAsync(
                run.PhotoBlobUrl, cancellationToken);

            // The cover the parent previewed, when it survived; drawn fresh when it did not.
            byte[]? existingCover = null;
            if (!string.IsNullOrWhiteSpace(run.CoverImageUrl))
            {
                try
                {
                    existingCover = await blobStorage.DownloadBytesFromStoredUrlAsync(
                        run.CoverImageUrl, cancellationToken);
                }
                catch (Exception coverEx)
                {
                    logger.LogWarning(
                        coverEx, "Preview cover unavailable for pack {PackId}; drawing one.", packId);
                }
            }

            // The stored URL is whatever UploadAsync returned, never a key assembled here: the
            // two storage implementations shape their keys differently, and a key built by hand
            // is a key that reads in one environment and 404s in the other.
            var storedUrls = new Dictionary<int, string>();

            // Resuming a job whose first attempt died partway: read back whatever it had already
            // drawn and accepted, so this attempt redraws only the holes rather than the whole
            // book. Any problem reading the manifest — it is absent, it will not parse, the
            // illustration contract it was written under is no longer the one in force — is
            // treated as no manifest at all; the worst that costs is redrawing spreads that were
            // already fine, and the alternative is a book drawn half under each set of rules.
            var manifestName = BekiPackBlobs.ManifestName(pack.UserId, pack.Id);
            var currentContract = BekiFulfillmentManifest.CurrentContract(BookFormat.SpreadCount);
            var manifest = await TryReadManifestAsync(manifestName, cancellationToken);

            if (manifest is not null && !manifest.IllustrationContract.SequenceEqual(currentContract))
            {
                logger.LogWarning(
                    "Beki pack {PackId}: the manifest's illustration contract (text side, shot, Beki "
                    + "version) no longer matches the current one; ignoring it and redrawing.",
                    packId);
                manifest = null;
            }

            var existingSpreads = new Dictionary<int, byte[]>();
            if (manifest is not null)
            {
                foreach (var entry in manifest.Entries)
                {
                    try
                    {
                        var bytes = await blobStorage.DownloadBytesFromStoredUrlAsync(
                            entry.StoredUrl, cancellationToken);
                        if (bytes is not { Length: > 0 })
                        {
                            continue;
                        }

                        existingSpreads[entry.SpreadNumber] = bytes;
                        storedUrls[entry.SpreadNumber] = entry.StoredUrl;
                    }
                    catch (Exception ex)
                    {
                        // A download failure just drops that entry — it will be redrawn, same as
                        // a spread that was never in the manifest at all.
                        logger.LogWarning(
                            ex, "Beki pack {PackId}: could not adopt spread {Spread} from the manifest.",
                            packId, entry.SpreadNumber);
                    }
                }

                if (existingSpreads.Count > 0)
                {
                    // Adopted spreads advance the percent immediately, using the same message the
                    // drawing loop below would use — a resumed job's progress bar should not lie
                    // by sitting at 10% while most of the book is already done.
                    var percent = 10 + (int)MathF.Round(existingSpreads.Count * 70f / BookFormat.SpreadCount);
                    await packRepository.UpdateProgressAsync(
                        packId,
                        $"დაიხატა {existingSpreads.Count}/{BookFormat.SpreadCount} ილუსტრაცია…",
                        percent,
                        cancellationToken);
                }
            }

            // Progress counts spreads processed, not the spread number being drawn. On a resume
            // the two disagree: with spreads 1 and 3–6 adopted and spread 2 being redrawn, the
            // number would report "2/8" and yank the bar backwards below the five images the
            // parent was already shown as done.
            var processedSpreads = existingSpreads.Count;

            var book = await generator.IllustrateAsync(
                plan,
                photo,
                "image/png",
                existingCover,
                async image =>
                {
                    if (image.SpreadNumber is not { } number)
                    {
                        return;
                    }

                    // Refused spreads are not uploaded mid-run and do not touch the manifest — the
                    // catch-up pass below still uploads whatever storedUrls lacks once the book is
                    // whole, unchanged from before this job became resumable.
                    if (image.Accepted)
                    {
                        var uploadStopwatch = Stopwatch.StartNew();
                        storedUrls[number] = await blobStorage.UploadAsync(
                            BekiPackBlobs.SpreadName(pack.UserId, pack.Id, number),
                            image.Image,
                            "image/png",
                            cancellationToken);

                        await WriteManifestAsync(manifestName, storedUrls, currentContract, cancellationToken);
                        uploadMs += uploadStopwatch.ElapsedMilliseconds;
                    }

                    processedSpreads++;
                    var percent = 10 + (int)MathF.Round(processedSpreads * 70f / BookFormat.SpreadCount);
                    await packRepository.UpdateProgressAsync(
                        packId,
                        $"დაიხატა {processedSpreads}/{BookFormat.SpreadCount} ილუსტრაცია…",
                        percent,
                        cancellationToken);
                },
                cancellationToken,
                existingSpreads.Count > 0 ? existingSpreads : null);

            foreach (var warning in book.Warnings)
            {
                logger.LogWarning("Beki pack {PackId}: {Warning}", packId, warning);
            }

            await packRepository.UpdateProgressAsync(
                packId, "წიგნს ვაწყობთ და PDF-ს ვამზადებთ…", 85, cancellationToken);

            // Everything ships. A NEEDS_REVIEW spread is a picture a human should look at, not a
            // hole in a paid book — the warning above is the trail. The callback has already
            // stored each spread; this pass only catches one it somehow missed.
            var stored = new List<BekiSpreadArtwork>(book.Spreads.Count);
            foreach (var spread in book.Spreads.OrderBy(s => s.SpreadNumber ?? 0))
            {
                var number = spread.SpreadNumber ?? 0;
                if (!storedUrls.ContainsKey(number))
                {
                    var uploadStopwatch = Stopwatch.StartNew();
                    storedUrls[number] = await blobStorage.UploadAsync(
                        BekiPackBlobs.SpreadName(pack.UserId, pack.Id, number),
                        spread.Image,
                        "image/png",
                        cancellationToken);
                    uploadMs += uploadStopwatch.ElapsedMilliseconds;
                }

                stored.Add(new BekiSpreadArtwork(number, spread.Image));
            }

            // Personalization is what the intro spread prints and the endpapers key on: the
            // child's name and age from the run, the world as the parent chose it
            // (StoryWorlds.For, the same canon the planner writes from), and the purchase date —
            // pack.CreatedAt, never "now", so a job that dies and resumes tomorrow prints the
            // same date its first attempt would have.
            var personalization = new BekiBookPersonalization(
                run.ChildName, run.Age, pack.CreatedAt, pack.Theme.ToString(),
                StoryWorlds.For(pack.Theme).Place);

            var pdfStopwatch = Stopwatch.StartNew();
            var pdf = composer.Compose(plan, book.Cover.Image, stored, personalization);
            pdfStopwatch.Stop();

            var pdfUploadStopwatch = Stopwatch.StartNew();
            var pdfUrl = await blobStorage.UploadAsync(
                $"{pack.UserId}/{pack.Id}.pdf", pdf, "application/pdf", cancellationToken);
            uploadMs += pdfUploadStopwatch.ElapsedMilliseconds;

            // One file serves both shelves: the Beki layout is print geometry already — bleed,
            // spread pages, the QR leaf — so the reading copy and the print copy are the same
            // bytes, unlike the A5 book whose print copy differs by binding blanks.
            await packRepository.UpdatePrintPdfUrlAsync(packId, pdfUrl, cancellationToken);

            var content = ProjectForReader(plan, run.ChildName, pack, storedUrls);

            await packRepository.UpdateStatusAsync(
                packId,
                AdventurePackStatus.Completed,
                JsonSerializer.Serialize(content, JsonOptions),
                pdfUrl,
                null,
                cancellationToken);

            await packRepository.UpdateProgressAsync(
                packId, "მზადაა! წიგნი ბიბლიოთეკაშია.", 100, cancellationToken);

            logger.LogInformation(
                "Beki pack {PackId} completed from run {RunId}: \"{Title}\", {Spreads} spreads.",
                packId, runId, plan.Concept.Title, stored.Count);

            // Telemetry last, and best-effort: the order is already complete, and a failed
            // measurement must never look like a failed book. Per-attempt rows come from the
            // generator's own stopwatches, so a first attempt the reviewer refused keeps its
            // verdict here even when the retry passed — the QA failure reasons are the point.
            try
            {
                totalStopwatch.Stop();
                var telemetry = new
                {
                    cover = new
                    {
                        attempts = book.Cover.AttemptDetails.Select(a => new
                        {
                            generationMs = a.GenerationMs,
                            reviewMs = a.ReviewMs,
                            accepted = a.Accepted,
                            issues = a.Accepted ? Array.Empty<string>() : ParseIssues(a.Verdict),
                        }).ToList(),
                        accepted = book.Cover.Accepted,
                        adopted = book.Cover.AttemptDetails.Count == 0,
                    },
                    spreads = book.Spreads.Select(s => new
                    {
                        spreadNumber = s.SpreadNumber,
                        attempts = s.AttemptDetails.Select(a => new
                        {
                            generationMs = a.GenerationMs,
                            reviewMs = a.ReviewMs,
                            accepted = a.Accepted,
                            issues = a.Accepted ? Array.Empty<string>() : ParseIssues(a.Verdict),
                        }).ToList(),
                        accepted = s.Accepted,
                        adoptedFromManifest = s.AttemptDetails.Count == 0,
                    }).ToList(),
                    uploadMs,
                    pdfBuildMs = pdfStopwatch.ElapsedMilliseconds,
                    totalMs = totalStopwatch.ElapsedMilliseconds,
                    totalImageAttempts = book.Cover.AttemptDetails.Count
                        + book.Spreads.Sum(s => s.AttemptDetails.Count),
                    acceptedCount = (book.Cover.Accepted ? 1 : 0) + book.Spreads.Count(s => s.Accepted),
                    needsReviewCount = (book.Cover.Accepted ? 0 : 1) + book.Spreads.Count(s => !s.Accepted),
                };

                await blobStorage.UploadAsync(
                    $"{pack.UserId}/{pack.Id}/telemetry.json",
                    JsonSerializer.SerializeToUtf8Bytes(telemetry, JsonOptions),
                    "application/json",
                    cancellationToken);

                logger.LogInformation(
                    "Beki pack {PackId} telemetry: totalMs={TotalMs}, pdfBuildMs={PdfBuildMs}, "
                    + "uploadMs={UploadMs}, imageAttempts={Attempts}, accepted={Accepted}, "
                    + "needsReview={NeedsReview}.",
                    packId, telemetry.totalMs, telemetry.pdfBuildMs, telemetry.uploadMs,
                    telemetry.totalImageAttempts, telemetry.acceptedCount, telemetry.needsReviewCount);
            }
            catch (Exception telemetryEx)
            {
                logger.LogWarning(telemetryEx, "Beki pack {PackId}: telemetry not written.", packId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Beki fulfilment failed for pack {PackId}.", packId);
            await packRepository.UpdateStatusAsync(
                packId, AdventurePackStatus.Failed, pack.GeneratedJson, null, ex.Message,
                CancellationToken.None);

            // This book was paid for before this job ever started, so a failure here is money
            // taken with nothing delivered. It is the one generation failure that always needs
            // a person.
            await adminNotifier.BookFailedAsync(packId, ex.Message, CancellationToken.None);
        }
    }

    /// <summary>
    /// Reads the manifest back by its bare name — never a stored URL, since the manifest is
    /// never handed to anything outside this job. Absence and any parse failure are treated the
    /// same way: no manifest, so every spread is redrawn. A resumed job must never fail over a
    /// manifest it cannot trust.
    /// </summary>

    private async Task<BekiFulfillmentManifest?> TryReadManifestAsync(
        string manifestName, CancellationToken cancellationToken)
    {
        try
        {
            if (!await blobStorage.ExistsAsync(manifestName, cancellationToken))
            {
                return null;
            }

            await using var stream = await blobStorage.DownloadAsync(manifestName, cancellationToken);
            return await JsonSerializer.DeserializeAsync<BekiFulfillmentManifest>(
                stream, JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Beki pack manifest {ManifestName} could not be read; ignoring it.", manifestName);
            return null;
        }
    }

    /// <summary>
    /// Rewritten after every accepted spread: the illustration contract these pictures were drawn
    /// under, plus every accepted entry so far — the ones this run just adopted from an earlier
    /// attempt included. Never after a refusal: a spread that did not pass is not something the
    /// next attempt should adopt either.
    /// </summary>
    private async Task WriteManifestAsync(
        string manifestName,
        IReadOnlyDictionary<int, string> storedUrls,
        IReadOnlyList<string> illustrationContract,
        CancellationToken cancellationToken)
    {
        var manifest = new BekiFulfillmentManifest
        {
            IllustrationContract = illustrationContract,
            Entries = storedUrls
                .OrderBy(pair => pair.Key)
                .Select(pair => new BekiFulfillmentManifestEntry(pair.Key, pair.Value))
                .ToList(),
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        await blobStorage.UploadAsync(manifestName, json, "application/json", cancellationToken);
    }

    /// <summary>
    /// The reviewer's issue strings out of a stored verdict, for telemetry only — parse trouble
    /// yields an empty list, never an exception, because a malformed verdict already cost its
    /// retry and must not also cost the measurement.
    /// </summary>
    private static string[] ParseIssues(string verdict)
    {
        if (string.IsNullOrWhiteSpace(verdict)) return [];

        try
        {
            var extracted = ModelJsonSanitizer.ExtractJsonObject(verdict);
            if (string.IsNullOrEmpty(extracted)) return [];

            using var document = JsonDocument.Parse(extracted);
            if (document.RootElement.TryGetProperty("issues", out var issues)
                && issues.ValueKind == JsonValueKind.Array)
            {
                return issues.EnumerateArray()
                    .Select(issue => issue.GetString() ?? string.Empty)
                    .Where(issue => !string.IsNullOrEmpty(issue))
                    .ToArray();
            }
        }
        catch (JsonException)
        {
        }

        return [];
    }

    /// <summary>
    /// The legacy projection, with every picture page pointed at its stored spread. The reader
    /// rewrites these keys into its own illustration endpoint, exactly as it does for books the
    /// old pipeline drew. The keys are the ones storage handed back at upload, verbatim.
    /// </summary>
    private static AdventureContentDto ProjectForReader(
        MasterStory plan,
        string childName,
        Domain.Entities.AdventurePack pack,
        IReadOnlyDictionary<int, string> storedUrls)
    {
        var content = MasterStoryProjection.ToContent(plan, childName, pack.Theme.ToString());

        var spreadNumber = 0;
        foreach (var page in content.StoryPages)
        {
            if (page.IsTextOnlyPage)
            {
                continue;
            }

            spreadNumber++;
            if (storedUrls.TryGetValue(spreadNumber, out var url))
            {
                page.IllustrationUrl = url;
            }
        }

        return content;
    }
}
