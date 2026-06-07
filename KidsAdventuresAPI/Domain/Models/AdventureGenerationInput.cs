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
}
