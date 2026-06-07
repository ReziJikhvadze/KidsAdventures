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

public sealed class StoryPageContentDto
{
    public string Title { get; set; } = string.Empty;
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
