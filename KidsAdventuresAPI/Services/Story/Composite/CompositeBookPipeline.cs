using System.Diagnostics;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using SixLabors.ImageSharp;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// A book that stopped, with the word it stopped on.
///
/// The code is the whole value of the type. Everything downstream of a failed book — the admin
/// notification, the support answer, the decision whether a retry could possibly help — turns on
/// which of the eight agreed failures happened, and a bare exception message makes that a matter
/// of reading English prose in a log.
/// </summary>
public sealed class CompositePipelineException(string failureCode, string message, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    /// <summary>One of <see cref="CompositeFailureCodes"/>.</summary>
    public string FailureCode { get; } = failureCode;

    /// <summary>Null for a book-level failure; the page for a per-spread one.</summary>
    public int? Page { get; init; }
}

/// <summary>
/// The four normalized inputs plus the job they belong to, handed to the illustrator by a caller
/// that actually knows them.
///
/// It exists because <see cref="IBekiBookGenerator.IllustrateAsync"/> receives a plan and a
/// photograph and nothing else: no age, no gender, no theme. The legacy path never needed them —
/// everything it draws from is inside the plan — and the composite path cannot do without them,
/// because the age band and the theme decide the Visual Scenario input and the theme decides which
/// approved world reference every image is generated against. So the fulfilment job, which holds
/// the run and the pack, supplies them; a caller that does not have them (the preview cover) simply
/// passes nothing and stays on the legacy path.
/// </summary>
public sealed record CompositeBookContext
{
    /// <summary>The pack id. Logged against every AI call, per the observability contract.</summary>
    public required Guid JobId { get; init; }

    /// <summary>The purchase as it was stored, before any mapping.</summary>
    public required BookGenerationInput Input { get; init; }

    /// <summary>
    /// What an earlier attempt at this book left in storage, already fetched.
    ///
    /// It rides on the context for the same reason the four inputs do: only the fulfilment job can
    /// read a blob, and the illustrator it passes through has no storage dependency and is not
    /// about to grow one for a path that is off by default. Empty means a first attempt.
    ///
    /// It also supersedes <c>IllustrateAsync</c>'s own <c>existingSpreads</c> on this path — one
    /// resume state rather than two, because the composited pages, their bases and the scenario
    /// they were drawn against have to be adopted together or not at all.
    /// </summary>
    public CompositeResumeState Resume { get; init; } = CompositeResumeState.Empty;

    /// <summary>
    /// Where to persist the validated scenario, called before the first image call.
    /// See <see cref="CompositeBookRequest.OnScenario"/> for why the timing is the point.
    /// </summary>
    public Func<string, Task>? OnScenario { get; init; }
}

/// <summary>
/// One generate-and-review cycle, measured.
///
/// Its own record rather than the generator's <see cref="BekiImageAttempt"/> so the pipeline owes
/// nothing to the shape of a result type it does not produce; the generator maps one to the other
/// at the seam, which is the only place both are in scope.
/// </summary>
/// <param name="GenerationMs">Zero when this cycle re-composited rather than redrawing.</param>
/// <param name="ReviewMs">How long the minimal visual QA call took, including its parse retry.</param>
/// <param name="Verdict">The reviewer's verdict as one line — the thing telemetry is read for.</param>
/// <param name="Accepted">Whether this cycle's page is the one that shipped.</param>
public sealed record CompositeAttempt(long GenerationMs, long ReviewMs, string Verdict, bool Accepted);

/// <summary>What one spread came out as, and every receipt it produced on the way.</summary>
public sealed record CompositeSpreadResult
{
    public required int Page { get; init; }

    /// <summary>The child/world image, before Beki. Kept: it is what a re-composite starts from.</summary>
    public required byte[] BasePng { get; init; }

    /// <summary>The page: base plus the approved Beki PNG, pasted.</summary>
    public required byte[] CompositePng { get; init; }

    public required BekiCompositionManifest Manifest { get; init; }

    /// <summary>The prompt the base was generated from, stored the way the legacy path stores its own.</summary>
    public required string Prompt { get; init; }

    public required string PoseId { get; init; }

    /// <summary>LEFT or RIGHT, from the config's rhythm.</summary>
    public required string TextSide { get; init; }

    /// <summary>The reviewer's verdict as one line, or the reason there is not one.</summary>
    public required string Verdict { get; init; }

    /// <summary>How many base images were paid for. 1 unless QA asked for a regeneration.</summary>
    public required int BaseAttempts { get; init; }

    /// <summary>
    /// One row per generate-and-review cycle this page cost, in order.
    ///
    /// Not derivable from <see cref="BaseAttempts"/>, which is why it is carried rather than
    /// reconstructed: a page re-composited after a placement failure was reviewed twice and
    /// generated once, and the telemetry the fulfilment job writes is read to answer "what did the
    /// second attempt object to", which only the rows can answer.
    /// </summary>
    public IReadOnlyList<CompositeAttempt> Attempts { get; init; } = [];

    /// <summary>True when this page was adopted whole from an earlier attempt at the same book.</summary>
    public bool Adopted { get; init; }

    /// <summary>
    /// True when no registry keyword matched this page's Beki sentence and the neutral hover was
    /// used instead. Carried out of the pipeline rather than only logged: a book quietly composited
    /// from eight fallbacks is a scenario-prompt problem, and it is only visible if it is counted.
    /// </summary>
    public bool PoseFallback { get; init; }
}

/// <summary>
/// The artifacts a finished composite book has to persist: the scenario the whole book was planned
/// from, and one composition receipt per page.
///
/// Carried out of the pipeline rather than written by it. The pipeline has no storage dependency on
/// purpose — it is run in tests with no blob account and no container — and the fulfilment job
/// already owns every naming decision about where a pack's files live.
/// </summary>
public sealed record CompositeBookArtifacts
{
    /// <summary>The validated Visual Scenario, exactly as the model returned it.</summary>
    public required string ScenarioJson { get; init; }

    public required IReadOnlyList<CompositeSpreadArtifact> Spreads { get; init; }
}

/// <summary>One page's composition manifest, ready to store beside the image it describes.</summary>
/// <param name="BasePng">
/// The child/world image before Beki was pasted onto it, which has to be stored and not only used.
///
/// It is the continuity reference: the picture a later spread reusing the same creature is shown
/// and told to match. A run that resumed with only the composited pages would have to either
/// forgo continuity on every redrawn spread — letting a recurring character be redesigned halfway
/// through a book — or hand the image model a page with Beki already on it, which is a picture of
/// Beki and the one image this pipeline promises never to send. So the base is an artifact in its
/// own right.
/// </param>
public sealed record CompositeSpreadArtifact(
    int SpreadNumber, string PoseId, string ManifestJson, string OutputSha256, byte[] BasePng);

