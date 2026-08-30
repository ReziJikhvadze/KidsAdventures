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
    /// The cover this pack shipped, stored beside its spreads.
    ///
    /// Under the pack's own prefix rather than the preview run's, because from v1.2 they are not
    /// always the same picture: the cover is redrawn against the book's accepted first spread, and
    /// the run's copy is what the parent previewed. Storing it here is also what makes a finished
    /// pack directory a complete book — cover, eight spreads and their receipts — rather than eight
    /// spreads and a pointer to somebody else's blob.
    /// </summary>
    public static string CoverName(Guid userId, Guid packId) => $"{userId}/{packId}/cover.png";

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
    TimeProvider? timeProvider = null) : IBekiPackFulfillment
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

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

            // The picture the PDF is laid out from. Normally the cover this run produced; on a
            // resume that adopted everything, the redrawn cover an earlier run stored — so the
            // printed book and the on-screen book stay the same book.
            var coverImage = book.Cover.Image;

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
                    if (compositions.ContainsKey(artifact.SpreadNumber))
                    {
                        continue;
                    }

                    compositions[artifact.SpreadNumber] =
                        await StoreCompositionAsync(pack, artifact, jobToken);
                }

                /*
                  The cover, stored with the book rather than borrowed from the preview run.

                  It is a different picture now: drawn again after the first spread was accepted,
                  with the child's identity lock in the prompt and that accepted spread attached as
                  the appearance anchor, then reviewed against the spec — which is the one image in
                  a Beki book that had never been reviewed for identity at all, and the one the
                  owner watched lose the eye colour on almost every book.

                  A redraw that was refused twice leaves the previewed cover in place and says so on
                  the record, because a book must not die for its cover. The two cases are told
                  apart by whether anything was actually attempted: an adopted cover has no attempt
                  rows, and its verdict is null rather than blank — nobody reviewed it, and an empty
                  string in that field would read as a pass.
                */
                var redrawn = book.Cover.AttemptDetails.Count > 0;

                /*
                  A resumed run must not undo an earlier run's redrawn cover.

                  The redraw happens only on a run that draws the first spread, so a resume that
                  adopted all eight pages produces no redraw and hands back the previewed cover.
                  Uploading that over the stored one would replace a reviewed cover with an
                  unreviewed one, and rewriting the record would tell an operator the opposite of
                  what happened — on a pack whose reader is already pointing at the good picture.
                  So a stored redraw stands, and the run reads it back for the PDF instead, which
                  is what keeps the printed book and the on-screen book the same book.
                */
                if (!redrawn && coverRecord is { IsRedraw: true } storedCover)
                {
                    try
                    {
                        var keptCover = await blobStorage.DownloadBytesFromStoredUrlAsync(
                            storedCover.StoredUrl, jobToken);

                        if (keptCover is { Length: > 0 })
                        {
                            coverImage = keptCover;
                        }
                    }
                    catch (Exception ex)
                    {
                        // The PDF falls back to the previewed cover for this attempt; the reader
                        // and the record still point at the redrawn one, which is the better half
                        // to keep when only one can be had.
                        logger.LogWarning(
                            ex, "Beki pack {PackId}: the stored redrawn cover could not be read; "
                            + "this attempt lays out the previewed cover instead.", packId);
                    }

                    logger.LogInformation(
                        "Beki pack {PackId}: keeping the cover an earlier attempt redrew and "
                        + "reviewed.", packId);
                }
                else
                {
                    var coverUrl = await blobStorage.UploadAsync(
                        BekiPackBlobs.CoverName(pack.UserId, pack.Id),
                        book.Cover.Image,
                        "image/png",
                        jobToken);

                    coverRecord = new BekiCoverRecord(
                        coverUrl,
                        redrawn
                            ? CompositeIllustrationPrompt.CoverRedrawVersion
                            : BekiCoverRecord.AdoptedPreviewCover,
                        redrawn ? book.Cover.Verdict : null);
                }

                /*
                  And the reader is pointed at it, which is the half that was missing.

                  The pack's own cover column is what the library card and the reader serve, and it
                  has held the preview run's cover since purchase — so the PDF shipped the redrawn
                  cover while the screen kept showing the one drawn before the child had a spec.
                  The owner's first check is the on-screen book, so the two have to agree.

                  Only for a redraw. An adopted cover IS the preview run's cover, and re-pointing
                  the column at a copy of it would change nothing except which blob a reader that
                  has cached the first one has to fetch.
                */
                if (coverRecord is { IsRedraw: true } shipped)
                {
                    await packRepository.UpdateBookPresentationAsync(
                        packId, title: null, coverImageUrl: shipped.StoredUrl, jobToken);
                }

                logger.LogInformation(
                    "Beki pack {PackId}: cover stored — {Provenance}.",
                    packId,
                    redrawn
                        ? $"redrawn against the accepted first spread and reviewed ({book.Cover.Verdict})"
                        : "the previewed cover, adopted unchanged");

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

            // Everything ships. A NEEDS_REVIEW spread is a picture a human should look at, not a
            // hole in a paid book — the warning above is the trail. The callback has already
            // stored each spread; this pass only catches one it somehow missed.
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
            var pdf = composer.Compose(plan, coverImage, stored, personalization);
            pdfStopwatch.Stop();

            var pdfUploadStopwatch = Stopwatch.StartNew();
            var pdfUrl = await blobStorage.UploadAsync(
                $"{pack.UserId}/{pack.Id}.pdf", pdf, "application/pdf", jobToken);
            uploadMs += pdfUploadStopwatch.ElapsedMilliseconds;

            /*
              Two shelves, two files — the supplier's audit ended the era of one blob serving
              both. The full document above, cover faces and all, stays the parent's reading
              copy. The print deliverable is the interior alone: the production cover is a
              continuous back-spine-front wrap built from the printer's dieline, which this
              deployment does not have, and the audit's ruling is that the 14-page hybrid must
              never stand in for it.

              And the interior only earns the print slot through the print-preparation stage —
              PDF/X-4 identification, the Coated FOGRA39 output intent, a preflight report. If
              that stage refuses (today it does: the ICC profile is owner item 4), the slot is
              cleared rather than pointed at a layout export, because "layout export treated as
              completed print preparation" is a finding this book already has. The parent's
              digital book ships either way.
            */
            var interior = composer.ComposeInterior(plan, stored, personalization);

            try
            {
                var (preparedInterior, preflightReport) = BekiPrintPrep.Prepare(
                    interior, plan.Concept.Title, bekiOptions.Value.PrintPrep);

                var interiorUrl = await blobStorage.UploadAsync(
                    $"{pack.UserId}/{pack.Id}-interior.pdf",
                    preparedInterior, "application/pdf", jobToken);

                await blobStorage.UploadAsync(
                    $"{pack.UserId}/{pack.Id}-interior-preflight.json",
                    System.Text.Encoding.UTF8.GetBytes(preflightReport),
                    "application/json", jobToken);

                await packRepository.UpdatePrintPdfUrlAsync(packId, interiorUrl, jobToken);

                logger.LogInformation(
                    "Beki pack {PackId}: print interior prepared ({PdfxVersion}, {Intent}) and "
                    + "stored with its preflight report; the print cover stays withheld ({Code}: "
                    + "no printer-approved cover dieline is configured).",
                    packId, BekiPrintPrep.PdfxVersion,
                    bekiOptions.Value.PrintPrep.OutputConditionInfo,
                    CompositeFailureCodes.LayoutFailed);
            }
            catch (BekiLayoutException ex)
                when (ex.FailureCode == CompositeFailureCodes.PrintPreflightFailed)
            {
                await packRepository.UpdatePrintPdfUrlAsync(packId, null, jobToken);

                logger.LogWarning(
                    "Beki pack {PackId}: print artifact withheld ({Code}) — {Reason} The parent's "
                    + "digital book is unaffected.",
                    packId, CompositeFailureCodes.PrintPreflightFailed, ex.Message);
            }

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
            var completed = await packRepository.TryUpdateStatusAsync(
                packId,
                expectedStatus,
                AdventurePackStatus.Completed,
                JsonSerializer.Serialize(content, JsonOptions),
                pdfUrl,
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
                    uploadMs,
                    pdfBuildMs = pdfStopwatch.ElapsedMilliseconds,
                    totalMs = totalStopwatch.ElapsedMilliseconds,
                    totalImageAttempts = book.Cover.AttemptDetails.Count
                        + book.Spreads.Sum(s => s.AttemptDetails.Count),
                    acceptedCount = (book.Cover.Accepted ? 1 : 0) + book.Spreads.Count(s => s.Accepted),
                    needsReviewCount = (book.Cover.Accepted ? 0 : 1) + book.Spreads.Count(s => !s.Accepted),
                };

                await blobStorage.UploadAsync(
                    $"{pack.UserId}/{pack.Id}/telemetry.json",
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
    private async Task WriteManifestAsync(
        string manifestName,
        IReadOnlyDictionary<int, string> storedUrls,
        IReadOnlyList<string> illustrationContract,
        string? scenarioUrl,
        string? identitySpecUrl,
        BekiCoverRecord? cover,
        IReadOnlyDictionary<int, BekiCompositionManifestEntry> compositions,
        CancellationToken cancellationToken,
        string? reviewUrl = null)
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
