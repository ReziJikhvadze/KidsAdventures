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
/// **3:2, and that is the whole reason these numbers look the way they do.** The handoff asked
/// for a 440×200 spread, which is 2.2:1, and no image model draws it — gpt-image offers 1:1, 2:3
/// and 3:2 and nothing else. Cropping a 3:2 picture down to 2.2:1 throws away nearly a third of
/// its height, and stretching it is worse. So the book was moved to the shape the artwork is
/// actually drawn in: the spread is 3:2, the picture fills it exactly, and no pixel is discarded.
/// </summary>
public sealed class BekiPrintLayoutOptions
{
    public const string SectionName = "BekiPrintLayout";

    /// <summary>
    /// The finished spread, both leaves together, in millimetres. 440 keeps the handoff's page
    /// width of 220; the height follows from 3:2 rather than being chosen.
    /// </summary>
    public float SpreadWidthMm { get; set; } = 440f;

    /// <summary>
    /// 440 ÷ 1.5. Set this and the width together or the picture stops fitting the sheet — the
    /// one property of this format worth protecting.
    /// </summary>
    public float SpreadHeightMm { get; set; } = 293.3f;

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
}
