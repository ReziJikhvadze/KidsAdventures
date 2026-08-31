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
/// Pose variety and its fallback, the Georgian check-list, and the shot instruction the reviewer
/// may only advise on.
///
/// One of the classes CompositePipelineTestBase serves; see it for the fixtures these use.
/// </summary>
public class CompositePipelinePoseTests : CompositePipelineTestBase
{
    // ---------------------------------------------------------------------------------------
    // R13 — pose variety: the vocabulary steering, the fallback count, and the one retry it spends
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The planner is sent the contract's instruction with the verb families appended, on both
    /// attempts — vocabulary steering, not a rewritten ask.
    /// </summary>
    [Fact]
    public async Task The_planner_is_told_which_verbs_the_pose_table_can_read()
    {
        var storyClient = new ScriptedStoryModelClient(ScenarioFixture());

        await Pipeline(storyClient, new StubImageService()).RunAsync(Request(), CancellationToken.None);

        var system = storyClient.SystemPrompts[0];

        Assert.Contains("You are the Visual Scenario Planner", system);
        Assert.Contains("BEKI ACTION VOCABULARY", system);
        Assert.Contains("- celebrate: celebrates, claps, cheers", system);
        Assert.Contains("- reassure: reassures, comforts, stands beside, nods", system);

        // Still a beki_action of the same shape: no pose is named and no pose id is asked for.
        Assert.DoesNotContain("pose_0", system);
    }

    /// <summary>
    /// A scenario that would compose most of its book from the neutral hover is valid, and is still
    /// re-asked once — the R13c check, spending the retry the scenario stage already had.
    ///
    /// The evidence this is modelled on is book <c>c4fc5fe7</c>: eight ordinary Beki sentences, all
    /// of them schema-valid, six of them selecting the fallback. Nothing in the pipeline objected,
    /// and the book printed with the same drawing on six spreads.
    /// </summary>
    [Fact]
    public async Task A_scenario_that_would_compose_six_neutral_hovers_is_re_asked_once()
    {
        var storyClient = new ScriptedStoryModelClient(
            WithBekiActions(
                "Beki hovers quietly nearby.", "Beki hovers quietly nearby.",
                "Beki hovers quietly nearby.", "Beki hovers quietly nearby.",
                "Beki hovers quietly nearby.", "Beki hovers quietly nearby.",
                "Beki points toward the path.", "Beki claps for the child."),
            ScenarioFixture());

        var result = await Pipeline(storyClient, new StubImageService())
            .RunAsync(Request(), CancellationToken.None);

        // Exactly two: the ask and its one retry. Never a third.
        Assert.Equal(2, storyClient.Calls);

        // The retry is the original ask with the reason appended, in the same idiom every other
        // corrective retry on this path uses.
        Assert.StartsWith(storyClient.UserPrompts[0], storyClient.UserPrompts[1]);
        Assert.Contains(VisualScenarioProblemCodes.PoseVocabularyMiss, storyClient.UserPrompts[1]);
        Assert.Contains("Beki hovers quietly nearby.", storyClient.UserPrompts[1]);
        Assert.Contains("nine verb families", storyClient.UserPrompts[1]);

        // The second answer is the fixture, which reads cleanly — so the book that ships has none.
        Assert.Equal(0, result.Review.PoseSelectionFallbacks);
        Assert.True(result.Review.PoseVocabularyRetrySpent);
        Assert.False(result.Review.PoseFallbackBudgetExceeded);
    }

    /// <summary>
    /// Two fallbacks is inside the budget and buys nothing: the scenario stands and the book is
    /// drawn from one call.
    ///
    /// The budget exists because a fallback is an approved pose. Re-asking for a sentence the table
    /// genuinely has no verb for would spend a retry on a book that is fine.
    /// </summary>
    [Fact]
    public async Task Two_fallbacks_are_inside_the_budget_and_cost_no_retry()
    {
        var storyClient = new ScriptedStoryModelClient(
            WithBekiActions(
                "Beki hovers quietly nearby.", "Beki hovers quietly nearby.",
                "Beki points toward the path.", "Beki claps for the child.",
                "Beki listens attentively.", "Beki stands beside the child.",
                "Beki welcomes the child.", "Beki walks beside the child."));

        var result = await Pipeline(storyClient, new StubImageService())
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(1, storyClient.Calls);
        Assert.Equal(2, result.Review.PoseSelectionFallbacks);
        Assert.Equal([1, 2], result.Review.PoseFallbackPages);
        Assert.False(result.Review.PoseVocabularyRetrySpent);
        Assert.False(result.Review.PoseFallbackBudgetExceeded);
    }

