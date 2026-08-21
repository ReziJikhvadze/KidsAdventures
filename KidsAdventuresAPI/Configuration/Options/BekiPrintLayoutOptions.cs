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
    /// How far the story text stays clear of the trim — the spec's outer safe area. Larger than
    /// the A5 book's margin because this text sits over artwork rather than on paper, and a line
    /// that runs close to the edge of a picture reads as part of the picture.
    /// </summary>
    public float SafeMarginMm { get; set; } = 12f;

    /// <summary>
    /// The width of the low-information band straddling the fold, in millimetres — half of it
    /// falls on each page. A print gutter swallows a sliver of the sheet into the binding, and
    /// even before a printer's own imposition is known, nothing planned this close to the fold
    /// should be trusted to survive it. Not yet wired into a dedicated layout check — that is
    /// future QA — but used now to hold the story text column's inner edge back from the fold on
    /// every spread, so a widened <see cref="TextColumnShare"/> can never quietly creep into it.
    /// </summary>
    public float GutterZoneMm { get; set; } = 30f;

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

    /// <summary>
    /// Where spread 8's Continue Adventure QR sends the reader. Used to be the closing page's own
    /// code as well, back when one URL did both jobs; <see cref="ReviewQrUrl"/> is the one that
    /// took over the closing page, so each code can be repointed without disturbing the other.
    /// </summary>
    public string EndingQrUrl { get; set; } = "https://beki.ge";

    /// <summary>
    /// Where the closing page's rate-us QR sends the reader — see <see cref="EndingQrUrl"/> for
    /// the sibling that stayed behind on spread 8.
    /// </summary>
    public string ReviewQrUrl { get; set; } = "https://beki.ge";

    /// <summary>The closing line. Reusable across every order, as the handoff's P18 asks.</summary>
    public string EndingLine { get; set; } = "ამბავი აქ მთავრდება — თავგადასავალი კი გრძელდება.";

    /// <summary>Printed under the QR code, saying what scanning it is for.</summary>
    public string EndingQrCaption { get; set; } = "შეაფასე ბეკის წიგნი";

    /// <summary>
    /// P1's line, set beside the Beki visual — the book's first words, before the story itself
    /// begins. Reusable across every order, like P1 as a whole.
    /// </summary>
    public string InvitationLine { get; set; } = "მზად ხარ? ბეკი გელოდება.";

    /// <summary>
    /// The short line beside spread 8's Continue Adventure QR, so the code reads as an invitation
    /// rather than a bare square the reader has to guess the purpose of.
    /// </summary>
    public string ContinueCtaText { get; set; } = "განაგრძე თავგადასავალი ბეკისთან";

    /// <summary>
    /// Finished art for P1, the invitation leaf. Null — the default — keeps the code-drawn
    /// placeholder the composer has always built.
    ///
    /// Six of the book's fourteen pages carry nothing from the customer's own story: both
    /// endpapers, P1, P18 and the back cover are the same paper in every order, and the composer
    /// draws them from primitives only because the partner has not delivered the real thing yet.
    /// These four settings are the door that delivery comes through: point one at a file, drop the
    /// file into the published folder, and that page starts printing the partner's art full bleed
    /// instead. Nothing else changes — the page count, the sheet size and the bleed are the book's,
    /// not the asset's.
    ///
    /// Relative paths resolve against the published output folder
    /// (<see cref="AppContext.BaseDirectory"/>), the same way the fonts and the canonical Beki PNG
    /// do, so a value like <c>Assets/Beki/p1-invitation.png</c> works identically on a developer's
    /// machine and in a container. An absolute path is used as given. A path that points at
    /// nothing is not an error: the page falls back to the drawn placeholder, because a
    /// mistyped setting should cost a page its art, never the order its book.
    /// </summary>
    public string? InvitationAssetPath { get; set; }

    /// <summary>Finished art for P18, the closing leaf. See <see cref="InvitationAssetPath"/>.</summary>
    public string? ClosingAssetPath { get; set; }

    /// <summary>Finished art for the back cover. See <see cref="InvitationAssetPath"/>.</summary>
    public string? BackCoverAssetPath { get; set; }

    /// <summary>
    /// Finished art for the endpapers — a template rather than a path, because the endpaper is the
    /// one reusable page the handoff wants to vary: a book about the sea and a book about space can
    /// bind different papers. <c>{theme}</c> is replaced with the pack's theme, lowercased and
    /// stripped to letters, digits, dashes and underscores, so
    /// <c>Assets/Beki/endpaper-{theme}.png</c> becomes <c>Assets/Beki/endpaper-space.png</c>.
    ///
    /// A theme with no file of its own falls back to the drawn dot field, which means a partial
    /// set of endpapers is a perfectly good state to ship in — the themes that have art get it,
    /// the rest keep the placeholder. See <see cref="InvitationAssetPath"/> for how paths resolve.
    /// </summary>
    public string? EndpaperAssetPathTemplate { get; set; }
}
