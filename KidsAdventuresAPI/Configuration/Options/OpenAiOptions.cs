namespace AdventurePacks.Api.Configuration.Options;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4.1-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    /// <summary>responses = Responses API + image_generation tool (recommended). dall-e = Images API only.</summary>
    public string ImageGenerationProvider { get; set; } = "responses";
    /// <summary>Image model for text-only generation: gpt-image-1-mini (budget), gpt-image-1, or gpt-image-2.</summary>
    public string ImageModel { get; set; } = "gpt-image-1-mini";
    /// <summary>Model for photo-reference edits (images/edits). gpt-image-2 gives the best likeness; mini is cheaper.</summary>
    public string ImageEditModel { get; set; } = "gpt-image-2";
    public string ImageSize { get; set; } = "1024x1024";
    public string ImageQuality { get; set; } = "low";
    /// <summary>Quality when a hero photo is used (minimum medium recommended for recognizable likeness).</summary>
    public string ImagePhotoQuality { get; set; } = "high";
    public bool EnableStoryImages { get; set; } = true;
}
