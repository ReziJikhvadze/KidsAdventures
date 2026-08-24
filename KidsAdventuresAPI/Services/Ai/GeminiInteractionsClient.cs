using System.Net.Http.Json;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using Microsoft.Extensions.Options;

namespace AdventurePacks.Api.Services.Ai;

/// <summary>
/// The one place a request reaches Google.
///
/// Both Gemini-backed services — the one that writes books and the one that draws them — post the
/// same envelope to the same endpoint and read the same reply, so the transport lives here once:
/// the key header, the retry rule, and the walk through the response. What differs between them
/// is what they put in <c>input</c> and what they ask for back, which is exactly the part that
/// belongs in the caller.
///
/// The envelope is the Interactions API: a flat <c>input</c> array of typed items (text, image),
/// a <c>response_format</c> saying what the answer should be, and a reply whose <c>steps</c>
/// carry the model's output. Parsing is deliberately forgiving about where in that structure the
/// answer sits and strict about what counts as an answer — a provider is free to add steps, and
/// a client that assumed <c>steps[0]</c> would start failing the day it does.
/// </summary>
public interface IGeminiInteractionsClient
{
    /// <summary>The model's text answer, plus what the call cost.</summary>
    Task<GeminiTextResult> CompleteTextAsync(
        string model,
        IReadOnlyList<GeminiInputItem> input,
        object? responseFormat,
        CancellationToken cancellationToken);

    /// <summary>The first image the model returned, as bytes.</summary>
    Task<byte[]> GenerateImageAsync(
        string model,
        IReadOnlyList<GeminiInputItem> input,
        object responseFormat,
        CancellationToken cancellationToken);
}

/// <summary>One item of a request's input: either a piece of text or a picture.</summary>
public abstract record GeminiInputItem
{
    public static GeminiInputItem Text(string text) => new GeminiTextItem(text);

    public static GeminiInputItem Image(byte[] bytes, string mimeType) =>
        new GeminiImageItem(bytes, string.IsNullOrWhiteSpace(mimeType) ? "image/png" : mimeType);

    internal abstract object ToPayload();
}

internal sealed record GeminiTextItem(string Value) : GeminiInputItem
{
    internal override object ToPayload() => new { type = "text", text = Value };
}

internal sealed record GeminiImageItem(byte[] Bytes, string MimeType) : GeminiInputItem
{
    internal override object ToPayload() =>
        new { type = "image", mime_type = MimeType, data = Convert.ToBase64String(Bytes) };
}

public sealed record GeminiTextResult(string Text, int InputTokens, int OutputTokens);

