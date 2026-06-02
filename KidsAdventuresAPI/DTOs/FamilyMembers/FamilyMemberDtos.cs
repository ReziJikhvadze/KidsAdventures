namespace AdventurePacks.Api.DTOs.FamilyMembers;

public sealed class CreateFamilyMemberRequest
{
    [Required]
    public Guid ChildId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Relationship { get; set; } = string.Empty;
}

public sealed class UpdateFamilyMemberRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Relationship { get; set; } = string.Empty;
}

public sealed class FamilyMemberResponse
{
    public Guid Id { get; set; }
    public Guid ChildId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}
