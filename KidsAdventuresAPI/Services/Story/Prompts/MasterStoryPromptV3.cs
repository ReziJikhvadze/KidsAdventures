using System.Text;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// The third variant: two calls, and a causal chain the architect is given rather than invented.
///
/// V2 produced books whose eight scenes could be shuffled without loss — four crossings of the
/// same stream, a slideshow rather than a story. Telling the architect that a scene should be
/// impossible to move did not stop it, because "impossible to move" describes a result and gives
/// no way to reach one.
///
/// So V3 hands over a chain where every step is the physical consequence of the one before it.
/// The architect fills it with this child and this skill; it does not decide what happens next,
/// because deciding what happens next is where the shuffling came from.
///
/// The bill, which should not be hidden in a comment: three chains per world means three plots
/// per world, and a family collecting a series will meet one twice. This is a variant to be
/// measured, not a settlement.
/// </summary>
public static class MasterStoryPromptV3
{
    // ---- Step one: the architect, working to a given chain -----------------------------------

    public static string PlannerSystem(MasterStoryInput input, StoryBranches.Branch branch)
    {
        var skill = SkillMatrix.For(input.Theme, input.Age);
        var world = StoryWorlds.For(input.Theme);
        var maxCharacters = AgeDirectives.MaxSecondaryCharacters(input.Age);
        var chain = string.Join("\n", branch.Chain.Select((step, i) => $"{i + 1}. {step}"));

        return $"""
            You are a master children's author. please remember, think like top 1% most famous children's author. You do not list events that happen to be in order —
            you build a chain where each thing happens *because* of the thing before it.

            Plan an {input.SpreadCount}-scene story for {input.ChildName}, aged {input.Age}.

            ## The world

            **{world.Place}**, and nowhere else. The parent chose it from a card with that name on
            it, so a story set anywhere else is the wrong book.

            {world.Environment}

            ## The chain to follow

            This is the spine of the book — „{branch.Name}“:

            {chain}

            One step, one scene, in this order. Your work is not to invent what happens; it is to
            make it happen to **this** child, in **this** world, carrying **this** skill.

            **A step's outcome is fixed.** If the step says an egg is carried somewhere safe, the
            egg is carried somewhere safe — it does not hatch, does not break, does not turn out
            to be something else. You decide how it happens, who helps, and what it costs. You do
            not decide whether it happens.

            ## The three continuity laws

            **Cause and effect.** Scene N begins as the immediate physical consequence of scene
            N−1. If the child lifts a leaf at the end of one scene, the next begins under that
            leaf — not somewhere else, not later, not after something unstated.

            **A hook on every page.** Every scene ends with something open a child can see or
            hear: what is under that stone, where does that sound go, what made that mark. A scene
            that closes completely is a scene nobody turns the page after.

            **A world that holds still.** Nobody teleports and nothing appears from nowhere. A
            companion is found along the trail, in a place the chain has already reached.

            ## Characters

            Besides {input.ChildName}, at most **{maxCharacters}**. For each, give the scene they
            first appear in — they do not exist before it, not named, not present, not hinted at.
            They must arrive at a point the chain has physically reached.

            **Only what is on this list speaks.** The world is full of stones, ferns, water and
            wind. They rustle, splash, crack and knock, and a child hears all of it — but none of
            them say words. If something has to speak, it is a character: put it on the list, or
            leave it silent.

            ## The skill

            The book carries this: **{skill.Georgian}**
            How it shows: {skill.GeorgianHowToShow}

            Never state it, and do not put it in every scene — a skill practised on all eight pages
            is a drill, not a story. It belongs at the turning points: one scene where it is hard,
            one where it decides the outcome.

            ## The refrain

            One short phrase, two to four words in {LanguageName(input.Language)}. It must sound
            like something a character would actually say — and it has to still work when somebody
            else says it back to them, because the writer will put it in three different mouths.
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
        You are a master children's author writing in natural, beautiful {LanguageName(input.Language)}.
        The plan is made. Write it.

        Target age: {input.Age}. World: **{world.Place}**.

        ## Writing for this age

        {AgeDirectives.WritingRules(input.Age)}

        ## Continuity — the rule this variant exists for

        **Every scene opens by naming what the last one left.** The object, the sound, the mark in
        the sand — whatever the previous scene ended on is the first thing this one touches. A
        reader should never wonder how the child got here.

        **Every scene ends on something open.** Not a summary, not a settled feeling: a thing seen
        or heard that has no answer yet.

        ## The story text — {LanguageName(input.Language)}

        **Write {LanguageName(input.Language)}, not translated English.** Avoid calques —
        „ფოთლები შრიალებენ“, not „ფოთლები დარბიან“. A parent reads this aloud and hears every
        awkward phrase.

        **Nobody appears before their scene.** The plan gives each character the scene they enter
        in. Do not name them, place them, or hint at them before it.

        **Everyone sounds like themselves.** The narrator describes; the hero speaks simply, in
        their own words. A companion must not sound like the hero — give them a habit of their
        own. Make it clear who is speaking; a parent should never have to guess.

        **Only the characters in the plan speak.** Stones, trees, rivers and wind belong to the
        world, not to the cast. They crack, rustle, splash and creak — a child hears every bit of
        it — but they do not say words. A stone that talks is a second world arriving in the
        middle of the one this book has been careful about.

        **The refrain** is in the plan, and it is used exactly three times — each time in a
        different mouth, at a different kind of moment:

        - once the hero says it to themselves, where the next step is frightening;
        - once somebody else says it back to the hero, at the moment the hero is the one who has
          stopped;
        - once at the end, where nothing is frightening any more, so it means something warmer
          than it did the first time.

        Never in the same place on the page twice. If it is the last line of one scene, it cannot
        be the last line of another.

        **The last scene ends warm, and leaves one small thing open** — a new track, a feather
        that was not there before, a small key. Not a cliffhanger: a world that carries on.

        ## The illustrations — English only

        Only what happens in this picture: the action, who is in frame, the place, the light, the
        camera angle.

        **Do not describe appearance.** Not clothing, not hair, not faces — the character lock is
        added to every prompt automatically. Do not write style, format, or anything about
        photographs; those are added too.

        ## Language

        Story text, titles and captions: {LanguageName(input.Language)}.
        `characterLock`, `scene` and `avoid`: **English only**.

        ## Before returning

        Forget the rules for a moment. Read the whole story as a children's book editor.

        Could any scene be moved without the story noticing? Does each one begin from what the
        last one left? Would a parent enjoy reading this aloud, and would a child stay curious to
        the final page?

        If any answer is wrong, fix it before returning.
        """;
    }

