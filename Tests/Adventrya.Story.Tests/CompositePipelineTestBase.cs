using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Ai;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// Everything the composite-pipeline tests are built out of: the approved Nina fixtures, the
/// stubbed model doors, the synthetic pictures, and the plan and request builders.
///
/// These live on a base class rather than beside the tests because the tests are split across
/// several classes, and xUnit runs one class at a time. Splitting them is what lets the suite
/// use more than one core; sharing the helpers from here is what keeps that split free of
/// duplication. Every member is static and every fixture is immutable or handed out as a copy,
/// so the classes below share code without sharing state.
/// </summary>
public abstract class CompositePipelineTestBase
{
    protected static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "nina_dinosaurs", name);

    protected static string ScenarioFixture() =>
        File.ReadAllText(FixturePath("visual_scenario_output_v2.json"));

    /// <summary>
    /// The resolved prompt the invariants are checked against: the supplier's approved document as
    /// this campaign amended it to <c>child-world-image-v1.1</c>.
    /// </summary>
    protected static string ResolvedPromptFixture() =>
        File.ReadAllText(FixturePath("spread_01_resolved_image_prompt_v1_2.txt"));

    /// <summary>
    /// The same document for spread two: the anchored shape, where the accepted first spread leads
    /// and the photograph sits behind it. Both numbering cases exist in every book, so both are
    /// pinned to a resolved document rather than only to assertions.
    /// </summary>
    protected static string AnchoredPromptFixture() =>
        File.ReadAllText(FixturePath("spread_02_resolved_image_prompt_v1_2.txt"));

    /// <summary>
    /// The supplier's original v1 resolved prompt, kept byte-for-byte as the audit record of what a
    /// human approved a printed spread from.
    ///
    /// Not deleted and not edited, which is the point: the v1.1 amendments are only defensible next
    /// to the document they amend, and the one test that reads this file is the one that proves
    /// what actually changed — the fold naming — and what did not.
    /// </summary>
    protected static string V1PromptFixture() =>
        File.ReadAllText(FixturePath("spread_01_resolved_image_prompt_v2.txt"));

    /// <summary>Every string value anywhere in a JSON document, unescaped — so a privacy assertion
    /// is about what the document says rather than about how it happens to be encoded.</summary>
    protected static IReadOnlyList<string> Strings(JsonElement element)
    {
        var found = new List<string>();

        void Walk(JsonElement node)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.String:
                    found.Add(node.GetString() ?? string.Empty);
                    break;
                case JsonValueKind.Object:
                    foreach (var property in node.EnumerateObject()) Walk(property.Value);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in node.EnumerateArray()) Walk(item);
                    break;
            }
        }

        Walk(element);
        return found;
    }

    /// <summary>
    /// The supplied composition-manifest schema, loaded once.
    ///
    /// Once because JsonSchema.Net registers a document by its <c>$id</c> and refuses to see the
    /// same one twice — which a theory with two cases would otherwise do.
    /// </summary>
    protected static readonly Lazy<Json.Schema.JsonSchema> CompositionManifestSchema = new(() =>
        Json.Schema.JsonSchema.FromText(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Assets", "BekiComposite", "contracts",
            "composition_manifest_v1.schema.json"))));

    /// <summary>The outfit lock, which must not appear in a document stored for review.</summary>
    protected static readonly string result_outfit =
        VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!.VisualLock!.ChildOutfit!;

    // =======================================================================================
    // Harness
    // =======================================================================================

    protected sealed record TitleOnly
    {
        public string Title { get; init; } = string.Empty;
    }

    protected static string Section(string prompt, string from, string to)
    {
        var start = prompt.IndexOf(from, StringComparison.Ordinal) + from.Length;
        var end = prompt.IndexOf(to, start, StringComparison.Ordinal);
        return prompt[start..end];
    }

    /// <summary>The fixture with Beki named in a scene — the one fault that ruins a book.</summary>
    protected static string WithBekiInSceneThree()
    {
        var scenario = JsonNode.Parse(ScenarioFixture())!;
        scenario["spreads"]![2]!["child_world_scene"] =
            "Beside Bafu, the child lifts the vine while Beki hovers close by.";
        return scenario.ToJsonString();
    }

    protected static string Fail(string check, string action) =>
        $$"""
        {"status":"FAIL","failed_checks":["{{check}}"],"recommended_action":"{{action}}","notes":["a note"]}
        """;

    /// <summary>
    /// The four attributes the stub reads off the fixture photograph, and the same four the
    /// hand-resolved v1.1 fixture was written with — so a prompt built here and the approved
    /// document describe one child.
    /// </summary>
    internal static readonly ChildIdentitySpec IdentityFixture = new()
    {
        HairColor = "dark brown",
        HairStyle = "shoulder-length wavy with a soft fringe",
        EyeColor = "brown",
        SkinTone = "light warm",
        Eyebrows = "soft, medium-thick, gently arched",
        Glasses = "none",
        FaceShape = "round with a soft chin",
        DistinctiveFeatures = "light freckles across the nose; a dimple on the left cheek",
    };

    /// <summary>The INPUT IMAGES block on its own, so a test can say which image comes first.</summary>
    protected static string InputImages(string prompt) =>
        Section(prompt, "INPUT IMAGES\n", "\n\nSCENE").Trim();

    /// <summary>One spread's prompt, built from the fixture scenario the way the pipeline builds it.</summary>
    protected static string SpreadPrompt(
        VisualScenarioV2 scenario,
        int page,
        bool anchorAttached = false,
        IReadOnlyList<string>? continuityElements = null)
    {
        var spread = scenario.Spreads![page - 1];

        return CompositeIllustrationPrompt.ForSpread(new CompositeSpreadPromptInput
        {
            Page = page,
            ChildAge = 1,
            Theme = CompositeThemeReferences.For("dinosaurs"),
            ChildWorldScene = spread.ChildWorldScene!,
            ChildOutfit = scenario.VisualLock!.ChildOutfit!,
            RecurringElements = CompositeIllustrationPrompt.RelevantRecurringElements(
                scenario.VisualLock.RecurringElements, spread.ChildWorldScene),
            ContinuityElementNames = continuityElements ?? [],
            IdentitySpec = IdentityFixture,
            AnchorAttached = anchorAttached,
        });
    }

    protected static CompositeBookContext Context() => new()
    {
        JobId = Guid.NewGuid(),
        Input = new BookGenerationInput
        {
            ChildName = "ნინა",
            ChildAge = 1,
            ChildGender = "girl",
            ThemeId = "Dinosaurs",
            ChildPhotoRef = "books/nina/photo.jpg",
        }
    };

    /// <summary>One run of the pipeline, with the Nina plan and a valid photograph by default.</summary>
    protected static CompositeBookRequest Request(
        CompositeBookContext? context = null,
        CompositeResumeState? resume = null,
        Func<string, Task>? onScenario = null,
        Func<CompositeSpreadResult, Task>? onSpread = null) => new()
    {
        Context = context ?? Context(),
        ExistingPlan = Plan(),
        ChildPhoto = Photo(),
        ChildPhotoContentType = "image/png",
        Resume = resume ?? CompositeResumeState.Empty,
        OnScenario = onScenario,
        OnSpread = onSpread,
    };

    /// <summary>A real, decodable photograph: the boundary decodes it rather than looking it up.</summary>
    protected static byte[] Photo() => Png(512, 512);

    /// <summary>
    /// What an image provider actually hands back: 3:2, and not the shape the book prints at.
    /// </summary>
    protected const int ProviderWidth = 1536;

    protected const int ProviderHeight = 1024;

    /// <summary>
    /// The same frame after a centred crop to the printed 15:7 spread — 1536 wide, so the height
    /// is 1536 ÷ (15/7) rounded, and roughly 30% of the provider's height is gone.
    /// </summary>
    protected const int SpreadWidth = 1536;

    protected const int SpreadHeight = 717;

    /// <summary>
    /// The textured pictures below — the gradient, the painted seam, the noise JPEG — are pure
    /// functions of their arguments, right down to their fixed random seeds, and the same handful of
    /// them was rebuilt by nearly every test in this suite. So each is built once and handed out as
    /// a fresh copy: the copy is what keeps this a speed change and nothing else, since a caller
    /// still receives a private array it may do anything with.
    ///
    /// Flat pictures live in <see cref="SyntheticImages"/>, which the whole assembly shares.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte[]> TexturedImages = new();

    protected static byte[] Cached(string key, Func<byte[]> build) =>
        TexturedImages.GetOrAdd(key, _ => build()).ToArray();

    /// <summary>Content key for a helper that derives one picture from another.</summary>
    protected static string Fingerprint(byte[] source) =>
        Convert.ToHexString(SHA256.HashData(source));

    /// <summary>The shape the approved spread was composited on, so the geometry is comparable.</summary>
    protected static byte[] BasePng() => Png(1836, 857);

    /// <summary>A picture already at the printed spread's ratio: normalization must not touch it.</summary>
    protected static byte[] SpreadShapedPng() => Png(SpreadWidth, SpreadHeight);

    /// <summary>
    /// A picture with ordinary variation everywhere: a horizontal gradient plus a little noise, so
    /// that "the centre changes far more abruptly than anywhere else" is a statement about a real
    /// baseline rather than about a flat field.
    /// </summary>
    protected static byte[] Gradient(int width, int height) =>
        Cached($"gradient:{width}x{height}", () => BuildGradient(width, height));

    protected static byte[] BuildGradient(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var random = new Random(11);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var shade = (byte)(40 + (x * 150 / Math.Max(1, width)) + random.Next(6));
                    row[x] = new Rgba32(shade, (byte)(shade / 2), (byte)(255 - shade), 255);
                }
            }
        });

        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// The defect, painted deliberately: a run of darkened columns, at the exact centre unless a
    /// test asks for somewhere else.
    /// </summary>
    protected static byte[] WithSeam(byte[] png, int columns, int darken, int? atColumn = null) =>
        Cached(
            $"seam:{columns}:{darken}:{atColumn}:{Fingerprint(png)}",
            () => BuildWithSeam(png, columns, darken, atColumn));

    protected static byte[] BuildWithSeam(byte[] png, int columns, int darken, int? atColumn)
    {
        using var image = Image.Load<Rgba32>(png);

        var start = (atColumn ?? (image.Width / 2)) - (columns / 2);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = start; x < start + columns && x < row.Length; x++)
                {
                    if (x < 0) continue;

                    var pixel = row[x];
                    row[x] = new Rgba32(
                        (byte)Math.Max(0, pixel.R - darken),
                        (byte)Math.Max(0, pixel.G - darken),
                        (byte)Math.Max(0, pixel.B - darken),
                        pixel.A);
                }
            }
        });

        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    /// <param name="red">
    /// Paints the picture a distinguishable colour. Two blank images of the same size are the same
    /// bytes, which would make every "these are different pictures" assertion below vacuous.
    /// </param>
    protected static byte[] Png(int width, int height, byte red = 0) =>
        SyntheticImages.SolidPng(width, height, red);

    /// <summary>
    /// The fixture with a fourth recurring element: a shape the provider request permits — it has
    /// no maxItems — and the supplied contract forbids.
    /// </summary>
    protected static string WithFourRecurringElements()
    {
        var scenario = JsonNode.Parse(ScenarioFixture())!;
        var elements = scenario["visual_lock"]!["recurring_elements"]!.AsArray();
        elements.Add("A fourth element, which the contract caps at three.");
        return scenario.ToJsonString();
    }

    /// <summary>
    /// The fixture with its eight Beki sentences replaced, so a test can say what the pose table
    /// would make of a book without touching anything else the scenario fixes.
    /// </summary>
    protected static string WithBekiActions(params string[] actions)
    {
        Assert.Equal(BookFormat.SpreadCount, actions.Length);

        var scenario = JsonNode.Parse(ScenarioFixture())!;
        var spreads = scenario["spreads"]!.AsArray();

        for (var index = 0; index < actions.Length; index++)
        {
            spreads[index]!["beki_action"] = actions[index];
        }

        return scenario.ToJsonString();
    }

    /// <summary>The fixture with a different outfit lock — a scenario nothing would replan into.</summary>
    protected static string WithOutfit(string outfit)
    {
        var scenario = JsonNode.Parse(ScenarioFixture())!;
        scenario["visual_lock"]!["child_outfit"] = outfit;
        return scenario.ToJsonString();
    }

    /// <summary>
    /// A plan in the shape the fulfilment job adopts: the Nina story's eight Georgian pages, taken
    /// from the same fixture the scenario was planned from.
    /// </summary>
    protected static MasterStory Plan()
    {
        using var input = JsonDocument.Parse(File.ReadAllText(FixturePath("visual_scenario_input_v2.json")));

        var spreads = input.RootElement
            .GetProperty("story_pages")
            .EnumerateArray()
            .Select(page => new StorySpread
            {
                Number = page.GetProperty("page").GetInt32(),
                Title = string.Empty,
                Caption = string.Empty,
                Text = page.GetProperty("story_text").GetString()!,
                // No Beki in the cast: on the legacy path that is what keeps the characterization
                // test off the master-reference branch, which is a different rule under test.
                Characters = ["child"],
                Objects = [],
                Illustration = new IllustrationBrief { Scene = "The child in the valley." },
            })
            .ToList();

        return new MasterStory
        {
            Concept = new StoryConcept
            {
                Title = "ბაფუს დაკარგული ბილიკი",
                Outline = spreads.Select(spread => spread.Text).ToList(),
            },
            Spreads = spreads,
            CharacterLock = "A child.",
            Cover = new IllustrationBrief { Scene = "The child at the edge of the valley." },
            WorldLock = "A warm golden valley.",
            Cast = [],
            Objects = [],
        };
    }

    /// <param name="spreadConcurrency">
    /// One, so that the recorded call order in this file is the book's order and the assertions
    /// below are about what was sent rather than about scheduling. The parallel behaviour — the
    /// limit, ordered delivery, fail-fast — is <see cref="CompositeConcurrencyTests"/>'s subject,
    /// and it needs stubs that actually yield to say anything about it.
    /// </param>
    protected static CompositeBookPipeline Pipeline(
        IStoryModelClient storyClient, IOpenAiService images, int spreadConcurrency = 1) =>
        new(storyClient,
            images,
            new StubMasterStoryService(),
            Options.Create(new BekiOptions
            {
                CompositePipelineEnabled = true,
                SpreadConcurrency = spreadConcurrency,
            }),
            Options.Create(new BekiPrintLayoutOptions()),
            NullLogger<CompositeBookPipeline>.Instance);

    protected static BekiBookGenerator Generator(
        IOpenAiService images, ICompositeBookPipeline pipeline, bool compositeEnabled) =>
        new(new ScriptedStoryModelClient(),
            images,
            Options.Create(new BekiPrintLayoutOptions()),
            Options.Create(new BekiOptions
            {
                CompositePipelineEnabled = compositeEnabled,
                SpreadConcurrency = 1,
            }),
            NullLogger<BekiBookGenerator>.Instance,
            pipeline);

    /// <summary>
    /// Fails the test by being used. The point of the flag-off characterization is that the branch
    /// is not taken, and a stub that returned something plausible would let a taken branch pass.
    /// </summary>
    protected sealed class SpyCompositePipeline : ICompositeBookPipeline
    {
        public int RunCalls { get; private set; }
        public int CoverCalls { get; private set; }

        public Task<CompositeBookResult> RunAsync(
            CompositeBookRequest request, CancellationToken cancellationToken)
        {
            RunCalls++;
            throw new InvalidOperationException("The composite pipeline must not run with the flag off.");
        }

        public Task<byte[]> DrawCoverAsync(
            CompositeBookContext context, VisualScenarioV2 scenario, byte[] childPhoto,
            string childPhotoContentType, CancellationToken cancellationToken)
        {
            CoverCalls++;
            throw new InvalidOperationException("The composite cover must not run with the flag off.");
        }

        public Task<CompositeCoverWrap> DrawCoverWrapAsync(
            CompositeBookContext context, VisualScenarioV2 scenario, byte[] childPhoto,
            string childPhotoContentType, CancellationToken cancellationToken)
        {
            CoverCalls++;
            throw new InvalidOperationException("The press cover wrap must not run with the flag off.");
        }
    }

    /// <summary>
    /// The text door, scripted. Hands back queued replies in order and records exactly what it was
    /// asked, because the retry's shape — the original prompt, whole, with the reasons appended —
    /// is as much under test as the answer.
    /// </summary>
    protected sealed class ScriptedStoryModelClient(params string[] replies) : IStoryModelClient
    {
        private readonly Queue<string> _replies = new(replies);

        public int Calls { get; private set; }
        public List<string> SystemPrompts { get; } = [];
        public List<string> UserPrompts { get; } = [];
        public List<string> Models { get; } = [];

        public Task<ModelResult<T>> CompleteAsync<T>(
            string model, string systemPrompt, string userPrompt, string schemaName,
            JsonElement schema, CancellationToken cancellationToken)
        {
            Calls++;
            SystemPrompts.Add(systemPrompt);
            UserPrompts.Add(userPrompt);
            Models.Add(model);

            var reply = _replies.Count > 0 ? _replies.Dequeue() : "{}";
            return Task.FromResult(new ModelResult<T>(
                JsonSerializer.Deserialize<T>(reply, StoryJson.Options)!, 1, 1));
        }
    }

    /// <summary>
    /// Answers the first call and then throws — the polish call failing on a book that was written
    /// successfully, which is the case the editing pass has to survive.
    /// </summary>
    protected sealed class ThrowingAfterFirstCallClient(string firstReply) : IStoryModelClient
    {
        private int _calls;

        public Task<ModelResult<T>> CompleteAsync<T>(
            string model, string systemPrompt, string userPrompt, string schemaName,
            JsonElement schema, CancellationToken cancellationToken)
        {
            if (_calls++ > 0)
            {
                throw new HttpRequestException("the editor is unreachable.");
            }

            return Task.FromResult(new ModelResult<T>(
                JsonSerializer.Deserialize<T>(firstReply, StoryJson.Options)!, 1, 1));
        }
    }

    /// <summary>
    /// The image door, stubbed: a spread-shaped picture and a PASS verdict unless a test queues
    /// something else. It records the prompts and the number of references, which is how "never a
    /// Beki reference" is checked at the seam rather than in the builder.
    /// </summary>
    protected sealed class StubImageService : IOpenAiService
    {
        public int ImageCalls { get; private set; }
        public int ReviewCalls { get; private set; }
        public List<string> Prompts { get; } = [];
        public List<int> ReferenceCounts { get; } = [];

        /// <summary>
        /// The continuity reference of each call, or null when there was none — which is how "the
        /// composite was never sent as a continuity reference" is checked at the seam rather than
        /// inferred from a comment.
        ///
        /// Found by its label rather than by its position, because v1.1 added a reference in front
        /// of it: on a spread carrying both, continuity is the fourth image and the child appearance
        /// anchor is the third.
        /// </summary>
        public List<byte[]?> ContinuityImages { get; } = [];

        /// <summary>The child appearance anchor of each call, or null on the page that makes it.</summary>
        public List<byte[]?> AnchorImages { get; } = [];

        /// <summary>The child's photograph of each call, which is attached on every one of them.</summary>
        public List<byte[]?> PhotoImages { get; } = [];

        /// <summary>What each QA call was asked, and what it was shown.</summary>
        public List<string> ReviewPrompts { get; } = [];

        /// <summary>
        /// The picture each QA call actually judged.
        ///
        /// Recorded because of what it makes checkable: the re-composite retry used to hand the
        /// reviewer the identical image a second time, and no assertion about counts or verdicts
        /// can tell that apart from a retry that changed something. Comparing the bytes can.
        /// </summary>
        public List<byte[]> ReviewImages { get; } = [];

        public List<IReadOnlyList<(byte[] Bytes, string ContentType, string Label)>> ReviewReferences
        { get; } = [];

        public List<string> ReviewLabels { get; } = [];

        /// <summary>
        /// The identity calls, kept apart from the QA calls even though both arrive through the same
        /// door.
        ///
        /// They genuinely are the same call — an image, an instruction, one text answer, validated
        /// by the caller — which is why the pipeline reuses the reviewer surface rather than growing
        /// a second one. The stub tells them apart the only honest way: by what it was asked.
        /// </summary>
        public int IdentityCalls { get; private set; }

        public List<string> IdentityPrompts { get; } = [];

        public Queue<string> IdentityAnswers { get; } = new();

        private static readonly string DefaultIdentity = $$"""
            {"hair_color":"{{IdentityFixture.HairColor}}",
             "hair_style":"{{IdentityFixture.HairStyle}}",
             "eye_color":"{{IdentityFixture.EyeColor}}",
             "skin_tone":"{{IdentityFixture.SkinTone}}",
             "eyebrows":"{{IdentityFixture.Eyebrows}}",
             "glasses":"{{IdentityFixture.Glasses}}",
             "face_shape":"{{IdentityFixture.FaceShape}}",
             "distinctive_features":"{{IdentityFixture.DistinctiveFeatures}}"}
            """;

        /// <summary>Each call's answer, so a test can say which picture continuity kept.</summary>
        public List<byte[]> Returned { get; } = [];

        public Queue<string> Verdicts { get; } = new();

        /// <summary>What the cover review answers, and what it was asked.</summary>
        public Queue<string> CoverVerdicts { get; } = new();

        public List<string> CoverReviewPrompts { get; } = [];

        public List<IReadOnlyList<(byte[] Bytes, string ContentType, string Label)>>
            CoverReviewReferences { get; } = [];

        /// <summary>The picture the cover review actually judged — the crop, not the frame.</summary>
        public List<byte[]> CoverReviewImages { get; } = [];

        private const string PassVerdict =
            """{"status":"PASS","failed_checks":[],"recommended_action":"pass","notes":[]}""";

        /// <summary>
        /// Whether each call demanded that its references actually be sent. The composite path must
        /// never accept a picture drawn without them.
        /// </summary>
        public List<bool> StrictFlags { get; } = [];

        /// <summary>Set to make the call fail the way a dead edit route does under strict mode.</summary>
        public bool FailWhenStrict { get; init; }

        /// <summary>Returned instead of a good render — a truncated response, say.</summary>
        public byte[]? NextImage { get; init; }

        /// <summary>
        /// Scripted renders, one per call, ahead of <see cref="NextImage"/>. For the tests where
        /// the first picture and its regeneration must differ — a veiled base bought a redraw —
        /// which a single fixed image cannot express.
        /// </summary>
        public Queue<byte[]> QueuedImages { get; } = new();

        public Task<byte[]> GenerateStoryImageAsync(
            string imagePrompt, StoryImageReference? reference,
            CancellationToken cancellationToken, string? imageSize = null,
            bool requireReferences = false)
        {
            StrictFlags.Add(requireReferences);

            if (requireReferences && FailWhenStrict)
            {
                throw new InvalidOperationException(
                    "GPT Image edit failed after retries and this illustration may not be drawn "
                    + "without its references.");
            }

            ImageCalls++;
            Prompts.Add(imagePrompt);

            var cast = reference?.CastPhotos ?? [];
            ReferenceCounts.Add(reference is null ? 0 : 1 + cast.Count);

            /*
              Which attached image is which, read the way the model would read it.

              StoryImageReference has one unlabelled lead slot and a labelled tail, and from v1.2
              the lead is the appearance anchor on spreads 2-8 and the photograph on spread 1. The
              stub decides between them from the prompt's own first line — which makes every
              assertion below a statement about the request and the prompt agreeing, rather than
              about the stub's guess. A prompt that said Image 1 was the anchor while the anchor was
              attached third would fail here, which is the failure worth catching.
            */
            var leadsWithAnchor = imagePrompt.Contains(
                "Image 1 - child appearance anchor", StringComparison.Ordinal);

            ContinuityImages.Add(
                cast.FirstOrDefault(photo => photo.Name == "Continuity reference")?.Bytes);

            AnchorImages.Add(leadsWithAnchor ? reference?.CharacterAnchorBytes : null);

            PhotoImages.Add(leadsWithAnchor
                ? cast.FirstOrDefault(photo => photo.Name == "Child identity reference")?.Bytes
                : reference?.CharacterAnchorBytes);

            // The shape the providers actually return — 3:2, not the printed 15:7. Returning a
            // spread-shaped frame here would make the normalization step a no-op and every
            // geometry assertion below vacuous, which is exactly how the missing normalization
            // survived the first round of these tests.
            //
            // A distinct picture per call: identical bytes would make "continuity kept the most
            // recent one" unfalsifiable.
            var image = QueuedImages.Count > 0
                ? QueuedImages.Dequeue()
                : NextImage ?? Png(ProviderWidth, ProviderHeight, red: (byte)(10 + ImageCalls));
            Returned.Add(image);
            return Task.FromResult(image);
        }

        public Task<string> ReviewIllustrationAsync(
            byte[] imageBytes, string reviewPrompt,
            IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
            CancellationToken cancellationToken)
        {
            if (reviewPrompt.StartsWith("You are the identity reader", StringComparison.Ordinal))
            {
                IdentityCalls++;
                IdentityPrompts.Add(reviewPrompt);
                return Task.FromResult(
                    IdentityAnswers.Count > 0 ? IdentityAnswers.Dequeue() : DefaultIdentity);
            }

            // The cover ask is the same reviewer with a different page description, and it arrives
            // after the eight spreads. Kept on its own queue so a test can refuse a cover without
            // counting past eight spread verdicts to reach it.
            if (reviewPrompt.Contains("This is the book's COVER", StringComparison.Ordinal))
            {
                CoverReviewPrompts.Add(reviewPrompt);
                CoverReviewReferences.Add(references);
                CoverReviewImages.Add(imageBytes);

                return Task.FromResult(
                    CoverVerdicts.Count > 0 ? CoverVerdicts.Dequeue() : PassVerdict);
            }

            ReviewCalls++;
            ReviewPrompts.Add(reviewPrompt);
            ReviewImages.Add(imageBytes);
            ReviewReferences.Add(references);
            ReviewLabels.Add(string.Join(", ", references.Select(reference => reference.Label)));

            return Task.FromResult(Verdicts.Count > 0 ? Verdicts.Dequeue() : PassVerdict);
        }

        public Task<AdventureContentDto> GenerateAdventureContentAsync(
            AdventureGenerationInput input, Guid adventureId, CancellationToken cancellationToken) =>
            Task.FromResult(new AdventureContentDto());

        public Task<string> DescribeCharacterFromPhotoAsync(
            byte[] imageBytes, string contentType, string promptText, CancellationToken cancellationToken) =>
            Task.FromResult("a child");

        public Task<string> CompleteTextAsync(string promptText, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
    }

    /// <summary>
    /// Only <see cref="IMasterStoryService.ModelName"/> is ever reached in these tests — every run
    /// adopts a plan, which is what the fulfilment job does — so writing one throws rather than
    /// returning a book nobody asked for.
    /// </summary>
    protected sealed class StubMasterStoryService : IMasterStoryService
    {
        public string ModelName => "stub-story-model";

        public string PromptVersion => "v6";

        public (string System, string User) BuildPrompts(MasterStoryInput input) => (string.Empty, string.Empty);

        public Task<MasterStoryResult> WriteAsync(MasterStoryInput input, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MasterStoryResult> RetryPlanWithCorrectionsAsync(
            MasterStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MasterStoryResult> WriteCompositePlanAsync(
            CompositeStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "These tests adopt the previewed plan, exactly as the fulfilment job does.");
    }

    /// <summary>The real story service, with the composite flag on and a scripted model behind it.</summary>
    protected static MasterStoryService CompositeStoryService(IStoryModelClient client) =>
        new(client,
            new StoryPolishClient(client, "stub-polish-model"),
            Options.Create(new OpenAiOptions { Model = "stub-story-model" }),
            Options.Create(new BekiOptions { CompositePipelineEnabled = true }),
            NullLogger<MasterStoryService>.Instance);

    protected static CompositeStoryInput CompositeStoryInputFixture() => new()
    {
        ChildName = "ნინა",
        AgeBand = "3-5",
        Gender = "girl",
        ThemeId = "dinosaurs",
        Theme = AdventurePacks.Api.Domain.Enums.ThemeType.Dinosaurs,
    };

    /// <summary>
    /// A composite plan as the model returns it: the composite schema's fields and no
    /// <c>characterLock</c>, which is the field that schema deliberately drops.
    /// </summary>
    protected static string CompositePlanJson(int spreads)
    {
        var pages = Enumerable.Range(1, spreads).Select(number => new
        {
            number,
            title = string.Empty,
            caption = string.Empty,
            text = $"ნინა და ბეკი — გვერდი {number}.",
            characters = new[] { "child", "beki" },
            objects = Array.Empty<string>(),
            illustration = new { scene = "The child in the valley.", avoid = string.Empty },
        });

        return JsonSerializer.Serialize(
            new
            {
                concept = new
                {
                    title = "ბაფუს ბილიკი",
                    outline = Enumerable.Range(1, spreads).Select(n => $"beat {n}").ToArray(),
                },
                cast = Array.Empty<object>(),
                objects = Array.Empty<object>(),
                spreads = pages,
                worldLock = "A warm golden valley.",
                cover = new { scene = "The child at the valley's edge.", avoid = string.Empty },
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    /// <summary>
    /// The same plan with the editor's corrections applied — built by editing the parsed document
    /// rather than by string replacement.
    ///
    /// Replacement does not work here and fails silently, which is worse: the serializer escapes
    /// every Georgian character as \uXXXX, so a search for the literal title finds nothing and the
    /// "corrected" book comes back identical to the written one. A test that asserts a merge against
    /// a document that was never changed proves nothing.
    /// </summary>
    protected static string CompositePlanJson(
        int spreads, string? title = null, (int Number, string Text)? spreadText = null,
        int? renumberFirstSpreadTo = null)
    {
        var plan = JsonNode.Parse(CompositePlanJson(spreads))!;

        if (title is not null)
        {
            plan["concept"]!["title"] = title;
        }

        if (spreadText is { } edit)
        {
            plan["spreads"]!.AsArray()
                .First(spread => (int)spread!["number"]! == edit.Number)!["text"] = edit.Text;
        }

        if (renumberFirstSpreadTo is { } number)
        {
            plan["spreads"]!.AsArray()[0]!["number"] = number;
        }

        return plan.ToJsonString();
    }

    /// <summary>A real JPEG, so truncating it truncates something a decoder actually walks.</summary>
    protected static byte[] Jpeg(int width, int height) =>
        Cached($"jpeg:{width}x{height}", () => BuildJpeg(width, height));

    protected static byte[] BuildJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);

        // Noise rather than a flat colour: a uniform image compresses to almost nothing, and a
        // third of almost nothing is not a recognisable truncation.
        var random = new Random(7);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32(
                        (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
                }
            }
        });

        using var buffer = new MemoryStream();
        image.SaveAsJpeg(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// An upload that stopped as the pixels began: every header segment present, the scan itself
    /// missing. This is what a dropped connection actually leaves behind, and it is the one shape
    /// where reading the header and reading the picture give different answers.
    ///
    /// Cut at the start-of-scan marker rather than at an arbitrary fraction, because a JPEG decoder
    /// is forgiving about a scan that ends early — it fills what is missing — and only a scan that
    /// never starts is unambiguously not a picture.
    /// </summary>
    protected static byte[] TruncatedJpeg(int width, int height)
    {
        var whole = Jpeg(width, height);

        for (var i = 0; i < whole.Length - 1; i++)
        {
            // FF DA: start of scan. Twelve bytes past it keeps the scan header and drops the data.
            if (whole[i] == 0xFF && whole[i + 1] == 0xDA)
            {
                return whole[..(i + 12)];
            }
        }

        throw new InvalidOperationException("The encoded JPEG has no start-of-scan marker.");
    }

    protected static MasterBookService PreviewService(
        RecordingMasterStoryService story,
        RecordingRunRepository runs,
        bool compositeEnabled,
        bool bookFormatEnabled = true,
        StubImageService? images = null) =>
        new(runs,
            story,
            images ?? new StubImageService(),
            new StubBlobStorage(),
            new PassThroughNormalizer(),
            new StubBackgroundJobClient(),
            new SpyBekiBookGenerator(),
            Options.Create(new BekiOptions
            {
                CompositePipelineEnabled = compositeEnabled,
                // On by default: the composite pipeline only ever draws a book that routes to the
                // Beki fulfilment job, and that routing needs this switch.
                BookFormatEnabled = bookFormatEnabled,
            }),
            NullLogger<MasterBookService>.Instance);

    /// <summary>
    /// Which planner the preview reached for, and with what. The point of the class is the two
    /// counters: the failure being tested is a composite book written by the legacy prompt.
    /// </summary>
    protected sealed class RecordingMasterStoryService : IMasterStoryService
    {
        public int LegacyCalls { get; private set; }
        public int LegacyRetryCalls { get; private set; }
        public int CompositeCalls { get; private set; }
        public CompositeStoryInput? LastCompositeInput { get; private set; }
        public IReadOnlyList<string> LastCompositeProblems { get; private set; } = [];

        /// <summary>The plan handed back, so a test can check what the illustrator would receive.</summary>
        public MasterStory? LastStory { get; private set; }

        /// <summary>Makes the first plan fail validation, so the corrective retry is exercised.</summary>
        public bool FirstPlanIsInvalid { get; init; }

        /// <summary>First plan returns seven spreads — the fault the request schema cannot forbid.</summary>
        public bool FirstPlanHasSevenSpreads { get; init; }

        /// <summary>Both attempts return seven, so the preview has to fail.</summary>
        public bool EverySpreadCountIsWrong { get; init; }

        /// <summary>First plan leaves Beki off spread four.</summary>
        public bool FirstPlanDropsBekiFromSpreadFour { get; init; }

        public string ModelName => "stub-story-model";

        public string PromptVersion => "v6";

        public (string System, string User) BuildPrompts(MasterStoryInput input) =>
            ("legacy system", "legacy user");

        public Task<MasterStoryResult> WriteAsync(MasterStoryInput input, CancellationToken cancellationToken)
        {
            LegacyCalls++;
            return Task.FromResult(Remember(Result("legacy system", "legacy user", valid: true)));
        }

        public Task<MasterStoryResult> RetryPlanWithCorrectionsAsync(
            MasterStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken)
        {
            LegacyRetryCalls++;
            return Task.FromResult(Result("legacy system", "legacy user", valid: true));
        }

        public Task<MasterStoryResult> WriteCompositePlanAsync(
            CompositeStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken)
        {
            CompositeCalls++;
            LastCompositeInput = input;
            LastCompositeProblems = problems;

            // Invalid on the first attempt only, so the retry has something to correct and the
            // corrected plan then passes.
            var first = CompositeCalls == 1;
            var valid = !FirstPlanIsInvalid || !first;

            var plan = Result("composite system", "composite user", valid);

            if (EverySpreadCountIsWrong || (FirstPlanHasSevenSpreads && first))
            {
                plan = plan with
                {
                    Story = plan.Story with
                    {
                        Spreads = plan.Story.Spreads.Take(BookFormat.SpreadCount - 1).ToList()
                    }
                };
            }

            if (FirstPlanDropsBekiFromSpreadFour && first)
            {
                plan = plan with
                {
                    Story = plan.Story with
                    {
                        Spreads = plan.Story.Spreads
                            .Select(spread => spread.Number == 4
                                ? spread with { Characters = ["child"] }
                                : spread)
                            .ToList()
                    }
                };
            }

            return Task.FromResult(Remember(plan));
        }

        private MasterStoryResult Remember(MasterStoryResult result)
        {
            LastStory = result.Story;
            return result;
        }

        /// <param name="valid">
        /// False drops Beki out of spread one, which BekiPlanValidator reports — the cheapest
        /// problem to produce that the preview path actually retries over.
        /// </param>
        private static MasterStoryResult Result(string system, string user, bool valid)
        {
            var plan = Plan();

            if (!valid)
            {
                plan = plan with
                {
                    Spreads = plan.Spreads
                        .Select(spread => spread with { Characters = ["child"] })
                        .ToList()
                };
            }
            else
            {
                plan = plan with
                {
                    Spreads = plan.Spreads
                        .Select(spread => spread with { Characters = ["child", "beki"] })
                        .ToList()
                };
            }

            return new MasterStoryResult
            {
                Story = plan,
                SystemPrompt = system,
                UserPrompt = user,
                Model = "stub-story-model",
                PromptTokens = 1,
                CompletionTokens = 1,
            };
        }
    }

    /// <summary>The run row, in memory, remembering only what these tests assert about.</summary>
    protected sealed class RecordingRunRepository : IMasterStoryRunRepository
    {
        public MasterStoryRun Run { get; } = new()
        {
            Id = Guid.NewGuid(),
            ChildName = "ნინა",
            Age = 5,
            Gender = "girl",
            Theme = nameof(AdventurePacks.Api.Domain.Enums.ThemeType.Dinosaurs),
            SpreadCount = BookFormat.SpreadCount,
            StoryLanguage = "ka",
            // Parked, which is the ordinary case. The one test about a failed upload clears it.
            PhotoBlobUrl = "https://blob.test/portrait.png",
        };

        public string? SavedPromptVersion { get; private set; }
        public string? SavedSystemPrompt { get; private set; }

        public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<MasterStoryRun?>(Run);

        public Task SavePromptsAsync(
            Guid id, string model, string promptVersion, string systemPrompt, string userPrompt,
            CancellationToken cancellationToken)
        {
            SavedPromptVersion = promptVersion;
            SavedSystemPrompt = systemPrompt;
            return Task.CompletedTask;
        }

        public Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<MasterStoryRunProgress?>(null);

        public Task SetProgressAsync(
            Guid id, string status, string? progressMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveStoryAsync(
            Guid id, string storyJson, string contentJson, int promptTokens, int completionTokens,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        /// <summary>Non-null once the run failed — the preview swallows the exception itself.</summary>
        public string? FailureMessage { get; private set; }

        public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken)
        {
            FailureMessage = error;
            return Task.CompletedTask;
        }

        public Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(
            int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExpiredMasterStoryRun>>([]);

        public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    protected sealed class StubBlobStorage : IBlobStorageService
    {
        public Task<string> UploadAsync(
            string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken) =>
            Task.FromResult($"https://blob.test/{blobName}");

        public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<byte[]> DownloadBytesFromStoredUrlAsync(
            string storedUrl, CancellationToken cancellationToken) =>
            Task.FromResult(Photo());

        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    /// <summary>Draws whatever it is asked for; the router test is about what reaches it.</summary>
    protected sealed class NoOpIllustrationClient : IIllustrationClient
    {
        public Task<byte[]> GenerateStoryImageAsync(
            string imagePrompt, StoryImageReference? reference,
            CancellationToken cancellationToken, string? imageSize = null) =>
            Task.FromResult(Png(ProviderWidth, ProviderHeight));

        public Task<string> ReviewIllustrationAsync(
            byte[] imageBytes, string reviewPrompt,
            IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
            CancellationToken cancellationToken) => Task.FromResult("{}");

        public Task<string> DescribeCharacterFromPhotoAsync(
            byte[] imageBytes, string contentType, string promptText,
            CancellationToken cancellationToken) => Task.FromResult("a child");
    }

    protected sealed class PassThroughNormalizer : IReferenceImageNormalizer
    {
        public NormalizedReferenceImage NormalizeForOpenAi(byte[] bytes, string? hintContentType = null) =>
            new(bytes, hintContentType ?? "image/png", "reference.png");

        public NormalizedReferenceImage NormalizeForStorageWebp(byte[] bytes, string? hintContentType = null) =>
            new(bytes, "image/webp", "illustration.webp");
    }

    protected sealed class StubBackgroundJobClient : Hangfire.IBackgroundJobClient
    {
        public string Create(Hangfire.Common.Job job, Hangfire.States.IState state) => Guid.NewGuid().ToString();

        public bool ChangeState(string jobId, Hangfire.States.IState state, string? expectedState) => true;
    }

    /// <summary>The preview's cover path is not what these tests are about; it must simply not run.</summary>
    protected sealed class SpyBekiBookGenerator : IBekiBookGenerator
    {
        public Task<BekiBookResult> GenerateAsync(
            MasterStoryInput input, byte[] childPhoto, string childPhotoContentType,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BekiBookResult> IllustrateAsync(
            MasterStory plan, byte[] childPhoto, string childPhotoContentType, byte[]? existingCover,
            Func<BekiImageResult, Task>? onImage, CancellationToken cancellationToken,
            IReadOnlyDictionary<int, byte[]>? existingSpreads = null,
            CompositeBookContext? composite = null) => throw new NotSupportedException();

        public Task<BekiImageResult> DrawCoverAsync(
            MasterStory plan, byte[] childPhoto, string childPhotoContentType,
            CancellationToken cancellationToken, CompositeBookContext? composite = null) =>
            throw new NotSupportedException();
    }

    protected sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    protected sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    protected static HttpResponseMessage TextResponse(string text)
    {
        var payload = new
        {
            steps = new[]
            {
                new { type = "model_output", content = new[] { new { type = "text", text } } }
            },
            usage = new { total_input_tokens = 1, total_output_tokens = 1 }
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
    }
}
