namespace AdventurePacks.Api.Domain.Models;

public sealed class StoryNodeContent
{
    public string? Text { get; set; }
    public List<string> ArtVariantIds { get; set; } = [];
}
