namespace AdventurePacks.Api.Domain.Models;

public sealed class StoryImageReference
{
    public byte[]? HeroPhotoBytes { get; init; }
    public string HeroPhotoContentType { get; init; } = "image/jpeg";
    public byte[]? CharacterAnchorBytes { get; init; }
}
