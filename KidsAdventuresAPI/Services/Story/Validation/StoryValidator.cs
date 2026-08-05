using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Validation;

/// <summary>
/// Runs the rules. Nothing else.
///
/// This is the engine's answer to "code is responsible for truth". Every judgement it makes is
/// a pure function of the plan, decidable, repeatable and unit-testable — no model is asked
/// whether the key is still in the hero's pocket, because a model's answer would only usually
/// be right, and usually is what shipped the bugs in the first place.
/// </summary>
public interface IStoryValidator
{
    ValidationReport ValidateBlueprint(BlueprintContext context);
    ValidationReport ValidateProse(ProseContext context);
}

public sealed class StoryValidator : IStoryValidator
{
    private readonly IReadOnlyList<IBlueprintRule> _blueprintRules;
    private readonly IReadOnlyList<IProseRule> _proseRules;

    public StoryValidator()
        : this(DefaultBlueprintRules(), DefaultProseRules())
    {
    }

    /// <summary>Injectable so tests can exercise one rule at a time against a fixture.</summary>
    public StoryValidator(IReadOnlyList<IBlueprintRule> blueprintRules, IReadOnlyList<IProseRule> proseRules)
    {
        _blueprintRules = blueprintRules;
        _proseRules = proseRules;
    }

    public static IReadOnlyList<IBlueprintRule> DefaultBlueprintRules() =>
    [
        // Blocking: a book that breaks one of these is not a book, it is a bug with pictures.
        new DeclaredEntitiesRule(),
        new ObjectIntroducedBeforeUseRule(),
        new ChekhovRule(),
        new CharacterContinuityRule(),
        new LocationTransitionRule(),
        new EveryPageChangesStateRule(),
        new RunningThreadRule(),
        new PromiseResolvedRule(),
        new HookChainRule(),
        new VisualContinuityRule(),

        // Craft: these make a book better, and none of them may stop it shipping.
        new EmotionCurveRule(),
        new EmotionDiversityRule(),
        new StoryRhythmRule(),
        new PurposeDistributionRule(),
        new UniqueGoalsRule(),
        new NoFillerMovementRule(),
        new CharacterGrowthRule(),
        new SurpriseBudgetRule()
    ];

    public static IReadOnlyList<IProseRule> DefaultProseRules() =>
    [
        new WriterMayNotAlterRule(),
        new ProseInventoryRule(),
        new ScriptPurityRule(),
        new PageLengthRule(),
        new RepetitionRule(),
        new DialogueCoverageRule(),
        new NoGenericOpeningRule()
    ];

    public ValidationReport ValidateBlueprint(BlueprintContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Run(_blueprintRules, rule => rule.Check(context), rule => rule.Id);
    }

    public ValidationReport ValidateProse(ProseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Run(_proseRules, rule => rule.Check(context), rule => rule.Id);
    }

    /// <summary>
    /// A rule that throws is a bug in the rule, never a reason to fail the book. It is recorded
    /// as a finding so it surfaces in analytics rather than disappearing, and the remaining
    /// rules still run — one broken rule must not blind the whole validator.
    /// </summary>
    private static ValidationReport Run<TRule>(
        IReadOnlyList<TRule> rules,
        Func<TRule, IEnumerable<ValidationFinding>> check,
        Func<TRule, string> id)
    {
        var findings = new List<ValidationFinding>();

        foreach (var rule in rules)
        {
            try
            {
                findings.AddRange(check(rule));
            }
            catch (Exception ex)
            {
                findings.Add(new ValidationFinding(
                    id(rule),
                    RuleTier.Craft,
                    null,
                    $"the rule itself failed: {ex.Message}",
                    "this is an engine bug, not a story problem"));
            }
        }

        return new ValidationReport
        {
            Findings = [.. findings.OrderBy(f => f.Tier).ThenBy(f => f.Page ?? int.MaxValue).ThenBy(f => f.RuleId, StringComparer.Ordinal)]
        };
    }
}
