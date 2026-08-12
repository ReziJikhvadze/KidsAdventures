using AdventurePacks.Api.DTOs.AdventurePacks;

namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>
/// What the printer needs to set a book.
///
/// A record rather than four positional arguments: the cover and the language were both
/// added after the fact, and each one that arrives as a bare parameter is another call site
/// that compiles while passing the wrong thing.
/// </summary>
public sealed record PdfBookRequest
{
    public required AdventureContentDto Content { get; init; }

    public required string ThemeName { get; init; }

    /// <summary>
    /// The book's own cover art. Null falls back to a typeset cover with no picture — which is
    /// correct, and much better than the previous behaviour of reprinting page one's
    /// illustration and leaving the real cover out of the book entirely.
    /// </summary>
    public byte[]? CoverImage { get; init; }

    /// <summary>"ka" or "en". Decides the running heads, not the story, which is already written.</summary>
    public string Language { get; init; } = "ka";

    /// <summary>
    /// Where the back cover's QR sends a parent. A printed book has no button, so this is the
    /// only way out of it — and it goes to the same place the reader's button does.
    /// </summary>
    public string? ContinueUrl { get; init; }

    /// <summary>Beki on the closing page. Null falls back to the canonical portrait on disk.</summary>
    public byte[]? GuidePortrait { get; init; }
}

public interface IAdventurePdfService
{
    byte[] GeneratePdf(PdfBookRequest request);
}
