using System.Text;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Prompts;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// The four things this call is given, and there is no fifth.
///
/// Deliberately not <see cref="MasterStoryInput"/>. That record carries an eye colour, an
/// appearance description written from a photograph and the parent's Extra Wish, and every one of
/// them is locked out of the MVP — a prompt builder handed that type would only be one edit away
/// from using them again. A separate input type is how "four inputs" becomes something the
/// compiler enforces rather than something a reviewer has to notice.
/// </summary>
public sealed record CompositeStoryInput
{
    public required string ChildName { get; init; }

    /// <summary><c>1-2</c>, <c>3-5</c> or <c>6+</c>. The number never reaches the prompt.</summary>
    public required string AgeBand { get; init; }

    /// <summary><c>girl</c> or <c>boy</c>.</summary>
    public required string Gender { get; init; }

    /// <summary>The canonical BEKI theme id, which is what the rest of the pipeline keys on.</summary>
    public required string ThemeId { get; init; }

    /// <summary>The same theme as the backend enum, for the Georgian world copy the site shows.</summary>
    public required ThemeType Theme { get; init; }

    public int SpreadCount { get; init; } = BookFormat.SpreadCount;

    /// <summary>The boundary's output, narrowed to what a story call may see.</summary>
    public static CompositeStoryInput From(NormalizedBookInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return new CompositeStoryInput
        {
            ChildName = input.ChildName,
            AgeBand = input.AgeBand,
            Gender = input.ChildGender,
            ThemeId = input.ThemeId,
            Theme = input.Theme
        };
    }
}

/// <summary>
/// PLAN BOOK for the composite pipeline — <see cref="MasterStoryPromptV6"/> with everything the
/// locked MVP decisions forbid taken out, and nothing else changed.
///
/// v6 is the active prompt and it cannot be used here. Read against the handoff's §3 table it
/// breaks five locked decisions at once: it asks for an English version of every spread and an
/// English title (Georgian only); it integrates the Extra Wish (removed from the MVP, and not to
/// be sent to any model); it requires characterLock to state the child's gender and the eye colour
/// the parent chose (the child's likeness is the photograph's job, and the appearance description
/// v6 is fed comes from reading that photograph); and it tells the model, in as many words, that
/// Beki is "a small, floating, magical leaf spirit" (Beki's appearance is fixed by approved
/// artwork, and §3 says to use the canonical name and nothing else).
///
/// The legacy prompt is untouched. Every A5 and flow-misho book in production is written by v6 and
/// keeps being written by v6; this is a sibling selected under the composite flag, so the two
/// cannot drift into each other and neither has a branch inside it.
///
/// One rule is deliberately stricter than v6's, and it is not a style choice — it is the Visual
/// Scenario contract's shape reaching back into the story.
///
/// v6 asks for Beki on spread 1, spread 8 and "at least three others", because on that path a
/// spread's cast list decides whether Beki's reference is attached and therefore whether Beki is
/// drawn. This path has no such switch. <c>visual_scenario_v2.schema.json</c> makes
/// <c>beki_action</c> required and non-empty for every one of the eight spreads, the validator
/// enforces it, and the pipeline composites one approved pose per spread from it: there is no way
/// to express "no Beki on this page" anywhere in the contract, so the composite path puts her on
/// all eight whatever the plan says. A plan that listed her on five would therefore ship eight
/// illustrations contradicting its own stored cast list — the pictures right, the record wrong.
///
/// The honest fix is the one that makes the plan describe what gets printed, so this prompt asks
/// for Beki on every spread and <see cref="CompositePlanRules"/> refuses a plan that does not
/// deliver it. Skipping the composite on a Beki-less spread was the alternative and is wrong: the
/// scenario has no Beki-absent representation to skip on.
///
/// What is kept is kept word for word, for the reason v6 itself gives for keeping v5's wording:
/// two prompt versions are only comparable when the difference between them is the thing under
/// test. The narrative rhythm, the goal-and-choice paragraph, the recurring cast and object rules,
/// the shape-on-the-page rule, the Dumbadze voice, the title rule, the safety block and the
/// "style and camera are code's job" block are all v6's, verbatim where nothing forced a change.
/// </summary>
public static class MasterStoryPromptComposite
{
    /// <summary>The name this prompt is recorded under, beside "v5" and "v6".</summary>
    public const string Version = "composite-v1";

