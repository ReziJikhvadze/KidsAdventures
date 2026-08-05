namespace AdventurePacks.Api.Domain.Story;

/// <summary>
/// The plan. Beats, never prose.
///
/// This is the object the engine argues with. It is cheap to produce, cheap to validate and
/// cheap to repair, which is the entire reason it exists: prose is expensive and, once written
/// autoregressively, cannot be reconsidered. Everything structural is settled here, while it
/// is still cheap to be wrong.
///
/// Entities are declared up front — locations, objects, cast — because a reference to an
/// undeclared entity is precisely the failure that puts papers on page three that nobody
/// introduced. Declaration is what makes that checkable rather than hopeful.
/// </summary>
public sealed record StoryBlueprint
{
    /// <summary>The question this book asks. One sentence.</summary>
    public required string Promise { get; init; }

    /// <summary>How that question is answered. Validated against the final beat.</summary>
    public required string Answer { get; init; }

    /// <summary>The emotional shape the planner committed to, in page order.</summary>
    public required IReadOnlyList<StoryEmotion> EmotionCurve { get; init; }

    public required IReadOnlyList<StoryLocation> Locations { get; init; }
    public required IReadOnlyList<StoryObject> Objects { get; init; }

    /// <summary>Character ids drawn from the casting bible.</summary>
    public required IReadOnlyList<string> Cast { get; init; }

    /// <summary>
    /// Set-ups the book owes the reader a payoff for. Both page numbers are declared here so
    /// the callback is planned rather than hoped for — a memory list guarantees nothing.
    /// </summary>
    public required IReadOnlyList<RunningThread> Threads { get; init; }

    /// <summary>
    /// The deliberate unexpectedness in this book. Declared, so "be surprising" becomes
    /// checkable, and deduplicated against the child's earlier books so book forty does not
    /// rediscover book seven's idea.
    /// </summary>
    public required IReadOnlyList<Surprise> Surprises { get; init; }

    public required IReadOnlyList<StoryBeat> Beats { get; init; }

    public StoryLocation? Location(string id) =>
        Locations.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));

    public StoryObject? Object(string id) =>
        Objects.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One page, planned. Everything except the sentences.</summary>
public sealed record StoryBeat
{
    /// <summary>1-based, matching the printed page.</summary>
    public required int Page { get; init; }

    /// <summary>What the hero is trying to do. Two beats may not share one.</summary>
    public required string Goal { get; init; }

    /// <summary>What stands in the way. A page without one is a page without a story.</summary>
    public required string Obstacle { get; init; }

    /// <summary>What changes in what anyone knows.</summary>
    public required string Discovery { get; init; }

    /// <summary>The single action an illustration can actually show.</summary>
    public required string Action { get; init; }

    public required NarrativePurpose Purpose { get; init; }
    public required StoryEmotion Emotion { get; init; }
    public required NarrativeEnergy Energy { get; init; }

    public required string LocationId { get; init; }
    public required TimeOfDay TimeOfDay { get; init; }
    public required Weather Weather { get; init; }

    public required IReadOnlyList<string> CharactersPresent { get; init; }
    public IReadOnlyList<string> ObjectsIntroduced { get; init; } = [];
    public IReadOnlyList<string> ObjectsUsed { get; init; } = [];

    /// <summary>What this page changes. Projection folds these; every page must change something.</summary>
    public required IReadOnlyList<StateDelta> Deltas { get; init; }

    /// <summary>
    /// The question this page leaves open, which the next page's goal must take up. Empty only
    /// on the final page, where an open question would be an unresolved ending.
    /// </summary>
    public string? Hook { get; init; }

    /// <summary>Thread ids this beat sets up or pays off.</summary>
    public IReadOnlyList<string> ThreadRefs { get; init; } = [];
}

/// <summary>
/// A single, closed change to story state. Closed on purpose: projection and validation both
/// have to understand every change exactly, so a beat cannot invent a kind of change the
/// engine cannot reason about.
/// </summary>
public sealed record StateDelta
{
    public required DeltaKind Kind { get; init; }

    /// <summary>Object id, location id, character id, or an enum name, depending on Kind.</summary>
    public required string Target { get; init; }

    /// <summary>Second operand where one is needed: the other party in a relationship, an outfit note.</summary>
    public string? Value { get; init; }
}

public sealed record StoryLocation
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Concrete things to see, hear and smell. What stops a place being described twice the same way.</summary>
    public required IReadOnlyList<string> SensoryAnchors { get; init; }
}

public sealed record StoryObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Why it matters. An object that cannot state this is decoration, and Chekhov rejects it.</summary>
    public required string Significance { get; init; }
}

public sealed record RunningThread
{
    public required string Id { get; init; }
    public required ThreadKind Kind { get; init; }

    /// <summary>What is planted.</summary>
    public required string Setup { get; init; }

    /// <summary>What lands later, and what the child is waiting for without knowing it.</summary>
    public required string Payoff { get; init; }

    public required int SetupPage { get; init; }
    public required int PayoffPage { get; init; }
}

public sealed record Surprise
{
    public required SurpriseKind Kind { get; init; }
    public required string Description { get; init; }
    public required int UsedOnPage { get; init; }

    /// <summary>
    /// Stable signature used to deduplicate against this child's earlier books. Lower-cased and
    /// trimmed so trivial rewording does not read as a new idea.
    /// </summary>
    public string Signature() => $"{Kind}:{Description.Trim().ToLowerInvariant()}";
}
