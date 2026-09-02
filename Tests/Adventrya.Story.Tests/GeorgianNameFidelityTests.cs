using System.Text.Json;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// Whether a book spells the child's name the way the parent typed it.
///
/// **The observed defect, 2026-09-01.** A live composite run for a child called ვეკო came back
/// titled „ველო და მოციმციმე ტყე“ — one Georgian letter, კ written as ლ, in the child's own name,
/// in the title. Nothing looked at it. The title flows canonically to the cover, the pack row and
/// the PDF metadata, so the first thing that family would have seen of a book bought to put their
/// child in it is somebody else's name.
///
/// The tests below are that pair, and then the four things a check like this gets wrong if nobody
/// writes them down: that Georgian declines by suffix and a declined name is the name; that a title
/// naming nobody is a good title; that a name nowhere in the book is a book that was not
/// personalised; and that a check which fires on healthy books is a check operators learn to wave
/// through.
/// </summary>
public class GeorgianNameFidelityTests : CompositePipelineTestBase
{
    // ===========================================================================================
    // The observed defect
    // ===========================================================================================

    [Fact]
    public void The_observed_title_is_refused_and_names_both_words()
    {
        var problems = GeorgianNameFidelity.Inspect(
            Book("ველო და მოციმციმე ტყე", "ვეკო გაემართა ტყისკენ."), "ვეკო");

        var nearMiss = Assert.Single(
            problems, problem => problem.Kind == NameFidelityProblem.NearMiss);

        Assert.Equal("title", nearMiss.Location);
        Assert.Equal(0, nearMiss.Spread);
        Assert.Equal("ველო", nearMiss.Found);
        Assert.Equal("ვეკო", nearMiss.Expected);

        // And the title's own obligation: it reached for the name and missed, so it owes the name.
        Assert.Contains(problems, problem => problem.Kind == NameFidelityProblem.AbsentFromTitle);
    }

    /// <summary>
    /// The sentence the corrective retry is actually sent. It has to carry both words and the
    /// obligation, because a planner told only that "the name is wrong" has nothing to act on.
    /// </summary>
    [Fact]
    public void The_correction_names_the_child_the_word_written_and_the_rule()
    {
        var correction = Assert.Single(
            GeorgianNameFidelity.Problems(
                Book("ველო და მოციმციმე ტყე", "ვეკო გაემართა ტყისკენ."), "ვეკო"),
            text => text.Contains("ველო", StringComparison.Ordinal));

        Assert.Contains("ვეკო", correction, StringComparison.Ordinal);
        Assert.Contains("ველო", correction, StringComparison.Ordinal);
        Assert.Contains("letter for letter", correction, StringComparison.Ordinal);

        // The endings, because the first thing a model does when told to use the exact name is stop
        // declining it — and „ვეკო მიდის“ is a different defect in the same place.
        Assert.Contains("ვეკოს", correction, StringComparison.Ordinal);
    }

    /// <summary>A misspelling on a page is reported with the page, not just as "somewhere".</summary>
    [Fact]
    public void A_misspelling_on_a_spread_carries_its_spread_number()
    {
        var story = Book("მოციმციმე ტყე", "ვეკო გაემართა ტყისკენ.") with
        {
            Spreads =
            [
                Spread(1, "ვეკო გაემართა ტყისკენ."),
                Spread(2, "ველოს გაუხარდა და ბილიკი გამოჩნდა."),
            ]
        };

        var problem = Assert.Single(GeorgianNameFidelity.Inspect(story, "ვეკო"));

        Assert.Equal(NameFidelityProblem.NearMiss, problem.Kind);
        Assert.Equal("spread 2", problem.Location);
        Assert.Equal(2, problem.Spread);
        Assert.Equal("ველოს", problem.Found);
    }

    // ===========================================================================================
    // What a correct book looks like
    // ===========================================================================================

    /// <summary>
    /// Georgian declines by suffix. ვეკოს, ვეკომ and ვეკოსთვის are the name, and a check that
    /// reported them would report every correct book in the catalogue.
    /// </summary>
    [Theory]
    [InlineData("ვეკოს გაუხარდა.")]
    [InlineData("ვეკომ ბილიკი იპოვა.")]
    [InlineData("ბეკიმ ვეკოსთვის კარი გააღო.")]
    [InlineData("ვეკო და ბეკი ერთად წავიდნენ.")]
    [InlineData("„ვეკო!“ — თქვა ბეკიმ.")]
    public void A_declined_name_is_the_name(string text) =>
        Assert.Empty(GeorgianNameFidelity.Inspect(Book("მოციმციმე ტყე", text), "ვეკო"));

