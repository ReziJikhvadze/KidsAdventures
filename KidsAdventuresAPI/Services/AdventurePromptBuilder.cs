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

        var pageCount = input.StoryPageCount > 0 ? input.StoryPageCount : AdventureStoryConstants.LegacyPageCount;
        pageCount = Math.Min(pageCount, AdventureStoryConstants.LegacyPageCount);
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
            FormatStoryRule(input.StoryRule),
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
        // Immediately after the language rule, because both govern the same thing: what the
        // reader actually sees on the page.
        lines.Add($"- {string.Format(texts.ScriptPurityRule, texts.LanguageName)}");
        lines.Add($"- {texts.NoPromptEchoRule}");
        lines.Add($"- {string.Format(texts.PageCountRule, pageCount)}");
        lines.Add($"- {texts.NoExtraPagesRule}");
        lines.Add(storyArc);
        lines.Add($"- {texts.PageLengthRule}");
        lines.Add($"- {texts.CaptionRule}");
        // Placed immediately before the continuity rule: one says how to compose the story,
        // the other says what the result must satisfy. Together they are what stops a book
        // reading as twelve unrelated postcards.
        lines.Add($"- {texts.WholeStoryFirstRule}");
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

        // Stated as a rule rather than a label: a label is data the model may quietly drop,
        // and dropping this one produces a book about the wrong child.
        var genderWord = input.Gender?.Trim().ToLowerInvariant() switch
        {
            "girl" => texts.GenderGirl,
            "boy" => texts.GenderBoy,
            _ => null,
        };
        if (genderWord is not null)
        {
            lines.Add($"- {string.Format(texts.HeroGenderRule, input.ChildName, genderWord)}");
        }
        lines.Add(string.Format(texts.ThemeLabel, input.Theme));

        // The theme alone is one word, so the model invented its own version of that world
        // in every book. Naming the actual place, its landmarks and its companion is what
        // makes a story belong to the world the parent chose rather than to a generic idea
        // of the theme — and what keeps a series set in one world recognisably the same
        // place across books.
        var world = AdventureWorldCanon.For(input.Theme, input.StoryLanguage);
        if (world is not null)
        {
            lines.Add(string.Empty);
            lines.Add(texts.WorldCanonHeader);
            lines.Add(string.Format(texts.WorldCanonPlace, world.Place));
            lines.Add(string.Format(texts.WorldCanonLandmarks, world.Landmarks));
            lines.Add(string.Format(texts.WorldCanonAtmosphere, world.Atmosphere));
            if (!string.IsNullOrWhiteSpace(world.Companion))
            {
                lines.Add(string.Format(texts.WorldCanonCompanion, world.Companion));
            }

            lines.Add($"- {texts.WorldCanonRule}");

            // The theme card showed the parent a specific title and premise before they
            // chose. Until now the writer never saw either, so the card sold one book and
            // the model wrote another — which is exactly why the content felt unrelated to
            // the chosen story. Only for a first book: a continuation follows its own
            // series memory, and forcing the opening premise again would reset the arc.
            if (string.IsNullOrWhiteSpace(input.SeriesMemory))
            {
                lines.Add(string.Format(
                    texts.PromisedStoryRule,
                    string.Format(world.PromisedTitle, input.ChildName),
                    string.Format(world.PromisedPremise, input.ChildName)));
            }
        }
        FormatAppearanceLine(texts.HeroAppearanceLabel, input.ChildAppearanceDescription, lines);
        lines.Add($"{texts.FamilyMembersLabel}{Environment.NewLine}{familyMembersText}");

        // Series memory turns a shelf of unrelated books into one growing world. It is only
        // present from the second book onwards, and it is deliberately placed after the cast so
        // the model reads "who is in this book" before "what already happened to them".
        if (!string.IsNullOrWhiteSpace(input.SeriesMemory))
        {
            lines.Add(string.Empty);
            lines.Add(string.Format(texts.SeriesMemoryHeader, input.ChapterNumber));
            lines.Add(input.SeriesMemory.Trim());
            lines.Add($"- {texts.SeriesMemoryRule}");
        }

        if (!string.IsNullOrWhiteSpace(input.OptionalStoryNotes))
        {
            lines.Add(string.Empty);
            var wishPages = isWelcomeGift
                ? texts.ExtraWishesWelcomePages
                : pageCount <= AdventureStoryConstants.LegacyPageCount
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

        // The story prompt was told the gender; the illustrator never was, so a girl's book
        // could be written about a girl and drawn with a boy.
        var imageGender = input.Gender?.Trim().ToLowerInvariant() switch
        {
            "girl" => texts.GenderGirl,
            "boy" => texts.GenderBoy,
            _ => null,
        };
        if (imageGender is not null)
        {
            parts.Add(string.Format(texts.ImageHeroGender, imageGender));
        }

        parts.Add(texts.ImageStyle);
        parts.Add(string.Format(texts.ImageSafeForAge, input.Age, input.Theme));
        parts.Add(string.Format(texts.ImagePageTitle, pageIndex + 1, page.Title));
        parts.Add(string.Format(texts.ImageScene, scene));

        // The caption is laid over the finished picture, so the illustration has to leave
        // room for it. Alternating the reserved edge by page keeps consecutive spreads from
        // putting text in the same corner twice, and stops the composition becoming
        // monotonous. The rule also demands the opposite half carry the real action, so
        // reserving space does not turn into half an empty page.
        var (reserved, opposite) = pageIndex % 2 == 0
            ? (texts.ImageTextSafeTop, texts.ImageTextSafeBottom)
            : (texts.ImageTextSafeBottom, texts.ImageTextSafeTop);
        parts.Add(string.Format(texts.ImageTextSafeArea, reserved, opposite));

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

    /// <summary>
    /// Renders the operator's matrix cell as extra constraints under the built-in age block.
    /// Only fields that were actually set produce a line, so an untouched matrix contributes
    /// nothing and the prompt is byte-identical to what it was before the matrix existed.
    /// </summary>
    private static string FormatStoryRule(StoryRule? rule)
    {
        if (rule is null || !rule.IsActive)
        {
            return string.Empty;
        }

        var lines = new List<string>();

        if (rule.MaxWordsPerPage is { } maxWords and > 0)
        {
            lines.Add($"- Hard limit: no page may exceed {maxWords} words of on-page text.");
        }

        if (rule.MaxSentenceWords is { } maxSentence and > 0)
        {
            lines.Add($"- Keep every sentence at or under {maxSentence} words.");
        }

        if (!string.IsNullOrWhiteSpace(rule.VocabularyLevel))
        {
            lines.Add($"- Vocabulary level: {DescribeVocabulary(rule.VocabularyLevel)}");
        }

        if (rule.ScarinessLimit is { } scariness)
        {
            lines.Add($"- Tension ceiling: {DescribeScariness(scariness)}");
        }

        if (!string.IsNullOrWhiteSpace(rule.ExtraGuidance))
        {
            lines.Add($"- {rule.ExtraGuidance.Trim()}");
        }

        return lines.Count == 0 ? string.Empty : string.Join(Environment.NewLine, lines);
    }

    private static string DescribeVocabulary(string level) => level.Trim().ToLowerInvariant() switch
    {
        "simple" => "only everyday words a child already uses; explain nothing, choose plainer words instead.",
        "rich" => "reach for vivid, precise words and let one or two stretch the reader, made clear by context.",
        _ => "familiar words, with the occasional new one the surrounding sentence makes obvious.",
    };

    private static string DescribeScariness(int level) => level switch
    {
        0 => "nothing tense at all — no danger, no threat, no darkness; warmth throughout.",
        1 => "mild suspense only — a surprise or a puzzle, never a threat to anyone.",
        2 => "real stakes are allowed, but the hero is never physically in danger and it resolves warmly.",
        _ => "genuine jeopardy is allowed, resolved fully and reassuringly before the last page.",
    };

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
