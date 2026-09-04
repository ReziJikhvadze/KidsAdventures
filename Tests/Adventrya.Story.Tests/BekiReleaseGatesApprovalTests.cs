using System.Text;
using AdventurePacks.Api.Controllers;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
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
    // Harness
    // ==============================================================================================

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
