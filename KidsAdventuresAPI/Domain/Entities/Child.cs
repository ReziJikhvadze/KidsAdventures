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

    /// <summary>Cached vision description — reused when PhotoUrl matches AppearancePhotoUrl.</summary>
    public string? AppearanceDescription { get; set; }

    [MaxLength(512)]
    public string? AppearancePhotoUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<FamilyMember> FamilyMembers { get; set; } = new List<FamilyMember>();
    public ICollection<AdventurePack> AdventurePacks { get; set; } = new List<AdventurePack>();
}