/// <summary>
/// What an earlier attempt at this same book left behind, and what this attempt may therefore
/// adopt instead of paying for again.
/// </summary>
/// <param name="ScenarioJson">
/// The Visual Scenario that attempt planned, if it got that far.
///
/// Adopting it is not an optimisation. The scenario fixes the child's outfit and the recurring
/// elements for the whole book, and a resumed run that planned a fresh one would dress the child
/// differently on the spreads it redraws from the spreads it adopts — a book where the child
/// changes clothes at page four, assembled entirely from pages that each passed review.
/// </param>
/// <param name="Spreads">Composited pages already accepted and stored, by page number.</param>
/// <param name="BaseImages">
/// The pre-composite base of each of those pages, by page number, so continuity survives a resume.
/// Sparse is normal — a page stored before base images were kept has none.
/// </param>
public sealed record CompositeResumeState(
    string? ScenarioJson,
    IReadOnlyDictionary<int, byte[]> Spreads,
    IReadOnlyDictionary<int, byte[]> BaseImages)
{
    public static readonly CompositeResumeState Empty = new(
        null,
        new Dictionary<int, byte[]>(),
        new Dictionary<int, byte[]>());
}

/// <summary>
/// One run of the pipeline, as one object.
///
/// A record rather than nine positional parameters, because three of them are optional, two are
/// callbacks and the difference between "no scenario yet" and "a scenario to adopt" is the whole
/// resume story. A call site that has to count commas is a call site that will one day pass the
/// composite where the base belongs.
/// </summary>
public sealed record CompositeBookRequest
{
    public required CompositeBookContext Context { get; init; }

    /// <summary>The plan the parent previewed. Null asks the composite planner for a new one.</summary>
    public MasterStory? ExistingPlan { get; init; }

    public required byte[] ChildPhoto { get; init; }

    public required string ChildPhotoContentType { get; init; }

    public CompositeResumeState Resume { get; init; } = CompositeResumeState.Empty;

    /// <summary>
    /// Called with the validated scenario before the first image call, and awaited.
    ///
    /// Before, and awaited, for one reason: a job that dies during spread three has to come back to
    /// the scenario those three pages were drawn against. Persisting it with the finished book
    /// would mean the only attempt that stores it is the attempt that did not need it.
    /// </summary>
    public Func<string, Task>? OnScenario { get; init; }

    /// <summary>
    /// Called once per finished page, in page order, before the next one starts — the same
    /// contract the legacy generator's callback has, and for the same reason: a parent is watching
    /// a spinner for several minutes.
    /// </summary>
    public Func<CompositeSpreadResult, Task>? OnSpread { get; init; }
}

/// <summary>Everything one run of the pipeline produced.</summary>
public sealed record CompositeBookResult
{
    public required MasterStory Plan { get; init; }

    public required StoryBoundaryOutput Boundary { get; init; }

    public required VisualScenarioV2 Scenario { get; init; }

    public required IReadOnlyList<CompositeSpreadResult> Spreads { get; init; }

