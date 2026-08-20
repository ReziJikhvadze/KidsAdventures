using AdventurePacks.Api.Domain.Models;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// Turns the Beki generator's labelled references into the shape the image service already takes.
///
/// <see cref="StoryImageReference"/> was built for the A5 flow, where there is one hero anchor and
/// a list of cast photographs. Beki has a different cast: the child's photograph, Beki's canonical
/// picture, and one accepted illustration per recurring character. They are all reference images
/// to the edit endpoint, which is why this is a translation and not a second reference type — a
/// parallel model would mean a second code path through image generation, which is the one place
/// in this product where a second code path is most expensive to be wrong in.
///
/// The first reference becomes the anchor because the edit endpoint weights the first image most
/// heavily, and the child's likeness is the thing a parent checks first.
/// </summary>
public static class BekiImageReferences
{
    public static StoryImageReference? ToStoryImageReference(
        IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references)
    {
        if (references.Count == 0) return null;

        var first = references[0];
        var rest = references
            .Skip(1)
            .Select(reference => new CastPhotoReference
            {
                Name = reference.Label,
                Relationship = "story reference",
                Bytes = reference.Bytes,
                ContentType = reference.ContentType,
            })
            .ToList();

        return new StoryImageReference
        {
            CharacterAnchorBytes = first.Bytes,
            CastPhotos = rest,
        };
    }
}
