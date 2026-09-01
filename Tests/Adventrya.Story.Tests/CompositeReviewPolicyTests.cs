using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The owner's rule 5 of 2026-09-01, quoted: **"we don't need additional reviews for images"**.
///
/// One vision call per spread, plus the retry ladder it can drive, is the most expensive opinion in
/// this pipeline and the owner has ruled it optional. So `image_review` joins the policy table
/// beside `human_review` and behaves the same way: <c>blocker</c> is the reviewed loop this
/// pipeline has always run, <c>flag</c> — the shipped default — means the call is not bought at all.
///
/// Two things are load-bearing about "not bought", and every test here is about one of them.
///
/// The page must still be ACCEPTED honestly: the deterministic checks — the fold measurement, the
/// base image checks, the exact-Beki receipt — all still run, because turning off opinions is not
/// turning off arithmetic. And the record must still be TRUE: the spread's QA document says
/// REVIEW_SKIPPED_BY_POLICY with zero review attempts, and the release gate reads that back and
/// tells the supplier that nobody looked. A PASS anywhere on this path would be the exact lie the
/// truth split exists to prevent.
/// </summary>
public class CompositeReviewPolicyTests : CompositePipelineTestBase
{
    // ---------------------------------------------------------------------------------------
    // Nothing is asked
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The default policy draws the whole book and buys no review at all — and the record of each
    /// page says so rather than claiming a verdict.
    /// </summary>
    [Fact]
    public async Task The_shipped_policy_draws_the_book_and_asks_no_model_about_it()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(context: Skipping()), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        // One picture per page, and not one vision call. The identity derivation is the only other
        // model call on this path and it is not a review — it is how the book knows who it is about.
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.Equal(0, images.ReviewCalls);
        Assert.Empty(images.ReviewPrompts);
        Assert.Equal(1, images.IdentityCalls);

