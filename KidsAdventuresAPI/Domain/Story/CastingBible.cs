namespace AdventurePacks.Api.Domain.Story;

/// <summary>
/// Who everyone is, and what they look like, decided once and never re-derived.
///
/// This is cached against the child rather than the book. Book four uses the same bible as
/// book one, so a child looks like themselves across the whole series and we pay the vision
/// call once. It is also the reason hair and clothing can no longer drift: illustration
/// prompts are assembled from this plus tracked state, never from whatever the prose happened
/// to mention on that page.
/// </summary>
public sealed record CastingBible
{
    /// <summary>Everyone who can appear. Exactly one has <see cref="CharacterRole.Hero"/>.</summary>
    public required IReadOnlyList<StoryCharacter> Characters { get; init; }

    /// <summary>Art direction shared by every illustration in the book.</summary>
    public required VisualDirection Visual { get; init; }

    public StoryCharacter Hero =>
        Characters.FirstOrDefault(c => c.Role == CharacterRole.Hero)
        ?? throw new InvalidOperationException("A casting bible must contain exactly one hero.");

    public StoryCharacter? Find(string id) =>
        Characters.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
}

public sealed record StoryCharacter
{
    /// <summary>Stable slug used by beats, deltas and prompts. Never shown to a reader.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }
    public required CharacterRole Role { get; init; }
    public int? Age { get; init; }

    /// <summary>girl | boy | null when it does not apply, as for an animal companion.</summary>
    public string? Gender { get; init; }

    public required CharacterAppearance Appearance { get; init; }

    /// <summary>What they wear by default. State may override it, and only via a beat.</summary>
    public required Outfit DefaultOutfit { get; init; }

    public required CharacterPersonality Personality { get; init; }

    /// <summary>
    /// How this character speaks. Without it every character writes in the author's single
    /// voice, which is the main reason generated dialogue reads as narration with quote marks
    /// around it.
    /// </summary>
    public required CharacterVoice Voice { get; init; }

    /// <summary>Silhouette cues and anything an illustrator must never change.</summary>
    public string? ArtNotes { get; init; }
}

public sealed record CharacterAppearance
{
    public required string HairColor { get; init; }
    public required string HairLength { get; init; }
    public required string HairStyle { get; init; }
    public required string EyeColor { get; init; }
    public required string SkinTone { get; init; }
    public string? FaceNotes { get; init; }
    public string? Build { get; init; }
    public string? Height { get; init; }
}

public sealed record Outfit
{
    public required string Top { get; init; }
    public required string Bottom { get; init; }
    public required string Shoes { get; init; }
    public IReadOnlyList<string> Accessories { get; init; } = [];

    /// <summary>
    /// Canonical text for prompts and for the visual hash. Ordered and lower-cased so the same
    /// outfit always produces the same string, and therefore the same hash.
    /// </summary>
    public string ToCanonicalString()
    {
        var accessories = Accessories.Count == 0
            ? "none"
            : string.Join("+", Accessories.Select(a => a.Trim().ToLowerInvariant()).OrderBy(a => a, StringComparer.Ordinal));

        return $"top:{Top.Trim().ToLowerInvariant()}|bottom:{Bottom.Trim().ToLowerInvariant()}"
               + $"|shoes:{Shoes.Trim().ToLowerInvariant()}|acc:{accessories}";
    }
}

public sealed record CharacterPersonality
{
    public required IReadOnlyList<string> Traits { get; init; }
    public required string Strength { get; init; }

    /// <summary>What they are afraid of. The engine uses this to make growth mean something.</summary>
    public required string Fear { get; init; }

    /// <summary>What they want. A character without a want cannot drive a scene.</summary>
    public required string Want { get; init; }
}

public sealed record CharacterVoice
{
    /// <summary>e.g. "eager and blurts things out", "slow, picks words carefully".</summary>
    public required string Register { get; init; }

    /// <summary>Age-appropriate vocabulary ceiling for this speaker.</summary>
    public required string Vocabulary { get; init; }

    /// <summary>Repeated turns of phrase. These are what a child starts quoting.</summary>
    public IReadOnlyList<string> Tics { get; init; } = [];
}

/// <summary>Art direction that applies to every page, so the book looks like one book.</summary>
public sealed record VisualDirection
{
    public required string Style { get; init; }
    public required string Palette { get; init; }
    public required string LightingStyle { get; init; }
    public required string Mood { get; init; }
    public IReadOnlyList<string> Motifs { get; init; } = [];
}
