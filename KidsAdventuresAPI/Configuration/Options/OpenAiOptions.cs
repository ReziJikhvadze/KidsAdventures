namespace AdventurePacks.Api.Configuration.Options;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4.1-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    /// <summary>responses = Responses API + image_generation tool (recommended). dall-e = Images API only.</summary>
    public string ImageGenerationProvider { get; set; } = "responses";
    /// <summary>Model for images. Empty = use <see cref="Model"/> for Responses; dall-e-3 for Images API fallback.</summary>
    public string ImageModel { get; set; } = "";
    public string ImageSize { get; set; } = "1024x1024";
    public string ImageQuality { get; set; } = "standard";
    public bool EnableStoryImages { get; set; } = true;
}
