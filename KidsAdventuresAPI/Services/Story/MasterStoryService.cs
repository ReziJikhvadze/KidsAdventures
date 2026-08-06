using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Prompts;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// What the call produced, kept together with what was asked for.
///
/// The prompts travel back with the story because they are worth storing: when a book comes out
/// wrong, the only useful question is what the model was actually told, and a prompt that is
/// rebuilt later from the same inputs is not evidence — the inputs may have been edited since.
/// For the two-step variant both calls are recorded, separated, so the plan a book was written
/// from is as visible as the book.
/// </summary>
public sealed record MasterStoryResult
{
    public required MasterStory Story { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }
    public required string Model { get; init; }
    public required int PromptTokens { get; init; }
    public required int CompletionTokens { get; init; }
}

public interface IMasterStoryService
{
    /// <summary>The model this service will use. Exposed so callers can record it before the call.</summary>
    string ModelName { get; }

    /// <summary>Which prompt variant is in force. Recorded on the run so books can be compared.</summary>
    string PromptVersion { get; }

    /// <summary>
    /// The prompts this input would produce, without making any call. For the two-step variant
    /// only the first is knowable in advance — the writer's prompt contains the plan, which does
    /// not exist yet.
    /// </summary>
    (string System, string User) BuildPrompts(MasterStoryInput input);

    Task<MasterStoryResult> WriteAsync(MasterStoryInput input, CancellationToken cancellationToken);
}