    public required CompositeBookArtifacts Artifacts { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface ICompositeBookPipeline
{
    /// <summary>
    /// Input to eight composited spreads: normalize, story, boundary, Visual Scenario, then per
    /// page an image, a pose, a composite and a review.
    /// </summary>
    Task<CompositeBookResult> RunAsync(CompositeBookRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The continuous cover base, which this campaign cannot draw.
    ///
    /// Present, and failing loudly, rather than absent: the cover contract requires seven regions
    /// from the printer-approved dieline and says in as many words that a missing geometry stops
    /// the job with <see cref="CompositeFailureCodes.LayoutFailed"/> and never substitutes the
    /// interior bleed. A method that quietly returned the legacy cover would be that substitution
    /// with extra steps.
    /// </summary>
    Task<byte[]> DrawCoverAsync(
        CompositeBookContext context,
        VisualScenarioV2 scenario,
        byte[] childPhoto,
        string childPhotoContentType,
        CancellationToken cancellationToken);
}

/// <summary>
/// The composite pipeline, end to end (handoff §6, steps 0 through 7).
///
/// One class rather than a stage-per-service arrangement, because the stages are not independently
/// useful: there is exactly one order they run in, each one's output is the next one's only input,
/// and a scenario without a boundary or a composite without a scenario is not a thing anybody
/// wants. What that buys is that the whole sequence — including which failure code stops it where —
/// is readable top to bottom in one file.
///
/// Two rules shape everything below.
///
/// Beki is never generated. The image model receives the child's photograph and the approved world
/// reference and is told, in the prompt's hard constraints, not to draw her or anything like her;
/// she arrives afterwards as an exact PNG at coordinates the config decides. Every place this class
/// assembles a reference list is a place that rule could be broken by adding one entry, so the
/// reference lists are short and built in one method.
///
/// Nothing is retried more than the contract allows. One Visual Scenario retry, one base
/// regeneration, one re-composite, one QA parse retry, and then the book stops with a code. The
/// legacy pipeline learned this the expensive way — a refused spread redrawn twice changed no
/// outcome and doubled the bill — and the counts here are the supplier's own numbers from
/// <c>pipeline_config_v1.json</c>.
/// </summary>
public sealed class CompositeBookPipeline(
    IStoryModelClient storyClient,
    IOpenAiService openAi,
    IMasterStoryService masterStory,
    IOptions<BekiOptions> bekiOptions,
    IOptions<BekiPrintLayoutOptions> printLayoutOptions,
    ILogger<CompositeBookPipeline> logger) : ICompositeBookPipeline
{
    /// <summary>
    /// The shape asked of the image provider.
    ///
    /// The same value the legacy Beki path uses, and for the same reason: the providers offer three
    /// or four fixed shapes and none of them is 15:7, so the widest landscape on offer is the one
    /// that survives normalization with the least thrown away. <see
    /// cref="CompositeDeterministicChecks"/> is what actually enforces that the render can become a
    /// printed spread.
    /// </summary>
    public const string SpreadImageSize = BekiBookGenerator.SpreadImageSize;

    private readonly BekiOptions _options = bekiOptions.Value;

    /// <summary>
    /// The registry, the config and the nine pose PNGs, loaded on first use and not before.
    ///
    /// Lazy because this service is registered unconditionally and injected into the illustrator,
    /// so it is constructed for every book in production — including every book drawn by the legacy
    /// path, on a deployment where the composite assets may not even be present. Loading 16 MB of
    /// artwork and verifying nine hashes to then not use any of it would be a tax on the path this
    /// flag exists to leave alone.
    /// </summary>
    private readonly Lazy<BekiCompositeEngine> _engine = new(() => BekiCompositeEngine.Create());

    public async Task<CompositeBookResult> RunAsync(
        CompositeBookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = request.Context;
        var childPhoto = request.ChildPhoto;
        var childPhotoContentType = request.ChildPhotoContentType;
        var resume = request.Resume ?? CompositeResumeState.Empty;

        var warnings = new List<string>();

        // ---- Step 0: validate and normalize, before anything is paid for --------------------
        var normalized = InputNormalization.Normalize(context.Input, childPhoto);
        if (!normalized.IsValid)
        {
            throw new CompositePipelineException(
                CompositeFailureCodes.InvalidBookInput,
                $"The book input cannot be used: {string.Join(" ", normalized.Problems)}");
        }

        var input = normalized.Story!;
        var theme = CompositeThemeReferences.For(input.ThemeId);

        logger.LogInformation(
            "Composite pipeline {JobId}: age band {AgeBand}, gender {Gender}, theme {ThemeId} "
            + "({ThemeName}), reference {ThemeFile}.",
            context.JobId, input.AgeBand, input.ChildGender, input.ThemeId, theme.OfficialName,
            theme.FileName);

        // ---- Step 1: the story ---------------------------------------------------------------
        var plan = request.ExistingPlan ?? await WriteStoryAsync(context, input, cancellationToken);

        if (request.ExistingPlan is not null)
        {
            // Adopted, never rewritten. The parent read this story and bought it; the composite
            // prompt would write a different one, and a book that is not the book somebody chose
            // is a worse outcome than a book written by the older prompt. Everything the older
            // prompt puts in that this pipeline must not carry — English copy, the appearance
            // paragraph, the eye colour — is dropped by the boundary below rather than trusted.
            logger.LogInformation(
                "Composite pipeline {JobId}: adopting the story the parent previewed; no new "
                + "planning call.", context.JobId);
        }

        var boundaryResult = StoryBoundary.From(plan);
        if (!boundaryResult.IsValid)
        {
            throw new CompositePipelineException(
                CompositeFailureCodes.StoryFailed,
                $"The story cannot be mapped to the boundary: {string.Join(" ", boundaryResult.Problems)}");
        }

        var boundary = boundaryResult.Boundary!;

        // ---- Step 2: the Visual Scenario ------------------------------------------------------
        //
        // Adopted when a previous attempt at this book already planned one, and planned afresh
        // otherwise. Adopting is the correctness case, not the cheap one: the scenario fixes the
        // outfit and the recurring elements for all nine pictures, so a resumed run that planned a
        // second scenario would redraw its missing spreads against a different outfit from the ones
        // it is adopting — and every page would still pass its own review.
        var adoptedScenario = AdoptScenario(context, resume, warnings);

        if (adoptedScenario is null)
        {
            /*
              A replan and adopted artwork cannot both stand.

              The scenario is what every page was drawn against — the outfit, the recurring
              elements — so a book planned twice is a book drawn to two different specifications.
              Keeping the pages and planning a new scenario produces the exact failure the whole
              resume path exists to avoid, and it produces it silently: eight images that each
              passed their own review, a scenario record that describes none of them, and a child
              who changes clothes partway through.

              Which way it resolves depends on what has already been paid for.

              With pages already drawn and stored, redrawing them is money spent twice on artwork
              somebody may have already looked at, and the cause is not a book fault — it is a
              scenario this deployment can no longer read or no longer accepts, which is an
              operational question. So the job stops, names the stored scenario, and a person
              decides whether to clear it and redraw or to fix what made it unreadable.

              With nothing drawn there is nothing to lose and no decision to make: the bases are
              dropped along with the pages, because a base image belongs to the scenario its page
              was planned under, and the run plans freely.
            */
            if (resume.Spreads.Count > 0)
            {
                logger.LogError(
                    "Composite pipeline {JobId}: {Adopted} spread(s) are already stored but their "
                    + "Visual Scenario cannot be used, so this book would be finished against a "
                    + "scenario those pages were never drawn from. Stopping for a human.",
                    context.JobId, resume.Spreads.Count);

                throw new CompositePipelineException(
                    CompositeFailureCodes.VisualScenarioFailed,
                    $"{resume.Spreads.Count} spread(s) from an earlier attempt are stored, but the "
                    + "Visual Scenario they were drawn against is missing or no longer valid. "
                    + "Planning a new one would finish the book to a different specification. "
                    + "Clear the stored spreads to redraw the book, or restore the scenario.");
            }

            resume = CompositeResumeState.Empty;
        }

        var (scenario, scenarioJson) = adoptedScenario
            ?? await PlanVisualScenarioAsync(context, input, theme, boundary, cancellationToken);

        // Persisted before the first image call, and awaited, so that the attempt which dies on
        // spread three is not the attempt that never wrote down what it was drawing.
        if (request.OnScenario is not null)
        {
            await request.OnScenario(scenarioJson);
        }

        // ---- Steps 3-7: one page at a time ----------------------------------------------------
        //
        // Sequential, and not because it is simpler. Each page's continuity reference is the most
        // recent accepted base image containing a recurring element, so page five's request depends
        // on what page four actually produced; drawing them at once would mean either no continuity
        // or a dependency graph, and the legacy path's graph exists to solve a problem this
        // pipeline solves by having one obvious order.
        var visualLock = scenario.VisualLock!;
        var continuity = new CompositeContinuity();
        var spreads = new List<CompositeSpreadResult>(BookFormat.SpreadCount);

        foreach (var page in scenario.Spreads!)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (resume.Spreads.TryGetValue(page.Page, out var alreadyDrawn) && alreadyDrawn.Length > 0)
            {
                /*
                  An adopted page still teaches the pages after it.

                  Skipping it entirely was the bug: spread two introduces the story's creature, a
                  resumed run adopts spread two and redraws spread three, and spread three arrives
                  with no continuity reference and redesigns the creature — in the middle of a book
                  where the reader can see both pages at once.

                  The reference restored here is the BASE image, never the composited one. The
                  composite has the approved Beki pasted onto it, and the continuity instruction
                  tells the model to copy the named elements from the attached picture: hand it a
                  composite and the thing it is being shown is Beki.
                */
                var elements = CompositeIllustrationPrompt.RelevantRecurringElements(
                    visualLock.RecurringElements, page.ChildWorldScene);

                if (resume.BaseImages.TryGetValue(page.Page, out var storedBase)
                    && storedBase.Length > 0)
                {
                    continuity.Remember(elements, storedBase);
                }
                else if (elements.Count > 0)
                {
                    warnings.Add(
                        $"Spread {page.Page} was adopted without its base image, so the recurring "
                        + "elements it introduced cannot be a continuity reference for later spreads.");
                }

                spreads.Add(AdoptedSpread(page.Page, alreadyDrawn));
                continue;
            }

            var spread = await DrawSpreadAsync(
                context, input, theme, scenario, page, continuity, childPhoto, childPhotoContentType,
                cancellationToken);

            spreads.Add(spread);

            if (spread.PoseFallback)
            {
                warnings.Add(
                    $"Spread {spread.Page}: no pose keyword matched the scenario's Beki action, so "
                    + "the neutral hover was composited.");
            }

            if (request.OnSpread is not null)
            {
                await request.OnSpread(spread);
            }
        }

        return new CompositeBookResult
        {
            Plan = plan,
            Boundary = boundary,
            Scenario = scenario,
            Spreads = spreads,
            Warnings = warnings,
            Artifacts = new CompositeBookArtifacts
            {
                ScenarioJson = scenarioJson,
                Spreads = spreads
                    .Where(spread => !spread.Adopted)
                    .Select(spread => new CompositeSpreadArtifact(
                        spread.Page,
                        spread.PoseId,
                        spread.Manifest.ToJson(),
                        spread.Manifest.Output.Sha256,
                        spread.BasePng))
                    .ToList()
            }
        };
    }

    public async Task<byte[]> DrawCoverAsync(
        CompositeBookContext context,
        VisualScenarioV2 scenario,
        byte[] childPhoto,
        string childPhotoContentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scenario);

        var geometry = CompositeCoverGeometryResolver.TryResolve(printLayoutOptions.Value);

        if (geometry is null)
        {
            // Stated as a failure, not logged as a warning and worked around. The alternatives are
            // both worse than a stopped job: a cover generated to interior geometry puts the child
            // across the spine, and a book delivered without a cover is a book nobody can sell.
            logger.LogError(
                "Composite pipeline {JobId}: no printer-approved cover geometry is configured, so "
                + "the continuous cover base cannot be generated. The interior sheet's geometry is "
                + "not a substitute and is not being used.", context.JobId);

            throw new CompositePipelineException(
                CompositeFailureCodes.LayoutFailed,
                "The composite cover needs the active printer-approved cover geometry — back "
                + "panel, spine, hinge, front panel, title-safe, child/action, Beki integration and "
                + "wrap — and this deployment has none configured. The interior bleed must never be "
                + "substituted for it.");
        }

        // Unreachable until the cover composer campaign lands the dieline. Written out anyway, and
        // not left as a TODO: the shape of the call is what makes the missing input obvious, and a
        // stub returning null would hide which seven values are actually needed.
        var cover = scenario.Cover!;
        var input = InputNormalization.Normalize(context.Input, childPhoto).Story!;
        var theme = CompositeThemeReferences.For(input.ThemeId);

        var prompt = CompositeIllustrationPrompt.ForCover(
            geometry,
            input.ChildAge,
            theme,
            cover.FrontChildWorldScene!,
            cover.BackEnvironment!,
            scenario.VisualLock!.ChildOutfit!,
            CompositeIllustrationPrompt.RelevantRecurringElements(
                scenario.VisualLock.RecurringElements, cover.FrontChildWorldScene));

        var (image, _) = await GenerateBaseImageAsync(
            context, page: null, prompt,
            References(childPhoto, childPhotoContentType, theme, continuityImage: null),
            cancellationToken);

        return image;
    }

    /// <summary>
    /// The stored scenario, when there is one and it still holds.
    ///
    /// Re-validated rather than trusted, and that is not defensiveness about our own storage. The
    /// scenario is checked against the supplied schema and the contract's semantic rules, and both
    /// of those are documents the illustration supplier revises: a scenario written under last
    /// month's rules can be a scenario this month's pipeline must not draw from. When it no longer
    /// validates the honest answer is a new one — a redrawn book against current rules beats a book
    /// half-drawn under each.
    ///
    /// Null means "plan one", which is also what an attempt that never got that far leaves behind.
    /// </summary>
    private (VisualScenarioV2 Scenario, string Json)? AdoptScenario(
        CompositeBookContext context, CompositeResumeState resume, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(resume.ScenarioJson))
        {
            return null;
        }

        var validation = VisualScenarioValidator.Validate(resume.ScenarioJson);

        if (!validation.IsValid)
        {
            logger.LogWarning(
                "Composite pipeline {JobId}: the stored Visual Scenario no longer validates, so a "
                + "new one is being planned — {Problems}", context.JobId, validation.Summary);

            warnings.Add(
                "The stored Visual Scenario no longer validates and was replanned; spreads adopted "
                + "from the earlier attempt were drawn against the old one.");

            return null;
        }

        logger.LogInformation(
            "Composite pipeline {JobId}: adopting the Visual Scenario an earlier attempt planned; "
            + "no new scenario call.", context.JobId);

        return (validation.Scenario!, resume.ScenarioJson!);
    }

