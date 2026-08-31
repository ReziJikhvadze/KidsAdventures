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
/// The cover the composite pipeline does NOT draw, and the age the book is drawn to.
///
/// This class used to be about a redraw: after the spreads were accepted, an image model drew the
/// customer's front page again with the identity lock in its prompt, and the reader was re-pointed
/// at the result. Audit-2 P0-01 ended that — the printer's cover was the composited wrap and the
/// parent's was the redraw, which is two designs for one book, and the supplier rejected the
/// package for it. The five tests that pinned the redraw's ladder went with the code (correction
/// plan D1); the single cover master is made in fulfilment now, from the wrap, and
/// <c>CompositeCoverProjectionTests</c> is where it is pinned.
///
/// What is left here is what outlived the redraw: that this path draws no cover of its own, the
/// cover review prompt's own shape and rules (the wrap's QA still asks them), and the age band the
/// book is drawn and judged to.
///
/// One of the classes CompositePipelineTestBase serves; see it for the fixtures these use.
/// </summary>
public class CompositePipelineCoverTests : CompositePipelineTestBase
{
    // ---------------------------------------------------------------------------------------
    // The cover this path leaves alone
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A resume that adopted every page draws no cover — and neither, now, does any other run.
    ///
    /// The assertion outlived the redraw it was written for. It used to prove that a fully-adopted
    /// resume did not buy a second cover and overwrite the reviewed one an earlier attempt had
    /// stored; it now proves the flat rule that replaced that reasoning, which is that this path
    /// hands back the cover it was given and produces none of its own.
    /// </summary>
    [Fact]
    public async Task A_fully_adopted_resume_draws_no_cover_at_all()
    {
        var images = new StubImageService();
        var previewed = Png(1024, 1536, red: 42);

        var everySpread = Enumerable.Range(1, BookFormat.SpreadCount)
            .ToDictionary(page => page, _ => BasePng());

        var generator = Generator(
            images, Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images),
            compositeEnabled: true);

        var book = await generator.IllustrateAsync(
            Plan(), Photo(), "image/png", existingCover: previewed, onImage: null,
            cancellationToken: CancellationToken.None, existingSpreads: null,
            composite: Context() with
            {
                Resume = new CompositeResumeState(ScenarioFixture(), everySpread, everySpread)
                {
                    IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
                    AnchorBasePng = BasePng(),
                },
            });

        // Nothing was drawn: not a spread, and not a cover.
        Assert.Equal(0, images.ImageCalls);
        Assert.Empty(images.CoverReviewPrompts);

