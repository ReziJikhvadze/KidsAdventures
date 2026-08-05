using System.Text.Json;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// The JSON schema the planner must answer in.
///
/// Built rather than hand-written so the enums can never drift from the C# ones: an emotion
/// added to <see cref="StoryEmotion"/> appears here automatically, and a schema listing a value
/// the code cannot parse is impossible by construction. That mattered in practice — an earlier
/// pipeline shipped prompts naming a schema file it never actually sent, and attaching the real
/// one took nineteen validation errors to zero.
///
/// Strict mode requires every property to be listed as required and additionalProperties to be
/// false everywhere. That is stricter than the domain, which allows some collections to be
/// empty, but the cost is only that the model must send an empty array explicitly rather than
/// omitting the field — and in exchange nothing arrives half-shaped.
/// </summary>
public static class BlueprintSchema
{
    public const string Name = "story_blueprint";

    public static JsonElement Build()
    {
        var schema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[]
            {
                "promise", "answer", "emotionCurve", "locations", "objects",
                "cast", "threads", "surprises", "beats"
            },
            properties = new Dictionary<string, object>
            {
                ["promise"] = Text("The one question this book asks, in a single sentence."),
                ["answer"] = Text("How that question is answered by the final page."),
                ["emotionCurve"] = new
                {
                    type = "array",
                    description = "One emotion per page, in page order. Must match the beats exactly.",
                    items = EnumOf<StoryEmotion>()
                },
                ["locations"] = ArrayOf(LocationSchema(), "Every place the story visits."),
                ["objects"] = ArrayOf(ObjectSchema(), "Every object that matters. Declaring them is what makes continuity checkable."),
                ["cast"] = new
                {
                    type = "array",
                    description = "Character ids from the casting bible. Use only ids you were given.",
                    items = new { type = "string" }
                },
                ["threads"] = ArrayOf(ThreadSchema(), "Set-ups this book owes the reader a payoff for."),
                ["surprises"] = ArrayOf(SurpriseSchema(), "Deliberate unexpectedness. Each must be used on a real page."),
                ["beats"] = ArrayOf(BeatSchema(), "One entry per page, in order.")
            }
        };

        return JsonSerializer.SerializeToElement(schema, StoryJson.Options);
    }

    private static object BeatSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[]
        {
            "page", "goal", "obstacle", "discovery", "action", "purpose", "emotion", "energy",
            "locationId", "timeOfDay", "weather", "charactersPresent", "objectsIntroduced",
            "objectsUsed", "deltas", "hook", "threadRefs"
        },
        properties = new Dictionary<string, object>
        {
            ["page"] = new { type = "integer", description = "1-based page number." },
            ["goal"] = Text("What the hero is trying to do. No two pages may share a goal."),
            ["obstacle"] = Text("What stands in the way. A page without one is not a story page."),
            ["discovery"] = Text("What changes in what anyone knows."),
            ["action"] = Text("The single action an illustration can show."),
            ["purpose"] = EnumOf<NarrativePurpose>("What this page is structurally for."),
            ["emotion"] = EnumOf<StoryEmotion>("How this page feels."),
            ["energy"] = EnumOf<NarrativeEnergy>("The tempo of this page, independent of its feeling."),
            ["locationId"] = Text("An id from locations."),
            ["timeOfDay"] = EnumOf<TimeOfDay>(),
            ["weather"] = EnumOf<Weather>(),
            ["charactersPresent"] = new
            {
                type = "array",
                description = "Character ids visible on this page. The hero is usually among them.",
                items = new { type = "string" }
            },
            ["objectsIntroduced"] = new
            {
                type = "array",
                description = "Object ids appearing for the first time here. Empty array if none.",
                items = new { type = "string" }
            },
            ["objectsUsed"] = new
            {
                type = "array",
                description = "Object ids used here. Each must have been introduced on this page or earlier.",
                items = new { type = "string" }
            },
            ["deltas"] = ArrayOf(DeltaSchema(), "What this page changes. Every page must change something."),
            ["hook"] = new
            {
                type = new[] { "string", "null" },
                description = "The question this page leaves open. Null only on the final page."
            },
            ["threadRefs"] = new
            {
                type = "array",
                description = "Thread ids planted or paid off here. Empty array if none.",
                items = new { type = "string" }
            }
        }
    };

    private static object DeltaSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "kind", "target", "value" },
        properties = new Dictionary<string, object>
        {
            ["kind"] = EnumOf<DeltaKind>("The kind of change."),
            ["target"] = Text("Object id, location id, character id, or an enum name, depending on kind."),
            ["value"] = new
            {
                type = new[] { "string", "null" },
                description = "Second operand where one is needed, otherwise null."
            }
        }
    };

    private static object LocationSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "id", "name", "sensoryAnchors" },
        properties = new Dictionary<string, object>
        {
            ["id"] = Text("Short slug, e.g. 'glass-clearing'."),
            ["name"] = Text("The name a reader sees."),
            ["sensoryAnchors"] = new
            {
                type = "array",
                description = "Concrete things to see, hear or smell here. Two or three.",
                items = new { type = "string" }
            }
        }
    };

    private static object ObjectSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "id", "name", "significance" },
        properties = new Dictionary<string, object>
        {
            ["id"] = Text("Short slug, e.g. 'golden-key'."),
            ["name"] = Text("The name a reader sees."),
            ["significance"] = Text("Why it matters. An object that cannot answer this is decoration and will be rejected.")
        }
    };

    private static object ThreadSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "id", "kind", "setup", "payoff", "setupPage", "payoffPage" },
        properties = new Dictionary<string, object>
        {
            ["id"] = Text("Short slug."),
            ["kind"] = EnumOf<ThreadKind>(),
            ["setup"] = Text("What is planted."),
            ["payoff"] = Text("What lands later."),
            ["setupPage"] = new { type = "integer" },
            ["payoffPage"] = new { type = "integer", description = "Must be later than setupPage." }
        }
    };

    private static object SurpriseSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "kind", "description", "usedOnPage" },
        properties = new Dictionary<string, object>
        {
            ["kind"] = EnumOf<SurpriseKind>(),
            ["description"] = Text("The unexpected thing, in one line."),
            ["usedOnPage"] = new { type = "integer" }
        }
    };

    private static object Text(string description) => new { type = "string", description };

    private static object ArrayOf(object items, string description) =>
        new { type = "array", description, items };

    /// <summary>
    /// Enum values read straight off the C# type, camel-cased to match the serializer. This is
    /// the join that stops the schema and the code disagreeing.
    /// </summary>
    private static object EnumOf<TEnum>(string? description = null) where TEnum : struct, Enum
    {
        var values = Enum.GetNames<TEnum>()
            .Select(JsonNamingPolicy.CamelCase.ConvertName)
            .ToArray();

        return description is null
            ? new { type = "string", @enum = values }
            : new { type = "string", description, @enum = values };
    }
}
