using System.Text.Json.Serialization;

namespace AdventurePacks.Api.DTOs.AdventurePacks;

public class AdventurePackResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
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

    // -- book model --------------------------------------------------------

    public string? WorldId { get; set; }
    public Guid? PrimaryCharacterId { get; set; }
    public Guid? SeriesId { get; set; }
    public int SequenceNumber { get; set; }
    public Guid? ContinuesFromBookId { get; set; }
    public BookAccessLevel AccessLevel { get; set; }

    /// <summary>False while only the free sample is readable.</summary>
    public bool IsUnlocked { get; set; }

    public bool HasPrintEntitlement { get; set; }
    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// The book's own title. The library list renders this on every card, so it belongs on
    /// the list shape and not only on the detail one — without it a card falls back to a
    /// generic world title with a placeholder hero name.
    /// </summary>
    public string? Title { get; set; }
}

public sealed class AdventurePackDetailResponse : AdventurePackResponse
{
    public string? ChildName { get; set; }

    /// <summary>
    /// Pages the caller may read. A preview book returns the cover and page one only, with
    /// <see cref="LockedPageCount"/> standing in for the rest.
    /// </summary>
    public List<StoryPageContentDto> StoryPages { get; set; } = [];

    /// <summary>How many pages exist beyond the ones returned.</summary>
    public int LockedPageCount { get; set; }
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

public sealed class StoryPageContentDto
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Short evocative phrase (3-8 words) shown overlaid on the illustration.</summary>
    public string Caption { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
    public string? IllustrationUrl { get; set; }
    public bool IsIllustrated { get; set; }
}

public sealed class AdventureContentDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = string.Empty;

    [JsonPropertyName("childName")]
    public string ChildName { get; set; } = string.Empty;

    [JsonPropertyName("storyPages")]
    public List<StoryPageDto> StoryPages { get; set; } = [];

    [JsonPropertyName("activities")]
    public List<ActivityDto> Activities { get; set; } = [];

    [JsonPropertyName("certificate")]
    public CertificateDto Certificate { get; set; } = new();
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
