namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// Which side the text sits on, and how each spread is shot.
///
/// The handoff is explicit that this is a code rule and not a decision for the story model, and
/// the reason is visible the moment it is left to the model: eight spreads all come back as
/// centred medium shots with the text wherever the composition happened to leave room, and the
/// book reads as a slideshow with the Georgian landing on a different part of the page each time.
///
/// Fixed here, it is also reviewable — an operator can read the rhythm of a book without
/// generating one.
/// </summary>
public static class BekiSpreadRhythm
{
    /// <summary>
    /// Left, right, left, right, left, right, left, left — the handoff's own alternation (§15),
    /// not a code simplification of it. A book that set its text on the same leaf spread after
    /// spread would read as a slideshow with the Georgian always landing in the same corner;
    /// alternating is what makes the reader's eye move the way a real page-turn does.
    ///
    /// Spread 8 breaks the pattern on purpose. Its lower-right corner is where the Continue
    /// Adventure QR lives, and a QR shares a corner with nothing — so the eighth spread is
    /// forced left instead of following the alternation to "right", the one deliberate
    /// exception in an otherwise mechanical rule.
    /// </summary>
    private static readonly string[] TextSides =
        ["left", "right", "left", "right", "left", "right", "left", "left"];

    /// <summary>
    /// One short sentence each, as the handoff asks — but each now opens by naming a camera
    /// distance, and only then the intent of the beat.
    ///
    /// The first wording named intent alone, on the argument that an image model does more with
    /// "hold on the moment of realisation" than with 35mm. It does, and it also does the same
    /// thing every time: two whole books came back as runs of medium and close compositions, the
    /// framing the model reaches for by default when a scene is a child and a friend doing
    /// something. "Open wide" and "pull back" were read as mood words, not as instructions about
    /// where the camera is. Spreads 1, 4 and 8 in particular have to be wide — the book opens on
    /// a world, turns on an atmosphere and closes looking outward — so the distance is stated
    /// first and the intent follows it, with <see cref="IllustrationPrompt"/> adding the sentence
    /// that says the distance is not negotiable.
    /// </summary>
    private static readonly string[] Shots =
    [
        "Wide establishing shot: keep the camera far back so the child is small inside the whole "
        + "world the story begins in.",
        "Medium shot: closer now, framing the child from about the waist up as something is "
        + "noticed for the first time.",
        "Full-figure action shot: whole bodies in frame with room around them, so the movement "
        + "reads clearly.",
        "Dramatic atmospheric wide shot: pull far back and let the place and its light dominate "
        + "the small figures in it.",
        "Wide travelling shot: near foreground, deep middle distance and a far horizon, showing "
        + "the journey ahead.",
        "Medium-wide shot: hold the major reveal large and unmistakable, with open space around "
        + "it.",
        "Close shot: come in tight on the faces for the emotional beat between the characters.",
        "Cinematic wide shot: the characters small against an open view, looking out toward what "
        + "comes next.",
    ];

    /// <summary>1-based, matching the spread numbers a plan uses.</summary>
    public static string TextSideFor(int spreadNumber) => Pick(TextSides, spreadNumber);

    public static string ShotFor(int spreadNumber) => Pick(Shots, spreadNumber);

    /// <summary>
    /// A spread number outside the table falls back to the last entry rather than throwing. The
    /// table is written for eight; a plan that returns nine is a fault worth surviving, because
    /// the alternative is losing a whole generated book to an index.
    /// </summary>
    private static string Pick(string[] table, int spreadNumber)
    {
        if (spreadNumber < 1) return table[0];
        return spreadNumber > table.Length ? table[^1] : table[spreadNumber - 1];
    }
}