    // -----------------------------------------------------------------------------------------
    // Step 1 — story
    // -----------------------------------------------------------------------------------------

    private async Task<MasterStory> WriteStoryAsync(
        CompositeBookContext context, NormalizedBookInput input, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();

        try
        {
            var result = await masterStory.WriteCompositePlanAsync(
                CompositeStoryInput.From(input), [], cancellationToken);

            started.Stop();
            LogModelCall(
                context, "story", result.Model, MasterStoryPromptComposite.Version,
                started.ElapsedMilliseconds, retryCount: 0, validation: "accepted");

            return result.Story;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            started.Stop();
            LogModelCall(
                context, "story", masterStory.ModelName, MasterStoryPromptComposite.Version,
                started.ElapsedMilliseconds, retryCount: 0, validation: "failed");

            throw new CompositePipelineException(
                CompositeFailureCodes.StoryFailed, "The composite story call failed.", ex);
        }
    }

    // -----------------------------------------------------------------------------------------
    // Step 2 — Visual Scenario v2
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// One call, one validation, one retry, then stop — the contract's own sequence.
    ///
    /// Both attempts go through the same validator, and the retry is sent the validator's short
    /// error list appended to the original ask. What it is not sent is a rewritten prompt: the
    /// second attempt has to be the same scenario without the fault, and a model given a different
    /// instruction returns a different book's pictures.
    /// </summary>
    private async Task<(VisualScenarioV2 Scenario, string Json)> PlanVisualScenarioAsync(
        CompositeBookContext context,
        NormalizedBookInput input,
        CompositeThemeReference theme,
        StoryBoundaryOutput boundary,
        CancellationToken cancellationToken)
    {
        var inputJson = CompositeVisualScenarioPrompt.InputJson(input, theme, boundary);
        var model = VisualScenarioModel;

        VisualScenarioValidationResult? previous = null;

        for (var attempt = 0; attempt <= 1; attempt++)
        {
            var user = attempt == 0
                ? CompositeVisualScenarioPrompt.User(inputJson)
                : CompositeVisualScenarioPrompt.RetryUser(inputJson, previous!.Problems);

            var started = Stopwatch.StartNew();
            string? answer = null;
            Exception? failure = null;

            try
            {
                // JsonElement rather than the typed model: the validator needs the response as the
                // model actually wrote it, both to evaluate the supplied schema against it and to
                // store it. Deserializing to VisualScenarioV2 here would throw away the raw text on
                // exactly the responses that need explaining.
                var result = await storyClient.CompleteAsync<JsonElement>(
                    model,
                    CompositeVisualScenarioPrompt.System,
                    user,
                    CompositeVisualScenarioPrompt.SchemaName,
                    CompositeVisualScenarioPrompt.ResponseSchema(),
                    cancellationToken);

                answer = result.Value.GetRawText();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A transport failure and an unparseable answer arrive here the same way, and both
                // are worth the one retry — the alternative is a book lost to a dropped connection.
                failure = ex;
            }

            started.Stop();

            var validation = failure is null
                ? VisualScenarioValidator.Validate(answer)
                : new VisualScenarioValidationResult
                {
                    IsValid = false,
                    Problems =
                    [
                        new VisualScenarioProblem(
                            VisualScenarioProblemCodes.MalformedJson,
                            $"the scenario call failed: {failure.Message}")
                    ]
                };

            LogModelCall(
                context, "visual_scenario", model, CompositeVisualScenarioPrompt.Version,
                started.ElapsedMilliseconds, attempt,
                validation.IsValid ? "accepted" : validation.Summary);

            if (validation.IsValid)
            {
                return (validation.Scenario!, answer!);
            }

            previous = validation;

            logger.LogWarning(
                "Composite pipeline {JobId}: Visual Scenario attempt {Attempt} rejected — {Problems}",
                context.JobId, attempt + 1, validation.Summary);
        }

        throw new CompositePipelineException(
            CompositeFailureCodes.VisualScenarioFailed,
            "Two Visual Scenario attempts were both invalid: " + previous!.Summary);
    }

