namespace AdventurePacks.Api.Domain.Entities;

/// <summary>Per-child traversal state through an authored story graph.</summary>
public sealed class StoryPathGraphProgress
{
    public Guid Id { get; set; }
    public Guid ChildId { get; set; }
    public Guid StoryPathId { get; set; }
    public Guid? CurrentNodeId { get; set; }
    public string VisitedNodeIdsJson { get; set; } = "[]";
    public string ChoiceHistoryJson { get; set; } = "[]";
    public string? ProblemResolvedJson { get; set; }
    public string? ParentApprovedNodeIdsJson { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
