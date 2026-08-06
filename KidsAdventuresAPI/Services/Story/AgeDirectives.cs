namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// How to write for an age, as opposed to what to teach at that age.
///
/// Deliberately a different partition from <see cref="SkillMatrix.AgeBand"/>, and named
/// differently so the two are never mistaken for each other. A skill changes with what a child
/// can understand, which moves in four steps; sentence length, sound effects and whether a book
/// asks the child to point at the page change with how a child is read to, which moves in three.
/// A seven-year-old belongs in a different skill band from a five-year-old and in the same
/// writing bracket, and both statements are true.
///
/// Passing an age as a number and hoping was the previous arrangement. It produced the same
/// prose for a two-year-old and a nine-year-old.
/// </summary>
public static class AgeDirectives
{
    public enum Bracket
    {
        /// <summary>2-4: read to, on a lap, pointing at the page.</summary>
        SoundAndTouch,

        /// <summary>5-7: read aloud, following a story, laughing at a mistake.</summary>
        RhythmAndHeart,

        /// <summary>8-10: reading along or alone, wanting to work something out.</summary>
        AgencyAndClues
    }

    public static Bracket For(int age) => age switch
    {
        <= 4 => Bracket.SoundAndTouch,
        <= 7 => Bracket.RhythmAndHeart,
        _ => Bracket.AgencyAndClues
    };

    /// <summary>
    /// How many characters besides the hero a book may hold. A two-year-old loses track of a
    /// second friend; a nine-year-old finds one lonely.
    /// </summary>
    public static int MaxSecondaryCharacters(int age) => For(age) switch
    {
        Bracket.SoundAndTouch => 1,
        Bracket.RhythmAndHeart => 2,
        _ => 3
    };

    /// <summary>The writing rules for this age, dropped into the writer's instructions.</summary>
    public static string WritingRules(int age) => For(age) switch
    {
        Bracket.SoundAndTouch =>
            """
            **Sentences: two to four words.** Three or four of them per scene, no more. A child
            this age is listening, not reading, and a long sentence loses them halfway.

            **A sound on every page.** At least one vivid Georgian sound effect per scene —
            „ტუპ-ტუპ!“, „ხრაშ-ხრაშ!“, „წკაპ-წკაპ!“, „ფუუუ!“, „ბუმ!“. Never the same sound twice
            in one book; eight identical bumps is one sound repeated, not a story with sounds.

            **Ask the child to look.** In most scenes, point at the picture — „ნახე, ფოთლის ქვეშ
            რა არის?“, „სად დაიმალა?“, „თითი დაადე!“ At this age a book is something you do
            together, not something you are told.

            **Speech: two to four words, and worth repeating.** A child this age says the lines
            back before they can read them. Keep them tiny, and make it obvious who is talking —
            „ლოლომ თქვა: «ნელა!»“ rather than a bare quotation mark.

            One friend besides the hero, and no more. A second one is a name this child will not
            hold on to.
            """,

        Bracket.RhythmAndHeart =>
            """
            **Three or four sentences per scene**, written to be read aloud without stumbling.
            Vary their length — three of the same length read as a list. At most one line of
            dialogue per scene.

            **Show, don't tell.** Never write the feeling; write what the body did.
            Bad: „შეეშინდა“ · Good: „ნაბიჯი უკან გადადგა“
            Bad: „გახარებული იყო“ · Good: „ტაში დაუკრა“

            **The hero gets something wrong.** Once, somewhere, and harmlessly — steps in the
            puddle, picks the wrong stone, says the name backwards. Not for the plot: children
            trust a character who is not perfect, and they laugh before they trust.

            **Speech carries character.** One line per scene, but the friend must not sound like
            the child: give them a word they keep using, or a way of answering that is theirs.

            At most two friends, arriving one at a time.
            """,

        _ =>
            """
            **Four or five sentences per scene**, written as real paragraphs. This child reads
            along, or alone, and thin text reads as a book for somebody younger.

            **The child solves it.** The whole problem, by their own noticing or their own
            choice. Never magic, never luck, and never an adult arriving to help. A hero who is
            rescued is a hero this age has stopped believing in.

            **Clues in the world.** Scratches on a rock, which way the wind moves the grass,
            tracks that are fresher on one side. Give the child something to work out a page
            before the hero does — being right ahead of the story is the pleasure at this age.

            **Speech should be worth quoting.** Give each character a real manner — one talks too
            much, one answers in three words, one asks questions instead of answering. A child
            this age notices when everybody in a book has the same voice.

            Up to three friends, and they should not all sound alike.
            """
    };
}
