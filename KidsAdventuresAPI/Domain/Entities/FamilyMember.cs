namespace AdventurePacks.Api.Domain.Entities;

public sealed class FamilyMember
{
    public Guid Id { get; set; }
    public Guid ChildId { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Relationship { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? PhotoUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Child? Child { get; set; }
}
