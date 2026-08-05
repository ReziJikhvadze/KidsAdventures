namespace AdventurePacks.Api.Configuration.Options;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4.1-mini";

    /// <summary>
    /// The model that writes whole books. Left empty it falls back to <see cref="Model"/>, but a
    /// book is one long reasoning pass and the cheap model that is fine for short utility calls
    /// writes noticeably flatter stories, so this is worth setting separately.
    /// </summary>
    public string MasterStoryModel { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    /// <summary>responses = Responses API + image_generation tool (recommended). dall-e = Images API only.</summary>
    public string ImageGenerationProvider { get; set; } = "responses";
    /// <summary>Image model: gpt-image-1-mini (budget), gpt-image-1.5, or gpt-image-2 (best).</summary>
    public string ImageModel { get; set; } = "gpt-image-1.5";
    /// <summary>Model for character-anchor edits (images/edits) on pages 2+.</summary>
    public string ImageEditModel { get; set; } = "gpt-image-1.5";
    /// <summary>Landscape 3:2 fits picture-book pages better than square. gpt-image also supports 1024x1024, 1024x1536.</summary>
    public string ImageSize { get; set; } = "1024x1024";
    /// <summary>low | medium | high (gpt-image) or standard | hd (dall-e-3).</summary>
    public string ImageQuality { get; set; } = "medium";
    /// <summary>Legacy setting — story images now use ImageQuality for Pixar-style output.</summary>
    public string ImagePhotoQuality { get; set; } = "medium";
    public bool EnableStoryImages { get; set; } = true;
    /// <summary>Seconds to wait between sequential illustration requests (rate-limit pacing).</summary>
    public int IllustrationPacingSeconds { get; set; } = 5;
    /// <summary>How many pages to illustrate concurrently after the hero anchor (page 1) exists. 2 is a good balance.</summary>
    public int IllustrationMaxParallel { get; set; } = 2;
    /// <summary>Stagger parallel illustration starts to avoid burst rate limits.</summary>
    public int IllustrationStaggerSeconds { get; set; } = 2;
}
