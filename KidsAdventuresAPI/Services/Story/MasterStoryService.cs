using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Prompts;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// What one master call produced, kept together with what was asked for.
///
/// The prompts travel back with the story because they are worth storing: when a book comes out
/// wrong, the only useful question is what the model was actually told, and a prompt that is
/// rebuilt later from the same inputs is not evidence — the inputs may have been edited since.
/// </summary>
public sealed record MasterStoryResult
{
    public required MasterStory Story { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }
    public required string Model { get; init; }
    public required int PromptTokens { get; init; }
    public required int CompletionTokens { get; init; }
}

public interface IMasterStoryService
{
    /// <summary>The model this service will use. Exposed so callers can record it before the call.</summary>
    string ModelName { get; }

    /// <summary>Which prompt variant is in force. Recorded on the run so books can be compared.</summary>
    string PromptVersion { get; }

    /// <summary>The prompts this input would produce, without making the call.</summary>
    (string System, string User) BuildPrompts(MasterStoryInput input);

    Task<MasterStoryResult> WriteAsync(MasterStoryInput input, CancellationToken cancellationToken);
}

/// <summary>
/// Writes a whole book in one call: concept, all eight spreads, the character lock and every
/// illustration prompt.
///
/// One call rather than several is the point. The pictures cannot contradict the words when the
/// same pass wrote both, which is the failure this replaces — a fox named ბუბუ on one page and
/// ბუ on the next, because the name existed only inside generated prose and nothing carried it
/// forward. Here the name is written once and quoted afterwards.
/// </summary>
public sealed class MasterStoryService(
    IStoryModelClient modelClient,
    IOptions<OpenAiOptions> options,
    ILogger<MasterStoryService> logger) : IMasterStoryService
{
    private readonly OpenAiOptions _options = options.Value;

    public string ModelName =>
        string.IsNullOrWhiteSpace(_options.MasterStoryModel) ? _options.Model : _options.MasterStoryModel;

    public string PromptVersion =>
        string.Equals(_options.StoryPromptVersion, "v2", StringComparison.OrdinalIgnoreCase) ? "v2" : "v1";

    public (string System, string User) BuildPrompts(MasterStoryInput input) =>
        PromptVersion == "v2"
            ? (MasterStoryPromptV2.System(input), MasterStoryPromptV2.User(input))
            : (MasterStoryPrompt.System(input), MasterStoryPrompt.User(input));

    public async Task<MasterStoryResult> WriteAsync(MasterStoryInput input, CancellationToken cancellationToken)
    {
        var (systemPrompt, userPrompt) = BuildPrompts(input);
        var schema = MasterStorySchema.Build(input.SpreadCount);
        var model = ModelName;

        logger.LogInformation(
            "Writing a {Spreads}-spread book for {Child}, age {Age}, theme {Theme}, using {Model} and prompt {PromptVersion}.",
            input.SpreadCount,
            input.ChildName,
            input.Age,
            input.Theme,
            model,
            PromptVersion);

        var result = await modelClient.CompleteAsync<MasterStory>(
            model,
            systemPrompt,
            userPrompt,
            MasterStorySchema.Name,
            schema,
            cancellationToken);

        var story = result.Value;

        // The schema fixes the count, so a mismatch means the provider ignored it. Better to fail
        // here than to hand a short book to the page mapper, which would silently print blanks.
        if (story.Spreads.Count != input.SpreadCount)
        {
            throw new InvalidOperationException(
                $"The story model returned {story.Spreads.Count} spreads, expected {input.SpreadCount}.");
        }

        logger.LogInformation(
            "Book \"{Title}\" written: {Spreads} spreads, {Prompt} prompt tokens, {Completion} completion tokens.",
            story.Concept.Title,
            story.Spreads.Count,
            result.PromptTokens,
            result.CompletionTokens);

        return new MasterStoryResult
        {
            Story = story,
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            Model = model,
            PromptTokens = result.PromptTokens,
            CompletionTokens = result.CompletionTokens
        };
    }
}
