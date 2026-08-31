using System.Text.Json;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// QA follows the artwork — audit-2 P0-09 and D7, with amendment A4's adopted-page half.
///
/// The finding is one sentence: "PACKAGE_CONTENTS.json lists all eight qa/spread-XX-qa.json files
/// as missing", on a package that nonetheless contained final press and customer PDFs. Nothing was
/// broken in the reviewer — every page really was judged, and a refused page really did write its
/// record on the way out. The verdicts that mattered were the accepted ones, and those were held
/// in memory, used to decide whether to ship, and dropped. A book's QA either survives the book or
/// it was never evidence.
///
/// The adopted half is the same failure a level down. Adopted pages were filtered out of the
/// artifact list on the reasoning that this run had nothing new to say about them, which meant a
/// resumed book's record covered whatever it happened to redraw — six pages of evidence for an
/// eight-page book, indistinguishable from a book that only had six.
///
/// One of the classes CompositePipelineTestBase serves; see it for the fixtures these use.
/// </summary>
public class CompositeQaArtifactTests : CompositePipelineTestBase
{
    /// <summary>
    /// Every drawn page leaves its accepted verdict behind, as a document, with the provenance
    /// needed to judge it: which reviewer contract asked the questions and which image prompt drew
    /// the picture.
    /// </summary>
    [Fact]
    public async Task Every_drawn_spread_carries_the_verdict_it_was_accepted_on()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        var artifacts = result.Artifacts.Spreads;
        Assert.Equal(BookFormat.SpreadCount, artifacts.Count);

        foreach (var artifact in artifacts)
        {
            Assert.False(artifact.Adopted);
            Assert.NotNull(artifact.QaJson);

            using var document = JsonDocument.Parse(artifact.QaJson!);
            var root = document.RootElement;

            Assert.Equal(artifact.SpreadNumber, root.GetProperty("page").GetInt32());
            Assert.Equal(CompositeQaVerdict.Pass, root.GetProperty("status").GetString());
            Assert.Equal(
                CompositeQaVerdict.ActionPass, root.GetProperty("recommended_action").GetString());
            Assert.Equal(
                CompositeMinimalQa.Version, root.GetProperty("qa_prompt_version").GetString());
            Assert.Equal(
                CompositeIllustrationPrompt.Version,
                root.GetProperty("image_prompt_version").GetString());

            // The pose and the side the page was actually built with, so the verdict and the
            // composition receipt beside it describe one picture.
            Assert.Equal(artifact.PoseId, root.GetProperty("pose_id").GetString());
        }

