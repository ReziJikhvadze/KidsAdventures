using System.Text.Json.Nodes;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The v2.2 prop-state contract: the cross-spread object-state machine the supplier's audit
/// mandated after the lantern book, where the key object sat in the child's hand a page before
/// its discovery and again a page after being left in the nest — with every page individually
/// passing review, because no page knew where the object was supposed to be.
/// </summary>
public class CompositePropStateTests : CompositePipelineTestBase
{
    private const string Lantern = "A small blue lantern with a cracked pane";

    // ---------------------------------------------------------------------------------------
    // The validator's state machine
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void A_scenario_without_props_is_still_valid()
    {
        // The supplier's approved fixture predates v2.2 and a stored scenario read back on a
        // resume predates it too; both stay valid. The request schema is what makes every NEW
        // scenario carry states.
        var result = VisualScenarioValidator.Validate(ScenarioFixture());

        Assert.True(result.IsValid, result.Summary);
    }

    [Fact]
    public void A_legal_object_chain_passes()
    {
        var problems = VisualScenarioValidator.SemanticProblems(ScenarioWithStates(
            "NOT_FOUND", "FOUND", "CARRIED", "CARRIED", "CARRIED", "CARRIED", "PLACED",
            "NO_LONGER_CARRIED"));

        Assert.Empty(problems);
    }

    /// <summary>
    /// The audited book, exactly: carried on page 1 although discovery is page 2.
    /// </summary>
    [Fact]
    public void An_object_carried_before_its_discovery_is_rejected()
    {
        var problems = VisualScenarioValidator.SemanticProblems(ScenarioWithStates(
            "CARRIED", "FOUND", "CARRIED", "CARRIED", "CARRIED", "CARRIED", "PLACED",
            "NO_LONGER_CARRIED"));

        Assert.Contains(problems, problem =>
            problem.Code == VisualScenarioProblemCodes.PropStateSequence
            && problem.Detail.Contains("before any FOUND"));
    }

    /// <summary>
    /// The audited book's other half: back in the child's hand after being placed in the nest.
    /// </summary>
    [Fact]
    public void An_object_reappearing_after_being_left_behind_is_rejected()
    {
        var problems = VisualScenarioValidator.SemanticProblems(ScenarioWithStates(
            "NOT_FOUND", "FOUND", "CARRIED", "CARRIED", "CARRIED", "PLACED",
            "NO_LONGER_CARRIED", "CARRIED"));

        Assert.Contains(problems, problem =>
            problem.Code == VisualScenarioProblemCodes.PropStateSequence
            && problem.Detail.Contains("moves backwards"));
    }

    [Fact]
    public void A_second_discovery_is_rejected()
    {
        // Two FOUND pages in a row: not backwards along the chain, so it isolates the
        // discovery-happens-once rule. (A FOUND after a CARRIED page is also rejected, by the
        // moves-backwards rule.)
        var problems = VisualScenarioValidator.SemanticProblems(ScenarioWithStates(
            "NOT_FOUND", "FOUND", "FOUND", "CARRIED", "CARRIED", "CARRIED", "PLACED",
            "NO_LONGER_CARRIED"));

        Assert.Contains(problems, problem =>
            problem.Code == VisualScenarioProblemCodes.PropStateSequence
            && problem.Detail.Contains("FOUND twice"));
    }

    [Fact]
    public void Left_behind_without_a_placed_page_is_rejected()
    {
        var problems = VisualScenarioValidator.SemanticProblems(ScenarioWithStates(
            "NOT_FOUND", "FOUND", "CARRIED", "CARRIED", "CARRIED", "CARRIED", "CARRIED",
            "NO_LONGER_CARRIED"));

        Assert.Contains(problems, problem =>
            problem.Code == VisualScenarioProblemCodes.PropStateSequence
            && problem.Detail.Contains("without a PLACED"));
    }

    [Fact]
    public void Mixing_ambient_with_the_chain_is_rejected()
    {
        var problems = VisualScenarioValidator.SemanticProblems(ScenarioWithStates(
            "NOT_FOUND", "FOUND", "AMBIENT", "CARRIED", "CARRIED", "CARRIED", "PLACED",
            "NO_LONGER_CARRIED"));

        Assert.Contains(problems, problem =>
            problem.Code == VisualScenarioProblemCodes.PropStateInvalid
            && problem.Detail.Contains("mixes AMBIENT"));
    }