    public static string System(CompositeStoryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return $"""
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

            Beki is the platform's one canonical story character: every book this platform makes
            gives the child the same warm, curious, brave guide and friend. Beki is present in
            every one of the {input.SpreadCount} spreads, so list exactly the id "beki" in every
            spread's characters — never as a cast member, and never as anything else spelled or
            capitalised differently.

            Beki is a name and nothing else. Never state or suggest what Beki is or what Beki looks
            like — not a species, not an animal, not a size, a shape, a material, a colour or a
            costume — in any scene, in any cast entry, or in the story text. Beki's appearance is
            fixed by approved artwork this call never sees, and any description written here would
            contradict the picture that gets printed.

            In Georgian the companion's name is written exactly „ბეკი“, in every grammatical form —
            ბეკიმ, ბეკის, ბეკისთან, ბეკიდან, ბეკიო. Always კ, never ქ: „ბექი“ is a different word
            and is never this character's name.

            Beki is beside the child on all {input.SpreadCount} spreads, without exception. Write
            the story that way: there is no spread of this book where the child is alone. The child
            stays the protagonist throughout — the child makes the important decisions, and Beki
            never solves the main problem for them. Beki guides, reacts, encourages, listens,
            reassures or reveals a path, and does no more than that. On a spread where the child
            must act alone, Beki is still there, watching or waiting, and does not intervene. Beki
            needs no costume for the theme — Beki is Beki in every world.

            Use the selected theme as the world of the story.

            The story needs a clear goal or problem from the first spread, and the child must make
            at least one meaningful choice or discovery that changes how it ends. Do not write an
            errand, and do not write a chain of walking from one clue to the next. Where it fits
            naturally, give the story one memorable magical or emotional turn. Spread
            {input.SpreadCount} fully resolves it: only a subtle feeling that more adventures are
            possible, never a loose thread. The final spread's visual scene must contain one
            concrete, visible continuation signal (a new path appearing, a distant light, a door
            opening, a new star — one image-visible thing) while the story still fully resolves;
            the signal lives in the illustration, not only in the words.

            Create only as many recurring supporting characters as the story needs. None is a valid
            answer. For every recurring supporting character, provide one short, concrete visual
            description. Any supporting character who appears in two or more spreads must be one of
            them, with an id: a character named in two spreads and described in neither is drawn as
            two different characters.

            Create only as many recurring story objects as the story needs — only IMPORTANT objects
            appearing meaningfully in two or more spreads. Ids must be obj_01, obj_02, etc. Provide
            one short stable concrete visual description each. A recurring object's design never
            changes between spreads without a story reason. None is a valid answer. List a spread's
            recurring objects in its objects array.

            The cover shows the child in one inviting moment from this world. No other character,
            creature, animal or vehicle appears in it, and its setting stays simple and iconic —
            one clear suggestion of the world, uncluttered.

            For every spread:
            - write Georgian story text
            - describe one clear visual scene
            - list which characters appear

            Do not create page titles.

            Each spread should contain one clear story moment.

            The visual scene must describe only what should be visible in the illustration, and it
            must name exactly one visual focus — the single thing the reader's eye should land on
            first. Everything else in the scene is there to support it.

            Write worldLock as well: two or three English sentences that fix the constant look of
            this book's world — palette, quality of light, terrain or architecture, and one
            recurring landmark that can appear again. No characters, no story events, no camera or
            shot. It is repeated word for word into every illustration of this book, so everything
            in it must be true of every spread.

            Shape on the page: each spread's Georgian text is written as short lines separated by
            newlines, never as one block. Narration is its own line, and every speaker's words are
            their own line — never two speakers in one line, and never a line of speech with its
            narration attached. This is how the words are arranged, not how many there are: the
            word budget below still holds.

            Voice, for the Georgian story text: write in a warm, simple, spoken storytelling voice,
            in the manner of Nodar Dumbadze's prose — short natural sentences with the rhythm of
            speech; concrete, everyday words a child knows; gentle, humane humor and tenderness.
            Never archaic, bookish or ornate vocabulary, and no long winding constructions. Every
            sentence must read aloud beautifully.

            The title: short, warm and inviting — Georgian words a parent is happy to say aloud at
            bedtime, built from wonder, friendship, discovery or light. Never build the title on a
            harsh, loud or frightening word — roaring, growling, howling, screaming, shrieking
            (ღრიალი, ბრდღვინვა, ყვირილი and their kind), or anything naming danger or menace. If a
            sound or creature matters to the story, the title names the gentle side of it, not the
            noise.

            This book is written in Georgian and in no other language. The title and every spread's
            story text are Georgian. Nothing in this book is written twice in two languages.

            {WordBudget(input.AgeBand)}

            Safety, for readers aged 1–8. These override the theme and the story:
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
            text side, a fold, a margin, a page dimension, typography, a print setting, a text-safe
            area or an instruction about a photograph into any scene. A scene that contains them
            will be sent to the image model twice over.

            You are never told what the child looks like, and you never write it down. No face, no
            hair, no eyes, no skin, no build, no clothing belongs anywhere in this plan. The child's
            likeness comes from a photograph that this call does not receive, and a written guess at
            it would contradict the pictures.

            Return valid JSON only.
            """;
    }

