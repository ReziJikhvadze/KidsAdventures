using System.Text;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// The second variant: two calls instead of one.
///
/// V1 asks a single call to plan a book, write it, and describe nine illustrations at once. It
/// works, and it produces two faults reliably. Companions appear in scene one having never been
/// introduced, because a model holding eight scenes in mind reaches for a character it has not
/// established. And the prose comes out mechanical, because the same pass is satisfying a page of
/// craft rules while trying to write.
///
/// So the decisions are made first — the cast, the scene each character may enter, the shape of
/// each page, the refrain — and a second call writes with those settled and a far shorter set of
/// rules in front of it.
///
/// The identity guarantee survives the split, which was the reason not to split before. The
/// character lock is written once by the architect and quoted into every illustration prompt by
/// <see cref="IllustrationPrompt"/>; the writer is told not to describe appearance at all.
/// Nothing has to carry a face between calls, so nothing can drop it.
/// </summary>
public static class MasterStoryPromptV2
{
    // ---- Step one: the architect ------------------------------------------------------------

    public static string PlannerSystem(MasterStoryInput input)
    {
        var skill = SkillMatrix.For(input.Theme, input.Age);
        var world = StoryWorlds.For(input.Theme);
        var maxCharacters = AgeDirectives.MaxSecondaryCharacters(input.Age);

        return $"""
            You are a children's book architect. You decide what happens; somebody else writes it.

            Plan an {input.SpreadCount}-scene story for a {input.Age}-year-old child.

            ## The world

            This book is set in **{world.Place}**, and nowhere else. The parent chose it from a
            card with that name on it, so a story set anywhere else is the wrong book.

            {world.Environment}

            Use what is there. The writer will be asked for something heard, touched or smelled in
            most scenes, and can only reach for what you put in the plan.

            ## Characters

            Besides the hero, {input.ChildName}, there may be **at most {maxCharacters}**. A book
            this short cannot hold more, and a child this age cannot follow more.

            For each of them, decide the scene they first appear in, and give the number. They do
            not exist in the story before it — not named, not present, not hinted at.

            ## The skill

            The book is built around this: **{skill.Georgian}**
            How it should show: {skill.GeorgianHowToShow}

            Never state it, and do not put it in every scene. A skill practised on all eight pages
            is not a story, it is a drill: the same admission and the same small step, eight times.
            It belongs at the turning points — one scene where it is hard, one where it decides
            the outcome. Elsewhere the child is simply in the world.

            What every scene does need is its own purpose. Give each one a different job:
            noticing, meeting, trying and failing, being surprised, choosing, arriving.

            ## The refrain

            Invent one short phrase, two to four words in {LanguageName(input.Language)}, that
            recurs three times. It must sound like something a character would actually say — a
            phrase inserted because a plan asked for one is worse than none.

            ## Shape

            Beginning: something changes. Middle: the child tries. Ending: the child succeeds
            because of their own choice — not magic, not an adult, not luck.

            Each scene must be impossible to move. If scenes three and six could swap places
            without the story losing anything, one of them is not a scene.

            The obstacles must differ **in kind**, not in scenery. One physical — something in the
            way. One social — somebody to be understood or asked. One inward — something the child
            has decided about themselves. Four stones to step across is one obstacle drawn four
            times.

            Every scene leaves one question a child would want answered.
            """;
    }

    public static string PlannerUser(MasterStoryInput input)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("## მიღებული პარამეტრები");
        prompt.AppendLine($"- ბავშვის სახელი: {input.ChildName}");
        prompt.AppendLine($"- ასაკი: {input.Age}");
        prompt.AppendLine($"- სქესი: {GenderWord(input.Gender)}");
        prompt.AppendLine($"- სამყარო: {StoryWorlds.For(input.Theme).Place}");
        prompt.AppendLine($"- თვალის ფერი: {input.EyeColor}");
        prompt.AppendLine($"- ისტორიის ენა: {LanguageName(input.Language)} ენა");
        prompt.AppendLine($"- სცენების რაოდენობა: {input.SpreadCount}");

        if (!string.IsNullOrWhiteSpace(input.AppearanceDescription))
        {
            prompt.AppendLine();
            prompt.AppendLine("## ბავშვის გარეგნობა (ატვირთული ფოტოდან, ინგლისურად)");
            prompt.AppendLine(input.AppearanceDescription.Trim());
            // Stated after the description and marked as deciding, because the two can
            // disagree: a photograph read as blue against a parent who chose green.
            prompt.AppendLine($"Eye colour: **{input.EyeColor}** — this is what the parent chose "
                              + "and it decides, whatever the photograph appears to show.");
            prompt.AppendLine();
            prompt.AppendLine("ეს არის characterLock-ის საფუძველი — გამოიყენე თითქმის უცვლელად.");
            prompt.AppendLine("არაფერი დაუმატო გარეგნობას, რაც აღწერაში არ წერია.");
        }