    [Fact]
    public void A_page_missing_an_elements_state_is_rejected_once_any_page_states_one()
    {
        var scenario = ScenarioWithStates(
            "NOT_FOUND", "FOUND", "CARRIED", "CARRIED", "CARRIED", "CARRIED", "PLACED",
            "NO_LONGER_CARRIED");

        var spreads = scenario.Spreads!.ToList();
        spreads[4] = spreads[4] with { Props = [] };

        var problems = VisualScenarioValidator.SemanticProblems(scenario with { Spreads = spreads });

        Assert.Contains(problems, problem =>
            problem.Code == VisualScenarioProblemCodes.PropStatesIncomplete
            && problem.Detail.Contains("no state for"));
    }

    [Fact]
    public void A_state_for_an_element_the_lock_never_named_is_rejected()
    {
        var scenario = ScenarioWithStates(
            "NOT_FOUND", "FOUND", "CARRIED", "CARRIED", "CARRIED", "CARRIED", "PLACED",
            "NO_LONGER_CARRIED");

        var spreads = scenario.Spreads!.ToList();
        spreads[0] = spreads[0] with
        {
            Props =
            [
                new VisualScenarioProp { Element = Lantern, State = "NOT_FOUND" },
                new VisualScenarioProp { Element = "an invented torch", State = "CARRIED" },
            ],
        };

        var problems = VisualScenarioValidator.SemanticProblems(scenario with { Spreads = spreads });

        Assert.Contains(problems, problem =>
            problem.Code == VisualScenarioProblemCodes.PropStatesIncomplete
            && problem.Detail.Contains("not one of"));
    }

    // ---------------------------------------------------------------------------------------
    // What the states do to the image prompt
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void States_decide_inclusion_annotation_and_prohibition()
    {
        var elements = new[] { Lantern };

        var notFound = CompositeIllustrationPrompt.ElementsFor(
            elements, "The child walks.", [Prop(Lantern, "NOT_FOUND")]);
        Assert.Empty(notFound.Required);
        Assert.Contains(notFound.Forbidden, line =>
            line.Contains("Do not show") && line.Contains("has not discovered it yet"));

        var carried = CompositeIllustrationPrompt.ElementsFor(
            elements, "The child walks.", [Prop(Lantern, "CARRIED")]);
        Assert.Equal([Lantern], carried.Required);
        Assert.Contains(carried.Annotated, line => line.Contains("holding or carrying"));
        Assert.Empty(carried.Forbidden);

        var left = CompositeIllustrationPrompt.ElementsFor(
            elements, "The child walks.", [Prop(Lantern, "NO_LONGER_CARRIED")]);
        Assert.Empty(left.Required);
        Assert.Contains(left.Forbidden, line => line.Contains("left it behind"));

        var absent = CompositeIllustrationPrompt.ElementsFor(
            elements, "The child walks.", [Prop(Lantern, "ABSENT")]);
        Assert.Empty(absent.Required);
        Assert.Empty(absent.Forbidden);

        // No props at all: the pre-v2.2 fuzzy matching, unchanged.
        var fuzzy = CompositeIllustrationPrompt.ElementsFor(
            elements, $"The child lifts {Lantern.ToLowerInvariant()}.", null);
        Assert.Equal([Lantern], fuzzy.Required);
    }

    [Fact]
    public void A_forbidden_element_reaches_the_prompts_hard_constraints()
    {
        var prompt = CompositeIllustrationPrompt.ForSpread(new CompositeSpreadPromptInput
        {
            Page = 1,
            ChildAge = 5,
            Theme = CompositeThemeReferences.For("dinosaurs"),
            ChildWorldScene = "The child pauses at the valley's edge.",
            ChildOutfit = "a mustard tunic",
            ForbiddenElements =
            [
                "Do not show small blue lantern anywhere in this picture: the story has not "
                + "discovered it yet.",
            ],
            IdentitySpec = CompositePipelineTests.IdentityFixture,
        });

        var constraints = prompt[prompt.IndexOf("HARD CONSTRAINTS", StringComparison.Ordinal)..];
        Assert.Contains("Do not show small blue lantern", constraints);
    }

    // ---------------------------------------------------------------------------------------
    // What the states do to the reviewer
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void The_reviewer_is_told_the_states_and_that_contradicting_one_is_a_failure()
    {
        var lines = CompositeMinimalQa.PropStateLines(
        [
            Prop(Lantern, "CARRIED"),
            Prop("Bafu, a tiny sauropod", "AMBIENT"),
            Prop("a golden fern", "NOT_FOUND"),
        ]);

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, line => line.Contains("holding or carrying"));
        Assert.Contains(lines, line => line.Contains("must not appear"));

