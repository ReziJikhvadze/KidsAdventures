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
    ///
    /// Both text calls of a v6 book read this one setting: the call that writes the book and the
    /// editing pass over what it wrote. They are the same job at different scales, and an editor
    /// weaker than the writer is an editor that flattens what it was asked to correct.
    /// </summary>
    public string MasterStoryModel { get; set; } = "gpt-5.6-sol";

    /// <summary>
    /// Reasoning effort for the story calls: <c>minimal</c>, <c>low</c>, <c>medium</c>,
    /// <c>high</c> — or empty to send nothing and let the model use its own default.
    ///
    /// Empty by default because the parameter is only understood by the reasoning models, and a
    /// deployment still on a plain chat model would have every story call rejected for a field
    /// it never asked for. Set it and it applies to whichever text calls OpenAI is answering:
    /// with Providers:Story on another vendor, that is the polish pass alone, which is the case
    /// it was added for — an editor is allowed to be slow and careful in a way a writer being
    /// waited on is not.
    /// </summary>
    public string MasterStoryReasoningEffort { get; set; } = string.Empty;

    /// <summary>
    /// Which prompt variant writes books: "v1", "v2", "v3" or "v4".
    ///
    /// v4 is wired but not written yet — selecting it fails the run rather than sending an empty
    /// prompt to the model.
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
    public string ImageModel { get; set; } = "gpt-image-2";
    /// <summary>Model for character-anchor edits (images/edits) on pages 2+.</summary>
    public string ImageEditModel { get; set; } = "gpt-image-2";
    /// <summary>
    /// Portrait 2:3, because that is the page it has to fill.
    ///
    /// This was square while the story prompt asked the image model for a "Portrait format,
    /// full-frame illustration" — so every picture was drawn fighting its own instruction, and
    /// the printed page letterboxed it top and bottom. It also matters for print resolution:
    /// 1024px across A5 is roughly 90 DPI against the 300 a press wants, and the taller frame
    /// is half again as many pixels down the page.
    ///
    /// gpt-image accepts 1024x1024, 1024x1536 and 1536x1024. A taller image costs proportionally
    /// more, so this is a per-picture price rise, not a free change.
    /// </summary>
    public string ImageSize { get; set; } = "1024x1536";
    /// <summary>low | medium | high (gpt-image) or standard | hd (dall-e-3).</summary>
    public string ImageQuality { get; set; } = "medium";
    /// <summary>Legacy setting — story images now use ImageQuality for Pixar-style output.</summary>
    public string ImagePhotoQuality { get; set; } = "medium";

    /// <summary>
    /// How hard the image model works to keep the faces in the attached photographs: <c>high</c>,
    /// <c>low</c>, or empty to send nothing.
    ///
    /// This exists because of a real failure: on 2026-09-01 the first composite book kept failing
    /// CHILD_IDENTITY QA — the drawn child was not the child in the reference photo — and the
    /// images/edits request was found to be sending no fidelity setting at all. On the gpt-image-1
    /// family that means LOW, which is the setting that throws away exactly the facial detail this
    /// product is built on. High costs more input tokens per reference; a book of a different child
    /// costs the whole book.
    ///
    /// It is deliberately NOT sent to every model. gpt-image-2 processes every image input at high
    /// fidelity on its own and rejects the field outright — a 400,
    /// <c>invalid_input_fidelity_model</c>, "does not support the 'input_fidelity' parameter" — so
    /// sending it there would turn a quality setting into an outage. <c>OpenAiService</c> decides
    /// per model; this value only says what to ask for where asking is allowed. Which means that on
    /// the current gpt-image-2 configuration this setting changes nothing, and is here for the
    /// gpt-image-1.5 / -1 / -1-mini deployments and for the day a model asks again.
    /// </summary>
    public string ImageInputFidelity { get; set; } = "high";
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

    /// <summary>
    /// Attempts at the story call before giving up. It had none: a single 520 from the provider's
    /// edge — which is what actually happened — threw away a run that takes minutes, and the
    /// parent watching it was told the story could not be written.
    /// </summary>
    public int StoryRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base seconds between story attempts; the wait grows with each one. Zero is a real value —
    /// retry immediately — and it is what the retry tests run with so the suite does not sleep.
    /// An accidental zero cannot happen: unset stays at the default here, and a negative refuses
    /// to boot rather than being silently rewritten, so a zero that arrives at the client was
    /// typed on purpose.
    /// </summary>
    public int StoryRetryBackoffSeconds { get; set; } = 4;
}
