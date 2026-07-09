namespace AdventurePacks.Api.Domain.Models;

public sealed class AdventureGenerationInput
{
    public string ChildName { get; set; } = string.Empty;
    public int Age { get; set; }
    public ThemeType Theme { get; set; }
    public string? ChildAppearanceDescription { get; set; }
    public IReadOnlyList<FamilyMemberCastEntry> FamilyMembers { get; set; } = [];
    public string? OptionalStoryNotes { get; set; }
    public string StoryLanguage { get; set; } = "en";
    public int StoryPageCount { get; set; } = 6;

    /// <summary>1-based chapter number within a Story Path saga (chapter 1 = a book's first pack). Null for standalone books.</summary>
    public int? ChapterNumber { get; set; }
    public string? PreviousChapterRecap { get; set; }
    public string? PreviousCompanionName { get; set; }
    public string? PreviousCompanionType { get; set; }
}