        var prompt = CompositeMinimalQa.Prompt(
            "The child walks.", "Beki points ahead.", "a mustard tunic", [], "LEFT",
            identity: CompositePipelineTests.IdentityFixture,
            propStates: lines);

        Assert.Contains("Story object states this page:", prompt);
        Assert.Contains("PROP_STATE failure with recommended_action regenerate_base", prompt);
        Assert.Contains("9. PROP_STATE", prompt);
        Assert.Contains("10. SHOT_COMPLIANCE", prompt);
    }

    // ---------------------------------------------------------------------------------------
    // End to end: a stated NOT_FOUND keeps the object out of the page's generation call
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_pipeline_turns_states_into_prompt_facts()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioWithFixtureProps()), images)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        // Spread 1 is generated first and alone; the fern's NOT_FOUND state becomes a written
        // prohibition rather than an accident of the scene's phrasing.
        var first = images.Prompts[0];
        Assert.Contains("Do not show small spiral-shaped golden fern leaf", first);
        Assert.Contains("has not discovered it yet", first);

        // On the page that carries it, the same element is required with its state spelled out.
        var scenario = VisualScenarioValidator.Validate(ScenarioWithFixtureProps()).Scenario!;
        var page8Scene = scenario.Spreads![7].ChildWorldScene!;
        var page8Prompt = images.Prompts.Single(prompt => prompt.Contains(page8Scene));
        Assert.Contains("the child is holding or carrying this", page8Prompt);
        Assert.DoesNotContain("Do not show small spiral-shaped golden fern leaf", page8Prompt);
    }

    // ---------------------------------------------------------------------------------------

    private static VisualScenarioProp Prop(string element, string state) =>
        new() { Element = element, State = state };

    /// <summary>
    /// A minimal valid scenario with one recurring element whose state runs the given chain
    /// across the eight spreads.
    /// </summary>
    private static VisualScenarioV2 ScenarioWithStates(params string[] states)
    {
        Assert.Equal(BookFormat.SpreadCount, states.Length);

        return new VisualScenarioV2
        {
            VisualLock = new VisualLock
            {
                ChildOutfit = "a mustard tunic",
                RecurringElements = [Lantern],
            },
            Cover = new VisualScenarioCover
            {
                FrontChildWorldScene = "The child looks toward the glowing city.",
                BekiAction = "Beki welcomes the child.",
                BackEnvironment = "The city's rooftops continue under the evening light.",
            },
            Spreads = Enumerable.Range(1, BookFormat.SpreadCount)
                .Select(page => new VisualScenarioSpread
                {
                    Page = page,
                    ChildWorldScene = $"The child explores the city on page {page}.",
                    BekiAction = "Beki points ahead.",
                    Props = [Prop(Lantern, states[page - 1])],
                })
                .ToList(),
        };
    }

    /// <summary>
    /// The supplier's approved fixture, annotated to v2.2: the golden fern leaf runs the chain
    /// (unseen until page 7's thank-you, carried on page 8), and the two sauropods are ambient
    /// where the story shows them.
    /// </summary>
    private static string ScenarioWithFixtureProps()
    {
        var node = JsonNode.Parse(ScenarioFixture())!;
        var elements = node["visual_lock"]!["recurring_elements"]!.AsArray()
            .Select(entry => entry!.GetValue<string>())
            .ToList();

        var bafu = elements[0];
        var mother = elements[1];
        var fern = elements[2];

        string StateFor(string element, int page) =>
            element == fern
                ? page < 7 ? "NOT_FOUND" : page == 7 ? "FOUND" : "CARRIED"
                : element == bafu
                    ? page == 1 ? "ABSENT" : "AMBIENT"
                    : page >= 7 ? "AMBIENT" : "ABSENT";

        foreach (var spread in node["spreads"]!.AsArray())
        {
            var page = spread!["page"]!.GetValue<int>();
            var props = new JsonArray();

            foreach (var element in elements)
            {
                props.Add(new JsonObject
                {
                    ["element"] = element,
                    ["state"] = StateFor(element, page),
                });
            }

            spread["props"] = props;
        }

        return node.ToJsonString();
    }
}
