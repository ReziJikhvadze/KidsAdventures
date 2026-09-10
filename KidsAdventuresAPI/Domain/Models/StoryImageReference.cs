namespace AdventurePacks.Api.Domain.Models;

public sealed class StoryImageReference
{
    public byte[]? CharacterAnchorBytes { get; init; }
    public IReadOnlyList<CastPhotoReference> CastPhotos { get; init; } = [];

    /// <summary>
    /// How many pictures this actually carries - the anchor when it has bytes, plus each cast
    /// photograph that has bytes of its own.
    ///
    /// The same arithmetic the image service does after normalising, said here where a caller can
    /// ask before it spends anything. It exists because the preview cover's "the child and the
    /// world, both" rule was enforced only inside the concrete OpenAI service, so a caller that
    /// dropped one of the two compiled, passed its tests against a stub that has no such rule,
    /// and failed on every real preview.
    /// </summary>
    public int ImageCount =>
        (CharacterAnchorBytes is { Length: > 0 } ? 1 : 0)
        + CastPhotos.Count(photo => photo.Bytes is { Length: > 0 });
}
