using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// One serializer configuration for the whole engine.
///
/// Enums travel as names rather than numbers. A schema that says <c>"emotion": 3</c> asks a
/// model to remember an ordinal, and an engine that reorders an enum later would silently
/// reinterpret every stored book. Names cost a few tokens and remove that entire class of bug.
/// </summary>
public static class StoryJson
{
    public static readonly JsonSerializerOptions Options = Create();

    /// <summary>Indented, for prompts and logs where a human has to read the thing.</summary>
    public static readonly JsonSerializerOptions Readable = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented = false) => new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = indented,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static string Describe<T>(T value) => JsonSerializer.Serialize(value, Readable);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException($"The model returned no usable {typeof(T).Name}.");
}
