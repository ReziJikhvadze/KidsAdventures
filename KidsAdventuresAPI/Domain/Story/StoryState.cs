namespace AdventurePacks.Api.Domain.Story;

/// <summary>
/// The world as it stands after a given page.
///
/// Never stored and never inferred. Deltas on the beats are the only truth; this is folded
/// from them on demand. Caching it would be the bug that bites hardest — repair page five and
/// every cached snapshot after it is silently stale, which is exactly the class of error this
/// engine exists to remove.
/// </summary>
public sealed class StoryState
{
    public required int Page { get; init; }
    public required string LocationId { get; init; }
    public required TimeOfDay TimeOfDay { get; init; }
    public required Weather Weather { get; init; }

    /// <summary>Object ids the hero is carrying. The golden key lives here, so it cannot vanish.</summary>
    public required IReadOnlyList<string> Inventory { get; init; }

    /// <summary>Character ids travelling with the hero right now.</summary>
    public required IReadOnlyList<string> Companions { get; init; }

    /// <summary>What each character is wearing, overriding the bible default only where a beat said so.</summary>
    public required IReadOnlyDictionary<string, Outfit> Outfits { get; init; }

    public required StoryEmotion HeroEmotion { get; init; }

    /// <summary>The hero's trait right now. Growth is measured by this changing.</summary>
    public required string HeroTrait { get; init; }

    public required IReadOnlyList<string> OpenQuestions { get; init; }
    public required IReadOnlyList<string> ResolvedQuestions { get; init; }

    /// <summary>Relationship status keyed by an ordered character pair, e.g. "tamar|rex".</summary>
    public required IReadOnlyDictionary<string, string> Relationships { get; init; }

    public static string RelationshipKey(string a, string b)
    {
        var left = a.Trim().ToLowerInvariant();
        var right = b.Trim().ToLowerInvariant();
        return string.CompareOrdinal(left, right) <= 0 ? $"{left}|{right}" : $"{right}|{left}";
    }
}

/// <summary>
/// What later books remember about earlier ones.
///
/// This is what makes a shelf feel like a series rather than a pile. A companion who had a
/// habit in book one still has it in book four; a catchphrase a child started repeating comes
/// back. None of it is left to the model to recall — it is written down and handed forward.
/// </summary>
public sealed class StoryMemory
{
    public IReadOnlyList<string> ActivePromises { get; init; } = [];
    public IReadOnlyList<string> ActiveMysteries { get; init; } = [];

    /// <summary>Jokes that landed and can be called back without explanation.</summary>
    public IReadOnlyList<string> RunningJokes { get; init; } = [];

    /// <summary>Lines a character says often enough that the child expects them.</summary>
    public IReadOnlyList<string> Catchphrases { get; init; } = [];

    /// <summary>Small consistent behaviours: how a companion eats, what it always forgets.</summary>
    public IReadOnlyList<string> CompanionHabits { get; init; } = [];

    /// <summary>What each character has already learned, so book four does not teach it again.</summary>
    public IReadOnlyList<string> CharacterLessons { get; init; } = [];

    /// <summary>Moments worth referring back to for an emotional callback.</summary>
    public IReadOnlyList<string> EmotionalCallbacks { get; init; } = [];

    /// <summary>Visual ideas this series has made its own.</summary>
    public IReadOnlyList<string> VisualMotifs { get; init; } = [];

    /// <summary>Themes that have worked for this child before.</summary>
    public IReadOnlyList<string> EmotionalThemes { get; init; } = [];

    /// <summary>Deliberately planted for a future book to pick up.</summary>
    public IReadOnlyList<string> FutureSeeds { get; init; } = [];

    public static StoryMemory Empty => new();
}

/// <summary>
/// What this book was told to be about before planning began.
///
/// Version one already injected four random seeds and the stories still felt alike, because
/// seeds handed to a writer only decorate sentences. These are handed to the planner, which
/// must build structure around them, and they are drawn from a pool that excludes what this
/// child has already had.
/// </summary>
public sealed class Inspiration
{
    public required string WonderSeed { get; init; }
    public required string HumorSeed { get; init; }
    public required string VisualSeed { get; init; }
    public required string EmotionalSeed { get; init; }
    public required string MysterySeed { get; init; }

    public IEnumerable<string> All()
    {
        yield return WonderSeed;
        yield return HumorSeed;
        yield return VisualSeed;
        yield return EmotionalSeed;
        yield return MysterySeed;
    }
}
