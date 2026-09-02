using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.Orders;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using Hangfire;

namespace AdventurePacks.Api.Services.Story;

/// <summary>Which part of a book an operator has asked to have drawn again.</summary>
public static class BekiRegenerationScopes
{
    /// <summary>Every picture: the eight spreads and the cover master.</summary>
    public const string Book = "book";

    /// <summary>One spread, by number.</summary>
    public const string Spread = "spread";

    /// <summary>The cover wrap and everything cut from it.</summary>
    public const string Cover = "cover";

    public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Book => Book,
        Spread => Spread,
        Cover => Cover,
        _ => null,
    };
}

/// <summary>How the request ended, in the three shapes an HTTP caller has to tell apart.</summary>
public enum BekiRegenerationStatus
{
    /// <summary>The blobs are gone, the pack is claimed and the job is queued.</summary>
    Queued,

    /// <summary>No such book.</summary>
    NotFound,

    /// <summary>There is a book and this cannot be done to it — a 409, with a sentence saying why.</summary>
    Refused,
}

/// <summary>
/// One operator's request to draw part of a book again.
/// </summary>
/// <param name="Operator">
/// Who asked, from the authenticated admin. On the alarm rather than only in a log, because this
/// is the one console action that spends money and "who authorised this" must survive log rotation.
/// </param>
public sealed record BekiRegenerationRequest(
    Guid BookId,
    string Scope,
    int? Spread,
    string Reason,
    string Operator);

/// <summary><paramref name="Message"/> is Georgian and goes straight to the operator.</summary>
public sealed record BekiRegenerationResult(BekiRegenerationStatus Status, string Message)
{
    public bool Queued => Status == BekiRegenerationStatus.Queued;
}

