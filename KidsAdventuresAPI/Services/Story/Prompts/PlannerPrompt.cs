using System.Text;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Validation;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// What the planner is told.
///
/// The prompt is short on rules and long on inputs, which is the opposite of the engine it
/// replaces. Version one carried dozens of instructions the model was asked to hold in mind at
/// once — continuity, inventory, pacing, page count, JSON validity — and satisfied them roughly
/// as often as not, because nothing checked. Here the rules are code, so the prompt only has to
/// explain the job and hand over the material.
///
/// The rules it does state are the ones a validator cannot repair after the fact: a plan that
/// never had a joke in it cannot be given one by a rewrite that is not allowed to change what
/// happens.
/// </summary>
public static class PlannerPrompt
{
    public static string System(string language) => $"""
        You are the story planner for a personalised children's picture book.

        You do not write the story. You decide what happens, page by page, and hand that plan to
        a writer who will turn it into sentences. Think like a picture-book editor breaking a
        manuscript into spreads: every page is a beat, every beat earns its place.

        Write all reader-facing text in {LanguageName(language)}. Ids stay lowercase ASCII slugs.

        What makes a plan good:

        - The book asks one question on page one and answers it on the last. Everything between
          moves towards that answer.
        - Every page changes something. If a page could be removed and no one would notice, it
          does not belong in the book.
        - Every page ends owing the next one a question. That is what makes a child turn it.
        - Objects are declared before they are used, and every object introduced goes on to
          matter. A key that is found and then forgotten disappoints a child exactly as much as
          a key that vanishes.
        - Characters do not appear and disappear silently. If someone leaves, the story notices.
        - The hero ends the book different from how they started, and the change is earned by
          facing what they were afraid of.
        - Feeling and tempo both vary. Six exciting pages in a row are as flat as six calm ones.
        - At least one page is funny. Children forgive almost anything except being bored.

        Be specific. "They travel onwards" is not a beat. "Rust insists on leading and walks
        them into the wrong side of the ravine" is.
        """;

    public static string User(BookState state)
    {
        var meta = state.Meta;
        var hero = state.Casting.Hero;
        var prompt = new StringBuilder();

        prompt.AppendLine($"Plan a {meta.PageCount} page picture book for {hero.Name}, age {meta.ChildAge}.");
        prompt.AppendLine();

        prompt.AppendLine("## Cast");
        foreach (var character in state.Casting.Characters)
        {
            prompt.AppendLine(
                $"- {character.Id} — {character.Name}, {character.Role.ToString().ToLowerInvariant()}."
                + $" Wants: {character.Personality.Want}. Afraid of: {character.Personality.Fear}."
                + $" Speaks: {character.Voice.Register}.");
        }

        prompt.AppendLine();
        prompt.AppendLine("## The hero's arc");
        prompt.AppendLine(
            $"{hero.Name} begins {hero.Personality.Traits.FirstOrDefault() ?? "uncertain"} and must end the book "
            + $"changed. The change has to be earned by facing {hero.Personality.Fear}, not simply announced.");

        prompt.AppendLine();
        prompt.AppendLine("## Seeds");
        prompt.AppendLine("Build the story around these. They are the reason this book will not resemble the last one.");
        prompt.AppendLine($"- Wonder: {state.Inspiration.WonderSeed}");
        prompt.AppendLine($"- Humour: {state.Inspiration.HumorSeed}");
        prompt.AppendLine($"- Image: {state.Inspiration.VisualSeed}");
        prompt.AppendLine($"- Feeling: {state.Inspiration.EmotionalSeed}");
        prompt.AppendLine($"- Mystery: {state.Inspiration.MysterySeed}");

        AppendMemory(prompt, state.Memory, meta.ChapterNumber);

        prompt.AppendLine();
        prompt.AppendLine("## Required");
        prompt.AppendLine($"- Exactly {meta.PageCount} beats, numbered 1 to {meta.PageCount}.");
        prompt.AppendLine($"- An emotionCurve of exactly {meta.PageCount} entries, matching the beats.");
        prompt.AppendLine($"- At least {StoryScale.MinimumSurprises(meta.PageCount)} surprises, each on a real page.");
        prompt.AppendLine($"- At least {StoryScale.MinimumDistinctEmotions(meta.PageCount)} distinct emotions.");
        prompt.AppendLine("- At least one running thread: something planted early that lands later.");
        prompt.AppendLine("- The final beat has purpose Resolution or Victory, and a null hook.");
        prompt.AppendLine("- Every question opened by a delta is resolved before the end.");

        return prompt.ToString();
    }

    /// <summary>
    /// The repair prompt. It is given the exact failures rather than told to try again, because
    /// a second attempt at the whole plan produces a different set of mistakes rather than a fix
    /// — and throws away the parts that were already right.
    /// </summary>
    public static string Repair(StoryBlueprint blueprint, ValidationReport report)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("Your plan is close, but a checker found faults in it. Fix exactly these and change");
        prompt.AppendLine("nothing else. Keep every page that was not named, keep its goal, and keep the shape");
        prompt.AppendLine("of the story you built.");
        prompt.AppendLine();

        if (report.Blocking.Any())
        {
            prompt.AppendLine("## Must fix");
            foreach (var finding in report.Blocking)
            {
                prompt.AppendLine($"- {finding}");
            }
            prompt.AppendLine();
        }

        if (report.Craft.Any())
        {
            prompt.AppendLine("## Worth fixing");
            foreach (var finding in report.Craft)
            {
                prompt.AppendLine($"- {finding}");
            }
            prompt.AppendLine();
        }

        prompt.AppendLine("## The plan as it stands");
        prompt.AppendLine(StoryJson.Describe(blueprint));
        prompt.AppendLine();
        prompt.AppendLine("Return the whole corrected plan in the same shape.");

        return prompt.ToString();
    }

    /// <summary>
    /// Series memory, and only the parts a planner can act on. Handing over everything a child's
    /// shelf has ever contained would bury the seeds for this book under history.
    /// </summary>
    private static void AppendMemory(StringBuilder prompt, StoryMemory memory, int chapterNumber)
    {
        if (chapterNumber <= 1)
        {
            return;
        }

        var sections = new (string Heading, IReadOnlyList<string> Items)[]
        {
            ("Jokes this child already knows, worth calling back", memory.RunningJokes),
            ("Things characters say", memory.Catchphrases),
            ("Habits companions have", memory.CompanionHabits),
            ("Lessons already learned — do not teach these again", memory.CharacterLessons),
            ("Moments worth referring back to", memory.EmotionalCallbacks),
            ("Planted for a later book", memory.FutureSeeds)
        };

        var present = sections.Where(s => s.Items.Count > 0).ToList();
        if (present.Count == 0)
        {
            return;
        }

        prompt.AppendLine();
        prompt.AppendLine($"## What this child remembers (book {chapterNumber} of the series)");
        foreach (var (heading, items) in present)
        {
            prompt.AppendLine($"{heading}: {string.Join("; ", items.Take(5))}");
        }
    }

    private static string LanguageName(string code) =>
        code.Equals("ka", StringComparison.OrdinalIgnoreCase) ? "Georgian" : "English";
}
