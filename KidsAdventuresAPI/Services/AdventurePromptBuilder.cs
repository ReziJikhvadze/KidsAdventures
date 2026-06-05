using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;

namespace AdventurePacks.Api.Services;

internal static class AdventurePromptBuilder
{
    /// <summary>Style when no reference photo — more stylized animated look.</summary>
    public const string AnimatedIllustrationStylePrompt =
        "Premium 3D animated children's movie still, Pixar or DreamWorks quality. " +
        "Expressive cartoon hero, lively dynamic pose, cinematic warm lighting, rich saturated colors, lush magical environment. " +
        "Beautiful polished adventure scene — NOT flat sketch, NOT clipart.";

    /// <summary>Style when a hero photo is provided — likeness first, then polish.</summary>
    public const string PhotoLikenessStylePrompt =
        "High-quality 3D animated children's movie illustration with sharp clear detail. " +
        "CRITICAL: the hero must be clearly the SAME child as the reference photo — preserve exact eye shape and color, " +
        "eyebrows, nose shape, mouth, smile, face outline, cheek fullness, hair color, hair length, hair texture, parting, bangs, " +
        "skin tone, and apparent age. Do not swap in a different child. Do not enlarge eyes or cartoonify away recognizable features. " +
        "Natural friendly expression, beautiful cinematic lighting, vibrant colors, magical adventure background.";

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
        Guid adventureId,
        bool hasHeroPhoto,
        bool hasCharacterAnchor)
    {
        var scene = page.Content.Length > 280 ? page.Content[..280] + "..." : page.Content;
        var parts = new List<string>();

        if (hasHeroPhoto && hasCharacterAnchor)
        {
            parts.Add(
                "Image 1 is the child's real photo — use it as the identity anchor for the face (eyes, nose, mouth, hair, skin tone, age).");
            parts.Add(
                "Image 2 is the hero from page 1 — keep the same outfit, body proportions, and illustrated style; only change the scene.");
            parts.Add("The face must still match Image 1. Do NOT redesign into a different child.");
        }
        else if (hasHeroPhoto)
        {
            parts.Add(
                "Image 1 is a real photo of the child who MUST be the hero of this scene.");
            parts.Add(
                "Illustrate them in a polished animated adventure style while keeping their face unmistakably the same child — " +
                "same eyes, nose, mouth, hair, skin tone, and age as the photo.");
            parts.Add("Parents should instantly recognize their child. Not a generic cartoon kid.");
        }
        else
        {
            var heroLook = string.IsNullOrWhiteSpace(input.ChildAppearanceDescription)
                ? $"Hero child named {input.ChildName}, age {input.Age}"
                : $"Hero child named {input.ChildName}, age {input.Age}: {input.ChildAppearanceDescription}";
            parts.Add(heroLook + ".");
            parts.Add("Keep the hero's face and hair consistent with the description in every scene.");
        }

        if (!string.IsNullOrWhiteSpace(input.ChildAppearanceDescription))
        {
            parts.Add($"Hero appearance details from photo analysis: {input.ChildAppearanceDescription.Trim()}");
        }

        if (hasCharacterAnchor && !hasHeroPhoto)
        {
            parts.Add("Image 1 is the established animated hero — keep the same 3D character design in the new scene.");
        }

        parts.Add(hasHeroPhoto ? PhotoLikenessStylePrompt : AnimatedIllustrationStylePrompt);
        parts.Add("High quality, no text, no words, no letters, no watermark.");
        parts.Add($"Safe for children age {input.Age}. Theme: {input.Theme}.");
        parts.Add($"Page {pageIndex + 1} scene title: {page.Title}.");
        parts.Add($"Scene: {scene}");
        parts.Add($"Adventure id {adventureId}.");

        return string.Join(" ", parts);
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
