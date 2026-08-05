namespace AdventurePacks.Api.Domain.Story;

public static class MasterStoryRunStatus
{
    public const string Pending = "Pending";
    public const string Writing = "Writing";
    public const string Illustrating = "Illustrating";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
}

/// <summary>
/// One master call, from the inputs it was given to the book it produced.
///
/// This is the record the waiting browser polls and the record we read when a book turns out
/// wrong. It keeps the prompts as they were sent and the story exactly as it came back, before
/// projection, because those are the two things that cannot be reconstructed afterwards.
/// </summary>
public sealed class MasterStoryRun
{
    public Guid Id { get; set; }

    /// <summary>Null while the run belongs to a guest; set when an account claims it.</summary>
    public Guid? UserId { get; set; }

    public Guid? PackId { get; set; }

    public string Status { get; set; } = MasterStoryRunStatus.Pending;

    /// <summary>Copy shown to the parent while they wait.</summary>
    public string? ProgressMessage { get; set; }

    public string ChildName { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string? EyeColor { get; set; }
    public string? ExtraWishes { get; set; }
    public string? AppearanceDescription { get; set; }

    /// <summary>Where the uploaded portrait is parked for the job that draws the pictures.</summary>
    public string? PhotoBlobUrl { get; set; }
    public string StoryLanguage { get; set; } = "ka";
    public int SpreadCount { get; set; } = BookFormat.SpreadCount;

    public string? Model { get; set; }
    public string? SystemPrompt { get; set; }
    public string? UserPrompt { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }

    /// <summary>The model's answer, untouched.</summary>
    public string? StoryJson { get; set; }

    /// <summary>The same book in the shape the app renders.</summary>
    public string? ContentJson { get; set; }

    public string? CoverImageUrl { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Null means keep. Guest runs carry a value and are deleted once it passes.</summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>The small half of a run: everything a waiting browser needs and nothing it does not.</summary>
public sealed class MasterStoryRunProgress
{
    public Guid Id { get; set; }
    public string Status { get; set; } = MasterStoryRunStatus.Pending;
    public string? ProgressMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CoverImageUrl { get; set; }
}
