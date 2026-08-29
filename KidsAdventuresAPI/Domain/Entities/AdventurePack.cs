namespace AdventurePacks.Api.Domain.Entities;

public sealed class AdventurePack
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Legacy pointer to the pre-Characters <c>Children</c> table. Null for new books.</summary>
    public Guid? ChildId { get; set; }

    public ThemeType Theme { get; set; }
    public AdventurePackStatus Status { get; set; } = AdventurePackStatus.Pending;

    public string? GeneratedJson { get; set; }
    public string? PdfUrl { get; set; }

    /// <summary>
    /// The copy that goes to the binder, with the blank leaves saddle-stitch needs.
    ///
    /// Null for every book made before the two files were split apart. Those have a reading
    /// copy and nothing else, so anything wanting a printable file falls back to it.
    /// </summary>
    public string? PrintPdfUrl { get; set; }

    public string? ErrorMessage { get; set; }
    public string? OptionalStoryNotes { get; set; }
    public string? StoryLanguage { get; set; }
    public string? ProgressMessage { get; set; }

    /// <summary>
    /// How far the running job has got, 0-100, or null when nothing is running. The message
    /// used to carry this inside its own sentence, which no progress bar can read.
    /// </summary>
    public int? ProgressPercent { get; set; }
    public bool PdfCreditCharged { get; set; }
    public string? PreviewIllustrationUrl { get; set; }
    public PreviewIllustrationStatus PreviewIllustrationStatus { get; set; } = PreviewIllustrationStatus.None;
    public DateTime? PreviewIllustrationUpdatedAt { get; set; }
    public int StoryPageCount { get; set; } = 6;
    public bool IsWelcomeGiftStory { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // -- book model -------------------------------------------------------
    // A pack is now one book in a child's series rather than a standalone story.

    /// <summary>Groups the books of one child into an ongoing series.</summary>
    public Guid? SeriesId { get; set; }

    /// <summary>Position in the series, starting at 1.</summary>
    public int SequenceNumber { get; set; } = 1;

    /// <summary>The book this one picks up from, so characters and threads carry forward.</summary>
    public Guid? ContinuesFromBookId { get; set; }

    public BookAccessLevel AccessLevel { get; set; } = BookAccessLevel.Preview;

    /// <summary>Which world on the adventure map this book belongs to.</summary>
    public string? WorldId { get; set; }

    public Guid? PrimaryCharacterId { get; set; }

    public string? Title { get; set; }
    public string? CoverImageUrl { get; set; }

    /// <summary>A printed copy has been paid for, independent of any print order's status.</summary>
    public bool HasPrintEntitlement { get; set; }

    /// <summary>When this book was last opened in the reader, on any device. Null until read.</summary>
    public DateTime? LastReadAt { get; set; }

    /// <summary>
    /// When the generation job last said anything about this pack — refreshed on the claim and on
    /// every status or progress write, so a stalled job is distinguishable from a slow one.
    ///
    /// Null for a pack that has never been claimed, and for every row written before the column
    /// existed. The stale-generation sweep falls back to <see cref="CreatedAt"/> when it is null,
    /// which is the only reason it can reach the books that are already stuck.
    /// </summary>
    public DateTime? GenerationHeartbeatUtc { get; set; }

    public bool IsFullyUnlocked => AccessLevel == BookAccessLevel.Full;
}
