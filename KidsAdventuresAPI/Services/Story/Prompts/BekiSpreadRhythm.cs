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
    /// <summary>left | right, per the handoff's table. Spread 8 stays left for its ending.</summary>
    private static readonly string[] TextSides =
        ["left", "right", "left", "right", "left", "right", "left", "left"];

    /// <summary>
    /// One short sentence each, as the handoff asks. They name the intent of the beat rather than
    /// a lens: an image model does more with "hold on the moment of realisation" than with 35mm.
    /// </summary>
    private static readonly string[] Shots =
    [
        "Open wide, showing the whole world the story begins in.",
        "Move closer as something is noticed for the first time.",
        "Frame the action so the movement reads clearly.",
        "Pull back into an atmospheric, quieter composition.",
        "Show the journey with depth and distance ahead.",
        "Hold the major reveal in a dramatic, spacious shot.",
        "Come close for the emotional beat between the characters.",
        "Finish on a cinematic wide shot that looks toward what comes next.",
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