        // Nothing about the child anywhere in the record: the picture beside it is the thing to
        // look at, and a QA document is the one artifact most likely to be pasted into a chat.
        Assert.All(
            artifacts,
            artifact => Assert.DoesNotContain(result_outfit, artifact.QaJson!, StringComparison.Ordinal));
    }

    /// <summary>
    /// The record is the accepted attempt's, and it counts what the page cost: a spread refused
    /// once and redrawn ships with two bases and two readings on its record, not one.
    ///
    /// Which is the difference between a QA document and a rubber stamp — the release gate reading
    /// these has to be able to see that a page took two tries.
    /// </summary>
    [Fact]
    public async Task A_page_that_took_two_tries_says_so_in_its_record()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("MAIN_SCENE_BEAT", CompositeQaVerdict.ActionRegenerateBase));

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        var first = result.Artifacts.Spreads.Single(artifact => artifact.SpreadNumber == 1);

        using var document = JsonDocument.Parse(first.QaJson!);
        var root = document.RootElement;

        Assert.Equal(2, root.GetProperty("base_attempts").GetInt32());
        Assert.Equal(2, root.GetProperty("review_attempts").GetInt32());

        // And it is the ACCEPTED verdict that is stored, not the refusal: the refusal's record is
        // the attempt rows, and this document is what the page shipped on.
        Assert.Equal(CompositeQaVerdict.Pass, root.GetProperty("status").GetString());
        Assert.Empty(root.GetProperty("failed_checks").EnumerateArray());
    }

    /// <summary>
    /// A9, held: a clear SHOT_COMPLIANCE failure keeps exactly the semantics it had — it
    /// recommends a redraw like any other failed category and the record says so — while the
    /// borderline <c>shot_note</c> and the age remark stay advisory and stay out of failed_checks.
    ///
    /// The correction plan is explicit that D12's "ships to the parent after the budget" language
    /// is void, and this is the assertion that would fail if a later change quietly promoted the
    /// advisories or demoted the check.
    /// </summary>
    [Fact]
    public async Task The_record_keeps_shot_failures_scored_and_shot_notes_advisory()
    {
        var images = new StubImageService();

        images.Verdicts.Enqueue(Fail("SHOT_COMPLIANCE", CompositeQaVerdict.ActionRegenerateBase));
        images.Verdicts.Enqueue(
            """
            {"status":"PASS","failed_checks":[],"recommended_action":"pass","notes":[],
             "shot_note":"reads closer than the wide shot asked for",
             "age_note":"reads a little older than four"}
            """);

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        // The failure was scored: it bought a second base, exactly as it always did.
        Assert.Equal(2, result.Spreads[0].BaseAttempts);

        using var document = JsonDocument.Parse(
            result.Artifacts.Spreads.Single(artifact => artifact.SpreadNumber == 1).QaJson!);
        var root = document.RootElement;

        // The advisories are carried as themselves and are not failed checks.
        Assert.Equal(CompositeQaVerdict.Pass, root.GetProperty("status").GetString());
        Assert.Empty(root.GetProperty("failed_checks").EnumerateArray());
        Assert.Equal(
            "reads closer than the wide shot asked for", root.GetProperty("shot_note").GetString());
        Assert.Equal("reads a little older than four", root.GetProperty("age_note").GetString());

        // And they reached the book-level review, which is where a human gate reads them.
        Assert.Single(result.Review.ShotAdvisories);
        Assert.Single(result.Review.AgeAdvisories);
    }

    // ---------------------------------------------------------------------------------------
    // Adopted pages are in the record — amendment A4
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// An adopted page is in the artifact list, flagged, carrying nothing this run did not do.
    ///
    /// The empty manifest and empty hash are the contract with the fulfilment layer rather than an
    /// oversight: this attempt composited nothing for that page, so there is no receipt to write,
    /// and the earlier attempt's real one is the one that must stay in the manifest.
    /// </summary>
    [Fact]
    public async Task An_adopted_page_is_in_the_record_flagged_and_empty()
    {
        var images = new StubImageService();
        var storedAnchor = Png(SpreadWidth, SpreadHeight, red: 77);

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(
                Request(resume: Resume(
                    stored: [1, 2],
                    anchor: storedAnchor,
                    qaFor: null)),
                CancellationToken.None);

        var artifacts = result.Artifacts.Spreads;

        // All eight pages of the book are in the record, not only the six this run drew.
        Assert.Equal(BookFormat.SpreadCount, artifacts.Count);
        Assert.Equal(
            new[] { 1, 2 },
            artifacts.Where(artifact => artifact.Adopted)
                .Select(artifact => artifact.SpreadNumber).ToList());

        foreach (var adopted in artifacts.Where(artifact => artifact.Adopted))
        {
            Assert.Null(adopted.QaJson);
            Assert.Equal(string.Empty, adopted.ManifestJson);
            Assert.Equal(string.Empty, adopted.OutputSha256);
            Assert.Empty(adopted.BasePng);
        }

        // The pages this run did draw carry a full receipt and a verdict, as always.
        foreach (var drawn in artifacts.Where(artifact => !artifact.Adopted))
        {
            Assert.NotNull(drawn.QaJson);
            Assert.NotEmpty(drawn.ManifestJson);
            Assert.NotEmpty(drawn.OutputSha256);
        }

        // A book adopted without QA records says so, once, rather than pretending its record is
        // complete — the release gates decide what the gap is worth.
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("carry no stored QA verdict", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------
    // The resume guard
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A caller that keeps QA records resumes on them: pages whose verdict is stored are adopted,
    /// and nothing is redrawn.
    /// </summary>
    [Fact]
    public async Task A_stored_page_with_its_verdict_is_adopted()
    {
        var images = new StubImageService();
        var storedAnchor = Png(SpreadWidth, SpreadHeight, red: 77);

        await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images).RunAsync(
            Request(resume: Resume(
                stored: [1, 2, 3],
                anchor: storedAnchor,
                qaFor: [1, 2, 3])),
            CancellationToken.None);

        // Three adopted, five drawn, and no page redrawn for want of a record.
        Assert.Equal(BookFormat.SpreadCount - 3, images.ImageCalls);
    }

    /// <summary>
    /// A page whose verdict is gone is not a page: it is redrawn rather than adopted on a record
    /// nobody can produce.
    ///
    /// The audit's correction is that a missing mandatory QA artifact stops assembly. A run that
    /// adopted such a page would be manufacturing exactly the rejected package one page at a time
    /// — the picture is there, the evidence is not, and nothing downstream can tell a verdict that
    /// was lost from one that was never asked for.
    /// </summary>
    [Fact]
    public async Task A_stored_page_whose_verdict_is_gone_is_redrawn()
    {
        var images = new StubImageService();
        var storedAnchor = Png(SpreadWidth, SpreadHeight, red: 77);

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(
                Request(resume: Resume(
                    stored: [1, 2, 3],
                    anchor: storedAnchor,
                    // Spread two's blob is gone; the other two are intact.
                    qaFor: [1, 3])),
                CancellationToken.None);

        // Six pictures: the five never drawn, plus spread two.
        Assert.Equal(BookFormat.SpreadCount - 2, images.ImageCalls);

        Assert.Equal(
            new[] { 1, 3 },
            result.Artifacts.Spreads
                .Where(artifact => artifact.Adopted)
                .Select(artifact => artifact.SpreadNumber)
                .ToList());

        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("Spread 2 was redrawn rather than adopted", StringComparison.Ordinal));
    }

    /// <summary>
    /// A verdict written by a different reviewer prompt is not adopted. It is not corrupt — it is
    /// a good answer to questions this deployment no longer asks — and the QA prompt has been
    /// revised six times, with v1.5 alone adding two failing categories. "This page passed" under
    /// v1.2 is not the same claim as "this page passed" under the contract now in force.
    /// </summary>
    [Fact]
    public void A_verdict_from_another_reviewer_version_is_not_adopted()
    {
        var current = CompositeSpreadQa.Write(
            page: 3, poseId: "pose_01_neutral_hover", textSide: "LEFT", baseAttempts: 1,
            reviewAttempts: 1,
            new CompositeQaVerdict(
                CompositeQaVerdict.Pass, [], CompositeQaVerdict.ActionPass, []));

        var read = CompositeSpreadQa.TryReadStored(current);
        Assert.NotNull(read);
        Assert.Equal(3, read!.Page);
        Assert.Equal(CompositeQaVerdict.Pass, read.Status);
        Assert.Equal(CompositeMinimalQa.Version, read.QaPromptVersion);

        Assert.Null(CompositeSpreadQa.TryReadStored(
            current.Replace(CompositeMinimalQa.Version, "minimal-visual-qa-v1.0")));

        // And every shape of "no record" reads the same way, because every caller does the same
        // thing with all of them.
        Assert.Null(CompositeSpreadQa.TryReadStored(null));
        Assert.Null(CompositeSpreadQa.TryReadStored("   "));
        Assert.Null(CompositeSpreadQa.TryReadStored("not json"));
        Assert.Null(CompositeSpreadQa.TryReadStored("""{"status":"PASS"}"""));
        Assert.Null(CompositeSpreadQa.TryReadStored(
            $$"""{"qa_prompt_version":"{{CompositeMinimalQa.Version}}"}"""));
    }

    /// <summary>
    /// The stored spreads, their bases, the anchor and — when the caller keeps them — their QA
    /// records, in the shape the fulfilment job hands over.
    /// </summary>
    /// <param name="qaFor">
    /// Null for a caller that keeps no QA at all, which is what every book stored before this
    /// campaign looks like. A list makes the caller QA-aware, and a page left off it is a page
    /// whose blob is gone.
    /// </param>
    private static CompositeResumeState Resume(int[] stored, byte[] anchor, int[]? qaFor)
    {
        var spreads = stored.ToDictionary(page => page, _ => BasePng());
        var bases = stored.ToDictionary(
            page => page, page => page == 1 ? anchor : BasePng());

        return new CompositeResumeState(ScenarioFixture(), spreads, bases)
        {
            IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
            AnchorBasePng = anchor,
            SpreadQaJson = qaFor is null
                ? new Dictionary<int, string>()
                : qaFor.ToDictionary(
                    page => page,
                    page => CompositeSpreadQa.Write(
                        page, "pose_01_neutral_hover", CompositeSpreadRhythm.TextSideFor(page),
                        baseAttempts: 1, reviewAttempts: 1,
                        new CompositeQaVerdict(
                            CompositeQaVerdict.Pass, [], CompositeQaVerdict.ActionPass, []))),
        };
    }
}
