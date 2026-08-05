using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// Folds beats into state, one page at a time.
///
/// This is the piece the architecture most depends on being code. Asking a model to "update
/// the state after each page" would reintroduce the exact unreliability the engine exists to
/// remove: the inventory would be right most of the time, which is another way of saying the
/// key would sometimes vanish. A fold is deterministic, instant, free, and cannot hallucinate.
///
/// Nothing here is cached. Projection is a pure function of the blueprint, so a repaired beat
/// simply produces different state the next time anyone asks. A cache would go stale behind a
/// repair and be wrong in silence, which is worse than being slow.
/// </summary>
public static class StateProjector
{
    /// <summary>
    /// State after every page, index 0 being page 1. Deltas naming unknown entities are ignored
    /// rather than throwing: validation reports those precisely, and projection's job is to be
    /// total so a broken blueprint can still be described instead of crashing the report.
    /// </summary>
    public static IReadOnlyList<StoryState> Project(StoryBlueprint blueprint, CastingBible casting)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(casting);

        var hero = casting.Hero;
        var states = new List<StoryState>(blueprint.Beats.Count);

        var location = blueprint.Beats.FirstOrDefault()?.LocationId ?? string.Empty;
        var timeOfDay = blueprint.Beats.FirstOrDefault()?.TimeOfDay ?? TimeOfDay.Morning;
        var weather = blueprint.Beats.FirstOrDefault()?.Weather ?? Weather.Clear;
        var heroTrait = hero.Personality.Traits.FirstOrDefault() ?? "curious";

        var inventory = new List<string>();
        var companions = new List<string>();
        var openQuestions = new List<string>();
        var resolvedQuestions = new List<string>();
        var relationships = new Dictionary<string, string>(StringComparer.Ordinal);
        var outfits = casting.Characters.ToDictionary(
            c => c.Id,
            c => c.DefaultOutfit,
            StringComparer.OrdinalIgnoreCase);

        foreach (var beat in blueprint.Beats.OrderBy(b => b.Page))
        {
            // The beat's own declarations set the frame for the page; deltas then move things
            // on from there. Declaring both means a page can state where it is without having
            // to spend a delta saying so.
            location = string.IsNullOrWhiteSpace(beat.LocationId) ? location : beat.LocationId;
            timeOfDay = beat.TimeOfDay;
            weather = beat.Weather;

            companions = beat.CharactersPresent
                .Where(id => !string.Equals(id, hero.Id, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var delta in beat.Deltas)
            {
                Apply(delta, inventory, companions, outfits, openQuestions, resolvedQuestions,
                    relationships, hero.Id, ref location, ref timeOfDay, ref weather, ref heroTrait);
            }

            states.Add(new StoryState
            {
                Page = beat.Page,
                LocationId = location,
                TimeOfDay = timeOfDay,
                Weather = weather,
                Inventory = [.. inventory],
                Companions = [.. companions],
                Outfits = new Dictionary<string, Outfit>(outfits, StringComparer.OrdinalIgnoreCase),
                HeroEmotion = beat.Emotion,
                HeroTrait = heroTrait,
                OpenQuestions = [.. openQuestions],
                ResolvedQuestions = [.. resolvedQuestions],
                Relationships = new Dictionary<string, string>(relationships, StringComparer.Ordinal)
            });
        }

        return states;
    }

    private static void Apply(
        StateDelta delta,
        List<string> inventory,
        List<string> companions,
        Dictionary<string, Outfit> outfits,
        List<string> openQuestions,
        List<string> resolvedQuestions,
        Dictionary<string, string> relationships,
        string heroId,
        ref string location,
        ref TimeOfDay timeOfDay,
        ref Weather weather,
        ref string heroTrait)
    {
        var target = delta.Target?.Trim() ?? string.Empty;
        if (target.Length == 0)
        {
            return;
        }

        switch (delta.Kind)
        {
            case DeltaKind.AddToInventory:
                if (!inventory.Contains(target, StringComparer.OrdinalIgnoreCase))
                {
                    inventory.Add(target);
                }
                break;

            case DeltaKind.RemoveFromInventory:
                inventory.RemoveAll(i => string.Equals(i, target, StringComparison.OrdinalIgnoreCase));
                break;

            case DeltaKind.MoveToLocation:
                location = target;
                break;

            case DeltaKind.CompanionJoins:
                if (!string.Equals(target, heroId, StringComparison.OrdinalIgnoreCase)
                    && !companions.Contains(target, StringComparer.OrdinalIgnoreCase))
                {
                    companions.Add(target);
                }
                break;

            case DeltaKind.CompanionLeaves:
                companions.RemoveAll(c => string.Equals(c, target, StringComparison.OrdinalIgnoreCase));
                break;

            case DeltaKind.ChangeOutfit:
                // A wardrobe change is the only legitimate reason a character's visual hash may
                // differ from the previous page, so it must be an explicit beat rather than
                // something the prose mentions in passing.
                if (outfits.TryGetValue(target, out var current) && !string.IsNullOrWhiteSpace(delta.Value))
                {
                    outfits[target] = new Outfit
                    {
                        Top = current.Top,
                        Bottom = current.Bottom,
                        Shoes = current.Shoes,
                        Accessories = [.. current.Accessories, delta.Value!.Trim()]
                    };
                }
                break;

            case DeltaKind.SetTimeOfDay:
                if (Enum.TryParse<TimeOfDay>(target, ignoreCase: true, out var parsedTime))
                {
                    timeOfDay = parsedTime;
                }
                break;

            case DeltaKind.SetWeather:
                if (Enum.TryParse<Weather>(target, ignoreCase: true, out var parsedWeather))
                {
                    weather = parsedWeather;
                }
                break;

            case DeltaKind.OpenQuestion:
                if (!openQuestions.Contains(target, StringComparer.OrdinalIgnoreCase))
                {
                    openQuestions.Add(target);
                }
                break;

            case DeltaKind.ResolveQuestion:
                openQuestions.RemoveAll(q => string.Equals(q, target, StringComparison.OrdinalIgnoreCase));
                if (!resolvedQuestions.Contains(target, StringComparer.OrdinalIgnoreCase))
                {
                    resolvedQuestions.Add(target);
                }
                break;

            case DeltaKind.ChangeRelationship:
                if (!string.IsNullOrWhiteSpace(delta.Value))
                {
                    relationships[StoryState.RelationshipKey(heroId, target)] = delta.Value!.Trim();
                }
                break;

            case DeltaKind.HeroTraitShift:
                heroTrait = target;
                break;

            default:
                break;
        }
    }
}
