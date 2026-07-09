namespace AdventurePacks.Api.Domain.Entities;

public sealed class StoryPathNodeProgress
{
    public Guid Id { get; set; }
    public Guid ChildId { get; set; }
    public Guid AdventurePackId { get; set; }
    public ThemeType Theme { get; set; }
    public int NodeIndex { get; set; }
    public StoryPathNodeStatus Status { get; set; }
    public DateTime? CampfirePromptShownAt { get; set; }
    public DateTime? ParentConfirmedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
