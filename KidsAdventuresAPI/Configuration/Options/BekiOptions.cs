namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// Configuration for the Beki story and visual pipelines.
///
/// Every model name is a setting rather than a constant. The pipeline is deliberately
/// model-agnostic: it depends on JSON-schema-shaped output and a vision-capable image
/// model, not on any particular product name, so moving to a newer model is a
/// configuration change and never a code change.
/// </summary>
public sealed class BekiOptions
{
    public const string SectionName = "Beki";

    /// <summary>Master switch. The previous illustration flow stays available until this is on.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Turns on the new Beki book format end to end: v5 preview planning, the Beki cover path for
    /// previews, spread illustrations that carry Beki's own reference, and the manifest-based
    /// resumable fulfilment job.
    ///
    /// Deliberately its own switch rather than reusing <see cref="Enabled"/>. That flag already
    /// gates the older, unrelated <c>/api/beki/stories</c> pipeline (<c>BekiStoriesController</c>
    /// and everything under <c>Services/Beki</c>) — coupling the two would mean this format could
    /// never ship without also turning that pipeline on, or the reverse. Defaults to false, so
    /// nothing this option controls runs until someone deliberately turns it on.
    /// </summary>
    public bool BookFormatEnabled { get; set; }

    // ---- Story pipeline -------------------------------------------------

    /// <summary>Writes the 12-page draft. The most quality-sensitive call in the product.</summary>
    public string StoryGeneratorModel { get; set; } = "gpt-5.6-luna";

    /// <summary>Audits and rewrites the draft: Georgian quality, child agency, safety, continuity.</summary>
    public string StoryReviewerModel { get; set; } = "gpt-5.6-luna";

    /// <summary>Fixes structural violations only. A cheaper model is fine for mechanical repair.</summary>
    public string StoryRepairModel { get; set; } = "gpt-5.6-luna";

    /// <summary>The repair prompt runs at most this many times before the book is failed.</summary>
    public int MaxRepairAttempts { get; set; } = 1;

    /// <summary>Generation is long; a short timeout produces expensive half-written books.</summary>
    public int StoryTimeoutSeconds { get; set; } = 300;

    // ---- Visual pipeline ------------------------------------------------

    /// <summary>Reads the child's photo and returns a structured identity spec.</summary>
    public string IdentityAnalyzerModel { get; set; } = "gpt-5.6-luna";

    /// <summary>
    /// Decides whether a chosen photo shows a child at all, before anything is generated.
    ///
    /// Empty by default, and empty means <see cref="OpenAiOptions.Model"/> — the vision model the
    /// account is already configured with. It used to default to a name from the Beki handoff that
    /// nothing had ever pointed at a real deployment, so every check 400'd on an unknown model,
    /// every 400 became "could not reach the model", and every photo was refused. A default that
    /// only works once somebody configures it is not a default.
    ///
    /// Set it to override — this is the first place a cheaper or faster model is worth trying,
    /// because it runs while a parent waits on the form.
    /// </summary>
    public string PortraitGateModel { get; set; } = string.Empty;

    /// <summary>
    /// The gate runs between choosing a file and seeing it accepted, so it is bounded far tighter
    /// than the generation calls. A parent will wait a second; past ten they assume it broke, and
    /// waiting longer costs more than asking them to try the photo again.
    /// </summary>
    public int PortraitGateTimeoutSeconds { get; set; } = 10;

    /// <summary>Turns the approved story plus identity into the book's Visual Bible.</summary>
    public string VisualBibleModel { get; set; } = "gpt-5.6-luna";

    /// <summary>Writes the labelled image prompts for the cover and each page.</summary>
    public string VisualPromptModel { get; set; } = "gpt-5.6-luna";

    /// <summary>Scores a finished illustration against the references. Must be vision-capable.</summary>
    public string VisualReviewerModel { get; set; } = "gpt-5.6-luna";

    /// <summary>Renders illustrations.</summary>
    public string ImageModel { get; set; } = "gpt-image-2";

    /// <summary>Portrait single-page. The handoff sets 2:3 as the product default.</summary>
    public string InteriorAspectRatio { get; set; } = "2:3";
    public string CoverAspectRatio { get; set; } = "2:3";
    public string InteriorImageSize { get; set; } = "1024x1536";
    public string CoverImageSize { get; set; } = "1024x1536";

    /// <summary>The hero anchor and cover set the standard every page is matched against.</summary>
    public string AnchorImageQuality { get; set; } = "high";
    public string CoverImageQuality { get; set; } = "high";
    public string PageImageQuality { get; set; } = "medium";

    /// <summary>Pages start only once the hero anchor exists, then run in small batches.</summary>
    public int PageConcurrency { get; set; } = 2;
    public int PageStaggerSeconds { get; set; } = 2;

    /// <summary>Bounded so a persistently failing page cannot spend without limit.</summary>
    public int MaxPageRepairAttempts { get; set; } = 1;
    public int MaxPageRegenerationAttempts { get; set; } = 1;

    /// <summary>Canonical Beki asset, attached only to pages whose cast includes Beki.</summary>
    public string BekiReferenceAssetPath { get; set; } = "Assets/Beki/beki-canonical-v1.png";
    public string BekiAssetVersion { get; set; } = "beki-canonical-v1";

    /// <summary>Visual review thresholds. Configurable because they need tuning against real output.</summary>
    public BekiReviewThresholds ReviewThresholds { get; set; } = new();
}

/// <summary>
/// Minimum scores for an illustration to ship. Taken from the developer handoff; identity
/// is the loosest because stylisation legitimately moves a face, while age and outfit are
/// strict because drift there is what makes a book look like it is about a different child.
/// </summary>
public sealed class BekiReviewThresholds
{
    public double HeroIdentityMatch { get; set; } = 0.80;
    public double HeroAgeMatch { get; set; } = 0.90;
    public double HeroOutfitMatch { get; set; } = 0.90;
    public double BekiDesignMatch { get; set; } = 0.90;
    public double CharacterCountCorrect { get; set; } = 0.95;
    public double ChildVisualDominance { get; set; } = 0.85;
    public double SceneActionMatch { get; set; } = 0.85;
    public double TextSafeArea { get; set; } = 0.80;
}
