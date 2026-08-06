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
    public string MasterStoryModel { get; set; } = "gpt-5.6-luna";

    /// <summary>
    /// Which prompt variant writes books: "v1" or "v2".
    ///
    /// A switch rather than an edit, so the version that is known to work stays reachable while
    /// the other one changes. Every run records which wrote it, which is what makes two prompts
    /// comparable rather than just sequential.
    /// </summary>
    public string StoryPromptVersion { get; set; } = "v1";

    /// <summary>
    /// Writes the full prompts and the returned story to the log.
    ///
    /// On, because it was asked for and a flag you have to discover is a flag that never gets
    /// switched on when it is needed. It should not stay on: a prompt carries the child's name,
    /// age and a description of their face, so this writes a named child's personal details into
    /// log storage. Set OpenAI__LogPrompts to false before real families use the site.
    ///
    /// Returned illustrations are never logged either way — only what was asked for. Image bytes
    /// would be megabytes of base64 per picture, which makes the log both useless and expensive.
    /// </summary>
    public bool LogPrompts { get; set; } = true;
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

    /// <summary>
    /// Attempts at one illustration before giving up. Retries are for a rate limit or a blip; a
    /// fourth attempt is waiting out a bad afternoon, and somebody is watching a loading screen
    /// while it happens.
    /// </summary>
    public int ImageRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base seconds between attempts; the wait grows with each one. At the old eight this cost
    /// up to forty-eight seconds of pure waiting on a request nobody had cancelled.
    /// </summary>
    public int ImageRetryBackoffSeconds { get; set; } = 3;
}
