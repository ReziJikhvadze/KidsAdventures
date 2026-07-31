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

    /// <summary>
    /// What earlier books in this child's series established — companions, moments, the running
    /// goal — already rendered for the prompt. Null for a first book.
    /// </summary>
    public string? SeriesMemory { get; set; }

    /// <summary>Which book this is in the series. 1 for a first adventure.</summary>
    public int ChapterNumber { get; set; } = 1;

    /// <summary>
    /// Operator tuning for this age band and world, from the admin matrix. Null means the
    /// built-in age guidance stands on its own.
    /// </summary>
    public StoryRule? StoryRule { get; set; }
}
