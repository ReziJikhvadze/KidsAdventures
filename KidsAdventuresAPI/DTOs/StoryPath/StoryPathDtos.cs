namespace AdventurePacks.Api.DTOs.StoryPath;

public sealed class StoryPathNodeDto
{
    /// <summary>Which of the (up to) 5 chapter slots this is (0-based).</summary>
    public int ChapterIndex { get; set; }

    /// <summary>Authored graph node id when PathMode is Graph.</summary>
    public Guid? StoryNodeId { get; set; }

    /// <summary>Stable node key within the graph (e.g. chapter-0).</summary>
    public string? NodeKey { get; set; }

    /// <summary>Locked | Unlocked | Generating | ReadyToRead | Complete.</summary>
    public string Status { get; set; } = string.Empty;

    public Guid? AdventurePackId { get; set; }
    public string? Title { get; set; }
    public string? CoverIllustrationUrl { get; set; }
    public DateTime? ParentConfirmedAt { get; set; }
}

public sealed class StoryPathWorldDto
{
    public string Theme { get; set; } = string.Empty;
    public bool HasReadablePack { get; set; }
    public bool IsWorldComplete { get; set; }

    /// <summary>Linear = legacy 5-chapter slots; Graph = authored StoryNodes graph is active.</summary>
    public string PathMode { get; set; } = "Linear";

    public IReadOnlyList<StoryPathNodeDto> Nodes { get; set; } = [];
}

public sealed class StoryPathOverviewResponse
{
    public Guid ChildId { get; set; }
    public IReadOnlyList<StoryPathWorldDto> Worlds { get; set; } = [];
    public IReadOnlyList<StoryPathAchievementDto> Achievements { get; set; } = [];
}

public sealed class StoryPathWorldResponse
{
    public Guid ChildId { get; set; }
    public StoryPathWorldDto World { get; set; } = new();
    public string? CampfirePrompt { get; set; }
    public IReadOnlyList<StoryPathAchievementDto> Achievements { get; set; } = [];
}

public sealed class StoryPathAchievementDto
{
    public string Theme { get; set; } = string.Empty;
    public string AchievementKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTime EarnedAt { get; set; }
}

/// <summary>Confirms a page has been read within the CURRENT chapter's pack (mid-chapter campfire gating, page index 0-5).</summary>
public sealed class ConfirmCampfireRequest
{
    public Guid ChildId { get; set; }
    public Guid AdventurePackId { get; set; }
    public int NodeIndex { get; set; }
}

public sealed class ConfirmCampfireResponse
{
    public StoryPathWorldDto World { get; set; } = new();
    public StoryPathAchievementDto? NewAchievement { get; set; }
    public string? NextTheme { get; set; }
    public bool SuggestNextWorld { get; set; }
}

/// <summary>Kicks off generation of the next chapter's story (chapter must be Unlocked and not already generated).</summary>
public sealed class GenerateChapterRequest
{
    public Guid ChildId { get; set; }
}

public sealed class GenerateChapterResponse
{
    public StoryPathWorldDto World { get; set; } = new();
}

/// <summary>Called when the reader finishes a chapter's last page — unlocks the next chapter / completes the world.</summary>
public sealed class CompleteChapterRequest
{
    public Guid ChildId { get; set; }
}

public sealed class CompleteChapterResponse
{
    public StoryPathWorldDto World { get; set; } = new();
    public StoryPathAchievementDto? NewAchievement { get; set; }
    public string? NextTheme { get; set; }
    public bool SuggestNextWorld { get; set; }
}
