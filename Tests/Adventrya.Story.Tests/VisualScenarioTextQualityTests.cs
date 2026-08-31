using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The v2.3 text-quality bar (audit P1-08), and the page it was written for.
///
/// The delivered <c>visual-scenario.json</c> opened page 7 with <c>" sensitivity, the child gently
/// pats…"</c>. It passed the supplied schema, whose only constraint on that field is
/// <c>minLength: 1</c>. It passed every semantic rule, because it names the child and does not name
/// Beki. It went to the image model exactly as written, and the picture that came back is in the
/// printed book. Nothing in the pipeline was capable of noticing, because the entire text-quality
/// bar was <c>!IsNullOrWhiteSpace</c>.
///
/// The rules are deliberately mechanical and deliberately narrow. They are asked of the four
/// narrative fields and of nothing else — a lock like "a mustard tunic" is a phrase on purpose, and
/// a sentence rule applied there would reject the supplier's own approved fixture.
/// </summary>
public class VisualScenarioTextQualityTests : CompositePipelineTestBase
{
    /// <summary>The audit's own text, character for character.</summary>
    private const string AuditPageSeven =
        " sensitivity, the child gently pats the moss beside the sleeping bear cub.";

    [Fact]
    public void The_audits_own_page_seven_is_rejected()
    {
        var problems = VisualScenarioValidator.SemanticProblems(
            Scenario(sceneOnPageSeven: AuditPageSeven));

        var malformed = problems
            .Where(problem => problem.Code == VisualScenarioProblemCodes.MalformedText)
            .ToList();

        // Twice over, and the two messages say different things: one about the padding, one about
        // the fragment. A retry told only "malformed" learns less than a retry told both.
        Assert.Equal(2, malformed.Count);
        Assert.Contains(malformed, problem =>
            problem.Detail.Contains("whitespace", StringComparison.Ordinal));
        Assert.Contains(malformed, problem =>
            problem.Detail.Contains("capital letter", StringComparison.Ordinal));
        Assert.All(malformed, problem =>
            Assert.Contains("spreads[6].child_world_scene", problem.Detail, StringComparison.Ordinal));
    }

