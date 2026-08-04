using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Infrastructure;

namespace AdventurePacks.Api.Services.Beki;

public interface IBekiOpenAiClient
{
    /// <summary>
    /// One structured call: a system prompt, a JSON payload, and a JSON object back,
    /// deserialized to <typeparamref name="T"/>. Returns null when the model produced
    /// nothing parsable, so the caller can route to the repair path rather than throw.
    /// </summary>
    Task<T?> CompleteJsonAsync<T>(
        string model,
        string systemPrompt,
        object userPayload,
        CancellationToken cancellationToken,
        object? responseSchema = null) where T : class;

    /// <summary>Same, with images attached — identity analysis and visual review.</summary>
    Task<T?> CompleteJsonWithImagesAsync<T>(
        string model,
        string systemPrompt,
        object userPayload,
        IReadOnlyList<BekiImageAttachment> images,
        CancellationToken cancellationToken,
        object? responseSchema = null) where T : class;

    /// <summary>Free-text out: used to produce an image prompt, which is prose, not JSON.</summary>
    Task<string> CompleteTextWithImagesAsync(
        string model,
        string systemPrompt,
        object userPayload,
        IReadOnlyList<BekiImageAttachment> images,
        CancellationToken cancellationToken);

    /// <summary>
    /// Renders an illustration. References are passed as image inputs so the model can hold
    /// the child's identity, the hero anchor and Beki's canonical design while it draws.
    /// </summary>
    Task<byte[]> GenerateImageAsync(
        string prompt,
        IReadOnlyList<BekiImageAttachment> references,
        string size,
        string quality,
        CancellationToken cancellationToken);
}

/// <summary>A labelled reference image. The label matters: prompts refer to "Reference Image A".</summary>
public sealed record BekiImageAttachment(string Label, byte[] Bytes, string ContentType);

