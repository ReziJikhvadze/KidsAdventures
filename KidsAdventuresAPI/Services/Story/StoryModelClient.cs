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

        if (_options.LogPrompts)
        {
            // Everything that decides what comes back, in one entry. Split across several and the
            // one that matters is always the one that scrolled away.
            logger.LogInformation(
                "OpenAI story request → model={Model} schema={Schema} strict=true format=json_schema\n" +
                "--- instructions ---\n{System}\n--- input ---\n{User}",
                model,
                schemaName,
                systemPrompt,
                userPrompt);
        }

        var attempts = Math.Max(1, _options.StoryRetryAttempts);
        string body;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                body = await SendAsync(payload, cancellationToken);
                break;
            }
            catch (TransientStoryModelException ex) when (attempt < attempts)
            {
                var delay = ex.RetryAfter
                            ?? TimeSpan.FromSeconds(Math.Max(1, _options.StoryRetryBackoffSeconds) * attempt);
                logger.LogWarning(
                    "Story model attempt {Attempt}/{Total} failed ({Reason}); retrying in {Delay}s.",
                    attempt,
                    attempts,
                    ex.Message,
                    delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        using var document = JsonDocument.Parse(body);
        var text = ExtractOutputText(document.RootElement);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("The story model returned an empty response.");
        }

        if (_options.LogPrompts)
        {
            // The story as it arrived, before deserialising or projecting it. What the model
            // actually wrote is the only thing worth comparing a disappointing book against.
            logger.LogInformation("OpenAI story response ←\n{Story}", text);
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

    /// <summary>One attempt. Returns the raw body, or throws — transiently or not.</summary>
    private async Task<string> SendAsync(object payload, CancellationToken cancellationToken)
    {
        using var client = CreateClient();

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("responses", payload, StoryJson.Options, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // A connection that never landed. Nothing was generated, so another attempt costs
            // nothing but the wait.
            throw new TransientStoryModelException("the connection failed", null, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            var status = (int)response.StatusCode;
            logger.LogError("Story model call failed ({Status}): {Body}", status, Truncate(body));

            if (IsTransient(status))
            {
                throw new TransientStoryModelException(
                    $"The story model returned {status}.",
                    RetryAfter(response));
            }

            throw new InvalidOperationException($"The story model returned {status}.");
        }
    }

    /// <summary>
    /// Worth another attempt. 429 and 5xx are the provider asking to be asked again; 520-530 are
    /// its edge failing in front of it, which is the case that cost a real book. A 4xx is our
    /// request being wrong and will be exactly as wrong the second time.
    /// </summary>
    private static bool IsTransient(int status) =>
        status is 408 or 429 || status >= 500;

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var delta = response.Headers.RetryAfter?.Delta;
        if (delta is { } d && d > TimeSpan.Zero)
        {
            return d;
        }

        var date = response.Headers.RetryAfter?.Date;
        if (date is { } at)
        {
            var wait = at - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait;
            }
        }

        return null;
    }

    /// <summary>
    /// Derives from InvalidOperationException so that exhausting the attempts leaves callers —
    /// and the message a parent eventually sees — exactly as they were before retries existed.
    /// </summary>
    private sealed class TransientStoryModelException(
        string message,
        TimeSpan? retryAfter,
        Exception? inner = null) : InvalidOperationException(message, inner)
    {
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("OpenAI");
        client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new("Bearer", _options.ApiKey);

        // Reasoning-heavy work is legitimately slow, and a short timeout fails books that
        // would have been fine. Generous here; the pipeline is what decides a call has taken
        // too long to be worth waiting for.
        client.Timeout = TimeSpan.FromMinutes(12);
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
