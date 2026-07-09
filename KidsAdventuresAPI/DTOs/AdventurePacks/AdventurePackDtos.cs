using System.Text.Json.Serialization;

namespace AdventurePacks.Api.DTOs.AdventurePacks;

public sealed class GenerateAdventurePackRequest
{
    [Required]
    public Guid ChildId { get; set; }

    [Required]
    [EnumDataType(typeof(ThemeType))]
    public ThemeType Theme { get; set; }

    [MaxLength(1000)]
    public string? OptionalStoryNotes { get; set; }

    /// <summary>en, ka, es, or other ISO-style code. Defaults to English.</summary>
    [MaxLength(16)]
    public string? StoryLanguage { get; set; }

    /// <summary>Internal use only (set by Story Path when generating chapters 2-5 of a saga); never sent by the public generator UI.</summary>
    [JsonIgnore]
    public int? ChapterIndex { get; set; }

    /// <summary>Internal use only — the previous chapter's pack id, used to carry the companion/recap forward.</summary>
    [JsonIgnore]
    public Guid? PreviousChapterPackId { get; set; }
}

public class AdventurePackResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ChildId { get; set; }
    public ThemeType Theme { get; set; }
    public AdventurePackStatus Status { get; set; }
    public string? PdfUrl { get; set; }
    public string? ProgressMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StoryLanguage { get; set; }
    public PreviewIllustrationStatus PreviewIllustrationStatus { get; set; }
    public int StoryPageCount { get; set; }
    public bool IsWelcomeGiftStory { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AdventurePackDetailResponse : AdventurePackResponse
{
    public string? Title { get; set; }
    public string? ChildName { get; set; }
    public List<StoryPageContentDto> StoryPages { get; set; } = [];
}

/// <summary>
/// Result of the free, no-login teaser: a cover image, the title, and the first page — plus the full
/// story JSON so it can be saved to the account verbatim after the parent signs in.
/// </summary>
public sealed class GuestPreviewResult
{
    public string Title { get; set; } = string.Empty;
    public string ChildName { get; set; } = string.Empty;
    public string FirstPageTitle { get; set; } = string.Empty;
    public string FirstPageText { get; set; } = string.Empty;
    public string CoverImageDataUrl { get; set; } = string.Empty;
    public ThemeType Theme { get; set; }

    /// <summary>Server-side id of this teaser; sent back during sign-up so the welcome gift is trustable.</summary>
    public Guid GuestPreviewId { get; set; }

    /// <summary>Identity of the generated story; fallback link for entitlement when the previewId is lost.</summary>
    public Guid StoryId { get; set; }

    /// <summary>Serialized AdventureContentDto for the whole story, replayed into the account on sign-in.</summary>
    public string StoryJson { get; set; } = string.Empty;
}

/// <summary>Saves a story generated during the no-login teaser to the signed-in parent's account.</summary>
public sealed class ImportGuestStoryRequest
{
    [Required]
    public Guid ChildId { get; set; }

    [Required]
    [EnumDataType(typeof(ThemeType))]
    public ThemeType Theme { get; set; }

    [MaxLength(16)]
    public string? StoryLanguage { get; set; }

    [MaxLength(1000)]
    public string? OptionalStoryNotes { get; set; }

    [Required]
    [MaxLength(60000)]
    public string StoryJson { get; set; } = string.Empty;
}

public sealed class StoryPageContentDto
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Short evocative phrase (3-8 words) shown overlaid on the illustration.</summary>
    public string Caption { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
    public string? IllustrationUrl { get; set; }
    public bool IsIllustrated { get; set; }
    public PageInteractiveDto? Interactive { get; set; }
}

public sealed class PageInteractiveDto
{
    [JsonPropertyName("avatarTap")]
    public AvatarTapInteractiveDto? AvatarTap { get; set; }

    [JsonPropertyName("findIt")]
    public FindItInteractiveDto? FindIt { get; set; }

    [JsonPropertyName("counting")]
    public CountingInteractiveDto? Counting { get; set; }

    [JsonPropertyName("revealItem")]
    public RevealItemInteractiveDto? RevealItem { get; set; }
}

public sealed class HotspotRegionDto
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("w")]
    public double W { get; set; }

    [JsonPropertyName("h")]
    public double H { get; set; }
}

public sealed class AvatarTapInteractiveDto
{
    [JsonPropertyName("region")]
    public HotspotRegionDto? Region { get; set; }
}

public sealed class FindItInteractiveDto
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("objectLabel")]
    public string ObjectLabel { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public HotspotRegionDto Region { get; set; } = new();
}

public sealed class CountingInteractiveDto
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public int Target { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("regions")]
    public List<HotspotRegionDto>? Regions { get; set; }
}

public sealed class RevealItemInteractiveDto
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>What's shown closed in the illustration, e.g. "box", "bush", "egg".</summary>
    [JsonPropertyName("coverLabel")]
    public string CoverLabel { get; set; } = string.Empty;

    /// <summary>What's revealed on tap, e.g. "a sleepy bunny".</summary>
    [JsonPropertyName("revealLabel")]
    public string RevealLabel { get; set; } = string.Empty;

    /// <summary>Optional short, playful real-world fact about the revealed creature/object.</summary>
    [JsonPropertyName("funFact")]
    public string? FunFact { get; set; }

    [JsonPropertyName("region")]
    public HotspotRegionDto Region { get; set; } = new();
}

/// <summary>A recurring non-family character (animal, robot, magical friend) — kept identical across every
/// page it appears on, and carried forward into later Story Path chapters, to prevent identity drift.</summary>
public sealed class CompanionDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class AdventureContentDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = string.Empty;

    [JsonPropertyName("childName")]
    public string ChildName { get; set; } = string.Empty;

    [JsonPropertyName("companion")]
    public CompanionDto? Companion { get; set; }

    [JsonPropertyName("storyPages")]
    public List<StoryPageDto> StoryPages { get; set; } = [];

    [JsonPropertyName("activities")]
    public List<ActivityDto> Activities { get; set; } = [];

    [JsonPropertyName("certificate")]
    public CertificateDto Certificate { get; set; } = new();

    /// <summary>1-2 sentence wrap-up of how this chapter ends, used to seed the next Story Path chapter.</summary>
    [JsonPropertyName("chapterRecap")]
    public string? ChapterRecap { get; set; }
}

public sealed class StoryPageDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Short evocative phrase (3-8 words) shown overlaid on the illustration.</summary>
    [JsonPropertyName("caption")]
    public string Caption { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("illustrationUrl")]
    public string? IllustrationUrl { get; set; }

    [JsonPropertyName("interactive")]
    public PageInteractiveDto? Interactive { get; set; }

    /// <summary>Illustration bytes (set after OpenAI image generation; not part of story JSON).</summary>
    [JsonIgnore]
    public byte[]? ImageBytes { get; set; }
}

public sealed class ActivityDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public sealed class CertificateDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
