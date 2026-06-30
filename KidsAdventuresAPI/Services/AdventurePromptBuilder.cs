using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;

namespace AdventurePacks.Api.Services;

internal static class AdventurePromptBuilder
{
    public static string BuildStoryPrompt(AdventureGenerationInput input, Guid adventureId)
    {
        var texts = AdventurePromptTexts.ForLanguage(input.StoryLanguage);

        var familyMembersText = input.FamilyMembers.Count == 0
            ? texts.NoFamilyMembers
            : string.Join(Environment.NewLine, input.FamilyMembers.Select(m =>
                $"- {m.Name} ({m.Relationship}){FormatAppearance(texts.LooksLikePrefix, m.AppearanceDescription)}"));

        var storySeed = texts.StorySeeds[Random.Shared.Next(texts.StorySeeds.Length)];
        var toneSeed = texts.ToneSeeds[Random.Shared.Next(texts.ToneSeeds.Length)];
        var sceneVariety = texts.SceneVarietySeeds[Random.Shared.Next(texts.SceneVarietySeeds.Length)];
        var guestCharacter = texts.GuestCharacterSeeds[Random.Shared.Next(texts.GuestCharacterSeeds.Length)];

        var pageCount = input.StoryPageCount > 0 ? input.StoryPageCount : AdventureStoryConstants.FullPageCount;
        pageCount = Math.Min(pageCount, AdventureStoryConstants.FullPageCount);
        var isWelcomeGift = pageCount <= AdventureStoryConstants.WelcomeGiftPageCount;
        var storyArc = isWelcomeGift ? texts.WelcomeArc : texts.FullArc;

        var narrativeRules = texts.NarrativeCraftRules
            .Select((rule, index) => index switch
            {
                8 => string.Format(rule, sceneVariety),
                9 => string.Format(rule, guestCharacter),
                _ => rule,
            })
            .Select(rule => $"- {rule}")
            .ToList();

        var lines = new List<string>
        {
            texts.MasterStorytellerDirective.Trim(),
            string.Empty,
            texts.StorySystemPrompt.Trim(),
            string.Empty,
            string.Format(texts.AgeGuidelinesHeader, input.Age),
            GetAgeGuidelines(texts, input.Age),
            string.Empty,
            texts.OutputFormatHeader,
            "{",
            "  \"title\": \"\",",
            "  \"theme\": \"\",",
            "  \"childName\": \"\",",
            "  \"storyPages\": [{ \"title\": \"\", \"caption\": \"\", \"content\": \"\" }]",
            "}",
            string.Empty,
            texts.NarrativeCraftHeader,
        };
        lines.AddRange(narrativeRules);
        lines.Add(string.Empty);
        lines.Add(texts.RulesHeader);
        lines.Add($"- {texts.IncludeFamilyRule}");
        lines.Add($"- {string.Format(texts.WriteInLanguageRule, texts.LanguageName)}");
        lines.Add($"- {string.Format(texts.PageCountRule, pageCount)}");
        lines.Add($"- {texts.NoExtraPagesRule}");
        lines.Add(storyArc);
        lines.Add($"- {texts.PageLengthRule}");
        lines.Add($"- {texts.CaptionRule}");
        lines.Add($"- {texts.ContinuityRule}");
        lines.Add($"- {texts.JsonOnlyRule}");
        lines.Add($"- {texts.RawJsonRule}");
        lines.Add($"- {string.Format(texts.AdventureIdLabel, adventureId)}");
        lines.Add($"- {string.Format(texts.NarrativeToneLabel, toneSeed)}");
        lines.Add($"- {texts.NoGenericOpeningsRule}");
        lines.Add(string.Empty);
        lines.Add(texts.InputHeader);
        lines.Add(string.Format(texts.ChildNameLabel, input.ChildName));
        lines.Add(string.Format(texts.ChildAgeLabel, input.Age));
        lines.Add(string.Format(texts.ThemeLabel, input.Theme));
        FormatAppearanceLine(texts.HeroAppearanceLabel, input.ChildAppearanceDescription, lines);
        lines.Add($"{texts.FamilyMembersLabel}{Environment.NewLine}{familyMembersText}");

        if (!string.IsNullOrWhiteSpace(input.OptionalStoryNotes))
        {
            lines.Add(string.Empty);
            var wishPages = isWelcomeGift
                ? texts.ExtraWishesWelcomePages
                : pageCount <= AdventureStoryConstants.FullPageCount
                    ? texts.ExtraWishesFullPages
                    : texts.ExtraWishesManyPages;
            lines.Add(string.Format(texts.ExtraWishesHeader, wishPages));
            lines.Add(input.OptionalStoryNotes.Trim());
            lines.Add($"- {texts.LikesRule}");
            lines.Add($"- {texts.DislikesRule}");
            lines.Add($"- {texts.ParentWishesRule}");
        }
        else
        {
            lines.Add($"- {string.Format(texts.StoryHookLabel, storySeed)}");
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
        var texts = AdventurePromptTexts.ForLanguage(input.StoryLanguage);
        // Keep enough of the page text that any parent wish woven into it still reaches the illustrator.
        var scene = page.Content.Length > 600 ? page.Content[..600] + "..." : page.Content;
        var parts = new List<string>
        {
            texts.ImageTask,
            texts.ImageCharacterLock,
        };

        var imageIndex = 1;
        var heroDna = string.IsNullOrWhiteSpace(input.ChildAppearanceDescription)
            ? null
            : input.ChildAppearanceDescription.Trim();

        if (hasCharacterAnchor)
        {
            var dnaSuffix = heroDna is null ? "" : string.Format(texts.ImageHeroDna, heroDna);
            parts.Add(string.Format(texts.ImageLockedHero, imageIndex, dnaSuffix));
            imageIndex++;
        }

        foreach (var cast in castPhotos)
        {
            var role = cast.IsHero
                ? texts.ImageHeroChild
                : string.Format(texts.ImageFamilyRole, cast.Relationship);
            if (cast.Bytes is { Length: > 0 })
            {
                var dnaSuffix = cast.IsHero && heroDna is not null
                    ? string.Format(texts.ImageCastDna, heroDna)
                    : "";
                parts.Add(string.Format(texts.ImageCastPhoto, imageIndex, cast.Name, role, dnaSuffix));
                if (cast.IsHero && !hasCharacterAnchor)
                {
                    parts.Add(texts.PixarFromPhotoStylePrompt);
                }
            }
            else
            {
                var dna = string.IsNullOrWhiteSpace(cast.AppearanceDescription)
                    ? string.Format(texts.ImageInventCastLook, cast.Name)
                    : cast.AppearanceDescription.Trim();
                parts.Add(string.Format(texts.ImageCastInvented, imageIndex, cast.Name, role, dna));
            }

            imageIndex++;
        }

        if (!hasCharacterAnchor && castPhotos.Count == 0)
        {
            var heroLook = heroDna is null
                ? string.Format(texts.ImageHeroNoPhoto, input.ChildName, input.Age)
                : $"{string.Format(texts.ImageHeroNoPhoto, input.ChildName, input.Age)}: {heroDna}";
            parts.Add(string.Format(texts.ImageInventHero, heroLook));
        }

        parts.Add(texts.ImageStyle);
        parts.Add(string.Format(texts.ImageSafeForAge, input.Age, input.Theme));
        parts.Add(string.Format(texts.ImagePageTitle, pageIndex + 1, page.Title));
        parts.Add(string.Format(texts.ImageScene, scene));

        // Illustrations are now text-free; the app overlays the page caption. Keep words out of the picture.
        parts.Add(texts.ImageNoText);

        // Reinforce scene-to-scene visual continuity once we have a page-1 anchor to match.
        if (pageIndex > 0)
        {
            parts.Add(texts.ImageContinuity);
        }

        if (!string.IsNullOrWhiteSpace(input.OptionalStoryNotes))
        {
            parts.Add(string.Format(texts.ImageParentTheme, input.OptionalStoryNotes.Trim()));
        }

        parts.Add(string.Format(texts.ImageAdventureId, adventureId));
        return string.Join(" ", parts);
    }

    public static string BuildHeroPhotoDescribePrompt(string storyLanguage, string childName, int age) =>
        string.Format(
            AdventurePromptTexts.ForLanguage(storyLanguage).HeroPhotoDescribe,
            childName,
            age) + AdventurePromptTexts.ForLanguage(storyLanguage).VisionDescribeSuffix;

    public static string BuildFamilyPhotoDescribePrompt(
        string storyLanguage,
        string name,
        string relationship) =>
        string.Format(
            AdventurePromptTexts.ForLanguage(storyLanguage).FamilyPhotoDescribe,
            name,
            relationship) + AdventurePromptTexts.ForLanguage(storyLanguage).VisionDescribeSuffix;

    private static string GetAgeGuidelines(AdventurePromptLocale texts, int age) => age switch
    {
        <= 5 => texts.Age3to5,
        <= 9 => texts.Age6to9,
        _ => texts.Age10to13,
    };

    private static string FormatAppearance(string looksLikePrefix, string? appearance) =>
        string.IsNullOrWhiteSpace(appearance) ? "" : string.Format(looksLikePrefix, appearance);

    private static void FormatAppearanceLine(string label, string? appearance, List<string> lines)
    {
        if (!string.IsNullOrWhiteSpace(appearance))
        {
            lines.Add($"{label}: {appearance}");
        }
    }
}
