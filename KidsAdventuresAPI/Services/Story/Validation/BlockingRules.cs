using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Validation;

/// <summary>
/// R12 — every referenced entity was declared.
///
/// Runs first because the other rules assume ids resolve. An undeclared id is not a story
/// problem, it is a plan that cannot be reasoned about at all.
/// </summary>
public sealed class DeclaredEntitiesRule : IBlueprintRule
{
    public string Id => "R12";
    public RuleTier Tier => RuleTier.Blocking;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var blueprint = context.Blueprint;

        foreach (var beat in blueprint.Beats)
        {
            if (blueprint.Location(beat.LocationId) is null)
            {
                yield return new ValidationFinding(Id, Tier, beat.Page,
                    $"location '{beat.LocationId}' is not declared",
                    "declare it in Locations or use one that exists");
            }

            foreach (var characterId in beat.CharactersPresent)
            {
                if (context.Casting.Find(characterId) is null)
                {
                    yield return new ValidationFinding(Id, Tier, beat.Page,
                        $"character '{characterId}' is not in the casting bible",
                        "use a cast member, or drop them from this page");
                }
            }

            foreach (var objectId in beat.ObjectsIntroduced.Concat(beat.ObjectsUsed))
            {
                if (blueprint.Object(objectId) is null)
                {
                    yield return new ValidationFinding(Id, Tier, beat.Page,
                        $"object '{objectId}' is not declared",
                        "declare it in Objects with its significance, or remove the reference");
                }
            }
        }
    }
}

/// <summary>
/// R1 — an object may not be used before it is introduced.
///
/// This is the rule that stops papers appearing on page three that nobody put there. It is
/// the single most common way a generated story stops making sense, and it is trivially
/// decidable once objects are declared.
/// </summary>
public sealed class ObjectIntroducedBeforeUseRule : IBlueprintRule
{
    public string Id => "R1";
    public RuleTier Tier => RuleTier.Blocking;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var introduced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var beat in context.Blueprint.Beats.OrderBy(b => b.Page))
        {
            foreach (var objectId in beat.ObjectsIntroduced)
            {
                introduced.Add(objectId);
            }

            foreach (var objectId in beat.ObjectsUsed.Where(o => !introduced.Contains(o)))
            {
                yield return new ValidationFinding(Id, Tier, beat.Page,
                    $"object '{objectId}' is used but was never introduced",
                    $"introduce '{objectId}' on this page or an earlier one");
            }
        }
    }
}

/// <summary>
/// R2 / Chekhov — an object that is introduced must go on to matter.
///
/// Reference alone is not enough. A key that is found, mentioned once and forgotten is the
/// same disappointment to a child as a key that vanishes: it was made to look important and
/// then was not. So the payoff has to be a discovery or a state change, not a sighting.
/// </summary>
public sealed class ChekhovRule : IBlueprintRule
{
    public string Id => "CHEKHOV";
    public RuleTier Tier => RuleTier.Blocking;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var beats = context.Blueprint.Beats.OrderBy(b => b.Page).ToList();

        foreach (var beat in beats)
        {
            foreach (var objectId in beat.ObjectsIntroduced)
            {
                // Strictly later. The delta that puts the object in the hero's hand is part of
                // introducing it, so counting it as the payoff would let every object satisfy
                // Chekhov by merely existing — which is the failure this rule is here to catch.
                var mattersLater = beats
                    .Where(b => b.Page > beat.Page)
                    .Any(b =>
                        b.ObjectsUsed.Contains(objectId, StringComparer.OrdinalIgnoreCase)
                        || b.Deltas.Any(d => string.Equals(d.Target, objectId, StringComparison.OrdinalIgnoreCase))
                        || b.Discovery.Contains(objectId, StringComparison.OrdinalIgnoreCase));

                if (!mattersLater)
                {
                    yield return new ValidationFinding(Id, Tier, beat.Page,
                        $"object '{objectId}' is introduced but never becomes meaningful",
                        $"give '{objectId}' a later payoff — a discovery or a state change — or remove it");
                }
            }
        }
    }
}

/// <summary>
/// R3 — a character may not silently vanish and reappear.
///
/// The fox that is present, gone for six pages and suddenly back reads as a mistake, because
/// it is one. Leaving is allowed; leaving without the story noticing is not.
/// </summary>
public sealed class CharacterContinuityRule : IBlueprintRule
{
    public string Id => "R3";
    public RuleTier Tier => RuleTier.Blocking;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var beats = context.Blueprint.Beats.OrderBy(b => b.Page).ToList();
        var heroId = context.Casting.Hero.Id;