/// <summary>
/// Thin transport for the Beki pipelines.
///
/// Kept separate from <c>OpenAiService</c> deliberately. That class encodes the older
/// single-shot flow — one model, one prompt builder, story-and-image in one place. Beki
/// needs a different shape: the model varies per stage, the system prompt is a versioned
/// document, and every stage returns schema-bound JSON that ordinary code then validates.
/// Mixing the two would make both harder to reason about.
/// </summary>
public sealed class BekiOpenAiClient(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiOptions> openAiOptions,
    IOptions<BekiOptions> bekiOptions,
    ILogger<BekiOpenAiClient> logger) : IBekiOpenAiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OpenAiOptions _openAi = openAiOptions.Value;
    private readonly BekiOptions _beki = bekiOptions.Value;

    public Task<T?> CompleteJsonAsync<T>(
        string model,
        string systemPrompt,
        object userPayload,
        CancellationToken cancellationToken,
        object? responseSchema = null) where T : class =>
        CompleteJsonWithImagesAsync<T>(model, systemPrompt, userPayload, [], cancellationToken, responseSchema);

    public async Task<T?> CompleteJsonWithImagesAsync<T>(
        string model,
        string systemPrompt,
        object userPayload,
        IReadOnlyList<BekiImageAttachment> images,
        CancellationToken cancellationToken,
        object? responseSchema = null) where T : class
    {
        var raw = await SendAsync(model, systemPrompt, userPayload, images, jsonMode: true, responseSchema, cancellationToken);

        // Models occasionally wrap JSON in prose or a code fence despite json_object mode.
        var json = ModelJsonSanitizer.ExtractJsonObject(raw);
        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("Beki call to {Model} returned no JSON object. First 300 chars: {Preview}",
                model, Preview(raw));
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            // Not fatal: the caller hands the validator errors to the repair prompt.
            logger.LogWarning(ex, "Beki call to {Model} returned unparsable JSON. First 300 chars: {Preview}",
                model, Preview(json));
            return null;
        }
    }

    public async Task<string> CompleteTextWithImagesAsync(
        string model,
        string systemPrompt,
        object userPayload,
        IReadOnlyList<BekiImageAttachment> images,
        CancellationToken cancellationToken)
    {
        var raw = await SendAsync(model, systemPrompt, userPayload, images, jsonMode: false, null, cancellationToken);
        return raw.Trim();
    }

    public async Task<byte[]> GenerateImageAsync(
        string prompt,
        IReadOnlyList<BekiImageAttachment> references,
        string size,
        string quality,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAi.ApiKey);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(120, _beki.StoryTimeoutSeconds));

        // The image tool is driven through the Responses API so reference images can travel
        // with the instruction. The Images endpoint alone takes a prompt and nothing else,
        // which is exactly what made the previous flow drift between pages.
        var content = new List<object> { new { type = "input_text", text = prompt } };
        foreach (var reference in references)
        {
            content.Add(new { type = "input_text", text = $"[{reference.Label}]" });
            content.Add(new
            {
                type = "input_image",
                image_url = $"data:{reference.ContentType};base64,{Convert.ToBase64String(reference.Bytes)}",
            });
        }

        var payload = new
        {
            model = _beki.VisualPromptModel,
            input = new object[] { new { role = "user", content } },
            tools = new object[]
            {
                new
                {
                    type = "image_generation",
                    model = _beki.ImageModel,
                    size,
                    quality,
                },
            },
            tool_choice = new { type = "image_generation" },
        };

        using var response = await client.PostAsJsonAsync("responses", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Beki image generation failed with {Status}: {Body}",
                (int)response.StatusCode, Preview(body, 600));
            throw new InvalidOperationException($"Beki image generation failed ({(int)response.StatusCode}).");
        }

        var bytes = ExtractImageResult(body);
        if (bytes is null || bytes.Length == 0)
        {
            throw new InvalidOperationException("Beki image generation returned no image data.");
        }

        return bytes;
    }

    private static byte[]? ExtractImageResult(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) ||
                !string.Equals(type.GetString(), "image_generation_call", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (item.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
            {
                var base64 = result.GetString();
                if (!string.IsNullOrWhiteSpace(base64))
                {
                    return Convert.FromBase64String(base64);
                }
            }
        }

        return null;
    }

    private async Task<string> SendAsync(
        string model,
        string systemPrompt,
        object userPayload,
        IReadOnlyList<BekiImageAttachment> images,
        bool jsonMode,
        object? responseSchema,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAi.ApiKey);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(60, _beki.StoryTimeoutSeconds));

        // The response-format API rejects a JSON format unless the word "json" appears in
        // the input itself — having it only in `instructions` returns a 400.
        var preamble = jsonMode
            ? "Return only a valid JSON object matching the required schema.\n\n"
            : string.Empty;

        var content = new List<object>
        {
            new { type = "input_text", text = preamble + JsonSerializer.Serialize(userPayload, JsonOptions) },
        };

        // Label each image in text immediately before attaching it, so the prompt's
        // "Reference Image A / B / C" wording binds to a specific picture rather than to
        // whatever order the API happened to preserve.
        foreach (var image in images)
        {
            content.Add(new { type = "input_text", text = $"[{image.Label}]" });
            content.Add(new
            {
                type = "input_image",
                image_url = $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Bytes)}",
            });
        }

        /*
          Sending the actual schema matters more than it looks. The prompts say "return JSON
          matching story-output-v1.schema.json", but that is a filename the model cannot see:
          asked that way it writes good Georgian and quietly omits childName, ageBand and
          every page's charactersPresent, bekiPresent and childAgencyEn — which are exactly
          the fields the validator and the whole visual pipeline depend on. Attaching the
          schema turned a book with 19 validation errors into one with none, and made the
          call faster, because the model is no longer guessing the shape.
        */
        object format = responseSchema is not null
            ? new
            {
                type = "json_schema",
                name = "beki_structured_output",
                schema = responseSchema,
                strict = false,
            }
            : new { type = "json_object" };

        object payload = jsonMode
            ? new
            {
                model,
                instructions = systemPrompt,
                input = new object[] { new { role = "user", content } },
                text = new { format },
            }
            : new
            {
                model,
                instructions = systemPrompt,
                input = new object[] { new { role = "user", content } },
            };

        using var response = await client.PostAsJsonAsync("responses", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The body carries the actual reason — an unknown model name, a size limit, a
            // content refusal — and losing it turns every failure into "something broke".
            logger.LogError("Beki call to {Model} failed with {Status}: {Body}",
                model, (int)response.StatusCode, Preview(body, 600));
            throw new InvalidOperationException($"Beki OpenAI call failed ({(int)response.StatusCode}): {Preview(body, 400)}");
        }

        return ExtractOutputText(body);
    }

    private static string Preview(string value, int length = 300) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[..Math.Min(length, value.Length)];

    private static string ExtractOutputText(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("output_text", out var direct) && direct.ValueKind == JsonValueKind.String)
        {
            return direct.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        // Reasoning models emit non-message items before the answer; concatenating every
        // text segment is more robust than assuming the first one is the payload.
        var builder = new System.Text.StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var contentArray) || contentArray.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var segment in contentArray.EnumerateArray())
            {
                if (segment.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    builder.Append(text.GetString());
                }
            }
        }

        return builder.ToString();
    }
}
