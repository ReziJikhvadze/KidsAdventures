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
    /// A copy for a screen to draw: small enough for an avatar or a thumbnail in a list, and
    /// cheap enough to make that nobody waits for it. The two above are sized for the image model
    /// and cost seconds each — measured at twenty on the API, per photograph — which is not a
    /// price a page load can pay.
    /// </summary>
    NormalizedReferenceImage NormalizeForDisplayWebp(byte[] bytes, string? hintContentType = null);
}
