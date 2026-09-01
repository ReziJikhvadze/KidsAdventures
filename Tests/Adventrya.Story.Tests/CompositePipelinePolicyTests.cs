using System.Text.Json;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The pipeline under the release policy — amendment B3's whitelist, and what "flag" actually does
/// to a book being drawn.
///
/// The audit's finding was thirteen terminal paths that kill a paid book with the artwork already in
/// hand. Four of them are quality judgements about an intact composite — a fold measurement, a cover
/// band, a reviewer's opinion, an answer that would not parse — and the owner's ruling is that a
/// judgement does not kill a paid book. The other nine protect an invariant rather than a taste, and
/// every one of them still stops the run: there is nothing to ship.
///
/// So each test below is a pair of questions. Does the book survive? And is the truth about it still
/// written down — the reviewer's own verdict in the QA record, the picture and the numbers in an
/// alarm, and nothing anywhere claiming a page passed that did not.
/// </summary>
public class CompositePipelinePolicyTests : CompositePipelineTestBase
{
    /// <summary>
    /// The exhausted QA ladder: the reviewer refuses, the ladder has nowhere left to go, and under a
    /// flag the page ships with the refusal recorded rather than the book dying with it.
    /// </summary>
    [Fact]
    public async Task A_refused_spread_ships_with_its_refusal_recorded_when_the_check_is_flagged()
    {
        var images = new StubImageService();

        // human_review is the action with no rung: the ladder cannot regenerate or re-composite its
        // way past it, which is exactly the terminal path this test is about.
        images.Verdicts.Enqueue(Fail("CHILD_IDENTITY", CompositeQaVerdict.ActionHumanReview));

        var waivers = new List<CompositePolicyWaiver>();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(context: Flagged(waivers)), CancellationToken.None);

        // The book is whole.
        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        var refused = result.Spreads.Single(spread => spread.Page == 1);

        // And its record says what the reviewer said, in the reviewer's own words. A PASS here would
        // be the lie amendment B1 exists to prevent — the release gates read this document back.
        using var record = JsonDocument.Parse(refused.QaJson!);
        Assert.Equal("FAIL", record.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            CompositeQaVerdict.ActionHumanReview,
            record.RootElement.GetProperty("recommended_action").GetString());
        Assert.Contains(
            "CHILD_IDENTITY",
            record.RootElement.GetProperty("failed_checks").EnumerateArray()
                .Select(check => check.GetString()));

        // One alarm, with the picture a person can open and the attempt record beside it.
        var waiver = Assert.Single(waivers);
        Assert.Equal(BekiReleaseChecks.ImageQa, waiver.CheckId);
        Assert.Equal(1, waiver.Page);
        Assert.NotEmpty(waiver.EvidencePng);

