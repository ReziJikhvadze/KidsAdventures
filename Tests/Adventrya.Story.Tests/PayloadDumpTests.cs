using System.Text.Json;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Prompts;
using Xunit.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// Prints exactly what the live engine sends to OpenAI.
///
/// Not an assertion so much as a window. Prompts are assembled from a dozen places and then
/// thrown away after the call, so "what did we actually send" has until now been a question
/// nobody could answer without reading six files and guessing at the random seeds. This builds
/// the real payloads from the real builders and writes them out.
///
///   dotnet test --filter PayloadDump -v normal
/// </summary>
public class PayloadDumpTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>The whole of V4, with and without the optional parent wish.</summary>
    [Fact]
    public void Dump_the_v4_call()
    {
        var input = new MasterStoryInput
        {
            ChildName = "თამარი",
            Age = 3,
            Gender = "girl",
            Theme = ThemeType.Dinosaurs,
            EyeColor = "green",
            SpreadCount = BookFormat.SpreadCount,
            Language = "ka"
        };

        output.WriteLine("--- instructions ---");
        output.WriteLine(MasterStoryPromptV4.System(input));
        output.WriteLine("");
        output.WriteLine("--- input, no wish ---");
        output.WriteLine(MasterStoryPromptV4.User(input));
        output.WriteLine("--- input, with a wish ---");
        output.WriteLine(MasterStoryPromptV4.User(input with { ExtraWishes = "ძალიან უყვარს მელიები" }));
    }

    /// <summary>
    /// Both halves of a V3 book, for the exact input the sweep used. Deterministic given the
    /// input and the chain, so this is the same text those books were written from.
    /// </summary>
    [Fact]
    public void Dump_the_v3_calls()
    {
        var input = new MasterStoryInput
        {
            ChildName = "თამარი",
            Age = 3,
            Gender = "girl",
            Theme = ThemeType.Dinosaurs,
            EyeColor = "green",
            AppearanceDescription = null,
            SpreadCount = BookFormat.SpreadCount,
            Language = "ka"
        };

        var branch = StoryBranches.All(ThemeType.Dinosaurs)[0];

        output.WriteLine("########## CALL 1 — ARCHITECT ##########");
        output.WriteLine("");
        output.WriteLine("--- instructions ---");
        output.WriteLine(MasterStoryPromptV3.PlannerSystem(input, branch));
        output.WriteLine("");
        output.WriteLine("--- input ---");
        output.WriteLine(MasterStoryPromptV3.PlannerUser(input));
        output.WriteLine("");
        output.WriteLine("########## CALL 2 — WRITER ##########");
        output.WriteLine("");
        output.WriteLine("--- instructions ---");
        output.WriteLine(MasterStoryPromptV3.WriterSystem(input));
        output.WriteLine("");
        output.WriteLine("--- input: the plan from call 1, plus ---");
        output.WriteLine(MasterStoryPromptV3.WriterUser(
            SamplePlan(), "{ … the architect's JSON … }", branch));
    }

    private static StoryPlan SamplePlan() => new()
    {
        StoryTitle = "…",
        RefrainPhrase = "ნელა, თამარ, ნელა!",
        CharacterLock = "…",
        CharacterManifest = [new PlannedCharacter { Name = "ბუბუ", Role = "პატარა დინოზავრი", IntroducedInSpread = 4 }],
        Outline = Enumerable.Range(1, BookFormat.SpreadCount)
            .Select(n => new PlannedSpread { SpreadNumber = n, Title = "…", PlotSummary = "…", ChildAction = "…" })
            .ToList()
    };

    /// <summary>Everything the master call sends, as it is sent, with nothing in flight.</summary>
    [Fact]
    public void Dump_the_master_story_call()
    {
        var input = new MasterStoryInput
        {
            ChildName = "იაკო",
            Age = 2,
            Gender = "girl",
            Theme = ThemeType.Dinosaurs,
            EyeColor = "green",
            AppearanceDescription =
                "The child has dark brown hair gathered high, with short wavy curls. Light skin, "
                + "rounded dark brown eyes, a softly oval face with rounded cheeks and a small "
                + "nose. She wears a mustard-yellow turtleneck and loose black trousers.",
            SpreadCount = BookFormat.SpreadCount,
            Language = "ka"
        };

        var system = MasterStoryPrompt.System(input);
        var user = MasterStoryPrompt.User(input);

        output.WriteLine("=== instructions (system) ===");
        output.WriteLine(system);
        output.WriteLine("");
        output.WriteLine("=== input (user) ===");
        output.WriteLine(user);
        output.WriteLine("");
        output.WriteLine($"system: {system.Length} chars   user: {user.Length} chars");
    }

    [Fact]
    public void Dump_the_image_call()
    {
        var input = LiveInput();
        var page = new StoryPageDto
        {
            Title = "The glass clearing",
            Caption = "The chimes begin to sing",
            Content = "Nini pressed her hand to the nearest glass tree and the whole clearing rang."
        };

        var prompt = AdventurePromptBuilder.BuildStoryImagePrompt(
            input, page, pageIndex: 2, Guid.Parse("11111111-2222-3333-4444-555555555555"),
            hasCharacterAnchor: true, castPhotos: []);

        var payload = new
        {
            model = "gpt-5.6-luna",
            input = prompt,
            tools = new[]
            {
                new
                {
                    type = "image_generation",
                    model = "gpt-image-1.5",
                    size = "1024x1536",
                    quality = "medium"
                }
            }
        };

        output.WriteLine("=== POST https://api.openai.com/v1/responses  (illustration) ===");
        output.WriteLine(JsonSerializer.Serialize(payload, Pretty));
        output.WriteLine("");
        output.WriteLine($"prompt characters: {prompt.Length}");
    }

    [Fact]
    public void Dump_the_vision_call()
    {
        var prompt = AdventurePromptBuilder.BuildHeroPhotoDescribePrompt("ka", "ნინი", 5);

        var payload = new
        {
            model = "gpt-5.6-luna",
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = prompt },
                        new { type = "input_image", image_url = "data:image/jpeg;base64,<the uploaded photo>" }
                    }
                }
            }
        };

        output.WriteLine("=== POST https://api.openai.com/v1/responses  (describe the photo) ===");
        output.WriteLine(JsonSerializer.Serialize(payload, Pretty));
    }

    /// <summary>The inputs a real Georgian preview carries, as the frontend sends them.</summary>
    private static AdventureGenerationInput LiveInput() => new()
    {
        ChildName = "ნინი",
        Age = 5,
        Gender = "girl",
        Theme = ThemeType.Space,
        StoryLanguage = "ka",
        StoryPageCount = 6,
        ChildAppearanceDescription =
            "მოკლე მუქი ყავისფერი თმა ორ კუდად, მოყავისფრო თვალები, თბილი კანის ტონი, ღიმილიანი მრგვალი სახე",
        OptionalStoryNotes = "ძალიან უყვარს ვარსკვლავები",
        FamilyMembers = [],
        ChapterNumber = 1
    };
}
