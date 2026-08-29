using System.Text.Json;
using System.Text.Json.Nodes;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Json.Schema;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The contract layer of the BEKI composite pipeline, tested against the supplied fixture rather
/// than against examples written here.
///
/// The Nina book is the one the illustration side actually approved by hand, so it is the only
/// evidence that this code agrees with the people printing the result. A validator that passes
/// its own hand-written sample proves nothing; a validator that passes theirs, and fails each of
/// the four mutations that broke a real run, proves the rule is the rule they meant.
/// </summary>
public class CompositeContractsTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "nina_dinosaurs", name);

    private static string ContractPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "BekiComposite", "contracts", name);

    private static string ScenarioFixture() =>
        File.ReadAllText(FixturePath("visual_scenario_output_v2.json"));

    /// <summary>The fixture as a mutable tree, so a test can break exactly one thing.</summary>
    private static JsonNode Scenario() => JsonNode.Parse(ScenarioFixture())!;

    private static VisualScenarioValidationResult Validate(JsonNode scenario) =>
        VisualScenarioValidator.Validate(scenario.ToJsonString());

    /// <summary>
    /// A real, decodable image. Built rather than committed: the photo check decodes bytes, so the
    /// test needs bytes a decoder accepts, and one pixel is enough to be an image.
    /// </summary>
    private static byte[] OnePixelPng()
    {
        using var image = new Image<Rgba32>(1, 1);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static BookGenerationInput Purchase(
        int age = 5,
        string gender = "girl",
        string theme = "Dinosaurs") => new()
        {
            ChildName = "ნინა",
            ChildAge = age,
            ChildGender = gender,
            ThemeId = theme,
            ChildPhotoRef = "books/nina/photo.jpg"
        };

    // ---------------------------------------------------------------------------------------
    // Visual Scenario v2
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Approved_fixture_passes_both_validation_layers()
    {
        var result = VisualScenarioValidator.Validate(ScenarioFixture());

        Assert.True(result.IsValid, result.Summary);
        Assert.NotNull(result.Scenario);
        Assert.Equal(8, result.Scenario!.Spreads!.Count);
        Assert.Equal(3, result.Scenario.VisualLock!.RecurringElements!.Count);
        Assert.False(string.IsNullOrWhiteSpace(result.Scenario.Cover!.BackEnvironment));
    }

    [Fact]
    public void Beki_named_in_a_child_world_scene_is_rejected()
    {
        var scenario = Scenario();
        scenario["spreads"]![2]!["child_world_scene"] =
            "Beside Bafu, the child lifts the vine while Beki hovers close by.";

        var result = Validate(scenario);

        Assert.False(result.IsValid);
        Assert.True(result.Has(VisualScenarioProblemCodes.BekiInChildWorldScene), result.Summary);
        // The schema has no opinion about this one — it is a perfectly well-formed string. The
        // whole reason the semantic layer exists is that this is the fault that ruins a book.
        Assert.False(result.Has(VisualScenarioProblemCodes.SchemaViolation), result.Summary);
    }

    [Fact]
    public void Beki_named_in_georgian_in_a_child_world_scene_is_rejected()
    {
        var scenario = Scenario();
        scenario["spreads"]![0]!["child_world_scene"] =
            "In the warm valley the child pauses as ბეკი listens to the ferns.";

        var result = Validate(scenario);

        Assert.True(result.Has(VisualScenarioProblemCodes.BekiInChildWorldScene), result.Summary);
    }

    [Fact]
    public void Beki_named_in_the_back_environment_is_rejected()
    {
        var scenario = Scenario();
        scenario["cover"]!["back_environment"] =
            "The valley continues through layered ferns, with Beki drifting above the stream.";

        var result = Validate(scenario);

        Assert.True(result.Has(VisualScenarioProblemCodes.BekiInBackEnvironment), result.Summary);
    }

    [Fact]
    public void Seven_spreads_are_rejected_by_both_layers()
    {
        var scenario = Scenario();
        var spreads = scenario["spreads"]!.AsArray();
        spreads.RemoveAt(spreads.Count - 1);

        var result = Validate(scenario);

        Assert.False(result.IsValid);
        Assert.True(result.Has(VisualScenarioProblemCodes.SchemaViolation), result.Summary);
        Assert.True(result.Has(VisualScenarioProblemCodes.SpreadPagesInvalid), result.Summary);
        Assert.Contains(result.Problems, p => p.Detail.Contains("7 entries"));
    }

    [Fact]
    public void A_missing_child_outfit_is_rejected_by_both_layers()
    {
        var scenario = Scenario();
        scenario["visual_lock"]!.AsObject().Remove("child_outfit");

        var result = Validate(scenario);

        Assert.False(result.IsValid);
        Assert.True(result.Has(VisualScenarioProblemCodes.SchemaViolation), result.Summary);
        Assert.True(result.Has(VisualScenarioProblemCodes.EmptyRequiredString), result.Summary);
        Assert.Contains(result.Problems, p => p.Detail.Contains("visual_lock.child_outfit"));
    }

    [Fact]
    public void Page_five_appearing_twice_is_rejected()
    {
        var scenario = Scenario();
        scenario["spreads"]![5]!["page"] = 5;

        var result = Validate(scenario);

        Assert.False(result.IsValid);
        Assert.True(result.Has(VisualScenarioProblemCodes.SpreadPagesInvalid), result.Summary);
        Assert.Contains(result.Problems, p => p.Detail.Contains("spreads[5] is page 5"));
    }

    [Fact]
    public void A_beki_action_that_never_names_beki_is_rejected()
    {
        var scenario = Scenario();
        scenario["spreads"]![4]!["beki_action"] = "The guide gestures forward to support the choice.";

        var result = Validate(scenario);

        Assert.True(result.Has(VisualScenarioProblemCodes.BekiMissingFromAction), result.Summary);
    }

    [Fact]
    public void A_scene_that_never_names_the_child_is_rejected()
    {
        var scenario = Scenario();
        scenario["spreads"]![1]!["child_world_scene"] =
            "Among the giant ferns, a small girl points at a vine wrapped around a foot.";

        var result = Validate(scenario);

        Assert.True(result.Has(VisualScenarioProblemCodes.ChildMissingFromScene), result.Summary);
    }

    [Fact]
    public void An_unknown_key_is_rejected_by_the_supplied_schema()
    {
        var scenario = Scenario();
        scenario["pose_id"] = "pose_04_pointing";

        var result = Validate(scenario);

        // additionalProperties:false is one of the four constructs that made a hand-rolled
        // validator not worth writing; this is the test that the supplied file is what runs.
        Assert.True(result.Has(VisualScenarioProblemCodes.SchemaViolation), result.Summary);
    }

    [Fact]
    public void Text_that_is_not_json_fails_as_malformed_rather_than_throwing()
    {
        var result = VisualScenarioValidator.Validate("Here is the visual scenario you asked for:");

        Assert.False(result.IsValid);
        Assert.True(result.Has(VisualScenarioProblemCodes.MalformedJson), result.Summary);
        Assert.Null(result.Scenario);
    }

    // ---------------------------------------------------------------------------------------
    // Step 0 — input normalization
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, "1-2")]
    [InlineData(2, "1-2")]
    [InlineData(3, "3-5")]
    [InlineData(5, "3-5")]
    [InlineData(6, "6+")]
    [InlineData(99, "6+")]
    public void Age_maps_to_the_configured_band(int age, string expected) =>
        Assert.Equal(expected, InputNormalization.AgeBandFor(age));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_age_below_the_lowest_band_is_not_clamped_into_it(int age)
    {
        // The config's lowest band starts at 1. Rounding a zero up to "1-2" would mean writing a
        // book for an age nobody entered, so the honest answer is that no band claims it.
        Assert.Null(InputNormalization.AgeBandFor(age));

        var result = InputNormalization.Normalize(Purchase(age: age), OnePixelPng());

        Assert.False(result.IsValid);
        Assert.Equal(CompositeFailureCodes.InvalidBookInput, result.FailureCode);
    }

    [Fact]
    public void A_complete_purchase_normalizes_to_the_four_story_fields()
    {
        var result = InputNormalization.Normalize(
            Purchase(age: 4, gender: "girl", theme: "Dinosaurs") with { LegacyExtraWish = "a red balloon" },
            OnePixelPng());

        Assert.True(result.IsValid, string.Join("; ", result.Problems));
        Assert.Null(result.FailureCode);
        Assert.Equal("ნინა", result.Story!.ChildName);
        Assert.Equal("3-5", result.Story.AgeBand);
        Assert.Equal("girl", result.Story.ChildGender);
        Assert.Equal("dinosaurs", result.Story.ThemeId);
        Assert.Equal(ThemeType.Dinosaurs, result.Story.Theme);
        Assert.Equal("books/nina/photo.jpg", result.ChildPhotoRef);
    }

    [Fact]
    public void The_extra_wish_has_nowhere_to_land_in_the_normalized_form()
    {
        // Asserted structurally rather than by value: the guarantee is not "we did not copy it
        // this time", it is that the story-facing record has no field it could be copied into.
        var fields = typeof(NormalizedBookInput)
            .GetProperties()
            .Select(property => property.Name.ToLowerInvariant())
            .ToList();

        Assert.DoesNotContain(fields, name => name.Contains("wish"));
        Assert.DoesNotContain(fields, name => name.Contains("photo"));
        Assert.DoesNotContain(fields, name => name.Contains("eye"));
        Assert.DoesNotContain(fields, name => name.Contains("appearance"));
    }

    [Theory]
    [InlineData("Airplanes", "clouds")]
    [InlineData("Pirates", "ocean")]
    [InlineData("Animals", "forest")]
    [InlineData("Space", "space")]
    [InlineData("Magic", "magic")]
    [InlineData("dinosaurs", "dinosaurs")]
    [InlineData("2", "dinosaurs")]
    public void Backend_theme_values_map_to_canonical_ids(string stored, string canonical)
    {
        var result = InputNormalization.Normalize(Purchase(theme: stored), OnePixelPng());

        Assert.True(result.IsValid, string.Join("; ", result.Problems));
        Assert.Equal(canonical, result.Story!.ThemeId);
    }

    [Theory]
    [InlineData("Unicorns")]
    [InlineData("")]
    [InlineData("7")]
    [InlineData("mountains")]
    public void An_unknown_theme_is_refused_rather_than_guessed(string theme)
    {
        var result = InputNormalization.Normalize(Purchase(theme: theme), OnePixelPng());

        Assert.False(result.IsValid);
        Assert.Equal(CompositeFailureCodes.InvalidBookInput, result.FailureCode);
        Assert.Contains(result.Problems, problem => problem.Contains("theme_id"));
    }

    [Theory]
    [InlineData("girl", "girl")]
    [InlineData("Boy", "boy")]
    [InlineData("female", "girl")]
    [InlineData("ბიჭი", "boy")]
    public void Known_gender_spellings_map_to_the_two_contract_values(string stored, string canonical)
    {
        var result = InputNormalization.Normalize(Purchase(gender: stored), OnePixelPng());

        Assert.True(result.IsValid, string.Join("; ", result.Problems));
        Assert.Equal(canonical, result.Story!.ChildGender);
    }

    [Theory]
    [InlineData("not_specified")]
    [InlineData("nonbinary")]
    [InlineData("")]
    public void An_unknown_gender_is_refused_rather_than_guessed(string gender)
    {
        // "not_specified" is the Beki DTO's own default, which is exactly why it has to fail here
        // rather than quietly become a girl: the Visual Scenario contract admits two values.
        var result = InputNormalization.Normalize(Purchase(gender: gender), OnePixelPng());

        Assert.False(result.IsValid);
        Assert.Equal(CompositeFailureCodes.InvalidBookInput, result.FailureCode);
        Assert.Contains(result.Problems, problem => problem.Contains("child_gender"));
    }

    [Fact]
    public void A_photograph_that_is_not_an_image_is_refused_before_any_model_call()
    {
        var html = "<!doctype html><html><body>403 Forbidden</body></html>"u8.ToArray();

        var result = InputNormalization.Normalize(Purchase(), html);

        Assert.False(result.IsValid);
        Assert.Equal(CompositeFailureCodes.InvalidBookInput, result.FailureCode);
        Assert.Contains(result.Problems, problem => problem.Contains("could not be decoded"));
    }

    [Fact]
    public void A_missing_photograph_is_refused()
    {
        Assert.False(InputNormalization.Normalize(Purchase(), null).IsValid);
        Assert.False(InputNormalization.Normalize(Purchase(), []).IsValid);
    }

    [Fact]
    public void Every_reason_is_reported_not_only_the_first()
    {
        var result = InputNormalization.Normalize(
            Purchase(age: 0, gender: "unspecified", theme: "Unicorns") with { ChildName = "  " },
            null);

        Assert.False(result.IsValid);
        Assert.Equal(5, result.Problems.Count);
    }

    [Fact]
    public void The_failure_codes_are_the_supplied_configs_own_list()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "BekiComposite", "pipeline_config_v1.json")));

        var configured = config.RootElement
            .GetProperty("failure_codes")
            .EnumerateArray()
            .Select(code => code.GetString()!)
            .ToList();

        Assert.Equal<IEnumerable<string>>(configured, CompositeFailureCodes.All);
    }

    // ---------------------------------------------------------------------------------------
    // Step 1 — the story boundary
    // ---------------------------------------------------------------------------------------

    private static MasterStory Plan(int spreadCount = 8) => new()
    {
        Concept = new StoryConcept { Title = "ოქროსფერი ფოთოლი", Outline = ["beat"] },
        TitleEn = "The Golden Leaf",
        CharacterLock = "A girl with dark hair and green eyes, wearing a mustard romper.",
        WorldLock = "A warm golden valley.",
        Cover = new IllustrationBrief { Scene = "The child at the valley's edge." },
        Spreads = Enumerable.Range(1, spreadCount).Select(number => new StorySpread
        {
            Number = number,
            Title = string.Empty,
            Caption = string.Empty,
            Text = $"ქართული ტექსტი {number}",
            TextEn = $"Georgian text {number}",
            Illustration = new IllustrationBrief { Scene = $"Scene {number}" }
        }).ToList()
    };

    [Fact]
    public void The_boundary_maps_a_plan_to_a_title_and_eight_pages()
    {
        var result = StoryBoundary.From(Plan());

        Assert.True(result.IsValid, string.Join("; ", result.Problems));
        Assert.Equal("ოქროსფერი ფოთოლი", result.Boundary!.TitleKa);
        Assert.Equal(8, result.Boundary.StoryPages.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], result.Boundary.StoryPages.Select(page => page.Page));
        Assert.Equal("ქართული ტექსტი 4", result.Boundary.StoryPages[3].StoryText);
    }

    [Fact]
    public void The_boundary_leaves_the_english_and_the_childs_likeness_behind()
    {
        var json = JsonSerializer.Serialize(
            StoryBoundary.From(Plan()).Boundary, CompositeJson.Options);

        Assert.DoesNotContain("The Golden Leaf", json);
        Assert.DoesNotContain("Georgian text", json);
        Assert.DoesNotContain("green eyes", json);
        Assert.Contains("\"title_ka\"", json);
        Assert.Contains("\"story_pages\"", json);
    }

    [Fact]
    public void The_boundary_output_satisfies_the_supplied_story_boundary_schema()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(ContractPath("story_boundary_v1.schema.json")));
        var json = JsonSerializer.Serialize(
            StoryBoundary.From(Plan()).Boundary, CompositeJson.Options);

        using var document = JsonDocument.Parse(json);
        var results = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(results.IsValid);
    }

    [Fact]
    public void A_seven_spread_plan_is_refused_rather_than_padded()
    {
        var result = StoryBoundary.From(Plan(spreadCount: 7));

        Assert.False(result.IsValid);
        Assert.Equal(CompositeFailureCodes.StoryFailed, result.FailureCode);
        Assert.Contains(result.Problems, problem => problem.Contains("7 spreads"));
        Assert.Null(result.Boundary);
    }

    [Fact]
    public void A_plan_with_an_empty_spread_or_no_title_is_refused()
    {
        var untitled = Plan() with { Concept = new StoryConcept { Title = "   ", Outline = ["beat"] } };
        Assert.False(StoryBoundary.From(untitled).IsValid);

        var plan = Plan();
        var blank = plan with
        {
            Spreads = [plan.Spreads[0] with { Text = string.Empty }, .. plan.Spreads.Skip(1)]
        };
        Assert.False(StoryBoundary.From(blank).IsValid);
    }

    // ---------------------------------------------------------------------------------------
    // The composite story prompt and schema
    // ---------------------------------------------------------------------------------------

    private static CompositeStoryInput StoryInput(string ageBand = "3-5") => new()
    {
        ChildName = "ნინა",
        AgeBand = ageBand,
        Gender = "girl",
        ThemeId = "dinosaurs",
        Theme = ThemeType.Dinosaurs
    };

    [Fact]
    public void The_composite_prompt_asks_for_no_english_copy()
    {
        var system = MasterStoryPromptComposite.System(StoryInput());

        Assert.DoesNotContain("textEn", system);
        Assert.DoesNotContain("titleEn", system);
        Assert.DoesNotContain("English version", system);
        Assert.Contains("Georgian", system);
    }

    [Fact]
    public void The_composite_prompt_never_mentions_the_extra_wish()
    {
        var input = StoryInput();

        Assert.DoesNotContain("extra", MasterStoryPromptComposite.System(input), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("extra", MasterStoryPromptComposite.User(input), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_composite_prompt_never_describes_beki()
    {
        var system = MasterStoryPromptComposite.System(StoryInput());

        // v6 says, in as many words, that Beki is "a small, floating, magical leaf spirit". The
        // Georgian stem is checked too, because the story itself is Georgian and the prompt's own
        // spelling paragraph is the obvious place for a description to reappear.
        Assert.DoesNotContain("leaf spirit", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ფოთლ", system);
        Assert.Contains("Beki is a name and nothing else", system);
    }

    [Fact]
    public void The_composite_prompt_never_asks_for_the_childs_appearance()
    {
        var input = StoryInput();
        var system = MasterStoryPromptComposite.System(input);
        var user = MasterStoryPromptComposite.User(input);

        Assert.DoesNotContain("eye colour", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eye color", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("characterLock", system);
        Assert.DoesNotContain("eye colour", user, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("appearance", user, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_composite_prompt_sends_only_the_four_locked_inputs()
    {
        var user = MasterStoryPromptComposite.User(StoryInput());

        Assert.Contains("ნინა", user);
        Assert.Contains("age band: 3-5", user);
        Assert.Contains("gender: girl", user);
        Assert.Contains("dinosaurs", user);
        // The numeric age never travels: the band is mapped once, at the input boundary.
        Assert.DoesNotContain("age: ", user);
    }

    [Theory]
    [InlineData("1-2")]
    [InlineData("3-5")]
    [InlineData("6+")]
    public void The_composite_prompt_writes_a_budget_for_each_locked_band(string band)
    {
        var system = MasterStoryPromptComposite.System(StoryInput(band));

        Assert.Contains($"This book is for the {band} age band", system);
        Assert.Contains("Georgian words per spread", system);
    }

    [Fact]
    public void The_composite_prompt_refuses_a_band_that_is_not_locked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MasterStoryPromptComposite.System(StoryInput("2-4")));
    }

    [Fact]
    public void The_composite_schema_drops_the_english_and_identity_fields()
    {
        var json = CompositeStorySchema.Build().GetRawText();

        Assert.DoesNotContain("textEn", json);
        Assert.DoesNotContain("titleEn", json);
        Assert.DoesNotContain("characterLock", json);

        // worldLock is the place's lock, not the child's, so it stays; the illustration briefs
        // stay as the story's own account of what each spread shows.
        Assert.Contains("worldLock", json);
        Assert.Contains("illustration", json);
        Assert.Contains("\"scene\"", json);
    }

    [Fact]
    public void The_composite_schema_still_asks_for_eight_spreads_and_no_extra_keys()
    {
        var schema = CompositeStorySchema.Build();
        var json = schema.GetRawText();

        Assert.Contains("Exactly 8 spreads", json);
        Assert.Contains("additionalProperties", json);
        Assert.Equal(
            ["concept", "cast", "objects", "spreads", "worldLock", "cover"],
            schema.GetProperty("required").EnumerateArray().Select(entry => entry.GetString()));
    }

    [Fact]
    public void The_story_input_is_built_from_the_normalized_boundary()
    {
        var normalized = InputNormalization.Normalize(Purchase(age: 7), OnePixelPng()).Story!;

        var input = CompositeStoryInput.From(normalized);

        Assert.Equal("6+", input.AgeBand);
        Assert.Equal("dinosaurs", input.ThemeId);
        Assert.Equal(BookFormat.SpreadCount, input.SpreadCount);
    }
}
