namespace AdventurePacks.Api.Domain.Entities;

public sealed class StoryGraphNode
{
    public Guid Id { get; set; }
    public Guid StoryPathId { get; set; }

    [MaxLength(64)]
    public string NodeKey { get; set; } = string.Empty;

    public StoryGraphNodeType NodeType { get; set; } = StoryGraphNodeType.Narrative;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string? ContentJson { get; set; }
    public string? ProblemJson { get; set; }
    public bool RequiresParentApproval { get; set; }

    public decimal? MapPositionX { get; set; }
    public decimal? MapPositionY { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
