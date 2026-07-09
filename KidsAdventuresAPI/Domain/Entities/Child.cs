namespace AdventurePacks.Api.Domain.Entities;

public sealed class Child
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 18)]
    public int Age { get; set; }

    [MaxLength(512)]
    public string? PhotoUrl { get; set; }

    /// <summary>How the child appears in stories: "avatar" (structured JSON) or "photo" (uploaded likeness).</summary>
    [MaxLength(16)]
    public string? PersonalizationType { get; set; }

    /// <summary>Structured avatar choices (skin, hair, outfit, etc.) — JSON, not a raster image.</summary>
    public string? AvatarConfigJson { get; set; }

    /// <summary>Cached vision description — reused when PhotoUrl matches AppearancePhotoUrl.</summary>
    public string? AppearanceDescription { get; set; }

    [MaxLength(512)]
    public string? AppearancePhotoUrl { get; set; }

    /// <summary>
    /// One-time Pixar-style 3D "traveler" portrait, auto-generated from the child's very first
    /// generated story (name/age/theme/companion — no photo needed) and reused as their avatar
    /// across every Story Path saga map.
    /// </summary>
    [MaxLength(512)]
    public string? HeroPortraitUrl { get; set; }

    /// <summary>Claim timestamp guarding against concurrent duplicate portrait generation; reset to null on failure.</summary>
    public DateTime? HeroPortraitClaimedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<FamilyMember> FamilyMembers { get; set; } = new List<FamilyMember>();
    public ICollection<AdventurePack> AdventurePacks { get; set; } = new List<AdventurePack>();
}
