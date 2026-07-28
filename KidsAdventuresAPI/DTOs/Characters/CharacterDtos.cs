namespace AdventurePacks.Api.DTOs.Characters;

/// <summary>
/// Create/update payload. Sent as multipart when a portrait comes with it, which is
/// why every field is a simple form-bindable scalar.
/// </summary>
public sealed class SaveCharacterRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>ISO date. Required for a hero, since it sets the age band the story is written for.</summary>
    public DateOnly? BirthDate { get; set; }

    /// <summary>"girl" or "boy". Required for child and adult characters.</summary>
    [MaxLength(16)]
    public string? Gender { get; set; }

    /// <summary>"brown", "blue", "green" or "grey".</summary>
    [MaxLength(24)]
    public string? EyeColor { get; set; }

    /// <summary>"child", "adult", "animal" or "fantasy".</summary>
    [MaxLength(16)]
    public string CharacterType { get; set; } = "child";

    /// <summary>How this character relates to the hero. Required for supporting characters.</summary>
    [MaxLength(100)]
    public string? Relationship { get; set; }

    /// <summary>True for a hero, who gets their own adventure map and series.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>Set on update to drop the stored portrait without uploading a replacement.</summary>
    public bool RemovePhoto { get; set; }
}

public sealed class CharacterResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly? BirthDate { get; set; }

    /// <summary>Whole years today, derived from <see cref="BirthDate"/>.</summary>
    public int? Age { get; set; }

    public string? Gender { get; set; }
    public string? EyeColor { get; set; }
    public string CharacterType { get; set; } = "child";
    public string? Relationship { get; set; }
    public bool IsPrimary { get; set; }
    public string? PhotoUrl { get; set; }

    /// <summary>True once a portrait has been analysed, so illustrations stay face-consistent.</summary>
    public bool HasAppearanceProfile { get; set; }

    /// <summary>False while the character still appears in a book, which blocks deletion.</summary>
    public bool CanDelete { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
