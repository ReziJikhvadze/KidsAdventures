using System.Diagnostics;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story.Composite;
using Hangfire;

namespace AdventurePacks.Api.Services.Story;

public interface IBekiPackFulfillment
{
    /// <summary>
    /// Hangfire entry point: draws the book, lays it out, and completes the pack.
    ///
    /// <see cref="DisableConcurrentExecutionAttribute"/> is declared here rather than only on the
    /// implementation because jobs enqueued via <c>Enqueue&lt;IBekiPackFulfillment&gt;</c> carry
    /// the interface's <c>MethodInfo</c>, and Hangfire's filter provider reads attributes off
    /// exactly the method and type recorded on the job — the implementation's own copy of the
    /// attribute is for a reader looking at that file, not for Hangfire. One pack, one worker at a
    /// time: a book that costs nine images must never be drawn twice because a second worker
    /// picked up the same job while the first was still running.
    ///
    /// The lock is keyed by the pack id ({0} is formatted from the first job argument), not by
    /// the method alone. A method-keyed lock would serialize every Beki book in the system behind
    /// whichever one is currently drawing — many minutes each — and unrelated paid orders would
    /// queue up against the timeout for no reason.
    ///
    /// Sixty seconds, not the 1800 it was. The timeout is how long a duplicate waits for the lock,
    /// and a duplicate of a job that is genuinely running should not wait at all — the book is
    /// already being drawn, and the second worker's only job is to give up so its thread can do
    /// something else. Half an hour of a blocked worker per duplicate is how a queue of paid
    /// orders stops moving behind one book, over an SSH tunnel where duplicates are not rare.
    /// </summary>
    [DisableConcurrentExecution("beki-pack:{0}", 60)]
    Task ProcessAsync(Guid packId, Guid runId, CancellationToken cancellationToken);
}

/// <summary>
/// The one place a Beki pack's blob names are written down. The fulfilment job uploads under
/// these names and the making-of endpoint probes them; a name assembled anywhere else is a name
/// the two can disagree about.
/// </summary>
public static class BekiPackBlobs
{
    public static string SpreadName(Guid userId, Guid packId, int spreadNumber) =>
        $"{userId}/{packId}/spread-{spreadNumber:00}.png";

    public static string FailedSpreadName(Guid userId, Guid packId, int spreadNumber) =>
        $"{userId}/{packId}/spread-{spreadNumber:00}-failed.png";

    public static string SpreadQaName(Guid userId, Guid packId, int spreadNumber) =>
        $"{userId}/{packId}/spread-{spreadNumber:00}-qa.json";

    /// <summary>
    /// Where the record of a policy waiver goes: the numbers and the verdicts behind a refusal this
    /// deployment decided to ship anyway.
    ///
    /// A name of its own rather than the spread's QA record, and the distinction is the point.
    /// <see cref="SpreadQaName"/> holds what the REVIEWER said, and the whole of amendment B1 is
    /// that nothing may overwrite that with a decision made about it afterwards. This holds what was
    /// decided. Page zero is the cover wrap, as everywhere else in this pack's storage.
    /// </summary>
    public static string PolicyWaiverName(Guid userId, Guid packId, string checkId, int page) =>
        $"{userId}/{packId}/waived-{checkId}-{page:00}.json";

    /// <summary>
    /// The PICTURE a waived check refused, named for the check as well as the page.
    ///
    /// It shared <see cref="FailedSpreadName"/> with the terminal-failure path, and that name knows
    /// only the page. Two waived checks on one spread — a centre fold the measurement disliked and a
    /// reviewer who disliked something else — therefore wrote to one blob, and the later upload
    /// silently replaced the earlier: two alarms, one picture, and no way to tell which alarm the
    /// surviving picture belonged to. (Review finding 5.)
    ///
    /// Page zero is the cover wrap, as everywhere else in this pack's storage.
    /// </summary>
    public static string WaivedEvidenceName(Guid userId, Guid packId, string checkId, int page) =>
        $"{userId}/{packId}/spread-{page:00}-waived-{checkId}.png";

    /// <summary>
    /// Where a resumable job's progress lives between attempts. Read and written by its bare
    /// name, the way <see cref="IBlobStorageService.ExistsAsync"/> and
    /// <see cref="IBlobStorageService.DownloadAsync"/> both address a blob — never by a stored
    /// URL, which is exactly the mistake this whole product avoids: a key built by hand reads on
    /// one storage backend and 404s on the other.
    /// </summary>
    public static string ManifestName(Guid userId, Guid packId) => $"{userId}/{packId}/fulfilment.json";

    /// <summary>
    /// The validated Visual Scenario the composite pipeline planned this book from.
    ///
    /// Stored beside the book's images rather than in a separate audit store, because it is one of
    /// the book's artifacts: it is what says why the child is dressed the way she is on all nine
    /// pictures, and the question it answers only ever comes up while somebody is looking at those
    /// pictures.
    /// </summary>
    public static string ScenarioName(Guid userId, Guid packId) =>
        $"{userId}/{packId}/visual-scenario.json";

    /// <summary>
    /// The four identity attributes this book's child is drawn to, read once from the photograph.
    ///
    /// Under the pack's own prefix, beside the photograph and the pictures, because that is the
    /// privacy domain it belongs to: it describes a real child's hair, eyes and skin, and it is
    /// deleted when the pack's other private artifacts are. It is stored at all because a resumed
    /// run must draw its remaining spreads to the same description as the ones it adopts — a second
    /// derivation would give one book two slightly different children.
    /// </summary>
    public static string IdentitySpecName(Guid userId, Guid packId) =>
        $"{userId}/{packId}/child-identity.json";

    /// <summary>
    /// The book-level quality record: how often the pose table fell back, what the Georgian
    /// check-list found in the printed copy, and which pages the reviewer thought were shot wrongly.
    ///
    /// Its own artifact rather than a section of the manifest, for the reason the scenario and the
    /// identity spec are: the manifest is an operational document that gets read, logged and pasted
    /// into support threads, and this one quotes the book's own Georgian — which is where the
    /// child's name lives. The manifest carries the URL; the words stay under the pack's private
    /// prefix, beside the story they came from, and are deleted with it.
    /// </summary>
    public static string CompositeReviewName(Guid userId, Guid packId) =>
        $"{userId}/{packId}/composite-review.json";

    /// <summary>
    /// The AI redraw of the cover, kept only as history.
    ///
    /// It used to be the customer's cover: drawn again after the first spread was accepted, reviewed
    /// against the child's identity spec, and pointed at by the reader. Audit P0-01 ended that — a
    /// press cover composited from the wrap and a customer cover drawn by a model are two designs
    /// for one book, and the supplier rejected the package for exactly that. From the audit-2
    /// correction the composite path never calls the redraw at all; a blob under this name is a
    /// historical preview from a run that predates the correction, and the handback package carries
    /// it under <c>diagnostic/</c> so it can be seen and not mistaken for the master.
    /// </summary>
    public static string CoverName(Guid userId, Guid packId) => $"{userId}/{packId}/cover.png";

    // ----------------------------------------------------------------------------------------
    // The single cover master and its derivations (audit P0-01/P0-02/P0-10, plan D1/D2).
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// The canonical cover: the 512 × 245 mm wrap with the approved Beki already composited on the
    /// front board, as one PNG.
    ///
    /// Audit P0-10's finding was that this file did not exist. The composition receipt declared
    /// <c>cover-wrap-composite.png</c> and its SHA-256, the bytes were used in memory to make the
    /// press cover, and no code ever wrote them down — so the one artifact every cover derivation
    /// claims to come from could not be checked against its own receipt. It is written now, and the
    /// hash is recomputed and compared before anything is derived from it.
    /// </summary>
    public static string CoverWrapCompositeName(Guid userId, Guid packId) =>
        $"{userId}/{packId}-cover-wrap-composite.png";

    /// <summary>The wrap before Beki was composited onto it — the band gate's own evidence.</summary>
    public static string CoverWrapBaseName(Guid userId, Guid packId) =>
        $"{userId}/{packId}-cover-wrap-base.png";

    /// <summary>The wrap's exact-Beki receipt: pose, source hash, anchor, output hash.</summary>
    public static string CoverCompositionName(Guid userId, Guid packId) =>
        $"{userId}/{packId}-cover-composition.json";

    /// <summary>
    /// The reader's cover image: the wrap's front board, cropped, and nothing else.
    ///
    /// The pack's cover column points here, which is what makes the on-screen book, the download and
    /// the printed book one design. It replaces the redraw repoint that audit P0-01 found.
    /// </summary>
    public static string CoverFrontName(Guid userId, Guid packId) =>
        $"{userId}/{packId}/cover-front.png";

    // ----------------------------------------------------------------------------------------
    // Release evidence (audit P0-09, plan D7/D8, amendments A4/A5).
    // ----------------------------------------------------------------------------------------

    /// <summary>Which approved bytes this book was built from, proved before the first model call.</summary>
    public static string AssetLockName(Guid userId, Guid packId) =>
        $"{userId}/{packId}/{BekiAssetLock.ManifestFileName}";

    /// <summary>The sixteen-gate verdict, rewritten whenever anything that feeds it changes.</summary>
    public static string ReleaseGatesName(Guid userId, Guid packId) =>
        $"{userId}/{packId}/release-gates.json";

    /// <summary>A reviewer's signature on a named contact sheet (amendment A2/A5).</summary>
    public static string HumanApprovalName(Guid userId, Guid packId) =>
        $"{userId}/{packId}/human-approval.json";

    /// <summary>
    /// One fixed page's machine QA record: the cover boards, the endpapers, the intro, the credits.
    /// </summary>
    public static string FixedPageQaName(Guid userId, Guid packId, string role) =>
        $"{userId}/{packId}/fixed-{role}-qa.json";

    /// <summary>The three composed documents that carry post-layout receipts.</summary>
    public static readonly IReadOnlyList<string> LayoutModes = ["reading", "interior", "cover"];

    /// <summary>One composed document's whole receipt set (amendment A4).</summary>
    public static string LayoutReceiptName(Guid userId, Guid packId, string mode) =>
        $"{userId}/{packId}/receipts/{mode}-layout.json";

    /// <summary>One page of one composed document, under the composer's own file name.</summary>
    public static string LayoutPageReceiptName(Guid userId, Guid packId, string mode, string fileName) =>
        $"{userId}/{packId}/receipts/{mode}-{fileName}";

    /// <summary>The reading copy, as it is scanned and rendered back for the QR and human gates.</summary>
    public const string DigitalRenderArtifact = "digital-reading";

    public const string InteriorRenderArtifact = "press-interior";

    public const string CoverRenderArtifact = "press-cover";

    /// <summary>Every stored final that render validation is run against (amendment A8).</summary>
    public static readonly IReadOnlyList<string> RenderedArtifacts =
        [InteriorRenderArtifact, CoverRenderArtifact, DigitalRenderArtifact];

    /// <summary>
    /// The stored final one render artifact is the validation OF — the pairing that makes
    /// "every stored final has a releasable report" a question anything can ask.
    ///
    /// Written down once because two places need the same answer and used to each carry their own
    /// copy: the stage that renders the finals back, and the gate that checks whether every stored
    /// final was rendered. The gate could not enumerate the finals at all, which is how a press
    /// cover with no render report of its own passed RENDER_VALIDATION on the strength of the other
    /// two.
    /// </summary>
    public static string FinalPdfName(Guid userId, Guid packId, string artifact) => artifact switch
    {
        InteriorRenderArtifact => InteriorPdfName(userId, packId),
        CoverRenderArtifact => CoverPdfName(userId, packId),
        DigitalRenderArtifact => ReadingPdfName(userId, packId),
        _ => throw new ArgumentOutOfRangeException(
            nameof(artifact), artifact, "not a render-validated artifact."),
    };

    /// <summary>
    /// Which deliverable class an artifact's render evidence belongs to — amendment A5's governance
    /// split, applied per artifact rather than per gate id.
    ///
    /// RENDER_VALIDATION and QR are classed press because they were written for the printer's
    /// files, but they read evidence from the customer's PDF too. Classing the ARTIFACT is what
    /// lets a failure on the reading copy withhold the download it is about, while a press cover's
    /// failure still leaves the parent's book alone.
    /// </summary>
    public static string RenderArtifactClass(string artifact) =>
        artifact == DigitalRenderArtifact
            ? BekiReleaseGates.DigitalClass
            : BekiReleaseGates.PressClass;

    public static string RenderReportName(Guid userId, Guid packId, string artifact) =>
        $"{userId}/{packId}/render-{artifact}.json";

    public static string ContactSheetName(Guid userId, Guid packId, string artifact) =>
        $"{userId}/{packId}/contact-sheet-{artifact}.png";

    /// <summary>The press files and the reports that prove they were prepared rather than exported.</summary>
    public static string InteriorPdfName(Guid userId, Guid packId) => $"{userId}/{packId}-interior.pdf";

    public static string InteriorPreflightName(Guid userId, Guid packId) =>
        $"{userId}/{packId}-interior-preflight.json";

    public static string CoverPdfName(Guid userId, Guid packId) => $"{userId}/{packId}-cover.pdf";

    public static string CoverPreflightName(Guid userId, Guid packId) =>
        $"{userId}/{packId}-cover-preflight.json";

    /// <summary>
    /// Why the press files are not there, when they are not.
    ///
    /// Print preparation refuses rather than degrades, so its success leaves a preflight report and
    /// its failure used to leave a log line. The release gates need the second half: which gate
    /// refused, in a document that outlives the process that wrote it.
    /// </summary>
    public static string PressStatusName(Guid userId, Guid packId) =>
        $"{userId}/{packId}-press-status.json";

    /// <summary>The customer PDF and its own preflight (amendment A10c).</summary>
    public static string ReadingPdfName(Guid userId, Guid packId) => $"{userId}/{packId}.pdf";

    public static string DigitalReportName(Guid userId, Guid packId) =>
        $"{userId}/{packId}-digital-report.json";

    /// <summary>
    /// The normalized Story JSON this book was drawn from.
    ///
    /// Stored with the pack because audit §9 removes it from the handback's excluded list: the
    /// supplier needs the words the pictures were planned from, and "it lives on the preview run
    /// record" is an answer about our schema rather than about their package.
    /// </summary>
    public static string StoryName(Guid userId, Guid packId) => $"{userId}/{packId}/story.json";

    public static string TelemetryName(Guid userId, Guid packId) => $"{userId}/{packId}/telemetry.json";

    /// <summary>
    /// One page's composition receipt: which approved pose was pasted where, and what the result
    /// hashed to. Named beside the spread it describes so the pair is obvious in a listing.
    /// </summary>
    public static string CompositionManifestName(Guid userId, Guid packId, int spreadNumber) =>
        $"{userId}/{packId}/spread-{spreadNumber:00}-composition.json";

    /// <summary>
    /// The child/world image before Beki was pasted onto it.
    ///
    /// Kept beside the finished page, under a name that says what it is, because it has a job after
    /// the page is drawn: it is the continuity reference a later spread reusing the same creature is
    /// shown. A resumed run that had only the composited page would either lose continuity or send
    /// an image model a picture with Beki in it, and the second is worse than the first.
    /// </summary>
    public static string SpreadBaseName(Guid userId, Guid packId, int spreadNumber) =>
        $"{userId}/{packId}/spread-{spreadNumber:00}-base.png";
}

