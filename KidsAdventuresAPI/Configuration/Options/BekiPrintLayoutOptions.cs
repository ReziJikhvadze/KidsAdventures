namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// The physical shape of a Beki-format book.
///
/// Separate from <see cref="PrintLayoutOptions"/> rather than a second set of values inside it,
/// because both formats are staying: A5 books keep being printed from the numbers they were
/// always printed from, and nothing here can move them.
///
/// The spread is the unit, not the page. One illustration runs across both leaves and the story
/// text is set over it, so the geometry starts from the spread and the page is half of it — the
/// opposite of the A5 book, where a page is a page and a spread is two of them side by side.
///
/// **The sheet is the handoff's 440×200 and the artwork is not.** gpt-image draws 1:1, 2:3 and
/// 3:2 and nothing else, so every render arrives at 3:2 and the composer centre-crops it to the
/// sheet — the print keeps the central band and the top and bottom sixths are trimmed away.
/// This format held the artwork's own 3:2 for a while to avoid that loss; the product decision
/// went the other way, to the handoff's physical book, and the illustration prompt now confines
/// faces and action to the central band so the crop never takes anything the story needs.
/// </summary>
public sealed class BekiPrintLayoutOptions
{
    public const string SectionName = "BekiPrintLayout";

    /// <summary>
    /// The finished spread, both leaves together, in millimetres. The handoff's page is
    /// 220 × 200; the spread is two of them side by side.
    /// </summary>
    public float SpreadWidthMm { get; set; } = 440f;

    /// <summary>The handoff's page height. The spread and the single leaf share it.</summary>
    public float SpreadHeightMm { get; set; } = 200f;

    /// <summary>Half the spread. A single leaf, portrait, the way a picture book opens.</summary>
    public float PageWidthMm => SpreadWidthMm / 2f;

    /// <summary>How far the illustration runs past the trim on every side.</summary>
    public float BleedMm { get; set; } = 3f;

    /// <summary>
    /// How far the story text stays clear of the trim. Larger than the A5 book's margin because
    /// this text sits over artwork rather than on paper, and a line that runs close to the edge of
    /// a picture reads as part of the picture.
    /// </summary>
    public float SafeMarginMm { get; set; } = 14f;

    /// <summary>
    /// The share of the spread reserved for story text — the same third the illustrator was told
    /// to leave quiet. Written here as well so the two cannot disagree: if this widens and the
    /// prompt does not, text starts landing on faces the model was never asked to move.
    /// </summary>
    public float TextColumnShare { get; set; } = 0.33f;

    /// <summary>
    /// Story text size, in points. A spread is read aloud from arm's length by an adult holding a
    /// book open, which is further away than a page of prose is ever read from.
    /// </summary>
    public float StoryFontSize { get; set; } = 15f;

    /// <summary>
    /// Whether the English text is printed under the Georgian. Off by default: the handoff asks
    /// for both languages to exist, not for both to be on the same spread, and two languages over
    /// one illustration is twice the text in the space that was reserved for one.
    /// </summary>
    public bool PrintEnglishToo { get; set; }

    /// <summary>
    /// The stroke drawn around every printed glyph, in points. The wash quiets the artwork
    /// behind the words; the outline is the guarantee that holds when the wash meets a picture
    /// it cannot quiet — cream type over a sunlit cloud still has a dark edge to read by.
    /// Zero turns it off.
    /// </summary>
    public float TextOutlineWidth { get; set; } = 0.6f;

    /// <summary>Where the closing page's QR code sends the reader.</summary>
    public string EndingQrUrl { get; set; } = "https://beki.ge";

    /// <summary>The closing line. Reusable across every order, as the handoff's P18 asks.</summary>
    public string EndingLine { get; set; } = "ამბავი აქ მთავრდება — თავგადასავალი კი გრძელდება.";

    /// <summary>Printed under the QR code, saying what scanning it is for.</summary>
    public string EndingQrCaption { get; set; } = "შეაფასე ბეკის წიგნი";
}
