using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <inheritdoc />
public sealed class PortraitRenditionService(
    IBlobStorageService blobStorageService,
    IReferenceImageNormalizer referenceImageNormalizer,
    ILogger<PortraitRenditionService> logger) : IPortraitRenditionService
{
    public async Task<PortraitForDisplay> GetAsync(string storedUrl, CancellationToken cancellationToken)
    {
        /*
          Two requests arriving first will both make the copy and both write it. Deliberately not
          coordinated: they write the same bytes to the same name, so the loser costs a fraction of
          a second of encoding and nothing else, and it can only happen once in a portrait's life.
          A lock that actually held across requests would have to be a process-wide one keyed by
          name, which is more machinery than the duplicate work is worth.
        */
        var kept = await blobStorageService.TryDownloadBesideAsync(
            storedUrl, PortraitRendition.Suffix, cancellationToken);

        if (kept is { Length: > 0 })
        {
            return new PortraitForDisplay(kept, "image/webp", IsRendition: true);
        }

        var original = await blobStorageService.DownloadBytesFromStoredUrlAsync(storedUrl, cancellationToken);
        var display = Render(original, storedUrl);
        if (display is null)
        {
            // Unreadable by the resizer but very likely readable by a browser, so send what we
            // have — and say that it is not the rendition, so nobody caches it as one.
            return new PortraitForDisplay(original, ContentTypeFor(storedUrl), IsRendition: false);
        }

        await KeepAsync(storedUrl, display.Value, cancellationToken);
        return new PortraitForDisplay(display.Value.Bytes, display.Value.ContentType, IsRendition: true);
    }

    public async Task WarmAsync(string storedUrl, byte[] originalBytes, CancellationToken cancellationToken)
    {
        try
        {
            var display = Render(originalBytes, storedUrl);
            if (display is null) return;
            await KeepAsync(storedUrl, display.Value, cancellationToken);
        }
        catch (Exception ex)
        {
            /*
              Including cancellation, which is the point of catching this widely.

              By the time this runs the portrait itself is already in storage and the caller is
              about to write the row that points at it. Letting anything from here escape would
              abandon that upload over a thumbnail — and the thumbnail is not lost either way,
              because the first reader makes it. Warming is an optimisation and fails like one.
            */
            logger.LogWarning(ex, "Could not warm the display copy of a portrait");
        }
    }

    private NormalizedReferenceImage? Render(byte[] original, string storedUrl)
    {
        try
        {
            return referenceImageNormalizer.NormalizeForDisplayWebp(original, ContentTypeFor(storedUrl));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not make a display copy of a portrait ({Bytes} bytes)", original.Length);
            return null;
        }
    }

    private async Task KeepAsync(
        string storedUrl, NormalizedReferenceImage display, CancellationToken cancellationToken)
    {
        try
        {
            await blobStorageService.UploadBesideAsync(
                storedUrl, PortraitRendition.Suffix, display.Bytes, display.ContentType, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The copy is a cache, not the record. Failing to keep it costs the next reader the
            // same work and costs this one nothing — they already have the bytes.
            logger.LogWarning(ex, "Could not keep the display copy of a portrait");
        }
    }

    private static string ContentTypeFor(string url) => url switch
    {
        _ when url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) => "image/png",
        _ when url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) => "image/webp",
        _ => "image/jpeg"
    };
}
