using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Beki;
using AdventurePacks.Api.DTOs.Beki;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using Hangfire;

namespace AdventurePacks.Api.Services.Beki;

public interface IBekiStoryService
{
    Task<BekiStoryStatusResponse> CreateAsync(
        Guid userId,
        CreateBekiStoryRequest request,
        CancellationToken cancellationToken);

    Task<BekiStoryStatusResponse?> GetStatusAsync(Guid userId, Guid storyId, CancellationToken cancellationToken);

    Task<BekiStoryResponse?> GetStoryAsync(Guid userId, Guid storyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BekiStoryStatusResponse>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Hangfire entry point. Public because the job serializer needs to reach it.</summary>
    Task RunGenerationAsync(Guid storyId);

    /// <summary>
    /// Hangfire entry point for illustration. Queued automatically once a story is approved,
    /// and safe to re-run: assets that already exist are reused rather than redrawn.
    /// </summary>
    Task RunIllustrationAsync(Guid storyId);
}

/// <summary>
/// Application layer over <see cref="IBekiStoryPipeline"/>.
///
/// Generation is queued rather than awaited in the request. A book takes roughly a minute
/// to write and another to review; holding an HTTP connection open for that guarantees a
/// gateway timeout somewhere, and a timeout that loses an already-paid-for book is far
/// worse than a poll.
///
/// The series-shaped fields — book number, continuation mode, previous memory — are all
/// derived here from what the database already knows. A client cannot supply them, because
/// a client that could would be able to rewrite a child's history.
/// </summary>
public sealed class BekiStoryService(
    IBekiStoryPipeline pipeline,
    IBekiVisualPipeline visualPipeline,
    IBekiStoryRepository repository,
    ICharacterRepository characterRepository,
    IBlobStorageService blobStorage,
    IBackgroundJobClient backgroundJobs,
    IOptions<BekiOptions> options,
    ILogger<BekiStoryService> logger) : IBekiStoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly BekiOptions _options = options.Value;

    public async Task<BekiStoryStatusResponse> CreateAsync(
        Guid userId,
        CreateBekiStoryRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("The Beki pipeline is not enabled.");
        }

        var requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? $"beki-{Guid.NewGuid():N}"
            : request.RequestId.Trim();

        // Idempotency before anything is spent.
        var existing = await repository.GetByRequestIdAsync(requestId, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Beki request {RequestId} already exists as story {StoryId}", requestId, existing.Id);
            return ToStatus(existing);
        }

        var ageBand = BekiStoryConstants.AgeBandFor(request.Age);

        // Series position and memory come from the database, never from the client.
        var bookNumber = 1;
        BekiPreviousStoryMemory? memory = null;
        if (request.CharacterId is { } characterId)
        {
            bookNumber = await repository.GetLatestBookNumberAsync(characterId, cancellationToken) + 1;
            memory = await LoadMemoryAsync(characterId, cancellationToken);
        }

        var input = new BekiStoryInput
        {
            RequestId = requestId,
            ChildName = request.ChildName.Trim(),
            Age = request.Age,
            AgeBand = ageBand,
            Gender = request.Gender,
            EyeColor = request.EyeColor,
            Interests = request.Interests,
            Theme = request.Theme.Trim(),
            ExtraWish = string.IsNullOrWhiteSpace(request.ExtraWish) ? null : request.ExtraWish.Trim(),
            SelectedSupportingCharacters = request.SupportingCharacters
                .Select(c => new BekiSupportingCharacter
                {
                    Id = c.Id,
                    Name = c.Name,
                    Relationship = c.Relationship,
                    Description = c.Description,
                })
                .ToList(),
            BookNumber = bookNumber,
            ContinuationMode = ResolveContinuationMode(bookNumber, memory),
            // Production default. The other modes exist for licensed content and internal
            // tests, and neither should be reachable from an ordinary parent request.
            ThirdPartyCharacterMode = string.IsNullOrWhiteSpace(request.ThirdPartyCharacterMode)
                ? "originalize"
                : request.ThirdPartyCharacterMode,
            FearReframingAllowed = request.FearReframingAllowed,
            PreviousStoryMemory = memory,
        };

