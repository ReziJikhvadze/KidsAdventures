using System.Net.Http.Json;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;

namespace AdventurePacks.Api.Services.Story;

public interface IStoryModelClient
{
    /// <summary>
    /// One structured completion, answered against a schema. Returns the parsed value and what
    /// it cost, so analytics can attribute spend to the stage that incurred it.
    /// </summary>
    Task<ModelResult<T>> CompleteAsync<T>(
        string model,
        string systemPrompt,
        string userPrompt,
        string schemaName,
        JsonElement schema,
        CancellationToken cancellationToken);
}

public sealed record ModelResult<T>(T Value, int PromptTokens, int CompletionTokens);

/// <summary>
/// The engine's only door to a model.
///
/// Every call goes through a JSON schema rather than a free-form "reply with JSON" instruction.
/// That distinction is not cosmetic: the previous pipeline named a schema file in its prompt and
/// never actually attached it, and attaching the real one took nineteen validation errors to
/// zero and halved the latency. A schema is a contract the provider enforces; a sentence asking
/// for JSON is a hope.
/// </summary>
public sealed class StoryModelClient(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiOptions> options,
    ILogger<StoryModelClient> logger) : IStoryModelClient
{
    private readonly OpenAiOptions _options = options.Value;

    public async Task<ModelResult<T>> CompleteAsync<T>(
        string model,
        string systemPrompt,
        string userPrompt,
        string schemaName,
        JsonElement schema,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model,
            instructions = systemPrompt,
            input = userPrompt,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = schemaName,
                    schema,
                    strict = true
                }
            }
        };

        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("responses", payload, StoryJson.Options, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Story model call failed ({Status}): {Body}", (int)response.StatusCode, Truncate(body));
            throw new InvalidOperationException($"The story model returned {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(body);
        var text = ExtractOutputText(document.RootElement);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("The story model returned an empty response.");
        }

        T value;
        try
        {
            value = StoryJson.Deserialize<T>(text);
        }
        catch (JsonException ex)
        {
            // Schema mode makes this rare, but a truncated response is still possible, and the
            // first 400 characters are what makes the cause obvious in a log.
            logger.LogError(ex, "Story model returned unparseable {Type}: {Preview}",
                typeof(T).Name, Truncate(text));
            throw new InvalidOperationException($"The story model returned malformed {typeof(T).Name}.", ex);
        }

        var (promptTokens, completionTokens) = ExtractUsage(document.RootElement);
        return new ModelResult<T>(value, promptTokens, completionTokens);
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("OpenAI");
        client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new("Bearer", _options.ApiKey);

        // Planning and writing are reasoning-heavy and legitimately slow. A short timeout here
        // fails books that would have been fine.
        client.Timeout = TimeSpan.FromMinutes(5);
        return client;
    }

    /// <summary>
    /// Pulls the text out of a Responses API envelope, which nests it under output → content.
    /// Written defensively because the envelope carries reasoning items alongside the answer.
    /// </summary>
    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var direct) && direct.ValueKind == JsonValueKind.String)
        {
            return direct.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }

    private static (int Prompt, int Completion) ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return (0, 0);
        }

        var prompt = usage.TryGetProperty("input_tokens", out var input) && input.TryGetInt32(out var p) ? p : 0;
        var completion = usage.TryGetProperty("output_tokens", out var o) && o.TryGetInt32(out var c) ? c : 0;
        return (prompt, completion);
    }

    private static string Truncate(string value) =>
        value.Length <= 400 ? value : value[..400] + "…";
}
