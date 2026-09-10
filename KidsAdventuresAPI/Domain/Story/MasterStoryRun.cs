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

    /// <summary>
    /// Null while the run belongs to a guest.
    ///
    /// Written at the start when the parent is already signed in, and by fulfilment otherwise —
    /// which is what lets a parent's own space list the previews they have not bought yet. It
    /// says who the run belongs to and nothing about whether it was paid for: <see cref="PackId"/>
    /// is what separates a preview from a book.
    /// </summary>
    public Guid? UserId { get; set; }

    public Guid? PackId { get; set; }

    /// <summary>
    /// The saved hero this story was written for, when there was one.
    ///
    /// Only ever set for a signed-in parent, because a character is something an account holds.
    /// The child's own details are on the run either way; this is the pointer that files the
    /// preview under the same child the finished books are filed under.
    /// </summary>
    public Guid? CharacterId { get; set; }

    public string Status { get; set; } = MasterStoryRunStatus.Pending;

    /// <summary>Copy shown to the parent while they wait.</summary>
    public string? ProgressMessage { get; set; }

    public string ChildName { get; set; } = string.Empty;

    /// <summary>
    /// What the parent actually entered. <see cref="Age"/> is derived from it, and keeping only
    /// the derived number left no way to tell a mistyped date from a miscalculated age.
    /// </summary>
    public DateOnly? BirthDate { get; set; }

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

    /// <summary>v1 or v2 — which prompt variant wrote this book.</summary>
    public string? PromptVersion { get; set; }
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

/// <summary>
/// One unbought preview as the parent's own space lists it.
///
/// Deliberately not the run: the two columns that hold the story are NVARCHAR(MAX), and a shelf
/// listing six previews has no use for six books. What is here is what a card shows — which
/// child, which world, how far it got, and how long it will still be there.
/// </summary>
public sealed class MasterStoryRunSummary
{
    public Guid Id { get; set; }
    public Guid? CharacterId { get; set; }
    public string Status { get; set; } = MasterStoryRunStatus.Pending;
    public string? ProgressMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public string ChildName { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// The book's own title, lifted out of the stored story by SQL rather than by deserialising
    /// it here: the story is one of the two NVARCHAR(MAX) columns this projection exists to
    /// avoid reading. Null until the run is ready, and the card falls back to the world's.
    /// </summary>
    public string? Title { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// An expired guest run and what it left in storage. Deleting the row alone would orphan the
/// child's photograph, which is the file that mattered most for it to go.
/// </summary>
public sealed class ExpiredMasterStoryRun
{
    public Guid Id { get; set; }
    public string? PhotoBlobUrl { get; set; }
    public string? CoverImageUrl { get; set; }
}