/// <summary>
/// Writes a book, in one call or two depending on the variant in force.
///
/// V1 is one call: concept, every spread, the character lock and nine scene descriptions
/// together. The pictures cannot contradict the words when the same pass wrote both, which is the
/// failure it replaced — a fox named ბუბუ on one page and ბუ on the next.
///
/// V2 splits the decisions from the writing. An architect call settles the cast, the scene each
/// character may enter, the shape of each page and the refrain; a writer call turns that into
/// prose with a much shorter set of rules in front of it. The identity guarantee survives because
/// the character lock is written once, by the architect, and quoted into every illustration
/// prompt by code — nothing has to carry a face between calls.
/// </summary>
public sealed class MasterStoryService(
    IStoryModelClient modelClient,
    IOptions<OpenAiOptions> options,
    ILogger<MasterStoryService> logger) : IMasterStoryService
{
    private readonly OpenAiOptions _options = options.Value;

    /// <summary>Marks the two halves apart in the stored prompt columns.</summary>
    private const string StepSeparator = "\n\n===== STEP 2 =====\n\n";

    public string ModelName =>
        string.IsNullOrWhiteSpace(_options.MasterStoryModel) ? _options.Model : _options.MasterStoryModel;

    /// <summary>
    /// Which variant is in force.
    ///
    /// Forgiving about how it is written, because it is typed into a portal text box by hand and
    /// "2" is at least as natural a thing to enter as "v2". The first attempt at switching was
    /// spent on exactly that: the value said 2, the comparison wanted v2, and the run quietly
    /// carried on with v1 — a setting that silently does something other than what was asked is
    /// worse than one that refuses.
    /// </summary>
    public string PromptVersion
    {
        get
        {
            var configured = (_options.StoryPromptVersion ?? string.Empty).Trim().TrimStart('v', 'V');

            return configured switch
            {
                "3" => "v3",
                "2" => "v2",
                "1" or "" => "v1",
                _ => WarnAndDefault(configured)
            };
        }
    }

    private string WarnAndDefault(string configured)
    {
        logger.LogWarning(
            "OpenAI:StoryPromptVersion is \"{Configured}\", which is neither v1 nor v2. Using v1.",
            configured);

        return "v1";
    }

    public (string System, string User) BuildPrompts(MasterStoryInput input) => PromptVersion switch
    {
        "v3" => (MasterStoryPromptV3.PlannerSystem(input, StoryBranches.For(input.Theme, Guid.NewGuid())),
                 MasterStoryPromptV3.PlannerUser(input)),
        "v2" => (MasterStoryPromptV2.PlannerSystem(input), MasterStoryPromptV2.PlannerUser(input)),
        _ => (MasterStoryPrompt.System(input), MasterStoryPrompt.User(input))
    };

    public Task<MasterStoryResult> WriteAsync(MasterStoryInput input, CancellationToken cancellationToken) =>
        PromptVersion switch
        {
            "v3" => WriteAlongAChainAsync(input, cancellationToken),
            "v2" => WriteInTwoStepsAsync(input, cancellationToken),
            _ => WriteInOneStepAsync(input, cancellationToken)
        };

    // ---- V1 ---------------------------------------------------------------------------------

    private async Task<MasterStoryResult> WriteInOneStepAsync(
        MasterStoryInput input,
        CancellationToken cancellationToken)
    {
        var systemPrompt = MasterStoryPrompt.System(input);
        var userPrompt = MasterStoryPrompt.User(input);
        var model = ModelName;

        logger.LogInformation(
            "Writing a {Spreads}-spread book for {Child}, age {Age}, theme {Theme}, using {Model} (v1).",
            input.SpreadCount, input.ChildName, input.Age, input.Theme, model);

        var result = await modelClient.CompleteAsync<MasterStory>(
            model,
            systemPrompt,
            userPrompt,
            MasterStorySchema.Name,
            MasterStorySchema.Build(input.SpreadCount),
            cancellationToken);

        return Finish(input, result.Value, systemPrompt, userPrompt, model, result.PromptTokens, result.CompletionTokens);
    }

    // ---- V2 ---------------------------------------------------------------------------------

    private async Task<MasterStoryResult> WriteInTwoStepsAsync(
        MasterStoryInput input,
        CancellationToken cancellationToken)
    {
        var model = ModelName;

        var plannerSystem = MasterStoryPromptV2.PlannerSystem(input);
        var plannerUser = MasterStoryPromptV2.PlannerUser(input);

        logger.LogInformation(
            "Planning a {Spreads}-scene book for {Child}, age {Age}, theme {Theme}, using {Model} (v2).",
            input.SpreadCount, input.ChildName, input.Age, input.Theme, model);

        var planned = await modelClient.CompleteAsync<StoryPlan>(
            model,
            plannerSystem,
            plannerUser,
            StoryPlanSchema.Name,
            StoryPlanSchema.Build(input.SpreadCount),
            cancellationToken);

        var plan = planned.Value;

        if (plan.Outline.Count != input.SpreadCount)
        {
            throw new InvalidOperationException(
                $"The architect returned {plan.Outline.Count} scenes, expected {input.SpreadCount}.");
        }

        // Clamped rather than rejected. A character introduced outside the book is a nonsense the
        // writer would have to interpret, and correcting it costs nothing; failing the whole book
        // over it would cost a generation.
        var manifest = plan.CharacterManifest
            .Select(c => c with { IntroducedInSpread = Math.Clamp(c.IntroducedInSpread, 1, input.SpreadCount) })
            .ToList();
        plan = plan with { CharacterManifest = manifest };

        logger.LogInformation(
            "Plan for \"{Title}\": {Characters} secondary character(s), refrain „{Refrain}“, {Prompt}+{Completion} tokens.",
            plan.StoryTitle,
            plan.CharacterManifest.Count,
            plan.RefrainPhrase,
            planned.PromptTokens,
            planned.CompletionTokens);

        var writerSystem = MasterStoryPromptV2.WriterSystem(input);
        var writerUser = MasterStoryPromptV2.WriterUser(plan, StoryJson.Describe(plan));

        var written = await modelClient.CompleteAsync<MasterStory>(
            model,
            writerSystem,
            writerUser,
            MasterStorySchema.Name,
            MasterStorySchema.Build(input.SpreadCount),
            cancellationToken);

        return Finish(
            input,
            written.Value,
            plannerSystem + StepSeparator + writerSystem,
            plannerUser + StepSeparator + writerUser,
            model,
            planned.PromptTokens + written.PromptTokens,
            planned.CompletionTokens + written.CompletionTokens);
    }

    // ---- V3 ---------------------------------------------------------------------------------

    private async Task<MasterStoryResult> WriteAlongAChainAsync(
        MasterStoryInput input,
        CancellationToken cancellationToken)
    {
        var model = ModelName;

        // Chosen here, not by the model. Asked to pick one of three a model picks the first far
        // more often than a third of the time, and having three is only useful if consecutive
        // books land on different ones.
        var branch = StoryBranches.For(input.Theme, Guid.NewGuid());

        var plannerSystem = MasterStoryPromptV3.PlannerSystem(input, branch);
        var plannerUser = MasterStoryPromptV3.PlannerUser(input);

        logger.LogInformation(
            "Planning a {Spreads}-scene book for {Child}, age {Age}, theme {Theme} along „{Branch}“ (v3).",
            input.SpreadCount, input.ChildName, input.Age, input.Theme, branch.Name);

        var planned = await modelClient.CompleteAsync<StoryPlan>(
            model,
            plannerSystem,
            plannerUser,
            StoryPlanSchema.Name,
            StoryPlanSchema.Build(input.SpreadCount),
            cancellationToken);

        var plan = Sanitise(planned.Value, input);

        logger.LogInformation(
            "Plan for \"{Title}\": {Characters} secondary character(s), refrain „{Refrain}“.",
            plan.StoryTitle, plan.CharacterManifest.Count, plan.RefrainPhrase);

        var writerSystem = MasterStoryPromptV3.WriterSystem(input);
        var writerUser = MasterStoryPromptV3.WriterUser(plan, StoryJson.Describe(plan), branch);

        var written = await modelClient.CompleteAsync<MasterStory>(
            model,
            writerSystem,
            writerUser,
            MasterStorySchema.Name,
            MasterStorySchema.Build(input.SpreadCount),
            cancellationToken);

        return Finish(
            input,
            written.Value,
            plannerSystem + StepSeparator + writerSystem,
            plannerUser + StepSeparator + writerUser,
            model,
            planned.PromptTokens + written.PromptTokens,
            planned.CompletionTokens + written.CompletionTokens);
    }

    /// <summary>
    /// Checks the plan is usable, and corrects what is cheaper to correct than to reject.
    /// </summary>
    private static StoryPlan Sanitise(StoryPlan plan, MasterStoryInput input)
    {
        if (plan.Outline.Count != input.SpreadCount)
        {
            throw new InvalidOperationException(
                $"The architect returned {plan.Outline.Count} scenes, expected {input.SpreadCount}.");
        }

        // A character introduced outside the book is a nonsense the writer would have to
        // interpret; correcting it costs nothing, failing the book would cost a generation.
        var manifest = plan.CharacterManifest
            .Select(c => c with { IntroducedInSpread = Math.Clamp(c.IntroducedInSpread, 1, input.SpreadCount) })
            .ToList();

        return plan with { CharacterManifest = manifest };
    }

    // ---- Shared -----------------------------------------------------------------------------

    private MasterStoryResult Finish(
        MasterStoryInput input,
        MasterStory story,
        string systemPrompt,
        string userPrompt,
        string model,
        int promptTokens,
        int completionTokens)
    {
        // The schema fixes the count, so a mismatch means the provider ignored it. Better to fail
        // here than to hand a short book to the page mapper, which would silently print blanks.
        if (story.Spreads.Count != input.SpreadCount)
        {
            throw new InvalidOperationException(
                $"The story model returned {story.Spreads.Count} spreads, expected {input.SpreadCount}.");
        }

        logger.LogInformation(
            "Book \"{Title}\" written: {Spreads} spreads, {Prompt} prompt tokens, {Completion} completion tokens.",
            story.Concept.Title,
            story.Spreads.Count,
            promptTokens,
            completionTokens);

        return new MasterStoryResult
        {
            Story = story,
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            Model = model,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens
        };
    }
}
