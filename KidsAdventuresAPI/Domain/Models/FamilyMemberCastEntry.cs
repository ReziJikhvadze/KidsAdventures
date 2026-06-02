namespace AdventurePacks.Api.Domain.Models;

public sealed class FamilyMemberCastEntry
{
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string? AppearanceDescription { get; set; }
}
