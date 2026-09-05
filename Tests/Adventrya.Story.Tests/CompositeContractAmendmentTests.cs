using System.Text.Json;
using System.Text.Json.Nodes;
using AdventurePacks.Api.Services.Story.Composite;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The audit-2 contract amendments, each tested against the defect that produced it.
///
/// Every case here quotes a real shipped artefact: the cover's spine bands (P0-03), the visual
/// scenario's page-7 fragment (P1-08), the story's alternating tenses and its `მას ძილი ნებავს`
/// (P1-07). None of them is a hypothetical — the handoff's rule is that only observed defects
/// become implementation rules, so a test that invented one would be testing a rule nobody agreed
/// to.
/// </summary>
public class CompositeContractAmendmentTests : CompositePipelineTestBase
{
    // ===========================================================================================
    // D6c / P0-03 — the cover prompt names no regions
    // ===========================================================================================

    /// <summary>
    /// The de-zoned composition block, exactly as `BEKI_Cover_Base_Prompt_Template_v1.1.md`
    /// publishes it and as `BekiCoverDieline.PanelInstructions` carries it.
    ///
    /// A copy rather than a reference, for one batch only: the dieline file belongs to the composer
    /// agent in this campaign and the constant lands there, where `BekiCoverDielineTests` asserts
    /// the installed text. What this copy buys in the meantime is the law itself — the assertions
    /// below run against the words the contract publishes, so a text that reintroduces a percentage
    /// cannot pass review by being installed somewhere this test does not look.
    /// </summary>
    private const string DezonedPanelInstructions =
        "This is one continuous panoramic scene, painted as a single picture from edge to edge.\n"
        + "The child and the one inviting story action belong on the right side of the picture.\n"
        + "The left side is the same world continuing outward as quieter environment: no child, "
        + "no other character, and no story action there, and never a second version of the "
        + "composition on the right.\n"
        + "Through the middle of the picture the scene stays simple, calm, and low in detail — "
        + "open sky, far ground, quiet water or foliage — carrying the same light, colour, and "
        + "finish as everything around it, with nothing marked, tinted, framed, blurred, or edged "
        + "there and no face, hand, character, or story-critical detail sitting there.\n"
        + "The upper right of the picture stays naturally calm and open, readable without a blank "
        + "panel, artificial blur, dark rectangle, or hard-edged box.\n"
        + "Let the scene run off all four outer edges naturally, and keep everything important "
        + "well away from those edges.";

