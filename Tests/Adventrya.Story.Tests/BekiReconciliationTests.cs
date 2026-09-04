using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The two rescues — amendments B6 and B7.
///
/// Both are corrections to the same shape of fault: a book that was finished, or a book that was
/// publishable, sitting where nobody would ever look at it again because the only process that could
/// have said so had ended. The stale-generation sweep buries a pack whose job has gone quiet for the
/// whole budget plus a grace period; the job that was merely slow then finishes, loses the
/// compare-and-set, and leaves eight spreads, two press files and a reading PDF attached to a row
/// that says Failed. And a check flipped from blocker to flag changes nothing at all for the books
/// already withheld under it, which is exactly the state the operator was flipping it to end.
///
/// The guards matter as much as the rescues. A Failed→Completed transition that could be reached
/// from any failure would be a way out of every failure; it is reachable only from the sweep's own
/// code, and only when every artifact is there.
/// </summary>
public class BekiReconciliationTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PackId = Guid.NewGuid();

    [Fact]
    public async Task Print_only_failure_publishes_customer_pdf_and_revokes_stale_print_permission()
    {
        var blobs = new PolicyFakeBlobs();
        SeedFinishedBook(blobs);
        BekiReleasePolicyGateTests.Seed(blobs, UserId, PackId);
        blobs.Seed(BekiPackBlobs.PressStatusName(UserId, PackId),
            BekiReleasePolicyGateTests.Json(new
            {
                failed_gates = new[] { "PRESS_COLOR", "PRESS_RESOLUTION" },
                reason = "Print conversion failed; original customer PDF validated."
            }));
        var pack = CompletedPack();
        pack.PdfUrl = null;
        pack.PrintPdfUrl = "https://blob.test/stale-print-permission.pdf";
        var packs = new ReconcilePacks(pack) { Withheld = [pack] };

        var published = await Reconciliation(packs, blobs, new RecordingAlarms())
            .ReconcileWithheldAsync(CancellationToken.None);

        Assert.Equal(1, published);
        Assert.NotNull(pack.PdfUrl);
        Assert.Null(pack.PrintPdfUrl);
        Assert.Equal(AdventurePackStatus.Completed, pack.Status);
    }

    /// <summary>
    /// The case B6 exists for: the sweep was right about the silence and wrong about the book.
    /// </summary>
    [Fact]
    public async Task A_buried_book_whose_artifacts_are_all_there_is_restored()
    {
        var blobs = new PolicyFakeBlobs();
        SeedFinishedBook(blobs);

        var packs = new ReconcilePacks(BuriedPack());
        var alarms = new RecordingAlarms();
        var reconciliation = Reconciliation(packs, blobs, alarms);

        var result = await reconciliation.ReconcilePackAsync(
            PackId, "the fulfilment job finished after the sweep buried the book",
            CancellationToken.None);

        Assert.True(result.Restored);
        Assert.Equal(BekiReconcileOutcomes.Restored, result.Outcome);
        Assert.Equal(AdventurePackStatus.Completed, packs.Pack.Status);

        // The reader is pointed at the stored spreads, which is the one thing the row could not
        // carry: the Beki job writes the projection only in the same statement that writes Completed.
        var content = JsonSerializer.Deserialize<AdventureContentDto>(
            packs.Pack.GeneratedJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(content);
        Assert.All(
            content!.StoryPages.Where(page => !page.IsTextOnlyPage),
            page => Assert.False(string.IsNullOrWhiteSpace(page.IllustrationUrl)));

        // The download is published too, because nothing in this book's verdict withholds it.
        Assert.False(string.IsNullOrWhiteSpace(packs.Pack.PdfUrl));

        // And the burial stays on the record. A book that takes longer than the whole budget is a
        // fault even when it arrives.
        Assert.Contains(alarms.Raised, raise => raise.CheckId == GenerationBudget.StalledCode);
    }

    /// <summary>
    /// Only the sweep's verdict is reversible. A book that stopped on a named failure stopped for a
    /// reason somebody recorded, and reviving it would be claiming a book exists because some of its
    /// files do.
    /// </summary>
    [Fact]
    public async Task A_book_failed_by_anything_else_is_left_alone()
    {
        var blobs = new PolicyFakeBlobs();
        SeedFinishedBook(blobs);

        var pack = BuriedPack();
        pack.ErrorMessage = "IMAGE_QA_FAILED (spread 3): the child's hair does not match";

        var packs = new ReconcilePacks(pack);
        var result = await Reconciliation(packs, blobs, new RecordingAlarms())
            .ReconcilePackAsync(PackId, "a retry", CancellationToken.None);

        Assert.False(result.Restored);
        Assert.Equal(BekiReconcileOutcomes.NotTheSweep, result.Outcome);
        Assert.Equal(AdventurePackStatus.Failed, packs.Pack.Status);
    }

    /// <summary>
    /// A book the sweep killed MID-RUN stays failed, which is what the sweep is for. The
    /// reconciliation is not a retry.
    /// </summary>
    [Fact]
    public async Task A_book_with_a_missing_spread_stays_failed()
    {
        var blobs = new PolicyFakeBlobs();
        SeedFinishedBook(blobs);
        blobs.Remove(BekiPackBlobs.SpreadName(UserId, PackId, 6));

        var packs = new ReconcilePacks(BuriedPack());
        var result = await Reconciliation(packs, blobs, new RecordingAlarms())
            .ReconcilePackAsync(PackId, "a retry", CancellationToken.None);

        Assert.False(result.Restored);
        Assert.Equal(BekiReconcileOutcomes.Incomplete, result.Outcome);
        Assert.Contains("spread 6", result.Detail);
        Assert.Equal(AdventurePackStatus.Failed, packs.Pack.Status);
    }

    /// <summary>And a book with no reading PDF is not a finished book either.</summary>
    [Fact]
    public async Task A_book_with_no_reading_pdf_stays_failed()
    {
        var blobs = new PolicyFakeBlobs();
        SeedFinishedBook(blobs);
        blobs.Remove(BekiPackBlobs.ReadingPdfName(UserId, PackId));

        var packs = new ReconcilePacks(BuriedPack());
        var result = await Reconciliation(packs, blobs, new RecordingAlarms())
            .ReconcilePackAsync(PackId, "a retry", CancellationToken.None);

        Assert.False(result.Restored);
        Assert.Equal(BekiReconcileOutcomes.Incomplete, result.Outcome);
    }

    /// <summary>
    /// A pack that is not Failed has nothing to revive — and a compare-and-set that lost says so
    /// rather than writing over whoever moved it.
    /// </summary>
    [Fact]
    public async Task A_pack_that_is_not_failed_is_not_touched()
    {
        var blobs = new PolicyFakeBlobs();
        SeedFinishedBook(blobs);

        var pack = BuriedPack();
        pack.Status = AdventurePackStatus.Completed;

        var result = await Reconciliation(new ReconcilePacks(pack), blobs, new RecordingAlarms())
            .ReconcilePackAsync(PackId, "a retry", CancellationToken.None);

        Assert.False(result.Restored);
        Assert.Equal(BekiReconcileOutcomes.NotFailed, result.Outcome);
    }

    /// <summary>
    /// A buried book whose gates withhold the download comes back as a readable book with no
    /// download, rather than not coming back at all. The reader is what the family paid for; the
    /// file is what the gates are about.
    /// </summary>
    [Fact]
    public async Task A_book_whose_canonical_pdf_is_withheld_cannot_be_restored_to_completed()
    {
        var blobs = new PolicyFakeBlobs();
        SeedFinishedBook(blobs);

        // A stored verdict that withholds the customer PDF under any policy: the human gate, with
        // review required.
        BekiReleasePolicyGateTests.Seed(blobs, UserId, PackId, needsHumanReading: true);
        var held = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Strict);

        blobs.Seed(
            BekiPackBlobs.ReleaseGatesName(UserId, PackId),
            Encoding.UTF8.GetBytes(held.ToJson()));

        var packs = new ReconcilePacks(BuriedPack());
        var result = await Reconciliation(packs, blobs, new RecordingAlarms())
            .ReconcilePackAsync(PackId, "a retry", CancellationToken.None);

        Assert.False(result.Restored);
        Assert.Equal(AdventurePackStatus.Failed, packs.Pack.Status);
        Assert.True(string.IsNullOrWhiteSpace(packs.Pack.PdfUrl));
    }

    /// <summary>
    /// Amendment B7: a policy that got kinder republishes what it unlocked. This is the shared
    /// publish path the admin approval endpoint uses, exercised through the sweep that calls it.
    /// </summary>
    [Fact]
    public async Task The_withheld_sweep_publishes_what_the_current_policy_unlocks()
    {
        var blobs = new PolicyFakeBlobs();
        SeedFinishedBook(blobs);

        // A book whose only problem is the digital preflight — a blocker yesterday, a flag today.
        BekiReleasePolicyGateTests.Seed(blobs, UserId, PackId);
        blobs.Remove(BekiPackBlobs.DigitalReportName(UserId, PackId));

        var withheld = CompletedPack();
        withheld.PdfUrl = null;

        var packs = new ReconcilePacks(withheld) { Withheld = [withheld] };
        var alarms = new RecordingAlarms();

        var published = await Reconciliation(packs, blobs, alarms)
            .ReconcileWithheldAsync(CancellationToken.None);

        Assert.Equal(1, published);
        Assert.False(string.IsNullOrWhiteSpace(packs.Pack.PdfUrl));

        // The verdict is rewritten under the new policy, so the stored document and the published
        // file agree — and the waiver it now carries raises its alarm.
        var stored = BekiReleaseGateReport.TryParse(
            Encoding.UTF8.GetString(blobs.Get(BekiPackBlobs.ReleaseGatesName(UserId, PackId))!));

        Assert.NotNull(stored);
        Assert.True(stored!.CustomerPdfMayPublish);
        Assert.False(stored.SupplierCustomerPdfReleasable);
        Assert.Contains(alarms.Raised, raise => raise.CheckId == "DIGITAL_GEOMETRY");
    }

    /// <summary>
    /// A book whose gates still withhold is re-judged and left withheld — the sweep publishes what
    /// unlocks, not everything it can reach.
    /// </summary>
    [Fact]
    public async Task The_withheld_sweep_leaves_a_book_that_is_still_blocked_alone()
    {
        var blobs = new PolicyFakeBlobs();
        SeedFinishedBook(blobs);
        BekiReleasePolicyGateTests.Seed(blobs, UserId, PackId);

        // A press gate is a blocker under the shipped defaults, and the reading copy's own render
        // report refuses: the digital slice is flagged, but the missing spread QA below is not.
        blobs.Remove(BekiPackBlobs.SpreadQaName(UserId, PackId, 2));

        var withheld = CompletedPack();
        withheld.PdfUrl = null;

        var packs = new ReconcilePacks(withheld) { Withheld = [withheld] };

        var published = await Reconciliation(
                packs, blobs, new RecordingAlarms(),
                policy: BekiReleasePolicySnapshot.Strict)
            .ReconcileWithheldAsync(CancellationToken.None);

        Assert.Equal(0, published);
        Assert.True(string.IsNullOrWhiteSpace(packs.Pack.PdfUrl));
    }

    /// <summary>
    /// A press gate that got kinder releases the printer's file on a book the family has been
    /// reading for months — review finding 2.
    ///
    /// This is the one direction an operator actually flips a press gate, and it did nothing. The
    /// withheld scan asked only whether the PARENT's download was missing, so a book whose reading
    /// copy published and whose press interior was held by PRESS_RESOLUTION was not in the set at
    /// all: the switch changed the rule for books made after it and left every existing one where it
    /// was. Amendment A5 split the deliverables in two and the reconciliation has always published
    /// them separately; what was missing was being asked.
    /// </summary>
    [Fact]
    public async Task Loosening_a_press_gate_does_not_authorize_manufacturing_a_failed_pdf()
    {
        var blobs = new PolicyFakeBlobs();
        SeedFinishedBook(blobs);
        BekiReleasePolicyGateTests.Seed(blobs, UserId, PackId);

        // A press refusal, and nothing else wrong with the book.
        blobs.Seed(BekiPackBlobs.PressStatusName(UserId, PackId), BekiReleasePolicyGateTests.Json(new
        {
            failed_gates = new[] { "PRESS_RESOLUTION" },
            reason = "the source art carries 143 PPI of detail at placement size",
        }));

        // The family already has their book. Only the printer's file is being held.
        var pack = CompletedPack();
        pack.PdfUrl = "https://blob.test/already-published.pdf";
        pack.PrintPdfUrl = null;

        var packs = new ReconcilePacks(pack) { Withheld = [pack] };

        // The switch, stated explicitly rather than relied on: PRESS_RESOLUTION has been a flag by
        // default since the owner's rule 4 of 2026-09-01, and this test is about the row reaching an
        // already-withheld book rather than about which way the default points.
        var flagged = new BekiReleasePolicySnapshot(
            BekiReleasePolicySnapshot.Defaults.Settings.Append(
                new BekiReleaseCheckSetting(
                    "PRESS_RESOLUTION", BekiReleaseSeverity.AllClasses,
                    BekiReleaseSeverity.Flag, "misho", null)));

        var published = await Reconciliation(packs, blobs, new RecordingAlarms(), flagged)
            .ReconcileWithheldAsync(CancellationToken.None);

        Assert.Null(packs.Pack.PrintPdfUrl);

        // No manufacturing release is counted for a waived-but-failing print gate.
        Assert.Equal(0, published);
    }

    /// <summary>
    /// Every withheld book, not the newest two hundred — review finding 7.
    ///
    /// There was one batch and no loop, so the cap was not a cap on the work per pass: it was the
    /// permanent edge of what a policy change could reach. A deployment with more withheld books
    /// than the batch size had a tail that no operator action would ever touch, and re-running the
    /// scan re-read the same newest two hundred.
    /// </summary>
    [Fact]
    public async Task The_withheld_sweep_walks_past_its_batch_size_until_the_set_is_drained()
    {
        var blobs = new PolicyFakeBlobs();

        // More than one batch of books, each a complete, publishable book of its own.
        const int count = 230;
        var withheld = new List<AdventurePack>();

        for (var index = 0; index < count; index++)
        {
            var packId = Guid.NewGuid();

            SeedFinishedBook(blobs, packId);
            BekiReleasePolicyGateTests.Seed(blobs, UserId, packId);

            withheld.Add(new AdventurePack
            {
                Id = packId,
                UserId = UserId,
                Status = AdventurePackStatus.Completed,
                GenerationPipeline = GenerationPipelines.Beki,
                GeneratedJson = StoryJson(),
                AccessLevel = BookAccessLevel.Full,
                // Distinct creation times, so the keyset cursor has something to walk.
                CreatedAt = DateTime.UtcNow.AddMinutes(-index),
            });
        }

        var packs = new ReconcilePacks(withheld[0]) { Withheld = withheld };

        var published = await Reconciliation(packs, blobs, new RecordingAlarms())
            .ReconcileWithheldAsync(CancellationToken.None);

        Assert.Equal(count, published);
        Assert.All(withheld, pack => Assert.False(string.IsNullOrWhiteSpace(pack.PdfUrl)));

        // A full read and a short one: the batch size is a page, and the short page is the end.
        Assert.Equal(new[] { 200, 30 }, packs.Batches);
    }

    /// <summary>
    /// The download refusal's own question, and the end of the lie the audit found: a Completed book
    /// with no PDF answers "review" or "gates" rather than "story must be ready".
    /// </summary>
    [Fact]
    public async Task A_withheld_download_says_which_kind_of_withholding_it_is()
    {
        var blobs = new PolicyFakeBlobs();
        var packs = new ReconcilePacks(CompletedPack());
        var status = (IBekiDownloadStatusService)Reconciliation(packs, blobs, new RecordingAlarms());

        // No verdict at all — a legacy book, or one fulfilled before the gates existed. Nothing here
        // is holding it, and inventing a reason for a parent to read would be worse than silence.
        Assert.Null(await status.DownloadHeldReasonAsync(UserId, PackId, CancellationToken.None));

        BekiReleasePolicyGateTests.Seed(blobs, UserId, PackId, needsHumanReading: true);
        var awaiting = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Strict);
        blobs.Seed(
            BekiPackBlobs.ReleaseGatesName(UserId, PackId),
            Encoding.UTF8.GetBytes(awaiting.ToJson()));

        Assert.Equal(
            BekiDownloadHeld.Review,
            await status.DownloadHeldReasonAsync(UserId, PackId, CancellationToken.None));

        BekiReleasePolicyGateTests.Seed(blobs, UserId, PackId);
        blobs.Remove(BekiPackBlobs.DigitalReportName(UserId, PackId));
        var failing = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Strict);
        blobs.Seed(
            BekiPackBlobs.ReleaseGatesName(UserId, PackId),
            Encoding.UTF8.GetBytes(failing.ToJson()));

        Assert.Equal(
            BekiDownloadHeld.Gates,
            await status.DownloadHeldReasonAsync(UserId, PackId, CancellationToken.None));

        // And a book the policy published is not held at all, whatever the raw gates say.
        var published = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Defaults);
        blobs.Seed(
            BekiPackBlobs.ReleaseGatesName(UserId, PackId),
            Encoding.UTF8.GetBytes(published.ToJson()));

        Assert.Null(await status.DownloadHeldReasonAsync(UserId, PackId, CancellationToken.None));
    }

    // ==============================================================================================
    // Fixtures
    // ==============================================================================================

    private static BekiReleaseReconciliation Reconciliation(
        ReconcilePacks packs,
        PolicyFakeBlobs blobs,
        RecordingAlarms alarms,
        BekiReleasePolicySnapshot? policy = null) =>
        new(packs,
            blobs,
            new BekiReleaseGates(blobs),
            alarms,
            NullLogger<BekiReleaseReconciliation>.Instance,
            new FixedPolicy(policy ?? BekiReleasePolicySnapshot.Defaults));

    /// <summary>A pack the sweep buried, with the story the parent previewed still on the row.</summary>
    private static AdventurePack BuriedPack() => new()
    {
        Id = PackId,
        UserId = UserId,
        Status = AdventurePackStatus.Failed,
        GenerationPipeline = GenerationPipelines.Beki,
        ErrorMessage = GenerationBudget.StalledReason(TimeSpan.FromMinutes(47)),
        GeneratedJson = StoryJson(),
        AccessLevel = BookAccessLevel.Full,
    };

    private static AdventurePack CompletedPack() => new()
    {
        Id = PackId,
        UserId = UserId,
        Status = AdventurePackStatus.Completed,
        GenerationPipeline = GenerationPipelines.Beki,
        GeneratedJson = StoryJson(),
        AccessLevel = BookAccessLevel.Full,
    };

    /// <summary>
    /// The adopted preview story: eight picture pages and eight text pages, no illustration URLs.
    /// That is exactly what a buried Beki pack carries, because the projection with the URLs in it
    /// is written only by the statement that writes Completed.
    /// </summary>
    private static string StoryJson()
    {
        var pages = new List<StoryPageDto>();

        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            // The picture half, with no URL on it — which is the state a buried pack is in.
            pages.Add(new StoryPageDto { Title = $"სცენა {spread}" });

            // And the prose half, which is never illustrated.
            pages.Add(new StoryPageDto
            {
                Title = $"სცენა {spread}",
                Content = $"გვერდი {spread}",
                IsTextOnlyPage = true,
            });
        }

        return JsonSerializer.Serialize(
            new AdventureContentDto { Title = "ნინა და დინოზავრები", StoryPages = pages },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    /// <summary>
    /// The spreads and the two PDFs, for this file's own pack or for a named one — the drain test
    /// seeds a couple of hundred books and every one of them has to be a real book in storage.
    /// </summary>
    private static void SeedFinishedBook(PolicyFakeBlobs blobs, Guid? packId = null)
    {
        var id = packId ?? PackId;

        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            blobs.Seed(BekiPackBlobs.SpreadName(UserId, id, spread), [(byte)spread]);
        }

        blobs.Seed(BekiPackBlobs.ReadingPdfName(UserId, id), [9]);
        blobs.Seed(BekiPackBlobs.InteriorPdfName(UserId, id), [8]);
        blobs.Seed(BekiPackBlobs.ReleaseGatesName(UserId, id),
            Encoding.UTF8.GetBytes(new BekiReleaseGateReport
            {
                Verdict = BekiReleaseGates.Releasable,
                EvaluatedAtUtc = DateTimeOffset.UtcNow,
                Gates = [], FailingGates = [], AwaitingHumanReview = false,
            }.ToJson()));
    }
}