/// <summary>
/// The document written OVER a preflight report whose stage has just refused — the answer to a
/// review finding about retries.
///
/// The defect it closes: a pack is retried, the digital preparation that succeeded last time now
/// fails, the current unprepared PDF is uploaded under the same name — and the PREVIOUS run's
/// successful report is still sitting under the report's name. A gate that read presence then found
/// evidence, and an unvalidated file published on the strength of a document about a different one.
/// Stale evidence is worse than no evidence, because absence is at least legible as absence.
///
/// Written rather than deleted. <c>IBlobStorageService</c>'s delete is keyed by a stored URL, and
/// this code has no stored URL for a blob a previous run wrote; an overwrite is addressed the same
/// way the upload was, and it leaves an operator something to read instead of a hole. The verdict
/// field is what the release evaluator refuses on — the preparation stages write <c>PASS</c> there
/// on success, so a report claiming anything else is a report of a refusal.
/// </summary>
public static class BekiWithheldReport
{
    public const string Stage = "beki-withheld-report-v1";

    public const string FailVerdict = "FAIL";

    /// <summary>
    /// One refusal, in the shape the gate reads: which gate refused, at what stage, and why.
    /// </summary>
    public static byte[] Bytes(string gate, string stage, string reason) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                stage = Stage,
                gate,
                verdict = FailVerdict,
                withheld_at_utc = DateTime.UtcNow,
                withheld_stage = stage,
                reason,
                note = "This file replaced a report from an earlier attempt. The artifact it "
                    + "describes was not prepared on this run, and no earlier run's verdict "
                    + "applies to the bytes now in storage.",
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
}

