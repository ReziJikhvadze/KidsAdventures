using AdventurePacks.Api.Domain.Enums;

namespace AdventurePacks.Api.Domain.Entities;

/// <summary>One of the (up to) 5 chapter slots that make up a Story Path world for a child+theme.</summary>
public sealed class StoryPathChapter
{
    public Guid Id { get; set; }
    public Guid ChildId { get; set; }
    public ThemeType Theme { get; set; }
    public int ChapterIndex { get; set; }
    public Guid? AdventurePackId { get; set; }
    public StoryPathNodeStatus Status { get; set; }
    public DateTime? ParentConfirmedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
