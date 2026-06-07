namespace AdventurePacks.Api.Services.Interfaces;

public readonly record struct NormalizedReferenceImage(byte[] Bytes, string ContentType, string FileName);

/// <summary>
/// Decodes, auto-orients, and re-encodes reference images so OpenAI Images Edit accepts them.
/// </summary>
public interface IReferenceImageNormalizer
{
    NormalizedReferenceImage NormalizeForOpenAi(byte[] bytes, string? hintContentType = null);

    NormalizedReferenceImage NormalizeForStorageWebp(byte[] bytes, string? hintContentType = null);
}
