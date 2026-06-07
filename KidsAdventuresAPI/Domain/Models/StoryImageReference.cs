namespace AdventurePacks.Api.Domain.Models;

public sealed class StoryImageReference
{
    public byte[]? CharacterAnchorBytes { get; init; }
    public IReadOnlyList<CastPhotoReference> CastPhotos { get; init; } = [];
}
