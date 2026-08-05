using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Validation;

namespace Adventrya.Story.Tests;

/// <summary>
/// Tests for the architectural guarantees themselves, rather than for any one story rule.
///
/// These are the promises the rest of the engine is built on: that a rule can be run alone,
/// that page count is data rather than an assumption, that history is never overwritten, and
/// that snapshots are always derived. If one of these breaks, the design has quietly stopped
/// being the design, and every rule above it becomes harder to trust.
/// </summary>
public class ArchitectureTests
{
    [Fact]
    public void A_single_rule_runs_alone_with_no_pipeline_and_no_dependencies()
    {
        // The whole point of pure rules: construct one, hand it a context, get an answer. No DI
        // container, no services, no orchestration, nothing to mock.
        var rule = new ObjectIntroducedBeforeUseRule();

        var broken = StoryFixtures.Valid().With(beat =>
            beat.Page == 2 ? beat.Replace(objectsUsed: [StoryFixtures.KeyId]) : beat);

        var findings = rule.Check(StoryFixtures.Context(broken)).ToList();

        var finding = Assert.Single(findings);
        Assert.Equal("R1", finding.RuleId);
        Assert.Equal(2, finding.Page);
    }

    [Fact]
    public void Every_rule_is_constructible_without_arguments()
    {
        // A rule that needed a dependency could not be tested in isolation, so the default set
        // must stay dependency-free by construction.
        Assert.NotEmpty(StoryValidator.DefaultBlueprintRules());
        Assert.NotEmpty(StoryValidator.DefaultProseRules());

        Assert.All(StoryValidator.DefaultBlueprintRules(),
            rule => Assert.False(string.IsNullOrWhiteSpace(rule.Id)));
        Assert.All(StoryValidator.DefaultProseRules(),
            rule => Assert.False(string.IsNullOrWhiteSpace(rule.Id)));
    }

    [Fact]
    public void Rule_identifiers_are_unique_so_analytics_can_count_them()
    {
        var ids = StoryValidator.DefaultBlueprintRules().Select(r => r.Id)
            .Concat(StoryValidator.DefaultProseRules().Select(r => r.Id))
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Rules_are_pure_so_running_one_twice_gives_the_same_answer()
    {
        var rule = new ChekhovRule();
        var context = StoryFixtures.Context(StoryFixtures.Valid());

        var first = rule.Check(context).Select(f => f.ToString()).ToList();
        var second = rule.Check(context).Select(f => f.ToString()).ToList();

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    public void Books_of_any_length_validate_without_a_domain_change(int pageCount)
    {
        var blueprint = StoryFixtures.OfLength(pageCount);
        var report = new StoryValidator().ValidateBlueprint(StoryFixtures.Context(blueprint));

        Assert.Equal(pageCount, blueprint.Beats.Count);
        Assert.True(report.CanShip,
            $"a {pageCount} page book should pass: " + string.Join(" | ", report.Blocking));
    }

    [Fact]
    public void Thresholds_scale_with_length_rather_than_being_fixed()
    {
        // A short book cannot be asked for as many distinct emotions as a long one; if these
        // were constants, one of the two lengths would always be judged unfairly.
        Assert.True(
            StoryScale.MinimumDistinctEmotions(20) > StoryScale.MinimumDistinctEmotions(8),
            "a longer book should be asked for more variety");

        Assert.True(
            StoryScale.MinimumSurprises(20) > StoryScale.MinimumSurprises(8),
            "a longer book should be asked for more surprises");

        Assert.False(StoryScale.SupportsDistributionRules(6),
            "a very short book should not be judged on distribution");
    }

    [Fact]
    public void Book_state_is_append_only_so_earlier_versions_survive()
    {
        var original = StoryFixtures.EmptyBookState();
        var planned = original.WithBlueprint(StoryFixtures.Valid(), "planner");
        var written = planned.WithPages([], "writer");

        // Each stage produced a new state; none of them reached back and changed an older one.
        Assert.Null(original.Blueprint);
        Assert.NotNull(planned.Blueprint);
        Assert.Equal(0, original.Version);
        Assert.Equal(1, planned.Version);
        Assert.Equal(2, written.Version);
        Assert.NotSame(original, planned);
    }

    [Fact]
    public void Reviews_accumulate_rather_than_replacing_each_other()
    {
        var state = StoryFixtures.EmptyBookState()
            .WithReview(Review("structural", ["R1 fired"]))
            .WithReview(Review("craft", ["RHYTHM fired"]));

        // Comparing before and after a change is the entire value of analytics, and impossible
        // if the earlier verdict was overwritten.
        Assert.Equal(2, state.Reviews.Count);
        Assert.Equal("structural", state.Reviews[0].Stage);
        Assert.Equal("craft", state.Reviews[1].Stage);
    }

    [Fact]
    public void Revisions_record_which_stage_did_what()
    {
        var state = StoryFixtures.EmptyBookState()
            .WithBlueprint(StoryFixtures.Valid(), "planner", "first attempt")
            .WithBlueprint(StoryFixtures.Valid(), "planner-repair", "R1 on page 2");

        Assert.Equal(2, state.Revisions.Count);
        Assert.Equal("planner", state.Revisions[0].Stage);
        Assert.Equal("planner-repair", state.Revisions[1].Stage);
        Assert.Equal("R1 on page 2", state.Revisions[1].Note);
    }

    [Fact]
    public void Book_state_carries_no_page_snapshots()
    {
        // Snapshots must be projected, never stored: a cached one goes stale behind a repair and
        // is then wrong in silence. This asserts the shape of the type, not merely the habit.
        var properties = typeof(BookState).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("PageStates", properties);
        Assert.DoesNotContain("States", properties);
        Assert.Contains("Blueprint", properties);
    }

    [Fact]
    public void Projection_reflects_a_repair_immediately_because_nothing_is_cached()
    {
        var casting = StoryFixtures.Casting();
        var before = StateProjector.Project(StoryFixtures.Valid(), casting);

        // Repair page four so the key is never picked up.
        var repaired = StoryFixtures.Valid().With(beat =>
            beat.Page == 4 ? beat.Replace(deltas: [], objectsIntroduced: []) : beat);

        var after = StateProjector.Project(repaired, casting);

        Assert.Contains(StoryFixtures.KeyId, before.First(s => s.Page == 6).Inventory);
        Assert.DoesNotContain(StoryFixtures.KeyId, after.First(s => s.Page == 6).Inventory);
    }

    private static StoryReview Review(string stage, IReadOnlyList<string> findings) => new()
    {
        Stage = stage,
        AtUtc = DateTime.UtcNow,
        Findings = findings
    };
}