    public static string User(CompositeStoryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var world = StoryWorlds.For(input.Theme);
        var text = new StringBuilder();

        text.AppendLine("Child:");
        text.AppendLine($"- name: {input.ChildName}");
        // The band, never the number. It is mapped once at the input boundary, and a prompt given
        // the raw age would be a second place where "is five a 3-5 or a 6+" gets answered.
        text.AppendLine($"- age band: {input.AgeBand}");
        text.AppendLine($"- gender: {input.Gender}");
        text.AppendLine();

        // v6 sent the enum's own name and nothing else. The subject word is added because it is
        // the gap StoryWorlds was written to close: given only a place name, a model wrote three
        // good books about a valley for a parent who had chosen dinosaurs.
        text.AppendLine($"Theme: {input.ThemeId} — {world.Subject}, {world.Place}");

        /*
          Nothing follows. v6's User continues with the Extra Wish, the appearance description read
          from the child's photograph, and the eye colour the parent chose. All three are locked out
          of the MVP, and this is the method where their absence is visible.
        */

        return text.ToString();
    }

    /// <summary>
    /// How long a spread is, for the one band this book is being written in.
    ///
    /// v6 gave two bands — 2–4 and 5–8 — with a rule for clamping anything outside them. The
    /// locked bands are three and they start at one, so the budgets are resampled onto them rather
    /// than copied: the youngest band is now younger than v6's was, and the oldest is open-ended.
    /// One line is emitted rather than all three, because the band is an input here and a model
    /// given two budgets it does not need will average them.
    /// </summary>
    private static string WordBudget(string ageBand) => ageBand switch
    {
        "1-2" =>
            "This book is for the 1-2 age band. Keep the story very simple and very short — "
            + "approximately 15–25 Georgian words per spread, in short sentences a toddler can "
            + "hear all the way through.",
        "3-5" =>
            "This book is for the 3-5 age band. Keep the story simple and short — approximately "
            + "20–35 Georgian words per spread, easy to read aloud.",
        "6+" =>
            "This book is for the 6+ age band. Use slightly richer language while remaining "
            + "concise and easy to read aloud — approximately 30–45 Georgian words per spread.",
        _ => throw new ArgumentOutOfRangeException(
            nameof(ageBand),
            ageBand,
            "The composite story prompt only writes for the locked age bands 1-2, 3-5 and 6+.")
    };
}
