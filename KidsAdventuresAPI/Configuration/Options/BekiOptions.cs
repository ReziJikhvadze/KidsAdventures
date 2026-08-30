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

    /// <summary>
    /// Routes a Beki-format book through the composite pipeline: the composite Story prompt, the
    /// Visual Scenario v2 call, child/world-only image generation, deterministic pose selection,
    /// exact-PNG compositing and the minimal visual QA contract.
    ///
    /// False, and it has to stay false until the pipeline has drawn a real book somebody has
    /// looked at. Every book in production today is drawn by the path this flag bypasses, and the
    /// bypass is deliberately a single early branch at two entry points rather than a set of
    /// conditions threaded through the illustrator — off, the legacy path is not merely
    /// equivalent, it is the same code executing the same way it did before this flag existed.
    ///
    /// Its own switch rather than a second meaning for <see cref="BookFormatEnabled"/>: that flag
    /// decides the book *format* (eight print spreads, the Beki cover, the resumable job), and the
    /// composite pipeline is a different way of drawing that same format. The two are turned on in
    /// that order — format first, then pipeline — and coupling them would make the second
    /// impossible to try without the first, or the reverse.
    /// </summary>
    public bool CompositePipelineEnabled { get; set; }

    /// <summary>
    /// The model that plans the Visual Scenario, when it should not be the story provider's own.
    ///
    /// Empty by default, and empty means "whatever writes the story writes this too" — the
    /// handoff asks only that the slot be configurable, not that it be different. A value is
    /// honoured by the provider it names: the Gemini client takes an explicitly passed
    /// <c>gemini-</c> id and ignores anything else, because forwarding an OpenAI product name to
    /// Google fails every call.
    /// </summary>
    public string VisualScenarioModel { get; set; } = string.Empty;

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
    /// Whether the intake gate runs at all.
    ///
    /// Off. The gate fails closed by design — a photo it cannot check is a photo it refuses — and
    /// that turned into uploads being refused across the board, which is a product with no way to
    /// make a book. Until those refusals are understood, the check is skipped and every readable
    /// image is accepted.
    ///
    /// What that gives up is worth stating, because it is the reason the gate exists: nothing else
    /// in the pipeline ever asks whether the photo shows a child. The identity analyzer is told to
    /// extract a face, so shown a bottle it describes a bottle, and the book is written,
    /// illustrated and paid for around it. With this off, that book ships.
    ///
    /// Set it to true to restore the check. It is a setting, so that needs no deployment.
    /// </summary>
    public bool PortraitGateEnabled { get; set; }

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

    /// <summary>
    /// How many Beki spreads may draw at once. 1 restores strictly sequential drawing.
    /// The dependency rule (anchoring) is honoured at any setting.
    /// </summary>
    public int SpreadConcurrency { get; set; } = 2;

    /// <summary>
    /// The wall clock a whole book gets, in minutes, before the job is stopped and the pack is
    /// failed.
    ///
    /// There was no such limit, and a book found the shape of the gap: a pack stalled after one
    /// spread and sat in GeneratingStory permanently, because the only thing that ever wrote a
    /// terminal status was the job's own catch, and the job was inside a call that could take
    /// twelve minutes, three times over, sleeping an uncapped Retry-After between attempts. A
    /// parent had paid for it.
    ///
    /// Thirty minutes is well beyond a healthy run — the slowest complete book measured 651
    /// seconds — and well inside the patience of somebody who has been charged. A book that has
    /// not finished by then is not slow, it is lost, and saying so is worth more than waiting.
    ///
    /// The budget is the job's own deadline. The stale-generation sweep uses it too, plus a grace
    /// period, as the point past which a row nobody is updating must be a row whose process is
    /// gone.
    /// </summary>
    public int GenerationBudgetMinutes { get; set; } = 30;

    /// <summary>
    /// How many times a refused Beki illustration is redrawn with the reviewer's corrections
    /// attached, per image.
    ///
    /// Zero, because the first production book measured what a retry actually buys: eight
    /// spreads, sixteen renders, and eight verdicts that still read NEEDS_REVIEW. The redraw
    /// doubled both the wall clock and the image bill and changed not one outcome, because the
    /// refusals were not accidents the model could correct — they were the reserved third, the
    /// gutter and the trimmed band leaving too little frame for the shot the same prompt asks
    /// for. A retry is worth paying for once a refusal is rare enough to be a fluke; until the
    /// composition rules and the QA rules can both be satisfied, it is paying twice for the
    /// same picture.
    ///
    /// Raise it to 1 to restore the old behaviour without a deployment.
    /// </summary>
    public int SpreadRegenerationAttempts { get; set; }

    /// <summary>Canonical Beki asset, attached only to pages whose cast includes Beki.</summary>
    public string BekiReferenceAssetPath { get; set; } = Services.Story.BekiIdentity.ReferenceAssetPath;

    /// <summary>
    /// The print-preparation stage's inputs — the FOGRA39 profile path and the printer's CMYK
    /// ruling. Nested here so the fulfilment job, which already reads these options, needs no new
    /// constructor seam; see <see cref="BekiPrintPrepOptions"/> for why every default is "not
    /// supplied".
    /// </summary>
    public BekiPrintPrepOptions PrintPrep { get; set; } = new();
    public string BekiAssetVersion { get; set; } = Services.Story.BekiIdentity.Version;

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
