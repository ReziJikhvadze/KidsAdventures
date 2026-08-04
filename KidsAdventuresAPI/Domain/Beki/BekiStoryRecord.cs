namespace AdventurePacks.Api.Domain.Beki;

/// <summary>A row in <c>dbo.BekiStories</c> — one book and everything needed to explain it.</summary>
public sealed class BekiStoryRecord
{
    public Guid Id { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid? CharacterId { get; set; }
    public int BookNumber { get; set; }
    public string ChildName { get; set; } = string.Empty;
    public string AgeBand { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string? TitleKa { get; set; }

    /// <summary>See <see cref="BekiStoryStatus"/>.</summary>
    public string Status { get; set; } = BekiStoryStatus.Pending;

    public string? FinalStoryJson { get; set; }
    public string? RawGeneratorOutputJson { get; set; }
    public string? StoryInputJson { get; set; }
    public string? ReviewStatus { get; set; }
    public string? ValidationErrorsJson { get; set; }
    public string? FailureReason { get; set; }
    public string? CreativeSeedId { get; set; }
    public string? GeneratorPromptVersion { get; set; }
    public string? ReviewerPromptVersion { get; set; }
    public string? RepairPromptVersion { get; set; }
    public string? GeneratorModel { get; set; }
    public string? ReviewerModel { get; set; }
    public string InputSchemaVersion { get; set; } = "1.0";
    public string OutputSchemaVersion { get; set; } = "1.0";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public static class BekiStoryStatus
{
    public const string Pending = "pending";
    public const string Generating = "generating";
    public const string Approved = "approved";

    /// <summary>Passed validation, but the reviewer asked for a person to look.</summary>
    public const string NeedsHumanReview = "needs_human_review";

    public const string Failed = "failed";

    /// <summary>A book a parent may read. Anything else is still in flight or broken.</summary>
    public static bool IsReadable(string status) =>
        status is Approved or NeedsHumanReview;
}

/// <summary>A row in <c>dbo.BekiContinuationMemory</c>.</summary>
public sealed class BekiMemoryRecord
{
    public Guid Id { get; set; }
    public Guid StoryId { get; set; }
    public Guid? CharacterId { get; set; }
    public int BookNumber { get; set; }
    public string MemoryJson { get; set; } = string.Empty;
    public string? NextChapterHookKa { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