// ==================================================================================================
// Doubles
//
// File-scope and internal rather than nested and private, because the approval endpoint's tests
// need exactly these three: a packs table with the compare-and-set and the withheld scan in it, a
// policy that answers with one fixed reading, and somewhere for alarms to land. Copying a
// twenty-six-member repository double into a second file to say the same thing would be two places
// to update the next time the interface grows.
// ==================================================================================================

internal sealed class FixedPolicy(BekiReleasePolicySnapshot snapshot) : IBekiReleasePolicyService
{
    public Task<BekiReleasePolicySnapshot> SnapshotAsync(CancellationToken ct) =>
        Task.FromResult(snapshot);

    public Task<IReadOnlyList<BekiReleaseCheckSetting>> ListAsync(CancellationToken ct) =>
        Task.FromResult(snapshot.Settings);

    public Task<int> SetAsync(
        string checkId, string deliverableClass, string severity, string updatedBy,
        CancellationToken ct) => throw new NotSupportedException();
}

internal sealed class RecordingAlarms : IBekiAlarmService
{
    public List<BekiAlarmRaise> Raised { get; } = [];

    public Task RaiseAsync(BekiAlarmRaise raise, CancellationToken ct)
    {
        Raised.Add(raise);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BekiAlarm>>([]);

    public Task<IReadOnlyList<BekiAlarm>> ListRecentAsync(int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BekiAlarm>>([]);

    public Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BekiAlarm>>([]);

    public Task<BekiAlarm?> GetAsync(Guid alarmId, CancellationToken ct) =>
        Task.FromResult<BekiAlarm?>(null);

    public Task<bool> ReviewAsync(
        Guid alarmId, string reviewedBy, string resolution, CancellationToken ct) =>
        Task.FromResult(false);

    public Task<int> CountOpenAsync(CancellationToken ct) => Task.FromResult(Raised.Count);
}

/// <summary>
/// The packs table's slice of itself that the two rescues touch: the compare-and-set, the two
/// URL columns, and the withheld scan. Everything else throws — a rescue that started calling
/// something new should fail loudly rather than pass.
///
/// The withheld scan is EMULATED rather than stubbed, because its shape is the thing under test
/// in the drain case: the same predicate the SQL uses, the same newest-first ordering, the same
/// keyset cursor, the same TOP. A stub that handed back a fixed list would let a reconciliation
/// that reads only the first page look exactly like one that reads them all.
/// </summary>
internal sealed class ReconcilePacks(AdventurePack pack) : IAdventurePackRepository
{
    public AdventurePack Pack { get; } = pack;

    public IReadOnlyList<AdventurePack> Withheld { get; init; } = [];

    /// <summary>How many rows each batch handed back, in order — one entry per read.</summary>
    public List<int> Batches { get; } = [];

    public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Find(id));

    public Task<bool> TryUpdateStatusAsync(
        Guid id, AdventurePackStatus expectedStatus, AdventurePackStatus status,
        string? generatedJson, string? pdfUrl, string? errorMessage,
        CancellationToken cancellationToken)
    {
        if (Find(id) is not { } row || row.Status != expectedStatus)
        {
            return Task.FromResult(false);
        }

        row.Status = status;
        row.GeneratedJson = generatedJson;
        row.PdfUrl = pdfUrl;
        row.ErrorMessage = errorMessage;

        return Task.FromResult(true);
    }

    public Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken)
    {
        if (Find(id) is { } row)
        {
            row.PrintPdfUrl = printPdfUrl;
        }

        return Task.CompletedTask;
    }

