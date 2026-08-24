using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Options;

namespace AdventurePacks.Api.Services.Ai;

/// <summary>
/// Gemini writing the book, behind the same door the OpenAI client stands in.
///
/// The engine asks for one thing — a value of type T that satisfies a JSON schema — and both
/// vendors can be made to promise exactly that, so the seam is the whole story: nothing upstream
/// of <see cref="IStoryModelClient"/> knows or asks which one answered.
///
/// The <paramref name="model"/> the caller passes is an OpenAI product name out of BekiOptions,
/// and it is deliberately ignored here in favour of <see cref="GeminiOptions.StoryModel"/> —
/// forwarding "gpt-5.6-luna" to Google would fail every call, and quietly mapping stage names
/// across two vendors' catalogues is a table that would rot the first time either renamed a
/// model. What actually distinguishes the stages is the prompt and the schema, and both arrive
/// here intact.
/// </summary>
public sealed class GeminiStoryModelClient(
    IGeminiInteractionsClient gemini,
    IOptions<GeminiOptions> geminiOptions,
    IOptions<OpenAiOptions> openAiOptions,
    ILogger<GeminiStoryModelClient> logger) : IStoryModelClient
{
    private readonly GeminiOptions _gemini = geminiOptions.Value;
    private readonly OpenAiOptions _openAi = openAiOptions.Value;

    public async Task<ModelResult<T>> CompleteAsync<T>(
        string model,
        string systemPrompt,
        string userPrompt,
        string schemaName,
        JsonElement schema,
        CancellationToken cancellationToken)
    {
        // One prompt, not two: the Interactions envelope has no separate instructions field, so
        // the system prompt is the first thing the model reads and the request is the second,
        // which is the order they would have been applied in anyway.
        var prompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? userPrompt
            : $"{systemPrompt}\n\n---\n\n{userPrompt}";

        var responseFormat = new
        {
            type = "text",
            mime_type = "application/json",
            schema
        };

        if (_openAi.LogPrompts)
        {
            logger.LogInformation(
                "Gemini story request → model={Model} schema={Schema} (requested {Requested})\n" +
                "--- prompt ---\n{Prompt}",
                _gemini.StoryModel, schemaName, model, prompt);
        }

        var result = await gemini.CompleteTextAsync(
            _gemini.StoryModel,
            [GeminiInputItem.Text(prompt)],
            responseFormat,
            cancellationToken);

        if (_openAi.LogPrompts)
        {
            logger.LogInformation("Gemini story response ←\n{Story}", result.Text);
        }

        T value;
        try
        {
            // Through the same sanitizer-backed deserializer the OpenAI path uses. Schema mode
            // should make a code fence impossible; "should" is not a reason to be brittle about
            // one, and the failure it prevents is a whole book.
            value = StoryJson.Deserialize<T>(result.Text);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Gemini returned unparseable {Type}: {Preview}",
                typeof(T).Name, Truncate(result.Text));
            throw new InvalidOperationException($"Gemini returned malformed {typeof(T).Name}.", ex);
        }

        return new ModelResult<T>(value, result.InputTokens, result.OutputTokens);
    }

    private static string Truncate(string value) =>
        value.Length <= 400 ? value : value[..400] + "…";
}
