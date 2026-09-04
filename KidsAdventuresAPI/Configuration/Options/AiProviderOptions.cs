namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// Which vendor answers which half of the pipeline.
///
/// The two halves are genuinely independent — a book is a story model writing JSON against a
/// schema, and an image model drawing from references — and they are the two places this product
/// spends real money. Being able to move one without the other is what makes them comparable:
/// the same plan drawn by two illustrators, or the same illustrator given two plans, is the only
/// honest way to tell which vendor is actually better at this book.
///
/// The shipped appsettings.json carries the owner's split of 2026-09-01: Gemini writes the
/// words (Flash is fast and cheap enough to be the obvious choice for text), OpenAI draws and
/// judges the pictures (gpt-image-2 is the illustrator the owner picked for the part the money
/// and quality live in). The C# property defaults below deliberately stay OpenAI/OpenAI rather
/// than mirroring that file: a default of Gemini would make a configuration with no Gemini key
/// refuse to boot — including every test harness that builds DI from an empty configuration —
/// so the file states the product's choice and the code states the one that can always start.
/// </summary>
public sealed class AiProviderOptions
{
    public const string SectionName = "Providers";

    /// <summary>Writes the plan: <c>OpenAI</c> or <c>Gemini</c>. Case-insensitive.</summary>
    public string Story { get; set; } = AiProvider.OpenAi;

    /// <summary>
    /// Draws and judges the pictures: <c>OpenAI</c> or <c>Gemini</c>. Case-insensitive.
    ///
    /// Covers all three calls that look at or make an image — generation, the QA review, and the
    /// reading of the child's photograph into an appearance — because they are one conversation
    /// about one picture, and splitting them across vendors would mean one model judging
    /// another's work by a standard it does not share.
    ///
    /// One vision call is deliberately not covered: the intake gate that decides whether an
    /// uploaded photo shows a child at all. It runs through the older Beki client, in front of
    /// everything, while a parent waits on the form — and moving the gate that guards what
    /// reaches the pipeline is a decision to take on its own, not a side effect of choosing an
    /// illustrator. An OpenAI key is therefore still required whatever this is set to.
    /// </summary>
    public string Images { get; set; } = AiProvider.OpenAi;

    /// <summary>
    /// Edits the plan after it is written: <c>OpenAI</c>, <c>Gemini</c>, or empty to follow
    /// <see cref="Story"/>.
    ///
    /// Split out from <see cref="Story"/> because writing and editing are not the same job. The
    /// generator is asked to invent a book; the polish pass is asked to leave one alone except
    /// for grammar, spelling and what is unsafe for a small child — and a model that is better
    /// at inventing is not automatically the one you want holding the red pen. Keeping the two
    /// on one setting made the cheaper, faster writer also the proofreader, which is the wrong
    /// way round: the writing is the part worth spending latency on being cheap about, and the
    /// correction is the part a reader actually notices when it is wrong.
    ///
    /// Empty by default, and empty means "whatever Story says", so an installation that never
    /// sets it keeps a single-vendor pipeline exactly as before.
    /// </summary>
    public string StoryPolish { get; set; } = string.Empty;

    /// <summary>
    /// What <see cref="StoryPolish"/> actually resolves to once the fallback is applied. Read
    /// this rather than the raw property: the empty string is a valid configured value meaning
    /// "follow the writer", and every caller that forgot would otherwise treat it as OpenAI.
    /// </summary>
    public string ResolvedStoryPolish =>
        string.IsNullOrWhiteSpace(StoryPolish) ? Story : StoryPolish;

    public bool UsesGeminiForStory => AiProvider.IsGemini(Story);

    public bool UsesGeminiForImages => AiProvider.IsGemini(Images);

    public bool UsesGeminiForStoryPolish => AiProvider.IsGemini(ResolvedStoryPolish);

    /// <summary>
    /// Whether any half of the pipeline is pointed at Gemini — which is the question the startup
    /// key check is actually asking. Three switches now, and a check that forgot one would let a
    /// deployment boot with the vendor selected and no key to reach it.
    /// </summary>
    public bool UsesGeminiAnywhere =>
        UsesGeminiForStory || UsesGeminiForImages || UsesGeminiForStoryPolish;
}

