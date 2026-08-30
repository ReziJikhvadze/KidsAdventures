using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
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

    /// <summary>
    /// The picture the reviewer refused and the record of why, for the failures where "marked for
    /// human review" is the outcome.
    ///
    /// It rides on the exception because the exception is what reaches the fulfilment job, and the
    /// fulfilment job is the only part of this system that can write a blob. Until it does, a book
    /// that stopped at spread seven left a pack directory containing spreads one to six and nothing
    /// else: no picture to look at, no verdict to read, and a support answer that can only repeat
    /// the failure code back. The reviewable thing was generated, paid for, judged and discarded.
    ///
    /// Null on every failure that has no page to show — a refused input, a scenario that would not
    /// validate, an identity spec that could not be read.
    /// </summary>
    public CompositeFailureEvidence? Evidence { get; init; }
}

/// <summary>
/// What a page that stopped for human review leaves behind: the composite as the reviewer saw it,
/// and a short document saying what each attempt was and what was said about it.
/// </summary>
/// <param name="Page">The spread number, for the blob names the fulfilment job builds.</param>
/// <param name="CompositePng">
/// The last composite the reviewer refused — the whole page, Beki included, exactly as judged.
/// </param>
/// <param name="QaJson">
/// The attempt record: every verdict in order, what was generated or moved between them, and the
/// prompt versions in force. It is written for a person opening two files in a blob browser, so it
/// is indented and it names things the way the logs do.
/// </param>
public sealed record CompositeFailureEvidence(int Page, byte[] CompositePng, string QaJson);

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

    /// <summary>
    /// Whether an earlier attempt at this pack already redrew and reviewed the cover.
    ///
    /// The fulfilment job knows this from its own manifest and the illustrator cannot: the cover
    /// redraw is an improvement a book gets once, and a resumed attempt that drew some of the
    /// spreads must not buy it a second time — nor overwrite the reviewed picture with a fresh one
    /// that would have to be reviewed again from scratch.
    /// </summary>
    public bool CoverAlreadyRedrawn { get; init; }

    /// <summary>
    /// Where to persist the derived child identity spec, called before the first image call and
    /// for the same reason the scenario's callback is.
    ///
    /// It rides on the context rather than being written here because the spec is private data
    /// about a real child: it belongs in the pack's own storage, beside the photograph it was read
    /// from, and this pipeline has no storage dependency on purpose.
    /// </summary>
    public Func<string, Task>? OnIdentitySpec { get; init; }
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
public sealed record CompositeAttempt(long GenerationMs, long ReviewMs, string Verdict, bool Accepted)
{
    /// <summary>
    /// Where Beki stood for this cycle.
    ///
    /// Carried because the retry that moves her is only auditable if the rows say where she was
    /// each time. A page refused twice for FOLD_SAFETY reads as a pipeline that tried nothing
    /// unless the two rows show two different anchors — which is exactly the failure this record
    /// gained the field for.
    /// </summary>
    public BekiCompositeAnchor? Anchor { get; init; }
}

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

    /// <summary>
    /// The reviewer's advisory remark that this page's composition contradicts the shot it was asked
    /// for, or null — which is the usual answer.
    ///
    /// It rides on the accepted page rather than only on the attempt rows because it is a note about
    /// the picture that actually shipped. Nothing branches on it anywhere: <see cref="Verdict"/> is
    /// what the retry ladder read, and this was never part of that.
    /// </summary>
    public string? ShotNote { get; init; }
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

    /// <summary>
    /// The book-level quality record: pose fallbacks, Georgian flags, shot advisories — see
    /// <see cref="CompositeBookReview"/>.
    ///
    /// An artifact rather than a log line only, because every one of these is a thing somebody has
    /// to go and read *after* a book ships, and scrollback is not where that happens. Null on a
    /// path that produced no review, which is none of them today.
    /// </summary>
    public string? ReviewJson { get; init; }

    /// <summary>
    /// The same record, typed, for the caller that projects a few numbers out of it rather than
    /// storing the document whole.
    ///
    /// Both, rather than one and a parse. The fulfilment job stores <see cref="ReviewJson"/>
    /// byte-for-byte as the pack's own artifact, and separately puts the counts — and only the
    /// counts — into its telemetry; re-parsing a string it was just handed, to read two integers
    /// out of it, is the kind of seam that eventually disagrees with itself.
    /// </summary>
    public CompositeBookReview? Review { get; init; }
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
    /// <summary>
    /// The child identity spec that attempt derived, as it was stored.
    ///
    /// Adopting it matters for the same reason adopting the scenario does. The four attributes are
    /// written into every image prompt, so a resumed run that derived a second spec — from the same
    /// photograph, by the same model, and quite possibly with "wavy" where the first said "curly" —
    /// would redraw its missing spreads to a different description of the same child than the ones
    /// it is adopting. Every page would still pass its own review.
    ///
    /// Null, unreadable, or written by a different derivation prompt version all mean the same
    /// thing: derive a new one. See <see cref="CompositeChildIdentity.TryReadStored"/>.
    /// </summary>
    public string? IdentitySpecJson { get; init; }

    /// <summary>
    /// The base image of the accepted first spread — the child appearance anchor every later
    /// spread is drawn and reviewed against.
    ///
    /// It is not a second copy of anything: it is <see cref="BaseImages"/>' entry for spread one,
    /// named separately because its job is different. As a continuity reference a base teaches a
    /// later page about a creature; as the anchor it teaches every later page what this child looks
    /// like once drawn. A resumed run that adopted spread one but cannot produce this has no anchor,
    /// and the honest answer there is to redraw spread one rather than to draw seven pages of a
    /// child nothing pins down.
    /// </summary>
    public byte[]? AnchorBasePng { get; init; }

    /// <summary>
    /// The book-level review an earlier attempt stored, as it was written.
    ///
    /// Adopted for two fields only, and see <see cref="CompositeBookReview.MergedWith"/> for why
    /// those two: a shot advisory belongs to the attempt that reviewed the page, and an adopted page
    /// was reviewed by somebody else. Without it, a resume that adopts seven pages writes a review
    /// saying the book has no shot trouble, and the fulfilment job overwrites the earlier attempt's
    /// document with that — silence where there were observations, on pages nobody will look at
    /// again.
    ///
    /// Null is normal and harmless: a first attempt, a book that failed before the review existed,
    /// or a document that could no longer be read. It means there is nothing to complete this
    /// attempt's own reading with.
    /// </summary>
    public string? ReviewJson { get; init; }

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
    /// Called with the derived identity spec before the first image call, and awaited — the
    /// scenario callback's timing, for the scenario callback's reason.
    /// </summary>
    public Func<string, Task>? OnIdentitySpec { get; init; }

    /// <summary>
    /// Called once per finished page, in page order, one at a time — the same contract the legacy
    /// generator's callback has, and for the same reason: a parent is watching a spinner for
    /// several minutes.
    ///
    /// "In page order, one at a time" is a promise this pipeline keeps on the callback's behalf and
    /// not a description of how the pages are drawn. Spreads two to eight are drawn concurrently;
    /// the callback still sees two, then three, then four, with the previous call finished before
    /// the next begins. That is not politeness — the fulfilment job's callback mutates a dictionary
    /// and a counter and rewrites one manifest blob, none of which survives being run twice at
    /// once, and a manifest written out of order describes a book with holes in it.
    ///
    /// What it no longer promises is that nothing else is happening: page five may be generating
    /// while page two's picture is being uploaded.
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

    /// <summary>
    /// The eight attributes this book's child was drawn to, for the one picture this pipeline does
    /// not draw.
    ///
    /// The cover is the picture a parent judges the book by and the one the owner watched lose the
    /// eye colour "almost always". It is composed by the legacy upright-cover builder, which knows
    /// nothing about any of this — so the spec comes out here rather than staying private, and the
    /// caller that owns cover composition writes it into the lock.
    /// </summary>
    public required ChildIdentitySpec Identity { get; init; }

    /// <summary>
    /// The accepted first spread's base: the same picture spreads 2-8 were matched against, handed
    /// out so the cover can be matched against it too.
    ///
    /// Present on a fully-adopted resume as well, restored from what the earlier attempt stored —
    /// which is why it is not on its own a licence to redraw anything. See
    /// <see cref="SpreadsDrawnThisRun"/>.
    /// </summary>
    public byte[]? Anchor { get; init; }

    /// <summary>
    /// How many spreads this particular run actually drew, as opposed to adopted.
    ///
    /// It exists because <see cref="Anchor"/> could not answer the question the cover needs asked.
    /// A resume that adopts all eight pages still hands back an anchor — the stored one — and a
    /// caller reading only that would conclude a fresh book had just been drawn, redraw the cover
    /// against it, and upload a second cover over the reviewed one an earlier attempt had already
    /// stored. Zero here means this run changed no artwork, so there is nothing for a cover to be
    /// brought back into agreement with.
    /// </summary>
    public int SpreadsDrawnThisRun { get; init; }

    /// <summary>
    /// What this book is worth flagging for a person, none of which failed it: how often the pose
    /// table fell back, what the Georgian check-list found in the printed copy, and which pages the
    /// reviewer thought were shot wrongly.
    /// </summary>
    public required CompositeBookReview Review { get; init; }

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
/// Nothing is retried more than the contract allows. One Visual Scenario retry, one identity
/// derivation retry, one base regeneration, one re-composite, one QA parse retry, and then the book
/// stops with a code. The legacy pipeline learned this the expensive way — a refused spread redrawn
/// twice changed no outcome and doubled the bill — and the counts here are the supplier's own
/// numbers from <c>pipeline_config_v1.json</c>.
///
/// A retry that cannot change anything is not one of them. The re-composite used to hand the
/// reviewer the identical picture, which cost a book its seventh spread and taught the rule the
/// ladder in <see cref="DrawSpreadAsync"/> now follows: every rung must produce a different page,
/// and a rung with nothing left to change is skipped rather than spent.
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

    /// <summary>
    /// The page whose accepted base becomes the child appearance anchor for the rest of the book.
    ///
    /// One rather than "whichever page is drawn first", and named here rather than assumed in two
    /// files. The scenario is validated as exactly eight spreads numbered 1 to 8 in order, so the
    /// first page of the book is the first page drawn on a fresh run; the constant exists so that
    /// the fulfilment job — which has to hand a resumed run the right stored base image — is
    /// reading the same answer this pipeline is, rather than its own copy of the number 1.
    /// </summary>
    public const int AnchorSpreadNumber = 1;

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

        /*
          The deterministic Georgian read, on the copy that will actually be printed.

          Here rather than after the pictures because this is where the prose is settled: the plan is
          either the one the parent previewed or the one this run just wrote, and nothing downstream
          edits a Georgian word. Flags, never repairs — see CompositeGeorgianCheck for why the fix
          belongs to the polish pass and why a silent correction would hide the miss.

          It cannot fail a book. A misspelling is a thing to tell somebody about, not a reason to
          refuse an order that is otherwise a finished book.
        */
        var georgianFlags = CompositeGeorgianCheck.Inspect(plan);

        /*
          A check-list that could not be fully loaded is reported, not swallowed and not fatal.

          "No flags" and "no rules ran" look identical on a finished book, and only one of them
          means the Georgian was read. So a broken rule lands in the log and on the book's own
          record, while the rules that did compile still run — an advisory check may not be the
          reason a paid book fails, least of all over its own configuration.
        */
        foreach (var problem in CompositeGeorgianCheck.RuleProblems)
        {
            logger.LogWarning(
                "Composite pipeline {JobId}: georgian_checklist_problem — {Problem} This book was "
                + "checked by the remaining rules only.", context.JobId, problem);

            warnings.Add(
                $"Georgian check-list ({CompositeGeorgianCheck.ChecklistVersion}): {problem} This "
                + "book was checked by the remaining rules only.");
        }

        foreach (var flag in georgianFlags)
        {
            logger.LogWarning(
                "Composite pipeline {JobId}: georgian_text_flag rule={Rule} location={Location} "
                + "found=\"{Found}\" expected={Expected}. Flagged for human review; nothing was "
                + "rewritten.",
                context.JobId, flag.RuleId, flag.Location, flag.Found, flag.Expected);

            warnings.Add(
                $"Georgian check-list ({CompositeGeorgianCheck.ChecklistVersion}): {flag}. This book "
                + "is flagged for human reading; no text was changed.");
        }

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

        var planned = adoptedScenario is { } already
            ? (already.Scenario, already.Json, PoseAudit: (CompositePoseAudit?)null, RetrySpent: false)
            : await PlanVisualScenarioAsync(context, input, theme, boundary, cancellationToken);

        var (scenario, scenarioJson) = (planned.Scenario, planned.Json);

        /*
          An adopted scenario is audited too, and it is worth saying why rather than skipping it.

          The pose count is a fact about the book that ships, not about the call that planned it. A
          resumed run adopts the scenario an earlier attempt wrote — possibly under the previous
          keyword table, possibly before the verb steering existed — and the finished book still has
          however many neutral hovers in it. What the resumed run does NOT do is spend a retry: the
          scenario is not being asked for again, the pages are already drawn against it, and
          replanning it is the one thing the whole resume path exists to prevent.
        */
        var poseAudit = planned.PoseAudit ?? CompositePoseVocabulary.Audit(_engine.Value.Registry, scenario);

        // Persisted before the first image call, and awaited, so that the attempt which dies on
        // spread three is not the attempt that never wrote down what it was drawing.
        if (request.OnScenario is not null)
        {
            await request.OnScenario(scenarioJson);
        }

        // ---- Step 2b: the child identity spec --------------------------------------------------
        //
        // Once per book, before any picture is bought, and required. See DeriveIdentityAsync.
        var storedIdentity = CompositeChildIdentity.TryReadStored(resume.IdentitySpecJson);

        /*
          Stored artwork with no spec to go with it is adopted as nothing at all.

          The four attributes are written into every image prompt, so pages drawn under one spec and
          pages drawn under another are pages of two different children — and a run that adopted the
          first while deriving the second would produce exactly that, from the same photograph, with
          every page passing its own review. There is no reading of a missing spec that makes the
          two halves match: the derivation is a model call over a photograph, so a second one is a
          second opinion, not a recovery of the first.

          A spec is missing here for one of three reasons and they all end the same way: an earlier
          attempt stored none, the blob is gone, or it was written by a derivation prompt this
          deployment no longer uses. So the artwork goes and the book is redrawn under one spec,
          which is the same answer a prompt-version change already gets from the resume contract.

          The scenario is untouched. Nothing is wrong with it — the outfit and the recurring elements
          it fixes are still the ones this book was sold as — and eight pages redrawn against it is
          a whole book rather than two halves.
        */
        if (resume.Spreads.Count > 0 && storedIdentity is null)
        {
            logger.LogWarning(
                "Composite pipeline {JobId}: {Stored} stored spread(s) have no usable child "
                + "identity spec, so the pages this attempt redrew could not be drawn to the same "
                + "child as the pages it adopted. Redrawing the whole book.",
                context.JobId, resume.Spreads.Count);

            warnings.Add(
                $"{resume.Spreads.Count} spread(s) from an earlier attempt were discarded: this "
                + "book's child identity spec is missing or was derived by a different prompt "
                + "version, and finishing the book without it would draw two different children "
                + "into one book.");

            resume = resume with
            {
                Spreads = new Dictionary<int, byte[]>(),
                BaseImages = new Dictionary<int, byte[]>(),
                // The anchor goes with them: it is the first page of the discarded book, and
                // matching seven fresh spreads to it would put the drift back one level down.
                AnchorBasePng = null,
            };
        }

        var identity = storedIdentity
                       ?? await DeriveIdentityAsync(context, request, childPhoto, cancellationToken);

        if (storedIdentity is not null)
        {
            logger.LogInformation(
                "Composite pipeline {JobId}: identity_spec_adopted promptVersion={PromptVersion}; "
                + "no new identity call.",
                context.JobId, CompositeChildIdentity.Version);
        }

        // ---- Steps 3-7: the anchor spread, then the rest ---------------------------------------
        var visualLock = scenario.VisualLock!;
        var continuity = new CompositeContinuity();
        var pages = scenario.Spreads!;

        // Validated as exactly eight, numbered 1 to 8 in order, so the first entry is spread one —
        // the page that produces the anchor and therefore the page that cannot be drawn beside any
        // other.
        const int anchorPage = AnchorSpreadNumber;

        var adopted = new Dictionary<int, CompositeSpreadResult>();
        var toDraw = new List<VisualScenarioSpread>(pages.Count);

        /*
          The anchor an earlier attempt left behind, decided before a single page is adopted.

          Named explicitly by the caller, or simply spread one's stored base — the same bytes
          either way. The named field exists so the fulfilment job can say which image is the
          anchor rather than leaving this class to know the page number twice; the fallback keeps
          a caller that supplies only the bases correct.
        */
        var anchor = resume.AnchorBasePng is { Length: > 0 } named
            ? named
            : resume.BaseImages.GetValueOrDefault(anchorPage);

        /*
          Stored artwork with no anchor to go with it is adopted as nothing at all.

          Redrawing only the missing anchor was the wrong repair, and it was wrong in the way this
          whole amendment exists to prevent. The stored pages were drawn against an anchor this
          attempt cannot see; a fresh spread one is a fresh stylization of the same child; so the
          pages this run redraws would be matched to the new one and the pages it adopts would keep
          the old — one book, two children, every page passing its own review. The same holds when
          spread one itself is missing while later pages are stored.

          So the whole book is redrawn under one anchor. It costs eight images, which is the price
          of the only outcome that is a book rather than two halves of two books; and it is the
          same answer a prompt-version change already gets from the resume contract.
        */
        if (resume.Spreads.Count > 0 && anchor is not { Length: > 0 })
        {
            logger.LogWarning(
                "Composite pipeline {JobId}: {Stored} stored spread(s) have no child appearance "
                + "anchor — spread {AnchorPage}'s base image is missing — so they were drawn "
                + "against a stylization this attempt cannot match. Redrawing the whole book.",
                context.JobId, resume.Spreads.Count, anchorPage);

            warnings.Add(
                $"{resume.Spreads.Count} spread(s) from an earlier attempt were discarded: spread "
                + $"{anchorPage}'s base image, which is the child appearance anchor for the whole "
                + "book, is missing, and finishing the book without it would mix two stylizations "
                + "of the same child.");

            resume = resume with
            {
                Spreads = new Dictionary<int, byte[]>(),
                BaseImages = new Dictionary<int, byte[]>(),
            };
        }

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!resume.Spreads.TryGetValue(page.Page, out var alreadyDrawn)
                || alreadyDrawn.Length == 0)
            {
                toDraw.Add(page);
                continue;
            }

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

            adopted[page.Page] = AdoptedSpread(page.Page, alreadyDrawn);

            if (page.Page == anchorPage)
            {
                logger.LogInformation(
                    "Composite pipeline {JobId}: adopting spread {Page}'s stored base as the child "
                    + "appearance anchor for the rest of the book.", context.JobId, page.Page);
            }
        }

        var (drawn, bookAnchor) = await DrawSpreadsAsync(
            context, input, theme, scenario, toDraw, anchorPage, anchor, identity, continuity,
            childPhoto, childPhotoContentType, request.OnSpread, cancellationToken);

        var spreads = pages
            .Select(page => adopted.TryGetValue(page.Page, out var already)
                ? already
                : drawn[page.Page])
            .ToList();

        // Collected here rather than as each page lands, because the pages no longer land in order:
        // gathering them from the finished book keeps the warning list the same list it always was.
        foreach (var spread in spreads.Where(spread => spread.PoseFallback))
        {
            warnings.Add(
                $"Spread {spread.Page}: no pose keyword matched the scenario's Beki action, so "
                + "the neutral hover was composited.");
        }

        /*
          The book-level record: the three findings that are true of a book rather than of a page.

          Built from the scenario's audit rather than from the drawn pages, deliberately. A resumed
          run adopts pages it did not draw and knows nothing about how their poses were chosen; the
          scenario is what every page — adopted or drawn — was composited from, so replaying it is
          the only count that describes the finished book. The shot advisories come from the pages,
          because only a page that was actually reviewed has one.
        */
        var review = new CompositeBookReview
        {
            PoseRegistryVersion = _engine.Value.Registry.RegistryVersion,
            PoseKeywordRevision = _engine.Value.Registry.KeywordRevision,
            ScenarioPromptVersion = CompositeVisualScenarioPrompt.Version,
            PoseSelectionFallbacks = poseAudit.FallbackCount,
            PoseFallbackPages = poseAudit.FallbackPages,
            DistinctPoses = poseAudit.DistinctPoses,
            PoseVocabularyRetrySpent = planned.RetrySpent,
            PoseFallbackBudgetExceeded = poseAudit.ExceedsFallbackBudget,
            GeorgianFlags = georgianFlags,
            GeorgianChecklistVersion = CompositeGeorgianCheck.ChecklistVersion,
            GeorgianChecklistProblems = CompositeGeorgianCheck.RuleProblems,
            ShotAdvisories = spreads
                .Where(spread => spread.ShotNote is { Length: > 0 })
                .Select(spread => new CompositeShotAdvisory(
                    spread.Page, CompositeSpreadRhythm.ShotFor(spread.Page), spread.ShotNote!))
                .ToList(),
        }
            /*
              …completed with what an earlier attempt observed about the pages this one adopted.

              An adopted page was reviewed by the run that drew it, and its shot advisory lives in
              that run's review and nowhere else. Rebuilding from what this attempt can see would
              write a book with no shot trouble in it and hand that to the fulfilment job, which
              overwrites the stored document — losing observations about pages nobody is going to
              look at again. The retry flag rides along for the same reason: a resumed run adopts
              the scenario and never re-asks, so it cannot know the retry was spent.
            */
            .MergedWith(
                CompositeBookReview.TryRead(resume.ReviewJson),
                adopted.Keys.ToHashSet());

        // One line, whole, in the same key=value idiom as every other observability line here. The
        // fallback count is the number R13 exists to drive down and the one to watch across books.
        logger.LogInformation(
            "Composite book review {JobId}: {Summary} registry={Registry} keywords={Keywords} "
            + "scenarioPrompt={ScenarioPrompt} needsHumanReading={NeedsReading}",
            context.JobId, review.Summary, review.PoseRegistryVersion, review.PoseKeywordRevision,
            review.ScenarioPromptVersion, review.NeedsHumanReading);

        return new CompositeBookResult
        {
            Plan = plan,
            Boundary = boundary,
            Scenario = scenario,
            Spreads = spreads,
            Identity = identity,
            Anchor = bookAnchor,
            SpreadsDrawnThisRun = drawn.Count,
            Review = review,
            Warnings = warnings,
            Artifacts = new CompositeBookArtifacts
            {
                ScenarioJson = scenarioJson,
                ReviewJson = review.ToJson(),
                Review = review,
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
            References(childPhoto, childPhotoContentType, theme, childAnchor: null, continuityImage: null),
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
    ///
    /// v2.1 adds one more thing a scenario can be rejected for, and it is deliberately inside the
    /// same budget rather than beside it. After both validation layers pass, the pose registry is
    /// replayed over the eight Beki sentences — no model call, the same selector the pages use — and
    /// a book that would be composited from more than
    /// <see cref="CompositePoseVocabulary.MaxFallbacksPerBook"/> neutral hovers is treated as a
    /// semantic miss and spends the one retry it already had. It never buys a second one, and it
    /// never fails a book: a scenario that is still repetitive after its retry is drawn anyway, and
    /// the count is recorded. The fallback is an approved pose; six of them in eight pages is a
    /// quality signal, not a defect worth discarding a paid plan over.
    /// </summary>
    private async Task<(VisualScenarioV2 Scenario, string Json, CompositePoseAudit? PoseAudit, bool RetrySpent)>
        PlanVisualScenarioAsync(
        CompositeBookContext context,
        NormalizedBookInput input,
        CompositeThemeReference theme,
        StoryBoundaryOutput boundary,
        CancellationToken cancellationToken)
    {
        var inputJson = CompositeVisualScenarioPrompt.InputJson(input, theme, boundary);
        var model = VisualScenarioModel;

        VisualScenarioValidationResult? previous = null;

        // Whether the one retry was spent on the pose vocabulary specifically, rather than on a
        // schema or semantic fault. The record wants to distinguish them: "the planner was asked
        // again about its verbs" and "the planner returned invalid JSON" are different stories about
        // the same retry.
        var poseRetrySpent = false;

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
                    // The contract's instruction plus v2.1's verb-family block — see
                    // CompositeVisualScenarioPrompt.SystemInstruction for why they are two members.
                    CompositeVisualScenarioPrompt.SystemInstruction,
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

            /*
              The pose audit, run only on an answer that already passed both validation layers.

              Deterministic and free: it replays the registry over the eight Beki sentences with the
              selector the pages themselves use. Doing it here rather than in the validator keeps the
              two documents apart — the validator is the supplied schema plus the contract MD's own
              rules, and this is a fact about a different file with its own revisions.
            */
            CompositePoseAudit? audit = null;

            if (validation.IsValid)
            {
                audit = CompositePoseVocabulary.Audit(_engine.Value.Registry, validation.Scenario!);

                // Only on the first attempt. The retry is one retry; a second rejection here would
                // be the pipeline paying twice to be told the same thing, which is the exact rule
                // the whole retry budget exists to hold.
                if (audit.ExceedsFallbackBudget && attempt == 0)
                {
                    logger.LogWarning(
                        "Composite pipeline {JobId}: the Visual Scenario is valid but maps "
                        + "{Fallbacks} of {Spreads} spreads to the fallback pose (pages {Pages}); "
                        + "spending the one corrective retry on the Beki action vocabulary.",
                        context.JobId, audit.FallbackCount, audit.Choices.Count,
                        string.Join(", ", audit.FallbackPages));

                    validation = validation with
                    {
                        IsValid = false,
                        Problems = [CompositePoseVocabulary.Problem(audit)],
                    };

                    poseRetrySpent = true;
                    audit = null;
                }
            }

            LogModelCall(
                context, "visual_scenario", model, CompositeVisualScenarioPrompt.Version,
                started.ElapsedMilliseconds, attempt,
                validation.IsValid ? "accepted" : validation.Summary);

            if (validation.IsValid)
            {
                if (audit!.ExceedsFallbackBudget)
                {
                    // The retry has been spent and the second answer is still repetitive. The book
                    // is drawn: the fallback is an approved pose, and refusing a paid plan over
                    // Beki's variety would trade a delivered book for a better one nobody gets.
                    logger.LogWarning(
                        "Composite pipeline {JobId}: the Visual Scenario still maps "
                        + "{Fallbacks} of {Spreads} spreads to the fallback pose after its one "
                        + "retry (pages {Pages}). Drawing the book and recording the count.",
                        context.JobId, audit.FallbackCount, audit.Choices.Count,
                        string.Join(", ", audit.FallbackPages));
                }

                return (validation.Scenario!, answer!, audit, poseRetrySpent);
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
    // Step 2b — the child identity spec
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The four attributes every page of this book draws the child to, read once from the
    /// photograph.
    ///
    /// Required, and terminal when it cannot be had. That is the amendment's whole point. Before
    /// it, identity rode on the attached photograph alone, which means the model interpreted the
    /// same picture afresh nine times; the completed book that proved it wrong had a visibly
    /// different child on every spread and had passed all eight of its own reviews. So there is no
    /// soft-degrade here: two unusable answers stop the book with
    /// <see cref="CompositeFailureCodes.IdentitySpecFailed"/>, before the first image is paid for.
    ///
    /// Called only when there is no spec to adopt — see the caller, which discards a resumed run's
    /// artwork rather than let a second derivation describe the same child differently from the
    /// pages it would keep. The parent's eye colour is applied here, once, for the same reason: an
    /// adopted spec is adopted exactly as stored.
    ///
    /// Nothing in here logs an attribute, or a digest of one. The event and the prompt version are
    /// the whole record; the values live in the pack's private storage with the photograph they
    /// were read from.
    /// </summary>
    /// <param name="childPhoto">
    /// The photograph itself, and no content type beside it: the reviewer door takes bytes, and
    /// the normalizer behind it decodes by sniffing rather than by what it is told — a JPEG and a
    /// PNG reach the model as the same normalized picture either way.
    /// </param>
    private async Task<ChildIdentitySpec> DeriveIdentityAsync(
        CompositeBookContext context,
        CompositeBookRequest request,
        byte[] childPhoto,
        CancellationToken cancellationToken)
    {
        var model = _options.VisualReviewerModel;
        ChildIdentityParseResult? previous = null;

        for (var attempt = 0; attempt <= 1; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ask = attempt == 0
                ? CompositeChildIdentity.Prompt
                : CompositeChildIdentity.RetryPrompt(previous!.Problems);

            var started = Stopwatch.StartNew();
            string answer;

            try
            {
                /*
                  The existing multimodal reviewer door, not a new one.

                  It is already exactly this call — images plus an instruction, in, one text answer
                  out, validated against a schema by the caller — and on the Gemini route it is
                  already the vision model the handoff names for this work. A second method on the
                  illustration client would have been the same request with a different name on it,
                  and one more surface for the router, the OpenAI implementation and every test
                  double to keep in step.

                  The photograph goes in the "under review" position because that is the picture
                  being read; the prompt says in its first line what the image is.
                */
                answer = await openAi.ReviewIllustrationAsync(
                    childPhoto, ask, [], cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                started.Stop();
                LogModelCall(
                    context, "identity_spec", model, CompositeChildIdentity.Version,
                    started.ElapsedMilliseconds, attempt, "failed");

                // A transport failure and an unreadable answer are the same thing here: no spec.
                // Both are worth the one retry, and the second of either stops the book.
                previous = new ChildIdentityParseResult(
                    false, null, ["the identity call did not complete."]);

                continue;
            }

            started.Stop();

            var parsed = CompositeChildIdentity.Parse(answer);

            // parsed.Summary is value-free by construction — see ChildIdentityParseResult — which
            // is what makes it safe to put in the validation field of a log line.
            LogModelCall(
                context, "identity_spec", model, CompositeChildIdentity.Version,
                started.ElapsedMilliseconds, attempt, parsed.IsValid ? "accepted" : parsed.Summary);

            if (!parsed.IsValid)
            {
                previous = parsed;

                logger.LogWarning(
                    "Composite pipeline {JobId}: identity spec attempt {Attempt} rejected — {Problems}",
                    context.JobId, attempt + 1, parsed.Summary);

                continue;
            }

            var spec = CompositeChildIdentity.WithParentEyeColor(
                parsed.Spec!, context.Input.LegacyEyeColor);

            // The event and the version, and one boolean that is about the order form rather than
            // about the child: whether a parent-supplied eye colour replaced the derived one. No
            // attribute value and no digest of one — see CompositeChildIdentity for why a hash of
            // four low-entropy attributes salted with a job id that is logged beside it is the
            // attributes with extra steps.
            logger.LogInformation(
                "Composite pipeline {JobId}: identity_spec_derived promptVersion={PromptVersion} "
                + "parentEyeColour={ParentSupplied}.",
                context.JobId, CompositeChildIdentity.Version,
                !string.IsNullOrWhiteSpace(context.Input.LegacyEyeColor));

            // The request's own callback when a caller set one, and the context's otherwise. The
            // fallback is what lets the fulfilment job persist the spec without the illustrator in
            // between having to learn about it: that class builds this request from the context and
            // copies the callbacks it knows about, and this campaign did not touch it.
            var persist = request.OnIdentitySpec ?? context.OnIdentitySpec;

            if (persist is not null)
            {
                await persist(CompositeChildIdentity.ToStoredJson(spec));
            }

            return spec;
        }

        throw new CompositePipelineException(
            CompositeFailureCodes.IdentitySpecFailed,
            "The child's identity attributes could not be read from the photograph in two "
            + "attempts, and this book may not be drawn without them: " + previous!.Summary);
    }

    // -----------------------------------------------------------------------------------------
    // Steps 3-7 — the pages
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Draws every page this run owes, spread one alone and the rest together.
    ///
    /// Spread one is alone because it is the anchor: it is the picture the other seven are told to
    /// match the child against, so nothing else can start until it has been drawn and accepted.
    /// After that the remaining pages have no dependency on each other that a shared continuity
    /// reference does not already satisfy, and drawing them one at a time cost the first real book
    /// 651 seconds of mostly waiting.
    ///
    /// Three rules make the concurrency safe rather than merely fast.
    ///
    /// Delivery stays serialized and in spread order. The callback belongs to the fulfilment job,
    /// where it mutates a dictionary of stored URLs, advances a progress counter and rewrites one
    /// manifest blob; two of those at once corrupts the manifest and the third reports a book as
    /// further along than it is. <see cref="OrderedSpreadDelivery"/> is the whole of that promise.
    ///
    /// The first terminal failure stops the rest. A book fails as a book — one page's
    /// IMAGE_QA_FAILED is the run's failure — so the sibling token is cancelled the moment one page
    /// gives up, and the pages still in flight stop before their next paid call rather than
    /// finishing pictures for a book that is already over.
    ///
    /// The continuity reference is whatever was accepted when a page was scheduled. It is usually
    /// spread one, which is the accepted trade: a creature introduced mid-book may now be matched
    /// against the page that introduced it rather than against the page immediately before, and QA
    /// still checks it.
    /// </summary>
    private async Task<(IReadOnlyDictionary<int, CompositeSpreadResult> Drawn, byte[]? Anchor)>
        DrawSpreadsAsync(
        CompositeBookContext context,
        NormalizedBookInput input,
        CompositeThemeReference theme,
        VisualScenarioV2 scenario,
        IReadOnlyList<VisualScenarioSpread> toDraw,
        int anchorPage,
        byte[]? anchor,
        ChildIdentitySpec identity,
        CompositeContinuity continuity,
        byte[] childPhoto,
        string childPhotoContentType,
        Func<CompositeSpreadResult, Task>? onSpread,
        CancellationToken cancellationToken)
    {
        var drawn = new ConcurrentDictionary<int, CompositeSpreadResult>();

        if (toDraw.Count == 0)
        {
            return (drawn, anchor);
        }

        var delivery = new OrderedSpreadDelivery(
            toDraw.Select(page => page.Page).ToList(), onSpread);

        var remaining = toDraw;

        // The anchor page, when this run is the one drawing it: alone, first, and awaited.
        if (anchor is not { Length: > 0 })
        {
            var first = toDraw[0];

            if (first.Page != anchorPage)
            {
                // Unreachable: a page is either adopted with its base — which is what gives this
                // run an anchor — or it is in toDraw, and toDraw is in page order. Stated anyway,
                // because the alternative to a loud contradiction is seven spreads silently drawn
                // with no anchor at all.
                throw new CompositePipelineException(
                    CompositeFailureCodes.ImageGenerationFailed,
                    $"Spread {anchorPage} is neither adopted with its base image nor the first "
                    + "page to be drawn, so this run has no child appearance anchor.");
            }

            var anchorSpread = await DrawSpreadAsync(
                context, input, theme, scenario, first, continuity, childPhoto,
                childPhotoContentType, identity, anchor: null, cancellationToken);

            drawn[anchorSpread.Page] = anchorSpread;

            // The accepted base, which on a page that spent its one regeneration is the regenerated
            // picture and never the refused draft — DrawSpreadAsync only returns what QA passed.
            anchor = anchorSpread.BasePng;

            logger.LogInformation(
                "Composite pipeline {JobId}: spread {Page} accepted; its base is the child "
                + "appearance anchor for the remaining {Count} spread(s).",
                context.JobId, anchorSpread.Page, toDraw.Count - 1);

            await delivery.DeliverAsync(anchorSpread, cancellationToken);

            remaining = toDraw.Skip(1).ToList();
        }

        if (remaining.Count == 0)
        {
            return (drawn, anchor);
        }

        var concurrency = Math.Max(1, _options.SpreadConcurrency);
        var anchorImage = anchor!;

        logger.LogInformation(
            "Composite pipeline {JobId}: drawing {Count} spread(s) with at most {Concurrency} at "
            + "once.", context.JobId, remaining.Count, concurrency);

        // Linked, so the caller's own cancellation still stops everything, and cancellable by us,
        // so the first terminal failure stops everything too.
        using var siblings = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var slots = new SemaphoreSlim(concurrency, concurrency);

        Exception? terminal = null;

        var tasks = remaining.Select(DrawOneAsync).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception)
        {
            /*
              What the run failed of is the first page that gave up, not whichever exception
              Task.WhenAll happens to surface — and once the siblings are cancelled, most of what
              it surfaces is cancellation. So the real failure is captured at the moment it
              happens and rethrown here with its stack intact.
            */
            if (terminal is not null)
            {
                ExceptionDispatchInfo.Capture(terminal).Throw();
            }

            // No terminal failure means the caller cancelled us, which is not this run's to
            // reinterpret.
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        return (drawn, anchor);

        async Task DrawOneAsync(VisualScenarioSpread page)
        {
            await slots.WaitAsync(siblings.Token).ConfigureAwait(false);

            CompositeSpreadResult spread;

            try
            {
                spread = await DrawSpreadAsync(
                    context, input, theme, scenario, page, continuity, childPhoto,
                    childPhotoContentType, identity, anchorImage, siblings.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Fail(ex);
                throw;
            }
            finally
            {
                slots.Release();
            }

            drawn[spread.Page] = spread;

            try
            {
                // Outside the slot on purpose: the callback uploads a picture and rewrites a
                // manifest, and holding a generation slot through somebody else's network calls
                // would make the concurrency limit a limit on uploads.
                await delivery.DeliverAsync(spread, siblings.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Fail(ex);
                throw;
            }
        }

        void Fail(Exception ex)
        {
            // First writer wins: the page that actually stopped the book is the one reported.
            Interlocked.CompareExchange(ref terminal, ex, null);

            logger.LogError(
                ex, "Composite pipeline {JobId}: a spread failed terminally; cancelling the "
                + "spreads still in flight.", context.JobId);

            siblings.Cancel();
        }
    }

    private async Task<CompositeSpreadResult> DrawSpreadAsync(
        CompositeBookContext context,
        NormalizedBookInput input,
        CompositeThemeReference theme,
        VisualScenarioV2 scenario,
        VisualScenarioSpread page,
        CompositeContinuity continuity,
        byte[] childPhoto,
        string childPhotoContentType,
        ChildIdentitySpec identity,
        byte[]? anchor,
        CancellationToken cancellationToken)
    {
        var textSide = CompositeSpreadRhythm.TextSideFor(page.Page);
        var visualLock = scenario.VisualLock!;

        var elements = CompositeIllustrationPrompt.RelevantRecurringElements(
            visualLock.RecurringElements, page.ChildWorldScene);

        // Read once, when this page is scheduled: the most recently accepted base carrying one of
        // this page's recurring elements. With the pages drawn concurrently that is usually spread
        // one rather than the page immediately before, which is the trade the parallel campaign
        // accepted and QA's CAST_ERROR still checks.
        var reference = continuity.For(elements);

        var anchored = anchor is { Length: > 0 };

        var prompt = CompositeIllustrationPrompt.ForSpread(new CompositeSpreadPromptInput
        {
            Page = page.Page,
            ChildAge = input.ChildAge,
            Theme = theme,
            ChildWorldScene = page.ChildWorldScene!,
            ChildOutfit = visualLock.ChildOutfit!,
            RecurringElements = elements,
            ContinuityElementNames = reference?.ElementNames ?? [],
            IdentitySpec = identity,
            AnchorAttached = anchored,
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
            References(childPhoto, childPhotoContentType, theme, anchor, reference?.Image),
            cancellationToken);

        var basePng = NormalizeToSpread(context, page.Page, rawPng);

        var baseAttempts = 1;
        var recomposited = false;
        var regenerated = false;

        // Where Beki stands. Null means the deterministic default for this text side, which is what
        // every page starts from and what a regenerated base goes back to: the anchors are the
        // numbers the partners approved against a printed proof, and the adjusted one below is a
        // repair for a particular picture rather than a new default.
        BekiCompositeAnchor? placement = null;

        // One row per generate-and-review cycle, kept whether it passed or not. A page that shipped
        // on its second attempt is a page whose first verdict is the only record of what was wrong,
        // and that verdict is what the fulfilment job's telemetry is read for.
        var attempts = new List<CompositeAttempt>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var composite = Composite(context, page, basePng, selection.PoseId, textSide, placement);

            var (verdict, reviewMs) = await ReviewAsync(
                context, page, scenario, composite, textSide, childPhoto, childPhotoContentType,
                theme, elements, anchor, identity, cancellationToken);

            attempts.Add(new CompositeAttempt(
                generationMs, reviewMs, verdict.ToString(), verdict.Passed)
            {
                Anchor = composite.Manifest.BekiLayer.NormalizedAnchor is { } placed
                    ? new BekiCompositeAnchor(
                        placed.VisibleCenterX, placed.VisibleCenterY, placed.VisibleHeight)
                    : null,
            });

            if (verdict.Passed)
            {
                // Only an accepted page becomes a continuity reference. An image the reviewer
                // refused is precisely the one a later spread must not be told to match.
                continuity.Remember(elements, basePng);

                if (verdict.ShotNote is { Length: > 0 } shotNote)
                {
                    // Logged as a warning and carried out, and that is the whole of its effect. The
                    // page passed; nothing below this line reads it. A hard gate on a subjective
                    // single-frame judgement would spend a paid image call on every false positive,
                    // and there is no evidence yet to price that — which is what this collects.
                    logger.LogWarning(
                        "Composite pipeline {JobId} spread {Page}: shot_note (advisory, no effect "
                        + "on the verdict) — asked for \"{Shot}\", reviewer says \"{Note}\".",
                        context.JobId, page.Page, CompositeSpreadRhythm.ShotFor(page.Page), shotNote);
                }

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
                    ShotNote = verdict.ShotNote,
                };
            }

            /*
              The ladder a refused page climbs, and it stops at three rungs.

              Which rung applies is the reviewer's own recommended_action, because it is the only
              reader that can tell a badly generated world from a well-generated one with Beki in
              the wrong part of it — and the two cost very differently: one is another paid image
              call, the other is arithmetic.
            */

            // Rung one: the world is wrong, and there is a second picture in the budget.
            if (verdict.RecommendedAction == CompositeQaVerdict.ActionRegenerateBase && !regenerated)
            {
                (basePng, generationMs, placement) = await RegenerateBaseAsync(
                    context, page, prompt, verdict, childPhoto, childPhotoContentType, theme,
                    anchor, reference?.Image, cancellationToken);

                regenerated = true;
                baseAttempts++;

                continue;
            }

            /*
              Rung two: the world is usable and Beki landed badly, so Beki moves.

              This rung used to do nothing at all, and a real book died on it. A spread refused for
              FOLD_SAFETY was re-composited from the same bytes, with the same pose, at the same
              configured anchor, by arithmetic that is deterministic by design — so the "second
              attempt" produced the first attempt's exact image, and the reviewer refused it again
              in the same words. The pack stopped at spread seven having bought two reviews of one
              picture, and the retry rule had been, in effect, a way of paying to fail twice.

              What it does now is what §14 asked for: adjust the deterministic anchor. Beki steps
              away from the middle of the sheet and is drawn slightly smaller, which is what
              FOLD_SAFETY and BEKI_INTEGRATION are complaining about; the step is bounded and
              clamped to the rectangle the deterministic checks already enforce. She is not
              redrawn, mirrored, rotated, warped or recoloured — none of those exist as code, and
              an anchor is three numbers.
            */
            if (verdict.RecommendedAction == CompositeQaVerdict.ActionRecompositeBeki && !recomposited)
            {
                recomposited = true;

                var layer = composite.Manifest.BekiLayer;

                var adjusted = new BekiCompositeAnchor(
                        layer.NormalizedAnchor.VisibleCenterX,
                        layer.NormalizedAnchor.VisibleCenterY,
                        layer.NormalizedAnchor.VisibleHeight)
                    .NudgedAwayFromCentre(
                        BekiCompositeConfig.ParseTextSide(textSide),
                        composite.Manifest.Canvas.WidthPx,
                        layer.RenderedSizePx.WidthPx);

                if (adjusted is not null)
                {
                    logger.LogWarning(
                        "Composite pipeline {JobId} spread {Page}: QA asked for a re-composite; "
                        + "moving Beki from {FromX},{FromY},{FromH} to {ToX},{ToY},{ToH} — {Verdict}",
                        context.JobId, page.Page,
                        layer.NormalizedAnchor.VisibleCenterX, layer.NormalizedAnchor.VisibleCenterY,
                        layer.NormalizedAnchor.VisibleHeight,
                        adjusted.VisibleCenterX, adjusted.VisibleCenterY, adjusted.VisibleHeight,
                        verdict);

                    placement = adjusted;

                    // Nothing was generated for this cycle, and the row says so: a zero here is the
                    // difference between "the second attempt was free" and "the second attempt was
                    // another image bill", which is the question the retry rules exist to answer.
                    generationMs = 0;

                    continue;
                }

                // Nowhere to move her to — she is wide enough that the canvas and the reserved
                // third leave no window. Falling through rather than re-compositing is the whole
                // lesson of the defect: an identical picture reviewed a second time is a paid call
                // whose answer is already known.
                logger.LogWarning(
                    "Composite pipeline {JobId} spread {Page}: QA asked for a re-composite, but "
                    + "Beki cannot be moved within the canvas and the reserved text third; not "
                    + "re-reviewing an identical picture.", context.JobId, page.Page);
            }

            /*
              Rung three: moving her did not fix it, and the base budget is still untouched.

              Reached only after a re-composite has been tried, which is what keeps the ladder
              bounded and keeps it honest — a first verdict of human_review still stops the book,
              because "the failure source is ambiguous" is not a thing another picture answers. But
              a placement the reviewer refused twice is evidence about the picture rather than about
              the placement: there was nowhere on that base to put Beki. The base is what changes.

              She goes back to the approved anchor for the new picture. The nudge was a repair for
              the old one, and carrying it forward would draw every later attempt further out for a
              reason that no longer exists.
            */
            if (recomposited && !regenerated)
            {
                (basePng, generationMs, placement) = await RegenerateBaseAsync(
                    context, page, prompt, verdict, childPhoto, childPhotoContentType, theme,
                    anchor, reference?.Image, cancellationToken);

                regenerated = true;
                baseAttempts++;

                continue;
            }

            logger.LogError(
                "Composite pipeline {JobId} spread {Page}: stopping for human review after "
                + "{BaseAttempts} base image(s) and {Reviews} review(s) — {Verdict}",
                context.JobId, page.Page, baseAttempts, attempts.Count, verdict);

            throw new CompositePipelineException(
                CompositeFailureCodes.ImageQaFailed,
                $"Spread {page.Page} failed the minimal visual QA and is marked for human review: {verdict}")
            {
                Page = page.Page,
                // The picture and the paperwork, so that "marked for human review" leaves a human
                // something to review.
                Evidence = new CompositeFailureEvidence(
                    page.Page,
                    composite.Png,
                    FailureEvidenceJson(page.Page, selection.PoseId, textSide, baseAttempts, attempts)),
            };
        }
    }

    /// <summary>
    /// The second picture, and the reset that goes with it.
    ///
    /// Extracted because two rungs of the ladder spend the same budget for different reasons — the
    /// reviewer asked for a new world, or moving Beki on the old one did not work — and the one
    /// thing that must be identical either way is what happens to the placement: it goes back to
    /// the approved anchor, because the nudge belonged to the picture being thrown away.
    /// </summary>
    private async Task<(byte[] BasePng, long GenerationMs, BekiCompositeAnchor? Placement)>
        RegenerateBaseAsync(
            CompositeBookContext context,
            VisualScenarioSpread page,
            string prompt,
            CompositeQaVerdict verdict,
            byte[] childPhoto,
            string childPhotoContentType,
            CompositeThemeReference theme,
            byte[]? anchor,
            byte[]? continuityImage,
            CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Composite pipeline {JobId} spread {Page}: buying a new base image — {Verdict}",
            context.JobId, page.Page, verdict);

        var (rawPng, generationMs) = await GenerateBaseImageAsync(
            context, page.Page, prompt,
            References(childPhoto, childPhotoContentType, theme, anchor, continuityImage),
            cancellationToken);

        return (NormalizeToSpread(context, page.Page, rawPng), generationMs, null);
    }

    /// <summary>
    /// The document that goes into the pack beside the refused picture.
    ///
    /// Written for the person who opens two files in a blob browser and has to decide what happened:
    /// every cycle in order, what each one cost, where Beki stood for it, and what the reviewer
    /// said. The anchors are the point of including the rows at all — a page refused twice for the
    /// same category reads as a pipeline that tried nothing until the two rows show two different
    /// placements.
    ///
    /// Nothing about the child is in it. The scene, the outfit and the identity attributes stay out:
    /// this is a record of a placement decision, and the picture beside it is the thing to look at.
    /// </summary>
    private static string FailureEvidenceJson(
        int page,
        string poseId,
        string textSide,
        int baseAttempts,
        IReadOnlyList<CompositeAttempt> attempts) =>
        JsonSerializer.Serialize(
            new
            {
                page,
                failure_code = CompositeFailureCodes.ImageQaFailed,
                image_prompt_version = CompositeIllustrationPrompt.Version,
                qa_prompt_version = CompositeMinimalQa.Version,
                pose_id = poseId,
                text_side = textSide,
                base_attempts = baseAttempts,
                review_attempts = attempts.Count,
                attempts = attempts.Select((attempt, index) => new
                {
                    attempt = index + 1,
                    generation_ms = attempt.GenerationMs,
                    review_ms = attempt.ReviewMs,
                    accepted = attempt.Accepted,
                    verdict = attempt.Verdict,
                    beki_anchor = attempt.Anchor is null
                        ? null
                        : new
                        {
                            visible_center_x = attempt.Anchor.VisibleCenterX,
                            visible_center_y = attempt.Anchor.VisibleCenterY,
                            visible_height = attempt.Anchor.VisibleHeight,
                        },
                }).ToList(),
            },
            CompositeJson.Readable);

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
        // Checked here rather than only by the provider, and that is what makes fail-fast real: a
        // sibling spread that has already given up cancels this token, and the promise is that no
        // further image is PAID FOR after a book has terminally failed — not that a request is sent
        // and abandoned.
        cancellationToken.ThrowIfCancellationRequested();

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

        normalized = RepairSeam(context, page, normalized);

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
    /// The centre-column gate: measure every picture before anything else sees it, and paint out a
    /// seam when one is there.
    ///
    /// Here rather than at the end because everything downstream inherits the bytes — the reviewer
    /// judges them, Beki is composited onto them, the anchor is one of them, and the printer gets
    /// one of them. A repair applied after any of that would be a different picture from the one
    /// that was approved.
    ///
    /// Deliberately silent when there is nothing to do, which is the common case: the v1.1 prompt
    /// amendment removed the cause, and this catches the faint residue that survived it.
    /// </summary>
    /// <param name="page">Null for the cover, which is measured by the same gate.</param>
    private byte[] RepairSeam(CompositeBookContext context, int? page, byte[] png)
    {
        var (repaired, before, after) = CompositeSeamRepair.Gate(png);

        if (!before.Exceeded)
        {
            return png;
        }

        logger.LogWarning(
            "Composite pipeline {JobId} {Page}: a centre seam measured {BeforeRatio:F1}x the "
            + "picture's baseline column change ({BeforeCentre:F2} against {Baseline:F2}); "
            + "interpolated {Columns} column(s) from {First} to {Last}, now {AfterRatio:F1}x.",
            context.JobId, page is null ? "cover" : $"spread {page}",
            before.Ratio, before.Centre, before.Baseline,
            before.ColumnCount, before.FirstColumn, before.LastColumn, after.Ratio);

        return repaired;
    }

    /// <summary>
    /// Paste the approved pose, then check the receipt it wrote.
    ///
    /// The deterministic post-checks read the manifest rather than the pixels, which is the whole
    /// design: the engine records where Beki went and what she hashed to, so "is Beki fully inside
    /// the canvas" is arithmetic somebody can repeat months later without the pipeline.
    /// </summary>
    /// <param name="placement">
    /// Null for the deterministic anchor this text side's config gives, which is what every first
    /// attempt uses; an adjusted anchor only on the one permitted placement retry. The engine has
    /// taken an override since it was written — §14 anticipated exactly this — so nothing about the
    /// composite itself changes here except the three numbers it is told to place her at.
    /// </param>
    private BekiCompositeResult Composite(
        CompositeBookContext context,
        VisualScenarioSpread page,
        byte[] basePng,
        string poseId,
        string textSide,
        BekiCompositeAnchor? placement = null)
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
                $"spread-{page.Page:00}.png",
                placement);
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
        byte[]? anchor,
        ChildIdentitySpec identity,
        CancellationToken cancellationToken)
    {
        var anchored = anchor is { Length: > 0 };

        var prompt = CompositeMinimalQa.Prompt(
            page.ChildWorldScene!,
            page.BekiAction!,
            scenario.VisualLock!.ChildOutfit!,
            elements,
            textSide,
            anchored,
            identity,
            // v1.3: the same sentence the image prompt opened its composition block with, so the
            // reviewer's advisory shot_note is a comparison rather than a description.
            CompositeSpreadRhythm.ShotFor(page.Page));

        /*
          The child's photograph, and — after spread one — the child appearance anchor. Nothing else.

          The photograph answers "is this the same child"; the anchor answers "is this the same
          child as the rest of this book", which is the question the drifting book's eight PASSes
          were never asked. Both are about the child. The theme reference is still absent, because
          it would invite the reviewer to grade the world against a picture the scene was never
          meant to reproduce, and a Beki reference is still absent, because a SHA-256 settles that.
        */
        var references = new List<(byte[] Bytes, string ContentType, string Label)>
        {
            (childPhoto, childPhotoContentType, "Original child photograph"),
        };

        if (anchored)
        {
            references.Add((anchor!, "image/png", "Child appearance anchor (accepted spread 1)"));
        }

        CompositeQaParseResult? previous = null;

        // Across both attempts, because the attempt record measures what reviewing this page cost
        // and a parse retry is part of that cost even though it bought no new picture.
        var reviewClock = Stopwatch.StartNew();

        for (var attempt = 0; attempt <= 1; attempt++)
        {
            // A sibling spread that has terminally failed has already ended this book; reviewing
            // a page for it is one more paid call for nothing.
            cancellationToken.ThrowIfCancellationRequested();

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
    /// The images the generation call carries, in the order the prompt numbers them, and the one it
    /// must never carry.
    ///
    /// Two on the first spread — the child's photograph as the identity reference and the approved
    /// world reference — then the child appearance anchor on every page after it, then the last
    /// accepted base containing this page's recurring element when there is one. Four at most,
    /// which is the template's own limit.
    ///
    /// The order is not cosmetic. The prompt calls them Image 1 to Image 4 by position, so a list
    /// assembled in a different order tells the model to take the child's stylization from a
    /// picture of a creature. It is also the weighting: the first reference is the one the image
    /// model leans on hardest, and that is the photograph, deliberately, on every page.
    ///
    /// No Beki, in any position, under any label — the config says <c>send_beki_reference: false</c>,
    /// and a list built anywhere else is a list this rule could be broken in. Note what the anchor
    /// is for the same reason: the accepted BASE of spread one, which is the page before Beki was
    /// pasted onto it.
    /// </summary>
    private static StoryImageReference? References(
        byte[] childPhoto,
        string childPhotoContentType,
        CompositeThemeReference theme,
        byte[]? childAnchor,
        byte[]? continuityImage)
    {
        var references = new List<(byte[] Bytes, string ContentType, string Label)>();

        // The anchor first, from v1.2, on every page that has one. The first reference is the one
        // the image model leans on hardest, and until now that was a photograph of a real child —
        // so every spread re-stylized it from scratch and the book's own answer, sitting third in
        // the list, was a hint. The picture that already shows the drawn child now leads.
        if (childAnchor is { Length: > 0 })
        {
            references.Add((childAnchor, "image/png", "Child appearance anchor"));
        }

        // And the photograph directly behind it, never dropped: the anchor is one stylization, and
        // a stylization is answerable to the child it was made from.
        references.Add((childPhoto, childPhotoContentType, "Child identity reference"));
        references.Add((theme.Bytes, "image/png", $"Approved {theme.OfficialName} world reference"));

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
        /// Read while one page is being scheduled, written when another is accepted, and those two
        /// now happen at the same time. A plain dictionary torn by a concurrent write is not a
        /// wrong reference — it is a corrupted dictionary, on the path that decides what nine paid
        /// image calls are shown.
        /// </summary>
        private readonly object _gate = new();

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
            lock (_gate)
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

            lock (_gate)
            {
                foreach (var element in elements)
                {
                    _byElement[element] = basePng;
                }
            }
        }
    }

    /// <summary>
    /// The promise the fulfilment callback is written against: one at a time, in spread order,
    /// however the pages actually finish.
    ///
    /// It exists because the callback is not a notification. On the other side of it a job uploads
    /// a picture, stores a composition receipt, advances a percentage and rewrites the one manifest
    /// blob that decides what a resumed run may adopt — reading and rewriting a dictionary it also
    /// mutates. Two of those at once is a manifest missing whichever page lost the race, which is
    /// then a page redrawn by the next attempt and paid for twice; out of order, it is a book that
    /// reports page five as done while page three is still being drawn.
    ///
    /// Making the callback thread-safe was the alternative, and it is the wrong place for the fix:
    /// the fulfilment job's callback is written once and read by people reasoning about a book, and
    /// "pages arrive in order, one at a time" is a sentence they can hold. The concurrency this
    /// campaign added belongs to the pipeline that added it.
    ///
    /// Pages that were adopted from an earlier attempt are not in the order at all — they were
    /// delivered by the run that drew them, and their pictures are already stored.
    /// </summary>
    private sealed class OrderedSpreadDelivery(
        IReadOnlyList<int> order, Func<CompositeSpreadResult, Task>? callback)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<int, CompositeSpreadResult> _ready = [];

        /// <summary>How far down <c>order</c> the callback has been taken.</summary>
        private int _next;

        /// <summary>
        /// Set when the callback itself throws, and read before anything else is handed over.
        ///
        /// The sibling cancellation that follows such a failure is a moment behind it — the
        /// callback throws, this gate is released, and only then does the catch upstream cancel —
        /// which is long enough for the next page, already queued here, to be delivered into a
        /// storage layer that has just failed. The latch closes that window without depending on
        /// how quickly cancellation arrives. Only ever touched inside the gate.
        /// </summary>
        private bool _abandoned;

        /// <summary>
        /// Hands over one finished page, and with it every page that was only waiting for this one.
        ///
        /// A spread that finishes early simply waits: page four completing before page three
        /// records itself and returns, and page three's own call delivers three and then four. So
        /// the callback runs on whichever thread completed the page that unblocked it, one call at
        /// a time, in the book's order.
        /// </summary>
        public async Task DeliverAsync(CompositeSpreadResult spread, CancellationToken cancellationToken)
        {
            if (callback is null)
            {
                return;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (_abandoned)
                {
                    return;
                }

                _ready[spread.Page] = spread;

                while (_next < order.Count && _ready.TryGetValue(order[_next], out var due))
                {
                    // A book that has already failed terminally does not go on being delivered:
                    // the pages after the failure will never exist, and the manifest should not
                    // claim otherwise.
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        await callback(due).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        _abandoned = true;
                        throw;
                    }

                    _next++;
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
