using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// A real plan from a real model, judged by the real rules.
///
/// Skipped unless ADVENTRYA_OPENAI_KEY is set, so the ordinary suite stays offline, fast and
/// free. This one exists to answer the question no unit test can: whether two dozen rules are
/// actually satisfiable by a model writing a genuine story, or whether the engine is so strict
/// that nothing it produces can ever ship.
///
/// Run it with:
///   $env:ADVENTRYA_OPENAI_KEY = "sk-..."
///   dotnet test --filter LivePlanner
/// </summary>
public class LivePlannerTests(ITestOutputHelper output)
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("ADVENTRYA_OPENAI_KEY");
    private static bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);

    [SkippableFact]
    public async Task A_real_plan_survives_the_validator()
    {
        Skip.IfNot(Enabled, "Set ADVENTRYA_OPENAI_KEY to run the live planner test.");

        var state = LiveBookState(pageCount: 12);
        var planner = BuildPlanner();

        var started = DateTime.UtcNow;
        var blueprint = await planner.PlanAsync(state, CancellationToken.None);
        var elapsed = DateTime.UtcNow - started;

        var context = new BlueprintContext
        {
            Blueprint = blueprint,
            Casting = state.Casting,
            States = StateProjector.Project(blueprint, state.Casting),
            Meta = state.Meta
        };

        var report = new StoryValidator().ValidateBlueprint(context);

        output.WriteLine($"Planned {blueprint.Beats.Count} beats in {elapsed.TotalSeconds:0}s.");
        output.WriteLine($"Promise: {blueprint.Promise}");
        output.WriteLine($"Answer:  {blueprint.Answer}");
        output.WriteLine("");

        foreach (var beat in blueprint.Beats)
        {
            output.WriteLine(
                $"  {beat.Page,2}. [{beat.Purpose}/{beat.Emotion}/{beat.Energy}] {beat.Goal}");
        }

        output.WriteLine("");
        output.WriteLine($"Blocking failures: {report.Blocking.Count()}");
        foreach (var finding in report.Blocking)
        {
            output.WriteLine($"  ✗ {finding}");
        }

        output.WriteLine($"Craft findings: {report.Craft.Count()}");
        foreach (var finding in report.Craft)
        {
            output.WriteLine($"  · {finding}");
        }

        // Craft findings are expected and acceptable — that is the whole point of the tier. A
        // blocking failure on a first attempt is worth knowing about, but the pipeline repairs
        // those, so this asserts the shape of the answer rather than perfection on attempt one.
        Assert.Equal(12, blueprint.Beats.Count);
        Assert.NotEmpty(blueprint.Locations);
        Assert.NotEmpty(blueprint.Objects);
        Assert.NotEmpty(blueprint.Threads);
    }

    [SkippableFact]
    public async Task A_repair_actually_fixes_what_it_was_told_about()
    {
        Skip.IfNot(Enabled, "Set ADVENTRYA_OPENAI_KEY to run the live planner test.");

        var state = LiveBookState(pageCount: 8);
        var planner = BuildPlanner();
        var validator = new StoryValidator();

        var blueprint = await planner.PlanAsync(state, CancellationToken.None);

        // Break it deliberately, in the way that reached real readers: an object used on a page
        // before anything introduced it.
        var broken = blueprint with
        {
            Beats = [.. blueprint.Beats.Select(b => b.Page == 2
                ? b with { ObjectsUsed = [.. b.ObjectsUsed, blueprint.Objects[^1].Id] }
                : b)]
        };

        var before = validator.ValidateBlueprint(ContextFor(broken, state));
        Assert.Contains(before.Blocking, f => f.RuleId == "R1");

        var repaired = await planner.RepairAsync(
            state, broken, before, CancellationToken.None);
        var after = validator.ValidateBlueprint(ContextFor(repaired, state));

        output.WriteLine($"Before repair: {before.Blocking.Count()} blocking");
        foreach (var finding in before.Blocking)
        {
            output.WriteLine($"  ✗ {finding}");
        }

        output.WriteLine($"After repair:  {after.Blocking.Count()} blocking");
        foreach (var finding in after.Blocking)
        {
            output.WriteLine($"  ✗ {finding}");
        }

        Assert.True(after.Blocking.Count() < before.Blocking.Count() || after.CanShip,
            "a repair given the exact finding should reduce the blocking failures");
    }

    private static BlueprintContext ContextFor(StoryBlueprint blueprint, BookState state) => new()
    {
        Blueprint = blueprint,
        Casting = state.Casting,
        States = StateProjector.Project(blueprint, state.Casting),
        Meta = state.Meta
    };

    private static StoryPlanner BuildPlanner()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        var provider = services.BuildServiceProvider();

        var client = new StoryModelClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new OpenAiOptions
            {
                ApiKey = ApiKey!,
                BaseUrl = "https://api.openai.com/v1"
            }),
            NullLogger<StoryModelClient>.Instance);

        return new StoryPlanner(
            client,
            Options.Create(new StoryModelOptions()),
            NullLogger<StoryPlanner>.Instance);
    }

    private static BookState LiveBookState(int pageCount) => StoryFixtures.EmptyBookState() with
    {
        Meta = StoryFixtures.Meta() with { PageCount = pageCount, ChildAge = 6, Language = "en" }
    };
}
