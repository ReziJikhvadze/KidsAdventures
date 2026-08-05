namespace AdventurePacks.Api.Domain.Story;

/// <summary>
/// What a page is FOR, structurally. Deliberately separate from <see cref="StoryEmotion"/>:
/// a Comedy beat and a Danger beat can both read as excitement, so one axis cannot police
/// pacing on its own. Purpose answers "why does this page exist", emotion answers "how does
/// it feel", and the validator checks both independently.
/// </summary>
public enum NarrativePurpose
{
    Hook,
    Discovery,
    Relationship,
    Comedy,
    Puzzle,
    Danger,
    Reflection,
    Victory,
    Twist,
    Resolution
}

/// <summary>
/// The emotional colour of a beat. An enum rather than free text because variety has to be
/// countable — "curious" and "curiosity" would defeat every diversity rule if this were a
/// string.
/// </summary>
public enum StoryEmotion
{
    Wonder,
    Curiosity,
    Excitement,
    Fear,
    Suspense,
    Sadness,
    Hope,
    Relief,
    Joy,
    Pride,
    Courage,
    Kindness,
    Triumph
}

/// <summary>
/// The physical energy of a page — how it moves, not what it means. Used by the rhythm rule
/// to stop a book settling into six pages of the same tempo, which reads as flat even when
/// the emotions on paper differ.
/// </summary>
public enum NarrativeEnergy
{
    /// <summary>Something happens: running, climbing, chasing, escaping.</summary>
    Action,

    /// <summary>The hero notices, learns, or realises something.</summary>
    Discovery,

    /// <summary>Talk, feeling, or stillness. The breath between louder pages.</summary>
    Reflection,

    /// <summary>Play, mischief, a joke landing.</summary>
    Humor,

    /// <summary>Threat, risk, the floor tilting.</summary>
    Tension,

    /// <summary>Awe. The page where the child stops and looks.</summary>
    Wonder
}

/// <summary>A thread the book promises to pay off before it ends.</summary>
public enum ThreadKind
{
    /// <summary>A line or habit set up early and called back later — the payoff a child laughs at.</summary>
    Joke,

    /// <summary>Something a character promises to do.</summary>
    Promise,

    /// <summary>A question the book raises and must answer.</summary>
    Mystery,

    /// <summary>Something a character learns, which must visibly change them.</summary>
    Lesson
}

/// <summary>The kind of unexpectedness a planned surprise provides.</summary>
public enum SurpriseKind
{
    Character,
    Solution,
    Joke,
    Image
}

public enum CharacterRole
{
    Hero,
    Companion,
    Guest
}

/// <summary>
/// The operations a beat may perform on story state. Closed on purpose: state changes are the
/// one thing the engine must be able to reason about exactly, so a beat cannot invent a new
/// kind of change that projection and validation would not understand.
/// </summary>
public enum DeltaKind
{
    AddToInventory,
    RemoveFromInventory,
    MoveToLocation,
    CompanionJoins,
    CompanionLeaves,
    ChangeOutfit,
    SetTimeOfDay,
    SetWeather,
    OpenQuestion,
    ResolveQuestion,
    ChangeRelationship,
    HeroTraitShift
}

public enum TimeOfDay
{
    Dawn,
    Morning,
    Midday,
    Afternoon,
    Dusk,
    Night
}

public enum Weather
{
    Clear,
    Cloudy,
    Rain,
    Snow,
    Fog,
    Storm,
    Windy
}
