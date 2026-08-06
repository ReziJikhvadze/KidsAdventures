namespace AdventurePacks.Api.Domain.Story;

/// <summary>
/// What the architect call decides before a word of the story is written.
///
/// The plan exists to settle the things a writer cannot be trusted to keep straight while also
/// writing: who is in the book, when each of them is allowed to appear, what the hero physically
/// does in every scene, and the phrase that recurs. Secondary characters turning up in scene one
/// with no introduction is what this is for — a writer holding eight scenes in mind will reach
/// for a companion it has not introduced, and no amount of asking it not to has fixed that.
/// </summary>
public sealed record StoryPlan
{
    public required string StoryTitle { get; init; }

    /// <summary>Two to four Georgian words, recurring three times.</summary>
    public required string RefrainPhrase { get; init; }

    /// <summary>English. Written once here and quoted into every illustration prompt by code.</summary>
    public required string CharacterLock { get; init; }

    public required IReadOnlyList<PlannedCharacter> CharacterManifest { get; init; }

    public required IReadOnlyList<PlannedSpread> Outline { get; init; }
}

public sealed record PlannedCharacter
{
    public required string Name { get; init; }
    public required string Role { get; init; }

    /// <summary>
    /// The first scene this character may appear in. The writer is given this and told the name
    /// must not occur before it, which is the only thing that has stopped companions materialising
    /// on page one.
    /// </summary>
    public required int IntroducedInSpread { get; init; }
}

public sealed record PlannedSpread
{
    public required int SpreadNumber { get; init; }
    public required string Title { get; init; }

    /// <summary>One short Georgian sentence. The plot, not the prose.</summary>
    public required string PlotSummary { get; init; }

    /// <summary>What the child physically does — the skill, shown rather than named.</summary>
    public required string ChildAction { get; init; }
}
