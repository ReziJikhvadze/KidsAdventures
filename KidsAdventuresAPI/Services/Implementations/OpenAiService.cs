using System.Net.Http.Headers;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Text + images via OpenAI Responses API; reference-photo illustrations via Images Edit (gpt-image-2).
/// </summary>
public sealed class OpenAiService(
    IHttpClientFactory httpClientFactory,
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
            input = prompt
        };

        using var response = await client.PostAsJsonAsync("responses", payload, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        var outputText = ExtractOutputText(responseText);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new InvalidOperationException("OpenAI output was empty.");
        }

        var content = JsonSerializer.Deserialize<AdventureContentDto>(outputText, JsonOptions)
                      ?? throw new InvalidOperationException("Failed to parse OpenAI JSON output.");

        if (content.StoryPages.Count == 0 || content.Activities.Count == 0)
        {
            throw new InvalidOperationException("Generated content is incomplete.");
        }

        return content;
    }

    public async Task<string> DescribeCharacterFromPhotoAsync(
        byte[] imageBytes,
        string contentType,
        string roleDescription,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var mime = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType;
        var dataUrl = ToDataUrl(imageBytes, mime);

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
                            text = roleDescription +
                                   " Reply with one dense paragraph for an illustrator who must match this exact child in every drawing: " +
                                   "hair color, length, texture, parting, bangs; eye color, shape, spacing; eyebrow shape; nose shape; mouth and smile; face shape and cheek fullness; skin tone; apparent age; glasses or freckles if any; clothing colors if visible. " +
                                   "Be specific and visual. No markdown."
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

    public async Task<byte[]> GenerateStoryImageAsync(
        string imagePrompt,
        StoryImageReference? reference,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableStoryImages)
        {
            throw new InvalidOperationException("Story images are disabled in configuration.");
        }

        var referenceImages = CollectReferenceImages(reference);
        var hasHeroPhoto = reference?.HeroPhotoBytes is { Length: > 0 };
        if (referenceImages.Count > 0)
        {
            try
            {
                return await GenerateStoryImageViaEditApiAsync(
                    imagePrompt,
                    referenceImages,
                    hasHeroPhoto,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "GPT Image edit with photo reference failed; falling back to Images API.");
            }
        }

        if (string.Equals(_options.ImageGenerationProvider, "dall-e", StringComparison.OrdinalIgnoreCase))
        {
            return await GenerateStoryImageViaImagesApiAsync(imagePrompt, hasHeroPhoto, cancellationToken);
        }

        try
        {
            return await GenerateStoryImageViaResponsesApiAsync(imagePrompt, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Responses API image_generation failed; falling back to Images API.");
            return await GenerateStoryImageViaImagesApiAsync(imagePrompt, hasHeroPhoto, cancellationToken);
        }
    }

    private async Task<byte[]> GenerateStoryImageViaEditApiAsync(
        string imagePrompt,
        IReadOnlyList<(byte[] Bytes, string FileName, string ContentType)> referenceImages,
        bool hasHeroPhoto,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();
        using var form = new MultipartFormDataContent();
        var model = ResolveGptImageEditModel();
        var quality = hasHeroPhoto
            ? MapGptImageQuality(_options.ImagePhotoQuality)
            : MapGptImageQuality(_options.ImageQuality);

        form.Add(new StringContent(model), "model");
        form.Add(new StringContent(imagePrompt), "prompt");
        form.Add(new StringContent(_options.ImageSize), "size");
        form.Add(new StringContent(quality), "quality");

        foreach (var (bytes, fileName, contentType) in referenceImages)
        {
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
        var payload = new
        {
            model = _options.Model,
            input = imagePrompt,
            tools = new[] { new { type = "image_generation" } }
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
        bool preferPhotoQuality,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var imageModel = ResolveImagesApiModel();
        var qualitySetting = preferPhotoQuality ? _options.ImagePhotoQuality : _options.ImageQuality;

        var payload = new Dictionary<string, object>
        {
            ["model"] = imageModel,
            ["prompt"] = imagePrompt,
            ["n"] = 1,
            ["size"] = _options.ImageSize
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

    private static List<(byte[] Bytes, string FileName, string ContentType)> CollectReferenceImages(
        StoryImageReference? reference)
    {
        var images = new List<(byte[] Bytes, string FileName, string ContentType)>();
        if (reference?.HeroPhotoBytes is { Length: > 0 } hero)
        {
            images.Add((hero, "hero-photo.jpg", reference.HeroPhotoContentType));
        }

        if (reference?.CharacterAnchorBytes is { Length: > 0 } anchor)
        {
            images.Add((anchor, "character-anchor.png", "image/png"));
        }

        return images;
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
