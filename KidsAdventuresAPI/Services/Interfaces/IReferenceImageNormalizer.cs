namespace AdventurePacks.Api.Services.Interfaces;

public readonly record struct NormalizedReferenceImage(byte[] Bytes, string ContentType, string FileName);

/// <summary>
/// Decodes, auto-orients, and re-encodes reference images so OpenAI Images Edit accepts them.
/// </summary>
public interface IReferenceImageNormalizer
{
    NormalizedReferenceImage NormalizeForOpenAi(byte[] bytes, string? hintContentType = null);

    NormalizedReferenceImage NormalizeForStorageWebp(byte[] bytes, string? hintContentType = null);

    /// <summary>
    /// What a portrait is kept as. Sized exactly like <see cref="NormalizeForOpenAi"/>, because
    /// this is the copy the image model is later given, but encoded rather than stored raw.
    ///
    /// <para>
    /// Reach for <see cref="NormalizeForOpenAi"/> when the bytes are about to be handed to a
    /// model, and for this when they are about to be written to storage. Both are needed: what
    /// goes into the request has to be a PNG, and what sits in the container for years does not.
    /// </para>
    /// </summary>
    NormalizedReferenceImage NormalizeForPortraitStorage(byte[] bytes, string? hintContentType = null);

    /// <summary>
    /// A copy for a screen to draw: small enough for an avatar or a thumbnail in a list, and
    /// cheap enough to make that nobody waits for it. The two above are sized for the image model
    /// and cost seconds each — measured at twenty on the API, per photograph — which is not a
    /// price a page load can pay.
    /// </summary>
    NormalizedReferenceImage NormalizeForDisplayWebp(byte[] bytes, string? hintContentType = null);
}