        var everyone = beats
            .SelectMany(b => b.CharactersPresent)
            .Where(id => !string.Equals(id, heroId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var characterId in everyone)
        {
            var pages = beats
                .Where(b => b.CharactersPresent.Contains(characterId, StringComparer.OrdinalIgnoreCase))
                .Select(b => b.Page)
                .Order()
                .ToList();

            for (var i = 1; i < pages.Count; i++)
            {
                var gapStart = pages[i - 1];
                var gapEnd = pages[i];
                if (gapEnd - gapStart <= 1)
                {
                    continue;
                }

                var departed = beats.First(b => b.Page == gapStart).Deltas
                    .Any(d => d.Kind == DeltaKind.CompanionLeaves
                              && string.Equals(d.Target, characterId, StringComparison.OrdinalIgnoreCase));

                var returned = beats.First(b => b.Page == gapEnd).Deltas
                    .Any(d => d.Kind == DeltaKind.CompanionJoins
                              && string.Equals(d.Target, characterId, StringComparison.OrdinalIgnoreCase));

                if (!departed || !returned)
                {
                    yield return new ValidationFinding(Id, Tier, gapEnd,
                        $"'{characterId}' is absent from page {gapStart + 1} to {gapEnd - 1} without leaving or returning",
                        $"add a CompanionLeaves delta on page {gapStart} and a CompanionJoins delta on page {gapEnd}, or keep them present");
                }
            }
        }
    }
}

/// <summary>
/// R4 — the story may not teleport.
///
/// A location change has to be something that happens, not something that has happened between
/// pages. Either the beat moves the hero, or the beat stays where it was.
/// </summary>
public sealed class LocationTransitionRule : IBlueprintRule
{
    public string Id => "R4";
    public RuleTier Tier => RuleTier.Blocking;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var beats = context.Blueprint.Beats.OrderBy(b => b.Page).ToList();

        for (var i = 1; i < beats.Count; i++)
        {
            var previous = beats[i - 1];
            var current = beats[i];

            if (string.Equals(previous.LocationId, current.LocationId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var moved = current.Deltas.Any(d => d.Kind == DeltaKind.MoveToLocation)
                        || previous.Deltas.Any(d => d.Kind == DeltaKind.MoveToLocation);

            if (!moved)
            {
                yield return new ValidationFinding(Id, Tier, current.Page,
                    $"location changes from '{previous.LocationId}' to '{current.LocationId}' with no movement",
                    "add a MoveToLocation delta, or keep the previous location");
            }
        }
    }
}

/// <summary>
/// R6 — every page must change something.
///
/// This is the rule that removes filler. If a page leaves the world exactly as it found it,
/// it can be deleted without anyone noticing, and a page nobody would miss should not be in a
/// book a parent paid for.
/// </summary>
public sealed class EveryPageChangesStateRule : IBlueprintRule
{
    public string Id => "R6";
    public RuleTier Tier => RuleTier.Blocking;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        foreach (var beat in context.Blueprint.Beats.Where(b => b.Deltas.Count == 0))
        {
            yield return new ValidationFinding(Id, Tier, beat.Page,
                "this page changes nothing, so removing it would cost the story nothing",
                "give it a real consequence, or cut it and let another page breathe");
        }
    }
}

/// <summary>THREADS — every set-up is paid off, and paid off after it is planted.</summary>
public sealed class RunningThreadRule : IBlueprintRule
{
    public string Id => "THREADS";
    public RuleTier Tier => RuleTier.Blocking;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var beats = context.Blueprint.Beats.ToDictionary(b => b.Page);

        foreach (var thread in context.Blueprint.Threads)
        {
            if (thread.PayoffPage <= thread.SetupPage)
            {
                yield return new ValidationFinding(Id, Tier, thread.PayoffPage,
                    $"thread '{thread.Id}' pays off on page {thread.PayoffPage}, at or before its set-up on {thread.SetupPage}",
                    "move the payoff to a later page");
                continue;
            }

            foreach (var page in new[] { thread.SetupPage, thread.PayoffPage })
            {
                if (!beats.TryGetValue(page, out var beat))
                {
                    yield return new ValidationFinding(Id, Tier, page,
                        $"thread '{thread.Id}' refers to page {page}, which does not exist",
                        "point the thread at a real page");
                    continue;
                }

                if (!beat.ThreadRefs.Contains(thread.Id, StringComparer.OrdinalIgnoreCase))
                {
                    yield return new ValidationFinding(Id, Tier, page,
                        $"page {page} carries thread '{thread.Id}' but does not reference it",
                        $"add '{thread.Id}' to this page's ThreadRefs, so the writer knows to plant or land it");
                }
            }
        }
    }
}

