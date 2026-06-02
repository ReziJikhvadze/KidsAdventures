namespace AdventurePacks.Api.Domain.Models;

public sealed class AdventureGenerationInput
{
    public string ChildName { get; set; } = string.Empty;
    public int Age { get; set; }
    public ThemeType Theme { get; set; }
    public IReadOnlyList<string> FamilyMembers { get; set; } = [];
}
