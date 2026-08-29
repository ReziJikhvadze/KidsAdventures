using System.Net.Http.Headers;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Services;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Text + images via OpenAI Responses API; reference-photo illustrations via Images Edit (gpt-image-1.5).
/// </summary>
public sealed class OpenAiService(
    IHttpClientFactory httpClientFactory,
    IReferenceImageNormalizer referenceImageNormalizer,
    IOptions<OpenAiOptions> options,
    ILogger<OpenAiService> logger) : IOpenAiService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenAiOptions _options = options.Value;

    public async Task<AdventureContentDto> GenerateAdventureContentAsync(
        AdventureGenerationInput input,
        Guid adventureId,
        CancellationToken cancellationToken)
    {
        var prompt = AdventurePromptBuilder.BuildStoryPrompt(input, adventureId);

        var client = CreateClient();
        var payload = new
        {
            model = _options.Model,
            input = prompt,
            text = new
            {
                format = new { type = "json_object" }
            }
        };

        using var response = await client.PostAsJsonAsync("responses", payload, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        var outputText = ModelJsonSanitizer.ExtractJsonObject(ExtractOutputText(responseText));
        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new InvalidOperationException("OpenAI output was empty.");
        }

        AdventureContentDto content;
        try
        {
            content = JsonSerializer.Deserialize<AdventureContentDto>(outputText, JsonOptions)
                      ?? throw new InvalidOperationException("Failed to parse OpenAI JSON output.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "OpenAI returned non-JSON story output (first 200 chars): {Preview}",
                outputText.Length > 200 ? outputText[..200] : outputText);
            throw new InvalidOperationException("Story model returned invalid JSON. Please try again.");
        }

        if (content.StoryPages.Count == 0)
        {
            throw new InvalidOperationException("Generated content is incomplete.");
        }

        var expectedPages = input.StoryPageCount > 0 ? input.StoryPageCount : AdventureStoryConstants.LegacyPageCount;
        if (expectedPages > AdventureStoryConstants.LegacyPageCount)
        {
            expectedPages = AdventureStoryConstants.LegacyPageCount;
        }

        if (content.StoryPages.Count > expectedPages)
        {
            logger.LogWarning(
                "Trimming {Actual} story pages down to {Expected} for adventure {AdventureId}",
                content.StoryPages.Count,
                expectedPages,
                adventureId);
            content.StoryPages = content.StoryPages.Take(expectedPages).ToList();
        }

        if (content.StoryPages.Count != expectedPages)
        {
            logger.LogWarning(
                "Expected {Expected} story pages but model returned {Actual} for adventure {AdventureId}",
                expectedPages,
                content.StoryPages.Count,
                adventureId);
        }

        return content;
    }

    public async Task<string> DescribeCharacterFromPhotoAsync(
        byte[] imageBytes,
        string contentType,
        string promptText,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var normalized = referenceImageNormalizer.NormalizeForOpenAi(imageBytes, contentType);
        var dataUrl = ToDataUrl(normalized.Bytes, normalized.ContentType);

        var payload = new
        {
            model = _options.Model,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "input_text",
                            text = promptText
                        },
                        new
                        {
                            type = "input_image",
                            image_url = dataUrl
                        }
                    }
                }
            }
        };

        using var response = await client.PostAsJsonAsync("responses", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI vision failed: {body}");
        }

        var text = ExtractOutputText(body);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("OpenAI vision returned empty description.");
        }

        return text.Trim();
    }

    public async Task<string> CompleteTextAsync(string promptText, CancellationToken cancellationToken)
    {
        var client = CreateClient();

        var payload = new
        {
            model = _options.Model,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = promptText }
                    }
                }
            }
        };

        using var response = await client.PostAsJsonAsync("responses", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI text completion failed: {body}");
        }

        return (ExtractOutputText(body) ?? string.Empty).Trim();
    }

    /// <summary>
    /// QA IMAGE — the third of the Beki format's three model tasks.
    ///
    /// Built on the same vision call as <see cref="DescribeCharacterFromPhotoAsync"/> rather than
    /// on a new client, and deliberately unopinionated: it hands back whatever JSON the model
    /// produced instead of parsing it into a verdict type. The prototype is here to find out what
    /// the model actually says and how often, and a parser written before that is known would
    /// quietly discard the cases worth reading.
    ///
    /// The generated image comes first in the payload, then the references, each announced by a
    /// line of text — an image model given three pictures and no labels has no way to know which
    /// one it is being asked to judge.
    /// </summary>
    public async Task<string> ReviewIllustrationAsync(
        byte[] imageBytes,
        string reviewPrompt,
        IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();

        var generated = referenceImageNormalizer.NormalizeForOpenAi(imageBytes, "image/png");
        var content = new List<object>
        {
            new { type = "input_text", text = reviewPrompt },
            new { type = "input_text", text = "[The generated illustration under review]" },
            new { type = "input_image", image_url = ToDataUrl(generated.Bytes, generated.ContentType) },
        };

        foreach (var (bytes, contentType, label) in references)
        {
            var normalized = referenceImageNormalizer.NormalizeForOpenAi(bytes, contentType);
            content.Add(new { type = "input_text", text = $"[{label}]" });
            content.Add(new { type = "input_image", image_url = ToDataUrl(normalized.Bytes, normalized.ContentType) });
        }

        var payload = new
        {
            model = _options.Model,
            input = new object[] { new { role = "user", content } },
        };

        using var response = await client.PostAsJsonAsync("responses", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI image QA failed: {body}");
        }

        return ExtractOutputText(body).Trim();
    }

    public async Task<byte[]> GenerateStoryImageAsync(
        string imagePrompt,
        StoryImageReference? reference,
        CancellationToken cancellationToken,
        string? imageSize = null,
        bool requireReferences = false)
    {
        if (!_options.EnableStoryImages)
        {
            throw new InvalidOperationException("Story images are disabled in configuration.");
        }

        // Null means the configured size, which is what every caller but the Beki prototype
        // passes. Resolved once here so the three routes below cannot disagree about it.
        var size = string.IsNullOrWhiteSpace(imageSize) ? _options.ImageSize : imageSize!;

        var referenceImages = CollectReferenceImages(reference);

        if (_options.LogPrompts)
        {
            // What we asked for, never what came back. A returned illustration is megabytes of
            // base64 and putting that in a log makes the log useless and expensive at once; the
            // prompt is the part that explains why the picture looks the way it does.
            logger.LogInformation(
                "OpenAI image request → model={Model} size={Size} quality={Quality} references={ReferenceCount} route={Route}\n" +
                "--- prompt ---\n{Prompt}",
                referenceImages.Count > 0 ? _options.ImageEditModel : _options.ImageModel,
                size,
                _options.ImageQuality,
                referenceImages.Count,
                referenceImages.Count > 0 ? "images/edits" : "images/generations",
                imagePrompt);
        }

        // A caller that needs the references cannot be handed a picture drawn without them, and
        // "there were none to send" is the same outcome as "sending them failed". Checked before
        // the call rather than after, because the failure is in the request and there is nothing to
        // learn by paying for the answer.
        if (requireReferences && referenceImages.Count == 0)
        {
            throw new InvalidOperationException(
                "This illustration must be drawn from its reference images, and none were "
                + "supplied. Drawing it from the prompt alone would produce a picture of a "
                + "different child in a different world.");
        }

        if (referenceImages.Count > 0)
        {
            try
            {
                return await GenerateStoryImageViaEditApiWithRetryAsync(
                    imagePrompt,
                    referenceImages,
                    size,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                /*
                  The fallback, and the one caller it is wrong for.

                  Drawing from the prompt alone when the edit route dies is the right trade for the
                  A5 flow: the prompt already carries a written appearance description, so what
                  comes back is a slightly-off hero rather than a stranger, and a book with one
                  weaker picture beats a failed job.

                  It is the wrong trade wherever the references ARE the description. On the
                  composite path the child's likeness exists only in the attached photograph, the
                  world only in the approved theme reference, and a recurring creature only in the
                  continuity image — so this fallback would return a picture of somebody else,
                  which is then composited with the approved Beki, reviewed, stored and printed.
                  Such a caller sets requireReferences and gets the exception instead.
                */
                if (requireReferences)
                {
                    logger.LogError(
                        ex,
                        "GPT Image edit failed after retries and this illustration may not be "
                        + "drawn without its references; failing rather than generating an "
                        + "unanchored picture.");
                    throw;
                }

                var usedPhoto = reference?.CastPhotos.Any(static c => c.Bytes is { Length: > 0 }) == true;
                logger.LogWarning(
                    ex,
                    usedPhoto
                        ? "GPT Image edit failed after retries; falling back to text-only generation (uploaded photo was not used — likeness may be lost)."
                        : "GPT Image edit failed after retries; one text-only images/generations fallback.");
            }
        }

        return await GenerateStoryImageViaImagesApiAsync(imagePrompt, size, cancellationToken);
    }

    private async Task<byte[]> GenerateStoryImageViaEditApiWithRetryAsync(
        string imagePrompt,
        IReadOnlyList<(byte[] Bytes, string FileName, string ContentType)> referenceImages,
        string size,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Clamp(_options.ImageRetryAttempts, 1, 6);
        Exception? last = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                return await GenerateStoryImageViaEditApiAsync(imagePrompt, referenceImages, size, cancellationToken);
            }
            catch (Exception ex)
            {
                last = ex;
                if (!IsRetryableOpenAiError(ex) || attempt == maxAttempts - 1)
                {
                    throw;
                }

                var delaySeconds = Math.Max(1, _options.ImageRetryBackoffSeconds) * (attempt + 1);
                logger.LogWarning(
                    ex,
                    "Image edit attempt {Attempt} hit a retryable OpenAI error; waiting {Delay}s",
                    attempt + 1,
                    delaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        throw last ?? new InvalidOperationException("Image edit failed.");
    }

    private static bool IsRetryableOpenAiError(Exception ex)
    {
        if (ex.Message.Contains("invalid_image_file", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("unsupported mimetype", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("image_generation_user_error", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ex.Message.Contains("rate_limit", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("disconnect", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<byte[]> GenerateStoryImageViaEditApiAsync(
        string imagePrompt,
        IReadOnlyList<(byte[] Bytes, string FileName, string ContentType)> referenceImages,
        string size,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();
        using var form = new MultipartFormDataContent();
        var model = ResolveGptImageEditModel();
        var quality = MapGptImageQuality(_options.ImageQuality);

        form.Add(new StringContent(model), "model");
        form.Add(new StringContent(imagePrompt), "prompt");
        form.Add(new StringContent(size), "size");
        form.Add(new StringContent(quality), "quality");

        for (var index = 0; index < referenceImages.Count; index++)
        {
            var (bytes, fileName, contentType) = referenceImages[index];
            logger.LogDebug(
                "Images edit reference {Index}: {FileName} ({ContentType}, {Bytes} bytes)",
                index + 1,
                fileName,
                contentType,
                bytes.Length);

            var imageContent = new ByteArrayContent(bytes);
            imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(imageContent, "image[]", fileName);
        }

        using var response = await client.PostAsync("images/edits", form, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI Images Edit API failed: {responseText}");
        }

        return await ExtractImageBytesFromImagesResponseAsync(responseText, cancellationToken);
    }

    private async Task<byte[]> GenerateStoryImageViaResponsesApiAsync(string imagePrompt, CancellationToken cancellationToken)
    {
        var client = CreateClient();

        // The tool was declared with no settings at all, so every illustration came back at
        // the model's own default — a 1024x1024 square — no matter what OpenAI:ImageSize and
        // OpenAI:ImageQuality were set to. This is the default provider, so that silently
        // applied to essentially every picture the product has ever produced, and made those
        // two settings look broken. A storybook page is portrait; state it explicitly.
        var payload = new
        {
            model = _options.Model,
            input = imagePrompt,
            tools = new[]
            {
                new
                {
                    type = "image_generation",
                    model = ResolveImagesApiModel(),
                    size = _options.ImageSize,
                    quality = MapGptImageQuality(_options.ImageQuality)
                }
            }
        };

        using var response = await client.PostAsJsonAsync("responses", payload, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI Responses image generation failed: {responseText}");
        }

        var imageBytes = ExtractImageGenerationResult(responseText);
        if (imageBytes is null || imageBytes.Length == 0)
        {
            throw new InvalidOperationException("OpenAI Responses image generation returned no image.");
        }

        return imageBytes;
    }

    private async Task<byte[]> GenerateStoryImageViaImagesApiAsync(
        string imagePrompt,
        string size,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var imageModel = ResolveImagesApiModel();
        var qualitySetting = _options.ImageQuality;

        var payload = new Dictionary<string, object>
        {
            ["model"] = imageModel,
            ["prompt"] = imagePrompt,
            ["n"] = 1,
            ["size"] = size
        };

        if (IsGptImageModel(imageModel))
        {
            payload["quality"] = MapGptImageQuality(qualitySetting);
        }
        else if (imageModel.Equals("dall-e-3", StringComparison.OrdinalIgnoreCase))
        {
            payload["quality"] = MapDalleQuality(_options.ImageQuality);
        }

        using var response = await client.PostAsJsonAsync("images/generations", payload, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI Images API failed: {responseText}");
        }

        return await ExtractImageBytesFromImagesResponseAsync(responseText, cancellationToken);
    }

    private List<(byte[] Bytes, string FileName, string ContentType)> CollectReferenceImages(
        StoryImageReference? reference)
    {
        var images = new List<(byte[] Bytes, string FileName, string ContentType)>();
        if (reference is null)
        {
            return images;
        }

        if (reference.CharacterAnchorBytes is { Length: > 0 } anchor)
        {
            var normalizedAnchor = referenceImageNormalizer.NormalizeForOpenAi(anchor, "image/webp");
            images.Add((normalizedAnchor.Bytes, "01-hero-anchor.png", normalizedAnchor.ContentType));
        }

        foreach (var cast in reference.CastPhotos)
        {
            if (cast.Bytes is not { Length: > 0 })
            {
                continue;
            }

            var slot = images.Count + 1;
            var slug = cast.IsHero ? "hero" : SanitizeFileSlug(cast.Name);
            var normalized = referenceImageNormalizer.NormalizeForOpenAi(cast.Bytes, cast.ContentType);
            images.Add((normalized.Bytes, $"{slot:D2}-{slug}-reference.png", normalized.ContentType));
        }

        return images;
    }

    private static string SanitizeFileSlug(string name)
    {
        var chars = name.Where(char.IsLetterOrDigit).Take(24).ToArray();
        return chars.Length > 0 ? new string(chars).ToLowerInvariant() : "cast";
    }

    private string ResolveGptImageEditModel()
    {
        if (IsGptImageModel(_options.ImageEditModel))
        {
            return _options.ImageEditModel;
        }

        if (IsGptImageModel(_options.ImageModel))
        {
            return _options.ImageModel;
        }

        return "gpt-image-1-mini";
    }

    private string ResolveImagesApiModel()
    {
        if (IsGptImageModel(_options.ImageModel) || IsDalleModel(_options.ImageModel))
        {
            return _options.ImageModel;
        }

        return "gpt-image-1-mini";
    }

    private static bool IsGptImageModel(string model) =>
        model.StartsWith("gpt-image", StringComparison.OrdinalIgnoreCase);

    private static bool IsDalleModel(string model) =>
        model.StartsWith("dall-e", StringComparison.OrdinalIgnoreCase);

    private static string MapGptImageQuality(string quality)
    {
        if (quality.Equals("hd", StringComparison.OrdinalIgnoreCase) ||
            quality.Equals("high", StringComparison.OrdinalIgnoreCase))
        {
            return "high";
        }

        if (quality.Equals("medium", StringComparison.OrdinalIgnoreCase))
        {
            return "medium";
        }

        return "low";
    }

    private static string MapDalleQuality(string quality) =>
        quality.Equals("hd", StringComparison.OrdinalIgnoreCase) ? "hd" : "standard";

    private static string ToDataUrl(byte[] bytes, string contentType) =>
        $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";

    private static async Task<byte[]> ExtractImageBytesFromImagesResponseAsync(
        string responseJson,
        CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var data = doc.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("OpenAI Images API response contained no data.");
        }

        var item = data[0];
        if (item.TryGetProperty("b64_json", out var b64Prop))
        {
            var b64 = b64Prop.GetString();
            if (!string.IsNullOrWhiteSpace(b64))
            {
                return Convert.FromBase64String(b64);
            }
        }

        if (item.TryGetProperty("url", out var urlProp))
        {
            var url = urlProp.GetString();
            if (!string.IsNullOrWhiteSpace(url))
            {
                using var http = new HttpClient();
                return await http.GetByteArrayAsync(url, cancellationToken);
            }
        }

        throw new InvalidOperationException("OpenAI Images API response missing image data.");
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        return client;
    }

    private static byte[]? ExtractImageGenerationResult(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeProp))
            {
                continue;
            }

            var type = typeProp.GetString();
            if (!string.Equals(type, "image_generation_call", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (item.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
            {
                var b64 = result.GetString();
                if (!string.IsNullOrWhiteSpace(b64))
                {
                    return Convert.FromBase64String(b64);
                }
            }
        }

        return null;
    }

    private static string ExtractOutputText(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("output_text", out var outputTextElement) && outputTextElement.ValueKind == JsonValueKind.String)
        {
            return outputTextElement.GetString() ?? string.Empty;
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

            foreach (var segment in content.EnumerateArray())
            {
                if (segment.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }
}
