namespace AdventurePacks.Api.Domain.Entities;

/// <summary>
/// Anyone who can star in a book: the child hero, a sibling, a grandparent, the
/// family dog, or an invented friend. Replaces the old <c>Child</c> plus
/// <c>FamilyMember</c> split, which could not express a book with three equal leads.
/// </summary>
public sealed class Character
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Drives the age band the story is written for. Required for a hero.</summary>
    public DateOnly? BirthDate { get; set; }

    /// <summary>"girl" or "boy"; see <see cref="CharacterTraits"/>.</summary>
    public string? Gender { get; set; }

    public string? EyeColor { get; set; }

    public string CharacterType { get; set; } = CharacterTraits.TypeChild;

    /// <summary>How this character relates to the hero. Free text, chosen from chips in the UI.</summary>
    public string? Relationship { get; set; }

    /// <summary>A hero: owns an adventure map and a series of their own.</summary>
    public bool IsPrimary { get; set; }

    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Cached description derived from the portrait, so every illustration in every book
    /// renders the same face instead of paying for a fresh vision call each time.
    /// </summary>
    public string? AppearanceDescription { get; set; }

    /// <summary>The photo the cached description was derived from; a new photo invalidates it.</summary>
    public string? AppearancePhotoUrl { get; set; }

    public Guid? LegacyChildId { get; set; }
    public Guid? LegacyFamilyMemberId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whole years as of today, or null when no birth date was given.</summary>
    public int? AgeYears => AgeYearsOn(DateOnly.FromDateTime(DateTime.UtcNow));

    public int? AgeYearsOn(DateOnly today)
    {
        if (BirthDate is not { } birthDate)
        {
            return null;
        }

        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age))
        {
            age--;
        }

        return age < 0 ? 0 : age;
    }
}
