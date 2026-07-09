namespace AdventurePacks.Api.Domain.Entities;

public sealed class StoryPathAchievement
{
    public Guid Id { get; set; }
    public Guid ChildId { get; set; }
    public ThemeType Theme { get; set; }
    public string AchievementKey { get; set; } = string.Empty;
    public DateTime EarnedAt { get; set; } = DateTime.UtcNow;
}
