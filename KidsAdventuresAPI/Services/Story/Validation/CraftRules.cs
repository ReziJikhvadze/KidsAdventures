using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Validation;

/// <summary>
/// CURVE — the book has to follow the emotional shape it committed to.
///
/// Declaring the curve and then checking it is stronger than merely counting distinct
/// emotions: it makes the arc intentional rather than accidentally varied.
/// </summary>
public sealed class EmotionCurveRule : IBlueprintRule
{
    public string Id => "CURVE";
    public RuleTier Tier => RuleTier.Craft;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var curve = context.Blueprint.EmotionCurve;
        var beats = context.Blueprint.Beats.OrderBy(b => b.Page).ToList();

        if (curve.Count != beats.Count)
        {
            yield return new ValidationFinding(Id, Tier, null,
                $"the declared curve has {curve.Count} steps for {beats.Count} pages",
                "declare one emotion per page");
            yield break;
        }

        for (var i = 0; i < beats.Count; i++)
        {
            if (beats[i].Emotion != curve[i])
            {
                yield return new ValidationFinding(Id, Tier, beats[i].Page,
                    $"page feels '{beats[i].Emotion}' but the curve promised '{curve[i]}'",
                    $"write this page as '{curve[i]}', or change the curve deliberately");
            }
        }
    }
}

/// <summary>R5 — the same feeling three pages running reads as flat, whatever is happening.</summary>
public sealed class EmotionDiversityRule : IBlueprintRule
{
    public string Id => "R5";
    public RuleTier Tier => RuleTier.Craft;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var beats = context.Blueprint.Beats.OrderBy(b => b.Page).ToList();

        var run = StoryScale.MaximumSameRun(beats.Count);

        for (var i = run - 1; i < beats.Count; i++)
        {
            var window = beats.Skip(i - run + 1).Take(run).Select(b => b.Emotion);
            if (window.Distinct().Count() == 1)
            {
                yield return new ValidationFinding(Id, Tier, beats[i].Page,
                    $"{run} pages in a row are '{beats[i].Emotion}'",
                    "break the run with a different feeling");
            }
        }

        if (!StoryScale.SupportsDistributionRules(beats.Count))
        {
            yield break;
        }

        var required = StoryScale.MinimumDistinctEmotions(beats.Count);
        var distinct = beats.Select(b => b.Emotion).Distinct().Count();
        if (distinct < required)
        {
            yield return new ValidationFinding(Id, Tier, null,
                $"the whole book uses only {distinct} emotions",
                $"a {beats.Count} page book needs at least {required} to stay alive");
        }
    }
}

/// <summary>
/// RHYTHM — pages must alternate in energy, not only in feeling.
///
/// Six pages of Action can all be different emotions and still read as one long chase. Energy
/// is the tempo underneath the emotion, and a book that never changes tempo is exhausting
/// however varied its feelings are.
/// </summary>
public sealed class StoryRhythmRule : IBlueprintRule
{
    public string Id => "RHYTHM";
    public RuleTier Tier => RuleTier.Craft;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var beats = context.Blueprint.Beats.OrderBy(b => b.Page).ToList();

        var run = StoryScale.MaximumSameRun(beats.Count);

        for (var i = run - 1; i < beats.Count; i++)
        {
            var window = beats.Skip(i - run + 1).Take(run).Select(b => b.Energy);
            if (window.Distinct().Count() == 1)
            {
                yield return new ValidationFinding(Id, Tier, beats[i].Page,
                    $"{run} pages in a row are '{beats[i].Energy}' — the tempo never changes",
                    "put a different energy between them; loud pages need quiet ones to land");
            }
        }

        // A book with no still page never lets the reader feel anything. A book with no quick
        // page never makes them hurry. Both are worth flagging, but only once a book is long
        // enough for the absence to be a choice rather than a consequence of its length.
        if (StoryScale.SupportsDistributionRules(beats.Count))
        {
            if (!beats.Any(b => b.Energy is NarrativeEnergy.Reflection))
            {
                yield return new ValidationFinding(Id, Tier, null,
                    "no page pauses for breath",
                    "add a Reflection page — emotion needs somewhere quiet to land");
            }

            if (!beats.Any(b => b.Energy is NarrativeEnergy.Action or NarrativeEnergy.Tension))
            {
                yield return new ValidationFinding(Id, Tier, null,
                    "nothing in this book ever moves quickly",
                    "add an Action or Tension page");
            }
        }
    }
}

/// <summary>PURPOSE — twelve pages of the same job is a pamphlet, not a story.</summary>
public sealed class PurposeDistributionRule : IBlueprintRule
{
    public string Id => "PURPOSE";
    public RuleTier Tier => RuleTier.Craft;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var beats = context.Blueprint.Beats.OrderBy(b => b.Page).ToList();

        var run = StoryScale.MaximumSameRun(beats.Count) + 1;

        for (var i = run - 1; i < beats.Count; i++)
        {
            if (beats.Skip(i - run + 1).Take(run).Select(b => b.Purpose).Distinct().Count() == 1)
            {
                yield return new ValidationFinding(Id, Tier, beats[i].Page,
                    $"{run} pages in a row all serve '{beats[i].Purpose}'",
                    "vary what these pages are for");
            }
        }