    [Theory]
    // Trimmed on either side: the fingerprint of a value sliced out of something longer.
    [InlineData(" The child steps into the valley.", "whitespace")]
    [InlineData("The child steps into the valley. ", "whitespace")]
    // Opening on punctuation: the tail of a list, not a sentence that forgot its capital.
    [InlineData(", the child steps into the valley.", "punctuation mark")]
    [InlineData("; the child steps into the valley.", "punctuation mark")]
    [InlineData("— the child steps into the valley.", "punctuation mark")]
    // Opening on a lowercase word — including the conjunction fragments, caught by the same rule
    // rather than by a hand-kept list of conjunctions that would always be somebody's partial list.
    [InlineData("and the child steps into the valley.", "capital letter")]
    [InlineData("but the child steps into the valley.", "capital letter")]
    [InlineData("the child steps into the valley.", "capital letter")]
    // Cut at the other end.
    [InlineData("The child steps into the valley and", "does not end")]
    [InlineData("The child steps into the valley,", "does not end")]
    // Too short to draw from.
    [InlineData("The child waits.", "at least 4")]
    public void A_scene_that_is_not_a_whole_sentence_is_rejected(string scene, string expected)
    {
        var problems = VisualScenarioValidator.SemanticProblems(Scenario(sceneOnPageSeven: scene));

        Assert.Contains(problems, problem =>
            problem.Code == VisualScenarioProblemCodes.MalformedText
            && problem.Detail.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>
    /// Georgian sentences are accepted, which the obvious implementation would not do.
    ///
    /// Georgian is unicameral — ბ is neither upper nor lower case, and <c>char.IsUpper</c> says no
    /// to every letter in the alphabet. A capital-letter rule written for English would reject
    /// every correctly written Georgian scene in a book whose story is Georgian.
    /// </summary>
    [Theory]
    [InlineData("ბავშვი ხეობაში შედის ნელა.")]
    [InlineData("Ⴁავშვი ხეობაში შედის ნელა.")]
    public void A_georgian_sentence_is_a_sentence(string scene)
    {
        // The child-naming rule is English-only and fires separately; this asserts only that the
        // sentence shape is accepted.
        var problems = VisualScenarioValidator.SemanticProblems(Scenario(sceneOnPageSeven: scene));

        Assert.DoesNotContain(problems, problem =>
            problem.Code == VisualScenarioProblemCodes.MalformedText);
    }

    /// <summary>
    /// A Beki action is held to the same shape and a shorter minimum. "Beki listens attentively." is
    /// a complete instruction to the pose selector, and a four-word rule would reject it.
    /// </summary>
    [Fact]
    public void A_beki_action_may_be_three_words_and_no_fewer()
    {
        Assert.DoesNotContain(
            VisualScenarioValidator.SemanticProblems(Scenario(actionOnPageSeven: "Beki listens attentively.")),
            problem => problem.Code == VisualScenarioProblemCodes.MalformedText);

        Assert.Contains(
            VisualScenarioValidator.SemanticProblems(Scenario(actionOnPageSeven: "Beki listens.")),
            problem => problem.Code == VisualScenarioProblemCodes.MalformedText
                && problem.Detail.Contains("at least 3", StringComparison.Ordinal));

        Assert.Contains(
            VisualScenarioValidator.SemanticProblems(Scenario(actionOnPageSeven: " beki listens attentively")),
            problem => problem.Code == VisualScenarioProblemCodes.MalformedText);
    }

    /// <summary>The back cover is a narrative field too — the audit's rule is about the shape of the text, not about which page prints it.</summary>
    [Fact]
    public void The_back_cover_environment_is_held_to_the_scene_rules()
    {
        var problems = VisualScenarioValidator.SemanticProblems(
            Scenario(backEnvironment: "the valley, empty"));

        Assert.Contains(problems, problem =>
            problem.Code == VisualScenarioProblemCodes.MalformedText
            && problem.Detail.Contains("cover.back_environment", StringComparison.Ordinal));
    }

    [Fact]
    public void The_front_cover_scene_is_held_to_the_scene_rules()
    {
        var problems = VisualScenarioValidator.SemanticProblems(
            Scenario(frontScene: " the child at the edge of the valley."));

        Assert.Contains(problems, problem =>
            problem.Code == VisualScenarioProblemCodes.MalformedText
            && problem.Detail.Contains("cover.front_child_world_scene", StringComparison.Ordinal));
    }

    /// <summary>
    /// The locks are phrases, and stay phrases.
    ///
    /// <c>child_outfit</c> and <c>recurring_elements</c> are noun phrases by contract — "a mustard
    /// tunic", "A small blue lantern with a cracked pane" — and the supplier's approved fixture is
    /// written that way. A sentence rule applied here would reject the fixture and teach the model
    /// to pad its visual lock into prose, which is the opposite of what a lock is for.
    /// </summary>
    [Fact]
    public void The_visual_lock_phrases_are_not_held_to_the_sentence_rules()
    {
        var scenario = Scenario() with
        {
            VisualLock = new VisualLock
            {
                ChildOutfit = "a mustard tunic",
                RecurringElements = ["A small blue lantern with a cracked pane", "two sauropods"]
            }
        };

        Assert.DoesNotContain(
            VisualScenarioValidator.SemanticProblems(scenario),
            problem => problem.Code == VisualScenarioProblemCodes.MalformedText);
    }

    /// <summary>
    /// The supplier's approved scenario still passes, whole, through both validator layers.
    ///
    /// The point of a new rule family is to reject the book that was rejected; a rule that also
    /// rejects the document the supplier approved is a rule that has been written wrong.
    /// </summary>
    [Fact]
    public void The_suppliers_approved_scenario_still_passes()
    {
        var result = VisualScenarioValidator.Validate(ScenarioFixture());

        Assert.True(result.IsValid, result.Summary);
    }

    /// <summary>
    /// A well-formed scenario with one field swapped, so each test changes exactly one thing.
    /// </summary>
    private static VisualScenarioV2 Scenario(
        string? sceneOnPageSeven = null,
        string? actionOnPageSeven = null,
        string? frontScene = null,
        string? backEnvironment = null) => new()
        {
            VisualLock = new VisualLock { ChildOutfit = "a mustard tunic", RecurringElements = [] },
            Cover = new VisualScenarioCover
            {
                FrontChildWorldScene = frontScene ?? "The child stands at the edge of the valley.",
                BekiAction = "Beki welcomes the child.",
                BackEnvironment = backEnvironment ?? "The valley continues under the evening light."
            },
            Spreads = Enumerable.Range(1, BookFormat.SpreadCount)
                .Select(page => new VisualScenarioSpread
                {
                    Page = page,
                    ChildWorldScene = page == 7 && sceneOnPageSeven is not null
                        ? sceneOnPageSeven
                        : $"The child explores the valley on page {page}.",
                    BekiAction = page == 7 && actionOnPageSeven is not null
                        ? actionOnPageSeven
                        : "Beki points ahead of the child."
                })
                .ToList()
        };
}
