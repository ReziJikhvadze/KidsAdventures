namespace AdventurePacks.Api.Services.Beki;

/// <summary>
/// Reads the <c>data:image/jpeg;base64,…</c> string the form already holds.
///
/// The photo is sent as a data URL rather than a file because that is exactly what the browser
/// keeps in the draft after downscaling, and it is what will later be uploaded. Checking anything
/// else would be checking a photo the book is not made from.
/// </summary>
public static class PortraitDataUrl
{
    /// <summary>The same three the file picker accepts. Anything else never came from that input.</summary>
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];

    public static bool TryDecode(string? dataUrl, out byte[] bytes, out string contentType)
    {
        bytes = [];
        contentType = string.Empty;

        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            return false;
        }

        var comma = dataUrl.IndexOf(',');
        if (comma < 0 || !dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var header = dataUrl[5..comma];
        if (!header.Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var declared = header.Split(';')[0].Trim().ToLowerInvariant();
        if (!AllowedContentTypes.Contains(declared))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }

        contentType = declared;
        return bytes.Length > 0;
    }
}
