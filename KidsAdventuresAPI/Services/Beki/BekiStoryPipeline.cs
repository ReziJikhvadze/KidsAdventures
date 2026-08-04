using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Beki;

namespace AdventurePacks.Api.Services.Beki;

public interface IBekiStoryPipeline
{
    Task<BekiStoryResult> CreateStoryAsync(BekiStoryInput input, CancellationToken cancellationToken);
}

/// <summary>Everything the caller needs, including what to store for audit.</summary>
public sealed class BekiStoryResult
{
    public required bool Success { get; init; }
    public BekiStoryOutput? Story { get; init; }
    public BekiStoryOutput? RawGeneratorOutput { get; init; }
    public required IReadOnlyList<string> ValidationErrors { get; init; }
    public string? CreativeSeedId { get; init; }
    public required string GeneratorPromptVersion { get; init; }
    public required string ReviewerPromptVersion { get; init; }
    public string? RepairPromptVersion { get; init; }
    public required string GeneratorModel { get; init; }
    public required string ReviewerModel { get; init; }
    public required string FailureReason { get; init; }
}

/// <summary>
/// Orchestrates one Beki book: generate, validate, review, validate, repair once if needed.
///
/// The shape matters more than any single step. A book is not "whatever the model returned";
/// it is output that passed a deterministic gate twice, written by one model and audited by
/// another whose only job is to find what the first one got wrong. When repair cannot fix
/// the remaining errors the book fails loudly rather than reaching a child half-formed —
/// there is no unbounded retry, because a model that has failed twice usually fails again
/// while spending real money.
/// </summary>
public sealed class BekiStoryPipeline(
    IBekiOpenAiClient client,
    IBekiPromptProvider prompts,
    IBekiCreativeSeedPool seedPool,
    BekiStoryValidator validator,
    IOptions<BekiOptions> options,
    ILogger<BekiStoryPipeline> logger) : IBekiStoryPipeline
{
    private readonly BekiOptions _options = options.Value;

    public async Task<BekiStoryResult> CreateStoryAsync(BekiStoryInput input, CancellationToken cancellationToken)
    {
        var inputErrors = ValidateInput(input);
        if (inputErrors.Count > 0)
        {
            return Failed(inputErrors, "invalid_input");
        }

        // Step 3: the seed is chosen here, never inside the prompt.
        var seed = seedPool.Select(input);
        var seeded = WithSeed(input, seed);

        logger.LogInformation(
            "Beki story {RequestId}: book {BookNumber}, age band {AgeBand}, seed {SeedId}, mode {Mode}",
            seeded.RequestId, seeded.BookNumber, seeded.AgeBand, seed.SeedId, seeded.ContinuationMode);

        // Step 4: draft.
        var draft = await client.CompleteJsonAsync<BekiStoryOutput>(
            _options.StoryGeneratorModel,
            prompts.Get(BekiPromptProvider.StoryGenerator),
            new { storyInput = seeded },
            cancellationToken);

        // Step 5: first deterministic gate. Draft errors are diagnostic only — the reviewer
        // is explicitly asked to repair content, so a flawed draft is expected, not fatal.
        var draftErrors = validator.ValidateDraft(draft, seeded);
        if (draft is null)
        {
            return Failed(draftErrors, "generator_returned_no_json", seed.SeedId);
        }

        if (draftErrors.Count > 0)
        {
            logger.LogInformation(
                "Beki story {RequestId}: draft has {Count} issue(s) for the reviewer to fix: {Errors}",
                seeded.RequestId, draftErrors.Count, string.Join(" | ", draftErrors.Take(5)));
        }

        // Step 6: the reviewer returns a corrected story, not a list of comments.
        var reviewed = await client.CompleteJsonAsync<BekiStoryOutput>(
            _options.StoryReviewerModel,
            prompts.Get(BekiPromptProvider.StoryReviewer),
            new { storyInput = seeded, storyDraft = draft },
            cancellationToken);

        // A reviewer that returns nothing usable must not discard a draft that may be fine.
        var candidate = reviewed ?? draft;
        if (reviewed is null)
        {
            logger.LogWarning(
                "Beki story {RequestId}: reviewer returned no usable JSON; validating the draft instead.",
                seeded.RequestId);
        }

        // Step 7: the gate that actually decides whether this book can be sold.
        var errors = validator.ValidateFinal(candidate, seeded);
        if (errors.Count == 0)
        {
            return Succeeded(candidate, draft, seed.SeedId, repairUsed: false);
        }

        logger.LogWarning(
            "Beki story {RequestId}: {Count} validation error(s) after review: {Errors}",
            seeded.RequestId, errors.Count, string.Join(" | ", errors.Take(10)));

        // Step 8: repair, at most once. Structural fixes only — the reviewer already owns craft.
        if (_options.MaxRepairAttempts <= 0)
        {
            return Failed(errors, "failed_validation_no_repair_configured", seed.SeedId, draft);
        }

        var repaired = await client.CompleteJsonAsync<BekiStoryOutput>(
            _options.StoryRepairModel,
            prompts.Get(BekiPromptProvider.StoryRepair),
            new { storyInput = seeded, currentStory = candidate, validatorErrors = errors },
            cancellationToken);

        if (repaired is null)
        {
            return Failed(errors, "repair_returned_no_json", seed.SeedId, draft);
        }

        var repairErrors = validator.ValidateFinal(repaired, seeded);
        if (repairErrors.Count > 0)
        {
            logger.LogError(
                "Beki story {RequestId} failed after repair with {Count} error(s): {Errors}",
                seeded.RequestId, repairErrors.Count, string.Join(" | ", repairErrors.Take(10)));
            return Failed(repairErrors, "failed_validation_after_repair", seed.SeedId, draft);
        }

        return Succeeded(repaired, draft, seed.SeedId, repairUsed: true);
    }

    /// <summary>
    /// Checks the caller got the request right before any money is spent. These are
    /// programming errors rather than model errors, so they fail fast and never retry.
    /// </summary>
    private static List<string> ValidateInput(BekiStoryInput input)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(input.RequestId)) errors.Add("requestId is required.");
        if (string.IsNullOrWhiteSpace(input.ChildName)) errors.Add("childName is required.");
        if (input.Age is < 2 or > 10) errors.Add("age must be between 2 and 10.");

        if (!BekiStoryConstants.AgeBands.Contains(input.AgeBand))
        {
            errors.Add($"ageBand must be one of {string.Join(", ", BekiStoryConstants.AgeBands)}.");
        }
        else if (BekiStoryConstants.AgeBandFor(input.Age) != input.AgeBand)
        {
            // The prompt adapts vocabulary, cast size and suspense from the band, so a band
            // that disagrees with the age silently produces a book aimed at the wrong child.
            errors.Add($"ageBand '{input.AgeBand}' does not match age {input.Age}.");
        }

        if (!BekiStoryConstants.ContinuationModes.Contains(input.ContinuationMode))
        {
            errors.Add($"continuationMode must be one of {string.Join(", ", BekiStoryConstants.ContinuationModes)}.");
        }

        if (!BekiStoryConstants.ThirdPartyModes.Contains(input.ThirdPartyCharacterMode))
        {
            errors.Add($"thirdPartyCharacterMode must be one of {string.Join(", ", BekiStoryConstants.ThirdPartyModes)}.");
        }

        if (input.PageCount != BekiStoryConstants.PageCount)
        {
            errors.Add($"pageCount must be {BekiStoryConstants.PageCount}.");
        }

        if (input.Language != "ka") errors.Add("language must be 'ka'.");
        if (input.BookNumber < 1) errors.Add("bookNumber must be at least 1.");
        if (input.SelectedSupportingCharacters.Count > 4) errors.Add("At most 4 supporting characters may be selected.");
        if (input.ExtraWish is { Length: > 1000 }) errors.Add("extraWish exceeds 1000 characters.");

        if (input.ContinuationMode != "first_book" && input.PreviousStoryMemory is null)
        {
            errors.Add($"continuationMode '{input.ContinuationMode}' requires previousStoryMemory.");
        }

        // A placeholder that survives into the prompt becomes literal text in a child's book.
        if (input.ExtraWish is { } wish && wish.Contains("{random", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("extraWish still contains an unresolved template placeholder.");
        }

        return errors;
    }

    private static BekiStoryInput WithSeed(BekiStoryInput input, BekiCreativeSeed seed) => new()
    {
        SchemaVersion = input.SchemaVersion,
        RequestId = input.RequestId,
        ChildName = input.ChildName,
        Age = input.Age,
        AgeBand = input.AgeBand,
        Gender = input.Gender,
        EyeColor = input.EyeColor,
        Interests = input.Interests,
        Theme = input.Theme,
        ExtraWish = input.ExtraWish,
        SelectedSupportingCharacters = input.SelectedSupportingCharacters,
        BookNumber = input.BookNumber,
        ContinuationMode = input.ContinuationMode,
        PageCount = input.PageCount,
        Language = input.Language,
        ThirdPartyCharacterMode = input.ThirdPartyCharacterMode,
        FearReframingAllowed = input.FearReframingAllowed,
        CreativeSeed = seed,
        PreviousStoryMemory = input.PreviousStoryMemory,
    };

    private BekiStoryResult Succeeded(
        BekiStoryOutput story,
        BekiStoryOutput? draft,
        string seedId,
        bool repairUsed) => new()
    {
        Success = true,
        Story = story,
        RawGeneratorOutput = draft,
        ValidationErrors = [],
        CreativeSeedId = seedId,
        GeneratorPromptVersion = prompts.VersionOf(BekiPromptProvider.StoryGenerator),
        ReviewerPromptVersion = prompts.VersionOf(BekiPromptProvider.StoryReviewer),
        RepairPromptVersion = repairUsed ? prompts.VersionOf(BekiPromptProvider.StoryRepair) : null,
        GeneratorModel = _options.StoryGeneratorModel,
        ReviewerModel = _options.StoryReviewerModel,
        FailureReason = string.Empty,
    };

    private BekiStoryResult Failed(
        IReadOnlyList<string> errors,
        string reason,
        string? seedId = null,
        BekiStoryOutput? draft = null) => new()
    {
        Success = false,
        Story = null,
        RawGeneratorOutput = draft,
        ValidationErrors = errors,
        CreativeSeedId = seedId,
        GeneratorPromptVersion = prompts.VersionOf(BekiPromptProvider.StoryGenerator),
        ReviewerPromptVersion = prompts.VersionOf(BekiPromptProvider.StoryReviewer),
        RepairPromptVersion = null,
        GeneratorModel = _options.StoryGeneratorModel,
        ReviewerModel = _options.StoryReviewerModel,
        FailureReason = reason,
    };
}
