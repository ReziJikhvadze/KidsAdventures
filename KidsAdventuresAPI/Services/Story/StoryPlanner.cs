using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Prompts;
using AdventurePacks.Api.Services.Story.Validation;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// Which model does which job.
///
/// Structure is a reasoning problem and prose is a writing one, so they do not want the same
/// model. The planner gets the deepest available, because a plan that is sound costs the rest
/// of the pipeline nothing and a plan that is not costs it everything.
/// </summary>
public sealed record StoryModelOptions
{
    public const string SectionName = "StoryEngine";

    /// <summary>Structure. The one place extra reasoning is unambiguously worth paying for.</summary>
    public string PlannerModel { get; init; } = "gpt-5.6-sol";

    /// <summary>Prose. Quality of sentence is the product.</summary>
    public string WriterModel { get; init; } = "gpt-5.6-sol";

    /// <summary>Judgement, and cheaper — it is reading rather than creating.</summary>
    public string ReviewerModel { get; init; } = "gpt-5.6-terra";

    /// <summary>Structured extraction from a photograph.</summary>
    public string CastingModel { get; init; } = "gpt-5.6-terra";
}

/// <summary>
/// Turns inputs into a plan, and findings into a fix.
///
/// It knows nothing about repair loops or tiers — the pipeline owns those. Its whole
/// responsibility is one call and one shape of answer, which is what makes it substitutable in
/// tests and replaceable when a better model appears.
/// </summary>
public sealed class StoryPlanner(
    IStoryModelClient client,
    IOptions<StoryModelOptions> models,
    ILogger<StoryPlanner> logger) : IStoryPlanner
{
    private readonly StoryModelOptions _models = models.Value;

    public async Task<StoryBlueprint> PlanAsync(BookState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var result = await client.CompleteAsync<StoryBlueprint>(
            _models.PlannerModel,
            PlannerPrompt.System(state.Meta.Language),
            PlannerPrompt.User(state),
            BlueprintSchema.Name,
            BlueprintSchema.Build(),
            cancellationToken);

        logger.LogInformation(
            "Book {BookId}: planned {Beats} beats, {Threads} threads, {Surprises} surprises "
            + "({PromptTokens}+{CompletionTokens} tokens).",
            state.Meta.BookId, result.Value.Beats.Count, result.Value.Threads.Count,
            result.Value.Surprises.Count, result.PromptTokens, result.CompletionTokens);

        return Normalize(result.Value, state.Meta.PageCount);
    }

    public async Task<StoryBlueprint> RepairAsync(
        BookState state,
        StoryBlueprint blueprint,
        ValidationReport report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(report);

        var result = await client.CompleteAsync<StoryBlueprint>(
            _models.PlannerModel,
            PlannerPrompt.System(state.Meta.Language),
            PlannerPrompt.Repair(blueprint, report),
            BlueprintSchema.Name,
            BlueprintSchema.Build(),
            cancellationToken);

        logger.LogInformation(
            "Book {BookId}: repaired the plan against {Findings} findings ({PromptTokens}+{CompletionTokens} tokens).",
            state.Meta.BookId, report.Findings.Count, result.PromptTokens, result.CompletionTokens);

        return Normalize(result.Value, state.Meta.PageCount);
    }

    /// <summary>
    /// The two corrections worth making silently, because both are mechanical and neither
    /// changes the story: beats sorted into page order, and page numbers made contiguous.
    ///
    /// Anything beyond that is left alone deliberately. Quietly patching a plan would hide the
    /// faults the validator exists to report, and a rule that never fires because something
    /// upstream tidied the evidence is worse than no rule at all.
    /// </summary>
    private static StoryBlueprint Normalize(StoryBlueprint blueprint, int expectedPages)
    {
        var ordered = blueprint.Beats.OrderBy(b => b.Page).ToList();

        var renumbered = ordered
            .Select((beat, index) => beat.Page == index + 1 ? beat : Renumber(beat, index + 1))
            .ToList();

        if (renumbered.Count != expectedPages)
        {
            // Not repaired here: the page count is a fact the validator should report against,
            // not something to be papered over before anyone can see it went wrong.
            return blueprint with { Beats = renumbered };
        }

        return blueprint with { Beats = renumbered };
    }

    private static StoryBeat Renumber(StoryBeat beat, int page) => new()
    {
        Page = page,
        Goal = beat.Goal,
        Obstacle = beat.Obstacle,
        Discovery = beat.Discovery,
        Action = beat.Action,
        Purpose = beat.Purpose,
        Emotion = beat.Emotion,
        Energy = beat.Energy,
        LocationId = beat.LocationId,
        TimeOfDay = beat.TimeOfDay,
        Weather = beat.Weather,
        CharactersPresent = beat.CharactersPresent,
        ObjectsIntroduced = beat.ObjectsIntroduced,
        ObjectsUsed = beat.ObjectsUsed,
        Deltas = beat.Deltas,
        Hook = beat.Hook,
        ThreadRefs = beat.ThreadRefs
    };
}