public interface IBekiRegeneration
{
    /// <summary>
    /// Deletes what is being redrawn, claims the pack and queues the fulfilment job again.
    /// Never throws for a refusal — the reason comes back as a sentence.
    /// </summary>
    Task<BekiRegenerationResult> RequestAsync(BekiRegenerationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a redraw is possible for this book right now, for the console's own button state.
    /// Cheap: it reads nothing the caller has not already read.
    /// </summary>
    bool CanRegenerate(AdventurePack pack);
}

/// <summary>
/// Drawing part of a paid book again, on an operator's say-so.
///
/// The pipeline already knows how to resume: a Beki run adopts every spread whose blob it can
/// download and redraws the rest, so the whole of a redraw is deciding which stored bytes to
/// delete before the same job is queued a second time. That is why this is a small class in front
/// of <see cref="IBekiPackFulfillment"/> rather than a second pipeline — a "regenerate" that had
/// its own drawing code would be a second definition of what a book is.
///
/// Three things make it safe to expose. It is compare-and-set: the pack is claimed BEFORE anything
/// is deleted, so an operator who loses a race to the stale sweep or to another admin destroys
/// nothing. It refuses a book that has a live job rather than racing it, because two runs drawing
/// one pack is the duplicate spend Hangfire's per-pack lock exists to prevent and this would be
/// asking for it politely. And it leaves a row in the alarms table naming the operator, the scope
/// and the reason — every one of these costs real money at an image API, and a spend with no
/// stated cause is one nobody can account for at the end of the month.
///
/// What is never deleted: the story, the Visual Scenario, the child's identity spec and the
/// photograph. Those are the book's IDENTITY, not its rendering. Replanning them would dress the
/// child differently and rewrite words the parent already read — a redraw is meant to give them
/// the same book, drawn properly.
/// </summary>
public sealed class BekiRegeneration(
    IAdventurePackRepository packRepository,
    IOrderRepository orderRepository,
    IMasterStoryRunRepository masterStoryRunRepository,
    IBlobStorageService blobStorage,
    IBekiAlarmService alarms,
    IBackgroundJobClient backgroundJobClient,
    IOptions<BekiOptions> bekiOptions,
    ILogger<BekiRegeneration> logger,
    TimeProvider? timeProvider = null) : IBekiRegeneration
{
    /// <summary>What the parent sees while the pictures are being made again.</summary>
    public const string ProgressMessage = "ადმინის მოთხოვნით ხელახლა იხატება…";

    /// <summary>The alarms-table check id for a deliberate redraw. Severity flag: it is not a fault.</summary>
    public const string AlarmCheckId = "admin_regenerate";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public bool CanRegenerate(AdventurePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        if (!pack.IsBekiPipeline)
        {
            return false;
        }

        return pack.Status switch
        {
            AdventurePackStatus.Completed or AdventurePackStatus.Failed => true,

            // A book that stopped mid-run leaves its pack in a working status forever, and those
            // are the books most in need of this. Allowed only once the job behind it has gone
            // quiet for longer than the sweep tolerates — before that there is a run drawing
            // spreads, and deleting them underneath it is how one book gets drawn twice.
            AdventurePackStatus.StoryReady => IsSilent(pack),

            _ => false,
        };
    }

    public async Task<BekiRegenerationResult> RequestAsync(
        BekiRegenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = BekiRegenerationScopes.Normalize(request.Scope);
        if (scope is null)
        {
            return Refused("ხელახლა დახატვის არეალი არასწორია.");
        }

        if (scope == BekiRegenerationScopes.Spread
            && request.Spread is not (>= 1 and <= BookFormat.SpreadCount))
        {
            return Refused($"გვერდის ნომერი უნდა იყოს 1-დან {BookFormat.SpreadCount}-მდე.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Refused("ხელახლა დახატვის მიზეზი აუცილებელია.");
        }

        var pack = await packRepository.GetByIdNoOwnershipAsync(request.BookId, cancellationToken);
        if (pack is null)
        {
            return new BekiRegenerationResult(BekiRegenerationStatus.NotFound, "წიგნი ვერ მოიძებნა.");
        }

        if (!pack.IsBekiPipeline)
        {
            // The legacy pipeline draws per page on demand and has no spreads, no cover wrap and no
            // resumable manifest. Queuing the Beki job at it would start a run against a book whose
            // preview plan was never written for it.
            return Refused("ეს წიგნი ძველი პაიპლაინითაა დახატული — ხელახლა დახატვა მხოლოდ ახალ ფორმატზეა შესაძლებელი.");
        }

        if (!CanRegenerate(pack))
        {
            // Everything that reaches here is a book with a job in front of it: claimed, planned,
            // drawing or laying out. Refused rather than raced — Hangfire's per-pack lock exists to
            // stop one book being drawn twice, and this would be asking for it politely.
            return Refused("წიგნი ახლა იხატება — დაელოდეთ დასრულებას ან ჩავარდნას.");
        }

        var (runId, orderId) = await ResumePointAsync(pack, cancellationToken);
        if (runId is not { } run)
        {
            // Without the preview run there is no plan, no portrait and no scenario source, and the
            // job would fail on its first line. Said plainly rather than queued and left to die.
            return Refused("ამ წიგნის საწყისი გეგმა ან ფოტო ვეღარ მოიძებნა, ამიტომ ხელახლა დახატვა შეუძლებელია.");
        }

        /*
          Claim first, delete second.

          The compare-and-set is what makes the whole operation safe to lose. If the sweep buried
          this pack, or another admin clicked the same button a second earlier, the write fails and
          nothing has been deleted — the book is exactly as it was. The reverse order would delete
          eight pictures and then discover it was not allowed to.

          Passing null for the PDF url is how the reading copy is unpublished in the same statement:
          a book being redrawn must not stay downloadable, or a parent opens the old one halfway
          through and the release gates re-publish over it later.
        */
        var claimed = await packRepository.TryUpdateStatusAsync(
            pack.Id,
            pack.Status,
            AdventurePackStatus.GeneratingStory,
            pack.GeneratedJson,
            null,
            null,
            cancellationToken);

        if (!claimed)
        {
            return Refused("წიგნის სტატუსი შეიცვალა — გვერდი განაახლეთ და ხელახლა სცადეთ.");
        }

        await packRepository.UpdatePrintPdfUrlAsync(pack.Id, null, cancellationToken);
        await packRepository.UpdateProgressAsync(pack.Id, ProgressMessage, 0, cancellationToken);

        var deleted = await DeleteAsync(pack, scope, request.Spread, cancellationToken);

        backgroundJobClient.Enqueue<IBekiPackFulfillment>(service =>
            service.ProcessAsync(pack.Id, run, CancellationToken.None));

        await RaiseAsync(pack, orderId, scope, request, deleted, cancellationToken);

        logger.LogWarning(
            "Beki pack {PackId}: {Operator} asked for a {Scope}{Spread} redraw — {Deleted} stored "
            + "artifact(s) removed, run {RunId} queued again. Reason: {Reason}",
            pack.Id, request.Operator, scope,
            scope == BekiRegenerationScopes.Spread ? $" {request.Spread}" : string.Empty,
            deleted, run, request.Reason.Trim());

        return new BekiRegenerationResult(
            BekiRegenerationStatus.Queued,
            scope switch
            {
                BekiRegenerationScopes.Spread => $"გვერდი {request.Spread} ხელახლა დახატვის რიგშია.",
                BekiRegenerationScopes.Cover => "ყდა ხელახლა დახატვის რიგშია.",
                _ => "წიგნი ხელახლა დახატვის რიგშია.",
            });
    }

    // -- what gets deleted --------------------------------------------------------------------

    /// <summary>
    /// Removes exactly the stored bytes this scope is redrawing, plus the evidence that describes
    /// them.
    ///
    /// The resume path adopts a manifest entry only when its blob still downloads, so deleting a
    /// spread's PNG is the whole of "redraw this spread" — the manifest needs no surgery, and
    /// leaving it alone keeps the composition receipts of the pages that are NOT being redrawn.
    ///
    /// The release verdict and the contact sheets go in every scope, because both describe a
    /// rendering that is about to stop existing. A verdict left behind is the most dangerous
    /// artifact here: it would let the reconciliation publish a book on the strength of gates
    /// evaluated against pictures that have been deleted.
    /// </summary>
    private async Task<int> DeleteAsync(
        AdventurePack pack, string scope, int? spread, CancellationToken cancellationToken)
    {
        var names = new List<string>
        {
            BekiPackBlobs.ReleaseGatesName(pack.UserId, pack.Id),
        };

        foreach (var artifact in BekiPackBlobs.RenderedArtifacts)
        {
            names.Add(BekiPackBlobs.ContactSheetName(pack.UserId, pack.Id, artifact));
            names.Add(BekiPackBlobs.RenderReportName(pack.UserId, pack.Id, artifact));
        }

        switch (scope)
        {
            case BekiRegenerationScopes.Spread:
                names.AddRange(SpreadNames(pack, spread!.Value));
                break;

            case BekiRegenerationScopes.Cover:
                names.AddRange(CoverNames(pack));
                break;

            default:
                // The manifest goes too: with no adopted entries the run replans nothing and
                // redraws everything, which is exactly what "the whole book" was asked for.
                names.Add(BekiPackBlobs.ManifestName(pack.UserId, pack.Id));
                names.AddRange(CoverNames(pack));

                for (var number = 1; number <= BookFormat.SpreadCount; number++)
                {
                    names.AddRange(SpreadNames(pack, number));
                }

                break;
        }

        // The finals in every scope: a press interior built from a spread that no longer exists is
        // a file whose pages disagree with the book.
        names.Add(BekiPackBlobs.ReadingPdfName(pack.UserId, pack.Id));
        names.Add(BekiPackBlobs.DigitalReportName(pack.UserId, pack.Id));
        names.Add(BekiPackBlobs.InteriorPdfName(pack.UserId, pack.Id));
        names.Add(BekiPackBlobs.InteriorPreflightName(pack.UserId, pack.Id));
        names.Add(BekiPackBlobs.PressStatusName(pack.UserId, pack.Id));

        var deleted = 0;

        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            try
            {
                // Keyed by name rather than by a stored url: both storage backends treat a key with
                // no scheme as a blob in the configured container, which is how every other reader
                // in this pipeline addresses these files.
                if (await blobStorage.DeleteByStoredUrlAsync(name, cancellationToken))
                {
                    deleted++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A blob that will not delete is a stale artifact, not a reason to abandon a redraw
                // that has already claimed the pack. The run will overwrite what it redraws anyway.
                logger.LogWarning(
                    ex, "Beki pack {PackId}: {Blob} could not be deleted for a redraw.", pack.Id, name);
            }
        }

        return deleted;
    }

    private static IEnumerable<string> SpreadNames(AdventurePack pack, int spread) =>
    [
        BekiPackBlobs.SpreadName(pack.UserId, pack.Id, spread),
        BekiPackBlobs.SpreadBaseName(pack.UserId, pack.Id, spread),
        BekiPackBlobs.SpreadQaName(pack.UserId, pack.Id, spread),
        BekiPackBlobs.CompositionManifestName(pack.UserId, pack.Id, spread),
        BekiPackBlobs.FailedSpreadName(pack.UserId, pack.Id, spread),
    ];

    /// <summary>
    /// The cover master and everything cut from it. All of it, because there is exactly one cover
    /// master (audit P0-01) and a derivation that outlived it would be the second cover design the
    /// supplier rejected the package for.
    /// </summary>
    private static IEnumerable<string> CoverNames(AdventurePack pack) =>
    [
        BekiPackBlobs.CoverWrapCompositeName(pack.UserId, pack.Id),
        BekiPackBlobs.CoverWrapBaseName(pack.UserId, pack.Id),
        BekiPackBlobs.CoverCompositionName(pack.UserId, pack.Id),
        BekiPackBlobs.CoverFrontName(pack.UserId, pack.Id),
        BekiPackBlobs.CoverName(pack.UserId, pack.Id),
        BekiPackBlobs.CoverPdfName(pack.UserId, pack.Id),
        BekiPackBlobs.CoverPreflightName(pack.UserId, pack.Id),
    ];

    // -- the run this book resumes from -------------------------------------------------------

    /// <summary>
    /// The preview run the fulfilment job needs, and the order that paid for the book.
    ///
    /// The run id is not on the pack — it lives on the order's frozen draft, which is where
    /// <c>BookFulfillmentService</c> reads it from too. Validated the same way that service
    /// validates it, because a run without a plan or without the child's photograph is a job that
    /// throws on its first line, and an operator would be shown "queued" for a redraw that never
    /// starts.
    /// </summary>
    private async Task<(Guid? RunId, Guid? OrderId)> ResumePointAsync(
        AdventurePack pack, CancellationToken cancellationToken)
    {
        var orders = await orderRepository.GetPaidForBookAsync(pack.Id, cancellationToken);

        foreach (var order in orders)
        {
            if (string.IsNullOrWhiteSpace(order.DraftJson))
            {
                continue;
            }

            Guid? previewBookId;

            try
            {
                previewBookId = JsonSerializer
                    .Deserialize<BookDraftRequest>(order.DraftJson, JsonOptions)?.PreviewBookId;
            }
            catch (JsonException ex)
            {
                logger.LogWarning(
                    ex, "Beki pack {PackId}: order {OrderId} has an unreadable draft.", pack.Id, order.Id);
                continue;
            }

            if (previewBookId is not { } runId)
            {
                continue;
            }

            var run = await masterStoryRunRepository.GetByIdAsync(runId, cancellationToken);

            if (run is not null
                && !string.IsNullOrWhiteSpace(run.StoryJson)
                && !string.IsNullOrWhiteSpace(run.PhotoBlobUrl)
                && BookFormat.IsPrintPlan(run.PromptVersion))
            {
                return (runId, order.Id);
            }
        }

        return (null, orders.Count > 0 ? orders[0].Id : null);
    }

    private async Task RaiseAsync(
        AdventurePack pack,
        Guid? orderId,
        string scope,
        BekiRegenerationRequest request,
        int deleted,
        CancellationToken cancellationToken)
    {
        var target = scope == BekiRegenerationScopes.Spread ? $"spread {request.Spread}" : scope;

        await alarms.RaiseAsync(
            new BekiAlarmRaise(
                pack.Id,
                orderId,
                pack.UserId,
                AlarmCheckId,
                // A flag, not a blocker: nothing is broken and nobody needs paging. What this row
                // is for is the money — it is the audit trail for a deliberate spend.
                BekiReleaseSeverity.Flag,
                $"{request.Operator} asked for the {target} to be drawn again; {deleted} stored "
                + $"artifact(s) were deleted and the fulfilment job was queued. "
                + $"Reason: {request.Reason.Trim()}",
                null,
                // Timestamped so two redraws of the same book are two rows. Everywhere else in this
                // system deduplication is the point; here each request is its own event, and
                // collapsing them would hide the third redraw of a book somebody keeps paying for.
                BekiAlarmEvidence.ForAttempt(
                    AlarmCheckId, target, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds())),
            cancellationToken);
    }

    /// <summary>
    /// Whether this pack's job has gone quiet for longer than the stale sweep tolerates — the same
    /// limit the sweep judges by, so the console and the sweep cannot disagree about which books
    /// have something running behind them.
    /// </summary>
    private bool IsSilent(AdventurePack pack) =>
        (pack.GenerationHeartbeatUtc ?? pack.CreatedAt)
        < _timeProvider.GetUtcNow().UtcDateTime - GenerationBudget.SweepSilenceLimit(bekiOptions.Value);

    private static BekiRegenerationResult Refused(string message) =>
        new(BekiRegenerationStatus.Refused, message);
}