        if (!string.IsNullOrWhiteSpace(input.ExtraWishes))
        {
            prompt.AppendLine();
            prompt.AppendLine("## მშობლის სურვილი");
            prompt.AppendLine(input.ExtraWishes.Trim());
            prompt.AppendLine("ეს სურვილი ისტორიაში ბუნებრივად უნდა ჩაიქსოვოს, არა გვერდით ნახსენები.");
        }

        return prompt.ToString();
    }

    // ---- Step two: the writer ---------------------------------------------------------------

    public static string WriterSystem(MasterStoryInput input)
    {
        var world = StoryWorlds.For(input.Theme);

        return $"""
        You are a children's book writer. The plan is already made. Write it.

        Target age: {input.Age}. World: **{world.Place}**.

        ## Writing for this age

        {AgeDirectives.WritingRules(input.Age)}

        ## The story text — {LanguageName(input.Language)}

        **Write Georgian, not translated English.** Avoid calques — „ფოთლები შრიალებენ“, not
        „ფოთლები დარბიან“. This is read aloud by a parent who will hear every awkward phrase.

        **Nobody appears before their scene.** The plan gives every character the scene they enter
        in. Do not name them, place them, or hint at them before it.

        **Everyone sounds like themselves.** The narrator describes; the hero speaks simply, in
        their own words, sometimes in half a sentence. A companion must not sound like the hero —
        give them a habit that is theirs: a word they always use, a way of starting, a thing they
        always notice first. A companion whose only line is the refrain is not a character.

        Make it clear who is speaking. A parent reading aloud should never have to guess.

        **The refrain** is in the plan. Use it three times, in three different positions — once
        near a beginning, once mid-scene, once inside dialogue — and let it mean something
        different each time: playful, then brave, then triumphant. Never end three pages with it.

        Every scene leaves one question open. A child turns the page because they need to know.

        **The last scene ends warm, and leaves one small thing open.** The problem is solved and
        the child is safe — then a new track in the sand, a feather that was not there before, a
        small key. Not a cliffhanger: a world that carries on after the book closes.

        ## The illustrations — English only

        Write only what happens in this picture: the action, who is in frame, the place, the
        light, the camera angle.

        **Do not describe appearance.** Not clothing, not hair, not faces. The character lock is
        added to every prompt automatically, and repeating it spends the description on words that
        are already there.

        Do not write style, format, or anything about photographs. Those are added too.

        ## Language

        Story text, titles and captions: {LanguageName(input.Language)}.
        `characterLock`, `scene` and `avoid`: **English only**.

        ## Before returning

        Forget the rules for a moment. Read the whole story as a children's book editor.

        Would a parent enjoy reading this aloud? Would a child stay curious to the last page?
        Does every scene earn its place? Does the hero become more lovable?

        If any answer is no, improve it before returning.
        """;
    }

    public static string WriterUser(StoryPlan plan, string planJson)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("Write the book from this plan. Follow it exactly.");
        prompt.AppendLine();
        prompt.AppendLine(planJson);
        prompt.AppendLine();

        // Said again in plain sentences outside the JSON. A constraint buried in a data structure
        // reads as data; this is the one fault the plan exists to fix, so it is worth twice.
        if (plan.CharacterManifest.Count > 0)
        {
            prompt.AppendLine("## When each character may appear");
            foreach (var character in plan.CharacterManifest.OrderBy(c => c.IntroducedInSpread))
            {
                prompt.AppendLine(
                    $"- **{character.Name}** ({character.Role}): first appears in scene "
                    + $"{character.IntroducedInSpread}. Not before — not named, not present, "
                    + "not hinted at.");
            }

            prompt.AppendLine();
        }

        prompt.AppendLine("## The refrain");
        prompt.AppendLine($"„{plan.RefrainPhrase}“ — three times, in three different positions.");
        prompt.AppendLine();
        prompt.AppendLine("## Return");
        prompt.AppendLine("- concept: the title from the plan, and its outline;");
        prompt.AppendLine($"- spreads: exactly {plan.Outline.Count}, each with title, caption, text and its illustration;");
        prompt.AppendLine("- characterLock: exactly as it is in the plan, unchanged;");
        prompt.AppendLine("- cover: the hero in this world, with calm space for a title.");

        return prompt.ToString();
    }

    private static string GenderWord(string gender) =>
        gender.Trim().ToLowerInvariant() switch
        {
            "girl" or "female" or "გოგო" => "გოგო",
            "boy" or "male" or "ბიჭი" => "ბიჭი",
            _ => "ბავშვი"
        };

    private static string LanguageName(string language) =>
        language.Trim().ToLowerInvariant() switch
        {
            "en" or "eng" or "english" => "English",
            _ => "Georgian"
        };
}
