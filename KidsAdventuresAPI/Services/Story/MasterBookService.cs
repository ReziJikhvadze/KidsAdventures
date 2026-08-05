using System.Text.Json;
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
                var describePrompt = AdventurePromptBuilder.BuildHeroPhotoDescribePrompt(
                    language, input.ChildName, input.Age);
                appearance = await openAiService.DescribeCharacterFromPhotoAsync(
                    input.PhotoBytes, input.PhotoContentType, describePrompt, cancellationToken);
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
            var systemPrompt = MasterStoryPrompt.System(storyInput);
            var userPrompt = MasterStoryPrompt.User(storyInput);
            await runRepository.SavePromptsAsync(
                runId, masterStoryService.ModelName, systemPrompt, userPrompt, cancellationToken);

            var result = await masterStoryService.WriteAsync(storyInput, cancellationToken);
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

            await runRepository.SetProgressAsync(
                runId,
                MasterStoryRunStatus.Illustrating,
                "ზღაპარი დაწერილია — ვხატავთ ყდას…",
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

            await runRepository.MarkReadyAsync(
                runId,
                JsonSerializer.Serialize(content, JsonOptions),
                cancellationToken);

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

    public Task<MasterStoryRun?> GetAsync(Guid runId, CancellationToken cancellationToken) =>
        runRepository.GetByIdAsync(runId, cancellationToken);

    /// <summary>
    /// Draws the cover only. The other eight illustrations belong to a bought book and are drawn
    /// by the existing fulfilment job, which already paces them against rate limits.
    /// </summary>
    private async Task<string?> DrawCoverAsync(
        MasterStoryRun run,
        MasterStory story,
        CancellationToken cancellationToken)
    {
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
                IllustrationPrompt.Compose(story.Cover.Prompt, story.Cover.NegativePrompt),
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
}
