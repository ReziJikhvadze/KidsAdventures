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

    /// <summary>
    /// Retries the v5 planning call once, with the given problems stapled onto the user prompt as
    /// a corrective note — the same idiom <see cref="BekiBookGenerator.Corrections"/> uses for a
    /// refused illustration: the original ask stays whole, and the fix rides along with it.
    /// Meaningless for any other variant, because only v5 produces the cast list and per-spread
    /// character placement <see cref="BekiPlanValidator"/> checks.
    /// </summary>
    Task<MasterStoryResult> RetryV5WithCorrectionsAsync(
        MasterStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken);
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
    IOptions<BekiOptions> bekiOptions,
    ILogger<MasterStoryService> logger) : IMasterStoryService
{
    private readonly OpenAiOptions _options = options.Value;
    private readonly BekiOptions _bekiOptions = bekiOptions.Value;

    /// <summary>Marks the two halves apart in the stored prompt columns.</summary>
    private const string StepSeparator = "\n\n===== STEP 2 =====\n\n";

    /// <summary>
    /// So the <see cref="BekiOptions.BookFormatEnabled"/> override below is logged once for this
    /// service instance, however many times <see cref="PromptVersion"/> is read while writing one
    /// preview — <see cref="BuildPrompts"/>, <see cref="WriteAsync"/> and the caller that stores
    /// the version each read it independently, and three log lines for one decision is noise.
    /// </summary>
    private bool _loggedBookFormatOverride;

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
    ///
    /// <see cref="BekiOptions.BookFormatEnabled"/> overrides whatever is configured here: the Beki
    /// book format needs a plan with a cast list and per-spread character placement, which only
    /// v5 produces, so a preview written while the flag is on must never silently fall back to an
    /// A5-shaped plan just because nobody also updated OpenAI:StoryPromptVersion.
    /// </summary>
    public string PromptVersion
    {
        get
        {
            if (_bekiOptions.BookFormatEnabled)
            {
                if (!_loggedBookFormatOverride)
                {
                    logger.LogInformation(
                        "Beki:BookFormatEnabled is on; writing this preview as v5 regardless of "
                        + "OpenAI:StoryPromptVersion ({Configured}).",
                        _options.StoryPromptVersion);
                    _loggedBookFormatOverride = true;
                }

                return "v5";
            }

            var configured = (_options.StoryPromptVersion ?? string.Empty).Trim().TrimStart('v', 'V');

            return configured switch
            {
                "5" => "v5",
                "4" => "v4",
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
            "OpenAI:StoryPromptVersion is \"{Configured}\", which is not v1, v2, v3 or v4. Using v1.",
            configured);

        return "v1";
    }

    public (string System, string User) BuildPrompts(MasterStoryInput input) => PromptVersion switch
    {
        "v5" => (MasterStoryPromptV5.System(input), MasterStoryPromptV5.User(input)),
        "v4" => (MasterStoryPromptV4.System(input), MasterStoryPromptV4.User(input)),
        "v3" => (MasterStoryPromptV3.PlannerSystem(input, StoryBranches.For(input.Theme, Guid.NewGuid())),
                 MasterStoryPromptV3.PlannerUser(input)),
        "v2" => (MasterStoryPromptV2.PlannerSystem(input), MasterStoryPromptV2.PlannerUser(input)),
        _ => (MasterStoryPrompt.System(input), MasterStoryPrompt.User(input))
    };

    public Task<MasterStoryResult> WriteAsync(MasterStoryInput input, CancellationToken cancellationToken) =>
        PromptVersion switch
        {
            "v5" => WriteWithV5Async(input, cancellationToken),
            "v4" => WriteWithV4Async(input, cancellationToken),
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

    // ---- V4 ---------------------------------------------------------------------------------

    /// <summary>
    /// The fourth variant: one call, and no prompt beyond the parameters the parent gave us.
    ///
    /// Structurally this is V1 again, which is the point — V1's prompt grew for three variants
    /// running, and the only way to find out which of that growth the model actually needed is to
    /// run a book without any of it and read the result.
    /// </summary>
    private async Task<MasterStoryResult> WriteWithV4Async(
        MasterStoryInput input,
        CancellationToken cancellationToken)
    {
        var systemPrompt = MasterStoryPromptV4.System(input);
        var userPrompt = MasterStoryPromptV4.User(input);
        var model = ModelName;

        logger.LogInformation(
            "Writing a {Spreads}-spread book for {Child}, age {Age}, theme {Theme}, using {Model} (v4).",
            input.SpreadCount, input.ChildName, input.Age, input.Theme, model);

        var result = await modelClient.CompleteAsync<MasterStory>(
            model,
            systemPrompt,
            userPrompt,
            MasterStorySchema.Name,
            MasterStorySchema.Build(input.SpreadCount),
            cancellationToken);

        return Finish(
            input, result.Value, systemPrompt, userPrompt, model,
            result.PromptTokens, result.CompletionTokens);
    }

    // ---- V5 ---------------------------------------------------------------------------------

    /// <summary>
    /// The Beki format's planning call, reused here for previews: one call, against
    /// <see cref="BekiBookPlanSchema"/> rather than <see cref="MasterStorySchema"/>, because the
    /// result must carry the cast list and each spread's character placement — the things
    /// <see cref="BekiPlanValidator"/> checks and the Beki illustrator reads — which the A5 schema
    /// has no room for.
    /// </summary>
    private async Task<MasterStoryResult> WriteWithV5Async(
        MasterStoryInput input,
        CancellationToken cancellationToken)
    {
        var systemPrompt = MasterStoryPromptV5.System(input);
        var userPrompt = MasterStoryPromptV5.User(input);
        var model = ModelName;

        logger.LogInformation(
            "Planning a {Spreads}-spread Beki book for {Child}, age {Age}, theme {Theme}, using {Model} (v5).",
            input.SpreadCount, input.ChildName, input.Age, input.Theme, model);

        var result = await modelClient.CompleteAsync<MasterStory>(
            model,
            systemPrompt,
            userPrompt,
            BekiBookPlanSchema.Name,
            BekiBookPlanSchema.Build(input.SpreadCount),
            cancellationToken);

        return Finish(
            input, result.Value, systemPrompt, userPrompt, model,
            result.PromptTokens, result.CompletionTokens);
    }

    public async Task<MasterStoryResult> RetryV5WithCorrectionsAsync(
        MasterStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken)
    {
        var systemPrompt = MasterStoryPromptV5.System(input);
        var userPrompt = MasterStoryPromptV5.User(input) + "\n\n" + CorrectionNote(problems);
        var model = ModelName;

        logger.LogInformation(
            "Retrying the Beki plan for {Child}: {Count} problem(s) from the first attempt.",
            input.ChildName, problems.Count);

        var result = await modelClient.CompleteAsync<MasterStory>(
            model,
            systemPrompt,
            userPrompt,
            BekiBookPlanSchema.Name,
            BekiBookPlanSchema.Build(input.SpreadCount),
            cancellationToken);

        return Finish(
            input, result.Value, systemPrompt, userPrompt, model,
            result.PromptTokens, result.CompletionTokens);
    }

    /// <summary>
    /// The same idiom <see cref="BekiBookGenerator.Corrections"/> staples onto a refused
    /// illustration's prompt: the original ask stays whole, and the fix is numbered and appended
    /// rather than the whole plan being re-explained.
    /// </summary>
    private static string CorrectionNote(IReadOnlyList<string> problems)
    {
        var numbered = string.Join("\n", problems.Select((problem, index) => $"{index + 1}. {problem}"));
        return "The previous plan was rejected for these reasons. Return a corrected plan for the "
            + $"same book, with each of them fixed:\n{numbered}";
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