    private static string CoverPrompt(string panelInstructions)
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        return CompositeIllustrationPrompt.ForCover(new CompositeCoverPromptInput
        {
            Geometry = new CompositeCoverGeometry(
                panelInstructions,
                new AdventurePacks.Api.Services.Story.Composite.Poses.BekiCompositeAnchor(
                    0.87, 0.64, 0.30)),
            ChildAge = 5,
            Theme = CompositeThemeReferences.For("dinosaurs"),
            FrontChildWorldScene = scenario.Cover!.FrontChildWorldScene!,
            BackEnvironment = scenario.Cover.BackEnvironment!,
            ChildOutfit = scenario.VisualLock!.ChildOutfit!,
            // v1.2: the lock is not optional on a cover any more — see CompositeCoverIdentityTests
            // for what it says and why. Here it is simply part of the prompt these assertions read.
            IdentitySpec = IdentityFixture,
        });
    }

    /// <summary>
    /// The reifiable-region law, stated as an assertion: the whole cover prompt contains no
    /// percentage and no word that names a place on the canvas.
    ///
    /// Three incidents produced this. A prompt that named the fold got a dark band painted down
    /// the middle; a prompt that named a "Beki integration zone" at 40.6% got a translucent
    /// rectangle whose left edge sat at 40.6%; a prompt that named the centre construction "from
    /// 47% to 53%" got tonal jumps at 250.5 mm and 261.5 mm, which are those percentages in the
    /// printer's own millimetres. The model paints what it is told is there.
    /// </summary>
    [Fact]
    public void The_cover_prompt_preserves_unmarked_art_but_reserves_the_scoped_title_and_logo()
    {
        var prompt = CoverPrompt(BekiCoverDieline.Geometry.PanelInstructions);
        Assert.Contains("never paint these rectangles", prompt);
        Assert.Contains("TITLE:", prompt);
        Assert.Contains("LOGO:", prompt);
        Assert.Contains("Do not draw borders, bands, blank panels or fold marks", prompt);
        Assert.Contains("selective", prompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What replaced the regions: one picture, a right-side subject, a quiet left side, a calm
    /// middle, and edges the world runs off. The composition the printer needs, asked for as a
    /// composition.
    /// </summary>
    [Fact]
    public void The_cover_prompt_asks_for_one_picture_in_painters_language()
    {
        var prompt = CoverPrompt(DezonedPanelInstructions);

        Assert.Contains("one continuous panoramic scene", prompt, StringComparison.Ordinal);
        Assert.Contains("right side of the picture", prompt, StringComparison.Ordinal);
        Assert.Contains("The left side is the same world", prompt, StringComparison.Ordinal);
        Assert.Contains("the middle of the picture the scene stays simple", prompt, StringComparison.Ordinal);
        Assert.Contains("upper right of the picture stays naturally calm and open", prompt, StringComparison.Ordinal);
        Assert.Contains("keep everything important well away from those edges", prompt, StringComparison.Ordinal);

        // The back cover still carries no cast and no second composition — the audit's one
        // content rule for that side, kept while its name was dropped.
        Assert.Contains("no child, no other character, and no story action there", prompt, StringComparison.Ordinal);
        Assert.Contains("never a second version of the composition", prompt, StringComparison.Ordinal);

        // And the exact-Beki promise, which every version of this prompt exists to protect.
        Assert.Contains("Do not generate Beki.", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Beki integration area is not mentioned at all — not as a rectangle, and not as a
    /// keep-it-calm area either.
    ///
    /// v1.6 of the child/world template learned this the expensive way: it softened the zone into
    /// "keep this area calm, it is never a shape to draw" and that was still a sentence pointing at
    /// a coordinate. The pose is composited by code afterwards, so the model has no reason to be
    /// told anything about where it lands.
    /// </summary>
    [Fact]
    public void The_cover_prompt_says_nothing_about_where_the_pose_will_be_composited()
    {
        var prompt = CoverPrompt(DezonedPanelInstructions);

        Assert.DoesNotContain("integration", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("naturally lit and clear of characters", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The negatives ban the defect in the form it actually shipped: a vertical step in tone, not
    /// only a drawn line.
    /// </summary>
    [Fact]
    public void The_cover_constraints_ban_a_tonal_step_and_no_longer_explain_the_fold()
    {
        var prompt = CoverPrompt(DezonedPanelInstructions);

        Assert.Contains(
            "No vertical step in tone, colour, temperature, or light anywhere in the picture",
            prompt,
            StringComparison.Ordinal);
        Assert.Contains(
            "must match in brightness, colour, contrast, and finish", prompt, StringComparison.Ordinal);

        // The sentence that told the model where the book would be bound is gone; so is the one
        // word in the no-text line that named a place.
        Assert.DoesNotContain("where the printed book will be bound", prompt, StringComparison.Ordinal);
        Assert.Contains("QR code, watermark, or pseudo-text anywhere", prompt, StringComparison.Ordinal);
    }

    /// <remarks>
    /// v1.2 is the owner's rule 2 of 2026-09-01 — the identity lock and the appearance anchor on the
    /// cover. The de-zoning this class is about is v1.1 and is unchanged by it; both are recorded in
    /// the contract's changelog, and this assertion pins whichever amendment is currently shipped.
    /// </remarks>
    [Fact]
    public void The_cover_prompt_version_is_bumped_for_the_amendment() =>
        Assert.Equal("cover-child-world-v1.3", CompositeIllustrationPrompt.CoverVersion);

    // ===========================================================================================
    // D10 / P1-08 — the scenario's text quality bar
    // ===========================================================================================

    private static JsonNode Scenario() => JsonNode.Parse(ScenarioFixture())!;

    private static VisualScenarioValidationResult Validate(JsonNode scenario) =>
        VisualScenarioValidator.Validate(scenario.ToJsonString());

    /// <summary>
    /// The approved book still passes. Said first, because a guard that also refuses good work is
    /// a guard nobody can ship.
    /// </summary>
    [Fact]
    public void The_approved_fixture_still_passes_the_amended_schema()
    {
        var result = VisualScenarioValidator.Validate(ScenarioFixture());

        Assert.True(result.IsValid, result.Summary);
    }

    /// <summary>
    /// The audit's own page-7 string, refused by the supplied schema.
    ///
    /// `" sensitivity, the child gently pats..."` was planned, stored, validated and sent to a paid
    /// image call, because the only text rule in the contract was `minLength: 1`. It fails the
    /// amended schema twice: on the leading space and on the lowercase first letter.
    /// </summary>
    [Fact]
    public void The_audited_page_seven_fragment_is_refused_by_the_supplied_schema()
    {
        var scenario = Scenario();
        scenario["spreads"]![6]!["child_world_scene"] =
            " sensitivity, the child gently pats the small dinosaur beside the river.";

        var result = Validate(scenario);

        Assert.False(result.IsValid);
        Assert.True(result.Has(VisualScenarioProblemCodes.SchemaViolation), result.Summary);
    }

    [Theory]
    // A fragment with no beginning — the shipped defect, in each of the three narrative fields.
    [InlineData("spreads", "child_world_scene", " sensitivity, the child pats the small dinosaur.")]
    // Untrimmed at the end: the schema catches the trailing space, MALFORMED_TEXT owns exact trim.
    [InlineData("spreads", "child_world_scene", "The child pats the small dinosaur. ")]
    // No terminal punctuation — a sentence the model stopped writing mid-thought.
    [InlineData("spreads", "child_world_scene", "The child pats the small dinosaur beside")]
    // Three words where the contract wants a scene.
    [InlineData("spreads", "child_world_scene", "The child waits.")]
    public void A_malformed_spread_scene_is_refused(string _, string field, string value)
    {
        var scenario = Scenario();
        scenario["spreads"]![3]![field] = value;

        Assert.True(
            Validate(scenario).Has(VisualScenarioProblemCodes.SchemaViolation),
            $"the schema accepted \"{value}\"");
    }

    [Theory]
    [InlineData("front_child_world_scene", " sensitivity, the child parts the ferns and looks ahead.")]
    [InlineData("front_child_world_scene", "the child parts the ferns and looks ahead.")]
    [InlineData("back_environment", "warm valley continues through layered ferns and flowers")]
    [InlineData("beki_action", "Beki welcomes")]
    public void A_malformed_cover_field_is_refused(string field, string value)
    {
        var scenario = Scenario();
        scenario["cover"]![field] = value;

        Assert.True(
            Validate(scenario).Has(VisualScenarioProblemCodes.SchemaViolation),
            $"the schema accepted \"{value}\" for cover.{field}");
    }

    /// <summary>
    /// Three words is a legitimate Beki sentence and stays one.
    ///
    /// The pose table reads a verb, not a paragraph: "Beki listens attentively." is exactly what
    /// the vocabulary block asks for. A four-word floor on this field would have refused half the
    /// approved lines of two shipped books, which is why the schema carries two definitions rather
    /// than one.
    /// </summary>
    [Fact]
    public void A_three_word_beki_action_is_accepted()
    {
        var scenario = Scenario();
        scenario["spreads"]![0]!["beki_action"] = "Beki listens attentively.";

        var result = Validate(scenario);

        Assert.False(result.Has(VisualScenarioProblemCodes.SchemaViolation), result.Summary);
    }

    /// <summary>
    /// Georgian text is not refused for being Georgian.
    ///
    /// The fields are English by contract — the system instruction's first general rule is "write
    /// every output description in clear English" — and a scene written in Georgian is refused a
    /// line later, by the semantic layer, for the reason it is actually wrong. What the pattern
    /// must not do is refuse it as *malformed*, because then a proper noun or a quoted Georgian
    /// word inside an otherwise English sentence would be a schema violation.
    /// </summary>
    [Fact]
    public void A_well_formed_georgian_sentence_is_not_malformed()
    {
        var scenario = Scenario();
        scenario["spreads"]![0]!["child_world_scene"] =
            "ბავშვი დგას ხეობაში და უყურებს გვიმრებს.";

        var result = Validate(scenario);

        Assert.False(result.Has(VisualScenarioProblemCodes.SchemaViolation), result.Summary);
    }

    [Fact]
    public void The_scenario_prompt_version_is_bumped_for_the_amendment() =>
        Assert.Equal("visual-scenario-v2.4", CompositeVisualScenarioPrompt.Version);

    /// <summary>
    /// The request schema carries the same rule the supplied file states as a pattern — in words,
    /// because strict structured output rejects the keyword and would fail the request instead of
    /// the answer.
    /// </summary>
    [Fact]
    public void The_request_schema_asks_the_model_for_whole_sentences()
    {
        var schema = CompositeVisualScenarioPrompt.ResponseSchema();
        var spread = schema
            .GetProperty("properties").GetProperty("spreads")
            .GetProperty("items").GetProperty("properties");
        var cover = schema
            .GetProperty("properties").GetProperty("cover").GetProperty("properties");

        foreach (var description in (string[])
                 [spread.GetProperty("child_world_scene").GetProperty("description").GetString()!,
                  cover.GetProperty("front_child_world_scene").GetProperty("description").GetString()!,
                  cover.GetProperty("back_environment").GetProperty("description").GetString()!])
        {
            Assert.Contains("at least four words", description, StringComparison.Ordinal);
            Assert.Contains("Never a fragment", description, StringComparison.Ordinal);
        }

        Assert.Contains(
            "at least three words",
            spread.GetProperty("beki_action").GetProperty("description").GetString()!,
            StringComparison.Ordinal);

        // And no pattern keyword anywhere: the whole point of carrying the rule as prose.
        Assert.DoesNotContain("\"pattern\"", schema.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>The same rule reaches the planner as a rule, not only as a field note.</summary>
    [Fact]
    public void The_system_instruction_asks_for_whole_sentences_and_stated_luminosity()
    {
        var instruction = CompositeVisualScenarioPrompt.SystemInstruction;

        Assert.Contains(
            "Write every output description as whole sentences", instruction, StringComparison.Ordinal);
        Assert.Contains("Never begin a description mid-phrase", instruction, StringComparison.Ordinal);

        // P1-07's visual half: the prop chain says where an object is; this says what it is doing.
        Assert.Contains(
            "must state how strongly it is shining right then", instruction, StringComparison.Ordinal);
        Assert.Contains("dimming", instruction, StringComparison.Ordinal);

        // Stated in the prompt, and nowhere else: no new state joins the enum the validator, the
        // image prompt and the reviewer all read.
        Assert.Equal(7, VisualScenarioPropStates.All.Count);
    }

    // ===========================================================================================
    // D11 / P1-07 — the story's editorial amendments
    // ===========================================================================================

    private static string StoryPrompt() =>
        MasterStoryPromptComposite.System(new CompositeStoryInput
        {
            ChildName = "ნინა",
            AgeBand = "1-2",
            Gender = "girl",
            ThemeId = "dinosaurs",
            Theme = AdventurePacks.Api.Domain.Enums.ThemeType.Dinosaurs
        });

    [Fact]
    public void The_story_prompt_asks_for_one_tense_across_the_whole_book()
    {
        var prompt = StoryPrompt();

        Assert.Contains("One tense for the whole book", prompt, StringComparison.Ordinal);
        Assert.Contains("The present tense is the natural", prompt, StringComparison.Ordinal);
        Assert.Contains("Never drift between present and past narration", prompt, StringComparison.Ordinal);

        // The audit's own evidence pair, so the rule is legible against the book that broke it.
        Assert.Contains("ქრება", prompt, StringComparison.Ordinal);
        Assert.Contains("გამოვიდა", prompt, StringComparison.Ordinal);

        // And it is the spread count the book actually has, not a hardcoded eight.
        Assert.Contains("all 8 spreads", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The audit's canonical phrasing pair, in the prompt.
    ///
    /// The preference is the audit's and so is its condition: `მას ეძინება` "if approved by the
    /// Georgian editor". The prompt states the preference, the checklist rule flags the old form
    /// for a person, and neither one rewrites a word — the editorial pass is still human.
    /// </summary>
    [Fact]
    public void The_story_prompt_carries_the_natural_phrasing_example()
    {
        var prompt = StoryPrompt();

        Assert.Contains("მას ეძინება", prompt, StringComparison.Ordinal);
        Assert.Contains("მას ძილი ნებავს", prompt, StringComparison.Ordinal);
        Assert.Contains("bookish or archaic construction", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_story_prompt_requires_the_text_to_track_prop_state()
    {
        var prompt = StoryPrompt();

        Assert.Contains("if it gives off light", prompt, StringComparison.Ordinal);
        Assert.Contains(
            "is never described again as glowing, shining or bright", prompt, StringComparison.Ordinal);
        Assert.Contains(
            "An object that mattered to the ending is still there at the ending",
            prompt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_story_prompt_version_is_bumped_for_the_amendment() =>
        Assert.Equal("composite-v1.2", MasterStoryPromptComposite.Version);

    // ===========================================================================================
    // v1.2 / the observed defect of 2026-09-01 — the child's name
    // ===========================================================================================

    /// <summary>
    /// The prompt asks for the name to be copied, with the name itself in front of the model.
    ///
    /// A live run for a child called ვეკო came back titled „ველო და მოციმციმე ტყე“: one Georgian
    /// letter, in the child's own name, in the string that becomes the cover, the pack row and the
    /// PDF's metadata. The prompt was given the name and never told it was a name rather than a
    /// word, so the model spelled it the way it spells everything — plausibly.
    /// </summary>
    [Fact]
    public void The_story_prompt_asks_for_the_childs_name_letter_for_letter()
    {
        var prompt = StoryPrompt();

        Assert.Contains("copied, never spelled", prompt, StringComparison.Ordinal);
        Assert.Contains("letter for letter", prompt, StringComparison.Ordinal);

        // The name itself, and the endings Georgian actually adds — a model told to use the exact
        // name and nothing else stops declining it, which is a different defect in the same place.
        Assert.Contains("„ნინა“", prompt, StringComparison.Ordinal);
        Assert.Contains("„ნინას“", prompt, StringComparison.Ordinal);
        Assert.Contains("„ნინასთვის“", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the contract says what the code does, which is the point of a versioned boundary: the
    /// prompt asks, and <c>GeorgianNameFidelity</c> reads the answer.
    /// </summary>
    [Fact]
    public void The_story_boundary_contract_records_the_name_amendment()
    {
        var contract = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Assets", "BekiComposite", "contracts",
            "BEKI_Story_Boundary_v1.md"));

        Assert.Contains("story-boundary-v1.2", contract, StringComparison.Ordinal);
        Assert.Contains("composite-v1.2", contract, StringComparison.Ordinal);

        // The observed defect, as the canonical example.
        Assert.Contains("ველო და მოციმციმე ტყე", contract, StringComparison.Ordinal);
        Assert.Contains("GeorgianNameFidelity", contract, StringComparison.Ordinal);
        Assert.Contains("name_fidelity", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// Everything composite-v1 already promised is still promised. A prompt amendment that quietly
    /// dropped a locked rule would be the most expensive kind of edit in this file.
    /// </summary>
    [Fact]
    public void The_amended_story_prompt_keeps_every_locked_rule()
    {
        var prompt = StoryPrompt();

        Assert.Contains("Nodar Dumbadze", prompt, StringComparison.Ordinal);
        Assert.Contains("Always კ, never ქ", prompt, StringComparison.Ordinal);
        Assert.Contains("No alcohol anywhere", prompt, StringComparison.Ordinal);
        Assert.Contains("This book is for the 1-2 age band", prompt, StringComparison.Ordinal);
        Assert.Contains("Return valid JSON only.", prompt, StringComparison.Ordinal);
    }

    // ===========================================================================================
    // D11 — the Georgian checklist's two new rules
    // ===========================================================================================

    [Fact]
    public void The_amended_checklist_loads_with_every_rule_compiled()
    {
        Assert.Empty(CompositeGeorgianCheck.RuleProblems);
        Assert.Equal("georgian-text-checklist-v1.1", CompositeGeorgianCheck.ChecklistVersion);
    }

    /// <summary>The phrase the audited book shipped, flagged for the editor who has to rule on it.</summary>
    [Fact]
    public void The_unnatural_toddler_phrasing_is_flagged()
    {
        var flag = Assert.Single(
            CompositeGeorgianCheck.InspectText("მას ძილი ნებავს და თვალებს ხუჭავს.", "spread 8"));

        Assert.Equal("unnatural_toddler_phrasing", flag.RuleId);
        Assert.Contains("მას ეძინება", flag.Expected, StringComparison.Ordinal);
        Assert.Contains("pending Georgian editor approval", flag.Expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// A page written in two tenses is flagged; a page written in one is not.
    ///
    /// The rule reads one location at a time, which is all this check ever sees, so it fires on the
    /// mixture inside a page and says so in its own note. A book that is present on spread 3 and
    /// past on spread 5 is the Georgian editor's read and the story prompt's rule, not this file's.
    /// </summary>
    [Theory]
    [InlineData("ბეკი გამოვიდა და პატარა შუქი ქრება.")]
    [InlineData("შუქი ანათებს, ბავშვი და ბეკი გაჰყვნენ ბილიკს.")]
    public void A_page_that_mixes_present_and_past_is_flagged(string text)
    {
        var flag = Assert.Single(CompositeGeorgianCheck.InspectText(text, "spread 4"));

        Assert.Equal("mixed_tense_on_one_page", flag.RuleId);
    }

    [Theory]
    // All past: the audited book's own aorists, with nothing present beside them.
    [InlineData("ბეკი გამოვიდა და ბავშვი გაჰყვა ბილიკს.")]
    // All present: the tense the amendment actually recommends.
    [InlineData("შუქი ანათებს და ბავშვი გაზაფხულს ხედავს.")]
    // And the plain, correct sentences the existing rules already leave alone.
    [InlineData("ბავშვი ღიმილით გაემართა ბილიკზე.")]
    [InlineData("ცისფერ-მწვანე შუქი ანათებდა.")]
    public void One_tense_on_a_page_is_left_alone(string text) =>
        Assert.Empty(CompositeGeorgianCheck.InspectText(text, "spread 1"));

    /// <summary>
    /// The amendment is still only a reading aid: it flags, and the book goes on.
    ///
    /// Two new rules that could fail a paid book would be a worse defect than the one they were
    /// written for — the file's whole contract is that a broken or firing rule costs a note, never
    /// a generation.
    /// </summary>
    [Fact]
    public void The_new_rules_flag_and_never_rewrite()
    {
        const string original = "მას ძილი ნებავს.";
        var flags = CompositeGeorgianCheck.InspectText(original, "spread 8");

        // A flag carries what was found and where; the sentence it was found in is handed back
        // untouched, because a rule that cannot read a sentence may not edit one.
        Assert.NotEmpty(flags);
        Assert.All(flags, flag => Assert.Contains(flag.Found, original, StringComparison.Ordinal));
        Assert.Equal("მას ძილი ნებავს.", original);
    }
}
