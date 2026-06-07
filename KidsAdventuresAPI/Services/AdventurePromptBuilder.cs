using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;

namespace AdventurePacks.Api.Services;

internal static class AdventurePromptBuilder
{
    /// <summary>Style when no reference photo — full Pixar-style CG illustration.</summary>
    public const string AnimatedIllustrationStylePrompt =
        "Full-frame still from a premium 3D animated children's movie (Pixar / DreamWorks quality). " +
        "Stylized CG character with expressive cartoon proportions, soft subsurface skin, big lively eyes, cinematic rim lighting, " +
        "rich saturated colors, depth of field, magical environment. " +
        "MUST look like rendered animation — NOT a photograph, NOT a photo filter, NOT flat clipart.";

    /// <summary>Style when a hero photo is provided — Pixar character inspired by the child, never a photo edit.</summary>
    public const string PixarFromPhotoStylePrompt =
        "Create a FULL Pixar-style 3D animated movie still. The reference photo is ONLY for character design inspiration — " +
        "hair color, hair style, skin tone, apparent age, and general vibe — then completely RE-DRAW the child as a stylized CG cartoon hero. " +
        "CRITICAL: output must look like a Pixar film frame (Inside Out, Coco, Luca, Turning Red), NOT a real photo, NOT a lightly edited portrait, " +
        "NOT photorealistic skin, NOT visible photographic texture, NOT a face-swap or filter effect. " +
        "Use classic animated proportions: slightly larger expressive eyes, smooth stylized skin, rounded friendly features, " +
        "clean modeled hair, vibrant costume. Parents should recognize their child through hair and coloring, not through a realistic face copy. " +
        "Cinematic warm lighting, shallow depth of field, polished render quality.";

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

        var pageCount = input.StoryPageCount > 0 ? input.StoryPageCount : AdventureStoryConstants.FullPageCount;
        pageCount = Math.Min(pageCount, AdventureStoryConstants.FullPageCount);
        var storyArc = pageCount <= AdventureStoryConstants.WelcomeGiftPageCount
            ? "- Story arc: page 1 introduces the adventure, page 2 is a warm happy ending."
            : "- Story arc: pages 1–2 setup the adventure, 3–4 build excitement, page 5 is the climax, page 6 is a warm happy ending.";

        var lines = new List<string>
        {
            "You are creating a personalized kids adventure pack.",
            "Return ONLY valid JSON matching this exact schema:",
            "{",
            "  \"title\": \"\",",
            "  \"theme\": \"\",",
            "  \"childName\": \"\",",
            "  \"storyPages\": [{ \"title\": \"\", \"content\": \"\" }]",
            "}",
            string.Empty,
            "Rules:",
            "- Make the child the main hero.",
            "- Include all family members as supporting characters.",
            $"- Write the entire pack in {languageName}.",
            $"- Keep language age-appropriate for age {input.Age}.",
            "- Keep the tone positive and educational.",
            $"- Create exactly {pageCount} story pages — no more, no fewer — with distinct scenes and titles (story text only — no quizzes or activities).",
            "- Never add extra pages beyond the required count.",
            storyArc,
            "- Each page should be 1–2 short paragraphs — concise for read-aloud; do not pad with filler.",
            "- Never include markdown, code fences (```), explanations, or extra text outside JSON.",
            "- The response must start with { and end with } — raw JSON only.",
            $"- Adventure ID (must be unique): {adventureId}",
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
            lines.Add(string.Empty);
            var wishPages = pageCount <= AdventureStoryConstants.WelcomeGiftPageCount
                ? "both pages"
                : pageCount <= AdventureStoryConstants.FullPageCount
                    ? "at least 2 pages"
                    : "at least 3 pages";
            lines.Add($"REQUIRED parent wishes (highest priority — weave into the plot on {wishPages}, not just one mention):");
            lines.Add(input.OptionalStoryNotes.Trim());
            lines.Add("- Parent wishes override any generic story hook if they conflict.");
        }
        else
        {
            lines.Add($"- Story hook to weave in: {storySeed}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildStoryImagePrompt(
        AdventureGenerationInput input,
        StoryPageDto page,
        int pageIndex,
        Guid adventureId,
        bool hasCharacterAnchor,
        IReadOnlyList<CastPhotoReference> castPhotos)
    {
        var scene = page.Content.Length > 280 ? page.Content[..280] + "..." : page.Content;
        var parts = new List<string>
        {
            "TASK: Illustrate this story page as a Pixar-quality 3D animated movie still using the attached reference photo(s).",
            "CHARACTER IDENTITY LOCK (non-negotiable — zero stylistic drift between reference and output):"
        };

        var imageIndex = 1;
        var heroDna = string.IsNullOrWhiteSpace(input.ChildAppearanceDescription)
            ? null
            : input.ChildAppearanceDescription.Trim();

        if (hasCharacterAnchor)
        {
            var dnaSuffix = heroDna is null ? "" : $" Hero DNA (must match): {heroDna}";
            parts.Add(
                $"Reference Image {imageIndex}: LOCKED HERO — copy the attached Pixar CG cartoon from page 1 EXACTLY. " +
                "Same face shape, eyes, nose, hair color/style, skin tone, outfit, and body proportions — zero redesign. " +
                "Change ONLY pose, expression, camera angle, background, and scene action." +
                dnaSuffix);
            imageIndex++;
        }

        foreach (var cast in castPhotos)
        {
            var role = cast.IsHero ? "HERO CHILD (main character)" : $"FAMILY — {cast.Relationship}";
            if (cast.Bytes is { Length: > 0 })
            {
                var dnaSuffix = cast.IsHero && heroDna is not null ? $" DNA: {heroDna}" : "";
                parts.Add(
                    $"Reference Image {imageIndex}: {cast.Name} ({role}). Real photo — transform into Pixar 3D CG; " +
                    "preserve exact hair color/style, skin tone, age, and face from the photo. NOT photorealistic, NOT a photo filter." +
                    dnaSuffix);
                if (cast.IsHero && !hasCharacterAnchor)
                {
                    parts.Add(PixarFromPhotoStylePrompt);
                }
            }
            else
            {
                var dna = string.IsNullOrWhiteSpace(cast.AppearanceDescription)
                    ? $"Invent a consistent look for {cast.Name}."
                    : cast.AppearanceDescription.Trim();
                parts.Add($"Reference Image {imageIndex}: {cast.Name} ({role}). DNA: {dna}");
            }

            imageIndex++;
        }

        if (!hasCharacterAnchor && castPhotos.Count == 0)
        {
            var heroLook = heroDna is null
                ? $"Hero child named {input.ChildName}, age {input.Age}"
                : $"Hero child named {input.ChildName}, age {input.Age}: {heroDna}";
            parts.Add($"No reference photos — invent a consistent Pixar hero: {heroLook}.");
        }

        parts.Add(
            "STYLE: Pixar/DreamWorks 3D cartoon still — stylized CG, cinematic lighting, NOT photorealistic, NOT a photo filter. " +
            "Only cast in this scene. No text or watermarks.");
        parts.Add($"Safe for children age {input.Age}. Theme: {input.Theme}.");
        parts.Add($"Page {pageIndex + 1} title: {page.Title}.");
        parts.Add($"Scene to illustrate: {scene}");

        if (!string.IsNullOrWhiteSpace(input.OptionalStoryNotes))
        {
            parts.Add($"Parent theme (reflect in props/setting when relevant): {input.OptionalStoryNotes.Trim()}");
        }

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
