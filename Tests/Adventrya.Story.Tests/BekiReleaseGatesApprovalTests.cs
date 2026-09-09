using System.Text;
using AdventurePacks.Api.Controllers;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The admin approval endpoint, and the policy it judges a book under — review finding 1.
///
/// Approving a contact sheet does not just record a signature: it RE-RUNS the whole gate evaluation
/// against stored artifacts and overwrites the book's verdict with the result. That is the right
/// design — the evaluation is deliberately built to be re-answerable hours or days later — but it
/// makes the endpoint the second place in the system where a policy decides what a family gets, and
/// it was the one place that never read the policy.
///
/// The evaluator's policy argument was optional and fell back to the shipped defaults, so an
/// operator's override was silently discarded at exactly the moment it mattered. Both directions
/// were wrong and both are pinned below: a check they had TIGHTENED published a book they had asked
/// to hold, and a check they had LOOSENED kept holding a file they had asked to release.
/// </summary>
public class BekiReleaseGatesApprovalTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PackId = Guid.NewGuid();
    private static readonly Guid OrderId = Guid.NewGuid();

    /// <summary>
    /// A check the operator made STRICTER still holds the book after a reviewer signs it off.
    ///
    /// DIGITAL_GEOMETRY ships as a flag, so under the shipped defaults a missing digital preflight
    /// is waived and the download goes out. A deployment that has made it a blocker has said the
    /// opposite, in the admin console, on purpose. Before this fix the approval endpoint re-judged
    /// the book under the defaults and published it — over the operator's own decision, and writing
    /// a stored verdict that recorded a waiver nobody had granted.
    /// </summary>
    [Fact]
    public async Task An_approval_honours_a_check_the_operator_tightened()
    {
        var blobs = new PolicyFakeBlobs();
        BekiReleasePolicyGateTests.Seed(blobs, UserId, PackId, needsHumanReading: true);

        // The one thing wrong with this book, and a gate this deployment has chosen to block on.
        blobs.Remove(BekiPackBlobs.DigitalReportName(UserId, PackId));

        var policy = With("DIGITAL_GEOMETRY", BekiReleaseSeverity.Blocker);
        var sheet = await StoreVerdictAsync(blobs, policy);

        var packs = new ReconcilePacks(WithheldPack());
        var response = await Approve(packs, blobs, policy, sheet);

        // The signature is recorded and the book is still held, because the operator said so.
        Assert.False(response.CustomerPdfPublished);
        Assert.True(string.IsNullOrWhiteSpace(packs.Pack.PdfUrl));

        // And the verdict the endpoint rewrote says the same thing — no waiver was granted, so the
        // document a person reads in six months does not claim one was.
        var rewritten = Stored(blobs);
        Assert.False(rewritten.CustomerPdfMayPublish);
        Assert.DoesNotContain(rewritten.PolicyWaivers, waiver => waiver.CheckId == "DIGITAL_GEOMETRY");

        // The human gate itself did pass: the approval was real, and the withholding is the
        // operator's rule rather than a missing signature.
        Assert.False(rewritten.AwaitingHumanReview);
    }

    /// <summary>
    /// The other direction, which fails just as quietly: a check the operator made KINDER releases
    /// the file it was holding.
    ///
    /// A press gate a deployment has flagged should release the printer's file when the book is
    /// approved, and under the shipped defaults it did not — the operator was left with a switch
    /// that appeared to do nothing.
    ///
    /// PRESS_RESOLUTION is the example because it is the one press gate the owner has since ruled a
    /// flag by default (rule 4, 2026-09-01: the sizes we indicated for printing are correct). The
    /// row is stated explicitly here anyway, so this test keeps saying what it is about — an
    /// operator's setting reaching an approval — rather than depending on which way the default
    /// happens to point.
    /// </summary>
    [Fact]
    public async Task Customer_approval_does_not_waive_a_manufacturing_failure()
    {
        var blobs = new PolicyFakeBlobs();
        BekiReleasePolicyGateTests.Seed(blobs, UserId, PackId, needsHumanReading: true);
        blobs.Seed(BekiPackBlobs.InteriorPdfName(UserId, PackId), [8]);

        blobs.Seed(BekiPackBlobs.PressStatusName(UserId, PackId), BekiReleasePolicyGateTests.Json(new
        {
            failed_gates = new[] { "PRESS_RESOLUTION" },
            reason = "the source art carries 143 PPI of detail at placement size",
        }));

        var policy = With("PRESS_RESOLUTION", BekiReleaseSeverity.Flag);
        var sheet = await StoreVerdictAsync(blobs, policy);

        var packs = new ReconcilePacks(WithheldPack());
        var response = await Approve(packs, blobs, policy, sheet);

        Assert.False(response.PressFilesPublished);
        Assert.Null(packs.Pack.PrintPdfUrl);

        // The waiver is written down, because a press gate that was let through is exactly the kind
        // of decision that has to be answerable later.
        Assert.Contains(
            Stored(blobs).PolicyWaivers,
            waiver => waiver.CheckId == "PRESS_RESOLUTION"
                      && waiver.DeliverableClass == BekiReleaseGates.PressClass);
    }

    // ==============================================================================================
    // The signature on a book no model reviewed — the print-release fault, 2026-09-06
    //
    // Owner's rule 5 turns the per-spread model review off, so every spread record on every book
    // this deployment produces says REVIEW_SKIPPED_BY_POLICY. That kept VISUAL_QA off PASS;
    // SupplierPressReleasable is policy-blind and wants every gate passing; PrintReady is gated on
    // it. So the printer's files were unreachable for every book, and no approval could change that
    // — while AwaitingHumanReview, computed from a NEEDS_HUMAN nobody raised, was false, so the
    // console offered no signature either. Observed on a real book: approved, verdict still
    // NOT_RELEASABLE with VISUAL_QA the only non-PASS gate, print download 409.
    // ==============================================================================================

    /// <summary>
    /// A person looked at the contact sheet, so the gate about whether the artwork was looked at
    /// says PASS — and names who looked, at which rendering, and when.
    ///
    /// This is not a waiver. A waiver is B2's separate field and leaves the status alone; what the
    /// approval supplies here is the missing evidence itself. The reviewer the deployment employs is
    /// a human rather than a model, and amendment A2 binds their signature to the exact pixels.
    /// </summary>
    [Fact]
    public async Task A_signature_closes_the_visual_gate_on_a_book_no_model_reviewed()
    {
        var blobs = new PolicyFakeBlobs();
        SeedReviewSkipped(blobs);
        SeedApproval(blobs, BekiReleasePolicyGateTests.ContactSheetSha);

        var verdict = await Evaluate(blobs);
        var gate = Assert.Single(verdict.Gates, g => g.Id == "VISUAL_QA");

        Assert.Equal(BekiReleaseGates.Pass, gate.Status);
        Assert.Contains("reviewer@beki.ge", gate.Detail, StringComparison.Ordinal);

        // And it still says out loud that no model judged the pages, which stays true.
        Assert.Contains(
            CompositeBookPipeline.ReviewSkippedStatus, gate.Detail, StringComparison.Ordinal);
        Assert.Contains(BekiPackBlobs.HumanApprovalName(UserId, PackId), gate.Evidence);

        // The point of the whole change: the printer's files are reachable.
        Assert.True(verdict.SupplierPressReleasable);
        Assert.True(verdict.PrintReady);
        Assert.False(verdict.PrintAwaitingHumanApproval);
        Assert.False(verdict.AwaitingHumanReview);
        Assert.DoesNotContain("VISUAL_QA", verdict.FailingGates);
    }

    /// <summary>
    /// With no signature the gate says exactly what it said before, word for word — and the console
    /// is now told that IT is the thing standing between this book and a printer.
    ///
    /// The family's copy is untouched either way: VISUAL_QA is a shared gate the policy flags, so
    /// the ordinary waiver publishes the reading copy exactly as it always did.
    /// </summary>
    [Fact]
    public async Task Without_a_signature_the_skip_says_exactly_what_it_always_said()
    {
        var blobs = new PolicyFakeBlobs();
        SeedReviewSkipped(blobs);

        var verdict = await Evaluate(blobs);
        var gate = Assert.Single(verdict.Gates, g => g.Id == "VISUAL_QA");

        Assert.Equal(BekiReleaseGates.ReviewSkipped, gate.Status);
        Assert.Equal(SkipDetail, gate.Detail);

        Assert.False(verdict.PrintReady);
        Assert.True(verdict.PrintAwaitingHumanApproval);

        // NOT the NEEDS_HUMAN flag: nothing refused these pages and no gate raised that status.
        // Widening it would put a wrong sentence in the handback and in the held-download reason.
        Assert.False(verdict.AwaitingHumanReview);

        Assert.True(verdict.CustomerPdfMayPublish);
    }

    /// <summary>
    /// A signature on a different rendering closes nothing — amendment A2, applied to the new
    /// clause as it already was to the old one.
    /// </summary>
    [Fact]
    public async Task A_stale_signature_does_not_close_the_skip()
    {
        var blobs = new PolicyFakeBlobs();
        SeedReviewSkipped(blobs);
        SeedApproval(blobs, new string('b', 64));

        var verdict = await Evaluate(blobs);
        var gate = Assert.Single(verdict.Gates, g => g.Id == "VISUAL_QA");

        Assert.Equal(BekiReleaseGates.ReviewSkipped, gate.Status);
        Assert.Equal(SkipDetail, gate.Detail);
        Assert.False(verdict.PrintReady);
        Assert.True(verdict.PrintAwaitingHumanApproval);
        Assert.False(verdict.AwaitingHumanReview);
    }

    /// <summary>
    /// Review finding 4's ordering survives: a book that ALSO carries a human-review flag is a
    /// person's queue first and a skip second, so the status is NEEDS_HUMAN and the new flag is
    /// false — one book is never in both queues, and the console's existing branch already has it.
    /// </summary>
    [Fact]
    public async Task An_advisory_still_outranks_the_skip_when_nobody_has_signed()
    {
        var blobs = new PolicyFakeBlobs();
        BekiReleasePolicyGateTests.Seed(blobs, UserId, PackId, needsHumanReading: true);
        SeedSkippedSpreads(blobs);

        var verdict = await Evaluate(blobs);
        var gate = Assert.Single(verdict.Gates, g => g.Id == "VISUAL_QA");

        Assert.Equal(BekiReleaseGates.NeedsHuman, gate.Status);
        Assert.True(verdict.AwaitingHumanReview);
        Assert.False(verdict.PrintAwaitingHumanApproval);
    }

    /// <summary>
    /// The flag is written down and read back, and a report from before it existed reads false —
    /// which is the right answer for one: it was judged under an evaluation where the signature
    /// closed nothing anyway.
    /// </summary>
    [Fact]
    public void The_print_flag_round_trips_and_defaults_on_an_older_report()
    {
        var report = new BekiReleaseGateReport
        {
            Verdict = BekiReleaseGates.NotReleasable,
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            Gates = [],
            AwaitingHumanReview = false,
            PrintAwaitingHumanApproval = true,
            FailingGates = ["VISUAL_QA"],
        };

        var json = report.ToJson();
        Assert.Contains("\"print_awaiting_human_approval\": true", json, StringComparison.Ordinal);
        Assert.True(BekiReleaseGateReport.TryParse(json)!.PrintAwaitingHumanApproval);

        var older = BekiReleaseGateReport.TryParse(
            """
            {
              "schema": "beki-release-gates-v1",
              "verdict": "NOT_RELEASABLE",
              "evaluated_at_utc": "2026-09-01T00:00:00+00:00",
              "gates": [],
              "awaiting_human_review": false,
              "failing_gates": ["VISUAL_QA"]
            }
            """)!;

        Assert.False(older.PrintAwaitingHumanApproval);
    }

    /// <summary>
    /// End to end through the endpoint the console calls: the approval is recorded, the verdict is
    /// rewritten, the printer's file is published, and the response no longer asks for a signature —
    /// all in the one request, which is what the endpoint's atomicity claim is about.
    ///
    /// On a CANONICAL book, which is the only kind this deployment now makes: one PDF, an integrity
    /// record beside it, and no legacy press interior anywhere in storage. Seeding the interior here
    /// would have made the test pass over the fault it was written to catch — the publish path
    /// looked for the printer's file under the legacy name only, so a signature on a real book wrote
    /// nothing and the print download went on answering 409.
    /// </summary>
    [Fact]
    public async Task Approving_a_skipped_book_writes_the_print_url_in_one_request()
    {
        var blobs = new PolicyFakeBlobs();
        SeedReviewSkipped(blobs);
        blobs.Seed(BekiPackBlobs.ReadingPdfName(UserId, PackId), [7]);
        blobs.Seed(BekiPackBlobs.InteriorPdfName(UserId, PackId), [8]);

        var policy = BekiReleasePolicySnapshot.Defaults;
        var sheet = await StoreVerdictAsync(blobs, policy);

        // What the console reads before anyone signs: no NEEDS_HUMAN, and yet a signature wanted.
        var before = Stored(blobs);
        Assert.False(before.AwaitingHumanReview);
        Assert.True(before.PrintAwaitingHumanApproval);
        Assert.False(before.PrintReady);

        var packs = new ReconcilePacks(WithheldPack());
        var response = await Approve(packs, blobs, policy, sheet);

        Assert.False(response.PrintAwaitingHumanApproval);
        Assert.True(response.PressFilesPublished);

        // The separately prepared print PDF is published; the customer copy is preserved.
        Assert.Equal(
            $"https://blob.test/{BekiPackBlobs.InteriorPdfName(UserId, PackId)}",
            packs.Pack.PrintPdfUrl);
        Assert.True(blobs.Has(BekiPackBlobs.ReadingPdfName(UserId, PackId)));
    }

    // ==============================================================================================
    // Harness
    // ==============================================================================================

    /// <summary>The skip clause's text, which this change is required to leave untouched.</summary>
    private static readonly string SkipDetail =
        "no visual review was performed on spread(s) "
        + string.Join(", ", Enumerable.Range(1, BookFormat.SpreadCount))
        + $": the stored record for each says {CompositeBookPipeline.ReviewSkippedStatus} "
        + "(release policy check 'image_review'). Every page has a record and the "
        + "deterministic checks passed; no model judged the artwork.";

    /// <summary>The book this deployment actually makes: complete, and reviewed by no model.</summary>
    private static void SeedReviewSkipped(PolicyFakeBlobs blobs)
    {
        BekiReleasePolicyGateTests.Seed(blobs, UserId, PackId);
        SeedSkippedSpreads(blobs);
    }

    /// <summary>The pipeline's own skip record, written by the pipeline's own writer.</summary>
    private static void SeedSkippedSpreads(PolicyFakeBlobs blobs)
    {
        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            blobs.Seed(
                BekiPackBlobs.SpreadQaName(UserId, PackId, spread),
                Encoding.UTF8.GetBytes(CompositeSpreadQa.WriteSkipped(
                    spread, "pose_01_neutral_hover", "left", 1, BekiReleaseSeverity.Flag)));
        }
    }

    private static void SeedApproval(PolicyFakeBlobs blobs, string sheet) =>
        blobs.Seed(
            BekiPackBlobs.HumanApprovalName(UserId, PackId),
            Encoding.UTF8.GetBytes(new BekiHumanApproval(
                "reviewer@beki.ge", DateTimeOffset.UtcNow, sheet, "looked at every page").ToJson()));

    private static Task<BekiReleaseGateReport> Evaluate(PolicyFakeBlobs blobs) =>
        new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, BekiReleasePolicySnapshot.Defaults);

    /// <summary>The shipped policy with one check overridden, which is all an operator ever does.</summary>
    private static BekiReleasePolicySnapshot With(string checkId, string severity) =>
        new(BekiReleasePolicySnapshot.Defaults.Settings.Append(
            new BekiReleaseCheckSetting(
                checkId, BekiReleaseSeverity.AllClasses, severity, "misho@example.test", null)));

    /// <summary>
    /// The verdict as the fulfilment job left it, stored where the endpoint reads it, and the
    /// contact sheet a reviewer would be signing.
    /// </summary>
    private static async Task<string> StoreVerdictAsync(
        PolicyFakeBlobs blobs, BekiReleasePolicySnapshot policy)
    {
        var report = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy);

        blobs.Seed(
            BekiPackBlobs.ReleaseGatesName(UserId, PackId),
            Encoding.UTF8.GetBytes(report.ToJson()));

        Assert.False(string.IsNullOrWhiteSpace(report.ContactSheetSha256));

        return report.ContactSheetSha256!;
    }

    private static BekiReleaseGateReport Stored(PolicyFakeBlobs blobs) =>
        BekiReleaseGateReport.TryParse(
            Encoding.UTF8.GetString(blobs.Get(BekiPackBlobs.ReleaseGatesName(UserId, PackId))!))!;

    private static async Task<AdminReleaseGatesResponse> Approve(
        ReconcilePacks packs, PolicyFakeBlobs blobs, BekiReleasePolicySnapshot policy, string sheet)
    {
        var gates = new BekiReleaseGates(blobs);
        var reconciliation = new BekiReleaseReconciliation(
            packs, blobs, gates, new RecordingAlarms(),
            NullLogger<BekiReleaseReconciliation>.Instance, new FixedPolicy(policy));

        /*
          The collaborators this action never touches are null on purpose.

          ApproveReview reads the order, the pack, storage, the gates, the policy and the alarms;
          the PDF builder, the package export, the order service and repository, the print queue
          and the redraw belong to other routes on the same controller. Standing up doubles for
          them would say this action might use them.
        */
        var controller = new AdminOrdersController(
            new FakeReporting(),
            packs,
            orderRepository: null!,
            printOrders: null!,
            blobs,
            generationService: null!,
            orderService: null!,
            regeneration: null!,
            packageExport: null!,
            gates,
            reconciliation,
            new FixedPolicy(policy),
            new RecordingAlarms(),
            new ApproverContext(),
            NullLogger<AdminOrdersController>.Instance);

        var result = await controller.ApproveReview(
            OrderId,
            new AdminApproveReviewRequest { ContactSheetSha256 = sheet, Note = "looks right" },
            CancellationToken.None);

        return (AdminReleaseGatesResponse)Assert.IsType<OkObjectResult>(result.Result).Value!;
    }

    /// <summary>A finished book whose deliverables are both still withheld.</summary>
    private static AdventurePack WithheldPack() => new()
    {
        Id = PackId,
        UserId = UserId,
        Status = AdventurePackStatus.Completed,
        GenerationPipeline = GenerationPipelines.Beki,
        GeneratedJson = """{"title":"ნინა და დინოზავრები","storyPages":[]}""",
        AccessLevel = BookAccessLevel.Full,
    };

    /// <summary>One order, pointing at the one book. The console reaches a pack through this.</summary>
    private sealed class FakeReporting : IAdminReportingRepository
    {
        public Task<AdminOrderDetailResponse?> GetOrderDetailAsync(
            Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult<AdminOrderDetailResponse?>(
                orderId == OrderId
                    ? new AdminOrderDetailResponse { Book = new AdminOrderBook { Id = PackId } }
                    : null);

        public Task<AdminOrderListResponse> GetOrdersAsync(
            string? status, string? search, string? flag, int page, int pageSize,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AdminCustomerListResponse> GetCustomersAsync(
            string? search, int page, int pageSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ApproverContext : IUserContextService
    {
        public Guid GetUserId() => Guid.Parse("22222222-2222-2222-2222-222222222222");

        public string GetEmail() => "reviewer@beki.ge";
    }
}
