namespace AdventurePacks.Api.DTOs.StoryPath;

public sealed class StoryNodeContentDto
{
    public string? Text { get; set; }
    public IReadOnlyList<string> ArtVariantIds { get; set; } = [];
}

public sealed class ProblemDefinitionDto
{
    public string InteractionType { get; set; } = "choice_consequence";
    public string? Prompt { get; set; }
    public string? ConfigJson { get; set; }
}

public sealed class StoryGraphPathDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public Guid? StartNodeId { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class StoryGraphNodeDto
{
    public Guid Id { get; set; }
    public Guid StoryPathId { get; set; }
    public string NodeKey { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public StoryNodeContentDto? Content { get; set; }
    public ProblemDefinitionDto? Problem { get; set; }
    public bool RequiresParentApproval { get; set; }
    public decimal? MapPositionX { get; set; }
    public decimal? MapPositionY { get; set; }
    public int SortOrder { get; set; }
    public IReadOnlyList<StoryGraphChoiceDto> Choices { get; set; } = [];
}

public sealed class StoryGraphChoiceDto
{
    public Guid Id { get; set; }
    public Guid StoryPathId { get; set; }
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }
    public string ChoiceKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? ConsequenceTag { get; set; }
    public int SortOrder { get; set; }
}

public sealed class StoryGraphDetailResponse
{
    public StoryGraphPathDto Path { get; set; } = new();
    public IReadOnlyList<StoryGraphNodeDto> Nodes { get; set; } = [];
    public IReadOnlyList<StoryGraphChoiceDto> Choices { get; set; } = [];
}

public sealed class CreateStoryGraphPathRequest
{
    public string Title { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
}

public sealed class UpdateStoryGraphPathRequest
{
    public string Title { get; set; } = string.Empty;
}

public sealed class UpsertStoryGraphNodeRequest
{
    public string NodeKey { get; set; } = string.Empty;
    public string NodeType { get; set; } = "narrative";
    public string Title { get; set; } = string.Empty;
    public StoryNodeContentDto? Content { get; set; }
    public ProblemDefinitionDto? Problem { get; set; }
    public bool RequiresParentApproval { get; set; }
    public decimal? MapPositionX { get; set; }
    public decimal? MapPositionY { get; set; }
    public int SortOrder { get; set; }
}

public sealed class UpsertStoryGraphChoiceRequest
{
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }
    public string ChoiceKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? ConsequenceTag { get; set; }
    public int SortOrder { get; set; }
}

public sealed class StoryGraphProgressDto
{
    public Guid ChildId { get; set; }
    public Guid StoryPathId { get; set; }
    public Guid? CurrentNodeId { get; set; }
    public IReadOnlyList<Guid> VisitedNodeIds { get; set; } = [];
    public DateTime UpdatedAt { get; set; }
}

public sealed class StoryGraphPlayResponse
{
    public StoryGraphDetailResponse Graph { get; set; } = new();
    public StoryGraphProgressDto? Progress { get; set; }
    public string PathMode { get; set; } = "Graph";
}
