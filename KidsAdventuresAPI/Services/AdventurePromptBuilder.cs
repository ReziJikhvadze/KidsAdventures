using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;

namespace AdventurePacks.Api.Services;

internal static class AdventurePromptBuilder
{
    private static readonly string[] StorySeeds =
    [
        "A mysterious map appears in the hero's backpack.",
        "A friendly creature offers a riddle before the path continues.",
        "A sudden storm reveals a hidden doorway.",
        "An old song holds the clue to the next challenge.",
        "A bridge made of light appears only for the brave.",
        "A constellation guides the team through the night.",
        "A treasure is not gold, but kindness shared with friends.",
        "A lost compass spins wildly near something wonderful.",
        "A garden of glowing plants whispers encouragement.",
        "A race against time ends with teamwork and laughter."
    ];

    private static readonly string[] ToneSeeds =
    [
        "Warm, playful, and full of wonder.",
        "Curious and gently humorous.",
        "Epic but reassuring — never scary.",
        "Cozy bedtime-adventure energy.",
        "Bright Saturday-morning cartoon energy."
    ];

    public static string BuildStoryPrompt(AdventureGenerationInput input, Guid adventureId)
    {
        var familyMembersText = input.FamilyMembers.Count == 0
            ? "No family members provided."
            : string.Join(Environment.NewLine, input.FamilyMembers.Select(m =>
                $"- {m.Name} ({m.Relationship}){FormatAppearance(m.AppearanceDescription)}"));

        var storySeed = StorySeeds[Random.Shared.Next(StorySeeds.Length)];
        var toneSeed = ToneSeeds[Random.Shared.Next(ToneSeeds.Length)];
        var languageName = ResolveLanguageName(input.StoryLanguage);

        var lines = new List<string>
        {
            "You are creating a personalized kids adventure pack.",
            "Return ONLY valid JSON matching this exact schema:",
            "{",
            "  \"title\": \"\",",
            "  \"theme\": \"\",",
            "  \"childName\": \"\",",
            "  \"storyPages\": [{ \"title\": \"\", \"content\": \"\" }],",
            "  \"activities\": [{ \"type\": \"\", \"title\": \"\", \"content\": \"\" }],",
            "  \"certificate\": { \"title\": \"\", \"text\": \"\" }",
            "}",
            string.Empty,
            "Rules:",
            "- Make the child the main hero.",
            "- Include all family members as supporting characters.",
            $"- Write the entire pack in {languageName}.",
            $"- Keep language age-appropriate for age {input.Age}.",
            "- Keep the tone positive and educational.",
            "- Create 4 story pages with distinct scenes and titles.",
            "- Create 5 activities including quizzes, puzzles, and drawing challenges.",
            "- Never include markdown, explanations, or extra text outside JSON.",
            $"- Adventure ID (must be unique): {adventureId}",
            $"- Story hook to weave in: {storySeed}",
            $"- Narrative tone: {toneSeed}",
            "- Do not reuse plots, openings, or endings from typical templates.",
            string.Empty,
            "Input:",
            $"Child Name: {input.ChildName}",
            $"Child Age: {input.Age}",
            $"Theme: {input.Theme}",
            FormatAppearanceLine("Hero appearance (keep consistent in story)", input.ChildAppearanceDescription),
            $"Family Members:{Environment.NewLine}{familyMembersText}"
        };

        if (!string.IsNullOrWhiteSpace(input.OptionalStoryNotes))
        {
            lines.Add($"Parent's optional wishes (include naturally if appropriate): {input.OptionalStoryNotes.Trim()}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildStoryImagePrompt(
        AdventureGenerationInput input,
        StoryPageDto page,
        int pageIndex,
        Guid adventureId)
    {
        var scene = page.Content.Length > 280 ? page.Content[..280] + "..." : page.Content;
        var heroLook = string.IsNullOrWhiteSpace(input.ChildAppearanceDescription)
            ? $"Hero child named {input.ChildName}"
            : $"Hero child named {input.ChildName}: {input.ChildAppearanceDescription}";

        return string.Join(" ", new[]
        {
            "Children's book illustration, colorful, soft lighting, friendly characters,",
            "whimsical storybook style, high quality, no text, no words, no letters, no watermark.",
            $"Safe for children age {input.Age}. Theme: {input.Theme}.",
            heroLook + ".",
            "Keep the hero's face and hair consistent with the description in every scene.",
            $"Page {pageIndex + 1} scene title: {page.Title}.",
            $"Scene description: {scene}",
            $"Adventure id {adventureId} — make this illustration visually unique."
        });
    }

    private static string ResolveLanguageName(string code) => code.ToLowerInvariant() switch
    {
        "ka" => "Georgian",
        "es" => "Spanish",
        "fr" => "French",
        "de" => "German",
        _ => "English"
    };

    private static string FormatAppearance(string? appearance) =>
        string.IsNullOrWhiteSpace(appearance) ? "" : $" — looks like: {appearance}";

    private static string FormatAppearanceLine(string label, string? appearance) =>
        string.IsNullOrWhiteSpace(appearance) ? "" : $"{label}: {appearance}";
}
