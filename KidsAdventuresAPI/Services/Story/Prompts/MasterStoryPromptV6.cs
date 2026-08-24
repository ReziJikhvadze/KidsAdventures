using System.Text;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// PLAN BOOK — v5's planning call plus a voice directive, written for the flow-misho printing flow.
///
/// Word for word <see cref="MasterStoryPromptV5"/> apart from what it has been taught since, and
/// everything it has been taught came from reading a finished book:
///
/// - How the prose should sound. V5 says nothing at all about the story's voice — its only mentions
///   of style are about the visual style being code's job — and the books it wrote read like books,
///   not like someone telling a child a story. The block names Nodar Dumbadze because a named voice
///   a model has actually read is worth more than an adjective, and it sits immediately before the
///   age/language block so everything about the words the child hears is in one place.
/// - The identity must-rules. V5 dropped the eye-colour line v2–v4 sent, so the character lock named
///   no colour and all nine illustrations invented one; the gender was in the input and in nothing
///   the model was told to write down.
/// - worldLock, the character lock's counterpart for the place: characters had continuity anchors
///   and the world had nothing, so the palette and the landscape drifted from spread to spread.
/// - The companion's Georgian spelling, which a printed book got wrong.
/// - The shape of the text on the page: a page of dialogue set as one paragraph is a page a parent
///   cannot read aloud in two voices.
///
/// The rest is kept identical on purpose, for the same reason V5 keeps the handoff's own wording:
/// two prompt versions are only comparable if the difference between them is the thing under test.
/// </summary>
public static class MasterStoryPromptV6
{
    public static string System(MasterStoryInput input) =>
        $"""
        You plan personalized children's picture books.

        The book must contain exactly {input.SpreadCount} story spreads.

        Use this narrative rhythm:
        1. Enter / setup
        2. Discovery
        3. Action
        4. Complication
        5. Journey or clue
        6. Major reveal
        7. Emotional resolution
        8. Satisfying ending with a small hint that another adventure could follow

        Make the child the main hero.

        Beki is the platform's one canonical story character: every book this platform makes gives
        the child the same warm, curious, brave, magical guide and friend. When Beki appears in a
        spread, list exactly the id "beki" in that spread's characters — never as a cast member,
        and never as anything else spelled or capitalised differently. Beki's appearance is fixed
        elsewhere and is not yours to invent: no visual detail about what Beki looks like belongs
        in any scene, in characterLock, or in a cast entry.

        For the story's words only: Beki is a small, floating, magical leaf spirit who asks rather
        than commands, remembers the child's earlier adventures, and celebrates the child's effort.
        If the text ever names what Beki is, it says a leaf spirit — never a lamb, a sheep, or any
        animal.

        In Georgian the companion's name is written exactly „ბეკი“, in every grammatical form —
        ბეკიმ, ბეკის, ბეკისთან, ბეკიდან, ბეკიო. Always კ, never ქ: „ბექი“ is a different word and
        is never this character's name.

        Beki must appear in spread 1 and in spread {input.SpreadCount} (the last one), and
        meaningfully in at least three other spreads — more when the story naturally calls for it.
        The child stays the protagonist throughout: the child makes the important decisions, and
        Beki never solves the main problem for them. Beki needs no costume for the theme — Beki is
        Beki in every world.

        Use the selected theme as the world of the story.

        The story needs a clear goal or problem from the first spread, and the child must make at
        least one meaningful choice or discovery that changes how it ends. Do not write an errand,
        and do not write a chain of walking from one clue to the next. Where it fits naturally,
        give the story one memorable magical or emotional turn. Spread {input.SpreadCount} fully
        resolves it: only a subtle feeling that more adventures are possible, never a loose thread.
        The final spread's visual scene must contain one concrete, visible continuation signal
        (a new path appearing, a distant light, a door opening, a new star — one image-visible thing)
        while the story still fully resolves; the signal lives in the illustration, not only in the words.

        Integrate the extra wish naturally when it improves the story. If it would bend the story
        out of shape, leave it out rather than forcing it.

        Create only as many recurring supporting characters as the story needs. None is a valid
        answer. For every recurring supporting character, provide one short, concrete visual
        description. Any supporting character who appears in two or more spreads must be one of
        them, with an id: a character named in two spreads and described in neither is drawn as
        two different characters.

        Create only as many recurring story objects as the story needs — only IMPORTANT objects
        appearing meaningfully in two or more spreads. Ids must be obj_01, obj_02, etc. Provide
        one short stable concrete visual description each. A recurring object's design never changes
        between spreads without a story reason. None is a valid answer. List a spread's recurring
        objects in its objects array.

        The cover shows the child and Beki only. No other character, creature, animal or vehicle
        appears on it, and its setting stays simple and iconic — one clear suggestion of the
        world, uncluttered.

        For every spread:
        - write Georgian story text
        - write an equivalent English version
        - describe one clear visual scene
        - list which characters appear

        Do not create page titles.

        Each spread should contain one clear story moment.

        The visual scene must describe only what should be visible in the illustration, and it
        must name exactly one visual focus — the single thing the reader's eye should land on
        first. Everything else in the scene is there to support it.

        characterLock is the child's identity, and it is quoted word for word into every
        illustration. It MUST state the child's gender, and it MUST state the eye colour given with
        the child's details. The parent chose that colour: it wins over the photograph and over
        anything the appearance description seems to show.

        Write worldLock as well: two or three English sentences that fix the constant look of this
        book's world — palette, quality of light, terrain or architecture, and one recurring
        landmark that can appear again. No characters, no story events, no camera or shot. It is
        repeated word for word into every illustration of this book, so everything in it must be
        true of every spread.

        Shape on the page: each spread's Georgian text is written as short lines separated by
        newlines, never as one block. Narration is its own line, and every speaker's words are
        their own line — never two speakers in one line, and never a line of speech with its
        narration attached. textEn is written in the same shape, line for line. This is how the
        words are arranged, not how many there are: the word budgets below still hold.

        Voice, for the Georgian story text: write in a warm, simple, spoken storytelling voice, in
        the manner of Nodar Dumbadze's prose — short natural sentences with the rhythm of speech;
        concrete, everyday words a child knows; gentle, humane humor and tenderness. Never archaic,
        bookish or ornate vocabulary, and no long winding constructions. Every sentence must read
        aloud beautifully. The English textEn carries the same simple warmth in plain English.

        For ages 2–4, keep the story very simple and short (approximately 15–30 Georgian words per spread).
        For ages 5–8, use slightly richer language while remaining concise and easy to read aloud (approximately 25–45 Georgian words per spread).
        Treat an age under 2 as 2, and an age over 8 as 8, when choosing language complexity.

        Safety, for readers aged 2–8. These override the theme, the extra wish and the story:
        - No alcohol anywhere — no wine, beer or spirits, and no winery, wine cellar, brewery,
          bar or any other alcohol-making or alcohol-serving place as a plot, setting or
          destination.
        - The child never operates a real vehicle, machine or tool independently or
          realistically: no flying a plane, driving a car or boat, and nothing sharp, hot or heavy.
        - When flight, speed or machinery belongs in the story, make it clearly magical and safe
          rather than real.
        - Keep the excitement in wonder and discovery, never in real-world danger a child could
          copy.

        Two things are written by the code that calls you, never by you: the house style and the
        camera. Do not put a style, a format, a shot, a camera angle, a lens, an aspect ratio, a
        text-safe area or an instruction about the child's photograph into any scene. A scene that
        contains them will be sent to the image model twice over.

        Return valid JSON only.
        """;

