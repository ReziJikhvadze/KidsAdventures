using System.Text.Json;
using System.Text.Json.Nodes;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Composite;
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
    /// Retries the printing format's planning call once, with the given problems stapled onto the
    /// user prompt as a corrective note — the same idiom <see cref="BekiBookGenerator.Corrections"/>
    /// uses for a refused illustration: the original ask stays whole, and the fix rides along with
    /// it. The prompts follow <see cref="PromptVersion"/>, and a v6 retry is polished exactly like
    /// a v6 first attempt, so a corrected book is never a less finished book. Meaningless for any
    /// other variant, because only v5 and v6 produce the cast list and per-spread character
    /// placement <see cref="BekiPlanValidator"/> checks.
    /// </summary>
    Task<MasterStoryResult> RetryPlanWithCorrectionsAsync(
        MasterStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken);

    /// <summary>
    /// The composite pipeline's own PLAN BOOK call: <see cref="MasterStoryPromptComposite"/>
    /// against <see cref="CompositeStorySchema"/>, four inputs, Georgian only.
    ///
    /// A separate entry point rather than another case in <see cref="PromptVersion"/>, and the
    /// reason is a routing decision made elsewhere. <see cref="BookFormat.IsPrintPlan"/> is the
    /// gate that sends a purchased pack to the Beki fulfilment job, and it recognises exactly "v5"
    /// and "v6"; a version property that started answering "composite-v1" would route every
    /// composite book to the legacy A5 generator, which is the one path the composite pipeline can
    /// never be reached from. So the selection is made by the caller that wants it — nothing else
    /// calls this — and the preview flow's version, and the routing that reads it, are untouched.
    ///
    /// Refuses outright when <see cref="BekiOptions.CompositePipelineEnabled"/> is off. The flag is
    /// what makes this prompt reachable at all, and a method that quietly worked without it would
    /// be one accidental call away from writing a production book against an unproven prompt.
    /// </summary>
    /// <param name="problems">
    /// A previous attempt's validator problems, numbered onto the end of the user prompt exactly as
    /// <see cref="RetryPlanWithCorrectionsAsync"/> does it. Empty for a first attempt.
    ///
    /// It is a parameter on this method rather than a second method because the corrective retry
    /// must go back to the *composite* prompt. Sending it to the v5/v6 retry would answer a
    /// composite plan's problems with a v6 plan — English copy, Extra Wish, an eye colour and a
    /// leaf spirit, every one of them locked out — and the book would ship written by the prompt
    /// this path exists to avoid.
    /// </param>
    Task<MasterStoryResult> WriteCompositePlanAsync(
        CompositeStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken);
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
    StoryPolishClient polishClient,
    IOptions<OpenAiOptions> options,
    IOptions<BekiOptions> bekiOptions,
    ILogger<MasterStoryService> logger) : IMasterStoryService
{
    private readonly OpenAiOptions _options = options.Value;
    private readonly BekiOptions _bekiOptions = bekiOptions.Value;

    /// <summary>Marks the two halves apart in the stored prompt columns.</summary>
    private const string StepSeparator = "\n\n===== STEP 2 =====\n\n";

    /// <summary>
    /// How the written book is handed to the polisher: the same camelCase the schema and the
    /// stored StoryJson use, so what the editor reads is what an operator would read.
    /// </summary>
    private static readonly JsonSerializerOptions PolishJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// So the <see cref="BekiOptions.BookFormatEnabled"/> override below is logged once for this
    /// service instance, however many times <see cref="PromptVersion"/> is read while writing one
    /// preview — <see cref="BuildPrompts"/>, <see cref="WriteAsync"/> and the caller that stores
    /// the version each read it independently, and three log lines for one decision is noise.
    /// </summary>
    private bool _loggedBookFormatOverride;

    public string ModelName => modelClient.EffectiveModel(
        string.IsNullOrWhiteSpace(_options.MasterStoryModel) ? _options.Model : _options.MasterStoryModel);

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
    /// the printing versions produce, so a preview written while the flag is on must never
    /// silently fall back to an A5-shaped plan just because nobody also updated
    /// OpenAI:StoryPromptVersion. v6 is the current one — v5 plus a voice directive and a polish
    /// pass — and it stays reachable by configuration for a side-by-side comparison.
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
                        "Beki:BookFormatEnabled is on; writing this preview as v6 regardless of "
                        + "OpenAI:StoryPromptVersion ({Configured}).",
                        _options.StoryPromptVersion);
                    _loggedBookFormatOverride = true;
                }

                return "v6";
            }

            var configured = (_options.StoryPromptVersion ?? string.Empty).Trim().TrimStart('v', 'V');

            return configured switch
            {
                "6" => "v6",
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
            "OpenAI:StoryPromptVersion is \"{Configured}\", which is not v1, v2, v3, v4, v5 or v6. Using v1.",
            configured);

        return "v1";
    }

    public (string System, string User) BuildPrompts(MasterStoryInput input) => PromptVersion switch
    {
        "v6" => (MasterStoryPromptV6.System(input), MasterStoryPromptV6.User(input)),
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
            "v6" => WriteWithV6Async(input, cancellationToken),
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

    // ---- V6 ---------------------------------------------------------------------------------

    /// <summary>
    /// V5's planning call in a warmer voice, and then one editing pass over what it wrote.
    ///
    /// Two calls, but not two writers: the second is asked only to correct language, wording and
    /// anything unsafe for the age, and <see cref="PolishAndMergeAsync"/> merges back only the
    /// prose, so the plan the illustrator reads is the one the planner wrote. The polish runs
    /// after the whole book exists rather than instead of part of it, which is what makes it safe
    /// to lose: a polish that throws costs a correction, never a story.
    /// </summary>
    private async Task<MasterStoryResult> WriteWithV6Async(
        MasterStoryInput input,
        CancellationToken cancellationToken)
    {
        var systemPrompt = MasterStoryPromptV6.System(input);
        var userPrompt = MasterStoryPromptV6.User(input);
        var model = ModelName;

        logger.LogInformation(
            "Planning a {Spreads}-spread Beki book for {Child}, age {Age}, theme {Theme}, "
            + "written by {Model}, edited by {Editor} (v6).",
            input.SpreadCount, input.ChildName, input.Age, input.Theme, model, polishClient.ModelName);

        var written = await modelClient.CompleteAsync<MasterStory>(
            model,
            systemPrompt,
            userPrompt,
            BekiBookPlanSchema.Name,
            BekiBookPlanSchema.Build(input.SpreadCount),
            cancellationToken);

        // Before the polish, so the editor reads the book the illustrator will be given. The lock
        // is never merged back, so what is enforced here survives whatever the polisher returns.
        var identified = BekiIdentityRules.EnforceCharacterLock(written.Value, input);

        var polished = await PolishAndMergeAsync(input, identified, cancellationToken);

        // After it, and last: whatever either model spelled, the companion's name prints „ბეკი“.
        polished.Story = BekiIdentityRules.EnforceBrandSpelling(polished.Story, input.ChildName);

        // Two vendors, one column. When the writer and the editor are the same model the stored
        // name is unchanged; when they are not, storing only the writer would attribute the
        // book's grammar to a model that never saw it — and telling the two runs apart afterwards
        // is the entire point of being able to split them.
        var recordedModel = string.Equals(model, polishClient.ModelName, StringComparison.OrdinalIgnoreCase)
            ? model
            : $"{model} + {polishClient.ModelName}";

        return FinishPolished(input, recordedModel, systemPrompt, userPrompt, written, polished);
    }

    /// <summary>
    /// The editing pass, and the merge that keeps it to its job.
    ///
    /// The prompt tells the model not to touch the plot, the ids, the scenes, the character lock
    /// or the cast; this method makes that true whatever the model does, by starting from the
    /// written book and copying back four fields — the title, the English title, and each spread's
    /// text in both languages. Nothing else can cross, so a polisher that rewrites a scene or
    /// invents a cast member has simply wasted its own output.
    ///
    /// Returns the merged story with the polish prompts and what they cost, so the caller can
    /// record both calls the way v2 and v3 record theirs. A polish that never ran comes back as
    /// empty prompts and no tokens, alongside the written story untouched.
    /// </summary>
    private async Task<(MasterStory Story, string PolishSystem, string PolishUser, int PromptTokens, int CompletionTokens)>
        PolishAndMergeAsync(
            MasterStoryInput input,
            MasterStory generated,
            CancellationToken cancellationToken)
    {
        var polishSystem = StoryPolishPrompt.System(input);
        var polishUser = string.Empty;
        ModelResult<MasterStory> polished;

        try
        {
            polishUser = StoryPolishPrompt.User(
                input, JsonSerializer.Serialize(generated, PolishJsonOptions));

            polished = await polishClient.Client.CompleteAsync<MasterStory>(
                polishClient.ModelName,
                polishSystem,
                polishUser,
                BekiBookPlanSchema.Name,
                BekiBookPlanSchema.Build(input.SpreadCount),
                cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Best-effort, deliberately. What came out of the generator is already a whole book,
            // and a book that ships with an unpolished sentence is better than no book at all.
            // A cancelled caller is the one exception: falling back would report success for work
            // the caller has already walked away from, so cancellation passes through.
            logger.LogWarning(
                ex, "The polish call failed for {Child}'s book; keeping the written story as it is.",
                input.ChildName);

            return (generated, string.Empty, string.Empty, 0, 0);
        }

        // The prompts and the tokens are reported from here on whatever the merge decides: the
        // call was made and paid for, and a merge we refuse is still a call an operator should be
        // able to read.
        var writtenNumbers = generated.Spreads.Select(spread => spread.Number).ToList();
        var polishedNumbers = polished.Value.Spreads.Select(spread => spread.Number).ToList();

        // The schema fixes the spread count but neither the numbering nor its uniqueness, so the
        // numbers are checked rather than trusted: without the same set on both sides there is no
        // correspondence to merge along, and merging by position would put one spread's corrected
        // text under another spread's picture.
        if (polishedNumbers.Distinct().Count() != polishedNumbers.Count
            || !writtenNumbers.ToHashSet().SetEquals(polishedNumbers))
        {
            logger.LogWarning(
                "The polished book numbers its spreads {Polished}, not {Written}; nothing was "
                + "merged and the written story stands.",
                string.Join(", ", polishedNumbers), string.Join(", ", writtenNumbers));

            return (generated, polishSystem, polishUser, polished.PromptTokens, polished.CompletionTokens);
        }

        var polishedByNumber = polished.Value.Spreads.ToDictionary(spread => spread.Number);

        var merged = generated with
        {
            Concept = generated.Concept with
            {
                Title = Prefer(polished.Value.Concept.Title, generated.Concept.Title)
            },
            TitleEn = PreferOptional(polished.Value.TitleEn, generated.TitleEn),
            Spreads = generated.Spreads
                .Select(spread => spread with
                {
                    Text = Prefer(polishedByNumber[spread.Number].Text, spread.Text),
                    TextEn = PreferOptional(polishedByNumber[spread.Number].TextEn, spread.TextEn)
                })
                .ToList()
        };

        // The validator is the one reader that can tell whether the correction broke something —
        // a spread edited past the age's word cap, a title emptied. Only what the merge introduced
        // counts: a problem the written book already had is the retry's business, not the
        // polisher's.
        var before = BekiPlanValidator.Validate(generated, input.SpreadCount, input.Age);
        var introduced = BekiPlanValidator.Validate(merged, input.SpreadCount, input.Age)
            .Except(before)
            .ToList();

        if (introduced.Count == 0)
        {
            return (merged, polishSystem, polishUser, polished.PromptTokens, polished.CompletionTokens);
        }

        logger.LogWarning(
            "The polished book introduced {Count} problem(s); putting the written prose back where "
            + "they are: {Problems}",
            introduced.Count, string.Join(" | ", introduced));

        var revertSpreads = introduced
            .Select(SpreadNumberIn)
            .Where(number => number is not null)
            .Select(number => number!.Value)
            .ToHashSet();

        // A problem naming no spread is about the book's own title, which is the only other thing
        // the merge touched.
        var revertTitle = introduced.Any(problem => SpreadNumberIn(problem) is null);

        // By index rather than by number: `merged` was built one-for-one from `generated`, in
        // order, so the positions line up even for a plan whose numbering the validator already
        // objects to.
        var reverted = merged with
        {
            Concept = revertTitle ? generated.Concept : merged.Concept,
            TitleEn = revertTitle ? generated.TitleEn : merged.TitleEn,
            Spreads = merged.Spreads
                .Select((spread, index) => revertSpreads.Contains(spread.Number) ? generated.Spreads[index] : spread)
                .ToList()
        };

        // One pass, no loop. If putting the written prose back did not clear what the merge
        // introduced, the merge is not the thing to keep arguing with.
        var remaining = BekiPlanValidator.Validate(reverted, input.SpreadCount, input.Age)
            .Except(before)
            .ToList();

        if (remaining.Count > 0)
        {
            logger.LogWarning(
                "Reverting the polished prose did not clear {Count} problem(s); dropping the polish "
                + "entirely: {Problems}",
                remaining.Count, string.Join(" | ", remaining));

            return (generated, polishSystem, polishUser, polished.PromptTokens, polished.CompletionTokens);
        }

        return (reverted, polishSystem, polishUser, polished.PromptTokens, polished.CompletionTokens);
    }

    /// <summary>The corrected value when it is actually a value; the written one otherwise.</summary>
    private static string Prefer(string? polished, string written) =>
        string.IsNullOrWhiteSpace(polished) ? written : polished;

    /// <inheritdoc cref="Prefer"/>
    private static string? PreferOptional(string? polished, string? written) =>
        string.IsNullOrWhiteSpace(polished) ? written : polished;

    /// <summary>
    /// The spread a validator problem is about, when it names one. Every per-spread message
    /// <see cref="BekiPlanValidator"/> writes opens with "Spread {number}", which is all the merge
    /// guard needs to know whose prose to put back.
    /// </summary>
    private static int? SpreadNumberIn(string problem)
    {
        const string prefix = "Spread ";
        if (!problem.StartsWith(prefix, StringComparison.Ordinal)) return null;

        var rest = problem.AsSpan(prefix.Length);
        var digits = 0;
        while (digits < rest.Length && char.IsDigit(rest[digits])) digits++;

        return digits > 0 && int.TryParse(rest[..digits], out var number) ? number : null;
    }

    public async Task<MasterStoryResult> RetryPlanWithCorrectionsAsync(
        MasterStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken)
    {
        var version = PromptVersion;
        var polishes = version == "v6";

        var systemPrompt = polishes ? MasterStoryPromptV6.System(input) : MasterStoryPromptV5.System(input);
        var userPrompt = (polishes ? MasterStoryPromptV6.User(input) : MasterStoryPromptV5.User(input))
            + "\n\n" + CorrectionNote(problems);
        var model = ModelName;

        logger.LogInformation(
            "Retrying the Beki plan for {Child} ({Version}): {Count} problem(s) from the first attempt.",
            input.ChildName, version, problems.Count);

        var result = await modelClient.CompleteAsync<MasterStory>(
            model,
            systemPrompt,
            userPrompt,
            BekiBookPlanSchema.Name,
            BekiBookPlanSchema.Build(input.SpreadCount),
            cancellationToken);

        if (!polishes)
        {
            return Finish(
                input, result.Value, systemPrompt, userPrompt, model,
                result.PromptTokens, result.CompletionTokens);
        }

        // A retried book is polished exactly like a first-attempt one, and held to the same two
        // identity rules for the same reason. The retry answers the validator; the polish answers
        // a different question, and a corrected book is not a reason to ship the one unedited
        // book in the format.
        var identified = BekiIdentityRules.EnforceCharacterLock(result.Value, input);
        var polished = await PolishAndMergeAsync(input, identified, cancellationToken);

        polished.Story = BekiIdentityRules.EnforceBrandSpelling(polished.Story, input.ChildName);

        return FinishPolished(input, model, systemPrompt, userPrompt, result, polished);
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

    // ---- Composite --------------------------------------------------------------------------

    /// <inheritdoc cref="IMasterStoryService.WriteCompositePlanAsync" />
    public async Task<MasterStoryResult> WriteCompositePlanAsync(
        CompositeStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!_bekiOptions.CompositePipelineEnabled)
        {
            throw new InvalidOperationException(
                "The composite story prompt is only written when Beki:CompositePipelineEnabled is "
                + "on. Every other path writes its book with the configured prompt version.");
        }

        var systemPrompt = MasterStoryPromptComposite.System(input);
        var userPrompt = MasterStoryPromptComposite.User(input);

        // The correction rides on the composite prompt, never on v6's. See the interface's note.
        if (problems is { Count: > 0 })
        {
            userPrompt += "\n\n" + CorrectionNote(problems);
        }

        var model = ModelName;

        logger.LogInformation(
            "Planning a {Spreads}-spread composite book for {Child}, age band {AgeBand}, theme "
            + "{Theme}, using {Model} ({Version}), {Problems} correction(s).",
            input.SpreadCount, input.ChildName, input.AgeBand, input.ThemeId, model,
            MasterStoryPromptComposite.Version, problems?.Count ?? 0);

        // JsonElement rather than MasterStory: the composite schema has no characterLock, and the
        // record does — see ReadCompositePlan. Deserializing straight to the record would fail on
        // a perfectly correct answer.
        var result = await modelClient.CompleteAsync<JsonElement>(
            model,
            systemPrompt,
            userPrompt,
            CompositeStorySchema.Name,
            CompositeStorySchema.Build(input.SpreadCount),
            cancellationToken);

        var story = ReadCompositePlan(result.Value);

        /*
          A wrong spread count is reported, not thrown.

          The legacy path can throw here because its schema pins the count with an array length the
          provider enforces. The composite path's request schema cannot: strict structured-output
          mode rejects minItems and maxItems, so "exactly eight" survives only as a sentence in a
          description, and a model that returns seven is returning something the request permitted.

          Throwing made that a failed preview. But the caller already owns a corrective retry for
          exactly this class of fault — a plan that is well-formed and wrong — and it is a retry
          that was being skipped by the one problem most likely to need it. So the count travels out
          with the plan and reaches BekiPlanValidator, which reports it in the same list as every
          other plan problem and gets the same second attempt.
        */
        if (story.Spreads.Count != input.SpreadCount)
        {
            logger.LogWarning(
                "The composite planner returned {Actual} spreads, expected {Expected}; leaving it "
                + "for the caller's plan validation and its corrective retry.",
                story.Spreads.Count, input.SpreadCount);
        }

        /*
          The editing pass, which this path did not have.

          It was not a decision; it was a gap. v6 writes and then polishes, and the composite prompt
          was forked from v6's *writer* — so every composite book in existence shipped its first
          draft. The supplier's audit found what that costs in the only place it shows: ფუნღუროში
          for ფუღუროში and ეწყოს for იყოს, in a printed book, in a language most of the people
          reading the logs cannot proof-read.

          One call, best-effort, and the same merge discipline v6 uses: only the title and the
          per-spread Georgian text can cross back, so a polisher that rewrites a scene, invents a
          cast member or renumbers the spreads has wasted its own output rather than damaged a book.
        */
        var polished = await PolishCompositeAsync(input, story, cancellationToken);

        logger.LogInformation(
            "Composite book \"{Title}\" written: {Spreads} spreads, {Prompt} prompt tokens, "
            + "{Completion} completion tokens.",
            polished.Story.Concept.Title, polished.Story.Spreads.Count,
            result.PromptTokens + polished.PromptTokens,
            result.CompletionTokens + polished.CompletionTokens);

        var polishRan = !string.IsNullOrEmpty(polished.PolishSystem);

        return new MasterStoryResult
        {
            Story = polished.Story,
            // Both calls in the stored columns, separated, exactly as FinishPolished records v6's.
            // A polish that never happened leaves no separator behind, so a stored prompt never
            // describes a call that was not made.
            SystemPrompt = polishRan ? systemPrompt + StepSeparator + polished.PolishSystem : systemPrompt,
            UserPrompt = polishRan ? userPrompt + StepSeparator + polished.PolishUser : userPrompt,
            Model = model,
            PromptTokens = result.PromptTokens + polished.PromptTokens,
            CompletionTokens = result.CompletionTokens + polished.CompletionTokens
        };
    }

    /// <summary>
    /// The composite path's editing pass: one call, the composite schema, and a merge that copies
    /// back the title and each spread's Georgian text and nothing else.
    ///
    /// A sibling of <see cref="PolishAndMergeAsync"/> rather than a parameter on it, because three
    /// things differ and every one of them would have been a branch through the middle of a method
    /// that edits every book in production. The schema is <see cref="CompositeStorySchema"/>, which
    /// has no <c>characterLock</c>, no <c>titleEn</c> and no <c>textEn</c> — asking the polisher for
    /// v6's shape would demand a character lock this path deliberately does not have. The book is
    /// handed over as a JsonElement and read back through <see cref="ReadCompositePlan"/> for the
    /// same reason. And the merge copies two fields rather than four, because the other two do not
    /// exist here.
    ///
    /// Everything else is v6's, deliberately: best-effort (a failed polish keeps the written book,
    /// because a book with an unpolished sentence beats no book), the spread-number guard before any
    /// merge, and the validator diff that puts the written prose back if the correction introduced a
    /// problem the draft did not have.
    /// </summary>
    private async Task<(MasterStory Story, string PolishSystem, string PolishUser, int PromptTokens, int CompletionTokens)>
        PolishCompositeAsync(
            CompositeStoryInput input,
            MasterStory written,
            CancellationToken cancellationToken)
    {
        var polishSystem = StoryPolishPrompt.CompositeSystem;
        var polishUser = string.Empty;
        ModelResult<JsonElement> answer;

        try
        {
            polishUser = StoryPolishPrompt.CompositeUser(
                input.AgeBand, CompositePolishJson(written));

            answer = await polishClient.Client.CompleteAsync<JsonElement>(
                polishClient.ModelName,
                polishSystem,
                polishUser,
                CompositeStorySchema.Name,
                CompositeStorySchema.Build(input.SpreadCount),
                cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex, "The composite polish call failed for {Child}'s book; keeping the written story "
                + "as it is.", input.ChildName);

            return (written, string.Empty, string.Empty, 0, 0);
        }

        MasterStory corrected;
        try
        {
            corrected = ReadCompositePlan(answer.Value);
        }
        catch (Exception ex)
        {
            // The call was made and paid for, so its prompts and tokens are still reported; what
            // came back was not a book, so nothing is merged.
            logger.LogWarning(
                ex, "The composite polish returned something that is not a usable plan; the written "
                + "story stands.");

            return (written, polishSystem, polishUser, answer.PromptTokens, answer.CompletionTokens);
        }

        var writtenNumbers = written.Spreads.Select(spread => spread.Number).ToList();
        var polishedNumbers = corrected.Spreads.Select(spread => spread.Number).ToList();

        // Without the same set of numbers on both sides there is no correspondence to merge along,
        // and merging by position would put one spread's corrected text under another's picture.
        if (polishedNumbers.Distinct().Count() != polishedNumbers.Count
            || !writtenNumbers.ToHashSet().SetEquals(polishedNumbers))
        {
            logger.LogWarning(
                "The polished composite book numbers its spreads {Polished}, not {Written}; nothing "
                + "was merged and the written story stands.",
                string.Join(", ", polishedNumbers), string.Join(", ", writtenNumbers));

            return (written, polishSystem, polishUser, answer.PromptTokens, answer.CompletionTokens);
        }

        var byNumber = corrected.Spreads.ToDictionary(spread => spread.Number);

        var merged = written with
        {
            Concept = written.Concept with
            {
                Title = Prefer(corrected.Concept.Title, written.Concept.Title)
            },
            Spreads = written.Spreads
                .Select(spread => spread with
                {
                    Text = Prefer(byNumber[spread.Number].Text, spread.Text)
                })
                .ToList()
        };

        // Only what the correction introduced counts. A problem the written book already had is the
        // caller's corrective retry's business, not the polisher's — and the composite path has two
        // validators, so both are differenced.
        var before = CompositeProblems(written, input);
        var introduced = CompositeProblems(merged, input).Except(before).ToList();

        if (introduced.Count == 0)
        {
            logger.LogInformation(
                "Composite book polished by {Editor} ({Version}): {Changed} of {Spreads} spread "
                + "text(s) corrected, title {TitleChanged}.",
                polishClient.ModelName, StoryPolishPrompt.CompositeVersion,
                merged.Spreads.Count(spread =>
                    !string.Equals(
                        spread.Text,
                        written.Spreads.First(w => w.Number == spread.Number).Text,
                        StringComparison.Ordinal)),
                merged.Spreads.Count,
                string.Equals(merged.Concept.Title, written.Concept.Title, StringComparison.Ordinal)
                    ? "unchanged"
                    : "corrected");

            return (merged, polishSystem, polishUser, answer.PromptTokens, answer.CompletionTokens);
        }

        // One pass, no loop, and the whole polish is dropped rather than merged field by field: the
        // composite book has two prose fields, so "put back only the spreads that broke" and "put
        // the draft back" are nearly the same repair, and the simpler one cannot get it wrong.
        logger.LogWarning(
            "The composite polish introduced {Count} problem(s); dropping it and keeping the "
            + "written story: {Problems}",
            introduced.Count, string.Join(" | ", introduced));

        return (written, polishSystem, polishUser, answer.PromptTokens, answer.CompletionTokens);
    }

    /// <summary>
    /// Everything wrong with a composite plan, from both validators that judge one.
    ///
    /// Used only as a difference — before the merge against after it — so a plan that already fails
    /// a rule is not the polisher's fault and does not cost it its corrections. The age is not
    /// passed: the composite path holds an age band and <see cref="BekiPlanValidator"/> wants a
    /// number, and the word caps it would apply are not this book's caps.
    /// </summary>
    private static IReadOnlyList<string> CompositeProblems(MasterStory plan, CompositeStoryInput input) =>
    [
        .. BekiPlanValidator.Validate(plan, input.SpreadCount),
        .. CompositePlanRules.Problems(plan, input.SpreadCount),
    ];

    /// <summary>
    /// The written book as the polisher is shown it: the composite schema's own fields, and not one
    /// field more.
    ///
    /// <see cref="MasterStory"/> carries three properties this path does not use — an empty
    /// <c>characterLock</c> that <see cref="ReadCompositePlan"/> supplied, and a null
    /// <c>titleEn</c>/<c>textEn</c> from the A5 format. Serializing them would hand the editor a
    /// document in a shape its own response schema forbids, which is an invitation to answer in the
    /// shape it was shown.
    /// </summary>
    private static string CompositePolishJson(MasterStory story)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(story, PolishJsonOptions))!.AsObject();

        node.Remove("characterLock");
        node.Remove("titleEn");

        if (node["spreads"] is JsonArray spreads)
        {
            foreach (var spread in spreads)
            {
                spread?.AsObject().Remove("textEn");
            }
        }

        return node.ToJsonString(PolishJsonOptions);
    }

    /// <summary>
    /// Reads a composite plan into the record every other stage already speaks.
    ///
    /// The one adjustment is <c>characterLock</c>. <see cref="MasterStory"/> declares it required
    /// because every A5 book in storage has one and relaxing the record would break resuming those;
    /// <see cref="CompositeStorySchema"/> deliberately does not ask for it, because it is the
    /// paragraph describing a real child's face and the composite pipeline puts the child's
    /// likeness entirely in the photograph's hands. System.Text.Json throws on a missing required
    /// property, so the two facts have to meet somewhere, and this is the only place a composite
    /// plan is read.
    ///
    /// An empty string rather than a placeholder sentence: nothing on this path reads the field,
    /// and a plausible-looking description sitting in it is exactly what a later edit would pick up
    /// and put into an image prompt.
    /// </summary>
    private static MasterStory ReadCompositePlan(JsonElement plan)
    {
        var node = JsonNode.Parse(plan.GetRawText())?.AsObject()
                   ?? throw new InvalidOperationException("The composite planner returned no object.");

        node["characterLock"] ??= string.Empty;

        return JsonSerializer.Deserialize<MasterStory>(node.ToJsonString(), StoryJson.Options)
               ?? throw new InvalidOperationException("The composite planner returned no usable plan.");
    }

    // ---- Shared -----------------------------------------------------------------------------

    /// <summary>
    /// Records a polished book the way v2 and v3 record their two calls: both prompts in the
    /// stored columns with <see cref="StepSeparator"/> between them, and both calls' tokens added
    /// up. A polish that did not happen leaves no separator and no second half behind, so a stored
    /// prompt never describes a call that was never made.
    ///
    /// <see cref="Finish"/> runs once, here, over the merged story — the spread-count guard is
    /// about the book that will be printed, not about an intermediate draft of it.
    /// </summary>
    private MasterStoryResult FinishPolished(
        MasterStoryInput input,
        string model,
        string generatorSystem,
        string generatorUser,
        ModelResult<MasterStory> generated,
        (MasterStory Story, string PolishSystem, string PolishUser, int PromptTokens, int CompletionTokens) polished)
    {
        var polishRan = !string.IsNullOrEmpty(polished.PolishSystem);

        return Finish(
            input,
            polished.Story,
            polishRan ? generatorSystem + StepSeparator + polished.PolishSystem : generatorSystem,
            polishRan ? generatorUser + StepSeparator + polished.PolishUser : generatorUser,
            model,
            generated.PromptTokens + polished.PromptTokens,
            generated.CompletionTokens + polished.CompletionTokens);
    }

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
