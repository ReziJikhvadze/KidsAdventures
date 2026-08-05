using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Validation;

/// <summary>
/// How much a broken rule matters.
///
/// The distinction is the difference between a book that is wrong and a book that is merely
/// not as good as it could be. A vanished key makes a book feel broken to a five-year-old; a
/// slightly flat emotional curve does not. Only the first sort may stop a paid order.
/// </summary>
public enum RuleTier
{
    /// <summary>Objective correctness. Repair, and hard fail if it will not come right.</summary>
    Blocking,

    /// <summary>Craft. Repair once, then log the score and ship the book.</summary>
    Craft
}

/// <summary>
/// A single failure, phrased so it can be handed straight back to the model that caused it.
/// A boolean would be useless for repair — the model needs to know which page, which entity,
/// and what would satisfy the rule.
/// </summary>
public sealed record ValidationFinding(
    string RuleId,
    RuleTier Tier,
    int? Page,
    string Detail,
    string Fix)
{
    public override string ToString() =>
        Page is { } page
            ? $"[{RuleId}] page {page}: {Detail} — {Fix}"
            : $"[{RuleId}]: {Detail} — {Fix}";
}

public sealed class ValidationReport
{
    public required IReadOnlyList<ValidationFinding> Findings { get; init; }

    public IEnumerable<ValidationFinding> Blocking =>
        Findings.Where(f => f.Tier == RuleTier.Blocking);

    public IEnumerable<ValidationFinding> Craft =>
        Findings.Where(f => f.Tier == RuleTier.Craft);

    public bool CanShip => !Blocking.Any();
    public bool IsPerfect => Findings.Count == 0;

    /// <summary>Pages a repair should touch, so a rewrite stays surgical instead of regenerating the book.</summary>
    public IReadOnlyList<int> AffectedPages(RuleTier tier) =>
        [.. Findings.Where(f => f.Tier == tier && f.Page is not null)
            .Select(f => f.Page!.Value)
            .Distinct()
            .Order()];

    /// <summary>Findings rendered for a repair prompt.</summary>
    public string ToRepairBrief(RuleTier tier) =>
        string.Join(Environment.NewLine,
            Findings.Where(f => f.Tier == tier).Select(f => f.ToString()));

    public static ValidationReport Empty => new() { Findings = [] };
}

/// <summary>
/// Context a blueprint rule may read. Projected state is included because several rules are
/// about what is true after a page, not about what the beat happens to say.
/// </summary>
public sealed class BlueprintContext
{
    public required StoryBlueprint Blueprint { get; init; }
    public required CastingBible Casting { get; init; }
    public required IReadOnlyList<StoryState> States { get; init; }
    public required BookMeta Meta { get; init; }

    /// <summary>Surprise signatures already used by this child, so repetition across books is catchable.</summary>
    public IReadOnlyCollection<string> PreviousSurpriseSignatures { get; init; } = [];

    public StoryState? StateAt(int page) => States.FirstOrDefault(s => s.Page == page);
}

/// <summary>Context a prose rule may read. Prose is checked against the plan, never against itself.</summary>
public sealed class ProseContext
{
    public required IReadOnlyList<WrittenPage> Pages { get; init; }
    public required StoryBlueprint Blueprint { get; init; }
    public required CastingBible Casting { get; init; }
    public required IReadOnlyList<StoryState> States { get; init; }
    public required BookMeta Meta { get; init; }
}

public interface IBlueprintRule
{
    string Id { get; }
    RuleTier Tier { get; }
    IEnumerable<ValidationFinding> Check(BlueprintContext context);
}

public interface IProseRule
{
    string Id { get; }
    RuleTier Tier { get; }
    IEnumerable<ValidationFinding> Check(ProseContext context);
}