/// <summary>The provider names, so a typo is a compile error in code and a startup error in config.</summary>
public static class AiProvider
{
    public const string OpenAi = "OpenAI";
    public const string Gemini = "Gemini";

    public static bool IsGemini(string? value) =>
        string.Equals(value?.Trim(), Gemini, StringComparison.OrdinalIgnoreCase);

    public static bool IsOpenAi(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || string.Equals(value.Trim(), OpenAi, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Anything that is neither name is refused at startup rather than silently treated as the
    /// default: a deployment that meant to switch vendors and misspelled it would otherwise keep
    /// billing the old one while everyone believed the switch had happened.
    /// </summary>
    public static bool IsKnown(string? value) => IsOpenAi(value) || IsGemini(value);

    /// <summary>
    /// Same check for a setting whose empty value means "inherit" rather than "OpenAI". Only
    /// <see cref="AiProviderOptions.StoryPolish"/> is like this, and it needs its own predicate
    /// because <see cref="IsOpenAi"/> deliberately treats blank as OpenAI — correct for a
    /// setting that defaults to a vendor, wrong for one that defaults to another setting.
    /// </summary>
    public static bool IsKnownOrInherited(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsKnown(value);
}

/// <summary>
/// Everything the Gemini side needs. Empty by default: the section only has to be filled in when
/// <see cref="AiProviderOptions"/> actually points at Gemini, and a missing key is then a startup
/// failure rather than a book that dies halfway through being drawn.
/// </summary>
public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Interactions API root. A setting rather than a constant for the same reason the model
    /// names are: a proxy, a regional endpoint or a version bump must not need a deployment.
    /// </summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>
    /// Writes the book. One model for every story stage — generator, reviewer, repair — because
    /// the per-stage names in <see cref="BekiOptions"/> are OpenAI product names and mean nothing
    /// here; the stage still differs by its prompt and its schema, which is what actually decides
    /// what comes back.
    ///
    /// 3.6 rather than the newest flash: the first live calls to 3.7 came back 500 "experiencing
    /// high demand" while 3.6 answered every time. A book that fails because the fashionable model
    /// is busy is a worse book than one written by last month's.
    /// </summary>
    public string StoryModel { get; set; } = "gemini-3.1-pro-preview";

    /// <summary>Draws the illustrations. The image-capable models are a separate family.</summary>
    public string ImageModel { get; set; } = "gemini-3.1-flash-image";

    /// <summary>
    /// Looks at pictures: the QA verdict and the reading of the child's photograph. A text model
    /// with vision, not the image model — asking the illustrator to grade its own work is a
    /// different job from drawing.
    /// </summary>
    public string VisionModel { get; set; } = "gemini-3.6-flash";

    /// <summary>
    /// Resolution class for generated art, in the API's own vocabulary ("1K", "2K", "4K").
    /// The aspect ratio is not configured: it is derived per call from the size the pipeline
    /// asks for, so the Beki spread stays 3:2 and the A5 page stays 2:3 without a second place
    /// remembering which is which.
    /// </summary>
    public string ImageSize { get; set; } = "2K";

    /// <summary>
    /// Minutes one text call may take. Mirrors the OpenAI text client's ceiling rather than
    /// doubling it: it was twelve, and twelve minutes times <see cref="RetryAttempts"/> is more
    /// than a whole thirty-minute generation budget spent on one call that is not answering.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 6;

    /// <summary>
    /// Minutes one image call may take. Shorter than the text ceiling for the same reason the
    /// OpenAI side splits them: a book draws nine or more pictures and retries each, so this is
    /// the number that decides how long one bad slot can hold the whole job.
    /// </summary>
    public int ImageTimeoutMinutes { get; set; } = 4;

    /// <summary>Transient failures are retried the same way the OpenAI story client retries.</summary>
    public int RetryAttempts { get; set; } = 3;

    public int RetryBackoffSeconds { get; set; } = 5;
}
