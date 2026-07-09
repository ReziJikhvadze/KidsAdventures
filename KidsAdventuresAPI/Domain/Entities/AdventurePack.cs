namespace AdventurePacks.Api.Domain.Entities;

public sealed class AdventurePack
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ChildId { get; set; }

    public ThemeType Theme { get; set; }
    public AdventurePackStatus Status { get; set; } = AdventurePackStatus.Pending;

    public string? GeneratedJson { get; set; }
    public string? PdfUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OptionalStoryNotes { get; set; }
    public string? StoryLanguage { get; set; }
    public string? ProgressMessage { get; set; }
    public bool PdfCreditCharged { get; set; }
    public string? PreviewIllustrationUrl { get; set; }
    public PreviewIllustrationStatus PreviewIllustrationStatus { get; set; } = PreviewIllustrationStatus.None;
    public DateTime? PreviewIllustrationUpdatedAt { get; set; }
    public int StoryPageCount { get; set; } = 6;
    public bool IsWelcomeGiftStory { get; set; }
    public int? ChapterIndex { get; set; }
    public Guid? PreviousChapterPackId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