    /// <summary>
    /// A second repetitive answer is drawn anyway, and recorded.
    ///
    /// This is the rule that keeps the check from becoming a way to lose paid books: the retry is
    /// spent once, and after that a repetitive Beki is a quality signal rather than a failure. The
    /// count reaches the book's own record, and the pages say which spreads they were.
    /// </summary>
    [Fact]
    public async Task A_scenario_still_repetitive_after_its_retry_is_drawn_and_recorded()
    {
        var repetitive = WithBekiActions(
            "Beki hovers quietly nearby.", "Beki hovers quietly nearby.",
            "Beki hovers quietly nearby.", "Beki hovers quietly nearby.",
            "Beki hovers quietly nearby.", "Beki hovers quietly nearby.",
            "Beki points toward the path.", "Beki claps for the child.");

        var storyClient = new ScriptedStoryModelClient(repetitive, repetitive);
        var images = new StubImageService();

        var result = await Pipeline(storyClient, images).RunAsync(Request(), CancellationToken.None);

        // Two scenario calls, no third — and the book was drawn.
        Assert.Equal(2, storyClient.Calls);
        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        Assert.Equal(6, result.Review.PoseSelectionFallbacks);
        Assert.Equal([1, 2, 3, 4, 5, 6], result.Review.PoseFallbackPages);
        Assert.True(result.Review.PoseVocabularyRetrySpent);
        Assert.True(result.Review.PoseFallbackBudgetExceeded);
        Assert.True(result.Review.NeedsHumanReading);

        // The pages themselves agree with the audit, which is what makes the count a fact about the
        // book rather than about the plan.
        Assert.Equal(6, result.Spreads.Count(spread => spread.PoseFallback));
        Assert.Equal(6, result.Warnings.Count(warning => warning.Contains("no pose keyword matched")));

        // And it is on the stored record, not only in the log.
        Assert.Contains("\"pose_selection_fallback\": 6", result.Artifacts.ReviewJson);
        Assert.Contains("\"pose_keyword_revision\": \"v1.1\"", result.Artifacts.ReviewJson);
    }

    /// <summary>
    /// A resumed run audits the scenario it adopted, and does not re-ask for it.
    ///
    /// The count describes the book that ships, so an adopted scenario is still read; but replanning
    /// it is the one thing the whole resume path exists to prevent — the pages are already drawn
    /// against it.
    /// </summary>
    [Fact]
    public async Task An_adopted_scenario_is_audited_but_never_replanned()
    {
        var repetitive = WithBekiActions(
            "Beki hovers quietly nearby.", "Beki hovers quietly nearby.",
            "Beki hovers quietly nearby.", "Beki hovers quietly nearby.",
            "Beki hovers quietly nearby.", "Beki hovers quietly nearby.",
            "Beki points toward the path.", "Beki claps for the child.");

        var storyClient = new ScriptedStoryModelClient();

        var result = await Pipeline(storyClient, new StubImageService()).RunAsync(
            Request(resume: new CompositeResumeState(repetitive, new Dictionary<int, byte[]>(),
                new Dictionary<int, byte[]>())),
            CancellationToken.None);

        // Not one scenario call: the stored plan is the book's specification.
        Assert.Equal(0, storyClient.Calls);

        Assert.Equal(6, result.Review.PoseSelectionFallbacks);
        Assert.False(result.Review.PoseVocabularyRetrySpent);
    }