/// <summary>
/// R8 — the book must answer the question it asked.
///
/// A story that raises a promise and drifts to a stop is the "boring ending" complaint in its
/// purest form. The last page has to be about the thing the first page made us care about.
/// </summary>
public sealed class PromiseResolvedRule : IBlueprintRule
{
    public string Id => "R8";
    public RuleTier Tier => RuleTier.Blocking;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var beats = context.Blueprint.Beats.OrderBy(b => b.Page).ToList();
        if (beats.Count == 0)
        {
            yield break;
        }

        var final = beats[^1];

        if (final.Purpose is not (NarrativePurpose.Resolution or NarrativePurpose.Victory))
        {
            yield return new ValidationFinding(Id, Tier, final.Page,
                $"the book ends on a '{final.Purpose}' page",
                "end on Resolution or Victory — the last page is what the child is left holding");
        }

        var lastState = context.StateAt(final.Page);
        if (lastState is not null && lastState.OpenQuestions.Count > 0)
        {
            yield return new ValidationFinding(Id, Tier, final.Page,
                $"the book ends with unanswered questions: {string.Join(", ", lastState.OpenQuestions)}",
                "resolve them before the end, or never open them");
        }

        if (!string.IsNullOrWhiteSpace(final.Hook))
        {
            yield return new ValidationFinding(Id, Tier, final.Page,
                "the final page leaves a hook dangling",
                "the last page closes the book; move the hook earlier");
        }
    }
}

/// <summary>
/// R9 — pages must be linked, not merely adjacent.
///
/// Disconnected scenes are what makes a book feel like a slideshow. Every page owes the next
/// one a reason to be turned.
/// </summary>
public sealed class HookChainRule : IBlueprintRule
{
    public string Id => "R9";
    public RuleTier Tier => RuleTier.Blocking;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var beats = context.Blueprint.Beats.OrderBy(b => b.Page).ToList();

        for (var i = 0; i < beats.Count - 1; i++)
        {
            if (string.IsNullOrWhiteSpace(beats[i].Hook))
            {
                yield return new ValidationFinding(Id, Tier, beats[i].Page,
                    "this page gives no reason to turn it",
                    "end it on a question the next page takes up");
            }
        }
    }
}

/// <summary>
/// VISUAL — a character's look may only change when the story changes it.
///
/// Clothing and hair drift is the visual twin of the vanishing key, and it is caught here
/// rather than after twelve images have been paid for.
/// </summary>
public sealed class VisualContinuityRule : IBlueprintRule
{
    public string Id => "VISUAL";
    public RuleTier Tier => RuleTier.Blocking;

    public IEnumerable<ValidationFinding> Check(BlueprintContext context)
    {
        var hero = context.Casting.Hero;
        var states = context.States.OrderBy(s => s.Page).ToList();
        var beats = context.Blueprint.Beats.ToDictionary(b => b.Page);

        for (var i = 1; i < states.Count; i++)
        {
            var previous = states[i - 1];
            var current = states[i];

            if (!previous.Outfits.TryGetValue(hero.Id, out var before)
                || !current.Outfits.TryGetValue(hero.Id, out var after))
            {
                continue;
            }

            var beforeHash = VisualHash.For(hero, before);
            var afterHash = VisualHash.For(hero, after);
            if (beforeHash.Value == afterHash.Value)
            {
                continue;
            }

            var changed = beats.TryGetValue(current.Page, out var beat)
                          && beat.Deltas.Any(d => d.Kind == DeltaKind.ChangeOutfit
                                                  && string.Equals(d.Target, hero.Id, StringComparison.OrdinalIgnoreCase));

            if (!changed)
            {
                yield return new ValidationFinding(Id, Tier, current.Page,
                    $"{hero.Name}'s appearance changes with nothing in the story to explain it",
                    "add a ChangeOutfit delta if the change is intended, otherwise leave the outfit alone");
            }
        }
    }
}
