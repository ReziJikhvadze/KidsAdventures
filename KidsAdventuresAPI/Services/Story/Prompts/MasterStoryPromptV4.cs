using System.Text;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>The fourth variant: a voice, the inputs, and nothing else.</summary>
public static class MasterStoryPromptV4
{
    public static string System(MasterStoryInput input) =>
        $"""
        You are a children's author. Write a {input.SpreadCount}-scene picture book.

        Write as Iakob Gogebashvili wrote: plain everyday words a child already uses, short
        sentences, and a moral the hero earns through what they do — kindness, honesty, courage,
        care for others. Never state the lesson outright or end on one.

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
        """;

    public static string User(MasterStoryInput input)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine($"- ბავშვის სახელი: {input.ChildName}");
        prompt.AppendLine($"- ასაკი: {input.Age}");
        prompt.AppendLine($"- სქესი: {input.Gender}");
        var world = StoryWorlds.For(input.Theme);
        prompt.AppendLine($"- თემა: {world.Subject}");
        prompt.AppendLine($"- სამყარო: {world.Place}");
        prompt.AppendLine($"- თვალის ფერი: {input.EyeColor}");
        prompt.AppendLine($"- ენა: {input.Language}");
        prompt.AppendLine($"- სცენების რაოდენობა: {input.SpreadCount}");

        if (!string.IsNullOrWhiteSpace(input.AppearanceDescription))
        {
            prompt.AppendLine($"- გარეგნობა: {input.AppearanceDescription.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(input.ExtraWishes))
        {
            prompt.AppendLine($"- მშობლის სურვილი: {input.ExtraWishes.Trim()}");
        }

        return prompt.ToString();
    }

    private static string LanguageName(string language) =>
        language.Trim().ToLowerInvariant() switch
        {
            "en" or "eng" or "english" => "English",
            _ => "Georgian"
        };
}
