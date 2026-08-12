using System.Text.Json;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// The shape the master call must answer in.
///
/// The operator's original prompt asked for sections A to I as readable prose, which is right
/// for a person and wrong for a program: a heading can be renamed, reordered or quietly dropped,
/// and nothing notices until a book is missing its illustrations. The content is unchanged — the
/// same concept, story and prompts — but the container is a schema the provider enforces
/// rather than a layout the model is asked to remember.
/// </summary>
public static class MasterStorySchema
{
    public const string Name = "master_story";

    public static JsonElement Build(int spreadCount = BookFormat.SpreadCount)
    {
        var schema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "concept", "spreads", "characterLock", "cover" },
            properties = new Dictionary<string, object>
            {
                ["concept"] = ConceptSchema(),
                ["spreads"] = new
                {
                    type = "array",
                    description =
                        $"Exactly {spreadCount} spreads, numbered 1 to {spreadCount}, in order. "
                        + "Each is one scene: a picture on one page and its words on the facing page. "
                        // Every book is one chapter of a series, and the last page used to simply
                        // stop. A thread left open is what makes a child ask for the next one.
                        + "The last spread settles what this book began and then leaves one thread "
                        + "open — a door not yet gone through, a map with a further mark, a promise "
                        + "to meet again — so the next book has somewhere to start. A hook, not a "
                        + "cliffhanger: nothing frightening, and nothing the child was worried "
                        + "about left unresolved.",
                    items = SpreadSchema()
                },
                ["characterLock"] = new
                {
                    type = "string",
                    description =
                        "ENGLISH ONLY. One paragraph describing every recurring character's "
                        + "appearance: face, hair, eyes, skin, build, and the exact clothing worn "
                        + "in every scene. Appearance only — no instruction about the photograph "
                        + "and no scene. Written once; it is placed into every illustration "
                        + "prompt automatically, so it must not be repeated anywhere else."
                },
                ["cover"] = IllustrationSchema(
                    "The cover scene. It carries no story text, so compose it as a portrait of the "
                    + "hero in this world, with calm space where a title will be placed.")
            }
        };

        return JsonSerializer.SerializeToElement(schema, StoryJson.Options);
    }

    private static object ConceptSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "title", "outline" },
        properties = new Dictionary<string, object>
        {
            ["title"] = Text("The book's title."),
            ["outline"] = new
            {
                type = "array",
                description = "Five to eight beats, in order.",
                items = new { type = "string" }
            }
        }
    };

    private static object SpreadSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "number", "title", "caption", "text", "illustration" },
        properties = new Dictionary<string, object>
        {
            ["number"] = new { type = "integer", description = "1-based scene number." },
            ["title"] = Text("Short heading for this scene."),
            ["caption"] = Text("The short line beside the picture. Two to five words."),
            ["text"] = Text(
                "The read-aloud text for this scene: three to five short sentences. It has a page "
                + "to itself, but filling the page is not the goal — a child follows the picture "
                + "and the voice, not the length of the paragraph."),
            ["illustration"] = IllustrationSchema(
                "The picture for this scene, on the facing page.")
        }
    };

    private static object IllustrationSchema(string description) => new
    {
        type = "object",
        description,
        additionalProperties = false,
        required = new[] { "scene", "avoid" },
        properties = new Dictionary<string, object>
        {
            // The scene only. The photograph instruction, the house style, the format rule and
            // the standard exclusions are added by our code, so writing them here would be nine
            // copies of text that never changes — measured at roughly two thirds of everything
            // the model produced for a book.
            ["scene"] = new
            {
                type = "string",
                description =
                    "ENGLISH ONLY. This picture, and nothing else — no style, no format, no "
                    + "instruction about the photograph, and no description of the characters' "
                    + "permanent appearance. In one flowing paragraph: which moment of the text "
                    + "is shown, the main action, what the characters feel, the place with its "
                    + "objects, weather and time of day, and the shot and camera angle. "
                    // This used to end by asking for "the details the picture needs for the story
                    // to read without words", which is an invitation to invent: a star turned up
                    // on page one of a story that never mentioned one. The picture illustrates
                    // the text; it does not add to it.
                    + "Draw only what this spread's own text says is there. Do not add any "
                    + "object, creature, character or symbol the text does not mention."
            },
            ["avoid"] = new
            {
                type = "string",
                description =
                    "ENGLISH ONLY. Only what would go wrong in THIS picture — a hazard the scene "
                    + "could imply, a character who should not appear. Leave it empty when there "
                    + "is nothing particular; identity and artefact exclusions are always added."
            }
        }
    };

    private static object Text(string description) => new { type = "string", description };

    /// <summary>
    /// The negative prompt every illustration falls back to, so a page is never sent without
    /// one. Identity failures lead because they are the ones a parent notices immediately.
    /// </summary>
    public const string DefaultNegativePrompt =
        "changed identity, generic face, excessive facial stylization, inaccurate facial "
        + "proportions, different eye shape, different nose, different hairstyle, different skin "
        + "tone, incorrect age, altered body type, unrealistic body proportions, changed clothing, "
        + "extra accessories, asymmetrical eyes, distorted face, malformed hands, extra fingers, "
        + "missing fingers, duplicate person, blurry face, low detail, frightening expression, "
        + "text, captions, watermark, logo";
}