        using var evidence = JsonDocument.Parse(waiver.EvidenceJson);
        Assert.Equal(
            CompositeFailureCodes.ImageQaFailed,
            evidence.RootElement.GetProperty("failure_code").GetString());
    }

    /// <summary>
    /// And the same refusal with the check left as a blocker stops the book exactly as it did — the
    /// policy is a switch, not a rewrite.
    /// </summary>
    [Fact]
    public async Task The_same_refusal_still_stops_the_book_when_the_check_is_a_blocker()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("CHILD_IDENTITY", CompositeQaVerdict.ActionHumanReview));

        var waivers = new List<CompositePolicyWaiver>();

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(context: Blocking(waivers)), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageQaFailed, failure.FailureCode);
        Assert.Equal(1, failure.Page);
        Assert.NotNull(failure.Evidence);
        Assert.Empty(waivers);
    }

    /// <summary>
    /// A waived first page still anchors the book — and that is a decision, not an oversight.
    ///
    /// The anchor is what stops the child changing between pages, and the drifting-child defect
    /// passed eight independent reviews precisely because every page was judged alone. A book whose
    /// first page was questioned is better off with seven pages that match it than with seven that
    /// match nothing: the waiver is recorded and alarmed, and the person who opens it is looking at
    /// one child rather than eight.
    ///
    /// Continuity goes the other way, and the difference is the strength of the instruction: "this
    /// is the same child" is an identity reference, while a continuity image tells a later spread to
    /// REPRODUCE a particular creature from a picture nobody approved.
    /// </summary>
    [Fact]
    public async Task A_waived_first_page_still_anchors_the_book_and_is_never_a_continuity_reference()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("CHILD_IDENTITY", CompositeQaVerdict.ActionHumanReview));

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(context: Flagged([])), CancellationToken.None);

        // Spread one makes the anchor and therefore has none of its own; the seven after it do.
        Assert.Null(images.AnchorImages[0]);
        Assert.Equal(
            BookFormat.SpreadCount - 1,
            images.AnchorImages.Skip(1).Count(anchor => anchor is { Length: > 0 }));

        // Later spreads that share a recurring element are shown one — but never this one. The
        // accepted pages remember themselves; the waived page does not.
        var waived = result.Spreads.Single(spread => spread.Page == 1).BasePng;

        Assert.All(
            images.ContinuityImages.Where(continuity => continuity is not null),
            continuity => Assert.False(continuity!.SequenceEqual(waived)));
    }

    /// <summary>
    /// Two unreadable answers. The page ships with NEEDS_HUMAN — not PASS, which would invent a
    /// review, and not FAIL, which would invent a refusal — and it does not climb the ladder,
    /// because there is no recommended action to act on and a second base image would be a bill for
    /// a complaint nobody made.
    /// </summary>
    [Fact]
    public async Task An_unreadable_review_ships_the_page_as_needing_a_human_and_buys_nothing()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue("this is not JSON");
        images.Verdicts.Enqueue("nor is this");

        var waivers = new List<CompositePolicyWaiver>();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(context: Flagged(waivers)), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        var unreadable = result.Spreads.Single(spread => spread.Page == 1);

        using var record = JsonDocument.Parse(unreadable.QaJson!);
        Assert.Equal(
            CompositeBookPipeline.UnreadableStatus,
            record.RootElement.GetProperty("status").GetString());

        // One picture for this page, not two: the parse retry re-asks, it does not redraw.
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);

        // And the evidence this path never used to attach at all.
        var waiver = Assert.Single(waivers);
        Assert.Equal(BekiReleaseChecks.QaUnreadable, waiver.CheckId);
        Assert.NotEmpty(waiver.EvidencePng);

        using var evidence = JsonDocument.Parse(waiver.EvidenceJson);
        Assert.Equal(
            BekiReleaseChecks.QaUnreadable, evidence.RootElement.GetProperty("gate").GetString());
        Assert.Equal(2, evidence.RootElement.GetProperty("review_attempts").GetInt32());
    }

    /// <summary>
    /// The blocker path for the same failure now carries its evidence too — amendment B3 asks for it
    /// in BOTH cases, and its absence was the sharpest half of the audit's finding: the book stopped
    /// "marked for human review" and left the human nothing whatever to review.
    /// </summary>
    [Fact]
    public async Task An_unreadable_review_that_stops_the_book_still_leaves_the_page_and_the_reason()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue("this is not JSON");
        images.Verdicts.Enqueue("nor is this");

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(context: Blocking([])), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageQaFailed, failure.FailureCode);

        var evidence = Assert.IsType<CompositeFailureEvidence>(failure.Evidence);
        Assert.Equal(1, evidence.Page);
        Assert.NotEmpty(evidence.CompositePng);

        using var document = JsonDocument.Parse(evidence.QaJson);
        Assert.Equal(
            BekiReleaseChecks.QaUnreadable, document.RootElement.GetProperty("gate").GetString());
    }

    /// <summary>
    /// The centre-fold measurement, flagged: the page's one regeneration is still spent — the
    /// arithmetic is cheap and a second picture often is clean — and a second reading past the limit
    /// ships the artwork with the numbers recorded instead of ending the book.
    /// </summary>
    [Fact]
    public async Task A_base_veiled_twice_ships_with_its_readings_when_the_fold_check_is_flagged()
    {
        var veiled = CompositePipelineSeamTests.WithVeil(
            Gradient(ProviderWidth, ProviderHeight), leftSide: true, lift: 0.5, shoulderColumns: 24);

        var images = new StubImageService();
        images.QueuedImages.Enqueue(veiled);
        images.QueuedImages.Enqueue(veiled);

        var waivers = new List<CompositePolicyWaiver>();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(context: Flagged(waivers)), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        // The regeneration was still bought: nine pictures for eight spreads.
        Assert.Equal(BookFormat.SpreadCount + 1, images.ImageCalls);

        var waiver = Assert.Single(waivers);
        Assert.Equal(BekiReleaseChecks.CentreFold, waiver.CheckId);
        Assert.Equal(1, waiver.Page);

        // The evidence is the refused BASE — Beki is never composited onto a page that fails here —
        // and it still measures as two halves, which is what makes it worth storing.
        Assert.True(CompositeSeamRepair.MeasureCentreField(waiver.EvidencePng).Exceeded);

        using var document = JsonDocument.Parse(waiver.EvidenceJson);
        Assert.Equal(
            BekiReleaseChecks.CentreFold, document.RootElement.GetProperty("gate").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("readings").GetArrayLength());

        // And the page that shipped was reviewed like any other: the fold gate is not a substitute
        // for the reviewer.
        Assert.Equal(BookFormat.SpreadCount, images.ReviewCalls);
    }

    /// <summary>
    /// The cover wrap's construction bands, flagged: the one page a parent sees first ships with
    /// the reading recorded.
    ///
    /// What the measurement is about is a printing complaint — hinge and spine geometry rendered as
    /// artwork rather than merely guiding placement — and the family's copy is a screen. The press
    /// files are governed separately by gates that are blockers by default, so a wrap this reading
    /// dislikes still cannot reach a printer by accident.
    /// </summary>
    [Fact]
    public async Task A_wrap_banded_twice_ships_with_its_bands_recorded_when_the_check_is_flagged()
    {
        var banded = WithSeam(
            Gradient(ProviderWidth, ProviderHeight), columns: 3, darken: 90,
            atColumn: CompositeCoverBandTests.ColumnFor(261.5, ProviderWidth));

        var images = new StubImageService { NextImage = banded };
        var waivers = new List<CompositePolicyWaiver>();

        var pipeline = Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images);
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        var wrap = await pipeline.DrawCoverWrapAsync(
            Flagged(waivers), scenario, Photo(), "image/png", IdentityFixture, childAnchor: null,
            CancellationToken.None);

        // The wrap exists, receipt and all — and it still measures as painted, which is the point of
        // recording it rather than pretending otherwise.
        Assert.NotEmpty(wrap.PoseId);
        Assert.NotEmpty(wrap.ManifestJson);
        Assert.True(CompositeSeamRepair.MeasureConstructionBands(wrap.BasePng).Exceeded);

        // Two pictures and no third: the regeneration is still bought, the ladder still ends.
        Assert.Equal(2, images.ImageCalls);

        var waiver = Assert.Single(waivers);
        Assert.Equal(BekiReleaseChecks.CoverBands, waiver.CheckId);

        // Page zero: the cover is not a page of the book, and zero is the page number no book has.
        Assert.Equal(0, waiver.Page);

        using var document = JsonDocument.Parse(waiver.EvidenceJson);
        Assert.Equal(
            "cover_construction_bands", document.RootElement.GetProperty("gate").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("attempts").GetArrayLength());
    }

    /// <summary>
    /// B3's other half, and the one worth a test of its own: the never-eligible failures stay
    /// terminal under the most permissive policy this system can express.
    ///
    /// A provider that will not draw without its references is not a taste — it is the difference
    /// between this book and a picture of a different child in a generic world. Shipping it would be
    /// the "internal machinery lies to the parent" fault with the sign flipped.
    /// </summary>
    [Fact]
    public async Task A_provider_failure_is_terminal_under_every_policy()
    {
        var images = new StubImageService { FailWhenStrict = true };
        var waivers = new List<CompositePolicyWaiver>();

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(context: Flagged(waivers)), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageGenerationFailed, failure.FailureCode);
        Assert.Empty(waivers);
    }

    /// <summary>
    /// And a picture the compositor cannot use is terminal too. Bytes that will not decode are not a
    /// judgement about artwork; there is no artwork.
    /// </summary>
    [Fact]
    public async Task Unusable_bytes_are_terminal_under_every_policy()
    {
        var images = new StubImageService { NextImage = [1, 2, 3] };
        var waivers = new List<CompositePolicyWaiver>();

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(context: Flagged(waivers)), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageGenerationFailed, failure.FailureCode);
        Assert.Empty(waivers);
    }

    /// <summary>
    /// A context with no policy at all behaves exactly as this pipeline did before the policy
    /// existed. Nothing softens because an argument was omitted.
    /// </summary>
    [Fact]
    public async Task A_run_with_no_policy_blocks_everything_it_used_to_block()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("CHILD_IDENTITY", CompositeQaVerdict.ActionHumanReview));

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageQaFailed, failure.FailureCode);
    }

    /// <summary>
    /// A waiver sink that throws must not take the book down with it. The alternative is a book with
    /// perfectly good artwork dying because the alarms table was unreachable, which is this
    /// campaign's own fault class arriving through the door marked "recording that we removed it".
    /// </summary>
    [Fact]
    public async Task A_waiver_sink_that_throws_does_not_cost_the_family_their_book()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("CHILD_IDENTITY", CompositeQaVerdict.ActionHumanReview));

        var context = Context() with
        {
            // Reviewing, for the reason the Flagged harness gives: a waiver sink cannot be tested
            // by a policy under which nothing is ever waived.
            ReleasePolicy = Reviewing(BekiReleasePolicySnapshot.Defaults),
            OnPolicyWaiver = _ => throw new InvalidOperationException("the alarms table is down"),
        };

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(context: context), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);
    }

    // ==============================================================================================
    // Harness
    // ==============================================================================================

    /// <summary>
    /// The shipped policy — every whitelisted check a flag — with the visual reviewer left ON, and
    /// a sink to read.
    ///
    /// The one override is deliberate and is the whole reason it is spelled out here. Under the
    /// owner's rule 5 of 2026-09-01 — "we don't need additional reviews for images" — the shipped
    /// default for <c>image_review</c> is <c>flag</c>, which means no model looks at a spread at
    /// all. Every test in this class is about what happens AFTER something refuses a page, so a
    /// harness that skipped the reviewer would leave them asserting against a refusal that never
    /// occurred — passing, and testing nothing.
    ///
    /// So this is the policy of a deployment that has turned the reviewer back on and left
    /// everything else as it ships, which is exactly the case these tests describe: the check runs,
    /// it refuses, and the flag decides that the family still gets their book.
    /// <see cref="CompositeReviewPolicyTests"/> is where the default — no review at all — lives.
    /// </summary>
    private static CompositeBookContext Flagged(List<CompositePolicyWaiver> waivers) =>
        Context() with
        {
            ReleasePolicy = Reviewing(BekiReleasePolicySnapshot.Defaults),
            OnPolicyWaiver = waiver =>
            {
                waivers.Add(waiver);
                return Task.CompletedTask;
            },
        };

    /// <summary>The same snapshot with the per-spread visual review switched back on.</summary>
    internal static BekiReleasePolicySnapshot Reviewing(BekiReleasePolicySnapshot policy) =>
        new(policy.Settings.Append(
            new BekiReleaseCheckSetting(
                BekiReleaseChecks.ImageReview, BekiReleaseSeverity.AllClasses,
                BekiReleaseSeverity.Blocker, "test", null)));

    /// <summary>The same run with every check a blocker, which is the pre-policy behaviour.</summary>
    private static CompositeBookContext Blocking(List<CompositePolicyWaiver> waivers) =>
        Context() with
        {
            ReleasePolicy = BekiReleasePolicySnapshot.Strict,
            OnPolicyWaiver = waiver =>
            {
                waivers.Add(waiver);
                return Task.CompletedTask;
            },
        };
}
