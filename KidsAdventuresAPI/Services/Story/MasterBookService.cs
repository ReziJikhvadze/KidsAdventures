using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
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
    ILogger<MasterBookService> logger) : IMasterBookService
{
    /// <summary>
    /// How long an unclaimed guest run is kept. Long enough to finish, be read, and survive the
    /// sign-up round trip; short enough that a visitor who walks away leaves nothing behind.
    /// </summary>
    private static readonly TimeSpan GuestRunLifetime = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        var run = await runRepository.GetByIdAsync(runId, cancellationToken);
        if (run is null)
        {
            logger.LogWarning("Master story run {RunId} vanished before the job started.", runId);
            return;
        }

        // Where a preview's minutes actually go. Asked often enough — and guessed at often
        // enough — to be worth measuring rather than reasoning about.
        var started = System.Diagnostics.Stopwatch.StartNew();
        var writingDoneMs = 0L;

        // A book already written is never written twice.
        //
        // Hangfire re-queues a job whose process died, and this one dies in the most expensive
        // place available: mid-way through a call that takes minutes and costs real money. On a
        // deploy or a restart the retry would start again from the top and buy a second book —
        // and hand the parent a different story from the one they had been waiting for.
        //
        // Exceptions do not reach this path, because the catch below marks the run failed and
        // swallows them, so Hangfire never sees a failure to retry. This is for the case where
        // nothing threw and the process simply stopped existing.
        if (!string.IsNullOrWhiteSpace(run.StoryJson) && !string.IsNullOrWhiteSpace(run.ContentJson))
        {
            logger.LogInformation(
                "Run {RunId} already has its story; resuming at the cover rather than rewriting it.",
                runId);

            await ResumeAtCoverAsync(run, cancellationToken);
            return;
        }

        try
        {
            await runRepository.SetProgressAsync(
                runId,
                MasterStoryRunStatus.Writing,
                "ვწერთ შენს ზღაპარს… ეს რამდენიმე წუთს გრძელდება.",
                cancellationToken);

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

            // The prompts are stored before the call rather than after, so that a call which
            // times out still leaves behind what it was asked to do.
            var (systemPrompt, userPrompt) = masterStoryService.BuildPrompts(storyInput);
            await runRepository.SavePromptsAsync(
                runId,
                masterStoryService.ModelName,
                masterStoryService.PromptVersion,
                systemPrompt,
                userPrompt,
                cancellationToken);

            // Kept in step with what was just persisted: `run` was loaded once, above, before
            // this call wrote its version to the database, and DrawCoverAsync below reads
            // run.PromptVersion to decide whether this preview gets the Beki cover. Without this,
            // that check would read the stale, pre-save value — null on a run's first attempt —
            // and a v5 preview would silently fall back to the legacy cover every time.
            run.PromptVersion = masterStoryService.PromptVersion;

            var result = await masterStoryService.WriteAsync(storyInput, cancellationToken);

            // The v5 schema enforces shape but not the rules that only make sense reading the
            // whole plan together — Beki spelled the one way everything downstream expects, Beki
            // not quietly missing from the spreads the format promises. A plan that fails this is
            // worth one retry with the problems spelled out: a plan the parent never sees costs
            // far less than a book that ships without it.
            if (masterStoryService.PromptVersion == "v5")
            {
                var problems = BekiPlanValidator.Validate(result.Story, storyInput.SpreadCount);
                if (problems.Count > 0)
                {
                    logger.LogWarning(
                        "Run {RunId}: the Beki plan failed validation, retrying once: {Problems}",
                        runId, string.Join(" | ", problems));

                    var retried = await masterStoryService.RetryV5WithCorrectionsAsync(
                        storyInput, problems, cancellationToken);

                    // Both calls were paid for; the run's token accounting reports both, not just
                    // the attempt that happened to survive.
                    result = retried with
                    {
                        PromptTokens = result.PromptTokens + retried.PromptTokens,
                        CompletionTokens = result.CompletionTokens + retried.CompletionTokens,
                    };

                    var stillWrong = BekiPlanValidator.Validate(result.Story, storyInput.SpreadCount);
                    if (stillWrong.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"The Beki plan for run {runId} is still invalid after a retry: "
                            + string.Join("; ", stillWrong));
                    }
                }
            }

            var content = MasterStoryProjection.ToContent(result.Story, run.ChildName, run.Theme);

            await runRepository.SaveStoryAsync(
                runId,
                JsonSerializer.Serialize(result.Story, JsonOptions),
                JsonSerializer.Serialize(content, JsonOptions),
                result.PromptTokens,
                result.CompletionTokens,
                cancellationToken);

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
                cancellationToken);

            // The cover is the book's cover, not page one. Writing it onto the first page would
            // overwrite that page's own illustration — which has its own prompt and its own
            // moment in the story — and quietly leave the book with eight pictures instead of
            // nine.
            var coverUrl = await DrawCoverAsync(run, result.Story, cancellationToken);
            if (coverUrl is not null)
            {
                await runRepository.SaveCoverAsync(runId, coverUrl, cancellationToken);
            }

            logger.LogInformation(
                "Run {RunId} ready in {Total:F1}s — story {Story:F1}s, cover {Cover:F1}s: \"{Title}\".",
                runId,
                started.ElapsedMilliseconds / 1000.0,
                writingDoneMs / 1000.0,
                (started.ElapsedMilliseconds - writingDoneMs) / 1000.0,
                result.Story.Concept.Title);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Master story run {RunId} failed.", runId);
            await runRepository.MarkFailedAsync(runId, ex.Message, CancellationToken.None);
        }
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
        catch (Exception ex)
        {
            // The story is safe and the reader can open on the world's artwork, so a cover that
            // cannot be redrawn is not worth failing a finished book over.
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
    /// A run planned by v5 gets the Beki cover — child and Beki together, QA-reviewed exactly as
    /// the fulfilment job draws it — so the cover a parent previews is the cover they get if they
    /// buy the book, rather than the legacy single-reference art the A5 flow has always used. Any
    /// failure of that path falls back to the legacy cover below: a preview never dies over its
    /// cover, Beki or not.
    /// </summary>
    private async Task<string?> DrawCoverAsync(
        MasterStoryRun run,
        MasterStory story,
        CancellationToken cancellationToken)
    {
        if (bekiOptions.Value.BookFormatEnabled
            && string.Equals(run.PromptVersion, "v5", StringComparison.OrdinalIgnoreCase))
        {
            var bekiCover = await TryDrawBekiCoverAsync(run, story, cancellationToken);
            if (bekiCover is not null)
            {
                return bekiCover;
            }
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
            // inside it, and rewriting it here is how the hero used to drift between pages.
            var imageBytes = await openAiService.GenerateStoryImageAsync(
                IllustrationPrompt.Compose(story.CharacterLock, story.Cover.Scene, story.Cover.Avoid),
                new StoryImageReference { CharacterAnchorBytes = null, CastPhotos = castPhotos },
                cancellationToken);

            var stored = referenceImageNormalizer.NormalizeForStorageWebp(imageBytes);
            return await blobStorageService.UploadAsync(
                $"master-runs/{run.Id:N}/cover",
                stored.Bytes,
                stored.ContentType,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // A missing cover is a worse-looking preview, not a lost book. The story is already
            // saved, so failing the whole run here would throw away the expensive part over the
            // cheap one.
            logger.LogWarning(ex, "Cover illustration failed for run {RunId}; the story stands without it.", run.Id);
            return null;
        }
    }

    /// <summary>
    /// The Beki cover for a preview. Null on any failure — a missing photo, a download that
    /// fails, a refused review the generator could not correct — so the caller falls back to the
    /// legacy cover rather than losing the preview over it.
    /// </summary>
    private async Task<string?> TryDrawBekiCoverAsync(
        MasterStoryRun run, MasterStory story, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(run.PhotoBlobUrl))
            {
                return null;
            }

            var photo = await blobStorageService.DownloadBytesFromStoredUrlAsync(run.PhotoBlobUrl, cancellationToken);
            if (photo is not { Length: > 0 })
            {
                return null;
            }

            // "image/png" regardless of what the portrait was actually uploaded as, matching the
            // Beki fulfilment job: the generator's edit call reads the bytes rather than trusting
            // the label, and DownloadBytesFromStoredUrlAsync does not hand back the original
            // content type to relabel it with.
            var cover = await bekiBookGenerator.DrawCoverAsync(story, photo, "image/png", cancellationToken);

            // The generator returns its last attempt even when review refused it. For a paid
            // spread that policy is right — a flawed picture beats a hole — but this is the one
            // image the parent judges the whole book by, before paying. A refused cover falls
            // back to the legacy path instead of being stored, because fulfilment later adopts
            // whatever cover the run holds without reviewing it again.
            if (!cover.Accepted)
            {
                logger.LogWarning(
                    "Beki cover for run {RunId} was refused by review; using the legacy cover. {Verdict}",
                    run.Id, cover.Verdict);
                return null;
            }

            var stored = referenceImageNormalizer.NormalizeForStorageWebp(cover.Image);
            return await blobStorageService.UploadAsync(
                $"master-runs/{run.Id:N}/cover", stored.Bytes, stored.ContentType, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Beki cover failed for run {RunId}; falling back to the legacy cover.", run.Id);
            return null;
        }
    }
}
