using System.Text.Json;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Story;

public interface IBekiPackFulfillment
{
    /// <summary>Hangfire entry point: draws the book, lays it out, and completes the pack.</summary>
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
    ILogger<BekiPackFulfillment> logger) : IBekiPackFulfillment
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ProcessAsync(Guid packId, Guid runId, CancellationToken cancellationToken)
    {
        var pack = await packRepository.GetByIdNoOwnershipAsync(packId, cancellationToken);
        if (pack is null)
        {
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

            // Each spread lands in storage the moment it is accepted, so the generating screen
            // can show the parent real pictures while the rest are still being drawn. The
            // stored URL is whatever UploadAsync returned, never a key assembled here: the two
            // storage implementations shape their keys differently, and a key built by hand is
            // a key that reads in one environment and 404s in the other.
            var storedUrls = new Dictionary<int, string>();
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

                    storedUrls[number] = await blobStorage.UploadAsync(
                        BekiPackBlobs.SpreadName(pack.UserId, pack.Id, number),
                        image.Image,
                        "image/png",
                        cancellationToken);

                    var percent = 10 + (int)MathF.Round(number * 70f / BookFormat.SpreadCount);
                    await packRepository.UpdateProgressAsync(
                        packId,
                        $"დაიხატა {number}/{BookFormat.SpreadCount} ილუსტრაცია…",
                        percent,
                        cancellationToken);
                },
                cancellationToken);

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
                    storedUrls[number] = await blobStorage.UploadAsync(
                        BekiPackBlobs.SpreadName(pack.UserId, pack.Id, number),
                        spread.Image,
                        "image/png",
                        cancellationToken);
                }

                stored.Add(new BekiSpreadArtwork(number, spread.Image));
            }

            var pdf = composer.Compose(plan, book.Cover.Image, stored);
            var pdfUrl = await blobStorage.UploadAsync(
                $"{pack.UserId}/{pack.Id}.pdf", pdf, "application/pdf", cancellationToken);

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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Beki fulfilment failed for pack {PackId}.", packId);
            await packRepository.UpdateStatusAsync(
                packId, AdventurePackStatus.Failed, pack.GeneratedJson, null, ex.Message,
                CancellationToken.None);
        }
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
