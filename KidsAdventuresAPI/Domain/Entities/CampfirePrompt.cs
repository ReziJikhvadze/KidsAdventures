namespace AdventurePacks.Api.Domain.Entities;

public sealed class CampfirePrompt
{
    public Guid Id { get; set; }
    public ThemeType Theme { get; set; }
    public int NodeIndex { get; set; }
    public string PromptText { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