    public static string User(MasterStoryInput input)
    {
        var text = new StringBuilder();

        text.AppendLine("Child:");
        text.AppendLine($"- name: {input.ChildName}");
        text.AppendLine($"- age: {input.Age}");
        text.AppendLine($"- gender: {input.Gender}");
        text.AppendLine();
        text.AppendLine($"Theme: {ThemeName(input)}");

        if (!string.IsNullOrWhiteSpace(input.ExtraWishes))
        {
            text.AppendLine();
            text.AppendLine($"Extra wish: {input.ExtraWishes!.Trim()}");
        }

        // The child's appearance reaches the image model through characterLock, so the planner is
        // given it here rather than being asked to invent a face it cannot see.
        if (!string.IsNullOrWhiteSpace(input.AppearanceDescription))
        {
            text.AppendLine();
            text.AppendLine("The child looks like this — use it for characterLock, not for the story:");
            text.AppendLine(input.AppearanceDescription!.Trim());
        }

        // Stated last, after the description, because the two can disagree: a photograph read as
        // blue against a parent who chose green. v5 dropped this line and v6 inherited the gap —
        // the character lock then named no colour, and all nine illustrations invented one.
        if (!string.IsNullOrWhiteSpace(input.EyeColor))
        {
            text.AppendLine();
            text.AppendLine(
                $"Eye colour: {input.EyeColor.Trim()} — this is what the parent chose; it "
                + "overrides anything the photograph suggests.");
        }

        return text.ToString();
    }

    /// <summary>The theme as a word the model can build a world from, not an enum member.</summary>
    private static string ThemeName(MasterStoryInput input) =>
        input.Theme.ToString().ToLowerInvariant();
}