        var record = new BekiStoryRecord
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            UserId = userId,
            CharacterId = request.CharacterId,
            BookNumber = bookNumber,
            ChildName = input.ChildName,
            AgeBand = ageBand,
            Theme = input.Theme,
            Status = BekiStoryStatus.Pending,
            StoryInputJson = JsonSerializer.Serialize(input, JsonOptions),
        };

        await repository.CreateAsync(record, cancellationToken);
        backgroundJobs.Enqueue<IBekiStoryService>(service => service.RunGenerationAsync(record.Id));

        logger.LogInformation(
            "Beki story {StoryId} queued: book {BookNumber} for {ChildName} ({AgeBand})",
            record.Id, bookNumber, record.ChildName, ageBand);

        return ToStatus(record);
    }

    public async Task RunGenerationAsync(Guid storyId)
    {
        // Hangfire may deliver the same job twice; only the worker that flips pending ->
        // generating proceeds, so a book is never written and billed for twice.
        if (!await repository.TryMarkGeneratingAsync(storyId, CancellationToken.None))
        {
            logger.LogInformation("Beki story {StoryId} is already being generated; skipping.", storyId);
            return;
        }

        var record = await repository.GetByIdAsync(storyId, CancellationToken.None);
        if (record?.StoryInputJson is null)
        {
            logger.LogError("Beki story {StoryId} has no stored input; cannot generate.", storyId);
            return;
        }

        var input = JsonSerializer.Deserialize<BekiStoryInput>(record.StoryInputJson, JsonOptions);
        if (input is null)
        {
            await repository.MarkFailedAsync(storyId, "stored_input_unreadable", null, null, CancellationToken.None);
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(Math.Max(120, _options.StoryTimeoutSeconds) * 3));

            var result = await pipeline.CreateStoryAsync(input, timeout.Token);

            if (!result.Success || result.Story is null)
            {
                await repository.MarkFailedAsync(
                    storyId,
                    result.FailureReason,
                    JsonSerializer.Serialize(result.ValidationErrors, JsonOptions),
                    result.RawGeneratorOutput is null ? null : JsonSerializer.Serialize(result.RawGeneratorOutput, JsonOptions),
                    CancellationToken.None);
                return;
            }

            var story = result.Story;
            var reviewStatus = story.ReviewMetadata?.Status;

            record.TitleKa = story.TitleKa;
            // A reviewer asking for human eyes still produces a complete, valid book, so the
            // parent is not left with nothing while someone looks at it.
            record.Status = reviewStatus == "needs_human_review"
                ? BekiStoryStatus.NeedsHumanReview
                : BekiStoryStatus.Approved;
            record.ReviewStatus = reviewStatus;
            record.FinalStoryJson = JsonSerializer.Serialize(story, JsonOptions);
            record.RawGeneratorOutputJson = result.RawGeneratorOutput is null
                ? null
                : JsonSerializer.Serialize(result.RawGeneratorOutput, JsonOptions);
            record.CreativeSeedId = result.CreativeSeedId;
            record.GeneratorPromptVersion = result.GeneratorPromptVersion;
            record.ReviewerPromptVersion = result.ReviewerPromptVersion;
            record.RepairPromptVersion = result.RepairPromptVersion;
            record.GeneratorModel = result.GeneratorModel;
            record.ReviewerModel = result.ReviewerModel;

            var memory = new BekiMemoryRecord
            {
                Id = Guid.NewGuid(),
                StoryId = storyId,
                CharacterId = record.CharacterId,
                BookNumber = record.BookNumber,
                MemoryJson = JsonSerializer.Serialize(story.ContinuationMemory, JsonOptions),
                NextChapterHookKa = Truncate(story.ContinuationMemory.NextChapterHookKa, 500),
            };

            await repository.SaveApprovedAsync(record, memory, CancellationToken.None);

            logger.LogInformation(
                "Beki story {StoryId} completed: '{Title}' ({Status}, seed {Seed})",
                storyId, story.TitleKa, record.Status, result.CreativeSeedId);

            // Illustration is a separate job rather than a continuation of this one. A book
            // is thirteen images and can run for many minutes; keeping it separate means a
            // failure there never discards an approved story, and the work can be retried
            // on its own without rewriting the prose.
            if (record.CharacterId is not null)
            {
                backgroundJobs.Enqueue<IBekiStoryService>(service => service.RunIllustrationAsync(storyId));
            }
            else
            {
                logger.LogInformation(
                    "Beki story {StoryId} has no saved child, so there is no photo to illustrate from.", storyId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Beki story {StoryId} generation threw", storyId);
            await repository.MarkFailedAsync(storyId, ex.Message, null, null, CancellationToken.None);
        }
    }

    public async Task RunIllustrationAsync(Guid storyId)
    {
        var record = await repository.GetByIdAsync(storyId, CancellationToken.None);
        if (record?.FinalStoryJson is null || record.CharacterId is null)
        {
            logger.LogWarning("Beki story {StoryId} cannot be illustrated: no approved story or no child.", storyId);
            return;
        }

        // Illustration must never run ahead of an approved story: the scene specs are derived
        // from the story's structured metadata, and a draft's cast list may still be wrong.
        if (!BekiStoryStatus.IsReadable(record.Status))
        {
            logger.LogWarning(
                "Beki story {StoryId} is {Status}; illustration only runs on an approved story.",
                storyId, record.Status);
            return;
        }

        var story = JsonSerializer.Deserialize<BekiStoryOutput>(record.FinalStoryJson, JsonOptions);
        if (story is null)
        {
            logger.LogError("Beki story {StoryId} has unreadable stored JSON.", storyId);
            return;
        }

        var character = await characterRepository.GetByIdAsync(
            record.CharacterId.Value, record.UserId, CancellationToken.None);

        if (character?.PhotoUrl is null)
        {
            logger.LogWarning(
                "Beki story {StoryId}: child {CharacterId} has no photo, so there is nothing to build identity from.",
                storyId, record.CharacterId);
            return;
        }

        try
        {
            var photo = await blobStorage.DownloadBytesFromStoredUrlAsync(character.PhotoUrl, CancellationToken.None);

            // Thirteen images with review and repair; the story timeout is nowhere near enough.
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(Math.Max(600, _options.StoryTimeoutSeconds * 8)));

            var result = await visualPipeline.IllustrateAsync(
                story,
                new BekiVisualContext
                {
                    StoryId = storyId,
                    CharacterId = record.CharacterId.Value,
                    ChildPhotoBytes = photo,
                    ChildPhotoContentType = "image/jpeg",
                    Age = AgeFromBand(record.AgeBand),
                    AgeBand = record.AgeBand,
                    EyeColor = character.EyeColor,
                },
                timeout.Token);

            if (result.Success)
            {
                logger.LogInformation(
                    "Beki story {StoryId} illustrated: cover + {Pages} pages.", storyId, result.Pages.Count);
            }
            else
            {
                // The story stays approved. A book with prose and no pictures is recoverable
                // by re-running this job; a book marked failed is not.
                logger.LogError(
                    "Beki story {StoryId} illustration incomplete ({Reason}): {Warnings}",
                    storyId, result.FailureReason, string.Join(" | ", result.Warnings.Take(6)));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Beki story {StoryId} illustration threw", storyId);
        }
    }

    /// <summary>
    /// The identity analyzer wants a concrete age to cross-check against the photo, but only
    /// the band survives on the story record. The middle of the band is the safest guess and
    /// the parent-supplied value in the spec overrides it anyway.
    /// </summary>
    private static int AgeFromBand(string ageBand) => ageBand switch
    {
        "2-4" => 3,
        "5-7" => 6,
        _ => 9,
    };

    public async Task<BekiStoryStatusResponse?> GetStatusAsync(
        Guid userId,
        Guid storyId,
        CancellationToken cancellationToken)
    {
        var record = await repository.GetForUserAsync(storyId, userId, cancellationToken);
        return record is null ? null : ToStatus(record);
    }

    public async Task<BekiStoryResponse?> GetStoryAsync(Guid userId, Guid storyId, CancellationToken cancellationToken)
    {
        var record = await repository.GetForUserAsync(storyId, userId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        return new BekiStoryResponse
        {
            Id = record.Id,
            Status = record.Status,
            BookNumber = record.BookNumber,
            Story = record.FinalStoryJson is null
                ? null
                : JsonSerializer.Deserialize<BekiStoryOutput>(record.FinalStoryJson, JsonOptions),
        };
    }

    public async Task<IReadOnlyList<BekiStoryStatusResponse>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var records = await repository.ListForUserAsync(userId, cancellationToken);
        return records.Select(ToStatus).ToList();
    }

    private async Task<BekiPreviousStoryMemory?> LoadMemoryAsync(Guid characterId, CancellationToken cancellationToken)
    {
        var record = await repository.GetLatestMemoryForCharacterAsync(characterId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var memory = JsonSerializer.Deserialize<BekiContinuationMemory>(record.MemoryJson, JsonOptions);
        if (memory is null)
        {
            logger.LogWarning("Beki memory for character {CharacterId} is unreadable; starting a fresh arc.", characterId);
            return null;
        }

        // The output memory and the input memory are different shapes: the next book needs
        // to know what is established and unresolved, not a summary of what already happened.
        return new BekiPreviousStoryMemory
        {
            RelationshipWithBekiEn = string.Join(" ", memory.RelationshipUpdatesEn),
            KnownCompanions = memory.CharactersIntroduced.Concat(memory.ReturningCharacters).Distinct().ToList(),
            WorldsVisited = memory.LocationsDiscovered,
            WorldRulesEn = [memory.WorldStateEn],
            ImportantObjects = memory.ImportantObjects,
            PromisesKa = memory.PromisesMadeKa,
            ResolvedThreadsKa = [memory.ResolvedThreadKa],
            OpenThreadsKa = memory.OpenThreadsKa,
            RecentPlotPatternsToAvoidEn = memory.RecentPlotPatternsToAvoidEn,
            LastChapterHookKa = memory.NextChapterHookKa,
        };
    }

    /// <summary>
    /// Book 1 is a first book. Later books continue the exact unresolved hook when there is
    /// one, and otherwise start a fresh challenge in the same universe — never a reset.
    /// </summary>
    private static string ResolveContinuationMode(int bookNumber, BekiPreviousStoryMemory? memory)
    {
        if (bookNumber <= 1 || memory is null)
        {
            return "first_book";
        }

        return string.IsNullOrWhiteSpace(memory.LastChapterHookKa)
            ? "new_adventure_same_universe"
            : "continue_previous_chapter";
    }

    private static BekiStoryStatusResponse ToStatus(BekiStoryRecord record) => new()
    {
        Id = record.Id,
        Status = record.Status,
        IsReady = BekiStoryStatus.IsReadable(record.Status),
        TitleKa = record.TitleKa,
        BookNumber = record.BookNumber,
        FailureReason = record.FailureReason,
        ReviewStatus = record.ReviewStatus,
        CreatedAt = record.CreatedAt,
        CompletedAt = record.CompletedAt,
    };

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