        // The cover the caller came in with is the cover it leaves with, and it carries no attempt
        // rows — which is what the fulfilment job reads to leave the stored cover alone.
        Assert.Equal(previewed, book.Cover.Image);
        Assert.Empty(book.Cover.AttemptDetails);
    }

    /// <summary>
    /// And a run that draws all eight spreads still draws no cover — the case the old redraw
    /// existed for, which is now simply the same rule as every other case.
    ///
    /// <c>CoverAlreadyRedrawn</c> is set here because a resumed book still carries it on the
    /// context; nothing reads it any more, and the point of the test is that the answer no longer
    /// depends on it.
    /// </summary>
    [Fact]
    public async Task A_run_that_draws_every_spread_still_draws_no_cover()
    {
        var images = new StubImageService();
        var previewed = Png(1024, 1536, red: 42);

        var generator = Generator(
            images, Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images),
            compositeEnabled: true);

        var book = await generator.IllustrateAsync(
            Plan(), Photo(), "image/png", existingCover: previewed, onImage: null,
            cancellationToken: CancellationToken.None, existingSpreads: null,
            composite: Context() with { CoverAlreadyRedrawn = true });

        // The eight spreads were drawn; the cover was not.
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.Empty(images.CoverReviewPrompts);
        Assert.Equal(previewed, book.Cover.Image);
    }

    /// <summary>
    /// The cover ask is the spread reviewer with a cover's page description: the same nine
    /// categories and the same schema, minus the two criteria a cover cannot fail, plus the
    /// identity checks that are the whole reason it is reviewed at all.
    /// </summary>
    [Fact]
    public void The_cover_review_asks_about_a_cover_and_names_the_eye_colour()
    {
        var ask = CompositeMinimalQa.CoverPrompt(
            "The child stands at the valley's edge with Beki beside them.",
            "a soft mustard-yellow romper",
            IdentityFixture);

        // The contract's own instruction, unchanged.
        Assert.Contains(CompositeMinimalQa.SystemInstruction, ask);

        Assert.Contains("This is the book's COVER, not a story spread.", ask);
        Assert.Contains("Do not fail it for TEXT_SAFE_AREA or FOLD_SAFETY.", ask);
        Assert.DoesNotContain("Reserved text side:", ask);
        Assert.DoesNotContain("Central exclusion zone:", ask);

        Assert.Contains("The child's eyes must read as brown on this cover.", ask);
        Assert.Contains("Child identity spec for this book:", ask);
        Assert.Contains("Eyebrows: soft, medium-thick, gently arched", ask);
        Assert.Contains("This child wears no glasses", ask);

        // And a child who does wear them gets the rule the other way round.
        var bespectacled = CompositeMinimalQa.CoverPrompt(
            "scene", "outfit", IdentityFixture with { Glasses = "round thin gold frames" });

        Assert.Contains("This child wears glasses (round thin gold frames)", bespectacled);
        Assert.Contains("glasses missing here are a CHILD_IDENTITY failure", bespectacled);
    }

    /// <summary>
    /// The preview cover carries the parent's eye colour, on a composite run whose character lock
    /// is empty by design.
    ///
    /// This is the other end of the same defect. The composite planner may not invent an
    /// appearance, so the plan's character lock is stored as an empty string; the preview cover is
    /// composed from that lock; and the eye colour the parent typed into the form reached nothing
    /// at all. The run row had it the whole time.
    /// </summary>
    [Fact]
    public async Task A_preview_cover_states_the_eye_colour_the_parent_chose()
    {
        var story = new RecordingMasterStoryService();
        var runs = new RecordingRunRepository();
        var images = new StubImageService();

        runs.Run.EyeColor = "green";

        await PreviewService(story, runs, compositeEnabled: true, images: images)
            .WriteBookAsync(runs.Run.Id, CancellationToken.None);

        // The cover prompt the preview actually sent.
        Assert.NotEmpty(images.Prompts);
        var coverPrompt = images.Prompts[^1];

        Assert.Contains("The child's eyes are green.", coverPrompt);
        Assert.Contains("This is the parent's explicit choice", coverPrompt);

        // And the plan was never touched: the sentence is written into the string handed to the
        // cover prompt and nowhere else, so nothing about the child's appearance reaches the
        // planner or the stored story — which is what the composite boundary exists to prevent.
        Assert.DoesNotContain("green", story.LastStory!.CharacterLock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eyes", story.LastStory.CharacterLock, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A run with no stated eye colour is composed exactly as it was before.</summary>
    [Fact]
    public void An_absent_eye_colour_leaves_the_character_lock_untouched()
    {
        Assert.Equal("A child.", IllustrationPrompt.WithParentEyeColour("A child.", null));
        Assert.Equal("A child.", IllustrationPrompt.WithParentEyeColour("A child.", "   "));
        Assert.Equal(string.Empty, IllustrationPrompt.WithParentEyeColour(null, null));

        // And a composite plan's empty lock becomes the sentence alone rather than a stray space.
        Assert.StartsWith(
            "The child's eyes are green.", IllustrationPrompt.WithParentEyeColour(string.Empty, "green"));
    }

    // ---------------------------------------------------------------------------------------
    // CHILD_AGE: collected, not enforced
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A page objected to only for the child's age is a page with nothing wrong with it.
    ///
    /// Pack 7fc8faf4 died on this: spread 1 came back `FAIL (regenerate_base): CHILD_AGE`, bought
    /// its one regeneration, came back with the same verdict, and the book stopped. The owner's
    /// ruling is that the photograph is the identity reference and may be a year old — the entered
    /// age is what the book is for — so the observation is kept and the gate is gone.
    /// </summary>
    [Fact]
    public void An_age_objection_alone_reads_as_a_pass_with_an_advisory()
    {
        var parsed = CompositeMinimalQa.Parse(
            """
            {"status":"FAIL","failed_checks":["CHILD_AGE"],"recommended_action":"regenerate_base",
             "notes":["the child looks about seven"]}
            """);

        Assert.True(parsed.IsValid, parsed.Summary);

        var verdict = parsed.Verdict!;

        // The verdict the retry ladder reads is a pass, and carries no failed check at all.
        Assert.True(verdict.Passed);
        Assert.Equal(CompositeQaVerdict.ActionPass, verdict.RecommendedAction);
        Assert.Empty(verdict.FailedChecks);
        Assert.DoesNotContain("CHILD_AGE", verdict.ToString());

        // And the observation survives, off to one side where nothing branches on it.
        Assert.NotNull(verdict.AgeNote);
        Assert.Contains("CHILD_AGE", verdict.AgeNote);
    }

    /// <summary>
    /// A well-formed JSON answer of the wrong shape is refused, never thrown out of.
    ///
    /// <c>failed_checks: [123]</c> is valid JSON and not a verdict. Reading it as strings threw
    /// <see cref="InvalidOperationException"/> straight out of the parser and failed a paid book on
    /// the spot — where the schema would merely have refused the answer and spent the parse retry
    /// the contract provides for exactly this. Nothing about a malformed answer should cost more
    /// than a re-ask.
    /// </summary>
    [Theory]
    [InlineData("""{"status":"FAIL","failed_checks":[123],"recommended_action":"human_review","notes":[]}""")]
    [InlineData("""{"status":"FAIL","failed_checks":["CHILD_IDENTITY",123],"recommended_action":"human_review","notes":[]}""")]
    [InlineData("""{"status":"FAIL","failed_checks":["CHILD_AGE",123],"recommended_action":"human_review","notes":[]}""")]
    [InlineData("""{"status":"PASS","failed_checks":[null],"recommended_action":"pass","notes":[]}""")]
    [InlineData("""{"status":"PASS","failed_checks":[{"check":"CHILD_AGE"}],"recommended_action":"pass","notes":[]}""")]
    [InlineData("""{"status":"PASS","failed_checks":"CHILD_IDENTITY","recommended_action":"pass","notes":[]}""")]
    [InlineData("""{"status":"PASS","failed_checks":[],"recommended_action":"pass","notes":[7]}""")]
    [InlineData("""{"status":7,"failed_checks":[],"recommended_action":"pass","notes":[]}""")]
    public void A_malformed_answer_is_an_invalid_parse_rather_than_an_exception(string answer)
    {
        var parsed = CompositeMinimalQa.Parse(answer);

        Assert.False(parsed.IsValid, "a malformed answer was read as a verdict.");
        Assert.Null(parsed.Verdict);
        Assert.NotEmpty(parsed.Problems);
    }

    /// <summary>
    /// And the malformed shape that also names the advisory is not quietly promoted to a pass:
    /// the age demotion runs before validation, so a rewrite there could have turned an unreadable
    /// answer into a PASS with an empty check list.
    /// </summary>
    [Fact]
    public void A_malformed_answer_naming_the_age_is_still_refused()
    {
        var parsed = CompositeMinimalQa.Parse(
            """
            {"status":"FAIL","failed_checks":["CHILD_AGE",123],
             "recommended_action":"regenerate_base","notes":[]}
            """);

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Verdict);
    }

    /// <summary>The reviewer's own words are kept when it wrote any.</summary>
    [Fact]
    public void The_reviewers_own_age_note_is_the_one_recorded()
    {
        var parsed = CompositeMinimalQa.Parse(
            """
            {"status":"PASS","failed_checks":[],"recommended_action":"pass","notes":[],
             "age_note":"reads a couple of years older than five"}
            """);

        Assert.True(parsed.IsValid, parsed.Summary);
        Assert.True(parsed.Verdict!.Passed);
        Assert.Equal("reads a couple of years older than five", parsed.Verdict.AgeNote);
    }

    /// <summary>
    /// A page that fails for something real still fails: only the age comes off the list. The
    /// identity check is untouched and stays fully blocking — likeness and the eight locked
    /// attributes are still the contract.
    /// </summary>
    [Fact]
    public void An_age_objection_beside_a_real_failure_removes_only_the_age()
    {
        var parsed = CompositeMinimalQa.Parse(
            """
            {"status":"FAIL","failed_checks":["CHILD_AGE","CHILD_IDENTITY"],
             "recommended_action":"regenerate_base","notes":["different child"]}
            """);

        Assert.True(parsed.IsValid, parsed.Summary);

        var verdict = parsed.Verdict!;

        Assert.False(verdict.Passed);
        Assert.Equal(["CHILD_IDENTITY"], verdict.FailedChecks);
        Assert.Equal(CompositeQaVerdict.ActionRegenerateBase, verdict.RecommendedAction);
        Assert.NotNull(verdict.AgeNote);
    }

    /// <summary>
    /// And the whole book survives a reviewer that says it on every page: eight pictures, eight
    /// reviews, no regeneration bought, and the advisories on the review record.
    ///
    /// This is the pack that died, replayed.
    /// </summary>
    [Fact]
    public async Task A_book_the_reviewer_calls_the_wrong_age_on_every_page_still_ships()
    {
        var images = new StubImageService();

        for (var page = 0; page < BookFormat.SpreadCount; page++)
        {
            images.Verdicts.Enqueue(
                """
                {"status":"FAIL","failed_checks":["CHILD_AGE"],
                 "recommended_action":"regenerate_base","notes":["looks older than the age given"]}
                """);
        }

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        // One picture per page and one review per page: not a single regeneration was bought.
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.Equal(BookFormat.SpreadCount, images.ReviewCalls);
        Assert.All(result.Spreads, spread => Assert.Equal(1, spread.BaseAttempts));

        // Every page carries the advisory, and the book's review record has them all with the age
        // the parent actually entered beside each.
        Assert.All(result.Spreads, spread => Assert.NotNull(spread.AgeNote));
        Assert.Equal(BookFormat.SpreadCount, result.Review.AgeAdvisories.Count);
        Assert.All(result.Review.AgeAdvisories, advisory => Assert.Equal(1, advisory.EnteredAge));
        Assert.True(result.Review.NeedsHumanReading);
    }

    /// <summary>
    /// The contract no longer lists CHILD_AGE among the blocking categories, and the schema no
    /// longer accepts it in <c>failed_checks</c> — which is exactly why the parser takes it out
    /// before the schema is consulted rather than after.
    /// </summary>
    [Fact]
    public void The_blocking_categories_no_longer_include_the_age()
    {
        Assert.DoesNotContain("CHILD_AGE - The child appears", CompositeMinimalQa.SystemInstruction);
        Assert.Contains("age_note is optional, advisory, and never a failure", CompositeMinimalQa.SystemInstruction);
        Assert.Contains("The photograph says WHO the child is", CompositeMinimalQa.SystemInstruction);

        var schema = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Assets", "BekiComposite", "contracts",
            CompositeMinimalQa.SchemaFileName));

        Assert.DoesNotContain("\"CHILD_AGE\"", schema);
        Assert.Contains("\"age_note\"", schema);

        // The identity gate is untouched.
        Assert.Contains("1. CHILD_IDENTITY", CompositeMinimalQa.SystemInstruction);
        Assert.Contains("\"CHILD_IDENTITY\"", schema);
    }

    // ---------------------------------------------------------------------------------------
    // The entered age is the age the book is drawn to
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Both places the age is stated now say which source wins, because the model was being asked
    /// to reconcile a photograph with a number and given no rule for doing it.
    /// </summary>
    [Fact]
    public void The_prompt_says_the_photograph_is_who_and_the_entered_age_is_how_old()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;
        var prompt = SpreadPrompt(scenario, page: 1);

        Assert.Contains("this photograph says WHO the child is, and nothing else", prompt);
        Assert.Contains(
            "Render the child's proportions and face at 1 years old, which is the age this book is "
            + "for, even if the photograph appears older or younger", prompt);

        Assert.Contains("The child is 1 years old in this book.", prompt);
        Assert.Contains(
            "the photograph says who the child is, not how old they are here", prompt);

        // And the anchored spreads carry it too — the anchor is a drawing, not a birth certificate.
        Assert.Contains(
            "The child is 1 years old in this book.",
            SpreadPrompt(scenario, page: 2, anchorAttached: true));
    }

    /// <summary>
    /// The lock tells the illustrator which source wins for which attribute, and the two halves do
    /// not contradict each other.
    ///
    /// They did. The lock ended "where this list and that photograph disagree, follow the
    /// photograph" — every attribute, one authority — while the reviewer was told to fail any page
    /// whose eyes did not read as the entered colour. A parent entering green for a child
    /// photographed brown-eyed therefore had the illustrator instructed to draw brown and the
    /// reviewer instructed to refuse brown: refused, redrawn from the same instruction, refused
    /// again, book stopped. Neither model was wrong.
    /// </summary>
    [Fact]
    public void The_photograph_settles_who_the_child_is_and_the_entered_values_settle_the_rest()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;
        var prompt = SpreadPrompt(scenario, page: 1);

        Assert.Contains(
            "Image 1 is the identity reference photograph and settles who this child is — the face "
            + "and the likeness — wherever it and this list disagree about that; the eye colour and "
            + "the age above are the parent's own entered values and win over the photograph "
            + "wherever they differ.", prompt);

        // The blanket deference is gone: it is the sentence that made the loop.
        Assert.DoesNotContain("where this list and that photograph disagree, follow the photograph", prompt);

        // And the illustrator's rule now agrees with the reviewer's. The prompt is told to draw the
        // entered eye colour; the reviewer is told to fail anything else. One instruction, two
        // models — which is the only arrangement a page can actually satisfy.
        var ask = CompositeMinimalQa.Prompt(
            scenario.Spreads![0].ChildWorldScene!, scenario.Spreads[0].BekiAction!,
            scenario.VisualLock!.ChildOutfit!, [], "LEFT", false, IdentityFixture);

        Assert.Contains($"Eye colour: {IdentityFixture.EyeColor}", prompt);
        Assert.Contains($"The child's eyes must read as {IdentityFixture.EyeColor}", ask);

        // The same on the anchored spreads, where the photograph is Image 2.
        Assert.Contains(
            "Image 2 is the identity reference photograph and settles who this child is",
            SpreadPrompt(scenario, page: 2, anchorAttached: true));
    }

    /// <summary>
    /// The parent's entered eye colour reaches the prompt as the colour to draw — the case the
    /// contradiction was actually about.
    /// </summary>
    [Fact]
    public void A_parent_eye_colour_that_differs_from_the_photo_is_the_one_drawn()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        // The model read brown from the photograph; the parent entered green.
        var overridden = CompositeChildIdentity.WithParentEyeColor(IdentityFixture, "green");

        var prompt = CompositeIllustrationPrompt.ForSpread(new CompositeSpreadPromptInput
        {
            Page = 1,
            ChildAge = 1,
            Theme = CompositeThemeReferences.For("dinosaurs"),
            ChildWorldScene = scenario.Spreads![0].ChildWorldScene!,
            ChildOutfit = scenario.VisualLock!.ChildOutfit!,
            IdentitySpec = overridden,
        });

        Assert.Contains("Eye colour: green", prompt);
        Assert.Contains("The child's eyes are green on every page.", prompt);
        Assert.Contains("win over the photograph wherever they differ", prompt);
        Assert.DoesNotContain("Eye colour: brown", prompt);
    }

    /// <summary>
    /// The reserved third is described as scene rather than as a panel.
    ///
    /// The refused image rendered it as a flat blank field with a hard vertical boundary — which
    /// the constraint list forbids as a "blank rectangle" while the resolver was inviting it:
    /// "naturally calm, light background" reads as an instruction to paint a light background
    /// there. Same geometry, same percentages, different request.
    /// </summary>
    [Fact]
    public void The_reserved_text_third_is_asked_for_as_continued_scene()
    {
        foreach (var side in (string[])["LEFT", "RIGHT"])
        {
            var block = CompositeIllustrationPrompt.CompositionBlockFor(side);

            Assert.Contains("continue the same scene through it as calm open environment", block);
            Assert.Contains("no hard vertical boundary where it begins", block);
            Assert.Contains("no flat field of colour", block);
            Assert.DoesNotContain("naturally calm, light background", block);

            // v1.5: the third is calm at full colour. "Gently lightening toward the outer edge"
            // was the sentence the shipped book obeyed — a milky veil over the whole half — so
            // lightening is now forbidden by name rather than requested.
            Assert.DoesNotContain("gently lightening", block);
            Assert.Contains("do not lighten it", block);
            Assert.Contains("same colour depth, saturation, contrast, exposure, and finish", block);
        }

        // The geometry did not move: it is the same third and the same two anchors.
        Assert.Contains("59.4% of the canvas width", CompositeIllustrationPrompt.CompositionBlockFor("LEFT"));
        Assert.Contains("40.6% of the canvas width", CompositeIllustrationPrompt.CompositionBlockFor("RIGHT"));
        Assert.Contains("45.8% of the canvas height", CompositeIllustrationPrompt.CompositionBlockFor("LEFT"));
    }
}
