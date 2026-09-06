namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>
/// A portrait as a screen should receive it, and whether that is really what it is.
///
/// <paramref name="IsRendition"/> is false when the copy could not be made and the stored file is
/// being sent instead. The caller needs to know: a cache tag says "these exact bytes", and
/// labelling a one-off fallback with the rendition's tag would leave a browser holding the wrong
/// picture for as long as it kept it.
/// </summary>
public readonly record struct PortraitForDisplay(
    byte[] Bytes,
    string ContentType,
    bool IsRendition);

/// <summary>
/// The screen-sized copy of a stored portrait, kept beside the original.
///
/// What is stored for a child is a lossless PNG normalized for the image model — measured at
/// 2.3 MB on a real portrait — and it was being sent whole to draw an avatar the size of a
/// thumbnail. Converting it per request fixed the size and replaced it with a worse problem:
/// twenty seconds of decode and re-encode on the API, every single time, because the normalizer's
/// cache lives only as long as the request that owns it.
///
/// So the small copy is made once and kept. Made at upload, when the bytes are already in hand;
/// made on first sight for the portraits that predate this; and after that it is one small file
/// read, which is what a list of children should cost.
/// </summary>
/// <summary>
/// Names the rendition — in the stored file's own neighbourhood, and in the cache tag the photo
/// endpoints send. Both read it from here so that changing what is encoded cannot change one and
/// leave the other: a new size under an old tag is a browser that never asks for it again.
///
/// Bump it and the next request writes the new copy beside the old one. The stale copies are
/// orphaned, which costs pennies of storage and no correctness.
/// </summary>
public static class PortraitRendition
{
    public const string Version = "d1";

    /// <summary>What the copy is called, next to the file it was made from.</summary>
    public const string Suffix = $".{Version}.webp";
}

public interface IPortraitRenditionService
{
    /// <summary>
    /// The display copy for a stored portrait, making it first if this is the first time anyone
    /// has asked.
    /// </summary>
    Task<PortraitForDisplay> GetAsync(string storedUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Makes and keeps the copy for a portrait that has just been uploaded, from the bytes the
    /// caller already holds. Never throws: a portrait is saved whether or not its thumbnail is.
    /// </summary>
    Task WarmAsync(string storedUrl, byte[] originalBytes, CancellationToken cancellationToken);
}