    /// <summary>
    /// A title that names nobody is a good title — the prompt asks for wonder, friendship and
    /// light, not for the hero's name — so absence from the title is only a problem when the title
    /// reached for the name and missed.
    /// </summary>
    [Fact]
    public void A_title_that_does_not_name_the_hero_is_accepted() =>
        Assert.Empty(GeorgianNameFidelity.Inspect(
            Book("მოციმციმე ტყე", "ვეკო გაემართა ტყისკენ."), "ვეკო"));

    /// <summary>
    /// A word two edits away is a different word. ვედრო is not a misspelling of ვეკო, and reading
    /// it as one would be a bug in the check rather than a defect in the book.
    /// </summary>
    [Fact]
    public void A_word_two_edits_away_is_a_different_word() =>
        Assert.Empty(GeorgianNameFidelity.Inspect(
            Book("მოციმციმე ტყე", "ვეკო ვედროსთან მივიდა."), "ვეკო"));

    // ===========================================================================================
    // Absence
    // ===========================================================================================

    [Fact]
    public void A_book_that_never_names_the_child_is_refused()
    {
        var problem = Assert.Single(GeorgianNameFidelity.Inspect(
            Book("მოციმციმე ტყე", "ბავშვი გაემართა ტყისკენ."), "ვეკო"));

        Assert.Equal(NameFidelityProblem.AbsentFromBook, problem.Kind);
        Assert.Contains("never named", problem.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The English half is read too, where a book has one. The composite format has none; an A5
    /// book prints it, and a name is a name in either column.
    /// </summary>
    [Fact]
    public void The_english_secondary_text_counts_as_naming_the_child()
    {
        var story = Book("მოციმციმე ტყე", "ბავშვი გაემართა ტყისკენ.") with
        {
            Spreads = [Spread(1, "ბავშვი გაემართა ტყისკენ.") with { TextEn = "ვეკო went to the wood." }]
        };

        Assert.Empty(GeorgianNameFidelity.Inspect(story, "ვეკო"));
    }

    // ===========================================================================================
    // The guards — the reason this check can be left switched on
    // ===========================================================================================

    /// <summary>
    /// Two-letter names are not checked. Distance-1 prefix matching on two letters matches an
    /// enormous share of ordinary Georgian, and a check that fires on healthy books is one nobody
    /// reads by the third time.
    /// </summary>
    [Fact]
    public void A_name_shorter_than_three_letters_is_not_checked() =>
        Assert.Empty(GeorgianNameFidelity.Inspect(
            Book("ია და ია", "იო, ია, ეა და აა."), "ია"));

    /// <summary>
    /// **Review finding 2.** A three-letter name gets the exact-name and absence rules and nothing
    /// else, and this is the book that proved it has to: „Ana“ beside the English word "and" is one
    /// substitution with the same first letter, so every guard the check had was satisfied and a
    /// perfectly healthy book collected a near miss. name_fidelity is a blocker by default, so that
    /// book was replanned, replanned again and then refused.
    /// </summary>
    [Fact]
    public void A_three_letter_name_does_not_make_a_near_miss_of_an_ordinary_word() =>
        Assert.Empty(GeorgianNameFidelity.Inspect(
            Book("Ana and Beki", "Ana and Beki went to the wood."), "Ana"));

    /// <summary>
    /// The same rule in the language the books are actually written in: „აბა“ is a Georgian word,
    /// one letter from „ანა“, and it is not a misspelling of anybody's name.
    /// </summary>
    [Fact]
    public void A_three_letter_georgian_name_does_not_make_a_near_miss_of_an_ordinary_word() =>
        Assert.Empty(GeorgianNameFidelity.Inspect(
            Book("მოციმციმე ტყე", "ანა და ბეკი წავიდნენ. აბა, ბილიკი გამოჩნდა."), "ანა"));

    /// <summary>
    /// And the boundary is stated where it is, not one letter lower: ვეკო is four characters, which
    /// is exactly why the observed defect is still caught. The check is not weaker about the thing
    /// it was built for — it is only silent one letter below it.
    /// </summary>
    [Fact]
    public void The_four_letter_boundary_is_where_the_observed_defect_lives()
    {
        Assert.Equal(4, GeorgianNameFidelity.ShortestNearMissName);

        Assert.Contains(
            GeorgianNameFidelity.Inspect(
                Book("ველო და მოციმციმე ტყე", "ვეკო გაემართა ტყისკენ."), "ვეკო"),
            problem => problem.Kind == NameFidelityProblem.NearMiss && problem.Found == "ველო");
    }

    /// <summary>
    /// A four-letter Latin name keeps the rule. The boundary is about how much signal three letters
    /// carry, not about which alphabet the book is set in.
    /// </summary>
    [Fact]
    public void A_latin_name_of_four_letters_still_catches_its_near_miss()
    {
        var problem = Assert.Single(
            GeorgianNameFidelity.Inspect(Book("A day out", "Nina and Nino played."), "Nina"),
            candidate => candidate.Kind == NameFidelityProblem.NearMiss);

        Assert.Equal("nino", problem.Found);
        Assert.Equal("Nina", problem.Expected);
    }

    /// <summary>
    /// A three-letter name is still expected to appear. Absence is a reading that needs no
    /// neighbourhood, so it survives the boundary above — a book that never names ანა is a book
    /// nobody personalised, whatever its name is long enough for.
    /// </summary>
    [Fact]
    public void A_three_letter_name_is_still_required_to_appear_somewhere()
    {
        var problem = Assert.Single(GeorgianNameFidelity.Inspect(
            Book("მოციმციმე ტყე", "ბავშვი გაემართა ტყისკენ."), "ანა"));

        Assert.Equal(NameFidelityProblem.AbsentFromBook, problem.Kind);
    }

    /// <summary>
    /// A distance-1 word whose first letter differs is left alone, which is the trade the guard
    /// makes: ანა against ანგელოზი is the flood it prevents, and ბეკო for ვეკო is what it costs.
    /// </summary>
    [Fact]
    public void A_near_word_with_a_different_first_letter_is_left_alone() =>
        Assert.Empty(GeorgianNameFidelity.Inspect(
            Book("მოციმციმე ტყე", "ვეკო და ბეკო ერთად წავიდნენ."), "ვეკო"));

    /// <summary>
    /// The companion is never read as a misspelling of the child. „ბეკი“ is on every page by
    /// contract and is written there by BekiIdentityRules, so a child called ბეკა would otherwise
    /// collect eight problems a book against a word this system itself put in.
    /// </summary>
    [Fact]
    public void Bekis_own_name_is_never_a_misspelling_of_the_child() =>
        Assert.Empty(GeorgianNameFidelity.Inspect(
            Book("ბეკას დღე", "ბეკა და ბეკი წავიდნენ. ბეკიმ ბილიკი აჩვენა ბეკას."), "ბეკა"));

    /// <summary>
    /// A cast member far from the child's name is that character's name. The fixture book's ბაფუ
    /// beside a ნინა is the ordinary case, and reporting it would make the check useless.
    /// </summary>
    [Fact]
    public void A_cast_members_own_name_is_left_alone()
    {
        var story = Book("ბაფუს ბილიკი", "ნინამ ბაფუს გზა გაუკეთა.") with
        {
            Cast = [new StoryCastMember { Id = "char_01", Name = "ბაფუ", VisualDescription = "A small dinosaur." }]
        };

        Assert.Empty(GeorgianNameFidelity.Inspect(story, "ნინა"));
    }

    /// <summary>
    /// But a cast member one letter from the hero is not exempted by being in the cast list. That
    /// book is either the same defect wearing a cast entry or a story no child can follow, and both
    /// are worth the second attempt.
    /// </summary>
    [Fact]
    public void A_cast_member_one_letter_from_the_hero_is_still_reported()
    {
        var story = Book("მოციმციმე ტყე", "ვეკო და ველო ერთად წავიდნენ.") with
        {
            Cast = [new StoryCastMember { Id = "char_01", Name = "ველო", VisualDescription = "A fox." }]
        };

        var problem = Assert.Single(GeorgianNameFidelity.Inspect(story, "ვეკო"));

        Assert.Equal("ველო", problem.Found);
    }

    /// <summary>A truncated name is a misspelled name: ვეკ is not ვეკო.</summary>
    [Fact]
    public void A_truncated_name_is_reported() =>
        Assert.Contains(
            GeorgianNameFidelity.Inspect(Book("მოციმციმე ტყე", "ვეკო და ვეკ."), "ვეკო"),
            problem => problem.Found == "ვეკ");

    [Fact]
    public void A_missing_story_or_a_missing_name_is_no_ones_problem()
    {
        Assert.Empty(GeorgianNameFidelity.Inspect(null, "ვეკო"));
        Assert.Empty(GeorgianNameFidelity.Inspect(Book("მოციმციმე ტყე", "ბავშვი მიდის."), null));
        Assert.Empty(GeorgianNameFidelity.Inspect(Book("მოციმციმე ტყე", "ბავშვი მიდის."), "   "));
    }

    // ===========================================================================================
    // The policy
    // ===========================================================================================

    /// <summary>
    /// A flag by default since 2026-09-02: the name is restored from the input before this check
    /// runs, so what it can still catch is a story that never names the child — worth a note for
    /// the operator, never a dead end for the parent. It stays out of
    /// <see cref="BekiReleaseChecks.Pipeline"/>, which is the list of waivers and toggles; this is
    /// an identity check with its own row.
    /// </summary>
    [Fact]
    public void The_shipped_policy_flags_a_misspelled_name()
    {
        Assert.Equal(
            BekiReleaseSeverity.Flag,
            BekiReleasePolicySnapshot.Defaults.SeverityOf(BekiReleaseChecks.NameFidelity));

        Assert.DoesNotContain(BekiReleaseChecks.NameFidelity, BekiReleaseChecks.Pipeline);
        Assert.Contains(BekiReleaseChecks.NameFidelity, BekiReleaseChecks.All);

        // An absent row — a database that has not been migrated, a row somebody deleted — answers
        // the same way.
        Assert.Equal(
            BekiReleaseSeverity.Flag,
            new BekiReleasePolicySnapshot([]).SeverityOf(BekiReleaseChecks.NameFidelity));
    }

    /// <summary>And it is in the table admin actually renders, or it cannot be flipped.</summary>
    [Fact]
    public void The_check_appears_in_the_admin_settings_table() =>
        Assert.Contains(
            BekiReleasePolicySnapshot.Defaults.Settings,
            setting => setting.CheckId == BekiReleaseChecks.NameFidelity
                       && setting.DeliverableClass == BekiReleaseSeverity.AllClasses
                       && setting.Severity == BekiReleaseSeverity.Flag);

    // ===========================================================================================
    // The pipeline: its own planning call
    // ===========================================================================================

    /// <summary>
    /// The fulfilment job's own planning call: a misspelled name is repaired from the input, and
    /// the story is drawn with the name as typed — no second planning call is bought for it.
    /// </summary>
    [Fact]
    public async Task The_pipelines_own_story_call_repairs_a_misspelled_name_without_a_retry()
    {
        var planner = new ScriptedCompositeStoryService(MisspeltPlan(), NamedPlan());

        var result = await Pipeline(
                new ScriptedStoryModelClient(ScenarioFixture()), new StubImageService(),
                masterStory: planner)
            .RunAsync(WrittenRequest(VekoContext()), CancellationToken.None);

        Assert.Equal(1, planner.Calls);
        Assert.Empty(planner.Problems[0]);
        Assert.Equal("ვეკო და მოციმციმე ტყე", result.Plan.Concept.Title);
        Assert.Empty(GeorgianNameFidelity.Inspect(result.Plan, "ვეკო"));
    }

    /// <summary>
    /// A story that never names the child cannot be repaired by restoring letters. It is asked
    /// for again once, with the rule stated, and when the second attempt is no better the book
    /// still ships — as a flag for the operator, which is the shipped default — rather than
    /// failing the family's order over prose.
    /// </summary>
    [Fact]
    public async Task A_story_that_never_names_the_child_ships_with_a_flag_by_default()
    {
        var planner = new ScriptedCompositeStoryService(Plan());
        var images = new StubImageService();
        var waivers = new List<CompositePolicyWaiver>();

        // The shipped policy, stated: the fulfilment job always hands the pipeline a snapshot, and
        // a context without one is judged strictly.
        var context = VekoContext() with
        {
            ReleasePolicy = BekiReleasePolicySnapshot.Defaults,
            OnPolicyWaiver = waiver =>
            {
                waivers.Add(waiver);
                return Task.CompletedTask;
            },
        };

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images, masterStory: planner)
            .RunAsync(WrittenRequest(context), CancellationToken.None);

        // Two planning calls — the correction was tried — and then the book.
        Assert.Equal(2, planner.Calls);
        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        var waiver = Assert.Single(waivers);
        Assert.Equal(BekiReleaseChecks.NameFidelity, waiver.CheckId);
        Assert.Contains("never names", waiver.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// And with the check turned up to blocker by an operator, the same story stops the run with a
    /// coded failure before anything is drawn.
    /// </summary>
    [Fact]
    public async Task A_story_that_never_names_the_child_fails_the_run_when_the_check_is_a_blocker()
    {
        var planner = new ScriptedCompositeStoryService(Plan());
        var images = new StubImageService();

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images, masterStory: planner)
                .RunAsync(WrittenRequest(BlockingNameContext()), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.StoryFailed, failure.FailureCode);
        Assert.Contains("ვეკო", failure.Message, StringComparison.Ordinal);
        Assert.Equal(2, planner.Calls);
        Assert.Equal(0, images.ImageCalls);
    }

    // ===========================================================================================
    // The pipeline: the adopted preview story, which is the seam the defect came through
    // ===========================================================================================

    /// <summary>
    /// An adopted story is never rewritten over a respelled name — the parent read it and bought
    /// it, and the name is put back as typed. No planner is reached, and the story that is drawn
    /// is the one they previewed, with their child's name in it.
    /// </summary>
    [Fact]
    public async Task An_adopted_story_that_misspells_the_child_is_repaired_and_printed()
    {
        var planner = new ScriptedCompositeStoryService(NamedPlan());

        var result = await Pipeline(
                new ScriptedStoryModelClient(ScenarioFixture()), new StubImageService(),
                masterStory: planner)
            .RunAsync(
                Request(context: VekoContext()) with { ExistingPlan = MisspeltPlan() },
                CancellationToken.None);

        Assert.Equal(0, planner.Calls);
        Assert.Equal("ვეკო და მოციმციმე ტყე", result.Plan.Concept.Title);
        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);
    }

    /// <summary>
    /// **Review finding 3.** A replan takes the artwork with it.
    ///
    /// The replaced story is a different book — different title, different beats, different prose on
    /// every page — and everything a resumed run is holding was planned from the story that has just
    /// been discarded: the Visual Scenario from its boundary, and every stored spread from that
    /// scenario's pages. Leaving the resume state alone meant the run adopted all of it and laid the
    /// new text over the old pictures, with each page illustrating a beat the book no longer
    /// contains — and every one of those pages had passed its own review, because nothing reviews a
    /// picture against the sentence beside it.
    ///
    /// So this asks the three questions that can tell "redrawn" from "adopted": a fresh scenario was
    /// planned, the stored outfit is gone, and all eight pages were drawn rather than six.
    /// </summary>
    [Fact]
    public async Task A_replanned_story_discards_the_scenario_and_the_spreads_drawn_from_the_old_one()
    {
        // Deliberately not the outfit the model would return, which is the only way to tell an
        // adopted scenario from a replanned one that happened to agree.
        const string storedOutfit = "a teal corduroy pinafore with a single brass button.";

        // A replan happens only for a story that never names the child, under a blocker — a
        // respelled name is repaired in place since 2026-09-02, and the shipped default is a flag.
        var planner = new ScriptedCompositeStoryService(NamedPlan());
        var storyClient = new ScriptedStoryModelClient(ScenarioFixture());
        var images = new StubImageService();

        var resume = new CompositeResumeState(
            WithOutfit(storedOutfit),
            new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() },
            new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() })
        {
            // Everything a resumed run needs to adopt those two pages, so that what discards them
            // is the replan and not a missing spec or a missing anchor.
            IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
            AnchorBasePng = BasePng(),
        };

