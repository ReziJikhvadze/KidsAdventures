using System.Text.Json.Serialization;

namespace AdventurePacks.Api.DTOs.AdventurePacks;

public sealed class GenerateAdventurePackRequest
{
    [Required]
    public Guid ChildId { get; set; }

    [Required]
    [EnumDataType(typeof(ThemeType))]
    public ThemeType Theme { get; set; }
}

public sealed class AdventurePackResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ChildId { get; set; }
    public ThemeType Theme { get; set; }
    public AdventurePackStatus Status { get; set; }
    public string? PdfUrl { get; set; }
    public DateTime CreatedAt { get; set; }
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
