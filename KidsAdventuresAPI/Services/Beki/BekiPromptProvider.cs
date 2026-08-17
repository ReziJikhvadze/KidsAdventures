using System.Collections.Concurrent;
using System.Text.Json;

namespace AdventurePacks.Api.Services.Beki;

public interface IBekiPromptProvider
{
    /// <summary>The system prompt text, read from <c>Prompts/Beki/{name}.md</c>.</summary>
    string Get(string name);

    /// <summary>
    /// The JSON Schema the model must answer in, read from
    /// <c>Prompts/Beki/schemas/{name}.schema.json</c>. Returns null when the file is absent,
    /// which downgrades the call to plain JSON mode rather than failing the book.
    /// </summary>
    object? GetSchema(string name);

    /// <summary>Stable identifier recorded against every generated asset, e.g. <c>story-generator-v1</c>.</summary>
    string VersionOf(string name);
}

/// <summary>
/// Serves the Beki system prompts from disk.
///
/// The prompts are content files rather than C# string literals for two reasons: a prompt
/// revision should be reviewable as a document diff, and the exact file that produced a
/// book has to be recoverable months later when someone asks why a story reads oddly.
/// Files are cached after first read, so a prompt change needs a restart — which is
/// intentional: prompts should not drift under a running generation.
/// </summary>
public sealed class BekiPromptProvider(ILogger<BekiPromptProvider> logger) : IBekiPromptProvider
{
    public const string StoryGenerator = "story-generator-v1";
    public const string StoryReviewer = "story-reviewer-v1";
    public const string StoryRepair = "story-repair-v1";
    public const string CharacterIdentityAnalyzer = "character-identity-analyzer-v1";
    public const string VisualBibleBuilder = "visual-bible-builder-v1";
    public const string HeroCharacterAnchor = "hero-character-anchor-v1";
    public const string CoverImageGenerator = "cover-image-generator-v1";
    public const string PageImageGenerator = "page-image-generator-v1";
    public const string VisualReviewer = "visual-reviewer-v1";
    public const string VisualRepair = "visual-repair-v1";
    public const string PortraitGate = "portrait-gate-v1";

    private static readonly string PromptDirectory =
        Path.Combine(AppContext.BaseDirectory, "Prompts", "Beki");

    private readonly ConcurrentDictionary<string, string> _cache = new();

    public string Get(string name) => _cache.GetOrAdd(name, key =>
    {
        var path = Path.Combine(PromptDirectory, key + ".md");
        if (!File.Exists(path))
        {
            // Failing loudly beats generating a book with an empty system prompt, which
            // would produce plausible-looking output that ignores every product rule.
            logger.LogError("Beki prompt {Prompt} not found at {Path}", key, path);
            throw new FileNotFoundException($"Beki prompt '{key}' is missing from the deployment.", path);
        }

        var text = File.ReadAllText(path);
        logger.LogInformation("Loaded Beki prompt {Prompt} ({Length} chars)", key, text.Length);
        return text;
    });

    /// <summary>Schema file names, which do not match the prompt names one-to-one.</summary>
    public const string StoryOutputSchema = "story-output-v1";
    public const string VisualBibleSchema = "visual-bible-v1";
    public const string VisualReviewSchema = "visual-review-v1";
    public const string PortraitGateSchema = "portrait-gate-v1";

    private readonly ConcurrentDictionary<string, object?> _schemas = new();

    public object? GetSchema(string name) => _schemas.GetOrAdd(name, key =>
    {
        var path = Path.Combine(PromptDirectory, "schemas", key + ".schema.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("Beki schema {Schema} not found at {Path}; falling back to plain JSON mode.", key, path);
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement.Clone();

        // The response-format API rejects the JSON Schema vocabulary keywords, so strip the
        // three that are metadata rather than constraints.
        var stripped = new Dictionary<string, JsonElement>();
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("$schema") || property.NameEquals("$id") || property.NameEquals("title"))
            {
                continue;
            }

            stripped[property.Name] = property.Value;
        }

        logger.LogInformation("Loaded Beki schema {Schema}", key);
        return stripped;
    });

    public string VersionOf(string name) => name;
}
