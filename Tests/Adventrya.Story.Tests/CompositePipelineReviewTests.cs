using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Ai;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The polished composite story path, minimal visual QA, the placement retry, what a page marked
/// for human review leaves behind, and the printed canvas.
///
/// One of the classes CompositePipelineTestBase serves; see it for the fixtures these use.
/// </summary>
public class CompositePipelineReviewTests : CompositePipelineTestBase
{
    // ---------------------------------------------------------------------------------------
    // R12b — the composite story path is polished
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The composite plan goes through the editor, which this path simply never did.
    ///
    /// The composite prompt was forked from v6's *writer*, so every composite book shipped its first
    /// draft — which is how ფუნღუროში and ეწყოს reached a printed page. The pass is the same one v6
    /// has: one call, after the whole book exists, with only prose merged back.
    /// </summary>
    [Fact]
    public async Task A_composite_plan_is_edited_before_it_is_returned()
    {
        var written = CompositePlanJson(spreads: 8);
        var corrected = CompositePlanJson(
            spreads: 8,
            title: "ბაფუს ბილიკი და ვარსკვლავი",
            spreadText: (3, "ნინა და ბეკი ფუღუროში — გვერდი 3."));

        var client = new ScriptedStoryModelClient(written, corrected);

        var result = await CompositeStoryService(client).WriteCompositePlanAsync(
            CompositeStoryInputFixture(), [], CancellationToken.None);

        Assert.Equal(2, client.Calls);

        // The editor is asked in the composite schema, with the composite editor's rules.
        Assert.Equal(CompositeStorySchema.Name, "composite_book_plan");
        Assert.Contains("You are an editor of Georgian children's books", client.SystemPrompts[1]);
        Assert.Contains("MISSPELLINGS", client.SystemPrompts[1]);
        Assert.Contains("„ფუნღუროში“", client.SystemPrompts[1]);
        Assert.Contains("This book is Georgian only", client.SystemPrompts[1]);
        Assert.Contains("3-5 age band", client.UserPrompts[1]);

        // What crossed back: the title and the spread text, and that is all there is here.
        Assert.Equal("ბაფუს ბილიკი და ვარსკვლავი", result.Story.Concept.Title);
        Assert.Equal("ნინა და ბეკი ფუღუროში — გვერდი 3.", result.Story.Spreads[2].Text);

        // Both calls are on the record — prompts and tokens — the way v6 records its two.
        Assert.Contains("===== STEP 2 =====", result.SystemPrompt);
        Assert.Equal(2, result.PromptTokens);
        Assert.Equal(2, result.CompletionTokens);
    }

    /// <summary>
    /// The editor is shown the book in the shape it must answer in — no character lock, no English.
    ///
    /// A document carrying fields the response schema forbids is an invitation to answer in the
    /// shape it was shown, and <c>characterLock</c> is the paragraph this path deliberately does not
    /// have.
    /// </summary>
    [Fact]
    public async Task The_editor_is_shown_a_georgian_only_book()
    {
        var client = new ScriptedStoryModelClient(CompositePlanJson(spreads: 8), CompositePlanJson(spreads: 8));

        await CompositeStoryService(client).WriteCompositePlanAsync(
            CompositeStoryInputFixture(), [], CancellationToken.None);

        Assert.DoesNotContain("characterLock", client.UserPrompts[1]);
        Assert.DoesNotContain("titleEn", client.UserPrompts[1]);
        Assert.DoesNotContain("textEn", client.UserPrompts[1]);
        Assert.Contains("worldLock", client.UserPrompts[1]);
    }

