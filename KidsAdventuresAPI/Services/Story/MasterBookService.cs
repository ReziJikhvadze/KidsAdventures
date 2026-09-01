using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Prompts;
using Hangfire;

namespace AdventurePacks.Api.Services.Story;

public interface IMasterBookService
{
    /// <summary>
    /// Accepts the request and hands back an id to watch. Returns in about as long as it takes
    /// to describe the photograph; the book itself is written by <see cref="WriteBookAsync"/>.
    /// </summary>
    Task<Guid> StartAsync(GuestPreviewInput input, CancellationToken cancellationToken);

    /// <summary>The job. Public because Hangfire resolves and calls it by expression.</summary>
    Task WriteBookAsync(Guid runId, CancellationToken cancellationToken);

    Task<MasterStoryRun?> GetAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>Status only. What the polling client asks for, several times a minute.</summary>
    Task<MasterStoryRunProgress?> GetProgressAsync(Guid runId, CancellationToken cancellationToken);
}

/// <summary>
/// Writes a whole book out of band and lets the browser watch.
///
/// The split exists because of a hard limit rather than a preference: a sixteen-page book takes
/// minutes to write, and Azure App Service closes an inbound request at 230 seconds. Anything
/// that writes a book inside the request that asked for it fails in production and works
/// everywhere else, which is the worst way for it to fail.
///
/// So the request does only what is quick — describe the photograph, park it, record the ask —
/// and a job does the rest, reporting progress into the same row the client polls.
/// </summary>
public sealed class MasterBookService(
    IMasterStoryRunRepository runRepository,
    IMasterStoryService masterStoryService,
    IOpenAiService openAiService,
    IBlobStorageService blobStorageService,
    IReferenceImageNormalizer referenceImageNormalizer,
    IBackgroundJobClient backgroundJobClient,
    IBekiBookGenerator bekiBookGenerator,
    IOptions<BekiOptions> bekiOptions,
    ILogger<MasterBookService> logger,
    TimeProvider? timeProvider = null) : IMasterBookService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// How long an unclaimed guest run is kept. Long enough to finish, be read, and survive the
    /// sign-up round trip; short enough that a visitor who walks away leaves nothing behind.
    /// </summary>
    private static readonly TimeSpan GuestRunLifetime = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Marks the first attempt apart from its corrective retry in the stored prompts.</summary>
    private const string RetrySeparator = "\n\n===== CORRECTIVE RETRY =====\n\n";

    public async Task<Guid> StartAsync(GuestPreviewInput input, CancellationToken cancellationToken)
    {
        var language = string.IsNullOrWhiteSpace(input.StoryLanguage) ? "ka" : input.StoryLanguage.Trim();
        var runId = Guid.NewGuid();

        // Describing the photo is a short call and its answer is text, so it happens here where
        // the parent is still watching. The portrait itself is parked for the illustration step,
        // which runs later and draws a better likeness from the face than from a paragraph
        // about it.
        string? appearance = null;
        string? photoBlobUrl = null;

        if (input.PhotoBytes is { Length: > 0 })
        {
            try
            {
                appearance = await openAiService.DescribeCharacterFromPhotoAsync(
                    input.PhotoBytes,
                    input.PhotoContentType,
                    MasterStoryPrompt.PhotoDescribe,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Photo description failed for run {RunId}; continuing without it.", runId);
            }

            try
            {
                photoBlobUrl = await blobStorageService.UploadAsync(
                    $"master-runs/{runId:N}/portrait",
                    input.PhotoBytes,
                    input.PhotoContentType,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // Not fatal. Without the portrait the hero is drawn from the written description,
                // which is how the very first version of this worked.
                logger.LogWarning(ex, "Could not park the portrait for run {RunId}.", runId);
            }
        }

        var run = new MasterStoryRun
        {
            Id = runId,
            Status = MasterStoryRunStatus.Pending,
            ProgressMessage = null,
            ChildName = input.ChildName.Trim(),
            BirthDate = input.BirthDate,
            Age = input.Age,
            Gender = string.IsNullOrWhiteSpace(input.Gender) ? string.Empty : input.Gender.Trim(),
            Theme = input.Theme.ToString(),
            EyeColor = input.EyeColor,
            ExtraWishes = input.OptionalStoryNotes,
            AppearanceDescription = appearance,
            PhotoBlobUrl = photoBlobUrl,
            StoryLanguage = language,
            SpreadCount = BookFormat.SpreadCount,
            ExpiresAt = DateTime.UtcNow.Add(GuestRunLifetime)
        };

        await runRepository.CreateAsync(run, cancellationToken);

        // CancellationToken.None: the job must outlive the request that asked for it. Passing the
        // request's token would cancel the book the moment the browser disconnected.
        backgroundJobClient.Enqueue<IMasterBookService>(service =>
            service.WriteBookAsync(runId, CancellationToken.None));

        return runId;
    }

    public async Task WriteBookAsync(Guid runId, CancellationToken cancellationToken)
    {
        /*
          The same wall clock the fulfilment job runs under, for the same reason.

          A preview is cheaper than a purchased book but it fails the same way: the story call and
          the cover call are both minutes long, both retry, and both can sleep on a provider's
          Retry-After. A run that hangs inside one of them used to sit in Writing forever with a
          browser polling it, because the only thing that ever wrote a terminal status was the
          catch below — and nothing was going to throw.

          Which token fired decides what that means. A deadline that fires is a preview that is not
          coming; the host's token firing is a deployment, and the run has a resume path a few lines
          down that starts at the cover rather than rewriting the book.
        */
        using var deadline = GenerationBudget.Start(
            cancellationToken, GenerationBudget.For(bekiOptions.Value), _timeProvider);
        var jobToken = deadline.Token;

        // Where the job was when it stopped. A cancelled await leaves an exception that names
        // neither the call nor the stage, and "the story" and "the cover" are answered very
        // differently by whoever reads the log.
        var stage = "loading the run";

        // Where a preview's minutes actually go. Asked often enough — and guessed at often
        // enough — to be worth measuring rather than reasoning about.
        var started = System.Diagnostics.Stopwatch.StartNew();
        var writingDoneMs = 0L;

        /*
          Once the story is saved the run is a finished preview, cover or no cover.

          The reader opens on the world's own artwork and swaps in the real cover when it lands, so
          a run that is Ready without one is a supported state rather than a broken book. That is
          what makes the flag worth keeping: a deadline that expires while the cover is being drawn
          must not roll a complete story back to Failed over the one part of the job that was always
          allowed to be missing.
        */
        var storyIsSaved = false;

        /*
          The load, and the resume branch, are inside the guarded region — which is not where they
          started.

          Both sat above the try, under the budget's token, so a deadline expiring in either threw
          past every handler below: no terminal status, no classification of the cause, and a
          Hangfire retry that would do the same thing again while a browser kept polling a run that
          said Writing.
        */
        try
        {
            var run = await runRepository.GetByIdAsync(runId, jobToken);
            if (run is null)
            {
                logger.LogWarning("Master story run {RunId} vanished before the job started.", runId);
                return;
            }

            // A book already written is never written twice.
            //
            // Hangfire re-queues a job whose process died, and this one dies in the most expensive
            // place available: mid-way through a call that takes minutes and costs real money. On a
            // deploy or a restart the retry would start again from the top and buy a second book —
            // and hand the parent a different story from the one they had been waiting for.
            //
            // Exceptions do not reach this path, because the catch below marks the run failed and
            // swallows them, so Hangfire never sees a failure to retry — with one deliberate
            // exception, added with the budget: a job stopped because the host is going away is
            // rethrown, which is precisely the case this branch then answers on the next attempt.
            if (!string.IsNullOrWhiteSpace(run.StoryJson) && !string.IsNullOrWhiteSpace(run.ContentJson))
            {
                logger.LogInformation(
                    "Run {RunId} already has its story; resuming at the cover rather than rewriting it.",
                    runId);

                // The story is on the row already, so this branch can only ever be finishing the
                // cover — and the handlers below must treat it that way.
                storyIsSaved = true;
                stage = "finishing the cover of a resumed run";

                await ResumeAtCoverAsync(run, jobToken);
                return;
            }

            stage = "writing the story";

            await runRepository.SetProgressAsync(
                runId,
                MasterStoryRunStatus.Writing,
                "ვწერთ შენს ზღაპარს… ეს რამდენიმე წუთს გრძელდება.",
                jobToken);

            if (!Enum.TryParse<ThemeType>(run.Theme, ignoreCase: true, out var theme))
            {
                throw new InvalidOperationException($"Run {runId} carries an unknown theme '{run.Theme}'.");
            }

            var storyInput = new MasterStoryInput
            {
                ChildName = run.ChildName,
                Age = run.Age,
                Gender = run.Gender,
                Theme = theme,
                EyeColor = run.EyeColor ?? string.Empty,
                ExtraWishes = run.ExtraWishes,
                AppearanceDescription = run.AppearanceDescription,
                SpreadCount = run.SpreadCount,
                Language = run.StoryLanguage
            };

            /*
              Which planner writes this preview.

              The composite pipeline draws the book the parent buys, and the fulfilment job adopts
              the story written here rather than rewriting it — the parent read this story and paid
              for it. So if the pictures are going to be composite pictures, the story has to be a
              composite story, and this is the only moment it can be: by the time the job runs, the
              plan is a row in the database.

              Gated on the printing format as well as on the flag, because the composite pipeline
              only ever draws a book that routes to the Beki fulfilment job, and that routing is
              BookFormat.IsPrintPlan over the version stored below. An A5 preview is not a book this
              pipeline will ever touch.

              And gated on the photograph having actually parked, which is the subtle one.
              CreateAsync deliberately lets a preview continue when the portrait upload fails — a
              book with a generic hero beats no book — and such a run reaches here with no
              PhotoBlobUrl. The composite plan is written for a pipeline that gets the child's
              likeness from the photograph, so it carries no characterLock at all; but at purchase
              BekiRunForAsync refuses the Beki route without a photo URL, and the run falls to the
              legacy generator, which would then have neither a photograph NOR an appearance
              description NOR a characterLock to draw a child from. The result is a paid book about
              nobody in particular. So a run whose portrait did not park is written by the legacy
              planner, whose identity chain — appearance description into characterLock into every
              prompt — is the only one that still works without a picture.

              Null when any of the four is off, and null is what keeps every book in production on
              the planner it has always had.

              And gated on the book format switch, for the same reason as everything else in this
              condition: BekiRunForAsync requires BookFormatEnabled before it will send a purchase
              to the Beki fulfilment job. With the composite flag on and the format switch off, a
              composite-planned preview is bought and then drawn by the legacy A5 generator — a
              parent reads one book and receives another.
            */
            var portraitParked = !string.IsNullOrWhiteSpace(run.PhotoBlobUrl);

            var compositeStoryInput =
                bekiOptions.Value.CompositePipelineEnabled
                && bekiOptions.Value.BookFormatEnabled
                && BookFormat.IsPrintPlan(masterStoryService.PromptVersion)
                && portraitParked
                    ? CompositeStoryInputFor(runId, storyInput)
                    : null;

            if (bekiOptions.Value.CompositePipelineEnabled && !portraitParked)
            {
                logger.LogWarning(
                    "Run {RunId}: the composite pipeline is on, but this run has no parked "
                    + "portrait, so the purchase cannot take the Beki route and the book will be "
                    + "drawn by the legacy path. Writing it with the legacy planner, which is the "
                    + "only one whose plan carries an appearance description and a character lock "
                    + "to draw a child from without a photograph.",
                    runId);
            }

            // The prompts are stored before the call rather than after, so that a call which
            // times out still leaves behind what it was asked to do.
            var (systemPrompt, userPrompt) = compositeStoryInput is null
                ? masterStoryService.BuildPrompts(storyInput)
                : (MasterStoryPromptComposite.System(compositeStoryInput),
                   MasterStoryPromptComposite.User(compositeStoryInput));
            await runRepository.SavePromptsAsync(
                runId,
                masterStoryService.ModelName,
                masterStoryService.PromptVersion,
                systemPrompt,
                userPrompt,
                jobToken);

            // Kept in step with what was just persisted: `run` was loaded once, above, before
            // this call wrote its version to the database, and DrawCoverAsync below reads
            // run.PromptVersion to decide whether this preview gets the Beki cover. Without this,
            // that check would read the stale, pre-save value — null on a run's first attempt —
            // and a printing-format preview would silently fall back to the legacy cover every time.
            /*
              Still the configured version, even when the composite prompt wrote the book.

              It reads as a lie and is not one. This column is not a record of which prompt ran —
              the SystemPrompt and UserPrompt columns saved above and below carry the prompt itself,
              verbatim, which is better evidence than a version string. What this column *is* is the
              routing key: BookFormat.IsPrintPlan reads it to decide whether a purchased pack goes to
              the Beki fulfilment job, it recognises exactly "v5" and "v6", and a run stamped
              "composite-v1" would be routed to the legacy A5 generator — the one path the composite
              pipeline can never be reached from. So the version stays what it says it is, and the
              log line below records which planner actually wrote the story.
            */
            run.PromptVersion = masterStoryService.PromptVersion;

            var result = compositeStoryInput is null
                ? await masterStoryService.WriteAsync(storyInput, jobToken)
                : await masterStoryService.WriteCompositePlanAsync(
                    compositeStoryInput, [], jobToken);

            logger.LogInformation(
                compositeStoryInput is not null
                    ? "Run {RunId}: written by {Prompt} for the composite pipeline; stored under "
                      + "prompt version {Version} so the pack still routes to the Beki fulfilment job."
                    : "Run {RunId}: written by the configured planner ({Prompt} was not selected); "
                      + "prompt version {Version}.",
                runId,
                compositeStoryInput is not null
                    ? MasterStoryPromptComposite.Version
                    : "the composite planner",
                masterStoryService.PromptVersion);

            // The printing schema enforces shape but not the rules that only make sense reading
            // the whole plan together — Beki spelled the one way everything downstream expects,
            // Beki not quietly missing from the spreads the format promises. A plan that fails
            // this is worth one retry with the problems spelled out: a plan the parent never sees
            // costs far less than a book that ships without it.
            //
            // The gate is the printing book format, not a version equality: every version that
            // writes a cast list and per-spread placement is validated the same way.
            if (BookFormat.IsPrintPlan(masterStoryService.PromptVersion))
            {
                var problems = PlanProblems(result.Story, storyInput, compositeStoryInput is not null);
                if (problems.Count > 0)
                {
                    logger.LogWarning(
                        "Run {RunId}: the Beki plan failed validation, retrying once: {Problems}",
                        runId, string.Join(" | ", problems));

                    // The correction goes back to whichever planner wrote the draft. Sending a
                    // composite plan's problems to the v5/v6 retry would answer them with a v6
                    // plan — English copy, Extra Wish, an eye colour, a leaf spirit — and the book
                    // would ship written by the prompt the composite path exists to avoid.
                    var retried = compositeStoryInput is null
                        ? await masterStoryService.RetryPlanWithCorrectionsAsync(
                            storyInput, problems, jobToken)
                        : await masterStoryService.WriteCompositePlanAsync(
                            compositeStoryInput, problems, jobToken);

                    // Both attempts were paid for; the run's token accounting AND its stored
                    // prompts report both, not just the attempt that happened to survive. The
                    // first attempt's prompts embed the draft the retry was correcting, and
                    // nothing else retains that draft — dropping them would leave a paid call
                    // with no record of what it was asked.
                    result = retried with
                    {
                        SystemPrompt = result.SystemPrompt + RetrySeparator + retried.SystemPrompt,
                        UserPrompt = result.UserPrompt + RetrySeparator + retried.UserPrompt,
                        PromptTokens = result.PromptTokens + retried.PromptTokens,
                        CompletionTokens = result.CompletionTokens + retried.CompletionTokens,
                    };

                    var stillWrong = PlanProblems(
                        result.Story, storyInput, compositeStoryInput is not null);
                    if (stillWrong.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"The Beki plan for run {runId} is still invalid after a retry: "
                            + string.Join("; ", stillWrong));
                    }
                }
            }

            // Saved again now that `result` is final, because the prompts stored before the call
            // are only the first half of what was actually asked. A version that writes in two
            // calls — the plan and then the editing pass — and a run that needed a corrective
            // retry both end up asking for things that could not be known in advance, and the
            // pre-call row would silently claim they never happened. The earlier save stays where
            // it is: a call that times out must still leave behind what it was asked.
            await runRepository.SavePromptsAsync(
                runId,
                result.Model,
                masterStoryService.PromptVersion,
                result.SystemPrompt,
                result.UserPrompt,
                jobToken);

            var content = MasterStoryProjection.ToContent(result.Story, run.ChildName, run.Theme);

            await runRepository.SaveStoryAsync(
                runId,
                JsonSerializer.Serialize(result.Story, JsonOptions),
                JsonSerializer.Serialize(content, JsonOptions),
                result.PromptTokens,
                result.CompletionTokens,
                jobToken);

            writingDoneMs = started.ElapsedMilliseconds;
            logger.LogInformation(
                "Run {RunId}: story written in {Seconds:F1}s.", runId, writingDoneMs / 1000.0);

            // Ready means the story is written, not that the picture is painted.
            //
            // The cover used to be drawn first, so a parent whose book had been finished for a
            // minute was still watching a loading screen while an image model worked. The reader
            // opens on the world's own artwork and swaps in the real cover when it lands, which
            // costs a late change of picture and saves everyone that minute.
            await runRepository.MarkReadyAsync(
                runId,
                JsonSerializer.Serialize(content, JsonOptions),
                jobToken);

            // From here the expensive half is on the row and the run is a book somebody can read.
            storyIsSaved = true;

            // The cover is the book's cover, not page one. Writing it onto the first page would
            // overwrite that page's own illustration — which has its own prompt and its own
            // moment in the story — and quietly leave the book with eight pictures instead of
            // nine.
            stage = "drawing the cover";

            var coverUrl = await DrawCoverAsync(run, result.Story, jobToken);
            if (coverUrl is not null)
            {
                await runRepository.SaveCoverAsync(runId, coverUrl, jobToken);
            }

            logger.LogInformation(
                "Run {RunId} ready in {Total:F1}s — story {Story:F1}s, cover {Cover:F1}s: \"{Title}\".",
                runId,
                started.ElapsedMilliseconds / 1000.0,
                writingDoneMs / 1000.0,
                (started.ElapsedMilliseconds - writingDoneMs) / 1000.0,
                result.Story.Concept.Title);
        }
        /*
          Stopped by the host rather than by the clock.

          Rethrown, so Hangfire requeues it. That is not a lost preview: the next attempt reads the
          run back, finds the story if it was already saved, and resumes at the cover — the branch
          a hundred lines above, which existed for exactly this case and could never be reached
          while every cancellation was being swallowed into a terminal Failed.
        */
        catch (OperationCanceledException ex) when (!deadline.Expired)
        {
            logger.LogWarning(
                ex,
                "Master story run {RunId} was stopped by the host while {Stage} (cause: {Cause}); "
                + "leaving it unfinished so Hangfire can requeue it.",
                runId, stage, deadline.Cause);

            throw;
        }
        /*
          Out of time, but only for the cover.

          The story is written, projected and on the row, and the run is already Ready — a preview
          the parent can open and read. Rolling that back to Failed because the picture on the front
          took the last of the half hour would throw away the expensive part over the cheap one,
          which is the rule this flow has always had: the reader opens on the world's own artwork
          and swaps in the real cover when it lands.

          Only the budget lands here. A host cancellation goes back to Hangfire above, and the next
          attempt takes the resume branch and draws the cover properly.
        */
        catch (OperationCanceledException ex) when (storyIsSaved)
        {
            logger.LogWarning(
                ex,
                "Master story run {RunId} ran out of its {Minutes:0}-minute budget while {Stage}. "
                + "The story is saved and the run stays Ready; it simply has no cover of its own.",
                runId, deadline.Budget.TotalMinutes, stage);
        }
        catch (Exception ex)
        {
            // A cancellation reaching here is the deadline's — the filter above returned the
            // host's to Hangfire — and it gets a reason that says so, rather than the bare
            // "The operation was canceled." that a cancelled await leaves behind.
            var reason = ex is OperationCanceledException
                ? GenerationBudget.ExceededReason(deadline.Budget, stage)
                : ex.Message;

            logger.LogError(ex, "Master story run {RunId} failed while {Stage}: {Reason}", runId, stage, reason);
            await runRepository.MarkFailedAsync(runId, reason, CancellationToken.None);
        }
    }

    /// <summary>
    /// The four fields the composite planner may see, mapped from the preview's own input — or null
    /// when this purchase cannot be mapped.
    ///
    /// Null rather than a throw, and the fallback is the legacy planner. An unmappable value here is
    /// almost always an old gender or theme spelling on a stored row, and the composite pipeline's
    /// own boundary will refuse it again at fulfilment time with INVALID_BOOK_INPUT, where the
    /// refusal is about a book somebody paid for. Failing a free preview over it would turn a
    /// pipeline that is off by default into an outage for a class of returning customers.
    ///
    /// The photograph is deliberately not consulted: <see cref="InputNormalization.NormalizeForStory"/>
    /// maps the four fields and nothing else, because the story call is the one stage in this whole
    /// pipeline that is not allowed to see the child's picture.
    /// </summary>
    /// <summary>
    /// Everything wrong with a plan, in one list, so one corrective retry answers all of it.
    ///
    /// The shared checks first — Beki spelled the one way, the cast placed, the word budget, the
    /// spread count — then the child's own name, and then the composite path's own rule, which is
    /// stricter in exactly one place: Beki on all eight spreads rather than on five, because the
    /// illustration contract cannot describe a spread without her. Separate lists rather than one
    /// validator, because the stricter rule must not become the rule for every A5 book in
    /// production.
    ///
    /// The name check is NOT behind the composite flag, and that is the point of where it sits. The
    /// observed defect (2026-09-01) came out of the composite planner — ვეკო written ველო, one
    /// Georgian letter, in the title — but nothing about it is composite: every printing format
    /// takes the child's name as an input and prints it, and a misspelled name is the first thing a
    /// parent sees whichever prompt wrote the book. See <see cref="GeorgianNameFidelity"/>.
    /// </summary>
    private static IReadOnlyList<string> PlanProblems(
        MasterStory story, MasterStoryInput input, bool composite)
    {
        var problems = BekiPlanValidator
            .Validate(story, input.SpreadCount, input.Age)
            .ToList();

        problems.AddRange(GeorgianNameFidelity.Problems(story, input.ChildName));

        if (composite)
        {
            problems.AddRange(CompositePlanRules.Problems(story, input.SpreadCount));
        }

        return problems;
    }

    private CompositeStoryInput? CompositeStoryInputFor(Guid runId, MasterStoryInput input)
    {
        var normalized = InputNormalization.NormalizeForStory(new BookGenerationInput
        {
            ChildName = input.ChildName,
            ChildAge = input.Age,
            ChildGender = input.Gender,
            ThemeId = input.Theme.ToString(),
            // Never read by NormalizeForStory; the type requires it and the story stage must not
            // have it. Empty is the honest value.
            ChildPhotoRef = string.Empty,
        });

        if (!normalized.IsValid)
        {
            logger.LogWarning(
                "Run {RunId}: the composite pipeline is on but this input cannot be mapped to its "
                + "boundary ({Problems}); writing the story with the configured planner instead.",
                runId, string.Join(" ", normalized.Problems));

            return null;
        }

        return CompositeStoryInput.From(normalized.Story!) with { SpreadCount = input.SpreadCount };
    }

    /// <summary>
    /// Finishes a run whose story survived but whose job did not. The cover is the only step
    /// after the story, and it is cheap enough to simply attempt again.
    /// </summary>
    private async Task ResumeAtCoverAsync(MasterStoryRun run, CancellationToken cancellationToken)
    {
        try
        {
            await runRepository.MarkReadyAsync(run.Id, run.ContentJson!, cancellationToken);

            if (!string.IsNullOrWhiteSpace(run.CoverImageUrl))
            {
                return;
            }

            var story = JsonSerializer.Deserialize<MasterStory>(run.StoryJson!, JsonOptions);
            if (story is null)
            {
                return;
            }

            var coverUrl = await DrawCoverAsync(run, story, cancellationToken);
            if (coverUrl is not null)
            {
                await runRepository.SaveCoverAsync(run.Id, coverUrl, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The story is safe and the reader can open on the world's artwork, so a cover that
            // cannot be redrawn is not worth failing a finished book over.
            //
            // A cancellation is excluded, and that exclusion is the fix for a real fault: swallowed
            // here, a shutdown mid-cover looked exactly like a cover that could not be drawn, the
            // job reported success, Hangfire never requeued it, and the run kept a missing cover
            // permanently. Passed up, the caller can tell a deploy (requeue, and this very branch
            // redraws it next time) from a budget that ran out (keep the story, keep the run Ready).
            logger.LogWarning(ex, "Could not finish the cover for resumed run {RunId}.", run.Id);
        }
    }

    public Task<MasterStoryRun?> GetAsync(Guid runId, CancellationToken cancellationToken) =>
        runRepository.GetByIdAsync(runId, cancellationToken);

    public Task<MasterStoryRunProgress?> GetProgressAsync(Guid runId, CancellationToken cancellationToken) =>
        runRepository.GetProgressAsync(runId, cancellationToken);

    /// <summary>
    /// Draws the cover only. The other eight illustrations belong to a bought book and are drawn
    /// by the existing fulfilment job, which already paces them against rate limits.
    ///
    /// A run planned for the printing format gets the Beki cover — child and Beki together, drawn
    /// exactly as the fulfilment job draws it — so the cover a parent previews is the cover they
    /// get if they buy the book, rather than the legacy single-reference art the A5 flow has
    /// always used. Any failure of that path falls back to the legacy cover below: a preview never
    /// dies over its cover, Beki or not.
    ///
    /// What that fallback draws, though, is not the legacy cover unchanged. A printing cover scene
    /// is written for a book whose companion is Beki, and the legacy prompt carries no Beki
    /// reference and no rule about companions — so it read the scene, found a story about a child
    /// and a friend, and invented the friend. One shipped cover's companion was a white-and-blue
    /// robot. A cover with no companion is honest; a cover with the wrong one tells the parent
    /// this is not their book. So a run that loses the Beki cover falls back to an explicitly
    /// child-only prompt, and says loudly in the log why it had to.
    /// </summary>
    private async Task<string?> DrawCoverAsync(
        MasterStoryRun run,
        MasterStory story,
        CancellationToken cancellationToken)
    {
        // Non-null once the Beki path has been tried and lost: it doubles as the reason for the
        // log line and as the switch onto the child-only prompt below.
        string? bekiFailure = null;

        // The gate is the printing book format, not a version equality: a plan gets the Beki cover
        // because it carries the cast list and the placement that cover needs, whichever version
        // of the printing flow wrote it.
        if (bekiOptions.Value.BookFormatEnabled && BookFormat.IsPrintPlan(run.PromptVersion))
        {
            var (bekiCover, failure) = await TryDrawBekiCoverAsync(run, story, cancellationToken);
            if (bekiCover is not null)
            {
                return bekiCover;
            }

            bekiFailure = failure ?? "the Beki cover path returned nothing";
            logger.LogWarning(
                "Run {RunId}: no Beki cover ({Reason}). Falling back to a child-only cover — the "
                + "fallback prompt has no Beki reference, so it is forbidden from inventing a "
                + "companion in Beki's place.",
                run.Id, bekiFailure);
        }

        try
        {
            var castPhotos = new List<CastPhotoReference>();
            if (!string.IsNullOrWhiteSpace(run.PhotoBlobUrl))
            {
                var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(run.PhotoBlobUrl, cancellationToken);
                if (bytes is { Length: > 0 })
                {
                    castPhotos.Add(new CastPhotoReference
                    {
                        Name = run.ChildName,
                        Relationship = "hero child",
                        IsHero = true,
                        AppearanceDescription = run.AppearanceDescription,
                        Bytes = bytes
                    });
                }
            }

            // The prompt is used exactly as the story call wrote it: the character lock is already
            // inside it, and rewriting it here is how the hero used to drift between pages. The
            // only difference a lost Beki cover makes is the clause forbidding a stand-in
            // companion, and the plan's world lock — this cover is adopted into the book without
            // being drawn again, so it has to be drawn in the book's world. The scene, the lock
            // and the plan's own avoid list are untouched, and an A5 run, whose plan carries no
            // world lock, reaches this line exactly as it always has.
            // The parent's own eye colour, written into the lock for this one picture. A composite
            // plan's character lock is deliberately empty — the planner may not invent an
            // appearance — so without this the cover a parent judges the book by carried no eye
            // colour at all, however clearly they had typed one into the form.
            var coverLock = IllustrationPrompt.WithParentEyeColour(story.CharacterLock, run.EyeColor);

            var coverPrompt = bekiFailure is null
                ? IllustrationPrompt.Compose(coverLock, story.Cover.Scene, story.Cover.Avoid)
                : IllustrationPrompt.ComposeChildOnlyCover(
                    coverLock, story.Cover.Scene, story.Cover.Avoid, story.WorldLock);

            var imageBytes = await openAiService.GenerateStoryImageAsync(
                coverPrompt,
                new StoryImageReference { CharacterAnchorBytes = null, CastPhotos = castPhotos },
                cancellationToken);

            var stored = referenceImageNormalizer.NormalizeForStorageWebp(imageBytes);
            return await blobStorageService.UploadAsync(
                $"master-runs/{run.Id:N}/cover",
                stored.Bytes,
                stored.ContentType,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missing cover is a worse-looking preview, not a lost book. The story is already
            // saved, so failing the whole run here would throw away the expensive part over the
            // cheap one.
            //
            // A cancellation is not one of those failures and is passed up instead. It says the
            // process is going away or the run is out of time, and only the caller knows which —
            // and the difference is whether Hangfire gets to try again or the run stops here.
            logger.LogWarning(ex, "Cover illustration failed for run {RunId}; the story stands without it.", run.Id);
            return null;
        }
    }

    /// <summary>
    /// The Beki cover for a preview. A null url on any failure — a missing photo, a download that
    /// fails, a refused review the generator could not correct, a missing Beki master reference —
    /// so the caller falls back rather than losing the preview over its cover.
    ///
    /// The reason travels with the null. It used to be logged here and thrown away, which made
    /// the fallback's own log line say only that a cover had been drawn; the caller needs it both
    /// to explain itself and because "we could not draw Beki" is exactly the thing an operator
    /// reading a robot on a cover would have wanted to find in the log.
    /// </summary>
    private async Task<(string? Url, string? Failure)> TryDrawBekiCoverAsync(
        MasterStoryRun run, MasterStory story, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(run.PhotoBlobUrl))
            {
                return (null, "the run has no parked portrait");
            }

            var photo = await blobStorageService.DownloadBytesFromStoredUrlAsync(run.PhotoBlobUrl, cancellationToken);
            if (photo is not { Length: > 0 })
            {
                return (null, "the parked portrait could not be downloaded");
            }

            // "image/png" regardless of what the portrait was actually uploaded as, matching the
            // Beki fulfilment job: the generator's edit call reads the bytes rather than trusting
            // the label, and DownloadBytesFromStoredUrlAsync does not hand back the original
            // content type to relabel it with.
            // Same injection as the fallback path below, and for the same reason: the cover prompt
            // quotes the character lock verbatim, and a composite plan's is empty by design.
            var withEyeColour = story with
            {
                CharacterLock = IllustrationPrompt.WithParentEyeColour(story.CharacterLock, run.EyeColor),
            };

            var cover = await bekiBookGenerator.DrawCoverAsync(
                withEyeColour, photo, "image/png", cancellationToken);

            // The generator returns its last attempt even when review refused it. For a paid
            // spread that policy is right — a flawed picture beats a hole — but this is the one
            // image the parent judges the whole book by, before paying. A refused cover falls
            // back to the legacy path instead of being stored, because fulfilment later adopts
            // whatever cover the run holds without reviewing it again.
            if (!cover.Accepted)
            {
                logger.LogWarning(
                    "Beki cover for run {RunId} was refused by review. {Verdict}", run.Id, cover.Verdict);
                return (null, $"the Beki cover was refused by review: {cover.Verdict}");
            }

            var stored = referenceImageNormalizer.NormalizeForStorageWebp(cover.Image);
            var url = await blobStorageService.UploadAsync(
                $"master-runs/{run.Id:N}/cover", stored.Bytes, stored.ContentType, cancellationToken);

            return (url, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Includes the deliberate throw for a missing Beki master reference: the generator
            // will not draw a cover that needs Beki without it, and this is the caller that can
            // turn that into a cover with no companion at all rather than no preview.
            //
            // A cancellation is excluded for the reason it is excluded one level up: falling back
            // to the legacy cover because the host is shutting down would spend another image call
            // on a token that is already cancelled, and then report a cover that could not be drawn
            // rather than a job that should be retried.
            logger.LogWarning(ex, "Beki cover failed for run {RunId}.", run.Id);
            return (null, $"the Beki cover threw: {ex.Message}");
        }
    }
}
