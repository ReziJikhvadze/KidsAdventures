using System.Text;
using Microsoft.Net.Http.Headers;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// The name a downloaded book arrives under.
///
/// A parent who saves three books ended up with three files called beki-&lt;guid&gt;-book.pdf, which
/// is the row's primary key wearing a file extension — nothing a person can tell apart in a
/// downloads folder. The book has a name of its own and it is the one on the cover, so that is
/// what the browser is told.
///
/// Two names are needed, not one. RFC 6266 gives an <c>attachment</c> header a plain
/// <c>filename</c> and a UTF-8 <c>filename*</c>, and every current browser prefers the latter —
/// but <see cref="ContentDispositionHeaderValue.SetHttpFileName"/>, which is what ASP.NET reaches
/// for when a file result carries a download name, builds the plain one by replacing every
/// non-ASCII character with an underscore. A Georgian title is entirely non-ASCII, so that path
/// produces <c>_______.pdf</c> for anything that ignores <c>filename*</c>. Here the plain
/// parameter is built deliberately instead: a transliteration for the customer download, and the
/// old beki-&lt;id&gt; spelling for the console, where the operator's two files must stay
/// distinguishable in every spelling.
/// </summary>
public static class PdfFileNames
{
    /// <summary>
    /// Characters no file system will accept, from the union of Windows' and POSIX' refusals.
    /// Unicode is deliberately not in this set — a Georgian title is a legal file name.
    /// </summary>
    private const string IllegalCharacters = @"\/:*?""<>|";

    /// <summary>
    /// Room for a long title without approaching the 255-byte name limit, which Georgian reaches
    /// three times faster than Latin does because each letter costs three UTF-8 bytes.
    /// </summary>
    private const int MaxStemLength = 70;

    /// <summary>
    /// The name a person sees: the book's own title, with only the characters a file system
    /// refuses removed. Falls back to <paramref name="fallbackName"/> — used whole, extension and
    /// all — when the book has no title, or when nothing survives sanitising.
    /// </summary>
    public static string ForBook(string? title, string? suffix, string fallbackName)
    {
        var stem = Sanitize(title);
        return stem.Length == 0 ? fallbackName : stem + suffix + ".pdf";
    }

    /// <summary>
    /// The same name reduced to ASCII, for the plain <c>filename</c> parameter. Georgian is
    /// transliterated rather than blanked, so a client that ignores <c>filename*</c> still gets
    /// something readable instead of a row of underscores.
    /// </summary>
    public static string AsciiForBook(string? title, string? suffix, string fallbackName)
    {
        if (Sanitize(title).Length == 0)
        {
            return fallbackName;
        }

        var stem = ToAscii(title + suffix);
        return stem.Length == 0 ? fallbackName : stem + ".pdf";
    }

    /// <summary>
    /// An <c>attachment</c> header carrying both spellings. Set it on the response directly:
    /// passing a download name to <c>File(...)</c> instead would have ASP.NET rebuild the header
    /// with its underscore-substituted ASCII form and overwrite this one.
    /// </summary>
    public static ContentDispositionHeaderValue Attachment(string displayName, string asciiName) =>
        new("attachment")
        {
            FileName = asciiName,
            FileNameStar = displayName,
        };

    private static string Sanitize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(title.Length);
        foreach (var character in title)
        {
            if (char.IsControl(character) || IllegalCharacters.Contains(character))
            {
                continue;
            }

            builder.Append(character);
        }

        // Windows refuses a name that ends in a dot or a space, and "." and ".." are not names at
        // all — trimming both ends covers every one of those without touching a legal title.
        var cleaned = builder.ToString().Trim().Trim('.').Trim();
        return Truncate(cleaned);
    }

    private static string Truncate(string value)
    {
        if (value.Length <= MaxStemLength)
        {
            return value;
        }

        // Never split a surrogate pair: half an emoji is not a character, and the trailing
        // trim would leave the orphan behind.
        var length = char.IsHighSurrogate(value[MaxStemLength - 1]) ? MaxStemLength - 1 : MaxStemLength;
        return value[..length].TrimEnd().TrimEnd('.').TrimEnd();
    }

    private static string ToAscii(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (Georgian.TryGetValue(character, out var latin))
            {
                builder.Append(latin);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                // Everything else — spaces, punctuation, scripts we have no table for — becomes
                // one separator, so a title full of them cannot produce a run of dashes.
                builder.Append('-');
            }
        }

        var ascii = builder.ToString().TrimEnd('-');
        return ascii.Length <= MaxStemLength ? ascii : ascii[..MaxStemLength].TrimEnd('-');
    }

    /// <summary>
    /// Mkhedruli, in the Georgian national transliteration. Only the plain <c>filename</c>
    /// parameter is built from this, so the pairs that share a Latin spelling (თ/ტ, ფ/ქ, ც/წ,
    /// ჩ/ჭ) collapsing is not a loss — <c>filename*</c> carries the real title.
    /// </summary>
    private static readonly Dictionary<char, string> Georgian = new()
    {
        ['ა'] = "a", ['ბ'] = "b", ['გ'] = "g", ['დ'] = "d", ['ე'] = "e", ['ვ'] = "v",
        ['ზ'] = "z", ['თ'] = "t", ['ი'] = "i", ['კ'] = "k", ['ლ'] = "l", ['მ'] = "m",
        ['ნ'] = "n", ['ო'] = "o", ['პ'] = "p", ['ჟ'] = "zh", ['რ'] = "r", ['ს'] = "s",
        ['ტ'] = "t", ['უ'] = "u", ['ფ'] = "p", ['ქ'] = "k", ['ღ'] = "gh", ['ყ'] = "q",
        ['შ'] = "sh", ['ჩ'] = "ch", ['ც'] = "ts", ['ძ'] = "dz", ['წ'] = "ts", ['ჭ'] = "ch",
        ['ხ'] = "kh", ['ჯ'] = "j", ['ჰ'] = "h",
    };
}