public sealed class GeminiInteractionsClient(
    IHttpClientFactory httpClientFactory,
    IOptions<GeminiOptions> options,
    ILogger<GeminiInteractionsClient> logger) : IGeminiInteractionsClient
{
    private readonly GeminiOptions _options = options.Value;

    public async Task<GeminiTextResult> CompleteTextAsync(
        string model,
        IReadOnlyList<GeminiInputItem> input,
        object? responseFormat,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(model, input, responseFormat, cancellationToken);
        var text = ExtractText(document.RootElement);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Gemini returned an empty response.");
        }

        var (inputTokens, outputTokens) = ExtractUsage(document.RootElement);
        return new GeminiTextResult(text, inputTokens, outputTokens);
    }

    public async Task<byte[]> GenerateImageAsync(
        string model,
        IReadOnlyList<GeminiInputItem> input,
        object responseFormat,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(model, input, responseFormat, cancellationToken);
        var image = ExtractImage(document.RootElement);

        if (image is not { Length: > 0 })
        {
            // A refusal arrives as a perfectly successful response carrying prose instead of a
            // picture, and that prose is the only explanation anyone will ever get, so it is
            // worth more in the message than "no image".
            var said = ExtractText(document.RootElement);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(said)
                    ? "Gemini returned no image."
                    : $"Gemini returned no image. It said: {Truncate(said)}");
        }

        return image;
    }

    private async Task<JsonDocument> SendAsync(
        string model,
        IReadOnlyList<GeminiInputItem> input,
        object? responseFormat,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Gemini is selected as a provider but Gemini:ApiKey is empty.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["input"] = input.Select(item => item.ToPayload()).ToArray(),
        };

        if (responseFormat is not null)
        {
            payload["response_format"] = responseFormat;
        }

        var attempts = Math.Max(1, _options.RetryAttempts);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var body = await PostAsync(payload, cancellationToken);
                return JsonDocument.Parse(body);
            }
            catch (TransientGeminiException ex) when (attempt < attempts)
            {
                var delay = ex.RetryAfter
                            ?? TimeSpan.FromSeconds(Math.Max(1, _options.RetryBackoffSeconds) * attempt);
                logger.LogWarning(
                    "Gemini attempt {Attempt}/{Total} failed ({Reason}); retrying in {Delay}s.",
                    attempt, attempts, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task<string> PostAsync(
        Dictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        using var client = CreateClient();

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("interactions", payload, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Nothing reached the model, so nothing was generated and nothing was billed: another
            // attempt costs only the wait.
            throw new TransientGeminiException("the connection failed", null, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            var status = (int)response.StatusCode;
            logger.LogError("Gemini call failed ({Status}): {Body}", status, Truncate(body));

            // Same rule the OpenAI client uses: 429 and 5xx are the provider asking to be asked
            // again; a 4xx is our request being wrong and will be just as wrong next time.
            if (status is 408 or 429 || status >= 500)
            {
                throw new TransientGeminiException($"Gemini returned {status}.", RetryAfter(response));
            }

            throw new InvalidOperationException($"Gemini returned {status}: {Truncate(body)}");
        }
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("Gemini");
        client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");

        // The key is a header, never a query parameter: a URL is logged by every proxy and every
        // request log in the path, and a key in one is a key that has to be rotated.
        client.DefaultRequestHeaders.Remove("x-goog-api-key");
        client.DefaultRequestHeaders.Add("x-goog-api-key", _options.ApiKey);

        client.Timeout = TimeSpan.FromMinutes(Math.Max(1, _options.TimeoutMinutes));
        return client;
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var delta = response.Headers.RetryAfter?.Delta;
        if (delta is { } d && d > TimeSpan.Zero)
        {
            return d;
        }

        if (response.Headers.RetryAfter?.Date is { } at)
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
    /// The first text the model produced, wherever the envelope keeps it.
    ///
    /// Walks every step rather than reading a fixed index, because a reply may legitimately carry
    /// reasoning or tool steps before the answer — the same reason the OpenAI client walks its
    /// own output array instead of taking the first item.
    /// </summary>
    internal static string ExtractText(JsonElement root) =>
        WalkContent(root, part =>
            part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null) ?? string.Empty;

    /// <summary>The first image in the reply, decoded.</summary>
    internal static byte[]? ExtractImage(JsonElement root)
    {
        var encoded = WalkContent(root, part =>
        {
            var type = part.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (!string.Equals(type, "image", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return part.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String
                ? data.GetString()
                : null;
        });

        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The model's answer, found in two passes: the steps that claim to be output, and then — only
    /// if those yielded nothing — any step at all.
    ///
    /// The order is the whole point. A reply is a timeline, and a thinking step can carry a
    /// summary of its own reasoning as text; taking the first text in the timeline would hand a
    /// story call the model's musings to deserialize against a schema, and a QA call an opinion
    /// instead of a verdict. The first live replies happened to put nothing in their thought
    /// steps, which is exactly the kind of luck that hides a bug until a book is being paid for.
    ///
    /// The second pass stays because the envelope is the provider's to change: a reply that
    /// stopped labelling its output step should degrade to the old, over-eager behaviour rather
    /// than return nothing at all.
    /// </summary>
    private static string? WalkContent(JsonElement root, Func<JsonElement, string?> select)
    {
        if (!root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return Scan(steps, select, outputOnly: true) ?? Scan(steps, select, outputOnly: false);
    }

    private static string? Scan(JsonElement steps, Func<JsonElement, string?> select, bool outputOnly)
    {
        foreach (var step in steps.EnumerateArray())
        {
            if (outputOnly && !IsModelOutput(step))
            {
                continue;
            }

            if (!step.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                var found = select(part);
                if (!string.IsNullOrEmpty(found))
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static bool IsModelOutput(JsonElement step) =>
        step.TryGetProperty("type", out var type)
        && type.ValueKind == JsonValueKind.String
        && string.Equals(type.GetString(), "model_output", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Token counts, mapped onto the two the rest of the product records. Thinking tokens are
    /// billed as output and are counted as output here, so a Gemini book's cost line means the
    /// same thing an OpenAI book's does.
    /// </summary>
    internal static (int InputTokens, int OutputTokens) ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return (0, 0);
        }

        return (Count(usage, "total_input_tokens"),
                Count(usage, "total_output_tokens") + Count(usage, "total_thought_tokens"));

        static int Count(JsonElement usage, string name) =>
            usage.TryGetProperty(name, out var value) && value.TryGetInt32(out var count) ? count : 0;
    }

    private static string Truncate(string value) =>
        value.Length <= 400 ? value : value[..400] + "…";

    private sealed class TransientGeminiException(
        string message,
        TimeSpan? retryAfter,
        Exception? inner = null) : InvalidOperationException(message, inner)
    {
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }
}