    /// <summary>
    /// A polish that renumbers the spreads is dropped whole: without the same numbers on both sides
    /// there is no correspondence to merge along, and merging by position would put one spread's
    /// text under another spread's picture.
    /// </summary>
    [Fact]
    public async Task A_polish_that_renumbers_the_spreads_is_not_merged()
    {
        var renumbered = CompositePlanJson(
            spreads: 8,
            spreadText: (2, "სულ სხვა ტექსტი."),
            renumberFirstSpreadTo: 9);

        var client = new ScriptedStoryModelClient(CompositePlanJson(spreads: 8), renumbered);

        var result = await CompositeStoryService(client).WriteCompositePlanAsync(
            CompositeStoryInputFixture(), [], CancellationToken.None);

        // The written book stands, whole.
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], result.Story.Spreads.Select(s => s.Number));
        Assert.Equal("ნინა და ბეკი — გვერდი 2.", result.Story.Spreads[1].Text);

        // The call was still made and paid for, so it is still on the record.
        Assert.Contains("===== STEP 2 =====", result.SystemPrompt);
        Assert.Equal(2, result.PromptTokens);
    }

    /// <summary>
    /// A polish that empties a spread's text is dropped rather than merged: the polisher can only
    /// improve prose, never remove it, and an empty spread is a blank page in a printed book.
    /// </summary>
    [Fact]
    public async Task A_polish_that_empties_a_spread_is_not_merged()
    {
        var emptied = CompositePlanJson(spreads: 8, spreadText: (5, string.Empty));

        var client = new ScriptedStoryModelClient(CompositePlanJson(spreads: 8), emptied);

        var result = await CompositeStoryService(client).WriteCompositePlanAsync(
            CompositeStoryInputFixture(), [], CancellationToken.None);

        Assert.Equal("ნინა და ბეკი — გვერდი 5.", result.Story.Spreads[4].Text);
    }

    /// <summary>
    /// A failed polish call keeps the written book. It is best-effort by design: a book with an
    /// unpolished sentence beats no book at all.
    /// </summary>
    [Fact]
    public async Task A_failed_polish_keeps_the_written_book()
    {
        var client = new ThrowingAfterFirstCallClient(CompositePlanJson(spreads: 8));

        var result = await CompositeStoryService(client).WriteCompositePlanAsync(
            CompositeStoryInputFixture(), [], CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Story.Spreads.Count);
        Assert.Equal("ბაფუს ბილიკი", result.Story.Concept.Title);

        // A polish that never happened leaves no separator behind, so a stored prompt never
        // describes a call that was not made.
        Assert.DoesNotContain("===== STEP 2 =====", result.SystemPrompt);
    }

    // ---------------------------------------------------------------------------------------
    // Minimal visual QA and its retry rules
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_base_image_failure_buys_exactly_one_new_image()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("MAIN_SCENE_BEAT", CompositeQaVerdict.ActionRegenerateBase));

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        // Nine calls for eight spreads: spread one was drawn twice and then passed.
        Assert.Equal(BookFormat.SpreadCount + 1, images.ImageCalls);
        Assert.Equal(2, result.Spreads[0].BaseAttempts);
        Assert.Equal(1, result.Spreads[1].BaseAttempts);
    }

    [Fact]
    public async Task A_placement_failure_is_re_composited_without_another_image_call()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("BEKI_INTEGRATION", CompositeQaVerdict.ActionRecompositeBeki));

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.Equal(1, result.Spreads[0].BaseAttempts);

        // The review ran twice for spread one and once for each of the others.
        Assert.Equal(BookFormat.SpreadCount + 1, images.ReviewCalls);
    }

    // ---------------------------------------------------------------------------------------
    // The placement retry that used to do nothing
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The regression, stated as the thing that was actually wrong: the re-composite retry must
    /// hand the reviewer a different picture.
    ///
    /// A real book died on this. Spread seven was refused for FOLD_SAFETY with
    /// recommended_action=recomposite_beki; the retry re-composited the same base, with the same
    /// pose, at the same configured anchor, through arithmetic that is deterministic by design; so
    /// the second image was byte-for-byte the first, and the reviewer refused it again in the same
    /// words. The pack stopped at spread seven having paid for two reviews of one picture.
    ///
    /// Bytes rather than counts, because counts cannot tell the two apart.
    /// </summary>
    [Fact]
    public async Task A_placement_retry_moves_Beki_rather_than_re_reviewing_the_same_picture()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("FOLD_SAFETY", CompositeQaVerdict.ActionRecompositeBeki));

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        // No second picture was bought: the whole point of this rung is that it is arithmetic.
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.Equal(1, result.Spreads[0].BaseAttempts);

        // The reviewer saw two different pages.
        Assert.NotEqual(images.ReviewImages[0], images.ReviewImages[1]);

        // And they differ because Beki moved, not because anything was redrawn: further from the
        // centre of the sheet, and drawn a little smaller.
        var attempts = result.Spreads[0].Attempts;
        Assert.Equal(2, attempts.Count);

        var before = attempts[0].Anchor!;
        var after = attempts[1].Anchor!;

        Assert.Equal(0.594, before.VisibleCenterX, 3);
        Assert.Equal(0.654, after.VisibleCenterX, 3);
        Assert.True(
            Math.Abs(after.VisibleCenterX - 0.5) > Math.Abs(before.VisibleCenterX - 0.5),
            "Beki did not move away from the centre.");
        Assert.Equal(before.VisibleHeight * 0.9, after.VisibleHeight, 4);

        // The manifest that ships is the accepted attempt's, with the adjusted anchor in its
        // ordinary fields — a receipt for where she actually is on the page.
        var shipped = result.Spreads[0].Manifest.BekiLayer;
        Assert.Equal(0.654, shipped.NormalizedAnchor.VisibleCenterX, 3);
        Assert.Equal(after.VisibleHeight, shipped.NormalizedAnchor.VisibleHeight, 4);

        // And nothing was done to the artwork itself, which is the rule the whole pipeline rests on.
        Assert.False(shipped.Mirrored || shipped.Rotated || shipped.Warped || shipped.Redrawn);
        Assert.Equal(1.0, shipped.Opacity);
    }

    /// <summary>
    /// The nudge, at both text sides and against the bounds it has to respect.
    ///
    /// The direction is not symmetric decoration: Beki stands in the half of the spread the
    /// Georgian does not occupy, so "away from the centre" is rightward on a left-text page and
    /// leftward on a right-text one. And the clamp is the deterministic checks' own rule read
    /// forwards — fully on the canvas, never one pixel inside the reserved third — because an
    /// adjustment that violated either would fail the composite instead of the review.
    /// </summary>
    [Fact]
    public void The_placement_nudge_moves_outward_and_stops_at_the_bounds()
    {
        var config = BekiCompositeConfig.Load();

        var left = config.StoryDefaultFor(BekiTextSide.Left);
        var right = config.StoryDefaultFor(BekiTextSide.Right);

        // A rendered width of a tenth of the canvas, which is roughly what the approved pose comes
        // out at on a spread.
        const int canvas = 1536;
        const int rendered = 150;

        var nudgedLeft = left.NudgedAwayFromCentre(BekiTextSide.Left, canvas, rendered)!;
        var nudgedRight = right.NudgedAwayFromCentre(BekiTextSide.Right, canvas, rendered)!;

        Assert.Equal(left.VisibleCenterX + 0.06, nudgedLeft.VisibleCenterX, 4);
        Assert.Equal(right.VisibleCenterX - 0.06, nudgedRight.VisibleCenterX, 4);

        // The vertical centre never moves: the exclusion this repairs is a vertical strip, and
        // raising or lowering her would only change which part of it she crosses.
        Assert.Equal(left.VisibleCenterY, nudgedLeft.VisibleCenterY);
        Assert.Equal(right.VisibleCenterY, nudgedRight.VisibleCenterY);

        // Both stay on the canvas and out of the third the text is printed over.
        AssertWithinBounds(nudgedLeft, BekiTextSide.Left, canvas, rendered);
        AssertWithinBounds(nudgedRight, BekiTextSide.Right, canvas, rendered);

        // Already at the outer edge: the step is clamped rather than pushing her off the canvas.
        var atTheEdge = left with { VisibleCenterX = 0.95 };
        var clamped = atTheEdge.NudgedAwayFromCentre(BekiTextSide.Left, canvas, rendered)!;

        Assert.True(clamped.VisibleCenterX <= 1 - (rendered / 2.0 / canvas));
        AssertWithinBounds(clamped, BekiTextSide.Left, canvas, rendered);

        // The mirror image on the other side.
        var atTheOtherEdge = right with { VisibleCenterX = 0.05 };
        var clampedRight = atTheOtherEdge.NudgedAwayFromCentre(BekiTextSide.Right, canvas, rendered)!;

        Assert.True(clampedRight.VisibleCenterX >= rendered / 2.0 / canvas);
        AssertWithinBounds(clampedRight, BekiTextSide.Right, canvas, rendered);

        // A sprite with no window to move in is null rather than a nudge of zero: the caller asked
        // for a different picture and has to know it cannot have one.
        Assert.Null(left.NudgedAwayFromCentre(BekiTextSide.Left, canvas, canvas));
        Assert.Null(left.NudgedAwayFromCentre(BekiTextSide.Left, canvas, canvas * 2 / 3));
        Assert.Null(left.NudgedAwayFromCentre(BekiTextSide.Left, 0, rendered));

        static void AssertWithinBounds(
            BekiCompositeAnchor anchor, BekiTextSide side, int canvasWidth, int renderedWidth)
        {
            var half = renderedWidth / 2.0 / canvasWidth;
            var third = 1.0 / 3.0;

            Assert.True(anchor.VisibleCenterX - half >= 0, "Beki left the canvas on the left.");
            Assert.True(anchor.VisibleCenterX + half <= 1, "Beki left the canvas on the right.");

            if (side == BekiTextSide.Left)
            {
                Assert.True(anchor.VisibleCenterX - half >= third, "Beki entered the text third.");
            }
            else
            {
                Assert.True(anchor.VisibleCenterX + half <= 1 - third, "Beki entered the text third.");
            }

            anchor.Validate();
        }
    }

    /// <summary>
    /// The adjusted anchor survives the deterministic checks — which is the assertion that matters,
    /// because those checks are what would otherwise turn a repaired placement into
    /// IMAGE_GENERATION_FAILED. Composited for real, on both sides.
    /// </summary>
    [Theory]
    [InlineData("LEFT")]
    [InlineData("RIGHT")]
    public void A_nudged_composite_still_passes_the_deterministic_checks(string textSide)
    {
        var engine = BekiCompositeEngine.Create();
        var side = BekiCompositeConfig.ParseTextSide(textSide);

        var first = engine.CompositeStorySpread(
            SpreadShapedPng(), "base.png", "pose_04_listen", side, "out.png");

        Assert.Empty(CompositeDeterministicChecks.CompositeProblems(
            first.Manifest, engine.Registry, side));

        var nudged = new BekiCompositeAnchor(
                first.Manifest.BekiLayer.NormalizedAnchor.VisibleCenterX,
                first.Manifest.BekiLayer.NormalizedAnchor.VisibleCenterY,
                first.Manifest.BekiLayer.NormalizedAnchor.VisibleHeight)
            .NudgedAwayFromCentre(
                side,
                first.Manifest.Canvas.WidthPx,
                first.Manifest.BekiLayer.RenderedSizePx.WidthPx);

        Assert.NotNull(nudged);

        var second = engine.CompositeStorySpread(
            SpreadShapedPng(), "base.png", "pose_04_listen", side, "out.png", nudged);

        Assert.Empty(CompositeDeterministicChecks.CompositeProblems(
            second.Manifest, engine.Registry, side));

        // A different page, and the receipt says where she went.
        Assert.NotEqual(first.Png, second.Png);
        Assert.NotEqual(
            first.Manifest.BekiLayer.PlacementPx.XPx, second.Manifest.BekiLayer.PlacementPx.XPx);
        Assert.Equal(
            nudged!.VisibleCenterX, second.Manifest.BekiLayer.NormalizedAnchor.VisibleCenterX, 6);

        // The adjusted anchor goes into the ordinary fields and the receipt still satisfies the
        // supplied schema — which is what "schema-compatible" has to mean: new values, no new
        // fields, and the locked constants untouched.
        using var receipt = JsonDocument.Parse(second.Manifest.ToJson());
        var evaluation = CompositionManifestSchema.Value.Evaluate(
            receipt.RootElement,
            new Json.Schema.EvaluationOptions { OutputFormat = Json.Schema.OutputFormat.List });

        Assert.True(evaluation.IsValid, "the nudged composite's manifest does not satisfy the supplied schema.");
    }

    /// <summary>
    /// The ladder, in order and with its budget: a placement refused twice spends the base image it
    /// has not yet bought, at the approved anchor, and then stops.
    ///
    /// The third rung exists because a placement the reviewer refuses at two different anchors is
    /// evidence about the picture rather than about the placement — there was nowhere on that base
    /// to put her. What it is not is an open-ended search: two generated images and three reviews
    /// is the whole budget, and the run stops with the agreed code.
    /// </summary>
    [Fact]
    public async Task A_placement_refused_twice_spends_the_unused_base_image_and_then_stops()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("FOLD_SAFETY", CompositeQaVerdict.ActionRecompositeBeki));
        images.Verdicts.Enqueue(Fail("FOLD_SAFETY", CompositeQaVerdict.ActionRecompositeBeki));

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        // Nine images for eight spreads: spread one was drawn twice, and no more than twice.
        Assert.Equal(BookFormat.SpreadCount + 1, images.ImageCalls);
        Assert.Equal(2, result.Spreads[0].BaseAttempts);

        // Three reviews for spread one, and one each for the rest.
        Assert.Equal(BookFormat.SpreadCount + 2, images.ReviewCalls);

        var attempts = result.Spreads[0].Attempts;
        Assert.Equal(3, attempts.Count);

        // The order the rungs were climbed, read off the rows: the configured anchor, the adjusted
        // one, then the configured one again on the new picture — the nudge belonged to the base
        // that was thrown away.
        Assert.Equal(0.594, attempts[0].Anchor!.VisibleCenterX, 3);
        Assert.Equal(0.654, attempts[1].Anchor!.VisibleCenterX, 3);
        Assert.Equal(0.594, attempts[2].Anchor!.VisibleCenterX, 3);
        Assert.Equal(attempts[0].Anchor!.VisibleHeight, attempts[2].Anchor!.VisibleHeight, 6);

        // And the costs are where the rules say: the free retry generated nothing, the escalation
        // generated a picture.
        Assert.True(attempts[0].GenerationMs >= 0);
        Assert.Equal(0, attempts[1].GenerationMs);
        Assert.True(attempts[2].GenerationMs >= 0);
        Assert.True(attempts[2].Accepted);
    }

    /// <summary>
    /// And when the escalation does not save it either, the book stops — inside the same budget.
    /// </summary>
    [Fact]
    public async Task A_page_that_fails_every_rung_stops_after_two_images_and_three_reviews()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("FOLD_SAFETY", CompositeQaVerdict.ActionRecompositeBeki));
        images.Verdicts.Enqueue(Fail("FOLD_SAFETY", CompositeQaVerdict.ActionRecompositeBeki));
        images.Verdicts.Enqueue(Fail("FOLD_SAFETY", CompositeQaVerdict.ActionHumanReview));

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageQaFailed, failure.FailureCode);
        Assert.Equal(1, failure.Page);

        // Two generations and three reviews for the page, and nothing at all for the seven spreads
        // that never started — the book failed as a book.
        Assert.Equal(2, images.ImageCalls);
        Assert.Equal(3, images.ReviewCalls);

        // Every review saw a different picture. The no-op retry is what this book died of.
        Assert.NotEqual(images.ReviewImages[0], images.ReviewImages[1]);
        Assert.NotEqual(images.ReviewImages[1], images.ReviewImages[2]);
        Assert.NotEqual(images.ReviewImages[0], images.ReviewImages[2]);
    }

    /// <summary>
    /// A first verdict of human_review still stops the book on the spot: "the failure source is
    /// ambiguous" is not a question another picture answers, and the escalation is deliberately
    /// reachable only after a placement has actually been tried and refused.
    /// </summary>
    [Fact]
    public async Task An_ambiguous_first_verdict_still_stops_the_book_without_buying_anything()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("CAST_ERROR", CompositeQaVerdict.ActionHumanReview));

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageQaFailed, failure.FailureCode);
        Assert.Equal(1, images.ImageCalls);
        Assert.Equal(1, images.ReviewCalls);
    }

    // ---------------------------------------------------------------------------------------
    // What a page marked for human review leaves behind
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A terminal QA failure carries the refused picture and the attempt record out of the pipeline.
    ///
    /// The pack that failed at spread seven left a directory holding spreads one to six and nothing
    /// else: the composite that was generated, paid for and judged went into an exception message
    /// and out of existence. "Marked for human review" has to leave a human something to review.
    /// </summary>
    [Fact]
    public async Task A_terminal_QA_failure_carries_the_refused_page_and_its_verdicts_out()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("FOLD_SAFETY", CompositeQaVerdict.ActionRecompositeBeki));
        images.Verdicts.Enqueue(Fail("FOLD_SAFETY", CompositeQaVerdict.ActionRecompositeBeki));
        images.Verdicts.Enqueue(Fail("FOLD_SAFETY", CompositeQaVerdict.ActionHumanReview));

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(), CancellationToken.None));

        var evidence = failure.Evidence;
        Assert.NotNull(evidence);
        Assert.Equal(1, evidence!.Page);

        // The picture the reviewer actually refused, at the size the book prints — the composite,
        // Beki included, not the base underneath it.
        Assert.Equal(images.ReviewImages[^1], evidence.CompositePng);

        var refused = Image.Identify(evidence.CompositePng);
        Assert.Equal(SpreadWidth, refused.Width);
        Assert.Equal(SpreadHeight, refused.Height);

        // And the paperwork: every cycle, in order, with what was said and where Beki stood.
        using var qa = JsonDocument.Parse(evidence.QaJson);
        var root = qa.RootElement;

        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal("IMAGE_QA_FAILED", root.GetProperty("failure_code").GetString());
        Assert.Equal(CompositeMinimalQa.Version, root.GetProperty("qa_prompt_version").GetString());
        Assert.Equal(CompositeIllustrationPrompt.Version, root.GetProperty("image_prompt_version").GetString());
        Assert.Equal("pose_04_listen", root.GetProperty("pose_id").GetString());
        Assert.Equal(2, root.GetProperty("base_attempts").GetInt32());

        var rows = root.GetProperty("attempts").EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Contains("FOLD_SAFETY", row.GetProperty("verdict").GetString()));

        // The rows are what make the retry auditable: two different placements, so a reader can see
        // the pipeline moved her rather than reviewing the same page twice.
        var placements = rows
            .Select(row => row.GetProperty("beki_anchor").GetProperty("visible_center_x").GetDouble())
            .ToList();

        Assert.Equal(0.594, placements[0], 3);
        Assert.Equal(0.654, placements[1], 3);
        Assert.NotEqual(placements[0], placements[1]);

        // Nothing about the child is in it. It is a record of a placement decision, and the picture
        // beside it is the thing to look at.
        Assert.DoesNotContain("Hair", evidence.QaJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(result_outfit, evidence.QaJson);
    }

    /// <summary>
    /// A failure with no page to show carries no evidence — an input the boundary refused has no
    /// picture, and an empty file in a pack directory is worse than no file.
    /// </summary>
    [Fact]
    public async Task A_failure_with_nothing_to_look_at_carries_no_evidence()
    {
        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), new StubImageService())
                .RunAsync(
                    Request() with { ChildPhoto = TruncatedJpeg(640, 480) },
                    CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.InvalidBookInput, failure.FailureCode);
        Assert.Null(failure.Evidence);
    }

    [Fact]
    public async Task A_second_failure_stops_the_book_with_IMAGE_QA_FAILED()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("CHILD_IDENTITY", CompositeQaVerdict.ActionRegenerateBase));
        images.Verdicts.Enqueue(Fail("CHILD_IDENTITY", CompositeQaVerdict.ActionHumanReview));

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageQaFailed, failure.FailureCode);
        Assert.Equal(1, failure.Page);

        // One regeneration, and no third attempt.
        Assert.Equal(2, images.ImageCalls);
    }

    /// <summary>
    /// The deterministic side of the QA contract, which runs before any of the above.
    /// </summary>
    [Fact]
    public void A_verdict_is_read_strictly_and_a_composite_is_checked_from_its_manifest()
    {
        Assert.True(CompositeMinimalQa.Parse(File.ReadAllText(FixturePath("spread_01_minimal_qa.json"))).IsValid);

        // A PASS carrying a failed check is not a pass; the schema's conditional says so and the
        // reader defers to the schema rather than reading `status` and stopping there.
        var contradictory = CompositeMinimalQa.Parse(
            """{"status":"PASS","failed_checks":["CAST_ERROR"],"recommended_action":"pass","notes":[]}""");
        Assert.False(contradictory.IsValid);

        // An invented category is not one of the nine.
        Assert.False(CompositeMinimalQa.Parse(
            """{"status":"FAIL","failed_checks":["UGLY"],"recommended_action":"human_review","notes":[]}""")
            .IsValid);

        // Prose is not a verdict.
        Assert.False(CompositeMinimalQa.Parse("Looks good to me!").IsValid);

        // A fenced verdict still is one: the wrapper is forgiven, the content is not.
        Assert.True(CompositeMinimalQa.Parse(
            "```json\n{\"status\":\"PASS\",\"failed_checks\":[],\"recommended_action\":\"pass\",\"notes\":[]}\n```")
            .IsValid);
    }

    [Fact]
    public void A_render_of_the_wrong_shape_never_reaches_the_compositor()
    {
        Assert.Empty(CompositeDeterministicChecks.BaseImageProblems(BasePng()));

        // 3:2, which is what the providers actually offer, normalizes with 30% of the height gone.
        Assert.Empty(CompositeDeterministicChecks.BaseImageProblems(Png(1536, 1024)));

        // A portrait render is not a spread, however good the picture is.
        Assert.NotEmpty(CompositeDeterministicChecks.BaseImageProblems(Png(1024, 1536)));

        Assert.NotEmpty(CompositeDeterministicChecks.BaseImageProblems([]));
        Assert.NotEmpty(CompositeDeterministicChecks.BaseImageProblems([1, 2, 3, 4]));
    }

    // ---------------------------------------------------------------------------------------
    // The printed canvas
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Beki is pasted onto the page that prints, not onto the frame the provider returned.
    ///
    /// The failure this prevents is arithmetic and invisible. The image models give 3:2; the spread
    /// prints at 15:7; the layout stage removes the difference. Compositing before that crop meant
    /// the configured 0.333 of the canvas became about 0.476 of the printed page — Beki half again
    /// the approved size — the manifest recorded coordinates for a canvas that was never a page, and
    /// the reviewer judged a frame with bands top and bottom that no reader would ever see.
    /// </summary>
    [Fact]
    public async Task The_base_is_normalized_to_the_printed_spread_before_Beki_is_pasted()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        // What the provider returned, and what was composited on.
        var provider = Image.Identify(images.Returned[0]);
        Assert.Equal(ProviderWidth, provider.Width);
        Assert.Equal(ProviderHeight, provider.Height);

        var canvas = result.Spreads[0].Manifest.Canvas;
        Assert.Equal(SpreadWidth, canvas.WidthPx);
        Assert.Equal(SpreadHeight, canvas.HeightPx);

        // The crop is centred and never stretched: the width is untouched and only the height moved.
        Assert.Equal(provider.Width, canvas.WidthPx);
        Assert.True(canvas.HeightPx < provider.Height);
        Assert.Equal(
            15.0 / 7.0,
            (double)canvas.WidthPx / canvas.HeightPx,
            CompositeDeterministicChecks.SpreadAspectTolerance);

        // The number the whole finding is about: Beki occupies the configured fraction OF THE
        // PRINTED PAGE. Composited on the provider frame she would have measured about 0.476 here.
        var layer = result.Spreads[0].Manifest.BekiLayer;
        var printedFraction = (double)layer.RenderedSizePx.HeightPx / canvas.HeightPx;

        Assert.Equal(0.333, printedFraction, 2);
        Assert.True(printedFraction < 0.40, $"Beki is {printedFraction:F3} of the printed page.");

        // And the base kept for storage and continuity is the normalized one, so a re-composite or
        // a resumed run works from the same canvas.
        var storedBase = Image.Identify(result.Spreads[0].BasePng);
        Assert.Equal(SpreadWidth, storedBase.Width);
        Assert.Equal(SpreadHeight, storedBase.Height);

        // The composited page is the same canvas as the base it was made from.
        var page = Image.Identify(result.Spreads[0].CompositePng);
        Assert.Equal(SpreadWidth, page.Width);
        Assert.Equal(SpreadHeight, page.Height);
    }

    /// <summary>
    /// A render already at the printed ratio passes through untouched — normalization trims what is
    /// in excess of the target and never crops for the sake of cropping.
    /// </summary>
    [Fact]
    public void A_spread_shaped_render_is_already_normalized()
    {
        var spread = SpreadShapedPng();

        Assert.Empty(CompositeDeterministicChecks.NormalizedSpreadProblems(spread));
        Assert.Equal(spread, SpreadArtCrop.CropToRatio(spread, 15f / 7f));

        // And the provider's own frame is not: this is what the check is for.
        Assert.NotEmpty(CompositeDeterministicChecks.NormalizedSpreadProblems(
            Png(ProviderWidth, ProviderHeight)));
    }
}
