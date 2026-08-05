using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Validation;

namespace Adventrya.Story.Tests;

/// <summary>
/// The three faults that prompted this engine, plus the rules that guard the rest.
///
/// Each of the first three tests reproduces a failure that reached a real reader, so a
/// regression here is not theoretical — it is that bug coming back.
/// </summary>
public class ValidatorTests
{
    private readonly StoryValidator _validator = new();

    [Fact]
    public void A_valid_blueprint_has_no_blocking_failures()
    {
        var report = _validator.ValidateBlueprint(StoryFixtures.Context(StoryFixtures.Valid()));

        Assert.True(report.CanShip,
            "the reference book should pass: " + string.Join(" | ", report.Blocking));
    }

    /// <summary>"She suddenly picks up some papers. The papers were never introduced."</summary>
    [Fact]
    public void Object_used_before_it_is_introduced_is_rejected()
    {
        var broken = StoryFixtures.Valid().With(beat =>
            beat.Page == 2 ? beat.Replace(objectsUsed: [StoryFixtures.KeyId]) : beat);

        var report = _validator.ValidateBlueprint(StoryFixtures.Context(broken));

        var finding = Assert.Single(report.Blocking, f => f.RuleId == "R1");
        Assert.Equal(2, finding.Page);
        Assert.False(report.CanShip);
    }

    /// <summary>"The hero finds a golden key. The key disappears."</summary>
    [Fact]
    public void Object_introduced_but_never_meaningful_is_rejected()
    {
        var valid = StoryFixtures.Valid();
        var broken = valid.With(beat => beat.Page is 5 or 6
            ? beat.Replace(objectsUsed: [], deltas: beat.Deltas)
            : beat);

        var report = _validator.ValidateBlueprint(StoryFixtures.Context(broken));

        Assert.Contains(report.Blocking, f => f.RuleId == "CHEKHOV" && f.Detail.Contains(StoryFixtures.KeyId));
    }

    /// <summary>"The fox appears. Then disappears for 6 pages. Then suddenly appears again."</summary>
    [Fact]
    public void Character_vanishing_and_returning_without_explanation_is_rejected()
    {
        var broken = StoryFixtures.Valid().With(beat =>
            beat.Page is 3 or 4 or 5
                ? beat.Replace(charactersPresent: [StoryFixtures.HeroId])
                : beat);

        var report = _validator.ValidateBlueprint(StoryFixtures.Context(broken));

        var finding = Assert.Single(report.Blocking, f => f.RuleId == "R3");
        Assert.Contains(StoryFixtures.FoxId, finding.Detail);
    }

    [Fact]
    public void Teleporting_between_locations_is_rejected()
    {
        var broken = StoryFixtures.Valid().With(beat =>
            beat.Page == 3 ? beat.Replace(deltas: []) : beat);

        var report = _validator.ValidateBlueprint(StoryFixtures.Context(broken));

        Assert.Contains(report.Blocking, f => f.RuleId == "R4");
    }

    [Fact]
    public void A_page_that_changes_nothing_is_rejected()
    {
        var broken = StoryFixtures.Valid().With(beat =>
            beat.Page == 2 ? beat.Replace(deltas: []) : beat);

        var report = _validator.ValidateBlueprint(StoryFixtures.Context(broken));

        Assert.Contains(report.Blocking, f => f.RuleId == "R6" && f.Page == 2);
    }

    [Fact]
    public void An_undeclared_entity_is_rejected()
    {
        var broken = StoryFixtures.Valid().With(beat =>
            beat.Page == 1 ? beat.Replace(charactersPresent: [StoryFixtures.HeroId, "dragon"]) : beat);

        var report = _validator.ValidateBlueprint(StoryFixtures.Context(broken));

        Assert.Contains(report.Blocking, f => f.RuleId == "R12" && f.Detail.Contains("dragon"));
    }

    [Fact]
    public void A_surprise_this_child_already_had_is_flagged_but_still_ships()
    {
        var context = StoryFixtures.Context(
            StoryFixtures.Valid(),
            "Character:a fox afraid of butterflies");

        var report = _validator.ValidateBlueprint(context);

        Assert.Contains(report.Craft, f => f.RuleId == "SURPRISE");
        Assert.True(report.CanShip, "a repeated surprise is a craft problem, never a blocking one");
    }