    // ---------------------------------------------------------------------------------------
    // R12c — the Georgian check-list flags a book and never edits it
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A book carrying the two misspellings that actually shipped is flagged, delivered, and
    /// unchanged.
    ///
    /// All three matter. Flagged, because nobody reading these logs can proof-read Georgian.
    /// Delivered, because a misspelling is not a reason to refuse a paid order. Unchanged, because
    /// the substring rule that found it does not understand the sentence, and the pass that could
    /// correct it is the polish call upstream.
    /// </summary>
    [Fact]
    public async Task A_book_with_known_bad_georgian_is_flagged_delivered_and_left_alone()
    {
        var flagged = Plan() with
        {
            Concept = Plan().Concept with { Title = "ფუნღუროს ზღაპარი" },
            Spreads = Plan().Spreads
                .Select((spread, index) => index == 3
                    ? spread with { Text = "თემო-ს გაუხარდა და ბილიკი გამოჩნდა." }
                    : spread)
                .ToList(),
        };

        var request = Request() with { ExistingPlan = flagged };

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), new StubImageService())
            .RunAsync(request, CancellationToken.None);

        // The book is finished.
        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        // Both faults are named, with the page to open.
        Assert.Equal(2, result.Review.GeorgianFlags.Count);
        Assert.Contains(result.Review.GeorgianFlags,
            flag => flag.RuleId == "funguro_misspelling" && flag.Location == "title");
        Assert.Contains(result.Review.GeorgianFlags,
            flag => flag.RuleId == "hyphenated_name_suffix" && flag.Location == "spread 4");

        Assert.True(result.Review.NeedsHumanReading);
        Assert.Equal(2, result.Warnings.Count(w => w.Contains("Georgian check-list")));
        Assert.Contains("\"georgian_checklist_version\": \"georgian-text-checklist-v1.1\"",
            result.Artifacts.ReviewJson);

        // And not one word was rewritten: the plan that comes out is the plan that went in.
        Assert.Equal("ფუნღუროს ზღაპარი", result.Plan.Concept.Title);
        Assert.Equal("თემო-ს გაუხარდა და ბილიკი გამოჩნდა.", result.Plan.Spreads[3].Text);
    }

    /// <summary>A book with nothing to flag says so, and carries no noise into the record.</summary>
    [Fact]
    public async Task A_clean_book_is_flagged_for_nothing()
    {
        var result = await Pipeline(
                new ScriptedStoryModelClient(ScenarioFixture()), new StubImageService())
            .RunAsync(Request(), CancellationToken.None);

        Assert.Empty(result.Review.GeorgianFlags);
        Assert.Empty(result.Review.ShotAdvisories);
        Assert.False(result.Review.NeedsHumanReading);
        Assert.Equal(0, result.Review.PoseSelectionFallbacks);
    }

    // ---------------------------------------------------------------------------------------
    // R14 — the shot instruction leads, and the reviewer's note about it is advisory
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Every page's image prompt opens its composition block with that page's own shot, and the
    /// reviewer is told the same sentence.
    /// </summary>
    [Fact]
    public async Task Every_page_leads_its_composition_block_with_its_own_shot()
    {
        var images = new StubImageService();

        await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        for (var page = 1; page <= BookFormat.SpreadCount; page++)
        {
            var shot = CompositeSpreadRhythm.ShotFor(page);

            Assert.Contains($"COMPOSITION\n{shot}\n", images.Prompts[page - 1]);
            Assert.Contains($"Shot this page was asked for: {shot}", images.ReviewPrompts[page - 1]);
        }
    }

    /// <summary>
    /// A shot note is recorded and changes nothing: the page passes, no picture is bought again, and
    /// the note reaches the book's record rather than the retry ladder.
    /// </summary>
    [Fact]
    public async Task A_shot_note_is_recorded_and_costs_the_book_nothing()
    {
        var images = new StubImageService();

        images.Verdicts.Enqueue(
            """
            {"status":"PASS","failed_checks":[],"recommended_action":"pass","notes":[],
             "shot_note":"A tight close-up, where a wide establishing view was asked for."}
            """);

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        // One image per spread: an advisory note buys nothing.
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.Equal(BookFormat.SpreadCount, images.ReviewCalls);
        Assert.All(result.Spreads, spread => Assert.Equal(1, spread.BaseAttempts));

        var advisory = Assert.Single(result.Review.ShotAdvisories);
        Assert.Equal(1, advisory.Page);
        Assert.Equal(CompositeSpreadRhythm.ShotFor(1), advisory.ShotInstruction);
        Assert.Contains("close-up", advisory.ReviewerNote);

        Assert.True(result.Review.NeedsHumanReading);
        Assert.Contains("\"shot_advisories\"", result.Artifacts.ReviewJson);

        // The verdict the ladder read never mentioned it: PASS, and nothing else.
        Assert.StartsWith("PASS", result.Spreads[0].Verdict);
        Assert.DoesNotContain("close-up", result.Spreads[0].Verdict);
    }
}