        if (StoryScale.SupportsDistributionRules(beats.Count)
            && !beats.Any(b => b.Purpose == NarrativePurpose.Comedy))
        {
            yield return new ValidationFinding(Id, Tier, null,
                "nothing in this book is funny",
                "give at least one page a Comedy purpose — children forgive most things except being bored");
        }
    }
}

/// <summary>R7 — two pages chasing the same goal is one page told twice.</summary>
public sealed class UniqueGoalsRule : IBlueprintRule
{
    public string Id => "R7";
    public RuleTier Tier => RuleTier.Craft;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var beat in context.Blueprint.Beats.OrderBy(b => b.Page))
        {
            var normalized = Normalize(beat.Goal);
            if (seen.TryGetValue(normalized, out var firstPage))
            {
                yield return new ValidationFinding(Id, Tier, beat.Page,
                    $"this page wants the same thing as page {firstPage}",
                    "give it its own goal, or fold the two pages together");
            }
            else
            {
                seen[normalized] = beat.Page;
            }
        }
    }

    private static string Normalize(string goal) =>
        new(goal.Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());
}

/// <summary>
/// R10 — a page whose only content is travel.
///
/// "She walks into the forest" is the archetype: something happens, nothing changes, and the
/// reader has spent a page for a transition.
/// </summary>
public sealed class NoFillerMovementRule : IBlueprintRule
{
    public string Id => "R10";
    public RuleTier Tier => RuleTier.Craft;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        foreach (var beat in context.Blueprint.Beats)
        {
            var onlyMoves = beat.Deltas.Count > 0
                            && beat.Deltas.All(d => d.Kind is DeltaKind.MoveToLocation
                                                        or DeltaKind.SetTimeOfDay
                                                        or DeltaKind.SetWeather);

            if (onlyMoves && beat.ObjectsIntroduced.Count == 0 && string.IsNullOrWhiteSpace(beat.Discovery))
            {
                yield return new ValidationFinding(Id, Tier, beat.Page,
                    "this page only travels — nothing is learned and nothing is gained",
                    "let something happen on the way, or arrive at the end of the previous page");
            }
        }
    }
}

/// <summary>
/// GROWTH — the hero must not end the book as the person who started it.
///
/// A child reads a story to watch someone become braver than they were. If page twelve's hero
/// is identical to page one's, the book was an incident rather than a story.
/// </summary>
public sealed class CharacterGrowthRule : IBlueprintRule
{
    public string Id => "GROWTH";
    public RuleTier Tier => RuleTier.Craft;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var states = context.States.OrderBy(s => s.Page).ToList();
        if (states.Count < 2)
        {
            yield break;
        }

        if (string.Equals(states[0].HeroTrait, states[^1].HeroTrait, StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationFinding("GROWTH", Tier, states[^1].Page,
                $"{context.Casting.Hero.Name} ends the book exactly as they began it ('{states[^1].HeroTrait}')",
                "add a HeroTraitShift delta where the hero earns the change");
        }

        var fear = context.Casting.Hero.Personality.Fear;
        if (!string.IsNullOrWhiteSpace(fear)
            && !context.Blueprint.Beats.Any(b =>
                b.Obstacle.Contains(fear, StringComparison.OrdinalIgnoreCase)
                || b.Discovery.Contains(fear, StringComparison.OrdinalIgnoreCase)))
        {
            yield return new ValidationFinding("GROWTH", Tier, null,
                $"the hero's fear ('{fear}') is never faced",
                "make one obstacle turn on it — that is where growth comes from");
        }
    }
}

/// <summary>
/// SURPRISE — every book owes the reader something it did not have to include.
///
/// Also deduplicated against this child's earlier books, which is what stops book forty
/// rediscovering book seven's good idea.
/// </summary>
public sealed class SurpriseBudgetRule : IBlueprintRule
{
    public string Id => "SURPRISE";
    public RuleTier Tier => RuleTier.Craft;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var surprises = context.Blueprint.Surprises;
        var required = StoryScale.MinimumSurprises(context.Blueprint.Beats.Count);

        if (surprises.Count < required)
        {
            yield return new ValidationFinding(Id, Tier, null,
                $"only {surprises.Count} planned surprises for {context.Blueprint.Beats.Count} pages",
                $"a book this length needs at least {required} — an unexpected character, solution, joke or image");
        }

        var pages = context.Blueprint.Beats.Select(b => b.Page).ToHashSet();

        foreach (var surprise in surprises)
        {
            if (!pages.Contains(surprise.UsedOnPage))
            {
                yield return new ValidationFinding(Id, Tier, surprise.UsedOnPage,
                    $"surprise '{surprise.Description}' is placed on a page that does not exist",
                    "put it on a real page");
            }

            if (context.PreviousSurpriseSignatures.Contains(surprise.Signature()))
            {
                yield return new ValidationFinding(Id, Tier, surprise.UsedOnPage,
                    $"this child has already had '{surprise.Description}' in an earlier book",
                    "invent a different one — the series should not repeat itself");
            }
        }
    }
}