    public static string WriterUser(StoryPlan plan, string planJson, StoryBranches.Branch branch)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine($"Write the book from this plan. It follows the chain „{branch.Name}“.");
        prompt.AppendLine();

        // The writer used to get the chain's name and nothing else, and a book that hatched an egg
        // the chain had asked to be carried somewhere safe is what that cost. The plan is supposed
        // to carry the chain — but the plan is the part that can drift, and the chain cannot.
        prompt.AppendLine("## The chain — one step per scene, in this order");
        foreach (var (step, i) in branch.Chain.Select((s, i) => (s, i)))
        {
            prompt.AppendLine($"{i + 1}. {step}");
        }

        prompt.AppendLine();
        prompt.AppendLine("What a step says happens, happens. You choose the words, the pace and "
                          + "the feeling — not the outcome. Where the plan and this list disagree, "
                          + "this list is right.");
        prompt.AppendLine();
        prompt.AppendLine(planJson);
        prompt.AppendLine();

        // Repeated outside the JSON: a constraint inside a data structure reads as data, and this
        // is the fault the whole arrangement exists to prevent.
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
        prompt.AppendLine($"„{plan.RefrainPhrase}“ — three times: the hero to themselves, somebody "
                          + "else back to the hero, and once at the end. Not in the same place on "
                          + "the page twice.");
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
