namespace AdventurePacks.Api.Domain.Entities;

/// <summary>Authored directed graph for a theme's interactive story path.</summary>
public sealed class StoryPathGraph
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public ThemeType Theme { get; set; }
    public Guid? StartNodeId { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
