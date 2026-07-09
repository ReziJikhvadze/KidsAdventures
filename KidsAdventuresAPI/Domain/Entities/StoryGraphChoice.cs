namespace AdventurePacks.Api.Domain.Entities;

public sealed class StoryGraphChoice
{
    public Guid Id { get; set; }
    public Guid StoryPathId { get; set; }
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }

    [MaxLength(64)]
    public string ChoiceKey { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? ConsequenceTag { get; set; }

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
