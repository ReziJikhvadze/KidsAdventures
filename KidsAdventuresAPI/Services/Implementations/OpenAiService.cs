using System.Net.Http.Headers;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class OpenAiService(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiOptions> options) : IOpenAiService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenAiOptions _options = options.Value;

    public async Task<AdventureContentDto> GenerateAdventureContentAsync(AdventureGenerationInput input, CancellationToken cancellationToken)
    {
        var familyMembersText = input.FamilyMembers.Count == 0
            ? "No family members provided."
            : string.Join(", ", input.FamilyMembers);

        var prompt = string.Join(Environment.NewLine, new[]
        {
            "You are creating a personalized kids adventure pack.",
            "Return ONLY valid JSON matching this exact schema:",
            "{",
            "  \"title\": \"\",",
            "  \"theme\": \"\",",
            "  \"childName\": \"\",",
            "  \"storyPages\": [{ \"title\": \"\", \"content\": \"\" }],",
            "  \"activities\": [{ \"type\": \"\", \"title\": \"\", \"content\": \"\" }],",
            "  \"certificate\": { \"title\": \"\", \"text\": \"\" }",
            "}",
            string.Empty,
            "Rules:",
            "- Make the child the main hero.",
            "- Include all family members as supporting characters.",
            $"- Keep language age-appropriate for age {input.Age}.",
            "- Keep the tone positive and educational.",
            "- Create 4 story pages.",
            "- Create 5 activities including quizzes, puzzles, and drawing challenges.",
            "- Never include markdown, explanations, or extra text outside JSON.",
            string.Empty,
            "Input:",
            $"Child Name: {input.ChildName}",
            $"Child Age: {input.Age}",
            $"Theme: {input.Theme}",
            $"Family Members: {familyMembersText}"
        });

        var client = httpClientFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

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
