using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The release policy: what a check does, who can change it, and what happens when they do.
///
/// Owner ruling, 2026-09-01: "books are generated and delivered to parents almost always; problems
/// become admin alarms to check later, not blocks; human visual review is skipped by default and can
/// be turned on from admin; every check is admin-markable as a BLOCKER or a FLAG."
///
/// The tests below are about the four sentences of that ruling in order, plus the one thing the
/// ruling does not say and amendment B2 does: the same check can mean different things about
/// different files, because a render failure on a press PDF is a printer's invoice and the same
/// failure on the reading copy is a screen.
/// </summary>
public class BekiReleasePolicyTests
{
    /// <summary>
    /// The shipped defaults ARE the ruling: every quality check a flag, review skipped, the parent's
    /// deliverable governed leniently, the printer's strictly.
    /// </summary>
    [Fact]
    public void The_shipped_defaults_are_the_owners_ruling()
    {
        var policy = BekiReleasePolicySnapshot.Defaults;

        Assert.False(policy.HumanReviewRequired);

        foreach (var check in BekiReleaseChecks.Pipeline)
        {
            Assert.Equal(BekiReleaseSeverity.Flag, policy.SeverityOf(check));
        }

        // The press gates are the exception the ruling itself carves out — three of them, since
        // 2026-09-01.
        Assert.Equal(BekiReleaseSeverity.Blocker, policy.SeverityOf("PRESS_GEOMETRY"));
        Assert.Equal(BekiReleaseSeverity.Blocker, policy.SeverityOf("PRESS_COLOR"));
        Assert.Equal(BekiReleaseSeverity.Blocker, policy.SeverityOf("TEXT_COLOR_INTEGRITY"));

        /*
          And the fourth is a flag — owner's rule 4, 2026-09-01: the sizes we indicated for printing
          are correct.

          PRESS_RESOLUTION was measuring the approved artwork against a placement resolution the
          format does not require, and its refusal was withholding the printer's file on books whose
          art is the art we approved. Its neighbours keep their blockers because they are about what
          a press does with a file — geometry, colour, ink. What does not change is the raw report:
          a failing PRESS_RESOLUTION still fails, still names the file, and still makes the handback
          NOT_RELEASABLE. See A_press_gate_still_blocks_under_the_shipped_defaults below for both
          halves of that.
        */
        Assert.Equal(BekiReleaseSeverity.Flag, policy.SeverityOf("PRESS_RESOLUTION"));

        // The image reviewer, off by the same ruling's rule 5: "we don't need additional reviews
        // for images". Blocker means the per-spread vision call is bought.
        Assert.False(policy.ImageReviewRequired);
        Assert.Equal(BekiReleaseSeverity.Flag, policy.SeverityOf(BekiReleaseChecks.ImageReview));

        // And the shared and digital gates flag, so a book with artwork in hand reaches the family.
        Assert.Equal(BekiReleaseSeverity.Flag, policy.SeverityOf("VISUAL_QA"));
        Assert.Equal(BekiReleaseSeverity.Flag, policy.SeverityOf("DIGITAL_GEOMETRY"));
    }

    /// <summary>
    /// Amendment B2's whole reason for the two-part key: RENDER_VALIDATION and QR are answered per
    /// artifact, and the artifacts belong to deliverables with different economics.
    /// </summary>
    [Fact]
    public void The_two_per_artifact_gates_resolve_per_deliverable_class()
    {
        var policy = BekiReleasePolicySnapshot.Defaults;

        Assert.Equal(
            BekiReleaseSeverity.Blocker,
            policy.SeverityOf("RENDER_VALIDATION", BekiReleaseGates.PressClass));
        Assert.Equal(
            BekiReleaseSeverity.Flag,
            policy.SeverityOf("RENDER_VALIDATION", BekiReleaseGates.DigitalClass));

        Assert.Equal(BekiReleaseSeverity.Blocker, policy.SeverityOf("QR", BekiReleaseGates.PressClass));
        Assert.Equal(BekiReleaseSeverity.Flag, policy.SeverityOf("QR", BekiReleaseGates.DigitalClass));
    }