        foreach (var spread in result.Spreads)
        {
            using var record = JsonDocument.Parse(spread.QaJson!);
            var root = record.RootElement;

            // Not PASS. Nobody said this page was right, and the document does not say it either.
            Assert.Equal(
                CompositeBookPipeline.ReviewSkippedStatus,
                root.GetProperty("status").GetString());
            Assert.Equal(0, root.GetProperty("review_attempts").GetInt32());
            Assert.Empty(root.GetProperty("failed_checks").EnumerateArray());

            // What was decided, and under which vocabulary — for the person opening this file in a
            // year and asking what "skipped by policy" meant at the time.
            Assert.Equal(
                BekiReleaseChecks.ImageReview, root.GetProperty("release_policy_check").GetString());
            Assert.Equal(
                BekiReleaseSeverity.Flag, root.GetProperty("release_policy_severity").GetString());
            Assert.Equal(
                BekiReleasePolicySnapshot.Version,
                root.GetProperty("release_policy_version").GetString());

            // And the provenance a reader needs to judge the picture themselves.
            Assert.Equal(
                CompositeIllustrationPrompt.Version,
                root.GetProperty("image_prompt_version").GetString());
            Assert.Equal(1, root.GetProperty("base_attempts").GetInt32());
        }
    }

    /// <summary>
    /// A verdict nobody asked for is a verdict nobody reads. The reviewer's queue is untouched,
    /// which is the difference between "the review was skipped" and "the review passed".
    /// </summary>
    [Fact]
    public async Task A_refusal_nobody_asked_for_never_reaches_the_book()
    {
        var images = new StubImageService();

        // The verdict that kills a book when the reviewer is on: no rung of the ladder answers it.
        images.Verdicts.Enqueue(Fail("CHILD_IDENTITY", CompositeQaVerdict.ActionHumanReview));

        var waivers = new List<CompositePolicyWaiver>();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(context: Skipping(waivers)), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);
        Assert.Equal(0, images.ReviewCalls);

        // No waiver either: nothing refused anything. A skipped check is not a waived check, and an
        // alarm claiming otherwise would page somebody about a refusal that never happened.
        Assert.Empty(waivers);
    }

    /// <summary>
    /// The page still costs what it cost, and the record still says so.
    ///
    /// The fulfilment job reads an empty attempt list as "this page was adopted from an earlier run
    /// and cost nothing", so a freshly drawn page with no rows would report a paid image call as
    /// free — and every book under the new default would look like it had been adopted whole.
    /// </summary>
    [Fact]
    public async Task An_unreviewed_page_still_reports_the_image_it_was_paid_for()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(context: Skipping()), CancellationToken.None);

        Assert.All(result.Spreads, spread =>
        {
            var attempt = Assert.Single(spread.Attempts);

            Assert.True(attempt.Accepted);
            Assert.Equal(0, attempt.ReviewMs);
            Assert.Equal(CompositeBookPipeline.ReviewSkippedStatus, attempt.Verdict);

            // Where Beki actually landed, which is the row's other job.
            Assert.NotNull(attempt.Anchor);
        });
    }

    // ---------------------------------------------------------------------------------------
    // What is NOT skipped
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The centre-fold measurement is arithmetic, not an opinion, and it runs whatever the reviewer
    /// switch says: a veiled base still buys the page's one regeneration.
    ///
    /// This is the line rule 5 draws. "We don't need additional reviews for images" is about asking
    /// a model what it thinks; it is not about the checks that measure the picture, and a policy
    /// that quietly turned those off would be shipping unmeasured art under the owner's name.
    /// </summary>
    [Fact]
    public async Task The_deterministic_fold_gate_still_runs_and_still_buys_its_regeneration()
    {
        var veiled = CompositePipelineSeamTests.WithVeil(
            Gradient(ProviderWidth, ProviderHeight), leftSide: true, lift: 0.5, shoulderColumns: 24);

        var images = new StubImageService();
        images.QueuedImages.Enqueue(veiled);
        images.QueuedImages.Enqueue(veiled);

        var waivers = new List<CompositePolicyWaiver>();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(context: Skipping(waivers)), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        // Nine pictures for eight spreads: the regeneration was bought by the measurement, with no
        // reviewer anywhere in the loop.
        Assert.Equal(BookFormat.SpreadCount + 1, images.ImageCalls);
        Assert.Equal(0, images.ReviewCalls);

        // And the second reading past the limit is waived and alarmed exactly as it is when the
        // reviewer is on — a different check, a different switch.
        var waiver = Assert.Single(waivers);
        Assert.Equal(BekiReleaseChecks.CentreFold, waiver.CheckId);
    }

    /// <summary>
    /// Spread one is still the book's appearance anchor, and the seven pages after it are still
    /// drawn against it.
    ///
    /// The anchor is what keeps the child the same from page to page, and it was never the
    /// reviewer's doing — a fact worth a test, because "the reviewer accepted spread one" is how the
    /// old code phrased it and turning the reviewer off must not have quietly removed the anchor.
    /// </summary>
    [Fact]
    public async Task Spread_one_still_anchors_the_book_with_no_reviewer_in_sight()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(context: Skipping()), CancellationToken.None);

        Assert.Null(images.AnchorImages[0]);
        Assert.Equal(
            BookFormat.SpreadCount - 1,
            images.AnchorImages.Skip(1).Count(anchor => anchor is { Length: > 0 }));

        Assert.Equal(result.Spreads.Single(spread => spread.Page == 1).BasePng, result.Anchor);
    }

    /// <summary>
    /// And the switch works in the other direction, which is the whole reason it is a switch: an
    /// operator who turns the reviewer back on gets the reviewed loop, verdict for verdict.
    /// </summary>
    [Fact]
    public async Task An_operator_who_turns_the_reviewer_on_gets_every_page_reviewed()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(
                Request(context: Context() with
                {
                    ReleasePolicy = CompositePipelinePolicyTests.Reviewing(
                        BekiReleasePolicySnapshot.Defaults),
                }),
                CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, images.ReviewCalls);

        Assert.All(result.Spreads, spread =>
        {
            using var record = JsonDocument.Parse(spread.QaJson!);
            Assert.Equal(CompositeQaVerdict.Pass, record.RootElement.GetProperty("status").GetString());
        });
    }

    /// <summary>
    /// A run with no policy at all reviews everything, because a caller that said nothing must get
    /// the behaviour that predates the policy. Nothing softens by omission.
    /// </summary>
    [Fact]
    public async Task A_run_with_no_policy_still_reviews_every_page()
    {
        var images = new StubImageService();

        await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, images.ReviewCalls);
    }

    // ---------------------------------------------------------------------------------------
    // What the supplier is told
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The gate reads the record back and refuses to call it a pass — both directions in one test,
    /// because the point is that they differ.
    ///
    /// The family gets their book: VISUAL_QA is a shared gate the policy flags by default, so the
    /// ordinary waiver path publishes it and writes the waiver down. The supplier is told the truth:
    /// the gate's own status is REVIEW_SKIPPED_BY_POLICY, the handback verdict is NOT_RELEASABLE,
    /// and the detail names the pages nobody looked at. RELEASABLE on a book with eight unreviewed
    /// spreads would be a false statement in a document written to be checked.
    /// </summary>
    [Fact]
    public async Task A_book_nobody_reviewed_publishes_to_the_parent_and_is_not_releasable()
    {
        var blobs = new PolicyFakeBlobs();
        SeedSkippedBook(blobs);

        var verdict = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Defaults);

        var gate = Assert.Single(verdict.Gates, g => g.Id == "VISUAL_QA");

        Assert.Equal(BekiReleaseGates.ReviewSkipped, gate.Status);
        Assert.NotEqual(BekiReleaseGates.Pass, gate.Status);
        Assert.Contains("no visual review was performed", gate.Detail, StringComparison.Ordinal);
        Assert.Contains("spread(s) 1, 2, 3, 4, 5, 6, 7, 8", gate.Detail, StringComparison.Ordinal);

        // The supplier's half.
        Assert.Equal(BekiReleaseGates.NotReleasable, verdict.Verdict);
        Assert.Contains("VISUAL_QA", verdict.FailingGates);
        Assert.False(verdict.SupplierCustomerPdfReleasable);

        // The parent's half, by the ordinary shared-gate route.
        Assert.True(verdict.CustomerPdfMayPublish);
        Assert.Equal(BekiReleaseGates.WaivedByPolicy, gate.Disposition);
        Assert.Contains(
            verdict.PolicyWaivers,
            waiver => waiver.CheckId == "VISUAL_QA"
                      && waiver.DeliverableClass == BekiReleaseGates.DigitalClass);

        // And nobody is waiting: the ruling is that no person has to look, so the console does not
        // put this book in a signature queue.
        Assert.False(verdict.AwaitingHumanReview);
    }

    /// <summary>
    /// Under a policy that blocks, the same evidence withholds the book. The record is not a
    /// permission slip — it is a fact, and what is done about it is the operator's to set.
    /// </summary>
    [Fact]
    public async Task The_same_book_is_withheld_when_the_visual_gate_is_a_blocker()
    {
        var blobs = new PolicyFakeBlobs();
        SeedSkippedBook(blobs);

        var verdict = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Strict);

        Assert.False(verdict.CustomerPdfMayPublish);
        Assert.False(verdict.SupplierCustomerPdfReleasable);

        var gate = Assert.Single(verdict.Gates, g => g.Id == "VISUAL_QA");
        Assert.Equal(BekiReleaseGates.ReviewSkipped, gate.Status);
        Assert.Null(gate.Disposition);
    }

    /// <summary>
    /// A refusal outranks a skip. A book with seven unreviewed pages and one the reviewer refused —
    /// the shape a deployment gets on the day it flips the switch mid-flight — is graded by the
    /// refusal, because that is the strongest thing the evidence says.
    /// </summary>
    [Fact]
    public async Task A_stored_refusal_still_outranks_a_skipped_page()
    {
        var blobs = new PolicyFakeBlobs();
        SeedSkippedBook(blobs);

        blobs.Seed(BekiPackBlobs.SpreadQaName(UserId, PackId, 4), BekiReleasePolicyGateTests.Json(new
        {
            page = 4,
            qa_prompt_version = CompositeMinimalQa.Version,
            status = "FAIL",
            recommended_action = CompositeQaVerdict.ActionHumanReview,
        }));

        var verdict = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Defaults);

        var gate = Assert.Single(verdict.Gates, g => g.Id == "VISUAL_QA");

        Assert.Equal(BekiReleaseGates.Fail, gate.Status);
        Assert.Contains("spread(s) 4", gate.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// **Review finding 4.** A skipped book that ALSO needs a person reports NEEDS_HUMAN, and the
    /// skip is written into the evidence rather than allowed to answer for it.
    ///
    /// The clause order was the bug. The skip was asked first, so a book carrying a Georgian or pose
    /// advisory beside its skipped pages was graded REVIEW_SKIPPED_BY_POLICY and produced no
    /// NEEDS_HUMAN status anywhere — and since AwaitingHumanReview is computed from that status, the
    /// report came back saying nobody was waiting. The console offered no signature, and an operator
    /// who had deliberately made human_review a blocker had it bypassed by a switch about something
    /// else entirely. Two unrelated settings, and the quieter one won.
    /// </summary>
    [Fact]
    public async Task A_skipped_book_that_still_needs_a_human_says_so_rather_than_reporting_the_skip()
    {
        var blobs = new PolicyFakeBlobs();
        SeedSkippedBook(blobs, needsHumanReading: true);

        var verdict = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Defaults);

        var gate = Assert.Single(verdict.Gates, g => g.Id == "VISUAL_QA");

        Assert.Equal(BekiReleaseGates.NeedsHuman, gate.Status);
        Assert.True(verdict.AwaitingHumanReview);
        Assert.Contains("reviewer's signature", gate.Detail, StringComparison.Ordinal);

        // The skip is not lost by being outranked: it is in the detail and its eight records are in
        // the evidence, so the person this gate just summoned can see nobody looked at the artwork.
        Assert.Contains("No visual review was performed", gate.Detail, StringComparison.Ordinal);
        Assert.Contains("spread(s) 1, 2, 3, 4, 5, 6, 7, 8", gate.Detail, StringComparison.Ordinal);
        Assert.Contains(
            BekiPackBlobs.SpreadQaName(UserId, PackId, 4), gate.Evidence);
    }

    /// <summary>
    /// And the blocker an operator set is a blocker again. Under the shipped policy human_review is
    /// a flag and the family's book publishes with the wait recorded; under a policy that blocks it,
    /// the same evidence withholds — which is the behaviour the clause order was silently removing.
    /// </summary>
    [Fact]
    public async Task The_human_review_blocker_holds_a_skipped_book_that_needs_a_person()
    {
        var blobs = new PolicyFakeBlobs();
        SeedSkippedBook(blobs, needsHumanReading: true);

        var flagged = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Defaults);

        Assert.True(flagged.CustomerPdfMayPublish);
        Assert.True(flagged.AwaitingHumanReview);

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
        Assert.False(held.PressFilesMayPublish);
    }

    /// <summary>
    /// A stored NEEDS_HUMAN verdict on one page reaches the same answer by the other route: an
    /// unreadable reviewer verdict is a page somebody has to look at, whatever the other seven pages'
    /// records say about nobody having been asked.
    /// </summary>
    [Fact]
    public async Task A_skipped_book_with_one_unreadable_verdict_still_routes_to_a_person()
    {
        var blobs = new PolicyFakeBlobs();
        SeedSkippedBook(blobs);

        blobs.Seed(BekiPackBlobs.SpreadQaName(UserId, PackId, 6), BekiReleasePolicyGateTests.Json(new
        {
            page = 6,
            qa_prompt_version = CompositeMinimalQa.Version,
            status = CompositeBookPipeline.UnreadableStatus,
            recommended_action = CompositeQaVerdict.ActionHumanReview,
        }));

        var verdict = await new BekiReleaseGates(blobs).EvaluateAsync(
            UserId, PackId, CancellationToken.None, policy: BekiReleasePolicySnapshot.Defaults);

        var gate = Assert.Single(verdict.Gates, g => g.Id == "VISUAL_QA");

        Assert.Equal(BekiReleaseGates.NeedsHuman, gate.Status);
        Assert.True(verdict.AwaitingHumanReview);
        Assert.Contains("spread(s) 6", gate.Detail, StringComparison.Ordinal);
        Assert.Contains("No visual review was performed", gate.Detail, StringComparison.Ordinal);
    }

    // ==============================================================================================
    // Harness
    // ==============================================================================================

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PackId = Guid.NewGuid();

    /// <summary>The shipped policy, whose <c>image_review</c> is a flag: no model looks at a page.</summary>
    private static CompositeBookContext Skipping(List<CompositePolicyWaiver>? waivers = null) =>
        Context() with
        {
            ReleasePolicy = BekiReleasePolicySnapshot.Defaults,
            OnPolicyWaiver = waiver =>
            {
                waivers?.Add(waiver);
                return Task.CompletedTask;
            },
        };

    /// <summary>
    /// A complete book in storage whose eight spread records all say nobody reviewed them — what
    /// the pipeline above actually writes, seeded for the gate to read.
    /// </summary>
    private static void SeedSkippedBook(PolicyFakeBlobs blobs, bool needsHumanReading = false)
    {
        BekiReleasePolicyGateTests.Seed(blobs, UserId, PackId, needsHumanReading);

        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            blobs.Seed(
                BekiPackBlobs.SpreadQaName(UserId, PackId, spread),
                Encoding.UTF8.GetBytes(CompositeSpreadQa.WriteSkipped(
                    spread, "pose_01_neutral_hover", "LEFT", baseAttempts: 1,
                    BekiReleaseSeverity.Flag)));
        }
    }
}
