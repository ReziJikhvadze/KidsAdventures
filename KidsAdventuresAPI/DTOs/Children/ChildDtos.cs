namespace AdventurePacks.Api.DTOs.Children;

public sealed class CreateChildRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(3, 18)]
    public int Age { get; set; }

    /// <summary>"avatar" or "photo" — how the child appears in illustrated scenes.</summary>
    [MaxLength(16)]
    public string? PersonalizationType { get; set; }

    /// <summary>JSON avatar config when PersonalizationType is "avatar".</summary>
    public string? AvatarConfigJson { get; set; }
}

public sealed class UpdateChildRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(3, 18)]
    public int Age { get; set; }
}

public sealed class ChildResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string? PhotoUrl { get; set; }

    public string? PersonalizationType { get; set; }

    public string? AvatarConfigJson { get; set; }

    /// <summary>Non-null once the child's one-time Pixar-style hero portrait has been generated. Fetch bytes from /api/children/{id}/hero-portrait.</summary>
    public string? HeroPortraitUrl { get; set; }

    public DateTime CreatedAt { get; set; }
}