/// <summary>
/// Fulfils a purchased book in the Beki format: eight continuous spreads drawn from the plan the
/// parent previewed, laid out by <see cref="BekiPdfComposer"/>, ending as a completed pack with
/// a PDF — the same shape of result the legacy flow produces, reached by a different pipeline.
///
/// The story is never rewritten here. The preview run already holds the plan the parent read and
/// the cover they judged the book by; this job draws the eight spreads that were never shown,
/// which is the only part of the book that still costs a generation.
///
/// The reader is fed through the same projection the legacy flow uses: each spread's picture
/// page points at the stored spread image, so the existing reader and illustration endpoint
/// serve the book without knowing which pipeline made it.
/// </summary>
public sealed class BekiPackFulfillment(
    IAdventurePackRepository packRepository,
    IMasterStoryRunRepository masterStoryRunRepository,
    IBlobStorageService blobStorage,
    IBekiBookGenerator generator,
    IBekiPdfComposer composer,
    IAdminNotifier adminNotifier,
    IEmailService emailService,
    IUserRepository userRepository,
    IOptions<BekiOptions> bekiOptions,
    ILogger<BekiPackFulfillment> logger,
    TimeProvider? timeProvider = null,
    IPressUpscaler? pressUpscaler = null,
    BekiReleaseGates? releaseGates = null,
    BekiAssetLock? assetLock = null,
    IBekiReleasePolicyService? releasePolicy = null,
    IBekiAlarmService? alarms = null,
    IBekiReleaseReconciliation? reconciliation = null,
    IOrderRepository? orders = null) : IBekiPackFulfillment
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /*
      The three services the audit-2 correction added, defaulted rather than required.

      Defaulted because every one of them is constructible from what this class already holds, and
      because the alternative is a constructor break that reaches every test harness that ever
      builds this job. Container-resolved in production — see ServiceCollectionExtensions — where
      the upscaler reads the deployment's configured tool rather than the shipped-disabled default.

      The disabled default is the correct default. An unconfigured deployment withholds press files
      with PRESS_RESOLUTION rather than passing interpolated ones, which is precisely what audit
      P1-01 asks for.
    */
    private readonly IPressUpscaler _pressUpscaler =
        pressUpscaler ?? new CliPressUpscaler(bekiOptions.Value.PrintPrep);

    private readonly BekiReleaseGates _releaseGates = releaseGates ?? new BekiReleaseGates(blobStorage);

    private readonly BekiAssetLock _assetLock = assetLock ?? new BekiAssetLock();

    /// <summary>
    /// This job's one lookup of which order paid for this book, memoized for the run.
    ///
    /// Every alarm this job raises used to carry a null order id, and this job is the MAIN source of
    /// alarms — the waived spreads, the waived gates, the lost completion. The console's order link
    /// and its evidence button both key off that column, so the rows an operator most needs to act
    /// on were exactly the rows they could not open. (Review finding 4.)
    ///
    /// Memoized because a book raises one alarm per waived spread and per waived gate, and a
    /// database round trip apiece for an answer that cannot change during a job is a cost with
    /// nothing on the other side of it. Null is a real answer and is cached as one: a Beki book can
    /// exist without a paid order (a re-drive, a staging run), and an alarm about it is still worth
    /// having.
    /// </summary>
    private (Guid PackId, Guid? OrderId)? _order;

    /// <summary>
    /// The statuses a pack may be picked up from.
    ///
    /// <c>StoryReady</c> is the ordinary one: order fulfilment creates the pack, adopts the story
    /// the parent previewed — which writes StoryReady — and only then enqueues this job.
    /// <c>Pending</c> is the same book when that adoption failed. The two generating statuses are a
    /// requeue of an attempt that had already claimed the pack, which is how resuming works.
    ///
    /// Everything else is refused, and the one that matters is <c>Failed</c>: it is the only status
    /// written from outside this process, by the stale-generation sweep, and a job that claimed its
    /// way back out of it would make that verdict meaningless.
    /// </summary>
    private static readonly AdventurePackStatus[] ClaimableStatuses =
    [
        AdventurePackStatus.Pending,
        AdventurePackStatus.StoryReady,
        AdventurePackStatus.GeneratingStory,
        AdventurePackStatus.GeneratingPdf
    ];

    [DisableConcurrentExecution("beki-pack:{0}", 60)]
    public async Task ProcessAsync(Guid packId, Guid runId, CancellationToken cancellationToken)
    {
        // The spec's telemetry mandate (§27): before any performance work, measure where the
        // twenty minutes actually go. Total spans the whole job; uploads accumulate across the
        // mid-run callback, the catch-up pass and the PDF, which run one at a time by design.
        var totalStopwatch = Stopwatch.StartNew();
        long uploadMs = 0;

        /*
          The wall clock this book gets, and the thing that makes a stall terminal.

          Every await below runs under deadline.Token rather than Hangfire's, so the deadline
          reaches all the way down: the image calls, the QA calls, the retry sleeps inside the
          Gemini client, the uploads. Without that it would only be a timer nobody was watching —
          which is what the job had before, since it ran under CancellationToken.None.

          Which of the two tokens fired is the question the catch blocks ask, and it is why this is
          a deadline object rather than one linked source: a source that has cancelled itself
          cannot say why.
        */
        using var deadline = GenerationBudget.Start(
            cancellationToken, GenerationBudget.For(bekiOptions.Value), _timeProvider);
        var jobToken = deadline.Token;

        // Where the job was when it stopped, for the one log line somebody will read afterwards.
        // A cancelled job leaves no stack worth having: the exception says only that something was
        // cancelled, and every await in this method can raise it.
        var stage = "loading the pack";

        // What this job believes the row says, and therefore what its own terminal write is allowed
        // to overwrite. It becomes the status the pack was read in, then GeneratingStory the moment
        // the claim lands, so a failure at any point compares against the right thing.
        var expectedStatus = AdventurePackStatus.Pending;

        // Whether the row was ever actually read. A failure before that leaves the handler below
        // with nothing to compare against, and it goes and looks rather than guessing.
        var packWasRead = false;

        // Hoisted for the failure handler: the evidence blobs are keyed by owner, and the pack
        // variable itself lives inside the guarded region.
        Guid? packUserId = null;

        // Also for the failure handler, and for one line of one email: the child this book is
        // about. It lives on the preview run rather than the pack, so a failure before the run is
        // read simply has no name to use — which the letter is written to survive.
        string? childName = null;

        // Every SHA-256 the asset lock proved, so the fixed pages' machine QA can say whether the
        // rasters a page actually placed were approved files or something else.
        IReadOnlySet<string> assetLockHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /*
          The release policy, read ONCE per job and carried — amendment B4.

          This job runs for twenty minutes and makes a dozen policy-sensitive decisions in that time.
          Reading a cached service at each of them would let an admin's flip halfway through produce
          a book whose spread three was refused under one policy and whose spread seven shipped under
          another, with release-gates.json describing a decision that was never made as a whole. One
          reading, carried into the pipeline's context and down to the gate evaluation at the end.

          Declared here and taken inside the guarded region below, for the reason the pack load is:
          anything above the try has no handler, and the shipped defaults are the right value to hold
          while it is being read.
        */
        var policy = BekiReleasePolicySnapshot.Defaults;

        /*
          The load is inside the guarded region, which is not where it started.

          It ran above the try, under the budget's token, so a deadline that expired while this
          single SELECT was outstanding threw straight past every handler below: no terminal status,
          no classification of the cause, and a Hangfire retry that would do the same thing again.
          The pack would sit in the status it was enqueued in — one the sweep deliberately does not
          touch, because that is also the status a pack holds while queued — and nothing anywhere
          would ever close the case.
        */
        try
        {
            var pack = await packRepository.GetByIdNoOwnershipAsync(packId, jobToken);
            if (pack is null)
            {
                return;
            }

            packWasRead = true;
            packUserId = pack.UserId;

            // A completed pack is not reprocessed. The lease above stops two workers overlapping on
            // the same run; this stops a job that reaches this point a second time after the pack had
            // already finished — a stalled-order sweep re-enqueuing generation whose first attempt
            // had, in fact, already succeeded.
            if (pack.Status == AdventurePackStatus.Completed)
            {
                logger.LogInformation("Beki pack {PackId} is already completed; skipping.", packId);
                return;
            }

            /*
              And a pack the sweep has already buried is not exhumed.

              A requeued attempt used to claim any non-Completed pack straight back into
              GeneratingStory, which quietly undid the one verdict written by something outside this
              process: a book declared abandoned at forty minutes would be revived by the next
              retry, redrawn, and — because the redraw starts from the manifest — plausibly succeed,
              leaving nothing anywhere saying it had ever been lost. Worse, it made the sweep's
              Failed a status a book could bounce out of, which is the opposite of terminal.

              StoryReady belongs on this list and is the reason it is a list rather than a single
              check: a Beki pack adopts its previewed story into StoryReady at fulfilment and is
              enqueued from there, so that — not Pending — is the status nearly every real book
              arrives here in.
            */
            if (!ClaimableStatuses.Contains(pack.Status))
            {
                logger.LogWarning(
                    "Beki pack {PackId} is {Status}, which is not a status generation may claim; "
                    + "leaving it alone. A pack the stale-generation sweep failed needs a person to "
                    + "decide whether it is retried, not a requeue.",
                    packId, pack.Status);
                return;
            }

            expectedStatus = pack.Status;
            stage = "reading the release policy";

            if (releasePolicy is not null)
            {
                policy = await releasePolicy.SnapshotAsync(jobToken);
            }

            stage = "claiming the pack";

            // Claimed before any work: the stalled-order sweep re-enqueues generation for a pack
            // still Pending, and a book that costs nine images must never be drawn twice because
            // it was slow. Same move the legacy job opens with, for the same reason.
            //
            // The claim is also the first heartbeat — the repository stamps one on every status
            // write — which is what starts the clock the stale-generation sweep reads.
            await packRepository.UpdateStatusAsync(
                packId,
                AdventurePackStatus.GeneratingStory,
                pack.GeneratedJson,
                null,
                null,
                jobToken);

            expectedStatus = AdventurePackStatus.GeneratingStory;
            stage = "reading the plan";

            var run = await masterStoryRunRepository.GetByIdAsync(runId, jobToken)
                      ?? throw new InvalidOperationException($"Preview run {runId} is gone.");

            childName = run.ChildName;

            if (string.IsNullOrWhiteSpace(run.StoryJson) || string.IsNullOrWhiteSpace(run.PhotoBlobUrl))
            {
                throw new InvalidOperationException(
                    $"Run {runId} is missing its plan or its portrait; the Beki format needs both.");
            }

            var plan = JsonSerializer.Deserialize<MasterStory>(run.StoryJson, JsonOptions)
                       ?? throw new InvalidOperationException($"Run {runId} has an unreadable plan.");

            // Validated defensively, warnings only: the parent already read and bought this plan,
            // so a problem here is not this job's to refuse over. It is worth knowing about all
            // the same — the same checks that would have triggered a retry at preview time.
            foreach (var problem in BekiPlanValidator.Validate(plan, BookFormat.SpreadCount, run.Age))
            {
                logger.LogWarning("Beki pack {PackId}: plan validation problem — {Problem}", packId, problem);
            }

            await packRepository.UpdateProgressAsync(
                packId, "ბეკის წიგნის გვერდებს ვხატავთ…", 10, jobToken);

            stage = "reading the portrait";

            var photo = await blobStorage.DownloadBytesFromStoredUrlAsync(
                run.PhotoBlobUrl, jobToken);

            // The cover the parent previewed, when it survived; drawn fresh when it did not.
            byte[]? existingCover = null;
            if (!string.IsNullOrWhiteSpace(run.CoverImageUrl))
            {
                try
                {
                    existingCover = await blobStorage.DownloadBytesFromStoredUrlAsync(
                        run.CoverImageUrl, jobToken);
                }
                catch (Exception coverEx)
                {
                    logger.LogWarning(
                        coverEx, "Preview cover unavailable for pack {PackId}; drawing one.", packId);
                }
            }

            // The stored URL is whatever UploadAsync returned, never a key assembled here: the
            // two storage implementations shape their keys differently, and a key built by hand
            // is a key that reads in one environment and 404s in the other.
            var storedUrls = new Dictionary<int, string>();

            // Resuming a job whose first attempt died partway: read back whatever it had already
            // drawn and accepted, so this attempt redraws only the holes rather than the whole
            // book. Any problem reading the manifest — it is absent, it will not parse, the
            // illustration contract it was written under is no longer the one in force — is
            // treated as no manifest at all; the worst that costs is redrawing spreads that were
            // already fine, and the alternative is a book drawn half under each set of rules.
            var manifestName = BekiPackBlobs.ManifestName(pack.UserId, pack.Id);
            var compositeEnabled = bekiOptions.Value.CompositePipelineEnabled;

            /*
              The contract now names the pipeline as well as the page rules.

              Which pipeline drew a page is the term that matters most and was the one missing: the
              previous path has an image model draw Beki, the composite path pastes an approved PNG,
              and a flag flipped between two attempts at this pack would otherwise have adopted
              pages of the first kind into a book of the second — one book, two different characters,
              every page individually passing its own review. The composite versions ride with it for
              the same reason the shot wording does: a revised pose registry is different artwork and
              a revised pipeline config puts Beki somewhere else on the page.

              Read from the installed assets only when the flag is on. A deployment running the
              previous path may not have the composite assets at all, and loading them to describe a
              pipeline it is not using would fail a book over a file it never needed.
            */
            // The theme resolved before the contract, because the contract names the approved world
            // reference this book's pages are drawn against — a hash, per world, not a version per
            // deployment.
            var compositeThemeId = InputNormalization.CanonicalThemeId(pack.Theme.ToString());

            // Refused here rather than "a few lines later" as it used to be. A composite-enabled
            // run whose theme maps to nothing used to fall through with a LEGACY-shaped contract,
            // which meant a manifest written by the AI-draws-Beki pipeline would have matched it
            // and its pages could be adopted into this book before anything refused anything. The
            // pipeline did refuse the book eventually — but a contract that can wear the wrong
            // pipeline's shape while the flag is on is a mixed book waiting for the coupling to
            // loosen, and the supplier's audit found exactly that book: seven composited spreads
            // and one AI-drawn Beki.
            if (compositeEnabled && compositeThemeId is null)
            {
                throw new CompositePipelineException(
                    CompositeFailureCodes.InvalidBookInput,
                    $"Theme '{pack.Theme}' does not map to an approved composite theme, so no "
                    + "composite book can be drawn or resumed for it.");
            }

            /*
              The asset lock, before anything is drawn — audit P1-02, plan D9.

              First, and that word is the whole design. The lock proves every fixed asset this book
              will print — the endpaper pattern, the six intro backgrounds, the nine approved poses,
              the five licensed font files, the FOGRA39 profile — against the registries that
              approved them, and a book that fails it fails here rather than after nine paid image
              calls. The finding it answers was not that a check was wrong: it was that the machinery
              existed with no callers, so a delivered book could not be shown to have been built from
              approved bytes at all.

              Composite books only. The previous path draws Beki with an image model and places no
              approved pose, so a lock over the pose registry would be asserting something about
              artwork that path never touches.
            */
            if (compositeEnabled)
            {
                stage = "proving the approved assets";
                assetLockHashes = await VerifyAssetLockAsync(pack, jobToken);
            }

            var currentContract = BekiFulfillmentManifest.CurrentContract(
                BookFormat.SpreadCount,
                compositeEnabled && compositeThemeId is not null
                    ? BekiCompositeContractTerms.Current(compositeThemeId)
                    : null);

            var manifest = await TryReadManifestAsync(manifestName, jobToken);

            if (manifest is not null && !manifest.IllustrationContract.SequenceEqual(currentContract))
            {
                logger.LogWarning(
                    "Beki pack {PackId}: the manifest's illustration contract (pipeline, text side, "
                    + "shot, Beki version) no longer matches the current one; ignoring it and "
                    + "redrawing.", packId);
                manifest = null;
            }

            /*
              The child identity spec an earlier attempt derived, read BEFORE anything is adopted —
              because whether it can be read decides whether anything may be.

              The four attributes are written into every image prompt, so a run that adopted pages
              and then derived a second spec would describe the child one way on the spreads it
              redraws and another on the spreads it keeps. That is the split book this whole
              amendment exists to prevent, and it would pass every review on the way, so it cannot
              be left to the pipeline to notice: by the time the pipeline sees a null spec it has no
              way to tell "first attempt" from "resume whose spec blob is gone".

              So an unreadable spec drops the artwork rather than the spec. The scenario stays —
              nothing is wrong with it, the outfit and recurring elements it fixes are still the
              ones this book was sold as, and eight pages redrawn against it under one fresh spec is
              a whole book. It is the same answer a prompt-version change already gets from the
              resume contract, reached by a different route.
            */
            string? storedIdentitySpec = null;

            // The earlier attempt's review, read for the two things a resumed run cannot rebuild:
            // the shot advisories of the pages it adopts, and whether the pose-vocabulary retry was
            // already spent. Read even when the artwork is later discarded below — a redrawn book
            // still inherits the fact that its plan cost a retry.
            string? storedReview = null;

            if (compositeEnabled && manifest is not null)
            {
                storedIdentitySpec = await TryReadIdentitySpecAsync(
                    packId, manifest.IdentitySpecUrl, jobToken);

                storedReview = await TryReadReviewAsync(packId, manifest.ReviewUrl, jobToken);

                if (string.IsNullOrWhiteSpace(storedIdentitySpec) && manifest.Entries.Count > 0)
                {
                    logger.LogWarning(
                        "Beki pack {PackId}: {Stored} spread(s) are stored but this book's child "
                        + "identity spec cannot be read, so the pages that would be redrawn could "
                        + "not be drawn to the same child as the pages that would be adopted. "
                        + "Discarding the stored artwork and redrawing the whole book.",
                        packId, manifest.Entries.Count);

                    // The pages and their receipts go; the scenario and everything else about the
                    // manifest stay, which is what keeps this a redraw rather than a replan.
                    manifest = manifest with { Entries = [], Compositions = null };
                }
            }

            var existingSpreads = new Dictionary<int, byte[]>();

            // The pre-composite base of each adopted page, so continuity survives a resume: spread
            // three redrawn against a book whose creature was introduced on the adopted spread two
            // needs to be shown spread two's base, and the composited page is not a substitute —
            // it has Beki on it, and Beki is the one thing this pipeline never shows an image model.
            var existingBases = new Dictionary<int, byte[]>();

            if (manifest is not null)
            {
                foreach (var entry in manifest.Entries)
                {
                    try
                    {
                        var bytes = await blobStorage.DownloadBytesFromStoredUrlAsync(
                            entry.StoredUrl, jobToken);
                        if (bytes is not { Length: > 0 })
                        {
                            continue;
                        }

                        existingSpreads[entry.SpreadNumber] = bytes;
                        storedUrls[entry.SpreadNumber] = entry.StoredUrl;
                    }
                    catch (Exception ex)
                    {
                        // A download failure just drops that entry — it will be redrawn, same as
                        // a spread that was never in the manifest at all.
                        logger.LogWarning(
                            ex, "Beki pack {PackId}: could not adopt spread {Spread} from the manifest.",
                            packId, entry.SpreadNumber);
                    }
                }

                foreach (var entry in manifest.Compositions ?? [])
                {
                    // Only for a page that was actually adopted: a base image belonging to a page
                    // this attempt is redrawing anyway is a download nobody reads.
                    if (entry.BaseImageUrl is not { Length: > 0 } baseUrl
                        || !existingSpreads.ContainsKey(entry.SpreadNumber))
                    {
                        continue;
                    }

                    try
                    {
                        var bytes = await blobStorage.DownloadBytesFromStoredUrlAsync(
                            baseUrl, jobToken);

                        if (bytes is { Length: > 0 })
                        {
                            existingBases[entry.SpreadNumber] = bytes;
                        }
                    }
                    catch (Exception ex)
                    {
                        // A missing base costs continuity on this page, not the page itself. The
                        // pipeline says so in its warnings rather than silently drawing without one.
                        logger.LogWarning(
                            ex, "Beki pack {PackId}: could not read the base image for spread "
                            + "{Spread}; later spreads lose it as a continuity reference.",
                            packId, entry.SpreadNumber);
                    }
                }

                if (existingSpreads.Count > 0)
                {
                    // Adopted spreads advance the percent immediately, using the same message the
                    // drawing loop below would use — a resumed job's progress bar should not lie
                    // by sitting at 10% while most of the book is already done.
                    var percent = 10 + (int)MathF.Round(existingSpreads.Count * 70f / BookFormat.SpreadCount);
                    await packRepository.UpdateProgressAsync(
                        packId,
                        $"დაიხატა {existingSpreads.Count}/{BookFormat.SpreadCount} ილუსტრაცია…",
                        percent,
                        jobToken);
                }
            }

            // Progress counts spreads processed, not the spread number being drawn. On a resume
            // the two disagree: with spreads 1 and 3–6 adopted and spread 2 being redrawn, the
            // number would report "2/8" and yank the bar backwards below the five images the
            // parent was already shown as done.
            var processedSpreads = existingSpreads.Count;

            // Written as each page finishes rather than once at the end: a job that dies on spread
            // seven must not lose the receipts for the six pages that were fine, because a resumed
            // run adopts those pages and never composites them again. Seeded from the manifest for
            // exactly that reason — the receipts an earlier attempt wrote belong to the pages this
            // attempt is adopting, and rewriting the manifest without them would drop the evidence
            // for the half of the book that did not need redrawing.
            var compositions = (manifest?.Compositions ?? [])
                .ToDictionary(entry => entry.SpreadNumber);

            var scenarioUrl = manifest?.ScenarioUrl;
            var identitySpecUrl = manifest?.IdentitySpecUrl;

            /*
              The review an earlier attempt stored, kept until this attempt has one of its own.

              Seeded rather than started at null for the same reason the compositions are: a resumed
              run rewrites this manifest from the first page it stores, long before it has a finished
              book to measure. Dropping the earlier attempt's URL there would leave the pack with a
              review blob nothing points at — and if this attempt then failed, the pack would have
              lost the only record of what was wrong with the book it had.

              A run that completes overwrites both the blob and this URL with its own, which is
              correct: the review describes the book that shipped, and that is this attempt's book.
            */
            var reviewUrl = manifest?.ReviewUrl;

            // Written once, after the book is drawn — until then the manifest carries whatever an
            // earlier attempt recorded, which is the cover that attempt shipped.
            var coverRecord = manifest?.Cover;

            // Where this book's normalized story ends up, once there is a book to store it with.
            // Null on the previous path, which stores no such artifact and never has.
            string? storyUrl = manifest?.StoryUrl;

            /*
              The scenario an earlier attempt planned, read back before anything is drawn.

              This is the correctness half of resuming, not the cheap half. The scenario fixes the
              child's outfit and the book's recurring elements for all nine pictures; a resumed run
              that planned a second one would redraw its missing spreads against a different outfit
              from the spreads it is adopting, and produce a book where the child changes clothes at
              page four out of pages that each passed their own review.

              A scenario that cannot be read is simply absent — the pipeline plans a new one and
              says so in its warnings, which is the same answer as a first attempt.
            */
            string? storedScenario = null;
            if (compositeEnabled && scenarioUrl is { Length: > 0 })
            {
                try
                {
                    var bytes = await blobStorage.DownloadBytesFromStoredUrlAsync(
                        scenarioUrl, jobToken);

                    if (bytes is { Length: > 0 })
                    {
                        storedScenario = System.Text.Encoding.UTF8.GetString(bytes);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex, "Beki pack {PackId}: could not read the stored Visual Scenario; a new "
                        + "one will be planned.", packId);
                }
            }

            /*
              The four normalized inputs, assembled only when the composite flag is on.

              This job is the only place in the application that holds all of them: the run carries
              the child's name and age, the pack carries the theme and the owner, and the run's
              photo blob is the photograph. The illustrator receives a plan and a photograph and
              could not reconstruct any of it, which is why the context is passed down rather than
              derived there — and why every other caller of IllustrateAsync passes nothing and stays
              on the path it has always taken.

              Null when the flag is off, and a null here is what makes the branch inside the
              generator unreachable rather than merely untaken.
            */
            var compositeContext = compositeEnabled
                ? new CompositeBookContext
                {
                    JobId = pack.Id,
                    Input = new BookGenerationInput
                    {
                        ChildName = run.ChildName,
                        ChildAge = run.Age,
                        // The journey's own spelling, whatever it was when this run was written.
                        // InputNormalization maps it and refuses what it cannot map, which is the
                        // correct outcome for a row nobody can write a book from.
                        ChildGender = run.Gender,
                        ThemeId = pack.Theme.ToString(),
                        // The reference, never the bytes. The bytes travel as an argument; this
                        // field exists so a failure can say which blob could not be read without
                        // the photograph itself ending up anywhere near a log line.
                        ChildPhotoRef = run.PhotoBlobUrl!,
                        // The parent's own answer, where the run has one. It overrides the model's
                        // reading of the photograph for that single attribute and reaches nothing
                        // else: the normalized story input has nowhere to put it.
                        LegacyEyeColor = run.EyeColor,
                    },
                    // Whether this pack's cover has already been through the redraw. The
                    // illustrator cannot know it — the manifest is this job's — and a resumed
                    // attempt that redrew it again would replace a reviewed cover with one that
                    // has to be reviewed from scratch, for no gain.
                    CoverAlreadyRedrawn = coverRecord?.IsCurrentRedraw == true,
                    Resume = new CompositeResumeState(storedScenario, existingSpreads, existingBases)
                    {
                        IdentitySpecJson = storedIdentitySpec,
                        ReviewJson = storedReview,
                        // The child appearance anchor is the accepted first spread's base image,
                        // which is already in hand whenever that spread was adopted — the same
                        // bytes the manifest's composition entry points at, not a second copy of
                        // them. Absent means the pipeline redraws spread one and makes a new one.
                        AnchorBasePng = existingBases.GetValueOrDefault(
                            CompositeBookPipeline.AnchorSpreadNumber),
                    },
                    // This job's one reading of the policy, handed down. See the declaration above.
                    ReleasePolicy = policy,

                    /*
                      Where a waived quality refusal lands: the picture, the paperwork and the alarm.

                      The pipeline cannot write a blob and has no repository, which is why this is a
                      callback rather than a dependency of its own. What it writes is deliberately
                      NOT the spread's QA record — that comes back on the artifact and says what the
                      reviewer actually said — but a separate waiver document beside the refused
                      picture, so the two can be read together without either overwriting the other.
                    */
                    OnPolicyWaiver = async waiver =>
                        await RecordWaiverAsync(pack, waiver, jobToken),

                    OnIdentitySpec = async identityJson =>
                    {
                        // Before the first image call and awaited, exactly as the scenario is: the
                        // attempt that dies on spread three is the one that has to leave this
                        // behind, because the attempt that finishes never needed it written down.
                        identitySpecUrl = await blobStorage.UploadAsync(
                            BekiPackBlobs.IdentitySpecName(pack.UserId, pack.Id),
                            System.Text.Encoding.UTF8.GetBytes(identityJson),
                            "application/json",
                            jobToken);

                        await WriteManifestAsync(
                            manifestName, storedUrls, currentContract, scenarioUrl, identitySpecUrl,
                            coverRecord, compositions, jobToken, reviewUrl);
                    },
                    OnScenario = async scenarioJson =>
                    {
                        // Before the first image call, because the attempt that dies on spread three
                        // is exactly the attempt that has to leave this behind. Storing it with the
                        // finished book would mean the only run that records the scenario is the run
                        // that never needed it recorded.
                        scenarioUrl = await blobStorage.UploadAsync(
                            BekiPackBlobs.ScenarioName(pack.UserId, pack.Id),
                            System.Text.Encoding.UTF8.GetBytes(scenarioJson),
                            "application/json",
                            jobToken);

                        await WriteManifestAsync(
                            manifestName, storedUrls, currentContract, scenarioUrl, identitySpecUrl,
                            coverRecord, compositions, jobToken, reviewUrl);
                    },
                }
                : null;

            stage = "drawing the spreads";

            var book = await generator.IllustrateAsync(
                plan,
                photo,
                "image/png",
                existingCover,
                async image =>
                {
                    if (image.SpreadNumber is not { } number)
                    {
                        return;
                    }

                    // Refused spreads are not uploaded mid-run and do not touch the manifest — the
                    // catch-up pass below still uploads whatever storedUrls lacks once the book is
                    // whole, unchanged from before this job became resumable.
                    if (image.Accepted)
                    {
                        var uploadStopwatch = Stopwatch.StartNew();
                        storedUrls[number] = await blobStorage.UploadAsync(
                            BekiPackBlobs.SpreadName(pack.UserId, pack.Id, number),
                            image.Image,
                            "image/png",
                            jobToken);

                        // Null on the legacy path, so this block is not merely skipped there — it
                        // has nothing to skip.
                        if (image.Composition is { } composition)
                        {
                            compositions[number] = await StoreCompositionAsync(
                                pack, composition, jobToken);
                        }

                        await WriteManifestAsync(
                            manifestName, storedUrls, currentContract, scenarioUrl, identitySpecUrl,
                            coverRecord, compositions, jobToken, reviewUrl);
                        uploadMs += uploadStopwatch.ElapsedMilliseconds;
                    }

                    processedSpreads++;
                    var percent = 10 + (int)MathF.Round(processedSpreads * 70f / BookFormat.SpreadCount);
                    await packRepository.UpdateProgressAsync(
                        packId,
                        $"დაიხატა {processedSpreads}/{BookFormat.SpreadCount} ილუსტრაცია…",
                        percent,
                        jobToken);
                },
                jobToken,
                existingSpreads.Count > 0 ? existingSpreads : null,
                compositeContext);

            foreach (var warning in book.Warnings)
            {
                logger.LogWarning("Beki pack {PackId}: {Warning}", packId, warning);
            }

            /*
              The catch-up pass for composite artifacts, matching the one below for images.

              The scenario is already stored — the pipeline's own callback wrote it before the first
              picture was drawn, which is the whole point of doing it there — and the per-page
              callback stored each receipt as it landed. This only picks up a page delivered when
              nobody was watching, which is every page when onImage is null.

              Absent from every book in production: book.Composite is null on the legacy path.
            */
            if (book.Composite is { } compositeArtifacts)
            {
                var artifactStopwatch = Stopwatch.StartNew();

                foreach (var artifact in compositeArtifacts.Spreads)
                {
                    /*
                      An adopted page has no receipt to write, and writing one anyway is worse than
                      writing nothing.

                      Amendment A4 put adopted spreads back into this list — they used to be
                      filtered out, which is how a resumed book's QA coverage became "whatever this
                      attempt happened to redraw". They arrive flagged and deliberately empty: no
                      pose, no manifest, no output hash, because this run composited nothing. Storing
                      one as though it were a receipt would overwrite the earlier attempt's real
                      composition entry with a blank, and the exact-Beki gate would then be satisfied
                      by a document that proves nothing.
                    */
                    if (artifact.Adopted || compositions.ContainsKey(artifact.SpreadNumber))
                    {
                        continue;
                    }

                    compositions[artifact.SpreadNumber] =
                        await StoreCompositionAsync(pack, artifact, jobToken);
                }

                /*
                  And every page's QA verdict is written down — audit P0-09, plan D7.

                  The rejected package listed all eight `spread-XX-qa.json` files as missing beside
                  two finished PDFs, and the reason was not that the pages went unreviewed: the
                  accepted verdicts were held in memory, used to decide whether to ship, and dropped.
                  Only a refusal wrote a record, on its way out. A book's QA either survives the book
                  or it was never evidence.

                  An adopted page is the one case where this run has nothing to write, so it reads
                  back what the attempt that drew the page wrote — and a record that is gone or was
                  written under a superseded reviewer contract is left absent on purpose. The
                  release gates answer for the gap; papering over it here would be inventing a
                  verdict for a page nothing current has looked at.
                */
                foreach (var artifact in compositeArtifacts.Spreads)
                {
                    await StoreSpreadQaAsync(pack, artifact, jobToken);
                }

                /*
                  The cover is no longer decided here, and that is audit P0-01 being answered.

                  What used to stand in this place was a redraw: after the first spread was
                  accepted, an image model drew the customer's front page again, and the reader's
                  cover column was re-pointed at it. Meanwhile the printer's cover was the
                  composited wrap. Two producers, two designs, one book — the finding the supplier
                  rejected the package for, and one that no amount of reviewing either picture could
                  have caught, because each was individually fine.

                  So the composite path now has exactly one cover master, the wrap, and it is built
                  below with the delivery it feeds: press cover, customer front and back pages, and
                  the reader's own image, all cut from the same bytes. `cover.png` is not written by
                  this path any more; a blob under that name belongs to a run that predates the
                  correction and travels in the handback under `diagnostic/`.
                */

                /*
                  The book-level review, stored with the finished book and pointed at from the
                  manifest.

                  Last rather than first, and that is the whole reason it is not written beside the
                  scenario: every number in it is a count across all eight spreads. A partial review
                  written at spread three would say "two fallbacks" about a book that will have six,
                  and a number an operator can read is a number they will act on.

                  Best-effort, deliberately. The book is drawn, reviewed, composited and about to be
                  laid out; losing the measurement of a finished book to a storage hiccup is a bad
                  trade, and the same record is already in the log line the pipeline wrote.
                */
                if (compositeArtifacts.ReviewJson is { Length: > 0 } reviewDocument)
                {
                    try
                    {
                        reviewUrl = await blobStorage.UploadAsync(
                            BekiPackBlobs.CompositeReviewName(pack.UserId, pack.Id),
                            System.Text.Encoding.UTF8.GetBytes(reviewDocument),
                            "application/json",
                            jobToken);
                    }
                    catch (Exception reviewEx) when (reviewEx is not OperationCanceledException)
                    {
                        logger.LogWarning(
                            reviewEx, "Beki pack {PackId}: the composite review could not be "
                            + "stored; the book is unaffected and the counts are in the log.",
                            packId);
                    }
                }

                await WriteManifestAsync(
                    manifestName, storedUrls, currentContract, scenarioUrl, identitySpecUrl,
                    coverRecord, compositions, jobToken, reviewUrl);

                uploadMs += artifactStopwatch.ElapsedMilliseconds;

                logger.LogInformation(
                    "Beki pack {PackId}: composite artifacts stored — visual scenario at "
                    + "{ScenarioStored}, {Receipts} composition manifest(s), review at "
                    + "{ReviewStored} ({ReviewSummary}).",
                    packId, scenarioUrl is null ? "(none)" : "its blob", compositions.Count,
                    reviewUrl is null ? "(none)" : "its blob",
                    compositeArtifacts.Review?.Summary ?? "no review");
            }

            stage = "laying out the PDF";

            await packRepository.UpdateProgressAsync(
                packId, "წიგნს ვაწყობთ და PDF-ს ვამზადებთ…", 85, jobToken);

            /*
              Every spread is stored, and — since the audit — storing is not the same as shipping.

              This pass only catches a page the mid-run callback missed; that part is unchanged. What
              changed is what "everything ships" was allowed to mean. It used to mean the book
              completed the moment the pictures existed: a NEEDS_REVIEW spread was a warning in a log
              and a picture in a paid book, and the release gates document had no reader at all. The
              spreads still reach the parent's in-app reader unconditionally — that is deliberate and
              amendment A5 says so — but the deliverable FILES are now published by a verdict rather
              than by nothing having thrown.
            */
            var stored = new List<BekiSpreadArtwork>(book.Spreads.Count);
            foreach (var spread in book.Spreads.OrderBy(s => s.SpreadNumber ?? 0))
            {
                var number = spread.SpreadNumber ?? 0;
                if (!storedUrls.ContainsKey(number))
                {
                    var uploadStopwatch = Stopwatch.StartNew();
                    storedUrls[number] = await blobStorage.UploadAsync(
                        BekiPackBlobs.SpreadName(pack.UserId, pack.Id, number),
                        spread.Image,
                        "image/png",
                        jobToken);
                    uploadMs += uploadStopwatch.ElapsedMilliseconds;
                }

                stored.Add(new BekiSpreadArtwork(number, spread.Image));
            }

            // Personalization is what the intro spread prints and the endpapers key on: the
            // child's name and age from the run, the world as the parent chose it
            // (StoryWorlds.For, the same canon the planner writes from), and the purchase date —
            // pack.CreatedAt, never "now", so a job that dies and resumes tomorrow prints the
            // same date its first attempt would have.
            var personalization = new BekiBookPersonalization(
                run.ChildName, run.Age, pack.CreatedAt, pack.Theme.ToString(),
                StoryWorlds.For(pack.Theme).Place);

            /*
              Every page must show its exact-Beki receipt before it may be laid out.

              The composition manifest entries are the proof that a spread's Beki is the approved
              PNG — pose id, source hash, output hash — and the supplier's audit is what a book
              looks like when one page lacks the receipt and ships anyway: seven composited
              spreads and one AI-drawn Beki, indistinguishable in the PDF until a reviewer put
              them side by side. A missing receipt here means the page came from somewhere other
              than the compositor, whatever that somewhere was, and the book stops instead of
              printing it.
            */
            if (compositeEnabled)
            {
                var unreceipted = stored
                    .Select(artwork => artwork.SpreadNumber)
                    .Where(number => !compositions.ContainsKey(number))
                    .OrderBy(number => number)
                    .ToList();

                if (unreceipted.Count > 0)
                {
                    throw new CompositePipelineException(
                        CompositeFailureCodes.ImageGenerationFailed,
                        "No exact-Beki composition receipt for spread(s) "
                        + $"{string.Join(", ", unreceipted)}: a page without one did not come "
                        + "from the approved compositor and must not be printed.");
                }
            }

            var pdfStopwatch = Stopwatch.StartNew();

            /*
              From here the book is delivered, and the two pipelines deliver differently now.

              The composite path is the one the audit rewrote. Its order is the correction: the
              cover wrap is generated FIRST, becomes the only cover source, and everything a person
              ever sees — the printed wrap, the customer PDF's front and back pages, the image in
              the reader — is cut from those same bytes (D1). Its failure is fatal, which is new and
              deliberate: a composite book with no wrap has no cover master, and the old behaviour
              (fall back to the previewed picture) is precisely the second producer the supplier
              rejected the package for.

              The previous path is untouched. It has no wrap to generate, one cover it drew, and the
              same fourteen-page document it has always shipped.
            */
            string? pdfUrl;
            BekiReleaseGateReport? release = null;

            if (compositeEnabled && book.Composite is { ScenarioJson: { Length: > 0 } scenarioDocument })
            {
                stage = "drawing the cover wrap";

                var scenario = VisualScenarioValidator.Validate(scenarioDocument).Scenario
                    ?? throw new BekiLayoutException(
                        CompositeFailureCodes.LayoutFailed,
                        "the stored Visual Scenario could not be read back for the cover master.");

                var wrap = await generator.DrawCoverWrapAsync(
                    scenario, photo, "image/png", compositeContext!, jobToken);

                /*
                  And the master is written down, then checked against its own receipt.

                  Audit P0-10's finding was that this file did not exist: the composition manifest
                  declared `cover-wrap-composite.png` and its SHA-256, the bytes lived in a local
                  variable, and nothing wrote them. Every cover derivation claimed a provenance
                  nobody could check. So the bytes are stored, and the hash is recomputed from what
                  was stored and compared with what the receipt declares — a mismatch stops the book
                  rather than shipping three derivations of an unidentified image.
                */
                var wrapSha = Sha256Hex(wrap.CompositePng);
                var declaredSha = ReceiptValue(wrap.ManifestJson, "output", "sha256");

                if (!string.Equals(declaredSha, wrapSha, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CompositePipelineException(
                        CompositeFailureCodes.ImageGenerationFailed,
                        $"The cover wrap composite hashes to {wrapSha} and its composition receipt "
                        + $"declares {declaredSha ?? "(nothing)"}. The press cover, both customer "
                        + "cover pages and the reader's image are all cut from these bytes, so a "
                        + "master that does not match its own receipt is not a master.");
                }

                var wrapUrl = await blobStorage.UploadAsync(
                    BekiPackBlobs.CoverWrapCompositeName(pack.UserId, pack.Id),
                    wrap.CompositePng, "image/png", jobToken);

                // The wrap's audit trail: the pre-composite base the band gate measured, and the
                // exact-Beki receipt — the same paperwork every story spread carries.
                await blobStorage.UploadAsync(
                    BekiPackBlobs.CoverWrapBaseName(pack.UserId, pack.Id),
                    wrap.BasePng, "image/png", jobToken);
                await blobStorage.UploadAsync(
                    BekiPackBlobs.CoverCompositionName(pack.UserId, pack.Id),
                    System.Text.Encoding.UTF8.GetBytes(wrap.ManifestJson),
                    "application/json", jobToken);

                coverRecord = new BekiCoverRecord(
                    wrapUrl,
                    BekiCoverRecord.WrapMaster,
                    $"exact-Beki composite verified against its receipt ({wrapSha[..12]}…)")
                {
                    PoseId = wrap.PoseId,
                    CompositeSha256 = wrapSha,
                    Anchor = ReceiptAnchor(wrap.ManifestJson),
                };

                /*
                  The reader's cover, which is where the old redraw used to point.

                  The pack's cover column is what the library card and the reader serve. It held the
                  preview run's picture until v1.2 and the AI redraw after that, and in both cases
                  it was a different design from the printed cover. It is now the wrap's own front
                  board, cropped — the same rectangle the customer PDF's first page is built from.
                */
                var frontBoardUrl = await blobStorage.UploadAsync(
                    BekiPackBlobs.CoverFrontName(pack.UserId, pack.Id),
                    composer.CropFrontBoard(wrap.CompositePng), "image/png", jobToken);

                await packRepository.UpdateBookPresentationAsync(
                    packId, title: null, coverImageUrl: frontBoardUrl, jobToken);

                logger.LogInformation(
                    "Beki pack {PackId}: cover master stored — the composited wrap (pose {PoseId}, "
                    + "sha {Sha}), with the reader pointed at its front-board crop.",
                    packId, wrap.PoseId, wrapSha[..12]);

                // ---- The parent's book -------------------------------------------------------
                stage = "laying out the customer's book";

                /*
                  A dedicated trim-size export, which audit P0-08 is entirely about. What shipped
                  before was the press document with a different file name: bleed-inflated pages, no
                  CropBox, every raster stretched to a 300-PPI target nothing on a screen can use.
                  This is fourteen pages at the finished size, in sRGB, linearized, with the wrap's
                  own boards as its covers — and it goes through its own preflight (amendment A10c)
                  rather than through the press one.
                */
                var reading = composer.ComposeReading(plan, wrap.CompositePng, stored, personalization);

                /*
                  A refused digital preflight withholds the download; it does not fail the book.

                  The distinction is the one amendment A5 draws everywhere: the in-app reader is the
                  spread PNGs and is already serving, so a family whose file will not pass its own
                  geometry check still has the book they paid for on screen. What they do not get is
                  a download, and the DIGITAL_GEOMETRY gate says so by finding no report. The
                  composed bytes are still stored, under the same name, so an operator can open what
                  was actually made rather than reasoning about a file nobody kept.
                */
                byte[] readingBytes;
                string? digitalReport = null;

                try
                {
                    (readingBytes, digitalReport) = BekiDigitalPrep.Prepare(
                        reading.Pdf, bekiOptions.Value.PrintPrep);
                }
                catch (BekiLayoutException ex)
                    when (ex.FailureCode == CompositeFailureCodes.PrintPreflightFailed)
                {
                    readingBytes = reading.Pdf;

                    logger.LogWarning(
                        "Beki pack {PackId}: the customer PDF is withheld ({Code}) — {Reason} The "
                        + "in-app reader is unaffected.",
                        packId, ex.FailureCode, ex.Message);
                }

                pdfUrl = await blobStorage.UploadAsync(
                    BekiPackBlobs.ReadingPdfName(pack.UserId, pack.Id),
                    readingBytes, "application/pdf", jobToken);

                if (digitalReport is { Length: > 0 })
                {
                    await blobStorage.UploadAsync(
                        BekiPackBlobs.DigitalReportName(pack.UserId, pack.Id),
                        System.Text.Encoding.UTF8.GetBytes(digitalReport), "application/json", jobToken);
                }
                else
                {
                    /*
                      And the previous attempt's report is written over, which it was not.

                      The unprepared PDF has just been uploaded under the name the prepared one uses.
                      On a RETRY, the report blob still holds the last successful run's document —
                      about bytes that are no longer there — and the DIGITAL_GEOMETRY gate, which
                      read presence, would find evidence for a file nothing has preflighted and
                      publish it. Replaced with a refusal the evaluator treats as a failure.
                    */
                    await blobStorage.UploadAsync(
                        BekiPackBlobs.DigitalReportName(pack.UserId, pack.Id),
                        BekiWithheldReport.Bytes(
                            BekiDigitalPrep.DigitalGeometryGate,
                            "laying out the customer's book",
                            "the customer PDF did not pass its own preflight on this run, so the "
                            + "stored file is the unprepared composition."),
                        "application/json", jobToken);
                }

                await UploadLayoutReceiptsAsync(pack, "reading", reading.Receipts, jobToken);

                // The normalized story, stored with the book rather than only on the preview run —
                // audit §9 takes it off the handback's excluded list, and a package that excludes
                // the words the pictures were planned from is a package the supplier cannot check.
                storyUrl = await blobStorage.UploadAsync(
                    BekiPackBlobs.StoryName(pack.UserId, pack.Id),
                    System.Text.Encoding.UTF8.GetBytes(run.StoryJson!), "application/json", jobToken);

                await StoreFixedPageQaAsync(pack, reading.Receipts, assetLockHashes, jobToken);

                // ---- The printer's two files -------------------------------------------------
                stage = "preparing the press files";

                var press = await PreparePressAsync(
                    pack, plan, stored, personalization, wrap.CompositePng, jobToken);

                // ---- Render validation on what was actually stored ---------------------------
                stage = "rendering the stored artifacts back";

                await ValidateStoredRendersAsync(pack, jobToken);

                // ---- The verdict --------------------------------------------------------------
                stage = "evaluating the release gates";

                await WriteManifestAsync(
                    manifestName, storedUrls, currentContract, scenarioUrl, identitySpecUrl,
                    coverRecord, compositions, jobToken, reviewUrl,
                    await PrivateReferencesAsync(pack, run.PhotoBlobUrl!, identitySpecUrl, jobToken)
                        with { StoryUrl = storyUrl });

                // Judged under this job's own policy reading, not under whatever the table says by
                // the time the evaluation runs — the same snapshot the spreads were drawn against.
                release = await _releaseGates.EvaluateAsync(
                    pack.UserId, pack.Id, jobToken, policy);

                await blobStorage.UploadAsync(
                    BekiPackBlobs.ReleaseGatesName(pack.UserId, pack.Id),
                    System.Text.Encoding.UTF8.GetBytes(release.ToJson()), "application/json", jobToken);

                /*
                  One alarm per waived gate — amendment B4's second half.

                  A gate that fails and does not withhold produces no exception, no failed status and
                  no log anybody has a reason to read; without this it would be the quietest event in
                  the system and the one most worth knowing about. The pipeline's own waivers were
                  raised as they happened; these are the ones only the verdict can see.
                */
                if (reconciliation is not null)
                {
                    await reconciliation.RaiseWaiverAlarmsAsync(
                        pack.Id, pack.UserId, await OrderIdAsync(pack.Id, jobToken), release,
                        jobToken);
                }

                /*
                  And the withholding, which is the whole point of having measured any of it.

                  The in-app reader is already serving: the spreads are stored and the projection
                  below points at them, and no gate touches that — amendment A5 is explicit that a
                  paid book is not held hostage to a printer's colour profile. What is held is every
                  deliverable FILE. The press slot takes the interior only when the shared and press
                  gates pass; the parent's download column is written below, and only when the
                  shared, digital and human gates do.
                */
                await packRepository.UpdatePrintPdfUrlAsync(
                    packId,
                    release.PressFilesMayPublish ? press.InteriorUrl : null,
                    jobToken);

                logger.LogInformation(
                    "Beki pack {PackId}: release verdict {Verdict}. Failing gates: {Failing}. "
                    + "Customer PDF {Customer}; press files {Press}.",
                    packId, release.Verdict,
                    release.FailingGates.Count == 0 ? "(none)" : string.Join(", ", release.FailingGates),
                    release.CustomerPdfMayPublish ? "published" : "withheld",
                    release.PressFilesMayPublish && press.InteriorUrl is not null
                        ? "published" : "withheld");
            }
            else
            {
                /*
                  The previous path, unchanged by this campaign.

                  One document with the cover faces in it, laid out from the cover this run drew or
                  adopted, and an interior press file that earns the print slot through print
                  preparation or does not get it at all. No wrap, no gates, no withholding — a
                  legacy book has none of the artifacts they are computed from.
                */
                var composed = composer.ComposeWithReceipts(
                    plan, book.Cover.Image, stored, personalization);

                pdfUrl = await blobStorage.UploadAsync(
                    BekiPackBlobs.ReadingPdfName(pack.UserId, pack.Id),
                    composed.Pdf, "application/pdf", jobToken);

                try
                {
                    var (preparedInterior, preflightReport) = BekiPrintPrep.Prepare(
                        composer.ComposeInteriorWithReceipts(plan, stored, personalization).Pdf,
                        plan.Concept.Title,
                        bekiOptions.Value.PrintPrep);

                    var interiorUrl = await blobStorage.UploadAsync(
                        BekiPackBlobs.InteriorPdfName(pack.UserId, pack.Id),
                        preparedInterior, "application/pdf", jobToken);

                    await blobStorage.UploadAsync(
                        BekiPackBlobs.InteriorPreflightName(pack.UserId, pack.Id),
                        System.Text.Encoding.UTF8.GetBytes(preflightReport),
                        "application/json", jobToken);

                    await packRepository.UpdatePrintPdfUrlAsync(packId, interiorUrl, jobToken);

                    logger.LogInformation(
                        "Beki pack {PackId}: print interior prepared ({PdfxVersion}, {Intent}) and "
                        + "stored with its preflight report.",
                        packId, BekiPrintPrep.PdfxVersion,
                        bekiOptions.Value.PrintPrep.OutputConditionInfo);
                }
                catch (BekiLayoutException ex)
                    when (ex.FailureCode == CompositeFailureCodes.PrintPreflightFailed)
                {
                    await packRepository.UpdatePrintPdfUrlAsync(packId, null, jobToken);

                    logger.LogWarning(
                        "Beki pack {PackId}: print artifact withheld ({Code}) — {Reason} The "
                        + "parent's digital book is unaffected.",
                        packId, CompositeFailureCodes.PrintPreflightFailed, ex.Message);
                }
            }

            pdfStopwatch.Stop();
            uploadMs += pdfStopwatch.ElapsedMilliseconds;

            stage = "publishing the book";

            // The order record's copy of the canonical title — the same string the cover, the
            // intro and the PDF metadata carry, so an operator reading the order and a parent
            // holding the book are reading about the same object.
            await packRepository.UpdateTitleAsync(packId, plan.Concept.Title, jobToken);

            var content = ProjectForReader(plan, run.ChildName, pack, storedUrls);

            /*
              Completed, but only over the status this job left behind.

              The stale-generation sweep can have reached this pack while the last upload was in
              flight — it fails a book whose row has been silent for the whole budget plus a grace
              period, and a job that took that long and then finished is exactly the case. Writing
              Completed unconditionally would erase that verdict and leave nothing anywhere saying
              the book took forty minutes and was declared lost. The pictures and the PDF are
              already stored either way, so the losing side of this race costs nobody a book: it
              costs a status, and the sweep's is the one with a reason attached.
            */
            /*
              The download column is where the customer-file gate lands.

              Null is not "no book": the pack completes, the reader serves the spreads, and the
              parent's library card is there. It is the downloadable PDF that waits — for a human's
              signature on the rendered contact sheet, or for whichever digital gate is still
              refusing. The file itself is already in storage under its own name, so publishing it
              later is one column write and not a re-run.
            */
            var publishablePdfUrl = release is null || release.CustomerPdfMayPublish ? pdfUrl : null;

            var completed = await packRepository.TryUpdateStatusAsync(
                packId,
                expectedStatus,
                AdventurePackStatus.Completed,
                JsonSerializer.Serialize(content, JsonOptions),
                publishablePdfUrl,
                null,
                jobToken);

            if (completed)
            {
                await packRepository.UpdateProgressAsync(
                    packId, "მზადაა! წიგნი ბიბლიოთეკაშია.", 100, jobToken);

                logger.LogInformation(
                    "Beki pack {PackId} completed from run {RunId}: \"{Title}\", {Spreads} spreads.",
                    packId, runId, plan.Concept.Title, stored.Count);
            }
            else
            {
                logger.LogWarning(
                    "Beki pack {PackId} finished drawing {Spreads} spreads, but its status is no "
                    + "longer {Expected} — the stale-generation sweep or another writer moved it "
                    + "first. Leaving the stored status alone; the PDF and the spreads are saved "
                    + "and the manifest can resume from them.",
                    packId, stored.Count, expectedStatus);

                /*
                  And this is where amendment B6 stops the losing side of that race from costing a
                  family their book.

                  "Leaving the stored status alone" was the honest thing to do while nothing could
                  tell a book that had genuinely died from a book that had merely been slow. It
                  produced a real outcome nobody wanted: a pack with eight spreads, a manifest, both
                  press files and a reading PDF, sitting Failed with a message about a job that had
                  gone silent — because the job had gone silent, for forty minutes, and had then
                  finished. The sweep was right about the silence and wrong about the book.

                  The reconciliation re-verifies the artifacts and reverses only the sweep's own
                  verdict, and only for a book whose files are all there. The burial stays on the
                  record as an alarm either way: a book that takes longer than the whole budget is a
                  fault even when it arrives.
                */
                await ReconcileLostCompletionAsync(pack, expectedStatus, stored.Count);
            }

            // Telemetry last, and best-effort: the order is already complete, and a failed
            // measurement must never look like a failed book. Per-attempt rows come from the
            // generator's own stopwatches, so a first attempt the reviewer refused keeps its
            // verdict here even when the retry passed — the QA failure reasons are the point.
            try
            {
                totalStopwatch.Stop();
                var telemetry = new
                {
                    cover = new
                    {
                        attempts = book.Cover.AttemptDetails.Select(a => new
                        {
                            generationMs = a.GenerationMs,
                            reviewMs = a.ReviewMs,
                            accepted = a.Accepted,
                            issues = a.Accepted ? Array.Empty<string>() : ParseIssues(a.Verdict),
                        }).ToList(),
                        accepted = book.Cover.Accepted,
                        adopted = book.Cover.AttemptDetails.Count == 0,
                    },
                    spreads = book.Spreads.Select(s => new
                    {
                        spreadNumber = s.SpreadNumber,
                        attempts = s.AttemptDetails.Select(a => new
                        {
                            generationMs = a.GenerationMs,
                            reviewMs = a.ReviewMs,
                            accepted = a.Accepted,
                            issues = a.Accepted ? Array.Empty<string>() : ParseIssues(a.Verdict),
                        }).ToList(),
                        accepted = s.Accepted,
                        adoptedFromManifest = s.AttemptDetails.Count == 0,
                    }).ToList(),
                    /*
                      The book-level quality record, as numbers.

                      Here as well as in its own artifact because telemetry is the file that gets
                      read across books — "how often does the pose table fall back", "which packs
                      need a human to read the Georgian" — and those questions are answered by
                      counts, not by prose. The URL is carried with them so a number that looks
                      wrong leads straight to the document that explains it.

                      Counts only, and that is a privacy decision rather than a size one: a Georgian
                      flag's matched text is a window into the story, and the story is where the
                      child's name is — the hyphenated-suffix rule finds precisely that. Telemetry
                      is a comparison document; it gets the rule id and the page. See
                      CompositeBookReview.ToTelemetry.

                      Null on the legacy path, which writes the key with a null rather than omitting
                      it. Left that way deliberately: unlike the fulfilment manifest — which a
                      resumed run deserializes, and where an added property is a compatibility
                      question — nothing anywhere reads telemetry.json back. It is written once and
                      read by people, so a null key on a legacy book is a null key, not a breakage,
                      and the alternative (a second anonymous shape, or a serializer that drops
                      nulls across the whole document) buys nothing for it.
                    */
                    compositeReview = book.Composite?.Review?.ToTelemetry(reviewUrl),
                    /*
                      What the sixteen gates made of this book, as two fields somebody can compare
                      across books.

                      Here as well as in release-gates.json for the reason the review counts are:
                      telemetry is the document that gets read in aggregate — "how many books are
                      waiting on a human", "which gate refuses most often" — and those are questions
                      about counts. Null on the previous path, which has none of the artifacts a
                      verdict is computed from.
                    */
                    release = release is null
                        ? null
                        : new { verdict = release.Verdict, failingGates = release.FailingGates },
                    uploadMs,
                    pdfBuildMs = pdfStopwatch.ElapsedMilliseconds,
                    totalMs = totalStopwatch.ElapsedMilliseconds,
                    totalImageAttempts = book.Cover.AttemptDetails.Count
                        + book.Spreads.Sum(s => s.AttemptDetails.Count),
                    acceptedCount = (book.Cover.Accepted ? 1 : 0) + book.Spreads.Count(s => s.Accepted),
                    needsReviewCount = (book.Cover.Accepted ? 0 : 1) + book.Spreads.Count(s => !s.Accepted),
                };

                await blobStorage.UploadAsync(
                    BekiPackBlobs.TelemetryName(pack.UserId, pack.Id),
                    JsonSerializer.SerializeToUtf8Bytes(telemetry, JsonOptions),
                    "application/json",
                    jobToken);

                logger.LogInformation(
                    "Beki pack {PackId} telemetry: totalMs={TotalMs}, pdfBuildMs={PdfBuildMs}, "
                    + "uploadMs={UploadMs}, imageAttempts={Attempts}, accepted={Accepted}, "
                    + "needsReview={NeedsReview}.",
                    packId, telemetry.totalMs, telemetry.pdfBuildMs, telemetry.uploadMs,
                    telemetry.totalImageAttempts, telemetry.acceptedCount, telemetry.needsReviewCount);
            }
            catch (Exception telemetryEx)
            {
                logger.LogWarning(telemetryEx, "Beki pack {PackId}: telemetry not written.", packId);
            }
        }
        /*
          Cancelled by the host, not by the clock: a deploy, a restart, a scale-in.

          Nothing is wrong with this book, so nothing terminal is written. The exception is
          rethrown, Hangfire sees a failed attempt and requeues it, and the next attempt reads the
          manifest and adopts every spread this one had already drawn and stored. Marking the pack
          Failed here — which is what the catch below used to do, since an OperationCanceledException
          is an Exception like any other — would turn every deployment into a handful of paid books
          declared unmakeable, each of which was one requeue away from finishing.

          The pack is deliberately left in GeneratingStory. That is not a leak: the stale-generation
          sweep is what closes the case if the requeue never comes.
        */
        catch (OperationCanceledException ex) when (!deadline.Expired)
        {
            logger.LogWarning(
                ex,
                "Beki fulfilment for pack {PackId} was stopped by the host while {Stage} "
                + "(cause: {Cause}). The pack is left in {Status} so Hangfire can requeue it and "
                + "the manifest can resume from the spreads already stored.",
                packId, stage, deadline.Cause, expectedStatus);

            throw;
        }
        catch (Exception ex)
        {
            /*
              The composite pipeline stops with one of eight agreed words, and the word is the
              useful half of the failure — it is what decides whether a retry could possibly help,
              and it is what an operator matches against the supplier's own vocabulary. So it is
              put in front of the message rather than left inside an exception property nobody
              downstream unwraps.

              Inert on the legacy path: nothing there throws this type, so the message stored for
              every book in production is the one it always was.

              A cancellation reaching here is the budget's — the filter above sent the host's back
              to Hangfire — and it gets a reason of its own, because "The operation was canceled."
              is exactly the message that made this defect take a day to find.
            */
            var reason = ex switch
            {
                OperationCanceledException => GenerationBudget.ExceededReason(deadline.Budget, stage),
                _ => CodedFailureReason(ex) ?? ex.Message
            };

            logger.LogError(
                ex, "Beki fulfilment failed for pack {PackId} while {Stage}: {Reason}",
                packId, stage, reason);

            // The refused page and its verdicts, stored beside the book so "marked for human
            // review" leaves something a human can actually review. A fresh token on purpose:
            // the budget's may be the very cancellation that caused this failure.
            if (ex is CompositePipelineException { Evidence: { } evidence } && packUserId is { } owner)
            {
                try
                {
                    await blobStorage.UploadAsync(
                        BekiPackBlobs.FailedSpreadName(owner, packId, evidence.Page),
                        evidence.CompositePng, "image/png", CancellationToken.None);

                    await blobStorage.UploadAsync(
                        BekiPackBlobs.SpreadQaName(owner, packId, evidence.Page),
                        System.Text.Encoding.UTF8.GetBytes(evidence.QaJson), "application/json",
                        CancellationToken.None);
                }
                catch (Exception evidenceEx)
                {
                    logger.LogWarning(
                        evidenceEx, "Beki pack {PackId}: could not store the refused spread {Spread}.",
                        packId, evidence.Page);
                }
            }

            /*
              A failure before the row was ever read leaves nothing to compare against.

              That is a real case now that the load is inside this region: the budget can expire
              while the SELECT is outstanding. Guessing a status would either write nothing (a wrong
              guess loses the compare-and-set) or, worse, be right by accident. So it looks — with a
              fresh token, because the one that was in force is the one that just killed the read.
            */
            if (!packWasRead)
            {
                try
                {
                    var current = await packRepository.GetByIdNoOwnershipAsync(
                        packId, CancellationToken.None);

                    if (current is not null)
                    {
                        expectedStatus = current.Status;
                    }
                }
                catch (Exception readEx)
                {
                    logger.LogWarning(
                        readEx, "Beki pack {PackId}: could not re-read the pack to record why it "
                        + "failed; the terminal write will almost certainly find nothing to match.",
                        packId);
                }
            }

            // Compare-and-set for the same reason the Completed write is: the sweep may have got
            // here first, and its verdict — with the reason it recorded — is not worth overwriting
            // with a second copy of the same news. Status and message only, so the spreads, the
            // manifest and the story this attempt did manage to store all survive for the next one.
            var failed = await packRepository.TryFailAsync(
                packId, expectedStatus, reason, CancellationToken.None);

            if (!failed)
            {
                logger.LogWarning(
                    "Beki pack {PackId} could not be marked Failed: its status is no longer "
                    + "{Expected}. Deferring to what is stored.", packId, expectedStatus);
            }

            /*
              Who gets told, and when.

              This book was paid for before the job started, so a failure is money taken with
              nothing delivered — the one generation failure that always needs a person. But
              "always" used to mean literally always, including when the compare-and-set lost
              because the book had in fact been Completed by another writer. That is a page-out
              about a book that exists.

              So: told when this job's verdict stood, and told when it lost to a verdict that is
              also Failed — which is the sweep's, and the sweep tells nobody. A pack that rests in
              any other status is not a failure and nobody is woken for it.
            */
            if (failed || await RestsInFailedAsync(packId))
            {
                await adminNotifier.BookFailedAsync(packId, reason, CancellationToken.None);

                /*
                  The family, on a stricter condition than the operator.

                  An operator can absorb a second page about a book they are already looking at.
                  A parent cannot absorb a second apology — least of all one that explains the
                  failure differently, which is exactly what the losing branch would send: the
                  sweep's verdict is on the row, this job's own reason is in hand, and they are
                  not the same sentence.

                  The only writer that beats this one to Failed is the sweep, and the sweep now
                  writes to the parent itself. So a lost compare-and-set means the letter has
                  already gone, and the right move is to send nothing.
                */
                if (failed)
                {
                    await TellTheParentAsync(packId, childName, reason);
                }
                else
                {
                    logger.LogInformation(
                        "Beki pack {PackId}: the stale-generation sweep recorded this failure "
                        + "first and has already written to the parent, so this job is not "
                        + "sending a second letter.", packId);
                }
            }
            else
            {
                logger.LogWarning(
                    "Beki pack {PackId} failed in this job, but the stored pack is not Failed; "
                    + "another writer got a better outcome, so nobody is being paged.", packId);
            }
        }
    }

    /// <summary>
    /// Which order paid for this book, or null when nothing did.
    ///
    /// The cheapest lookup that already exists: the orders table carries the pack in its own
    /// <c>BookId</c> column and this reads paid-or-fulfilled rows for it, oldest first, which is the
    /// same query the "what does this parent already own" question uses. The first is the one the
    /// console wants — a re-purchase of the same book is a second row about the same artifact, and
    /// the alarm belongs on the order the book was made for.
    ///
    /// Never throws. An alarm is a record of something that already happened; a lookup that failed
    /// must cost it its order link, not its existence.
    /// </summary>
    private async Task<Guid?> OrderIdAsync(Guid packId, CancellationToken cancellationToken)
    {
        if (_order is { } cached && cached.PackId == packId)
        {
            return cached.OrderId;
        }

        Guid? orderId = null;

        if (orders is not null)
        {
            try
            {
                var paid = await orders.GetPaidForBookAsync(packId, cancellationToken);
                orderId = paid.Count > 0 ? paid[0].Id : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex, "Beki pack {PackId}: the order behind this book could not be looked up, so "
                        + "its alarms will not carry an order link.", packId);
            }
        }

        _order = (packId, orderId);

        return orderId;
    }

    /// <summary>
    /// Stores one waived refusal's evidence and raises its alarm — the fulfilment half of the
    /// pipeline's <c>OnPolicyWaiver</c> callback.
    ///
    /// Two blobs and a row. The picture goes where every refused picture has always gone, so that an
    /// operator opening a pack's storage finds the waived page beside the failed ones rather than in
    /// a new place they have to be told about; the document goes to a name of its own, because the
    /// spread's QA record belongs to the reviewer and this is a record of what was decided about it.
    ///
    /// Nothing here may throw. The book is mid-flight and its artwork is good; a storage hiccup that
    /// took it down would be the fault this whole policy exists to remove, arriving through the door
    /// marked "recording that we removed it".
    ///
    /// Internal rather than private so a test can hand it two waivers and look at what is left in
    /// storage. Reaching it through <see cref="ProcessAsync"/> would mean driving a whole book — nine
    /// image calls' worth of doubles — to observe two uploads and one row, and the faults this method
    /// has had (a blob name that collided, an order id that was always null) are exactly the kind a
    /// test at that distance does not see.
    /// </summary>
    internal async Task RecordWaiverAsync(
        Domain.Entities.AdventurePack pack,
        CompositePolicyWaiver waiver,
        CancellationToken cancellationToken)
    {
        var evidenceName = BekiPackBlobs.PolicyWaiverName(
            pack.UserId, pack.Id, waiver.CheckId, waiver.Page);

        try
        {
            /*
              The picture, under a name that says WHICH check refused it.

              It went to the plain FailedSpreadName, and two waived checks on one spread — the centre
              fold and the reviewer's opinion, which is not a rare pair — wrote to the same blob. The
              second upload replaced the first, so one of the two alarms pointed at a picture that
              was no longer the picture it was about, and nothing anywhere said so. The plain name
              stays what it has always been: where a spread that KILLED the book leaves its last
              attempt. (Review finding 5.)
            */
            await blobStorage.UploadAsync(
                BekiPackBlobs.WaivedEvidenceName(
                    pack.UserId, pack.Id, waiver.CheckId, waiver.Page),
                waiver.EvidencePng, "image/png", cancellationToken);

            await blobStorage.UploadAsync(
                evidenceName,
                System.Text.Encoding.UTF8.GetBytes(waiver.EvidenceJson),
                "application/json",
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "Beki pack {PackId}: the {CheckId} waiver's evidence for page {Page} could not "
                    + "be stored. The alarm is still raised.",
                pack.Id, waiver.CheckId, waiver.Page);
        }

        if (alarms is null)
        {
            return;
        }

        await alarms.RaiseAsync(
            new BekiAlarmRaise(
                pack.Id,
                await OrderIdAsync(pack.Id, cancellationToken),
                pack.UserId,
                waiver.CheckId,
                BekiReleaseSeverity.Flag,
                waiver.Page == 0
                    ? $"The cover wrap: {waiver.Detail}. The artwork shipped under the release policy."
                    : $"Spread {waiver.Page}: {waiver.Detail}. The artwork shipped under the release policy.",
                evidenceName,
                // The page is part of the identity: the same check on two spreads is two incidents,
                // and one spread refused on two attempts of the same book is one.
                BekiAlarmEvidence.ForAttempt(waiver.CheckId, waiver.Page)),
            cancellationToken);
    }

    /// <summary>
    /// The rescue for a book that finished and lost the race to say so — amendment B6.
    ///
    /// Best effort and entirely outside the book's own success path: by the time this runs the
    /// spreads, the PDFs and the manifest are all in storage, so the worst case is a pack that stays
    /// Failed and an operator who is told why. A fresh token, because the budget's may be about to
    /// expire and this is the one piece of work that must not be cut short by it.
    /// </summary>
    private async Task ReconcileLostCompletionAsync(
        Domain.Entities.AdventurePack pack, AdventurePackStatus expectedStatus, int spreads)
    {
        if (alarms is not null)
        {
            await alarms.RaiseAsync(
                new BekiAlarmRaise(
                    pack.Id,
                    await OrderIdAsync(pack.Id, CancellationToken.None),
                    pack.UserId,
                    "fulfilment_completion_lost",
                    BekiReleaseSeverity.Blocker,
                    $"This book finished drawing {spreads} spread(s) and could not be marked "
                    + $"Completed: its status was no longer {expectedStatus}. Every artifact is in "
                    + "storage. A reconciliation was attempted; check whether the pack is Completed.",
                    BekiPackBlobs.ManifestName(pack.UserId, pack.Id),
                    BekiAlarmEvidence.ForAttempt("completion-lost", pack.Id)),
                CancellationToken.None);
        }

        if (reconciliation is null)
        {
            return;
        }

        try
        {
            var result = await reconciliation.ReconcilePackAsync(
                pack.Id,
                "the fulfilment job finished after the stale-generation sweep had buried the book",
                CancellationToken.None);

            logger.LogWarning(
                "Beki pack {PackId}: reconciliation after a lost completion said {Outcome} — {Detail}",
                pack.Id, result.Outcome, result.Detail);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex, "Beki pack {PackId}: the reconciliation after a lost completion did not run.",
                pack.Id);
        }
    }

    /// <summary>
    /// Tells the parent their book could not be made, in their own language.
    ///
    /// Best effort in the strongest sense: every step is inside the try, and nothing it can throw
    /// is allowed to reach the caller. By the time this runs the verdict is written and the
    /// operator is paged, so an SMTP timeout must not turn a recorded failure into an unhandled
    /// exception on a Hangfire job that would then retry the whole book.
    ///
    /// The reason is mapped on the way out. What is stored is
    /// <c>IMAGE_QA_FAILED (spread 1): …</c>; what is sent is a Georgian sentence with no code in
    /// it — the same one the parent's screen is showing.
    /// </summary>
    private async Task TellTheParentAsync(Guid packId, string? childName, string reason)
    {
        try
        {
            var pack = await packRepository.GetByIdNoOwnershipAsync(packId, CancellationToken.None);
            if (pack is null)
            {
                return;
            }

            var user = await userRepository.GetByIdAsync(pack.UserId, CancellationToken.None);
            if (user is null || string.IsNullOrWhiteSpace(user.Email))
            {
                logger.LogWarning(
                    "Beki pack {PackId} failed and its owner has no email address on file; only "
                    + "the admin alert went out.", packId);
                return;
            }

            await emailService.SendBookFailedAsync(
                user.Email,
                childName,
                pack.Title,
                ParentFacingFailure.ToParentMessage(reason),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Beki pack {PackId}: the parent could not be told their book failed.", packId);
        }
    }

    /// <summary>
    /// Whether the pack actually rests in Failed, when this job's own attempt to say so lost.
    ///
    /// Best effort by design: if the read itself fails, the answer is "assume it did" and somebody
    /// is told. A spurious page about a book that turned out fine costs an operator a minute; a
    /// silent paid failure costs a family their book.
    /// </summary>
    private async Task<bool> RestsInFailedAsync(Guid packId)
    {
        try
        {
            var current = await packRepository.GetByIdNoOwnershipAsync(packId, CancellationToken.None);
            return current is null || current.Status == AdventurePackStatus.Failed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Beki pack {PackId}: could not confirm the stored status; notifying anyway.", packId);
            return true;
        }
    }

    // ==============================================================================================
    // The audit-2 correction's stages: asset lock, QA evidence, layout receipts, press, renders.
    // ==============================================================================================

    /// <summary>
    /// Proves every fixed asset this book will print, stores the manifest, and hands back the hashes
    /// the fixed-page QA measures placements against.
    /// </summary>
    /// <exception cref="BekiAssetLockException">
    /// <c>ASSET_LOCK_FAILED</c>, before any model call. The exception carries every failure at once
    /// rather than the first, because the answer to "which of my assets are wrong" is a list.
    /// </exception>
    private async Task<IReadOnlySet<string>> VerifyAssetLockAsync(
        Domain.Entities.AdventurePack pack, CancellationToken cancellationToken)
    {
        var options = bekiOptions.Value.PrintPrep;

        var manifest = _assetLock.Verify(new BekiAssetLockInputs
        {
            OutputIntentIccPath = options.OutputIntentIccPath,
            OutputIntentIccSha256 = options.OutputIntentIccSha256,
        });

        await blobStorage.UploadAsync(
            BekiPackBlobs.AssetLockName(pack.UserId, pack.Id),
            System.Text.Encoding.UTF8.GetBytes(manifest.ToJson()),
            "application/json",
            cancellationToken);

        logger.LogInformation(
            "Beki pack {PackId}: asset lock passed — {Count} approved assets from {Registries}.",
            pack.Id, manifest.Assets.Count,
            string.Join(", ", manifest.SourceRegistries.Select(pair => $"{pair.Key} {pair.Value}")));

        return manifest.Assets
            .Select(asset => asset.Sha256)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One spread's final QA verdict, stored beside the picture it judged.
    ///
    /// A drawn page writes what its reviewer said. An adopted page has nothing of its own to write
    /// and is deliberately left alone: its record was written by the attempt that drew it and is
    /// already under this exact name, so overwriting it would replace a real verdict with a blank
    /// and reading it back only to write it again would be a round trip for no one.
    /// </summary>
    private async Task StoreSpreadQaAsync(
        Domain.Entities.AdventurePack pack,
        CompositeSpreadArtifact artifact,
        CancellationToken cancellationToken)
    {
        if (artifact.QaJson is not { Length: > 0 } qa)
        {
            if (artifact.Adopted)
            {
                // Nothing to do and nothing wrong: the stored record stands. Whether it is still
                // there, and still written under a reviewer contract this deployment stands behind,
                // is the VISUAL_QA gate's question rather than this method's.
                return;
            }

            logger.LogWarning(
                "Beki pack {PackId}: spread {Spread} was drawn with no QA document to store; the "
                + "VISUAL_QA gate will refuse the release until one exists.",
                pack.Id, artifact.SpreadNumber);

            return;
        }

        await blobStorage.UploadAsync(
            BekiPackBlobs.SpreadQaName(pack.UserId, pack.Id, artifact.SpreadNumber),
            System.Text.Encoding.UTF8.GetBytes(qa),
            "application/json",
            cancellationToken);
    }

    /// <summary>
    /// One composed document's post-layout receipts — amendment A4: the whole-document file and one
    /// per page, under <c>receipts/</c>.
    ///
    /// They are what the TEXT_LAYER and wash gates read, and nothing upstream can stand in for them.
    /// Pre-layout illustration QA knows what was drawn; only layout knows where the words landed on
    /// it, how they broke, what colour they ended up and whether the cream under them stayed off the
    /// fold. The rejected book's wash crossed the centre fold on Story Spread 4 and no check in the
    /// pipeline could have seen it.
    /// </summary>
    private async Task UploadLayoutReceiptsAsync(
        Domain.Entities.AdventurePack pack,
        string mode,
        BekiLayoutReceipts receipts,
        CancellationToken cancellationToken)
    {
        await blobStorage.UploadAsync(
            BekiPackBlobs.LayoutReceiptName(pack.UserId, pack.Id, mode),
            System.Text.Encoding.UTF8.GetBytes(receipts.ToJson()),
            "application/json",
            cancellationToken);

        foreach (var page in receipts.Pages)
        {
            await blobStorage.UploadAsync(
                BekiPackBlobs.LayoutPageReceiptName(pack.UserId, pack.Id, mode, page.FileName),
                System.Text.Encoding.UTF8.GetBytes(page.ToJson()),
                "application/json",
                cancellationToken);
        }
    }

    /// <summary>
    /// The six pages nobody generated, given the QA record they never had — D7.
    ///
    /// Machine-generated on purpose: there is no model verdict to write down for an endpaper, and
    /// inventing one would be worse than the silence it replaces. What can be said is mechanical and
    /// is exactly what the audit asks — the approved assets the page placed hash to files the lock
    /// proved, the layout receipt exists, and any wash on it clears the fold and the trim.
    /// </summary>
    private async Task StoreFixedPageQaAsync(
        Domain.Entities.AdventurePack pack,
        BekiLayoutReceipts receipts,
        IReadOnlySet<string> lockedAssetHashes,
        CancellationToken cancellationToken)
    {
        foreach (var role in BekiFixedPageQa.Roles)
        {
            if (BekiFixedPageQa.Write(role, receipts, lockedAssetHashes) is not { } document)
            {
                continue;
            }

            await blobStorage.UploadAsync(
                BekiPackBlobs.FixedPageQaName(pack.UserId, pack.Id, role),
                System.Text.Encoding.UTF8.GetBytes(document),
                "application/json",
                cancellationToken);
        }
    }

    /// <summary>Which press files came out of preparation, and which the gates will find absent.</summary>
    private sealed record PressOutcome(string? InteriorUrl, string? CoverUrl);

    /// <summary>
    /// The printer's two files, with the resolution truth attached (D5c, audit P0-04/P1-01).
    ///
    /// The art is offered to the configured super-resolver first, and the receipt records what
    /// actually happened rather than what was wanted: a deployment with no upscaler — the shipped
    /// default — sends the raw art through with a receipt that says so, and print prep refuses the
    /// file on PRESS_RESOLUTION. That refusal is the correct outcome and the audit's own ruling:
    /// 2528×1180 art Lanczos-stretched to 5315×2480 measures 300 PPI and carries 143 PPI of detail,
    /// and passing it was the defect.
    ///
    /// A refusal here withholds press files and nothing else. The parent's book has already shipped
    /// by the time this runs, which is the reason the order was changed.
    /// </summary>
    private async Task<PressOutcome> PreparePressAsync(
        Domain.Entities.AdventurePack pack,
        MasterStory plan,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization personalization,
        byte[] wrapComposite,
        CancellationToken cancellationToken)
    {
        var options = bekiOptions.Value.PrintPrep;
        var failedGates = new List<string>();
        var reasons = new List<string>();
        string? interiorUrl = null;
        string? coverUrl = null;

        // ---- the interior -------------------------------------------------------------------
        var pressArt = new List<BekiSpreadArtwork>(spreads.Count);
        var sources = new List<BekiResolutionSource>(spreads.Count);

        foreach (var spread in spreads)
        {
            var upscale = await _pressUpscaler.UpscaleAsync(
                spread.Image, InteriorPressWidthPx, InteriorPressHeightPx, cancellationToken);

            pressArt.Add(upscale is { Succeeded: true, Png: { Length: > 0 } enlarged }
                ? new BekiSpreadArtwork(spread.SpreadNumber, enlarged)
                : spread);

            sources.Add(upscale.ToReceiptSource($"spread-{spread.SpreadNumber:00}"));

            if (!upscale.Succeeded && upscale.Reason is { Length: > 0 } why)
            {
                logger.LogInformation(
                    "Beki pack {PackId}: spread {Spread} goes to press preparation at its own "
                    + "{Width}×{Height} — {Reason}",
                    pack.Id, spread.SpreadNumber, upscale.SourceWidthPx, upscale.SourceHeightPx, why);
            }
        }

        try
        {
            var interior = composer.ComposeInteriorWithReceipts(plan, pressArt, personalization);

            var (preparedInterior, preflight) = BekiPrintPrep.Prepare(
                interior.Pdf,
                plan.Concept.Title,
                options,
                probe: new BekiPrintProbe(
                    interior.Receipts.LightTextPages, interior.Receipts.FlatGroundTextProbes),
                resolutionReceipt: new BekiResolutionReceipt(sources));

            interiorUrl = await blobStorage.UploadAsync(
                BekiPackBlobs.InteriorPdfName(pack.UserId, pack.Id),
                preparedInterior, "application/pdf", cancellationToken);

            await blobStorage.UploadAsync(
                BekiPackBlobs.InteriorPreflightName(pack.UserId, pack.Id),
                System.Text.Encoding.UTF8.GetBytes(preflight), "application/json", cancellationToken);

            await UploadLayoutReceiptsAsync(pack, "interior", interior.Receipts, cancellationToken);
        }
        catch (BekiLayoutException ex)
        {
            failedGates.AddRange(GatesNamedIn(ex.Message));
            reasons.Add($"interior: {ex.Message}");

            // Symmetrically with the customer PDF above: a retry whose interior now refuses must
            // not leave the previous attempt's preflight standing as this run's evidence.
            await OverwriteStalePreflightAsync(
                BekiPackBlobs.InteriorPreflightName(pack.UserId, pack.Id),
                "preparing the press interior", ex, cancellationToken);

            logger.LogWarning(
                "Beki pack {PackId}: the press interior is withheld ({Code}) — {Reason}",
                pack.Id, ex.FailureCode, ex.Message);
        }

        // ---- the cover ----------------------------------------------------------------------
        try
        {
            var coverUpscale = await _pressUpscaler.UpscaleAsync(
                wrapComposite, CoverPressWidthPx, CoverPressHeightPx, cancellationToken);

            var coverArt = coverUpscale is { Succeeded: true, Png: { Length: > 0 } enlarged }
                ? enlarged
                : wrapComposite;

            var cover = composer.ComposeCoverPressWithReceipts(plan.Concept.Title, coverArt);

            var (preparedCover, coverPreflight) = BekiPrintPrep.Prepare(
                cover.Pdf,
                plan.Concept.Title,
                options,
                // The locked spec sets every box equal to the 512 × 245 canvas: the turn-ins ARE
                // the overrun, so there is no inset to state.
                trimInsetMm: 0f,
                probe: new BekiPrintProbe(
                    cover.Receipts.LightTextPages, cover.Receipts.FlatGroundTextProbes),
                resolutionReceipt: new BekiResolutionReceipt(
                    [coverUpscale.ToReceiptSource("cover-wrap")]));

            coverUrl = await blobStorage.UploadAsync(
                BekiPackBlobs.CoverPdfName(pack.UserId, pack.Id),
                preparedCover, "application/pdf", cancellationToken);

            await blobStorage.UploadAsync(
                BekiPackBlobs.CoverPreflightName(pack.UserId, pack.Id),
                System.Text.Encoding.UTF8.GetBytes(coverPreflight), "application/json",
                cancellationToken);

            await UploadLayoutReceiptsAsync(pack, "cover", cover.Receipts, cancellationToken);
        }
        catch (BekiLayoutException ex)
        {
            failedGates.AddRange(GatesNamedIn(ex.Message));
            reasons.Add($"cover: {ex.Message}");

            await OverwriteStalePreflightAsync(
                BekiPackBlobs.CoverPreflightName(pack.UserId, pack.Id),
                "preparing the press cover", ex, cancellationToken);

            logger.LogWarning(
                "Beki pack {PackId}: the press cover is withheld ({Code}) — {Reason}",
                pack.Id, ex.FailureCode, ex.Message);
        }

        /*
          Why the press files are not there, in a document rather than in a log line.

          Print preparation refuses rather than degrades, so its success leaves a preflight report
          that a gate can read. Its failure used to leave nothing an evaluator running hours later
          could see, and "the report is absent" cannot tell a withheld file from an unattempted one.
        */
        await blobStorage.UploadAsync(
            BekiPackBlobs.PressStatusName(pack.UserId, pack.Id),
            JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    stage = "beki-press-status-v1",
                    recorded_at_utc = DateTime.UtcNow,
                    interior = interiorUrl is null ? "withheld" : "prepared",
                    cover = coverUrl is null ? "withheld" : "prepared",
                    failed_gates = failedGates.Distinct(StringComparer.Ordinal).ToList(),
                    reason = reasons.Count == 0 ? null : string.Join(" ", reasons),
                    upscaler_configured = _pressUpscaler.IsConfigured,
                },
                JsonOptions),
            "application/json",
            cancellationToken);

        return new PressOutcome(interiorUrl, coverUrl);
    }

    /// <summary>
    /// Replaces a preflight report a refused stage did not write, so that nothing an earlier
    /// attempt wrote can be read as this run's evidence.
    ///
    /// Best-effort on purpose: the press stage is already withholding, and a storage hiccup while
    /// recording WHY must not turn a withheld file into a failed book. What it costs when it fails
    /// is a stale report — which the press-status document written moments later also contradicts.
    /// </summary>
    private async Task OverwriteStalePreflightAsync(
        string reportName,
        string stage,
        BekiLayoutException failure,
        CancellationToken cancellationToken)
    {
        try
        {
            await blobStorage.UploadAsync(
                reportName,
                BekiWithheldReport.Bytes(
                    GatesNamedIn(failure.Message).FirstOrDefault() ?? "PRESS_GEOMETRY",
                    stage,
                    failure.Message),
                "application/json",
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex, "Beki: the withheld-preflight record for '{Report}' could not be written; a "
                + "report from an earlier attempt may still be stored under that name.", reportName);
        }
    }

    /// <summary>The press raster targets: 450 × 210 mm and 512 × 245 mm, both at 300 PPI.</summary>
    private const int InteriorPressWidthPx = 5315;

    private const int InteriorPressHeightPx = 2480;

    private const int CoverPressWidthPx = 6047;

    private const int CoverPressHeightPx = 2894;

    /// <summary>
    /// The acceptance-gate ids a print-prep refusal names in its own message, so the withholding
    /// record can say which gate refused rather than only that something did. A message that names
    /// none leaves the gates to report the absent preflight as UNKNOWN, which is the honest answer.
    /// </summary>
    private static IEnumerable<string> GatesNamedIn(string message) =>
        new[]
        {
            BekiPrintPrep.PressResolutionGate,
            BekiPrintPrep.TextColorIntegrityGate,
            "PRESS_GEOMETRY",
            "PRESS_COLOR",
        }.Where(gate => message.Contains(gate, StringComparison.Ordinal));

    /// <summary>
    /// Renders the stored finals back and looks at the pixels — audit P2-6, amendment A8.
    ///
    /// Stored is the word that matters. Everything upstream reasons about a document it built
    /// itself; this takes the bytes out of storage, hands them to two independent interpreters and
    /// scans the credits QR off the rendered page, which is the only way the defect the QR gate
    /// exists for — a code that draws perfectly and resolves to nothing — can be caught at all.
    ///
    /// It also produces the contact sheet the human approval signs, which is why the reading copy is
    /// validated as well as the two press files: the reviewer's fourteen pages include the cover,
    /// and amendment A7 puts the cover's identity and age review inside their scope.
    ///
    /// Best-effort as a stage and strict as evidence: a crash here must not fail a drawn book, and
    /// an absent report is a gate that does not pass.
    /// </summary>
    private async Task ValidateStoredRendersAsync(
        Domain.Entities.AdventurePack pack, CancellationToken cancellationToken)
    {
        var options = bekiOptions.Value.PrintPrep;

        // The credits page carries the one QR the spec allows. It is the second-to-last leaf of the
        // customer's fourteen pages and of the interior's twelve; a press cover has no credits page
        // at all, and asserting a QR on it would be asserting a defect.
        var artifacts = new (string Artifact, string Blob, int? QrPage)[]
        {
            (BekiPackBlobs.InteriorRenderArtifact,
             BekiPackBlobs.FinalPdfName(pack.UserId, pack.Id, BekiPackBlobs.InteriorRenderArtifact),
             BookFormat.SpreadCount + 3),
            (BekiPackBlobs.CoverRenderArtifact,
             BekiPackBlobs.FinalPdfName(pack.UserId, pack.Id, BekiPackBlobs.CoverRenderArtifact),
             null),
            (BekiPackBlobs.DigitalRenderArtifact,
             BekiPackBlobs.FinalPdfName(pack.UserId, pack.Id, BekiPackBlobs.DigitalRenderArtifact),
             BookFormat.SpreadCount + 4),
        };

        foreach (var (artifact, blobName, qrPage) in artifacts)
        {
            try
            {
                if (!await blobStorage.ExistsAsync(blobName, cancellationToken))
                {
                    continue;
                }

                await using var stream = await blobStorage.DownloadAsync(blobName, cancellationToken);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);

                var result = BekiRenderValidation.Validate(
                    buffer.ToArray(), artifact, options,
                    new BekiRenderValidationRequest(QrPage: qrPage));

                await blobStorage.UploadAsync(
                    BekiPackBlobs.RenderReportName(pack.UserId, pack.Id, artifact),
                    System.Text.Encoding.UTF8.GetBytes(result.ReportJson),
                    "application/json", cancellationToken);

                if (result.ContactSheetPng is { Length: > 0 } sheet)
                {
                    await blobStorage.UploadAsync(
                        BekiPackBlobs.ContactSheetName(pack.UserId, pack.Id, artifact),
                        sheet, "image/png", cancellationToken);
                }

                logger.LogInformation(
                    "Beki pack {PackId}: {Artifact} render validation {Verdict}{Failed}.",
                    pack.Id, artifact, result.Verdict,
                    result.FailedGates.Count == 0
                        ? string.Empty
                        : $" — {string.Join(", ", result.FailedGates)}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex, "Beki pack {PackId}: {Artifact} could not be rendered back; the gates will "
                    + "read the absence rather than a pass.", pack.Id, artifact);
            }
        }
    }

    /// <summary>
    /// The two artifacts that must be identified and must never travel — amendment A7.
    ///
    /// A reprint that cannot name the photograph it was drawn from cannot be shown to be the same
    /// book, so the manifest carries the blob reference and the SHA-256 of the bytes at it. The
    /// bytes themselves stay where they are and are excluded from the handback, as they always were.
    /// </summary>
    private async Task<BekiManifestPrivateRefs> PrivateReferencesAsync(
        Domain.Entities.AdventurePack pack,
        string photoBlobUrl,
        string? identitySpecUrl,
        CancellationToken cancellationToken)
    {
        return new BekiManifestPrivateRefs(
            ChildPhotograph: await ReferenceAsync(photoBlobUrl),
            ChildIdentity: await ReferenceAsync(identitySpecUrl));

        async Task<BekiPrivateArtifactReference?> ReferenceAsync(string? storedUrl)
        {
            if (storedUrl is not { Length: > 0 })
            {
                return null;
            }

            try
            {
                var bytes = await blobStorage.DownloadBytesFromStoredUrlAsync(
                    storedUrl, cancellationToken);

                return bytes is { Length: > 0 }
                    ? new BekiPrivateArtifactReference(storedUrl, Sha256Hex(bytes), bytes.Length)
                    : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex, "Beki pack {PackId}: a private artifact could not be hashed for the "
                    + "manifest; its reference is recorded without one.", pack.Id);

                return new BekiPrivateArtifactReference(storedUrl, string.Empty, 0);
            }
        }
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>One string out of a composition receipt, or null when it does not say.</summary>
    private static string? ReceiptValue(string manifestJson, string section, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(manifestJson);

            return document.RootElement.TryGetProperty(section, out var node)
                   && node.ValueKind == JsonValueKind.Object
                   && node.TryGetProperty(property, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Where on the front board the approved pose was placed, as the receipt states it.</summary>
    private static string? ReceiptAnchor(string manifestJson)
    {
        try
        {
            using var document = JsonDocument.Parse(manifestJson);

            if (!document.RootElement.TryGetProperty("beki_layer", out var layer)
                || !layer.TryGetProperty("normalized_anchor", out var anchor)
                || anchor.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return anchor.GetRawText();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Stores one page's composite artifacts: the composition receipt, and the pre-composite base
    /// image the receipt describes.
    ///
    /// The base is stored and not merely used because a resumed run needs it. It is the continuity
    /// reference later spreads reusing the same creature are shown, and the composited page cannot
    /// stand in for it: the composite has the approved Beki pasted onto it, and handing that to an
    /// image model is handing it a picture of Beki — the one image this pipeline exists to never
    /// send.
    /// </summary>
    private async Task<BekiCompositionManifestEntry> StoreCompositionAsync(
        Domain.Entities.AdventurePack pack,
        CompositeSpreadArtifact artifact,
        CancellationToken cancellationToken)
    {
        var receiptUrl = await blobStorage.UploadAsync(
            BekiPackBlobs.CompositionManifestName(pack.UserId, pack.Id, artifact.SpreadNumber),
            System.Text.Encoding.UTF8.GetBytes(artifact.ManifestJson),
            "application/json",
            cancellationToken);

        string? baseUrl = null;
        if (artifact.BasePng is { Length: > 0 })
        {
            baseUrl = await blobStorage.UploadAsync(
                BekiPackBlobs.SpreadBaseName(pack.UserId, pack.Id, artifact.SpreadNumber),
                artifact.BasePng,
                "image/png",
                cancellationToken);
        }

        return new BekiCompositionManifestEntry(
            artifact.SpreadNumber, receiptUrl, artifact.PoseId, artifact.OutputSha256, baseUrl);
    }

    /// <summary>
    /// Reads the manifest back by its bare name — never a stored URL, since the manifest is
    /// never handed to anything outside this job. Absence and any parse failure are treated the
    /// same way: no manifest, so every spread is redrawn. A resumed job must never fail over a
    /// manifest it cannot trust.
    /// </summary>

    /// <summary>
    /// This book's stored child identity spec, or null when there is not one to be had.
    ///
    /// Null for every reason at once — no URL on the manifest, a blob that is gone, a download that
    /// failed — because the caller does the same thing with all three, and it is not a thing to be
    /// done quietly: a stored book with no readable spec has its artwork discarded and is redrawn.
    /// Never throws; a resumed job must not die over a file it can replace.
    /// </summary>
    /// <summary>
    /// The stored failure reason for the exceptions that carry an agreed code, or null for the ones
    /// that do not — where the caller falls back to the bare message, as it always did.
    ///
    /// The code goes first because the reason is read by two audiences who both need it there: it is
    /// written onto the pack, where support reads it, and it is what the admin notification carries.
    /// A sentence like "The approved endpaper pattern is not in the published output." is a fine
    /// second half and a useless first one — every other failure on this path opens with a code
    /// somebody can look up, and a stage that quietly stopped doing that is a stage whose failures
    /// read as unclassified.
    ///
    /// <see cref="BekiLayoutException"/> is the case that was missing. It carries TEXT_OVERFLOW
    /// (a spread's copy will not fit at any permitted size) and LAYOUT_FAILED (a required layout
    /// asset is absent or does not hash to the approved file), and it fell through to the bare
    /// message. It has no page: a layout failure is about the book being assembled, and the
    /// composer's own message names the spread when there is one.
    ///
    /// A method rather than two more arms in the catch's switch so that it can be tested: the switch
    /// lives inside a job that needs a repository, a blob account, a generator and a PDF composer to
    /// reach, and "does a layout failure keep its code" is a question about one string.
    /// </summary>
    public static string? CodedFailureReason(Exception exception) => exception switch
    {
        CompositePipelineException composite =>
            $"{composite.FailureCode}"
            + (composite.Page is { } page ? $" (spread {page})" : string.Empty)
            + $": {composite.Message}",

        BekiLayoutException layout => $"{layout.FailureCode}: {layout.Message}",

        _ => null
    };

    private async Task<string?> TryReadIdentitySpecAsync(
        Guid packId, string? identitySpecUrl, CancellationToken cancellationToken) =>
        await TryReadTextAsync(packId, identitySpecUrl, "child identity spec", cancellationToken);

    /// <summary>
    /// The book-level review an earlier attempt stored, so a resumed run can complete its own
    /// reading with what that attempt observed about the pages this one is adopting.
    ///
    /// Best-effort in the strongest sense: a review that cannot be read is nothing to merge, and
    /// nothing about the book changes. It is not the identity spec — losing that discards artwork,
    /// because pages drawn to two descriptions of one child are two books; losing this loses a note.
    /// </summary>
    private async Task<string?> TryReadReviewAsync(
        Guid packId, string? reviewUrl, CancellationToken cancellationToken) =>
        await TryReadTextAsync(packId, reviewUrl, "composite review", cancellationToken);

    private async Task<string?> TryReadTextAsync(
        Guid packId, string? storedUrl, string what, CancellationToken cancellationToken)
    {
        if (storedUrl is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            var bytes = await blobStorage.DownloadBytesFromStoredUrlAsync(storedUrl, cancellationToken);

            return bytes is { Length: > 0 }
                ? System.Text.Encoding.UTF8.GetString(bytes)
                : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Beki pack {PackId}: could not read the stored {What}.", packId, what);

            return null;
        }
    }

    private async Task<BekiFulfillmentManifest?> TryReadManifestAsync(
        string manifestName, CancellationToken cancellationToken)
    {
        try
        {
            if (!await blobStorage.ExistsAsync(manifestName, cancellationToken))
            {
                return null;
            }

            await using var stream = await blobStorage.DownloadAsync(manifestName, cancellationToken);
            return await JsonSerializer.DeserializeAsync<BekiFulfillmentManifest>(
                stream, JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Beki pack manifest {ManifestName} could not be read; ignoring it.", manifestName);
            return null;
        }
    }

    /// <summary>
    /// Rewritten after every accepted spread: the illustration contract these pictures were drawn
    /// under, plus every accepted entry so far — the ones this run just adopted from an earlier
    /// attempt included. Never after a refusal: a spread that did not pass is not something the
    /// next attempt should adopt either.
    /// </summary>
    /// <param name="scenarioUrl">
    /// Null for a legacy book, and the property is omitted from the JSON when it is — a manifest
    /// written by the path this campaign leaves alone is byte-identical to the ones written before
    /// the composite pipeline existed.
    /// </param>
    /// <param name="identitySpecUrl">
    /// Likewise null and likewise omitted for a legacy book — the identity spec is a composite
    /// artifact and the previous path derives none.
    /// </param>
    /// <param name="compositions">Empty for a legacy book, and likewise omitted.</param>
    /// <param name="reviewUrl">
    /// Null on every write but the last one, and not because it is optional — the review counts
    /// fallbacks across a whole book, so there is nothing true to record until all eight spreads
    /// exist. A mid-run manifest that carried a partial count would be a number an operator could
    /// read and act on.
    /// </param>
    /// <param name="privateRefs">
    /// The normalized story's blob and the two hashed-but-excluded private artifacts (amendment A7).
    /// Null on every write but the last, and omitted from the JSON when so: they are facts about a
    /// finished book, and the mid-run writes have no photograph hash to record because nothing has
    /// read the photograph back yet.
    /// </param>
    private async Task WriteManifestAsync(
        string manifestName,
        IReadOnlyDictionary<int, string> storedUrls,
        IReadOnlyList<string> illustrationContract,
        string? scenarioUrl,
        string? identitySpecUrl,
        BekiCoverRecord? cover,
        IReadOnlyDictionary<int, BekiCompositionManifestEntry> compositions,
        CancellationToken cancellationToken,
        string? reviewUrl = null,
        BekiManifestPrivateRefs? privateRefs = null)
    {
        var manifest = new BekiFulfillmentManifest
        {
            ReviewUrl = reviewUrl,
            IllustrationContract = illustrationContract,
            Entries = storedUrls
                .OrderBy(pair => pair.Key)
                .Select(pair => new BekiFulfillmentManifestEntry(pair.Key, pair.Value))
                .ToList(),
            ScenarioUrl = scenarioUrl,
            IdentitySpecUrl = identitySpecUrl,
            Cover = cover,
            StoryUrl = privateRefs?.StoryUrl,
            ChildPhotograph = privateRefs?.ChildPhotograph,
            ChildIdentity = privateRefs?.ChildIdentity,
            Compositions = compositions.Count == 0
                ? null
                : compositions.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList(),
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        await blobStorage.UploadAsync(manifestName, json, "application/json", cancellationToken);
    }

    /// <summary>
    /// The reviewer's issue strings out of a stored verdict, for telemetry only — parse trouble
    /// yields an empty list, never an exception, because a malformed verdict already cost its
    /// retry and must not also cost the measurement.
    /// </summary>
    private static string[] ParseIssues(string verdict)
    {
        if (string.IsNullOrWhiteSpace(verdict)) return [];

        try
        {
            var extracted = ModelJsonSanitizer.ExtractJsonObject(verdict);
            if (string.IsNullOrEmpty(extracted)) return [];

            using var document = JsonDocument.Parse(extracted);
            if (document.RootElement.TryGetProperty("issues", out var issues)
                && issues.ValueKind == JsonValueKind.Array)
            {
                return issues.EnumerateArray()
                    .Select(issue => issue.GetString() ?? string.Empty)
                    .Where(issue => !string.IsNullOrEmpty(issue))
                    .ToArray();
            }
        }
        catch (JsonException)
        {
        }

        return [];
    }

    /// <summary>
    /// The legacy projection, with every picture page pointed at its stored spread. The reader
    /// rewrites these keys into its own illustration endpoint, exactly as it does for books the
    /// old pipeline drew. The keys are the ones storage handed back at upload, verbatim.
    /// </summary>
    private static AdventureContentDto ProjectForReader(
        MasterStory plan,
        string childName,
        Domain.Entities.AdventurePack pack,
        IReadOnlyDictionary<int, string> storedUrls)
    {
        var content = MasterStoryProjection.ToContent(plan, childName, pack.Theme.ToString());

        var spreadNumber = 0;
        foreach (var page in content.StoryPages)
        {
            if (page.IsTextOnlyPage)
            {
                continue;
            }

            spreadNumber++;
            if (storedUrls.TryGetValue(spreadNumber, out var url))
            {
                page.IllustrationUrl = url;
            }
        }

        return content;
    }
}
