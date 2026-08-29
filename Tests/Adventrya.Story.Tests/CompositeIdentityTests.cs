using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The child identity spec: reading it, overriding it, failing without it, and never logging it.
///
/// The defect these tests are about is a finished book. Its eight spreads each passed the minimal
/// visual QA and the child in them is visibly not the same child from page to page — because
/// identity rode entirely on the attached photograph, so every spread was an independent
/// stylization of it and every review compared one page against that photograph with nothing to say
/// about the other seven. The spec is what makes the eight pages one book's worth of one child.
///
/// Two properties get as much attention here as the reading itself. It is required — two unusable
/// answers stop the book rather than quietly restoring the old arrangement — and it is private: the
/// attributes describe a real child's body, and nothing in the pipeline's own logs or telemetry may
/// carry them.
/// </summary>
public class CompositeIdentityTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "nina_dinosaurs", name);

    private static string ScenarioFixture() =>
        File.ReadAllText(FixturePath("visual_scenario_output_v2.json"));

    private const string GoodAnswer =
        """
        {"hair_color":"dark brown","hair_style":"shoulder-length wavy with a soft fringe",
         "eye_color":"brown","skin_tone":"light warm",
         "eyebrows":"soft, medium-thick, gently arched","glasses":"none",
         "face_shape":"round with a soft chin",
         "distinctive_features":"light freckles across the nose; a dimple on the left cheek"}
        """;

    // ---------------------------------------------------------------------------------------
    // Reading one answer
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void A_well_formed_answer_reads_as_four_attributes()
    {
        var parsed = CompositeChildIdentity.Parse(GoodAnswer);

        Assert.True(parsed.IsValid, parsed.Summary);
        Assert.Equal("dark brown", parsed.Spec!.HairColor);
        Assert.Equal("shoulder-length wavy with a soft fringe", parsed.Spec.HairStyle);
        Assert.Equal("brown", parsed.Spec.EyeColor);
        Assert.Equal("light warm", parsed.Spec.SkinTone);
    }

    /// <summary>
    /// The wrapper is forgiven and the content is not — the same trade the QA reader makes, because
    /// a model in prose mode fences its JSON and that is not a failed answer.
    /// </summary>
    [Fact]
    public void The_wrapper_is_forgiven_and_the_content_is_not()
    {
        Assert.True(CompositeChildIdentity.Parse(
            "Here is what I can see:\n```json\n" + GoodAnswer + "\n```").IsValid);

        // A fifth key: the prompt says no additional keys, and an answer that volunteers an
        // ethnicity or a mood is exactly what the prompt spends four lines forbidding.
        Assert.False(CompositeChildIdentity.Parse(
            """
            {"hair_color":"dark brown","hair_style":"wavy","eye_color":"brown",
             "skin_tone":"light warm","mood":"cheerful"}
            """).IsValid);

        // A missing attribute is not a spec: the lock has four lines and there is no default for
        // any of them.
        Assert.False(CompositeChildIdentity.Parse(
            """{"hair_color":"dark brown","hair_style":"wavy","eye_color":"brown"}""").IsValid);

        // An empty attribute, and one that is only punctuation once tidied.
        Assert.False(CompositeChildIdentity.Parse(
            """
            {"hair_color":"","hair_style":"wavy","eye_color":"brown","skin_tone":"light warm"}
            """).IsValid);

        Assert.False(CompositeChildIdentity.Parse(
            """
            {"hair_color":" . ","hair_style":"wavy","eye_color":"brown","skin_tone":"light warm"}
            """).IsValid);

        // A sentence is not an attribute. It would be repeated in nine image prompts.
        var essay = new string('a', CompositeChildIdentity.MaxAttributeLength + 1);
        Assert.False(CompositeChildIdentity.Parse(
            $$"""
            {"hair_color":"{{essay}}","hair_style":"wavy","eye_color":"brown","skin_tone":"light warm"}
            """).IsValid);

        Assert.False(CompositeChildIdentity.Parse("She has lovely dark hair!").IsValid);
        Assert.False(CompositeChildIdentity.Parse(string.Empty).IsValid);
        Assert.False(CompositeChildIdentity.Parse(null).IsValid);
    }

    /// <summary>
    /// Values are tidied into the shape the prompt asked for, because they are pasted verbatim into
    /// nine image prompts and "Dark brown." is a sentence fragment rather than an attribute.
    /// </summary>
    [Fact]
    public void Values_are_tidied_into_prompt_shape()
    {
        var parsed = CompositeChildIdentity.Parse(
            """
            {"hair_color":"  Dark   brown. ","hair_style":"wavy\n bob","eye_color":"brown,",
             "skin_tone":"light warm","eyebrows":"soft arched","glasses":"none",
             "face_shape":"round","distinctive_features":"freckles"}
            """);

        Assert.True(parsed.IsValid, parsed.Summary);
        Assert.Equal("Dark brown", parsed.Spec!.HairColor);
        Assert.Equal("wavy bob", parsed.Spec.HairStyle);
        Assert.Equal("brown", parsed.Spec.EyeColor);
    }

    /// <summary>
    /// The problems that go back to the model — and into the call's own log line — never quote the
    /// value that was wrong. A message that helpfully said which attribute it choked on, and what
    /// it said, would put a real child's hair colour in a log.
    /// </summary>
    [Fact]
    public void The_reasons_an_answer_was_rejected_never_quote_the_answer()
    {
        var parsed = CompositeChildIdentity.Parse(
            """
            {"hair_color":"a very distinctive shade of pillarbox scarlet that runs past the limit",
             "hair_style":"wavy","eye_color":"brown","skin_tone":"light warm","mood":"cheerful"}
            """);

        Assert.False(parsed.IsValid);
        Assert.NotEmpty(parsed.Problems);
        Assert.DoesNotContain("scarlet", parsed.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cheerful", parsed.Summary, StringComparison.OrdinalIgnoreCase);

        // And the retry it produces is the original ask with the reasons on the end of it, rather
        // than a rewritten instruction that would return a different answer.
        var retry = CompositeChildIdentity.RetryPrompt(parsed.Problems);
        Assert.StartsWith(CompositeChildIdentity.Prompt, retry);
        Assert.Contains("The previous answer could not be used", retry);
    }

    // ---------------------------------------------------------------------------------------
    // Glasses: the field that must be answered even when the answer is nothing
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A spec with no glasses field is not a spec, and the eight attributes are all required.
    ///
    /// Glasses are the reason the rule is worth stating separately. A field a model may leave blank
    /// is a field it decides page by page — the child is studious on spread four and not on spread
    /// five — which is one of the drifts the owner listed by name.
    /// </summary>
    [Fact]
    public void An_answer_missing_any_of_the_eight_attributes_is_rejected()
    {
        foreach (var missing in (string[])
                 ["hair_color", "hair_style", "eye_color", "skin_tone",
                  "eyebrows", "glasses", "face_shape", "distinctive_features"])
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(GoodAnswer)!.AsObject();
            node.Remove(missing);

            var parsed = CompositeChildIdentity.Parse(node.ToJsonString());

            Assert.False(parsed.IsValid, $"an answer with no {missing} was accepted.");
        }
    }

    /// <summary>
    /// Every way a model says "no glasses" is stored as the one word the lock line prints.
    ///
    /// "not visible" is a sentence about a photograph and "none" is a fact about a child. The first
    /// invites an illustrator to supply what the photo did not show, which on nine prompts is a
    /// child who sometimes wears glasses.
    /// </summary>
    [Theory]
    [InlineData("none")]
    [InlineData("None")]
    [InlineData("no")]
    [InlineData("no glasses")]
    [InlineData("not visible")]
    [InlineData("n/a")]
    public void Every_way_of_saying_no_glasses_is_stored_as_none(string answered)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(GoodAnswer)!.AsObject();
        node["glasses"] = answered;

        var parsed = CompositeChildIdentity.Parse(node.ToJsonString());

        Assert.True(parsed.IsValid, parsed.Summary);
        Assert.Equal(CompositeChildIdentity.NoGlasses, parsed.Spec!.Glasses);

        // And the lock says so out loud rather than omitting the line.
        Assert.Contains("Glasses: none", CompositeChildIdentity.LockBlock(parsed.Spec, 5));
    }

    /// <summary>A child who does wear glasses keeps the description, untouched.</summary>
    [Fact]
    public void A_described_pair_of_glasses_is_kept_word_for_word()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(GoodAnswer)!.AsObject();
        node["glasses"] = "round thin gold frames";

        var parsed = CompositeChildIdentity.Parse(node.ToJsonString());

        Assert.True(parsed.IsValid, parsed.Summary);
        Assert.Equal("round thin gold frames", parsed.Spec!.Glasses);
        Assert.Contains(
            "Glasses: round thin gold frames", CompositeChildIdentity.LockBlock(parsed.Spec, 5));
    }

    /// <summary>
    /// The lock block, whole: eight lines, the age, the eye colour restated as a rule about every
    /// page, and the sentence that keeps the photograph in charge — pointed at whichever image the
    /// photograph actually is.
    /// </summary>
    [Fact]
    public void The_lock_block_states_every_attribute_and_defers_to_the_photograph()
    {
        var spec = CompositeChildIdentity.Parse(GoodAnswer).Spec!;

        var onSpreadOne = CompositeChildIdentity.LockBlock(spec, 5);
        var onLaterSpread = CompositeChildIdentity.LockBlock(spec, 5, identityImage: 2);

        foreach (var line in (string[])
                 ["Face shape: round with a soft chin",
                  "Hair colour: dark brown",
                  "Hair style: shoulder-length wavy with a soft fringe",
                  "Eyebrows: soft, medium-thick, gently arched",
                  "Eye colour: brown",
                  "Skin tone: light warm",
                  "Glasses: none",
                  "Distinctive features: light freckles across the nose; a dimple on the left cheek",
                  "The child is approximately 5 years old.",
                  "The child's eyes are brown on every page."])
        {
            Assert.Contains(line, onSpreadOne);
            Assert.Contains(line, onLaterSpread);
        }

        // The deference moves with the photograph's position, because a lock that named the wrong
        // image would tell the model to defer to a drawing.
        Assert.Contains("Image 1 is the identity reference photograph", onSpreadOne);
        Assert.Contains("Image 2 is the identity reference photograph", onLaterSpread);
    }

    /// <summary>
    /// The reviewer's copy of the spec is the same eight attributes in one line, and it does not
    /// tell the reviewer to defer to an image it is not judging.
    /// </summary>
    [Fact]
    public void The_reviewers_copy_of_the_spec_names_the_attributes_without_the_deference()
    {
        var spec = CompositeChildIdentity.Parse(GoodAnswer).Spec!;
        var text = CompositeChildIdentity.SpecText(spec);

        Assert.Contains("Face shape: round with a soft chin", text);
        Assert.Contains("Eyebrows: soft, medium-thick, gently arched", text);
        Assert.Contains("Eye colour: brown", text);
        Assert.Contains("Glasses: none", text);
        Assert.DoesNotContain("identity reference photograph", text);
        Assert.DoesNotContain("\n", text);
    }

    /// <summary>
    /// The eight attributes survive a round trip through storage, which is what a resumed run
    /// adopts. A spec that lost its eyebrows on the way to the blob would be a spec that drifts on
    /// resume for a reason nobody could see.
    /// </summary>
    [Fact]
    public void The_stored_spec_round_trips_all_eight_attributes()
    {
        var spec = CompositeChildIdentity.Parse(GoodAnswer).Spec!;

        var restored = CompositeChildIdentity.TryReadStored(CompositeChildIdentity.ToStoredJson(spec));

        Assert.Equal(spec, restored);

        // And a v1.1 document — four attributes and the old version string — is not adopted at all.
        Assert.Null(CompositeChildIdentity.TryReadStored(
            """
            {"derivation_version":"child-identity-spec-v1.1","hair_color":"dark brown",
             "hair_style":"wavy","eye_color":"brown","skin_tone":"light warm"}
            """));
    }

    // ---------------------------------------------------------------------------------------
    // The parent's own answer
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A parent looking at their child beats a model looking at a photograph in which the eyes may
    /// be forty pixels across — and eye colour is the attribute the drifting book lost outright.
    /// </summary>
    [Fact]
    public void A_parent_supplied_eye_colour_replaces_the_derived_one()
    {
        var derived = CompositeChildIdentity.Parse(GoodAnswer).Spec!;

        var overridden = CompositeChildIdentity.WithParentEyeColor(derived, "green");

        Assert.Equal("green", overridden.EyeColor);

        // Only that attribute. The form asks for one, and the other three are the model's.
        Assert.Equal(derived.HairColor, overridden.HairColor);
        Assert.Equal(derived.HairStyle, overridden.HairStyle);
        Assert.Equal(derived.SkinTone, overridden.SkinTone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_parent_answer_leaves_the_derived_value_alone(string? stated)
    {
        var derived = CompositeChildIdentity.Parse(GoodAnswer).Spec!;

        Assert.Equal(
            derived.EyeColor,
            CompositeChildIdentity.WithParentEyeColor(derived, stated).EyeColor);
    }

    /// <summary>
    /// A stored value long enough to be prose is not an eye colour, and a book is not worth failing
    /// over one. The derived value stands.
    /// </summary>
    [Fact]
    public void A_parent_answer_that_is_prose_is_ignored_rather_than_pasted_into_nine_prompts()
    {
        var derived = CompositeChildIdentity.Parse(GoodAnswer).Spec!;
        var essay = new string('x', CompositeChildIdentity.MaxAttributeLength + 1);

        Assert.Equal(
            derived.EyeColor,
            CompositeChildIdentity.WithParentEyeColor(derived, essay).EyeColor);
    }

    // ---------------------------------------------------------------------------------------
    // The call, in the pipeline
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// One identity call for a whole book, made before the first picture is bought, and its four
    /// attributes on all eight prompts.
    /// </summary>
    [Fact]
    public async Task The_spec_is_read_once_before_any_picture_and_locked_onto_every_page()
    {
        var images = new IdentityStubImageService();
        var identityAtFirstImage = -1;
        images.OnImage = () => identityAtFirstImage = images.IdentityCalls;

        var result = await Pipeline(images).RunAsync(Request(), CancellationToken.None);

        Assert.Equal(1, images.IdentityCalls);
        Assert.Equal(1, identityAtFirstImage);
        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        Assert.Equal(8, images.Prompts.Count);
        Assert.All(images.Prompts, prompt =>
        {
            Assert.Contains("CHILD IDENTITY LOCK", prompt);
            Assert.Contains("Hair colour: dark brown", prompt);
            Assert.Contains("Hair style: shoulder-length wavy with a soft fringe", prompt);
            Assert.Contains("Eye colour: brown", prompt);
            Assert.Contains("Skin tone: light warm", prompt);
        });

        // And the ask itself refuses everything that is not one of the four.
        Assert.Contains("Do not state or guess the child's name, ethnicity", images.IdentityPrompts[0]);
    }

    /// <summary>
    /// The parent's stored eye colour reaches the lock on every page — through the purchase record,
    /// which is the only place it lives, and never through the story input, which has nowhere to
    /// put it.
    /// </summary>
    [Fact]
    public async Task The_parents_eye_colour_reaches_every_image_prompt()
    {
        var images = new IdentityStubImageService();

        await Pipeline(images).RunAsync(
            Request(context: Context(eyeColor: "green")), CancellationToken.None);

        Assert.All(images.Prompts, prompt => Assert.Contains("Eye colour: green", prompt));
        Assert.All(images.Prompts, prompt => Assert.DoesNotContain("Eye colour: brown", prompt));

        // The four fields the planner may see still have nowhere to hold it.
        Assert.DoesNotContain(
            typeof(NormalizedBookInput).GetProperties(),
            property => property.Name.Contains("Eye", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// One unusable answer buys exactly one corrective retry, and the retry carries the reasons.
    /// </summary>
    [Fact]
    public async Task An_unusable_answer_is_retried_once_with_the_reasons_appended()
    {
        var images = new IdentityStubImageService();
        images.IdentityAnswers.Enqueue("I would rather not describe a child.");
        images.IdentityAnswers.Enqueue(GoodAnswer);

        var result = await Pipeline(images).RunAsync(Request(), CancellationToken.None);

        Assert.Equal(2, images.IdentityCalls);
        Assert.StartsWith(images.IdentityPrompts[0], images.IdentityPrompts[1]);
        Assert.Contains("The previous answer could not be used", images.IdentityPrompts[1]);
        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);
    }

    /// <summary>
    /// Two unusable answers stop the book with IDENTITY_SPEC_FAILED, before a single image is paid
    /// for.
    ///
    /// No soft-degrade, and the reason is evidence rather than principle: drawing without the lock
    /// is the arrangement that produced a book with a different child on every spread and eight
    /// PASSes to go with it. A pipeline that carried on here would restore that book minus the
    /// record of why.
    /// </summary>
    [Fact]
    public async Task Two_unusable_answers_stop_the_book_with_IDENTITY_SPEC_FAILED()
    {
        var images = new IdentityStubImageService();
        images.IdentityAnswers.Enqueue("no");
        images.IdentityAnswers.Enqueue("""{"hair_color":"dark brown"}""");

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(images).RunAsync(Request(), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.IdentitySpecFailed, failure.FailureCode);
        Assert.Equal(2, images.IdentityCalls);
        Assert.Equal(0, images.ImageCalls);
        Assert.Null(failure.Page);
    }

    /// <summary>
    /// A transport failure is the same thing as an unreadable answer — no spec — and gets the same
    /// one retry rather than losing the book to a dropped connection.
    /// </summary>
    [Fact]
    public async Task A_failed_identity_call_is_retried_once_and_then_stops_the_book()
    {
        var images = new IdentityStubImageService { FailIdentityCalls = 1 };

        var result = await Pipeline(images).RunAsync(Request(), CancellationToken.None);
        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        var both = new IdentityStubImageService { FailIdentityCalls = 2 };

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(both).RunAsync(Request(), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.IdentitySpecFailed, failure.FailureCode);
        Assert.Equal(0, both.ImageCalls);
    }

    /// <summary>The new code is registered beside the others and matches the supplied config.</summary>
    [Fact]
    public void IDENTITY_SPEC_FAILED_is_one_of_the_configured_failure_codes()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Assets", "BekiComposite", "pipeline_config_v1.json")));

        var configured = config.RootElement
            .GetProperty("failure_codes")
            .EnumerateArray()
            .Select(code => code.GetString()!)
            .ToList();

        Assert.Contains("IDENTITY_SPEC_FAILED", configured);
        Assert.Equal<IEnumerable<string>>(configured, CompositeFailureCodes.All);
    }

    // ---------------------------------------------------------------------------------------
    // Privacy
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Nothing the pipeline logs carries an attribute value — not on the happy path, and not when
    /// the answer is rejected, which is the log line most tempted to explain itself with the text
    /// it could not use.
    ///
    /// Nor a digest of one. An earlier version logged a SHA-256 of the four attributes salted with
    /// the job id, which reads as safe and is not: the attribute space is small enough to enumerate
    /// on a laptop, and the salt was printed on the same line as the digest, so anyone who could
    /// read the log could grind it back to the child's hair, eyes and skin. What is left is the
    /// event and the prompt version, which is what an operator actually reads these lines for.
    /// </summary>
    [Fact]
    public async Task No_attribute_value_appears_anywhere_in_the_pipelines_logs()
    {
        var log = new CapturingLogger();
        var images = new IdentityStubImageService();

        // A rejected first answer, so the rejection's own log line is under test too. The rejected
        // answer carries a distinctive attribute value that must not survive into the log.
        images.IdentityAnswers.Enqueue(
            """
            {"hair_color":"pillarbox scarlet","hair_style":"wavy","eye_color":"heterochromatic",
             "skin_tone":"light warm","mood":"cheerful"}
            """);
        images.IdentityAnswers.Enqueue(GoodAnswer);

        await Pipeline(images, log).RunAsync(Request(), CancellationToken.None);

        var everything = log.Everything;

        foreach (var value in (string[])
                 ["dark brown", "shoulder-length wavy with a soft fringe", "light warm",
                  "pillarbox scarlet", "heterochromatic", "cheerful"])
        {
            Assert.DoesNotContain(value, everything, StringComparison.OrdinalIgnoreCase);
        }

        // "brown" on its own is a substring of half the English language, so the eye colour is
        // checked as the line it would actually appear on.
        Assert.DoesNotContain("Eye colour:", everything);
        Assert.DoesNotContain("CHILD IDENTITY LOCK", everything);

        // No digest either: a hash over four low-entropy attributes, beside the job id that salted
        // it, is the attributes with extra steps. Checked on the identity lines as a shape rather
        // than by name, so reintroducing one under any name fails here. Scoped to those lines
        // because the composite receipt legitimately logs the approved pose PNG's SHA-256, which is
        // a hash of artwork rather than of a child.
        Assert.DoesNotMatch("[0-9a-f]{16,}", log.LinesMentioning("identity_spec"));

        // And the type no longer offers one to log. Its own declared members only — object's
        // GetHashCode is not a spec digest and cannot be removed.
        Assert.DoesNotContain(
            typeof(CompositeChildIdentity).GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.DeclaredOnly),
            method => method.Name.Contains("Fingerprint", StringComparison.OrdinalIgnoreCase)
                      || method.Name.Contains("Digest", StringComparison.OrdinalIgnoreCase));

        // And what IS there is the record the campaign asked for.
        Assert.Contains("identity_spec_derived", everything);
        Assert.Contains(CompositeChildIdentity.Version, everything);
        Assert.Contains("stage=identity_spec", everything);
    }

    // =======================================================================================
    // Harness
    // =======================================================================================

    private static CompositeBookPipeline Pipeline(
        IOpenAiService images, ILogger<CompositeBookPipeline>? logger = null) =>
        new(new ScenarioClient(ScenarioFixture()),
            images,
            new UnusedStoryService(),
            Options.Create(new BekiOptions
            {
                CompositePipelineEnabled = true,
                SpreadConcurrency = 1,
            }),
            Options.Create(new BekiPrintLayoutOptions()),
            logger ?? NullLogger<CompositeBookPipeline>.Instance);

    private static CompositeBookContext Context(string? eyeColor = null) => new()
    {
        JobId = Guid.NewGuid(),
        Input = new BookGenerationInput
        {
            ChildName = "ნინა",
            ChildAge = 1,
            ChildGender = "girl",
            ThemeId = "Dinosaurs",
            ChildPhotoRef = "books/nina/photo.jpg",
            LegacyEyeColor = eyeColor,
        }
    };

    private static CompositeBookRequest Request(CompositeBookContext? context = null) => new()
    {
        Context = context ?? Context(),
        ExistingPlan = Plan(),
        ChildPhoto = Png(512, 512),
        ChildPhotoContentType = "image/png",
    };

    private static MasterStory Plan()
    {
        using var input = JsonDocument.Parse(
            File.ReadAllText(FixturePath("visual_scenario_input_v2.json")));

        var spreads = input.RootElement
            .GetProperty("story_pages")
            .EnumerateArray()
            .Select(page => new StorySpread
            {
                Number = page.GetProperty("page").GetInt32(),
                Title = string.Empty,
                Caption = string.Empty,
                Text = page.GetProperty("story_text").GetString()!,
                Characters = ["child", "beki"],
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

    private static byte[] Png(int width, int height, byte red = 0)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(red, 0, 0, 255));
        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    /// <summary>Hands back the approved scenario; these tests never exercise its retry.</summary>
    private sealed class ScenarioClient(string scenario) : IStoryModelClient
    {
        public Task<ModelResult<T>> CompleteAsync<T>(
            string model, string systemPrompt, string userPrompt, string schemaName,
            JsonElement schema, CancellationToken cancellationToken) =>
            Task.FromResult(new ModelResult<T>(
                JsonSerializer.Deserialize<T>(scenario, StoryJson.Options)!, 1, 1));
    }

    private sealed class UnusedStoryService : IMasterStoryService
    {
        public string ModelName => "stub-story-model";

        public string PromptVersion => "v6";

        public (string System, string User) BuildPrompts(MasterStoryInput input) =>
            (string.Empty, string.Empty);

        public Task<MasterStoryResult> WriteAsync(
            MasterStoryInput input, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MasterStoryResult> RetryPlanWithCorrectionsAsync(
            MasterStoryInput input, IReadOnlyList<string> problems,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MasterStoryResult> WriteCompositePlanAsync(
            CompositeStoryInput input, IReadOnlyList<string> problems,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>
    /// The image door, with the identity call scripted separately from the QA verdicts.
    ///
    /// Both arrive through <see cref="IOpenAiService.ReviewIllustrationAsync"/> because they are
    /// the same shape of call — an image, an instruction, one text answer the caller validates —
    /// and the stub separates them the way anything else would: by reading what it was asked.
    /// </summary>
    private sealed class IdentityStubImageService : IOpenAiService
    {
        public int ImageCalls { get; private set; }
        public int IdentityCalls { get; private set; }
        public List<string> Prompts { get; } = [];
        public List<string> IdentityPrompts { get; } = [];
        public Queue<string> IdentityAnswers { get; } = new();

        /// <summary>How many identity calls throw before any answer is given.</summary>
        public int FailIdentityCalls { get; init; }

        /// <summary>Runs on the first image call, so "before any picture" is measurable.</summary>
        public Action? OnImage { get; set; }

        private const string PassVerdict =
            """{"status":"PASS","failed_checks":[],"recommended_action":"pass","notes":[]}""";

        public Task<byte[]> GenerateStoryImageAsync(
            string imagePrompt, StoryImageReference? reference,
            CancellationToken cancellationToken, string? imageSize = null,
            bool requireReferences = false)
        {
            if (ImageCalls == 0)
            {
                OnImage?.Invoke();
            }

            ImageCalls++;
            Prompts.Add(imagePrompt);

            using var image = new Image<Rgba32>(1536, 1024, new Rgba32((byte)(10 + ImageCalls), 0, 0, 255));
            using var buffer = new MemoryStream();
            image.SaveAsPng(buffer);
            return Task.FromResult(buffer.ToArray());
        }

        public Task<string> ReviewIllustrationAsync(
            byte[] imageBytes, string reviewPrompt,
            IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
            CancellationToken cancellationToken)
        {
            if (!reviewPrompt.StartsWith("You are the identity reader", StringComparison.Ordinal))
            {
                return Task.FromResult(PassVerdict);
            }

            IdentityCalls++;
            IdentityPrompts.Add(reviewPrompt);

            if (IdentityCalls <= FailIdentityCalls)
            {
                throw new HttpRequestException("the vision model is unreachable.");
            }

            return Task.FromResult(
                IdentityAnswers.Count > 0 ? IdentityAnswers.Dequeue() : GoodAnswer);
        }

        public Task<AdventureContentDto> GenerateAdventureContentAsync(
            AdventureGenerationInput input, Guid adventureId, CancellationToken cancellationToken) =>
            Task.FromResult(new AdventureContentDto());

        public Task<string> DescribeCharacterFromPhotoAsync(
            byte[] imageBytes, string contentType, string promptText,
            CancellationToken cancellationToken) => Task.FromResult("a child");

        public Task<string> CompleteTextAsync(string promptText, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
    }

    /// <summary>
    /// Everything the pipeline logged: the rendered message and every structured value behind it,
    /// because a value can reach a log through either.
    /// </summary>
    private sealed class CapturingLogger : ILogger<CompositeBookPipeline>
    {
        private readonly List<string> _lines = [];

        public string Everything => string.Join("\n", _lines);

        /// <summary>Only the lines about one thing, for assertions that would otherwise catch the
        /// rest of the pipeline's perfectly proper logging.</summary>
        public string LinesMentioning(string what) => string.Join(
            "\n", _lines.Where(line => line.Contains(what, StringComparison.Ordinal)));

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _lines.Add(formatter(state, exception));
            _lines.Add(exception?.ToString() ?? string.Empty);

            if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                _lines.AddRange(values.Select(value => value.Value?.ToString() ?? string.Empty));
            }
        }
    }
}