        var result = await Pipeline(storyClient, images, masterStory: planner).RunAsync(
            Request(context: BlockingNameContext(), resume: resume) with { ExistingPlan = Plan() },
            CancellationToken.None);

        // The previewed story was replaced over the name, exactly as before.
        Assert.Equal(1, planner.Calls);
        Assert.Equal(NamedPlan().Concept.Title, result.Plan.Concept.Title);

        // And nothing planned from the story it replaced came with it.
        Assert.Equal(1, storyClient.Calls);
        Assert.NotEqual(storedOutfit, result.Scenario.VisualLock!.ChildOutfit);
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);

        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("discarded", StringComparison.Ordinal)
                       && warning.Contains("Visual Scenario", StringComparison.Ordinal));
    }

    /// <summary>
    /// The mirror of it: an adopted story that is fine keeps every stored page. The invalidation
    /// above is a consequence of the replan, not of resuming.
    /// </summary>
    [Fact]
    public async Task A_correctly_named_adopted_story_keeps_the_stored_scenario_and_spreads()
    {
        const string storedOutfit = "a teal corduroy pinafore with a single brass button.";

        var storyClient = new ScriptedStoryModelClient(ScenarioFixture());
        var images = new StubImageService();

        var resume = new CompositeResumeState(
            WithOutfit(storedOutfit),
            new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() },
            new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() })
        {
            IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
            AnchorBasePng = BasePng(),
        };

        var result = await Pipeline(storyClient, images).RunAsync(
            Request(context: VekoContext(), resume: resume) with { ExistingPlan = VekoPlan("მოციმციმე ტყე") },
            CancellationToken.None);

        Assert.Equal(0, storyClient.Calls);
        Assert.Equal(storedOutfit, result.Scenario.VisualLock!.ChildOutfit);
        Assert.Equal(BookFormat.SpreadCount - 2, images.ImageCalls);
    }

    /// <summary>And a replan that never names the child either stops the book, under a blocker.</summary>
    [Fact]
    public async Task An_adopted_story_whose_replan_is_also_wrong_fails_the_run_under_a_blocker()
    {
        var planner = new ScriptedCompositeStoryService(Plan());

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(
                    new ScriptedStoryModelClient(ScenarioFixture()), new StubImageService(),
                    masterStory: planner)
                .RunAsync(
                    Request(context: BlockingNameContext()) with { ExistingPlan = Plan() },
                    CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.StoryFailed, failure.FailureCode);

        // The fresh plan plus its own corrective retry: text calls, and no artwork.
        Assert.Equal(2, planner.Calls);
    }

    /// <summary>
    /// An adopted story that never names the child, under the shipped default: the book ships as
    /// the parent previewed it, and an alarm says so with the expected name on record.
    ///
    /// No picture rides with it, and that is the point of the empty evidence — this is a refusal
    /// about prose, asked before an image exists.
    /// </summary>
    [Fact]
    public async Task An_unnamed_adopted_story_ships_as_previewed_and_raises_an_alarm()
    {
        var waivers = new List<CompositePolicyWaiver>();

        var context = VekoContext() with
        {
            ReleasePolicy = BekiReleasePolicySnapshot.Defaults,
            OnPolicyWaiver = waiver =>
            {
                waivers.Add(waiver);
                return Task.CompletedTask;
            },
        };

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), new StubImageService())
            .RunAsync(
                Request(context: context) with { ExistingPlan = Plan() },
                CancellationToken.None);

        // The story the parent previewed, untouched — and no planner was reached, which the
        // throwing default stub would have made loud.
        Assert.Equal(Plan().Concept.Title, result.Plan.Concept.Title);
        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        var waiver = Assert.Single(waivers);
        Assert.Equal(BekiReleaseChecks.NameFidelity, waiver.CheckId);
        Assert.Empty(waiver.EvidencePng);
        Assert.Contains("never names", waiver.Detail, StringComparison.Ordinal);

        using var evidence = JsonDocument.Parse(waiver.EvidenceJson);
        Assert.Equal("ვეკო", evidence.RootElement.GetProperty("expected_name").GetString());
    }

    /// <summary>
    /// And a book that spells the name correctly never reaches the planner at all — the adopted
    /// story is adopted, which is what the throwing default stub asserts by not being replaced.
    /// </summary>
    [Fact]
    public async Task A_correctly_named_adopted_story_is_adopted_exactly_as_before()
    {
        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), new StubImageService())
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);
    }

    // ===========================================================================================
    // Harness
    // ===========================================================================================

    /// <summary>The fixture book, retold for a child called ვეკო.</summary>
    private static CompositeBookContext VekoContext() =>
        Context() with
        {
            Input = Context().Input with { ChildName = "ვეკო" },
        };

    /// <summary>The fixture retold for ვეკო, with an operator's blocker on the name check.</summary>
    private static CompositeBookContext BlockingNameContext() =>
        VekoContext() with
        {
            ReleasePolicy = new BekiReleasePolicySnapshot(
            [
                new BekiReleaseCheckSetting(
                    BekiReleaseChecks.NameFidelity, BekiReleaseSeverity.AllClasses,
                    BekiReleaseSeverity.Blocker, "misho", null),
            ]),
        };

    /// <summary>A run with no previewed story, so the pipeline writes one of its own.</summary>
    private static CompositeBookRequest WrittenRequest(CompositeBookContext context) =>
        Request(context: context) with { ExistingPlan = null };

    // ===========================================================================================
    // Restoring the name as typed
    // ===========================================================================================

    [Fact]
    public void A_near_miss_is_put_back_as_typed_and_keeps_its_case_ending()
    {
        var (story, restored) = GeorgianNameFidelity.Restore(
            Book("ველო და მოციმციმე ტყე", "ველოს ბეკი შეხვდა. ველომ გაიცინა."), "ვეკო");

        Assert.Equal("ვეკო და მოციმციმე ტყე", story.Concept.Title);
        Assert.Equal("ვეკოს ბეკი შეხვდა. ვეკომ გაიცინა.", story.Spreads[0].Text);
        Assert.Equal(3, restored.Count);
        Assert.Contains(restored, r => r.Location == "title" && r.Found == "ველო" && r.Restored == "ვეკო");
        Assert.Contains(restored, r => r.Location == "spread 1" && r.Found == "ველოს" && r.Restored == "ვეკოს");

        // And the check that used to refuse this book now passes it.
        Assert.Empty(GeorgianNameFidelity.Inspect(story, "ვეკო"));
    }

    [Fact]
    public void A_correctly_spelled_book_is_returned_untouched()
    {
        var book = Book("ვეკო და ტყე", "ვეკო და ბეკი ერთად მიდიან.");

        var (story, restored) = GeorgianNameFidelity.Restore(book, "ვეკო");

        Assert.Same(book, story);
        Assert.Empty(restored);
    }

    [Fact]
    public void The_companion_and_a_different_word_are_never_rewritten()
    {
        // ბეკი is on every page by contract and is one letter from ბეკა; „ბაფუ“ is nobody's name.
        var (story, restored) = GeorgianNameFidelity.Restore(
            Book("ბეკა და ბეკი", "ბეკა და ბეკი ბაფუს ეძებენ."), "ბეკა");

        Assert.Empty(restored);
        Assert.Equal("ბეკა და ბეკი ბაფუს ეძებენ.", story.Spreads[0].Text);
    }

    [Fact]
    public void A_short_name_is_not_repaired_because_its_neighbours_are_real_words()
    {
        var book = Book("ანა და ტყე", "Ana and Beki walk. ანი მიდის.");

        var (story, restored) = GeorgianNameFidelity.Restore(book, "ანა");

        Assert.Same(book, story);
        Assert.Empty(restored);
    }

    [Fact]
    public void A_different_first_letter_or_alphabet_is_left_alone()
    {
        var book = Book("ბეკო და ტყე", "Veko goes. ბეკო goes.");

        var (story, restored) = GeorgianNameFidelity.Restore(book, "ვეკო");

        Assert.Same(book, story);
        Assert.Empty(restored);
    }

    /// <summary>The eight-spread fixture plan, naming ვეკო correctly, titled after the wood.</summary>
    private static MasterStory NamedPlan() => VekoPlan("მოციმციმე ტყე");

    /// <summary>The same book with the observed defect in its title: ვეკო written ველო.</summary>
    private static MasterStory MisspeltPlan() => VekoPlan("ველო და მოციმციმე ტყე");

    private static MasterStory VekoPlan(string title) => Plan() with
    {
        Concept = Plan().Concept with { Title = title },
        Spreads = Plan().Spreads
            .Select(spread => spread with
            {
                Text = $"ვეკო და ბეკი ერთად მიდიან. გვერდი {spread.Number}.",
                Characters = ["child", "beki"],
            })
            .ToList(),
    };

    /// <summary>One spread with the words under test and nothing else worth reading.</summary>
    private static StorySpread Spread(int number, string text) => new()
    {
        Number = number,
        Title = string.Empty,
        Caption = string.Empty,
        Text = text,
        Illustration = new IllustrationBrief { Scene = "The child in the wood." },
    };

    /// <summary>A one-spread book: a title and one page, which is all most of these need.</summary>
    private static MasterStory Book(string title, string text) => new()
    {
        Concept = new StoryConcept { Title = title, Outline = [text] },
        Spreads = [Spread(1, text)],
        CharacterLock = string.Empty,
        Cover = new IllustrationBrief { Scene = "The child in the wood." },
    };
}