    /// <summary>
    /// The resolution order: the exact class first, the wildcard next, the code default last. A row
    /// for one class does not silently govern the other.
    /// </summary>
    [Fact]
    public void A_class_row_wins_over_the_wildcard_and_the_wildcard_over_the_default()
    {
        var policy = new BekiReleasePolicySnapshot(
        [
            new BekiReleaseCheckSetting("VISUAL_QA", BekiReleaseSeverity.AllClasses, BekiReleaseSeverity.Blocker, "misho", null),
            new BekiReleaseCheckSetting("VISUAL_QA", BekiReleaseGates.DigitalClass, BekiReleaseSeverity.Flag, "misho", null),
        ]);

        Assert.Equal(
            BekiReleaseSeverity.Flag, policy.SeverityOf("VISUAL_QA", BekiReleaseGates.DigitalClass));
        Assert.Equal(
            BekiReleaseSeverity.Blocker, policy.SeverityOf("VISUAL_QA", BekiReleaseGates.PressClass));

        // A check with no row of any kind falls through to the shipped default rather than to
        // silence — the case of a fresh database, or a check a later campaign mints.
        Assert.Equal(BekiReleaseSeverity.Flag, policy.SeverityOf("SOME_FUTURE_CHECK"));
        Assert.Equal(BekiReleaseSeverity.Blocker, policy.SeverityOf("PRESS_GEOMETRY"));
    }

    /// <summary>
    /// <see cref="BekiReleasePolicySnapshot.Strict"/> is the behaviour this system had before the
    /// policy existed, kept as one word a caller can say.
    /// </summary>
    [Fact]
    public void The_strict_snapshot_blocks_everything()
    {
        var policy = BekiReleasePolicySnapshot.Strict;

        Assert.True(policy.HumanReviewRequired);
        Assert.All(
            BekiReleaseChecks.Pipeline,
            check => Assert.Equal(BekiReleaseSeverity.Blocker, policy.SeverityOf(check)));
        Assert.Equal(
            BekiReleaseSeverity.Blocker,
            policy.SeverityOf("RENDER_VALIDATION", BekiReleaseGates.DigitalClass));
    }

    /// <summary>
    /// A stored row overrides the default, which is the whole of "admin-markable" — and the reading
    /// is a snapshot, so a change mid-flight does not reach a book already being judged.
    /// </summary>
    [Fact]
    public async Task A_stored_row_overrides_the_default_and_a_snapshot_is_a_reading()
    {
        var repository = new FakePolicyRepository();
        var service = new BekiReleasePolicyService(
            repository, Scopes(new RecordingReconciliation()),
            NullLogger<BekiReleasePolicyService>.Instance);

        var before = await service.SnapshotAsync(CancellationToken.None);
        Assert.False(before.HumanReviewRequired);

        await service.SetAsync(
            BekiReleaseChecks.HumanReview, BekiReleaseSeverity.AllClasses,
            BekiReleaseSeverity.Blocker, "misho@example.test", CancellationToken.None);

        var after = await service.SnapshotAsync(CancellationToken.None);
        Assert.True(after.HumanReviewRequired);

        // The snapshot taken before the change is a reading and does not move under the caller —
        // amendment B4, and the reason a fulfilment job takes one rather than asking repeatedly.
        Assert.False(before.HumanReviewRequired);

        var stored = Assert.Single(repository.Rows);
        Assert.Equal(BekiReleaseChecks.HumanReview, stored.CheckId);
        Assert.Equal("misho@example.test", stored.UpdatedBy);
    }

    /// <summary>
    /// Amendment B7: a policy change ACTS. Flipping a check to flag re-judges the books already
    /// sitting withheld, because leaving them withheld is exactly what the operator was trying to
    /// stop.
    ///
    /// ONCE, and the count comes back — review finding 3. This used to start the reconciliation
    /// fire-and-forget on the reasoning that a console request must not wait on a scan, while the
    /// controller awaited a second reconciliation of its own so that it would have a number to
    /// report. The request waited either way; what the arrangement bought was two concurrent scans
    /// over one set of books and a reported figure that was whichever share the controller's copy
    /// won. Every write is compare-and-set, so no family got a book twice — the damage was to the
    /// one sentence the operator reads afterwards.
    /// </summary>
    [Fact]
    public async Task Setting_a_check_runs_the_withheld_reconciliation_once_and_returns_its_count()
    {
        var reconciliation = new RecordingReconciliation { Published = 7 };
        var service = new BekiReleasePolicyService(
            new FakePolicyRepository(), Scopes(reconciliation),
            NullLogger<BekiReleasePolicyService>.Instance);

        var published = await service.SetAsync(
            "DIGITAL_GEOMETRY", BekiReleaseSeverity.AllClasses, BekiReleaseSeverity.Flag,
            "misho@example.test", CancellationToken.None);

        // Awaited rather than raced: by the time SetAsync returns, the scan has run and been counted.
        Assert.True(reconciliation.Ran.Task.IsCompletedSuccessfully);
        Assert.Equal(1, reconciliation.Calls);
        Assert.Equal(7, published);
    }

