using System.Text.Json.Nodes;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Composite;

namespace Adventrya.Story.Tests;

public sealed class CompositeStoryCharacterTests : CompositePipelineTestBase
{
    private const string Design = "A tiny violet dinosaur with a pear-shaped body, amber eyes, a crescent mouth, four short legs and silver freckles.";

    private static MasterStory Book(string design) => Plan() with
    {
        Cast = [new StoryCastMember { Id = "char_01", Name = "Bafu", VisualDescription = design }],
        Spreads = Plan().Spreads.Select(spread => spread with
        {
            Characters = spread.Number is 2 or 4 ? ["child", "beki", "char_01"] : ["child", "beki"]
        }).ToList()
    };

    [Fact]
    public void Same_character_id_in_different_books_has_independent_designs()
    {
        var first = Book(Design);
        var second = Book("A tall teal dinosaur with blue eyes and striped fins.");
        Assert.Empty(CompositeStoryCharacters.ForPage(first, 1));
        Assert.Contains(Design, Assert.Single(CompositeStoryCharacters.ForPage(first, 2)));
        Assert.Equal(CompositeStoryCharacters.ForPage(first, 2), CompositeStoryCharacters.ForPage(first, 4));
        Assert.DoesNotContain(Design, CompositeStoryCharacters.PlannerInput(second));
        Assert.Contains("striped fins", Assert.Single(CompositeStoryCharacters.ForPage(second, 2)));
        Assert.Empty(CompositeStoryCharacters.ForPage(Plan(), 2));
    }

    [Fact]
    public async Task Separate_full_scenario_planning_also_preserves_writer_designs()
    {
        var text = new ScriptedStoryModelClient(ScenarioFixture());
        await Pipeline(text, new StubImageService())
            .PlanScenarioAsync(Context(), Book(Design), Photo(), CancellationToken.None);
        Assert.Contains(Design, Assert.Single(text.UserPrompts));
    }

    [Fact]
    public async Task Writer_design_reaches_planner_and_visible_spreads_even_when_planner_omits_it()
    {
        var scenario = JsonNode.Parse(ScenarioFixture())!;
        scenario["visual_lock"]!["recurring_elements"] = new JsonArray();
        var text = new ScriptedStoryModelClient(scenario.ToJsonString());
        var images = new StubImageService();
        var result = await Pipeline(text, images, insertBekiInGeneration: true)
            .RunAsync(Request() with { ExistingPlan = Book(Design) }, CancellationToken.None);

        Assert.Contains(Design, text.UserPrompts[0]);
        Assert.Contains(Design, images.Prompts[1]);
        Assert.Contains(Design, images.Prompts[3]);
        Assert.DoesNotContain(Design, images.Prompts[0]);
        Assert.DoesNotContain(Design, images.Prompts[2]);
        Assert.Null(images.ContinuityImages[1]);
        Assert.Equal(result.Spreads[1].BasePng, images.ContinuityImages[3]);
        Assert.Equal(8, images.ImageCalls);
    }
}
