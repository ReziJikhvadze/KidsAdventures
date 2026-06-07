namespace AdventurePacks.Api.Domain.Models;

public sealed class CastPhotoReference
{
    public required string Name { get; init; }
    public required string Relationship { get; init; }
    public bool IsHero { get; init; }
    public string? AppearanceDescription { get; init; }
    public required byte[] Bytes { get; init; }
    public string ContentType { get; init; } = "image/jpeg";
}