    /// <summary>
    /// A reconciliation that will not run does not un-write the setting.
    ///
    /// The operator's decision is recorded the moment the row lands; the scan is a follow-up. Rolling
    /// the row back because the follow-up failed would leave an admin who clicked a switch with no
    /// switch and no explanation, and the withheld books get re-judged by the next pass anyway.
    /// </summary>
    [Fact]
    public async Task A_reconciliation_that_fails_leaves_the_setting_stored()
    {
        var repository = new FakePolicyRepository();
        var service = new BekiReleasePolicyService(
            repository, Scopes(new ThrowingReconciliation()),
            NullLogger<BekiReleasePolicyService>.Instance);

        var published = await service.SetAsync(
            BekiReleaseChecks.HumanReview, BekiReleaseSeverity.AllClasses,
            BekiReleaseSeverity.Blocker, "misho@example.test", CancellationToken.None);

        Assert.Equal(0, published);
        Assert.Single(repository.Rows);
        Assert.True((await service.SnapshotAsync(CancellationToken.None)).HumanReviewRequired);
    }

    /// <summary>A severity that is neither word is refused rather than coerced into one.</summary>
    [Fact]
    public async Task An_unknown_severity_is_refused()
    {
        var service = new BekiReleasePolicyService(
            new FakePolicyRepository(), Scopes(new RecordingReconciliation()),
            NullLogger<BekiReleasePolicyService>.Instance);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetAsync(
            "VISUAL_QA", BekiReleaseSeverity.AllClasses, "warn", "misho", CancellationToken.None));
    }

    /// <summary>
    /// A policy table that cannot be read answers with the shipped defaults.
    ///
    /// The alternative — refusing to evaluate — would mean a database hiccup withholds every
    /// deliverable in flight, which is the exact fault class this campaign removes. The defaults are
    /// the intended behaviour, so falling back to them is falling back to the product.
    /// </summary>
    [Fact]
    public async Task An_unreadable_policy_table_falls_back_to_the_shipped_defaults()
    {
        var service = new BekiReleasePolicyService(
            new FakePolicyRepository { Throw = true }, Scopes(new RecordingReconciliation()),
            NullLogger<BekiReleasePolicyService>.Instance);

        var policy = await service.SnapshotAsync(CancellationToken.None);

        Assert.False(policy.HumanReviewRequired);
        Assert.Equal(BekiReleaseSeverity.Blocker, policy.SeverityOf("PRESS_COLOR"));
    }

    // ==============================================================================================
    // Doubles
    // ==============================================================================================

    /// <summary>
    /// A one-service container, because the policy service resolves the reconciliation from a scope
    /// of its own rather than holding it.
    ///
    /// That indirection is not ceremony: the reconciliation reads the policy, so a direct dependency
    /// would be a cycle, and the work it triggers outlives the request that triggered it, so the
    /// request's own scope would be disposed under it. Building a real container here is the honest
    /// way to exercise the arrangement the application actually uses.
    /// </summary>
    private static IServiceScopeFactory Scopes(IBekiReleaseReconciliation reconciliation)
    {
        var services = new ServiceCollection();
        services.AddSingleton(reconciliation);

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class FakePolicyRepository : IBekiReleasePolicyRepository
    {
        public List<BekiReleaseCheckSetting> Rows { get; } = [];

        public bool Throw { get; set; }

        public Task<IReadOnlyList<BekiReleaseCheckSetting>> ListAsync(CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("the policy table is unreachable");
            }

            return Task.FromResult<IReadOnlyList<BekiReleaseCheckSetting>>(Rows.ToList());
        }

        public Task SetAsync(
            string checkId, string deliverableClass, string severity, string updatedBy,
            CancellationToken cancellationToken)
        {
            Rows.RemoveAll(row => row.CheckId == checkId && row.DeliverableClass == deliverableClass);
            Rows.Add(new BekiReleaseCheckSetting(
                checkId, deliverableClass, severity, updatedBy, DateTimeOffset.UtcNow));

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingReconciliation : IBekiReleaseReconciliation
    {
        public TaskCompletionSource<bool> Ran { get; } = new();

        public int Calls { get; private set; }

        public int Published { get; init; }

        public Task<BekiReconcileResult> ReconcilePackAsync(Guid packId, string reason, CancellationToken ct) =>
            Task.FromResult(BekiReconcileResult.No(BekiReconcileOutcomes.NotFound, "not asked"));

        public Task<int> ReconcileWithheldAsync(CancellationToken ct)
        {
            Calls++;
            Ran.TrySetResult(true);
            return Task.FromResult(Published);
        }

        public Task<BekiPublishOutcome> PublishUnlockedFilesAsync(
            AdventurePack pack, BekiReleaseGateReport report, CancellationToken ct) =>
            Task.FromResult(BekiPublishOutcome.Nothing);

        public Task RaiseWaiverAlarmsAsync(
            Guid packId, Guid userId, Guid? orderId, BekiReleaseGateReport report, CancellationToken ct) =>
            Task.CompletedTask;
    }

    /// <summary>A reconciliation that cannot run — the policy row must survive it.</summary>
    private sealed class ThrowingReconciliation : IBekiReleaseReconciliation
    {
        public Task<BekiReconcileResult> ReconcilePackAsync(Guid packId, string reason, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<int> ReconcileWithheldAsync(CancellationToken ct) =>
            throw new InvalidOperationException("the packs table is unreachable");

        public Task<BekiPublishOutcome> PublishUnlockedFilesAsync(
            AdventurePack pack, BekiReleaseGateReport report, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task RaiseWaiverAlarmsAsync(
            Guid packId, Guid userId, Guid? orderId, BekiReleaseGateReport report, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}

/// <summary>
/// Amendment B1's truth split, at the gates.
///
/// The finding it answers is one sentence long: the parent's publication and the supplier's release
/// were one boolean, so a policy that let a family have their book would also have told the printer
/// the file was releasable — and BekiPackageExport would have filed a failing PDF at the root of the
/// handback as a deliverable. Two families of properties, one set of evidence, and a waiver list
/// that says exactly where they diverge.
/// </summary>
public class BekiReleasePolicyGateTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PackId = Guid.NewGuid();

    /// <summary>
    /// The headline case. A digital gate fails; the family gets their book, the supplier is told the
    /// truth, and the divergence is written down rather than inferred.
    /// </summary>
    [Fact]
    public async Task A_flagged_digital_gate_publishes_to_the_parent_and_not_to_the_supplier()
    {
        var blobs = new PolicyFakeBlobs();
        Seed(blobs, UserId, PackId);
        blobs.Remove(BekiPackBlobs.DigitalReportName(UserId, PackId));

        var verdict = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Defaults);

        // The parent's half.
        Assert.True(verdict.CustomerPdfMayPublish);

        // The supplier's half, which is what the handback and the package classification read.
        Assert.False(verdict.SupplierCustomerPdfReleasable);
        Assert.Equal(BekiReleaseGates.NotReleasable, verdict.Verdict);
        Assert.Contains("DIGITAL_GEOMETRY", verdict.FailingGates);

        // And the gate itself still says what the evidence said. B2: a waiver is a separate field,
        // never a status replacement.
        var gate = Assert.Single(verdict.Gates, g => g.Id == "DIGITAL_GEOMETRY");
        Assert.Equal(BekiReleaseGates.Unknown, gate.Status);
        Assert.Equal(BekiReleaseGates.WaivedByPolicy, gate.Disposition);

        var waiver = Assert.Single(
            verdict.PolicyWaivers, w => w.CheckId == "DIGITAL_GEOMETRY");
        Assert.Equal(BekiReleaseGates.DigitalClass, waiver.DeliverableClass);
        Assert.Equal(BekiReleaseGates.Unknown, waiver.Status);
    }

    /// <summary>
    /// The press files keep their blockers under the same default policy: a printer's file is
    /// somebody else's press time, and the owner's ruling carves it out by name.
    /// </summary>
    [Fact]
    public async Task A_press_gate_still_blocks_under_the_shipped_defaults()
    {
        var blobs = new PolicyFakeBlobs();
        Seed(blobs, UserId, PackId);

        blobs.Seed(BekiPackBlobs.PressStatusName(UserId, PackId), Json(new
        {
            failed_gates = new[] { "PRESS_COLOR" },
            reason = "the interior carries RGB image data outside the FOGRA39 profile",
        }));

        var verdict = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Defaults);

        Assert.False(verdict.PressFilesMayPublish);
        Assert.False(verdict.SupplierPressReleasable);
        Assert.DoesNotContain(verdict.PolicyWaivers, w => w.CheckId == "PRESS_COLOR");

        // And the parent's download, which this says nothing about, is unaffected.
        Assert.True(verdict.CustomerPdfMayPublish);
    }

    /// <summary>
    /// The one press gate that does NOT block any more — owner's rule 4, 2026-09-01: the sizes we
    /// indicated for printing are correct.
    ///
    /// Both halves in one test, because the point is precisely that they diverge. The printer's file
    /// publishes: a refusal measured against a placement resolution the format does not require is
    /// not a reason to hold a book. And the supplier is told exactly what happened anyway — the gate
    /// still reads FAIL, the handback verdict is still NOT_RELEASABLE, and the waiver list names the
    /// decision, which is what makes "why did this ship?" answerable a year later.
    /// </summary>
    [Fact]
    public async Task The_resolution_gate_publishes_the_press_file_and_still_tells_the_supplier()
    {
        var blobs = new PolicyFakeBlobs();
        Seed(blobs, UserId, PackId);

        blobs.Seed(BekiPackBlobs.PressStatusName(UserId, PackId), Json(new
        {
            failed_gates = new[] { "PRESS_RESOLUTION" },
            reason = "the source art carries 143 PPI of detail at placement size",
        }));

        var verdict = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Defaults);

        Assert.True(verdict.PressFilesMayPublish);

        // Nothing about the truth moved.
        Assert.False(verdict.SupplierPressReleasable);
        Assert.Equal(BekiReleaseGates.NotReleasable, verdict.Verdict);
        Assert.Contains("PRESS_RESOLUTION", verdict.FailingGates);

        var gate = Assert.Single(verdict.Gates, g => g.Id == "PRESS_RESOLUTION");
        Assert.Equal(BekiReleaseGates.Fail, gate.Status);
        Assert.Equal(BekiReleaseGates.WaivedByPolicy, gate.Disposition);

        Assert.Contains(
            verdict.PolicyWaivers,
            waiver => waiver.CheckId == "PRESS_RESOLUTION"
                      && waiver.DeliverableClass == BekiReleaseGates.PressClass);
    }

    /// <summary>
    /// B2's per-class split, exercised on the gate it exists for: one render report refuses the
    /// press cover and one refuses the reading copy, and the two answers differ.
    /// </summary>
    [Fact]
    public async Task Render_validation_blocks_the_press_file_and_flags_the_reading_copy()
    {
        var blobs = new PolicyFakeBlobs();
        Seed(blobs, UserId, PackId);

        blobs.Seed(
            BekiPackBlobs.RenderReportName(UserId, PackId, BekiPackBlobs.CoverRenderArtifact),
            RenderReport(BekiPackBlobs.CoverRenderArtifact, releasable: false, qrPage: null));
        blobs.Seed(
            BekiPackBlobs.RenderReportName(UserId, PackId, BekiPackBlobs.DigitalRenderArtifact),
            RenderReport(BekiPackBlobs.DigitalRenderArtifact, releasable: false, qrPage: 12));

        var verdict = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Defaults);

        Assert.True(verdict.CustomerPdfMayPublish);
        Assert.False(verdict.PressFilesMayPublish);

        // Neither is releasable to the supplier: both files failed, and the policy is not a claim
        // about the evidence.
        Assert.False(verdict.SupplierCustomerPdfReleasable);
        Assert.False(verdict.SupplierPressReleasable);

        var waiver = Assert.Single(verdict.PolicyWaivers, w => w.CheckId == "RENDER_VALIDATION");
        Assert.Equal(BekiReleaseGates.DigitalClass, waiver.DeliverableClass);
    }

    /// <summary>
    /// The human gate as a switch. Flagged, a book waiting on a signature publishes and records the
    /// wait; blocked, it is the approve-before-publish flow exactly as it was.
    /// </summary>
    [Fact]
    public async Task Human_review_is_skipped_by_default_and_restored_by_one_row()
    {
        var blobs = new PolicyFakeBlobs();
        Seed(blobs, UserId, PackId, needsHumanReading: true);

        var skipped = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Defaults);

        // The report still SAYS a person should look — the console offers the signature — and the
        // book is not held for it.
        Assert.True(skipped.AwaitingHumanReview);
        Assert.True(skipped.CustomerPdfMayPublish);
        Assert.False(skipped.SupplierCustomerPdfReleasable);
        Assert.Contains(
            skipped.PolicyWaivers, w => w.CheckId == BekiReleaseChecks.HumanReview);

        var required = new BekiReleasePolicySnapshot(
        [
            new BekiReleaseCheckSetting(
                BekiReleaseChecks.HumanReview, BekiReleaseSeverity.AllClasses,
                BekiReleaseSeverity.Blocker, "misho", null),
        ]);

        var held = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: required);

        Assert.True(held.AwaitingHumanReview);
        Assert.False(held.CustomerPdfMayPublish);
        Assert.Empty(held.PolicyWaivers.Where(w => w.CheckId == BekiReleaseChecks.HumanReview));
    }

    /// <summary>
    /// Amendment B1's other half: a stored spread QA record that says the reviewer refused the page
    /// can never grade the gate PASS. It used to be counted rather than read, which was survivable
    /// only while a refused page never got a record at all — and the release policy ends that.
    /// </summary>
    [Fact]
    public async Task A_stored_spread_refusal_fails_the_visual_gate_rather_than_counting_as_evidence()
    {
        var blobs = new PolicyFakeBlobs();
        Seed(blobs, UserId, PackId);

        blobs.Seed(BekiPackBlobs.SpreadQaName(UserId, PackId, 3), Json(new
        {
            page = 3,
            qa_prompt_version = CompositeMinimalQa.Version,
            status = "FAIL",
            recommended_action = CompositeQaVerdict.ActionHumanReview,
        }));

        var verdict = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Strict);

        var gate = Assert.Single(verdict.Gates, g => g.Id == "VISUAL_QA");
        Assert.Equal(BekiReleaseGates.Fail, gate.Status);
        Assert.Contains("spread(s) 3", gate.Detail);
        Assert.False(verdict.SupplierCustomerPdfReleasable);
    }

    /// <summary>
    /// And the weaker word for the weaker statement: a page whose review could not be read is
    /// NEEDS_HUMAN, not FAIL. Nobody said the picture was wrong.
    /// </summary>
    [Fact]
    public async Task A_stored_unreadable_review_routes_the_book_to_the_human_gate()
    {
        var blobs = new PolicyFakeBlobs();
        Seed(blobs, UserId, PackId);

        blobs.Seed(BekiPackBlobs.SpreadQaName(UserId, PackId, 6), Json(new
        {
            page = 6,
            qa_prompt_version = CompositeMinimalQa.Version,
            status = CompositeBookPipeline.UnreadableStatus,
            recommended_action = CompositeQaVerdict.ActionHumanReview,
        }));

        var verdict = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Strict);

        var gate = Assert.Single(verdict.Gates, g => g.Id == "VISUAL_QA");
        Assert.Equal(BekiReleaseGates.NeedsHuman, gate.Status);
        Assert.Contains("spread(s) 6", gate.Detail);
        Assert.True(verdict.AwaitingHumanReview);
    }

    /// <summary>A report that carries no waivers publishes exactly as it always did.</summary>
    [Fact]
    public async Task A_clean_book_publishes_to_both_audiences_with_no_waivers()
    {
        var blobs = new PolicyFakeBlobs();
        Seed(blobs, UserId, PackId);

        var verdict = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Defaults);

        Assert.Equal(BekiReleaseGates.Releasable, verdict.Verdict);
        Assert.Empty(verdict.PolicyWaivers);
        Assert.True(verdict.CustomerPdfMayPublish);
        Assert.True(verdict.SupplierCustomerPdfReleasable);
        Assert.True(verdict.PressFilesMayPublish);
        Assert.True(verdict.SupplierPressReleasable);
    }

    /// <summary>
    /// The waivers survive a round trip through JSON, which is what makes a stored verdict
    /// answerable months later: the admin endpoint reads the document, not the policy table as it
    /// stands today.
    /// </summary>
    [Fact]
    public async Task A_stored_report_answers_the_same_way_it_did_when_it_was_written()
    {
        var blobs = new PolicyFakeBlobs();
        Seed(blobs, UserId, PackId);
        blobs.Remove(BekiPackBlobs.DigitalReportName(UserId, PackId));

        var written = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Defaults);

        var reread = BekiReleaseGateReport.TryParse(written.ToJson());

        Assert.NotNull(reread);
        Assert.True(reread!.CustomerPdfMayPublish);
        Assert.False(reread.SupplierCustomerPdfReleasable);
        Assert.Contains(reread.PolicyWaivers, w => w.CheckId == "DIGITAL_GEOMETRY");
        Assert.Contains(
            reread.Gates,
            gate => gate.Id == "DIGITAL_GEOMETRY"
                    && gate.Disposition == BekiReleaseGates.WaivedByPolicy);
    }

    // ==============================================================================================
    // Fixtures — the same complete book BekiReleaseGatesTests seeds, kept here so the two files can
    // run in parallel without sharing mutable state.
    // ==============================================================================================

    internal const string ContactSheetSha =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>
    /// A complete, evidenced book in storage.
    ///
    /// The ids are parameters rather than this class's own constants because
    /// <see cref="BekiReconciliationTests"/> seeds the same book under ITS pack — and a helper that
    /// quietly used the wrong owner would seed a book nothing under test could see, which is the
    /// kind of green test that proves nothing.
    /// </summary>
    internal static void Seed(
        PolicyFakeBlobs blobs, Guid userId, Guid packId, bool needsHumanReading = false)
    {
        blobs.Seed(BekiPackBlobs.AssetLockName(userId, packId), Json(new
        {
            manifest_version = BekiAssetLock.ManifestVersion,
            generated_at_utc = DateTimeOffset.UtcNow,
            source_registries = new Dictionary<string, string> { ["layout"] = "v1.2" },
            assets = new[]
            {
                new { role = "noto_sans_georgian_regular_licensed", file = "n.ttf", version = "v1", sha256 = new string('1', 64), approval_status = "approved" },
                new { role = "ottia_regular_ttf_licensed", file = "o.ttf", version = "v1", sha256 = new string('2', 64), approval_status = "approved" },
                new { role = "fogra39_output_intent", file = "p.icc", version = "v1", sha256 = new string('3', 64), approval_status = "approved" },
            },
        }));

        blobs.Seed(
            BekiPackBlobs.ManifestName(userId, packId),
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new BekiFulfillmentManifest
                {
                    IllustrationContract = ["composite"],
                    Entries = Enumerable.Range(1, BookFormat.SpreadCount)
                        .Select(n => new BekiFulfillmentManifestEntry(n, $"https://blob.test/spread-{n}"))
                        .ToList(),
                    Compositions = Enumerable.Range(1, BookFormat.SpreadCount)
                        .Select(n => new BekiCompositionManifestEntry(
                            n, $"https://blob.test/receipt-{n}", "pose_01_neutral_hover",
                            new string('a', 64), $"https://blob.test/base-{n}"))
                        .ToList(),
                    ScenarioUrl = "https://blob.test/scenario",
                    StoryUrl = "https://blob.test/story",
                    Cover = new BekiCoverRecord(
                        "https://blob.test/wrap", BekiCoverRecord.WrapMaster, "verified")
                    {
                        PoseId = "pose_01_neutral_hover",
                        CompositeSha256 = new string('c', 64),
                    },
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        blobs.Seed(BekiPackBlobs.CoverWrapCompositeName(userId, packId), [1]);
        blobs.Seed(BekiPackBlobs.CoverWrapBaseName(userId, packId), [2]);
        blobs.Seed(BekiPackBlobs.CoverCompositionName(userId, packId), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.CoverFrontName(userId, packId), [3]);
        blobs.Seed(BekiPackBlobs.StoryName(userId, packId), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.ScenarioName(userId, packId), "{}"u8.ToArray());

        blobs.Seed(BekiPackBlobs.CompositeReviewName(userId, packId), Json(new
        {
            needs_human_reading = needsHumanReading,
        }));

        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            blobs.Seed(BekiPackBlobs.SpreadQaName(userId, packId, spread), Json(new
            {
                page = spread,
                qa_prompt_version = CompositeMinimalQa.Version,
                status = "PASS",
                recommended_action = "pass",
            }));
        }

        foreach (var role in BekiFixedPageQa.Roles)
        {
            blobs.Seed(BekiPackBlobs.FixedPageQaName(userId, packId, role), Json(new
            {
                role,
                page = 1,
                qa_prompt_version = BekiFixedPageQa.Version,
                status = BekiFixedPageQa.Pass,
            }));
        }

        foreach (var mode in BekiPackBlobs.LayoutModes)
        {
            blobs.Seed(BekiPackBlobs.LayoutReceiptName(userId, packId, mode), Json(new
            {
                mode,
                pages = new[]
                {
                    new
                    {
                        page = 1,
                        role = "intro",
                        text_lines = new[] { "ეს წიგნი ეკუთვნის ნინოს" },
                        typography = new[] { new { role = "intro", family = "Noto Sans Georgian" } },
                    },
                },
            }));
        }

        blobs.Seed(BekiPackBlobs.InteriorPreflightName(userId, packId), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.CoverPreflightName(userId, packId), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.DigitalReportName(userId, packId), "{}"u8.ToArray());

        foreach (var artifact in BekiPackBlobs.RenderedArtifacts)
        {
            blobs.Seed(BekiPackBlobs.FinalPdfName(userId, packId, artifact), [9]);
        }

        blobs.Seed(
            BekiPackBlobs.RenderReportName(userId, packId, BekiPackBlobs.InteriorRenderArtifact),
            RenderReport(BekiPackBlobs.InteriorRenderArtifact, releasable: true, qrPage: 11));
        blobs.Seed(
            BekiPackBlobs.RenderReportName(userId, packId, BekiPackBlobs.CoverRenderArtifact),
            RenderReport(BekiPackBlobs.CoverRenderArtifact, releasable: true, qrPage: null));
        blobs.Seed(
            BekiPackBlobs.RenderReportName(userId, packId, BekiPackBlobs.DigitalRenderArtifact),
            RenderReport(BekiPackBlobs.DigitalRenderArtifact, releasable: true, qrPage: 12));
    }

    internal static byte[] RenderReport(
        string artifact, bool releasable, int? qrPage, string qrStatus = "ok") => Json(new
        {
            stage = "beki-render-validation-v1",
            artifact,
            verdict = releasable ? "RELEASABLE" : "NOT_RELEASABLE",
            failed_gates = releasable ? Array.Empty<string>() : ["RENDER_VALIDATION"],
            qr = new { gate = "QR", status = qrStatus, page = qrPage },
            contact_sheet = new { sha256 = ContactSheetSha, bytes = 1024 },
        });

    internal static byte[] Json(object payload) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
}

/// <summary>An in-memory blob account. Names in, bytes out, nothing else.</summary>
internal sealed class PolicyFakeBlobs : IBlobStorageService
{
    private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    public void Seed(string name, byte[] bytes) => _blobs[name] = bytes;

    public void Remove(string name) => _blobs.Remove(name);

    public bool Has(string name) => _blobs.ContainsKey(name);

    public byte[]? Get(string name) => _blobs.GetValueOrDefault(name);

    public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) =>
        Task.FromResult(_blobs.ContainsKey(blobName));

    public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken) =>
        _blobs.TryGetValue(blobName, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes))
            : throw new FileNotFoundException(blobName);

    public Task<string> UploadAsync(
        string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        _blobs[blobName] = bytes;
        return Task.FromResult($"https://blob.test/{blobName}");
    }

    public Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