    [Fact]
    public void Craft_failures_never_block_shipping()
    {
        // Three identical emotions and energies in a row: flat, but not broken.
        var broken = StoryFixtures.Valid().With(beat => beat);
        var context = StoryFixtures.Context(broken);
        var report = _validator.ValidateBlueprint(context);

        Assert.All(report.Craft, f => Assert.Equal(RuleTier.Craft, f.Tier));
        Assert.True(report.CanShip);
    }
}

public class StateProjectorTests
{
    [Fact]
    public void The_key_stays_in_inventory_once_it_is_picked_up()
    {
        var blueprint = StoryFixtures.Valid();
        var states = StateProjector.Project(blueprint, StoryFixtures.Casting());

        // Found on page 4, and still carried on every page after it — which is precisely what
        // makes it impossible for the illustrator to lose.
        Assert.DoesNotContain(StoryFixtures.KeyId, states.First(s => s.Page == 3).Inventory);
        Assert.Contains(StoryFixtures.KeyId, states.First(s => s.Page == 4).Inventory);
        Assert.Contains(StoryFixtures.KeyId, states.First(s => s.Page == 6).Inventory);
    }

    [Fact]
    public void Projection_is_pure_so_repeated_calls_agree()
    {
        var blueprint = StoryFixtures.Valid();
        var casting = StoryFixtures.Casting();

        var first = StateProjector.Project(blueprint, casting);
        var second = StateProjector.Project(blueprint, casting);

        Assert.Equal(first.Count, second.Count);
        Assert.All(first.Zip(second), pair =>
        {
            Assert.Equal(pair.First.LocationId, pair.Second.LocationId);
            Assert.Equal(pair.First.Inventory, pair.Second.Inventory);
            Assert.Equal(pair.First.Companions, pair.Second.Companions);
        });
    }

    [Fact]
    public void The_hero_grows_by_the_end()
    {
        var states = StateProjector.Project(StoryFixtures.Valid(), StoryFixtures.Casting());

        Assert.Equal("cautious", states.First().HeroTrait);
        Assert.Equal("brave", states.Last().HeroTrait);
    }

    [Fact]
    public void Unknown_delta_targets_are_ignored_rather_than_throwing()
    {
        var blueprint = StoryFixtures.Valid().With(beat => beat.Page == 1
            ? beat.Replace(deltas:
            [
                new StateDelta { Kind = DeltaKind.AddToInventory, Target = "not-a-real-object" }
            ])
            : beat);

        // Projection must stay total: validation reports the bad reference precisely, and a
        // crash here would stop the report ever being produced.
        var states = StateProjector.Project(blueprint, StoryFixtures.Casting());
        Assert.Equal(6, states.Count);
    }
}

public class VisualHashTests
{
    [Fact]
    public void The_same_look_always_hashes_the_same()
    {
        var hero = StoryFixtures.Casting().Hero;

        Assert.Equal(
            VisualHash.For(hero, hero.DefaultOutfit).Value,
            VisualHash.For(hero, hero.DefaultOutfit).Value);
    }

    [Fact]
    public void Changing_the_outfit_changes_the_hash()
    {
        var hero = StoryFixtures.Casting().Hero;
        var changed = new Outfit { Top = "green cloak", Bottom = "blue trousers", Shoes = "yellow boots" };

        Assert.NotEqual(
            VisualHash.For(hero, hero.DefaultOutfit).Value,
            VisualHash.For(hero, changed).Value);
    }

    [Fact]
    public void Accessory_order_does_not_change_the_hash()
    {
        var hero = StoryFixtures.Casting().Hero;
        var a = new Outfit { Top = "red coat", Bottom = "blue trousers", Shoes = "yellow boots", Accessories = ["scarf", "badge"] };
        var b = new Outfit { Top = "red coat", Bottom = "blue trousers", Shoes = "yellow boots", Accessories = ["badge", "scarf"] };

        Assert.Equal(VisualHash.For(hero, a).Value, VisualHash.For(hero, b).Value);
    }
}