    public Task UpdateProgressAsync(
        Guid id, string? progressMessage, int? progressPercent, CancellationToken cancellationToken)
    {
        if (Find(id) is { } row)
        {
            row.ProgressMessage = progressMessage;
            row.ProgressPercent = progressPercent;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AdventurePack>> ListWithheldBekiPacksAsync(
        int limit, BekiWithheldCursor? after, CancellationToken cancellationToken)
    {
        // Either column missing is withheld — amendment A5's split, which the SQL asked only
        // half of until review finding 2.
        var page = Withheld
            .Where(row => string.IsNullOrWhiteSpace(row.PdfUrl)
                          || string.IsNullOrWhiteSpace(row.PrintPdfUrl))
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .Where(row => after is not { } cursor
                          || row.CreatedAt < cursor.CreatedAtUtc
                          || (row.CreatedAt == cursor.CreatedAtUtc
                              && row.Id.CompareTo(cursor.PackId) < 0))
            .Take(limit)
            .ToList();

        Batches.Add(page.Count);

        return Task.FromResult<IReadOnlyList<AdventurePack>>(page);
    }

    private AdventurePack? Find(Guid id) =>
        Withheld.FirstOrDefault(row => row.Id == id) ?? (Pack.Id == id ? Pack : null);

    public Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(Guid characterId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> SetAccessLevelAsync(Guid id, BookAccessLevel accessLevel, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task UpdateBookPresentationAsync(Guid id, string? title, string? coverImageUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(DateTime cutoffUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> TryFailStaleGenerationAsync(Guid id, AdventurePackStatus expectedStatus, DateTime cutoffUtc, string errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> TryFailAsync(Guid id, AdventurePackStatus expectedStatus, string errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task UpdateTitleAsync(Guid id, string title, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task UpdatePreviewIllustrationAsync(Guid id, PreviewIllustrationStatus status, string? illustrationUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> TryClaimPreviewIllustrationGenerationAsync(Guid id, int staleAfterMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> UpdateGeneratedJsonAsync(Guid id, string generatedJson, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task SetGenerationPipelineAsync(Guid id, string pipeline, CancellationToken cancellationToken) => throw new NotSupportedException();
}