    /// <summary>
    /// Which model plans the scenario.
    ///
    /// Empty configuration means the story provider's own model, which is what the handoff asks
    /// for — the slot exists so the scenario *can* be moved, not so it must be. The fallback is
    /// <see cref="IMasterStoryService.ModelName"/> rather than a literal, because that is the name
    /// the OpenAI client would need and the one the Gemini client is entitled to ignore in favour
    /// of its own configured story model.
    /// </summary>
    private string VisualScenarioModel =>
        string.IsNullOrWhiteSpace(_options.VisualScenarioModel)
            ? masterStory.ModelName
            : _options.VisualScenarioModel.Trim();

    // -----------------------------------------------------------------------------------------
    // Steps 3-7 — one page
    // -----------------------------------------------------------------------------------------

    private async Task<CompositeSpreadResult> DrawSpreadAsync(
        CompositeBookContext context,
        NormalizedBookInput input,
        CompositeThemeReference theme,
        VisualScenarioV2 scenario,
        VisualScenarioSpread page,
        CompositeContinuity continuity,
        byte[] childPhoto,
        string childPhotoContentType,
        CancellationToken cancellationToken)
    {
        var textSide = CompositeSpreadRhythm.TextSideFor(page.Page);
        var visualLock = scenario.VisualLock!;

        var elements = CompositeIllustrationPrompt.RelevantRecurringElements(
            visualLock.RecurringElements, page.ChildWorldScene);

        var reference = continuity.For(elements);

        var prompt = CompositeIllustrationPrompt.ForSpread(new CompositeSpreadPromptInput
        {
            Page = page.Page,
            ChildAge = input.ChildAge,
            Theme = theme,
            ChildWorldScene = page.ChildWorldScene!,
            ChildOutfit = visualLock.ChildOutfit!,
            RecurringElements = elements,
            ContinuityElementNames = reference?.ElementNames ?? []
        });

        // Chosen from the scenario's Beki sentence and nothing else, before a single pixel exists.
        // The selection cannot depend on the picture, because the picture was drawn with a hole
        // shaped like this pose in it.
        var selection = BekiPoseSelector.Select(_engine.Value.Registry, page.BekiAction);

        if (selection.Fallback)
        {
            logger.LogWarning(
                "Composite pipeline {JobId} spread {Page}: no pose keyword matched \"{Action}\"; "
                + "pose_selection_fallback=true, using {PoseId}.",
                context.JobId, page.Page, page.BekiAction, selection.PoseId);
        }

        var (rawPng, generationMs) = await GenerateBaseImageAsync(
            context, page.Page, prompt,
            References(childPhoto, childPhotoContentType, theme, reference?.Image),
            cancellationToken);

        var basePng = NormalizeToSpread(context, page.Page, rawPng);

        var baseAttempts = 1;
        var recomposited = false;
        var regenerated = false;

        // One row per generate-and-review cycle, kept whether it passed or not. A page that shipped
        // on its second attempt is a page whose first verdict is the only record of what was wrong,
        // and that verdict is what the fulfilment job's telemetry is read for.
        var attempts = new List<CompositeAttempt>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var composite = Composite(context, page, basePng, selection.PoseId, textSide);

            var (verdict, reviewMs) = await ReviewAsync(
                context, page, scenario, composite, textSide, childPhoto, childPhotoContentType,
                theme, elements, cancellationToken);

            attempts.Add(new CompositeAttempt(
                generationMs, reviewMs, verdict.ToString(), verdict.Passed));

            if (verdict.Passed)
            {
                // Only an accepted page becomes a continuity reference. An image the reviewer
                // refused is precisely the one a later spread must not be told to match.
                continuity.Remember(elements, basePng);

                return new CompositeSpreadResult
                {
                    Page = page.Page,
                    BasePng = basePng,
                    CompositePng = composite.Png,
                    Manifest = composite.Manifest,
                    Prompt = prompt,
                    PoseId = selection.PoseId,
                    TextSide = textSide,
                    Verdict = verdict.ToString(),
                    BaseAttempts = baseAttempts,
                    Attempts = attempts,
                    PoseFallback = selection.Fallback,
                };
            }

            // The contract's two second chances, each usable once. Which one applies is the
            // reviewer's own recommended_action, because it is the only reader that can tell a
            // badly generated world from a well-generated one with Beki in the wrong part of it —
            // and the two cost very differently: one is another paid image call, the other is
            // arithmetic.
            if (verdict.RecommendedAction == CompositeQaVerdict.ActionRegenerateBase && !regenerated)
            {
                regenerated = true;
                baseAttempts++;

                logger.LogWarning(
                    "Composite pipeline {JobId} spread {Page}: QA asked for a new base image — {Verdict}",
                    context.JobId, page.Page, verdict);

                (rawPng, generationMs) = await GenerateBaseImageAsync(
                    context, page.Page, prompt,
                    References(childPhoto, childPhotoContentType, theme, reference?.Image),
                    cancellationToken);

                basePng = NormalizeToSpread(context, page.Page, rawPng);

                continue;
            }

            if (verdict.RecommendedAction == CompositeQaVerdict.ActionRecompositeBeki && !recomposited)
            {
                recomposited = true;

                // Nothing was generated for this cycle, and the row says so: a zero here is the
                // difference between "the second attempt was free" and "the second attempt was
                // another image bill", which is the question the retry rules exist to answer.
                generationMs = 0;

                // A second deterministic pass over the same bytes, and deliberately not a second
                // image call — the contract is explicit that a placement fault must not buy a new
                // picture. It is also, deliberately, not a nudged anchor: the anchors are data the
                // partners approved, the only correct fix for a badly placed Beki is a different
                // configured anchor, and inventing one here to satisfy a retry would put the
                // character somewhere nobody signed off. So this attempt exists to survive a
                // composite that failed for a transient reason, and a second identical verdict
                // stops the book rather than guessing.
                logger.LogWarning(
                    "Composite pipeline {JobId} spread {Page}: QA asked for a re-composite — {Verdict}",
                    context.JobId, page.Page, verdict);

                continue;
            }

            logger.LogError(
                "Composite pipeline {JobId} spread {Page}: stopping for human review after "
                + "{BaseAttempts} base image(s) — {Verdict}",
                context.JobId, page.Page, baseAttempts, verdict);

            throw new CompositePipelineException(
                CompositeFailureCodes.ImageQaFailed,
                $"Spread {page.Page} failed the minimal visual QA and is marked for human review: {verdict}")
            {
                Page = page.Page
            };
        }
    }

    /// <summary>
    /// One paid image call, checked deterministically before anything else happens to it.
    ///
    /// The checks are here rather than at the call site because their whole value is being between
    /// the provider and the compositor: a base that will not decode composites into an exception
    /// several frames away from the reason, and a base of the wrong shape produces a page that is
    /// only wrong once it has been cropped for print.
    /// </summary>
    /// <returns>The picture, and how long it took — the latter for this page's attempt record.</returns>
    private async Task<(byte[] Png, long GenerationMs)> GenerateBaseImageAsync(
        CompositeBookContext context,
        int? page,
        string prompt,
        StoryImageReference? references,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        byte[] image;

        try
        {
            // requireReferences, and it is not caution — it is the difference between this
            // pipeline working and appearing to. The child's likeness lives only in the attached
            // photograph (the composite plan has no appearance description to fall back on), the
            // world only in the approved theme reference, and a recurring creature only in the
            // continuity image. The OpenAI path's silent retreat from the edit route to
            // images/generations would return a picture of a different child in a generic world,
            // and this pipeline would then composite the approved Beki onto it, review it, store
            // it and print it. Better a stopped book with a named failure code.
            image = await openAi.GenerateStoryImageAsync(
                prompt, references, cancellationToken, SpreadImageSize, requireReferences: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            started.Stop();
            LogModelCall(
                context, "image_generation", _options.ImageModel, CompositeIllustrationPrompt.Version,
                started.ElapsedMilliseconds, retryCount: 0, validation: "failed", page: page);

            throw new CompositePipelineException(
                CompositeFailureCodes.ImageGenerationFailed,
                $"The image call for {(page is null ? "the cover" : $"spread {page}")} failed.", ex)
            {
                Page = page
            };
        }

        started.Stop();

        var problems = CompositeDeterministicChecks.BaseImageProblems(image);

        LogModelCall(
            context, "image_generation", _options.ImageModel, CompositeIllustrationPrompt.Version,
            started.ElapsedMilliseconds, retryCount: 0,
            validation: problems.Count == 0 ? "accepted" : string.Join("; ", problems), page: page);

        if (problems.Count > 0)
        {
            throw new CompositePipelineException(
                CompositeFailureCodes.ImageGenerationFailed,
                $"The generated base image for {(page is null ? "the cover" : $"spread {page}")} is "
                + $"not usable: {string.Join(" ", problems)}")
            {
                Page = page
            };
        }

        return (image, started.ElapsedMilliseconds);
    }

    /// <summary>
    /// Brings the provider's frame to the printed spread's shape, before anything else sees it.
    ///
    /// This is the step whose absence made every other number on the page wrong. The image models
    /// offer 3:2 and the printed spread is 15:7, so a base composited at 3:2 gets roughly a third
    /// of its height removed at layout time — and everything computed against the taller canvas
    /// travels with it. Beki's configured visible height of 0.333 became about 0.476 of the page
    /// actually printed, half again the size the partners approved; her anchor moved; the
    /// composition manifest recorded coordinates for a canvas that never existed as a page; and the
    /// reviewer passed or failed a picture with a band top and bottom that the reader would never
    /// see. Normalizing first is what makes the manifest, the verdict and the printed sheet three
    /// descriptions of one thing.
    ///
    /// A centred crop and nothing else, which is exactly what the handoff permits (§8: a tiny
    /// centred crop to normalize to 15:7 is allowed, stretching is forbidden) and exactly what
    /// <see cref="SpreadArtCrop.CropToRatio"/> already does for the reviewer's copy on the legacy
    /// path. Reused rather than reimplemented so the two cannot drift: the crop the composite
    /// pipeline bakes in and the crop the layout stage applies have to be the same arithmetic.
    ///
    /// The image prompt is written for this: it asks for a panorama "designed for a final 15:7
    /// crop" and for the important content to stay in the central horizontal band, so what the crop
    /// removes is sky and ground the scene was told it could lose.
    /// </summary>
    private byte[] NormalizeToSpread(CompositeBookContext context, int page, byte[] rawPng)
    {
        var before = Image.Identify(rawPng);

        var normalized = SpreadArtCrop.CropToRatio(
            rawPng, (float)CompositeDeterministicChecks.TargetAspect);

        var after = Image.Identify(normalized);

        logger.LogInformation(
            "Composite pipeline {JobId} spread {Page}: normalized {BeforeW}x{BeforeH} to "
            + "{AfterW}x{AfterH} for the 15:7 spread before compositing.",
            context.JobId, page, before.Width, before.Height, after.Width, after.Height);

        var problems = CompositeDeterministicChecks.NormalizedSpreadProblems(normalized);
        if (problems.Count > 0)
        {
            // A crop that did not land on the sheet's shape is not something to composite onto and
            // then discover at layout time. It cannot normally happen — the crop is arithmetic —
            // which is precisely why it is worth saying out loud when it does.
            throw new CompositePipelineException(
                CompositeFailureCodes.ImageGenerationFailed,
                $"The base image for spread {page} could not be normalized to the printed spread: "
                + string.Join(" ", problems))
            {
                Page = page
            };
        }

        return normalized;
    }

    /// <summary>
    /// Paste the approved pose, then check the receipt it wrote.
    ///
    /// The deterministic post-checks read the manifest rather than the pixels, which is the whole
    /// design: the engine records where Beki went and what she hashed to, so "is Beki fully inside
    /// the canvas" is arithmetic somebody can repeat months later without the pipeline.
    /// </summary>
    private BekiCompositeResult Composite(
        CompositeBookContext context,
        VisualScenarioSpread page,
        byte[] basePng,
        string poseId,
        string textSide)
    {
        var side = BekiCompositeConfig.ParseTextSide(textSide);

        BekiCompositeResult result;
        try
        {
            result = _engine.Value.CompositeStorySpread(
                basePng,
                $"spread-{page.Page:00}-base.png",
                poseId,
                side,
                $"spread-{page.Page:00}.png");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CompositePipelineException(
                CompositeFailureCodes.ImageGenerationFailed,
                $"Compositing Beki onto spread {page.Page} failed.", ex)
            {
                Page = page.Page
            };
        }

        var manifest = result.Manifest;
        var layer = manifest.BekiLayer;

        // The per-composite record the observability contract asks for, in one entry. Split across
        // several and the one that matters is always the one that scrolled away.
        logger.LogInformation(
            "Composite {JobId} spread {Page}: pose {PoseId} ({PoseFile}, sha256 {PoseSha}) "
            + "alphaBox={BoxX},{BoxY},{BoxW}x{BoxH} rendered={RenderW}x{RenderH} "
            + "placement={PlaceX},{PlaceY} anchor={AnchorX},{AnchorY},{AnchorH} opacity={Opacity} "
            + "resampler={Resampler} mirrored={Mirrored} rotated={Rotated} warped={Warped} "
            + "redrawn={Redrawn} output={OutputSha}",
            context.JobId, page.Page, layer.PoseId, layer.File, layer.Sha256,
            layer.SourceAlphaBbox.XPx, layer.SourceAlphaBbox.YPx,
            layer.SourceAlphaBbox.WidthPx, layer.SourceAlphaBbox.HeightPx,
            layer.RenderedSizePx.WidthPx, layer.RenderedSizePx.HeightPx,
            layer.PlacementPx.XPx, layer.PlacementPx.YPx,
            layer.NormalizedAnchor.VisibleCenterX, layer.NormalizedAnchor.VisibleCenterY,
            layer.NormalizedAnchor.VisibleHeight, layer.Opacity, manifest.Resampler,
            layer.Mirrored, layer.Rotated, layer.Warped, layer.Redrawn, manifest.Output.Sha256);

        var problems = CompositeDeterministicChecks.CompositeProblems(
            manifest, _engine.Value.Registry, side);

        if (problems.Count > 0)
        {
            throw new CompositePipelineException(
                CompositeFailureCodes.ImageGenerationFailed,
                $"The composite for spread {page.Page} failed its deterministic checks: "
                + string.Join(" ", problems))
            {
                Page = page.Page
            };
        }

        return result;
    }

    /// <summary>
    /// The multimodal review, with the contract's one parse retry.
    ///
    /// The retry re-asks rather than re-generating, which is the distinction the contract draws:
    /// an answer that will not parse says nothing about the picture, and paying for another
    /// picture to get a better sentence is the wrong bill. A second unparseable answer is
    /// <see cref="CompositeFailureCodes.ImageQaFailed"/> — there is no verdict, and shipping a page
    /// because the reviewer was incoherent is the same as not reviewing it.
    /// </summary>
    /// <returns>The verdict, and the wall clock the whole review cost including a parse retry.</returns>
    private async Task<(CompositeQaVerdict Verdict, long ReviewMs)> ReviewAsync(
        CompositeBookContext context,
        VisualScenarioSpread page,
        VisualScenarioV2 scenario,
        BekiCompositeResult composite,
        string textSide,
        byte[] childPhoto,
        string childPhotoContentType,
        CompositeThemeReference theme,
        IReadOnlyList<string> elements,
        CancellationToken cancellationToken)
    {
        var prompt = CompositeMinimalQa.Prompt(
            page.ChildWorldScene!,
            page.BekiAction!,
            scenario.VisualLock!.ChildOutfit!,
            elements,
            textSide);

        // The child's photograph, and only the child's photograph. The reviewer is told to use it
        // for identity and age; sending the theme reference too would invite it to grade the world
        // against a picture the scene was never meant to reproduce, and sending a Beki reference
        // would invite exactly the identity judgement the hash already settles.
        var references = new List<(byte[] Bytes, string ContentType, string Label)>
        {
            (childPhoto, childPhotoContentType, "Original child photograph"),
        };

        CompositeQaParseResult? previous = null;

        // Across both attempts, because the attempt record measures what reviewing this page cost
        // and a parse retry is part of that cost even though it bought no new picture.
        var reviewClock = Stopwatch.StartNew();

        for (var attempt = 0; attempt <= 1; attempt++)
        {
            var ask = attempt == 0
                ? prompt
                : prompt
                  + "\n\nThe previous answer could not be read: "
                  + previous!.Summary
                  + " Return only the JSON object described above.";

            var started = Stopwatch.StartNew();
            string answer;

            try
            {
                answer = await openAi.ReviewIllustrationAsync(
                    composite.Png, ask, references, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                started.Stop();
                LogModelCall(
                    context, "visual_qa", _options.VisualReviewerModel, CompositeMinimalQa.Version,
                    started.ElapsedMilliseconds, attempt, "failed", page.Page);

                throw new CompositePipelineException(
                    CompositeFailureCodes.ImageQaFailed,
                    $"The minimal visual QA call for spread {page.Page} failed.", ex)
                {
                    Page = page.Page
                };
            }

            started.Stop();

            var parsed = CompositeMinimalQa.Parse(answer);

            LogModelCall(
                context, "visual_qa", _options.VisualReviewerModel, CompositeMinimalQa.Version,
                started.ElapsedMilliseconds, attempt,
                parsed.IsValid ? parsed.Verdict!.ToString() : parsed.Summary, page.Page);

            if (parsed.IsValid)
            {
                reviewClock.Stop();
                return (parsed.Verdict!, reviewClock.ElapsedMilliseconds);
            }

            previous = parsed;

            logger.LogWarning(
                "Composite pipeline {JobId} spread {Page}: the QA answer did not parse — {Problems}",
                context.JobId, page.Page, parsed.Summary);
        }

        throw new CompositePipelineException(
            CompositeFailureCodes.ImageQaFailed,
            $"Spread {page.Page} has no readable QA verdict after two attempts and is marked for "
            + $"human review: {previous!.Summary}")
        {
            Page = page.Page
        };
    }

    /// <summary>
    /// The images the generation call carries, and the one it must never carry.
    ///
    /// Two, or three when a recurring element has been drawn before: the child's photograph as the
    /// identity reference, the approved world reference, and optionally the last accepted base that
    /// contained the element this page reuses. No Beki, in any position, under any label — the
    /// config says <c>send_beki_reference: false</c>, and a list built anywhere else is a list this
    /// rule could be broken in.
    /// </summary>
    private static StoryImageReference? References(
        byte[] childPhoto, string childPhotoContentType, CompositeThemeReference theme, byte[]? continuityImage)
    {
        var references = new List<(byte[] Bytes, string ContentType, string Label)>
        {
            (childPhoto, childPhotoContentType, "Child identity reference"),
            (theme.Bytes, "image/png", $"Approved {theme.OfficialName} world reference"),
        };

        if (continuityImage is { Length: > 0 })
        {
            references.Add((continuityImage, "image/png", "Continuity reference"));
        }

        return BekiImageReferences.ToStoryImageReference(references);
    }

    private static CompositeSpreadResult AdoptedSpread(int page, byte[] image) => new()
    {
        Page = page,
        BasePng = [],
        CompositePng = image,
        // The manifest is not rebuilt for an adopted page. It was written, checked and stored the
        // run that drew it, and a fresh one built from bytes we did not composite would be a
        // receipt for work this run did not do.
        Manifest = AdoptedManifest,
        Prompt = string.Empty,
        PoseId = string.Empty,
        TextSide = CompositeSpreadRhythm.TextSideFor(page),
        Verdict = "Adopted from a previous attempt's accepted artwork.",
        BaseAttempts = 0,
        Adopted = true,
    };

    private static readonly BekiCompositionManifest AdoptedManifest = new()
    {
        Canvas = new BekiCompositionSize { WidthPx = 0, HeightPx = 0 },
        BaseImage = new BekiCompositionFile { File = string.Empty, Sha256 = string.Empty },
        BekiLayer = new BekiCompositionLayer
        {
            PoseId = string.Empty,
            File = string.Empty,
            Sha256 = string.Empty,
            SourceAlphaBbox = new BekiCompositionRect { XPx = 0, YPx = 0, WidthPx = 0, HeightPx = 0 },
            RenderedSizePx = new BekiCompositionSize { WidthPx = 0, HeightPx = 0 },
            PlacementPx = new BekiCompositionPoint { XPx = 0, YPx = 0 },
            NormalizedAnchor = new BekiCompositionAnchor
            {
                VisibleCenterX = 0, VisibleCenterY = 0, VisibleHeight = 0
            },
        },
        Output = new BekiCompositionFile { File = string.Empty, Sha256 = string.Empty },
    };

    /// <summary>
    /// One line per AI call, in the fields §8 asks for: the job, the stage, the model, the prompt
    /// version, the latency, the retry count and what validation made of the answer.
    ///
    /// What is not in it is as deliberate as what is: no API key, no signed URL, no photograph
    /// bytes, no child's name. A log line is the artifact most likely to be pasted into a chat
    /// window, and everything in this one is safe to paste.
    ///
    /// For the story and scenario stages the model is the id this pipeline actually passed. For the
    /// image and QA stages it is the configured slot instead, because those two go through the
    /// provider router, which picks the vendor and the model from its own options and logs the id it
    /// used; recording a guess here as though it were the real one would be worse than recording
    /// what was configured.
    /// </summary>
    private void LogModelCall(
        CompositeBookContext context,
        string stage,
        string model,
        string promptVersion,
        long latencyMs,
        int retryCount,
        string validation,
        int? page = null) =>
        logger.LogInformation(
            "Composite AI call {JobId}: stage={Stage} page={Page} model={Model} "
            + "promptVersion={PromptVersion} latencyMs={LatencyMs} retry={Retry} validation={Validation}",
            context.JobId, stage, page?.ToString() ?? "-", model, promptVersion, latencyMs,
            retryCount, validation);

    /// <summary>
    /// The continuity reference mechanism, reused rather than rebuilt (handoff §6 Step 4: "do not
    /// build a new extraction service for v0").
    ///
    /// It remembers the most recent accepted BASE image each recurring element appeared in — the
    /// base, never the composite, because the composite has Beki in it and the continuity
    /// instruction tells the model to copy only the named elements from that picture. Handing it a
    /// page with Beki on it is handing it a picture of Beki.
    /// </summary>
    private sealed class CompositeContinuity
    {
        private readonly Dictionary<string, byte[]> _byElement = new(StringComparer.Ordinal);

        /// <summary>
        /// The reference for this page: the last accepted base containing any of the elements this
        /// page reuses, and the names it may be read for.
        ///
        /// One image, not several. The image call takes a list of references and the model weights
        /// the first most heavily; two continuity pictures is how a spread came back with the same
        /// creature drawn twice on the legacy path, and this pipeline's answer is to attach the one
        /// picture and name what may be taken from it.
        /// </summary>
        public (byte[] Image, IReadOnlyList<string> ElementNames)? For(IReadOnlyList<string> elements)
        {
            foreach (var element in elements)
            {
                if (_byElement.TryGetValue(element, out var image))
                {
                    return (image, [element]);
                }
            }

            return null;
        }

        /// <summary>
        /// Records the most recent accepted appearance, replacing whatever was there.
        ///
        /// It used to keep the first and never look again, which is the wrong end of the book. The
        /// contract asks for "the most recent approved image containing a recurring story character
        /// or object", and the reason is drift: each spread is drawn from the one before it, so by
        /// spread seven the creature has moved a little from where spread two left it, and matching
        /// spread seven against spread two asks the model to undo six pages of accumulated change in
        /// one step. Matching it against spread six asks for one page's worth.
        /// </summary>
        public void Remember(IReadOnlyList<string> elements, byte[] basePng)
        {
            if (basePng is not { Length: > 0 })
            {
                return;
            }

            foreach (var element in elements)
            {
                _byElement[element] = basePng;
            }
        }
    }
}
