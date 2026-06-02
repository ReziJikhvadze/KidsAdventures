using System.Net.Http.Headers;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Text + images via OpenAI Responses API (images/generations used as fallback).
/// See https://developers.openai.com/api/docs/guides/images-vision
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
        var base64 = Convert.ToBase64String(imageBytes);
        var dataUrl = $"data:{mime};base64,{base64}";

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
                                   " Reply with one short paragraph only: hair, skin tone, eye color, clothing colors, and age-appropriate friendly look. No markdown."
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

    public async Task<byte[]> GenerateStoryImageAsync(string imagePrompt, CancellationToken cancellationToken)
    {
        if (!_options.EnableStoryImages)
        {
            throw new InvalidOperationException("Story images are disabled in configuration.");
        }

        if (string.Equals(_options.ImageGenerationProvider, "dall-e", StringComparison.OrdinalIgnoreCase))
        {
            return await GenerateStoryImageViaImagesApiAsync(imagePrompt, cancellationToken);
        }

        try
        {
            return await GenerateStoryImageViaResponsesApiAsync(imagePrompt, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Responses API image_generation failed; falling back to Images API.");
            return await GenerateStoryImageViaImagesApiAsync(imagePrompt, cancellationToken);
        }
    }

    private async Task<byte[]> GenerateStoryImageViaResponsesApiAsync(string imagePrompt, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var model = string.IsNullOrWhiteSpace(_options.ImageModel) ? _options.Model : _options.ImageModel;

        var payload = new
        {
            model,
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

    private async Task<byte[]> GenerateStoryImageViaImagesApiAsync(string imagePrompt, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var imageModel = string.IsNullOrWhiteSpace(_options.ImageModel) ? "dall-e-3" : _options.ImageModel;

        var payload = new
        {
            model = imageModel,
            prompt = imagePrompt,
            n = 1,
            size = _options.ImageSize,
            quality = _options.ImageQuality,
            response_format = "b64_json"
        };

        using var response = await client.PostAsJsonAsync("images/generations", payload, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI Images API failed: {responseText}");
        }

        using var doc = JsonDocument.Parse(responseText);
        var data = doc.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("OpenAI Images API response contained no data.");
        }

        var b64 = data[0].GetProperty("b64_json").GetString();
        if (string.IsNullOrWhiteSpace(b64))
        {
            throw new InvalidOperationException("OpenAI Images API response missing b64_json.");
        }

        return Convert.FromBase64String(b64);
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
