using System.Net;
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
/// The wiring: the flag, the two entry points it branches at, and the pipeline it branches into.
///
/// Everything here runs offline against the approved Nina fixture and stubs of the two model
/// doors. That is not a compromise — it is what the tests are actually about. Whether Gemini writes
/// a good scenario is a question only a live call answers and a human judges; whether *this code*
/// sends the right prompt, validates the answer against the supplied schema, retries exactly once,
/// picks the pose the registry says, composites at the anchor the config says, and leaves the
/// previous pipeline untouched when the flag is off — all of that is decidable here, and all of it
/// is the part that breaks silently.
/// </summary>
public class CompositePipelineTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "nina_dinosaurs", name);

    private static string ScenarioFixture() =>
        File.ReadAllText(FixturePath("visual_scenario_output_v2.json"));

    private static string ResolvedPromptFixture() =>
        File.ReadAllText(FixturePath("spread_01_resolved_image_prompt_v2.txt"));

    // ---------------------------------------------------------------------------------------
    // The deterministic rhythm: two tables, one answer
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The plan's own condition for not duplicating the rhythm in two places: the config's text
    /// sides and <see cref="BekiSpreadRhythm"/>'s must be the same eight answers.
    ///
    /// The composite path reads the config and the legacy path reads the code, deliberately — the
    /// supplier owns the wording that reaches a model, and the existing table is what every book in
    /// production was drawn against. Two tables that must agree is a duplication; a test that fails
    /// the moment they stop agreeing is what makes it a safe one.
    /// </summary>
    [Fact]
    public void Config_text_sides_are_the_rhythm_the_legacy_path_already_uses()
    {
        Assert.Equal(BookFormat.SpreadCount, CompositeSpreadRhythm.Pages.Count);

        foreach (var page in CompositeSpreadRhythm.Pages)
        {
            Assert.Equal(
                BekiSpreadRhythm.TextSideFor(page).ToUpperInvariant(),
                CompositeSpreadRhythm.TextSideFor(page));
        }

        // And the alternation itself, so a config that changed both sides at once still fails.
        Assert.Equal("LEFT", CompositeSpreadRhythm.TextSideFor(1));
        Assert.Equal("RIGHT", CompositeSpreadRhythm.TextSideFor(8));
    }

    /// <summary>
    /// The image prompt tells the model where to leave Beki's zone; the config tells the compositor
    /// where to paste her. Those are the same two numbers, and if they ever stop being the same two
    /// numbers every book gets a character pasted over the scene instead of into the gap left for
    /// her.
    /// </summary>
    [Fact]
    public void The_empty_Beki_zone_the_prompt_asks_for_is_where_the_config_pastes_her()
    {
        var config = BekiCompositeConfig.Load();

        Assert.Equal(0.594, config.StoryDefaultFor(BekiTextSide.Left).VisibleCenterX, 3);
        Assert.Equal(0.406, config.StoryDefaultFor(BekiTextSide.Right).VisibleCenterX, 3);
        Assert.Equal(0.458, config.StoryDefaultFor(BekiTextSide.Left).VisibleCenterY, 3);

        Assert.Contains("59.4% of the canvas width", CompositeIllustrationPrompt.CompositionBlockFor("LEFT"));
        Assert.Contains("45.8% of the canvas height", CompositeIllustrationPrompt.CompositionBlockFor("LEFT"));
        Assert.Contains("40.6% of the canvas width", CompositeIllustrationPrompt.CompositionBlockFor("RIGHT"));
    }

    // ---------------------------------------------------------------------------------------
    // The image prompt, against the approved resolved prompt
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The prompt builder against the one resolved prompt a human approved a printed spread from.
    ///
    /// Not byte equality, and the difference matters. The fixture was resolved by hand: it writes
    /// the age in words, rewrites the theme sentence into that world's own vocabulary, and adds a
    /// hard constraint about this page's hidden character. What it demonstrates — and what is
    /// asserted — is the set of things that must be true of any resolved prompt on this path: the
    /// scene arrives verbatim, the outfit lock is in it, the deterministic text side and shot are
    /// the config's, the empty Beki zone is described, no Beki is asked for, and no text, logo,
    /// frame or QR is permitted anywhere.
    /// </summary>
    [Fact]
    public void The_spread_prompt_carries_everything_the_approved_prompt_demonstrates()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;
        var spread = scenario.Spreads![0];
        var theme = CompositeThemeReferences.For("dinosaurs");

        var prompt = CompositeIllustrationPrompt.ForSpread(new CompositeSpreadPromptInput
        {
            Page = 1,
            ChildAge = 1,
            Theme = theme,
            ChildWorldScene = spread.ChildWorldScene!,
            ChildOutfit = scenario.VisualLock!.ChildOutfit!,
            RecurringElements = CompositeIllustrationPrompt.RelevantRecurringElements(
                scenario.VisualLock.RecurringElements, spread.ChildWorldScene),
        });

        var approved = ResolvedPromptFixture();

        // The frame of the document, shared with the approved prompt word for word.
        Assert.StartsWith("Use case: illustration-story", prompt);
        foreach (var heading in (string[])
                 ["INPUT IMAGES", "SCENE", "CHILD LOCK", "RECURRING ELEMENTS REQUIRED ON THIS IMAGE",
                  "COMPOSITION", "STYLE AND MOOD", "HARD CONSTRAINTS"])
        {
            Assert.Contains(heading, prompt);
            Assert.Contains(heading, approved);
        }

        // The scene, verbatim from the scenario — the one string this pipeline promises never to
        // edit on its way to the image model.
        Assert.Contains(spread.ChildWorldScene!, prompt);
        Assert.Contains(spread.ChildWorldScene!, approved);

        // The outfit lock, likewise.
        Assert.Contains(scenario.VisualLock.ChildOutfit!, prompt);

        // Deterministic, and from the config rather than from the model.
        Assert.Contains(CompositeSpreadRhythm.ShotFor(1), prompt);
        Assert.Contains(CompositeSpreadRhythm.ShotFor(1), approved);
        Assert.Contains("Reserve the full left third", prompt);
        Assert.DoesNotContain("Reserve the full right third", prompt);

        // Spread 1 is the page where the small dinosaur is deliberately unseen. A prompt carrying
        // his description would have drawn him, which is the whole reason relevance is computed.
        Assert.Contains("RECURRING ELEMENTS REQUIRED ON THIS IMAGE\nNone.", prompt);
        Assert.DoesNotContain("Bafu", prompt);

        // No third image is mentioned, because none is attached.
        Assert.DoesNotContain("Image 3", prompt);

        // The scene itself never names Beki — the fault the whole two-stage design exists to
        // prevent — while the constraints forbid her outright, exactly as the approved prompt does.
        var scene = Section(prompt, "SCENE", "CHILD LOCK");
        Assert.DoesNotContain("Beki", scene, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not generate Beki.", prompt);
        Assert.Contains("Do not generate Beki.", approved);
        Assert.Contains("leaf spirit, lamb, sheep, or Beki-like character", prompt);

        // Nothing typographic, anywhere.
        Assert.Contains(
            "No text, letters, numbers, logos, captions, labels, signs, frames, QR codes, "
            + "watermarks, or pseudo-text anywhere.", prompt);

        // The panorama and the fold rule the layout stage depends on.
        Assert.Contains("final 15:7 crop", prompt);
        Assert.Contains("center-fold zone low-information", prompt);
    }

    /// <summary>
    /// The relevance rule, on the two pages that make it worth having: the one where the recurring
    /// character must not appear, and the one where he is named.
    /// </summary>
    [Fact]
    public void Only_the_recurring_elements_a_page_actually_needs_reach_its_prompt()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;
        var elements = scenario.VisualLock!.RecurringElements!;

        Assert.Empty(CompositeIllustrationPrompt.RelevantRecurringElements(
            elements, scenario.Spreads![0].ChildWorldScene));

        var onPageTwo = CompositeIllustrationPrompt.RelevantRecurringElements(
            elements, scenario.Spreads[1].ChildWorldScene);

        Assert.Contains(onPageTwo, element => element.StartsWith("Bafu,", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------
    // Flag OFF: the previous pipeline, untouched
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// With the flag off, neither entry point can reach the composite pipeline — proved by a spy
    /// that fails the test if it is called at all, not by reading the branch and agreeing with it.
    ///
    /// The composite context is supplied deliberately. A caller passing every input the composite
    /// path needs, with the flag off, must still be drawn by the previous path; if the branch ever
    /// keyed on the context rather than on the flag, this is where that would surface.
    /// </summary>
    [Fact]
    public async Task With_the_flag_off_both_entry_points_take_the_previous_path()
    {
        var spy = new SpyCompositePipeline();
        var images = new StubImageService();
        var generator = Generator(images, spy, compositeEnabled: false);

        var plan = Plan();

        var cover = await generator.DrawCoverAsync(
            plan, Photo(), "image/png", CancellationToken.None, Context());

        var book = await generator.IllustrateAsync(
            plan, Photo(), "image/png", cover.Image, null, CancellationToken.None,
            existingSpreads: null, composite: Context());

        Assert.Equal(0, spy.RunCalls);
        Assert.Equal(0, spy.CoverCalls);

        // The legacy path drew the cover and eight spreads through the ordinary image door, and
        // returned the legacy shape: no composite artifacts anywhere on it.
        Assert.Equal(9, images.ImageCalls);
        Assert.Null(book.Composite);
        Assert.All(book.Spreads, spread => Assert.Null(spread.Composition));
        Assert.Equal(BookFormat.SpreadCount, book.Spreads.Count);
    }

    /// <summary>
    /// The flag on, but from a caller that has no composite context — the preview cover, today.
    ///
    /// It draws by the previous path rather than failing, which is a judgement rather than an
    /// oversight: throwing would make the flag impossible to switch on in staging without breaking
    /// every preview, and the composite pipeline genuinely cannot run without the age band, gender
    /// and theme that caller does not hold.
    /// </summary>
    [Fact]
    public async Task With_the_flag_on_and_no_context_the_previous_path_still_draws_the_cover()
    {
        var spy = new SpyCompositePipeline();
        var images = new StubImageService();
        var generator = Generator(images, spy, compositeEnabled: true);

        var cover = await generator.DrawCoverAsync(
            Plan(), Photo(), "image/png", CancellationToken.None);

        Assert.Equal(0, spy.CoverCalls);
        Assert.Equal(1, images.ImageCalls);
        Assert.True(cover.Accepted);
    }

    // ---------------------------------------------------------------------------------------
    // Flag ON: input → boundary → scenario → prompt → image → composite → manifests
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_whole_book_walks_from_the_boundary_to_eight_composited_spreads()
    {
        var storyClient = new ScriptedStoryModelClient(ScenarioFixture());
        var images = new StubImageService();
        var pipeline = Pipeline(storyClient, images);

        var result = await pipeline.RunAsync(Request(), CancellationToken.None);

        // One scenario call for the whole book, as the contract says.
        Assert.Equal(1, storyClient.Calls);

        // The boundary: a Georgian title and eight numbered Georgian pages, and nothing else.
        Assert.Equal(BookFormat.SpreadCount, result.Boundary.StoryPages.Count);
        Assert.Equal(1, result.Boundary.StoryPages[0].Page);
        Assert.False(string.IsNullOrWhiteSpace(result.Boundary.TitleKa));

        // The scenario reached the image stage in the shape the validator accepted.
        Assert.True(VisualScenarioValidator.Validate(result.Artifacts.ScenarioJson).IsValid);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.Equal(BookFormat.SpreadCount, images.ReviewCalls);

        var first = result.Spreads[0];

        // The pose came from the scenario's Beki sentence — "Beki listens attentively" — through
        // the registry's keywords, with no model call involved.
        Assert.Equal("pose_04_listen", first.PoseId);
        Assert.Equal("LEFT", first.TextSide);
        Assert.False(first.PoseFallback);

        // The canvas is the printed spread, not the provider's frame — the base was normalized
        // before Beki was pasted, so these coordinates describe the page a reader will hold.
        var layer = first.Manifest.BekiLayer;
        Assert.Equal(SpreadWidth, first.Manifest.Canvas.WidthPx);
        Assert.Equal(SpreadHeight, first.Manifest.Canvas.HeightPx);
        Assert.Equal(0.333, layer.NormalizedAnchor.VisibleHeight, 3);
        Assert.Equal(239, layer.RenderedSizePx.HeightPx);
        Assert.False(layer.Mirrored || layer.Rotated || layer.Warped || layer.Redrawn);
        Assert.Equal(1.0, layer.Opacity);
        Assert.Equal(BekiCompositeEngine.ResamplerName, first.Manifest.Resampler);

        // A receipt per page, ready to be stored beside the picture it describes.
        Assert.Equal(BookFormat.SpreadCount, result.Artifacts.Spreads.Count);
        Assert.All(result.Artifacts.Spreads, artifact =>
        {
            Assert.False(string.IsNullOrWhiteSpace(artifact.OutputSha256));
            Assert.Contains("\"composition_version\": \"beki-exact-composite-v1\"", artifact.ManifestJson);
        });

        // The prompt the image model was actually sent, checked at the seam rather than only where
        // it was built: the scene, the outfit, the config's shot, and no Beki reference of any kind.
        var sent = images.Prompts[0];
        Assert.Contains(result.Scenario.Spreads![0].ChildWorldScene!, sent);
        Assert.Contains(result.Scenario.VisualLock!.ChildOutfit!, sent);
        Assert.Contains(CompositeSpreadRhythm.ShotFor(1), sent);

        // Two references on page one — the photograph and the approved world — and never a third
        // one carrying Beki. The continuity reference is the only third image this path can send,
        // and it only appears once a recurring element has actually been drawn: spread two
        // introduces Bafu with two references, and spread three, which reuses him, gets three.
        Assert.Equal(2, images.ReferenceCounts[0]);
        Assert.Equal(2, images.ReferenceCounts[1]);
        Assert.Equal(3, images.ReferenceCounts[2]);
        Assert.DoesNotContain("Image 3", images.Prompts[1]);
        Assert.Contains("Image 3 - continuity reference", images.Prompts[2]);
    }

    /// <summary>
    /// The seam the fulfilment job consumes: with the flag on and a context supplied, the
    /// illustrator returns the composite artifacts on the result and a receipt on every page.
    /// </summary>
    [Fact]
    public async Task The_illustrator_hands_the_fulfilment_job_a_scenario_and_a_receipt_per_page()
    {
        var storyClient = new ScriptedStoryModelClient(ScenarioFixture());
        var images = new StubImageService();
        var generator = Generator(
            images, Pipeline(storyClient, images), compositeEnabled: true);

        var delivered = new List<BekiImageResult>();

        var book = await generator.IllustrateAsync(
            Plan(), Photo(), "image/png",
            existingCover: BasePng(),
            onImage: image => { delivered.Add(image); return Task.CompletedTask; },
            cancellationToken: CancellationToken.None,
            existingSpreads: null,
            composite: Context());

        Assert.NotNull(book.Composite);
        Assert.Equal(BookFormat.SpreadCount, book.Composite!.Spreads.Count);
        Assert.True(VisualScenarioValidator.Validate(book.Composite.ScenarioJson).IsValid);

        Assert.Equal(BookFormat.SpreadCount, delivered.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], delivered.Select(image => image.SpreadNumber));
        Assert.All(delivered, image => Assert.NotNull(image.Composition));

        // And the manifest the job writes from them carries both new references, while a legacy
        // manifest is byte-identical to the ones written before this campaign.
        var composite = JsonSerializer.Serialize(
            new BekiFulfillmentManifest
            {
                IllustrationContract = ["a"],
                Entries = [new BekiFulfillmentManifestEntry(1, "https://blob/spread-01.png")],
                ScenarioUrl = "https://blob/visual-scenario.json",
                Compositions =
                [
                    new BekiCompositionManifestEntry(
                        1, "https://blob/spread-01-composition.json", "pose_04_listen", "abc",
                        "https://blob/spread-01-base.png")
                ],
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("scenarioUrl", composite);
        Assert.Contains("pose_04_listen", composite);

        var legacy = JsonSerializer.Serialize(
            new BekiFulfillmentManifest
            {
                IllustrationContract = ["a"],
                Entries = [new BekiFulfillmentManifestEntry(1, "https://blob/spread-01.png")],
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("scenarioUrl", legacy);
        Assert.DoesNotContain("compositions", legacy);
    }

    // ---------------------------------------------------------------------------------------
    // Resume: the scenario, the bases, and the contract that decides what may be adopted
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The scenario is written down before the first picture is bought, not after the last one.
    ///
    /// The failure it prevents: a job that dies on spread three had, until now, stored nothing about
    /// what it was drawing, so the retry planned a second scenario — a different outfit and
    /// different recurring elements — and then adopted the three pages drawn against the first one.
    /// Every page passes its own review and the child changes clothes at page four.
    /// </summary>
    [Fact]
    public async Task The_scenario_is_persisted_before_the_first_picture_is_drawn()
    {
        var images = new StubImageService();
        var imagesAtScenarioTime = -1;
        string? stored = null;

        await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images).RunAsync(
            Request(onScenario: json =>
            {
                imagesAtScenarioTime = images.ImageCalls;
                stored = json;
                return Task.CompletedTask;
            }),
            CancellationToken.None);

        Assert.Equal(0, imagesAtScenarioTime);
        Assert.NotNull(stored);
        Assert.True(VisualScenarioValidator.Validate(stored).IsValid);
    }

    /// <summary>
    /// A resumed run adopts the scenario the first attempt planned, so the pages it redraws are
    /// drawn against the outfit the pages it adopts were drawn against.
    ///
    /// The stored scenario here carries a deliberately different outfit from the one the model
    /// would return, which is the only way to tell "adopted" from "replanned and happened to
    /// match".
    /// </summary>
    [Fact]
    public async Task A_resumed_run_draws_against_the_stored_scenario_rather_than_a_new_one()
    {
        const string storedOutfit = "a teal corduroy pinafore with a single brass button.";

        var storyClient = new ScriptedStoryModelClient(ScenarioFixture());
        var images = new StubImageService();

        var resume = new CompositeResumeState(
            WithOutfit(storedOutfit),
            new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() },
            new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() });

        var result = await Pipeline(storyClient, images).RunAsync(
            Request(resume: resume), CancellationToken.None);

        // No scenario call at all: the book was already planned.
        Assert.Equal(0, storyClient.Calls);
        Assert.Equal(storedOutfit, result.Scenario.VisualLock!.ChildOutfit);

        // Six pages redrawn, two adopted, and every redrawn prompt carries the stored outfit.
        Assert.Equal(6, images.ImageCalls);
        Assert.All(images.Prompts, prompt => Assert.Contains(storedOutfit, prompt));

        // And the scenario that comes back out is the stored one, so what is re-persisted is what
        // the whole book was drawn against.
        Assert.Contains(storedOutfit, result.Artifacts.ScenarioJson);
    }

    /// <summary>
    /// A stored scenario that no longer satisfies the contract is replanned rather than obeyed —
    /// the supplier revises these rules, and a scenario written under the old ones is not a
    /// scenario this pipeline may draw from.
    ///
    /// With nothing drawn yet there is nothing to lose by replanning, which is the only case where
    /// replanning is free.
    /// </summary>
    [Fact]
    public async Task A_stored_scenario_that_no_longer_validates_is_replanned_when_nothing_is_drawn()
    {
        var storyClient = new ScriptedStoryModelClient(ScenarioFixture());

        var result = await Pipeline(storyClient, new StubImageService()).RunAsync(
            Request(resume: new CompositeResumeState(
                WithBekiInSceneThree(),
                new Dictionary<int, byte[]>(),
                new Dictionary<int, byte[]>())),
            CancellationToken.None);

        Assert.Equal(1, storyClient.Calls);
        Assert.Contains(result.Warnings, warning => warning.Contains("no longer validates"));
    }

    /// <summary>
    /// A replan with pages already drawn stops the job instead of finishing the book to a second
    /// specification.
    ///
    /// The pages that exist were drawn against the scenario that can no longer be used, so a new
    /// scenario would describe none of them: eight images each passing their own review, a stored
    /// scenario record matching none of them, and a child whose clothes change partway through.
    /// Redrawing silently would spend the image budget twice on artwork somebody may already have
    /// approved, and the cause is operational rather than a fault in the book — so a person decides.
    /// </summary>
    [Fact]
    public async Task A_replan_with_pages_already_drawn_stops_for_a_human()
    {
        var storyClient = new ScriptedStoryModelClient(ScenarioFixture());
        var images = new StubImageService();

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(storyClient, images).RunAsync(
                Request(resume: new CompositeResumeState(
                    WithBekiInSceneThree(),
                    new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() },
                    new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() })),
                CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.VisualScenarioFailed, failure.FailureCode);
        Assert.Contains("Visual Scenario", failure.Message);
        Assert.Contains("2 spread(s)", failure.Message);

        // Nothing was planned and nothing was drawn: the job stopped before spending anything.
        Assert.Equal(0, storyClient.Calls);
        Assert.Equal(0, images.ImageCalls);
    }

    /// <summary>
    /// The same rule when the scenario is simply missing — an earlier attempt that stored pages but
    /// no scenario, or a scenario blob that could not be read. Adopted artwork with no scenario to
    /// adopt is the same hazard by a different route.
    /// </summary>
    [Fact]
    public async Task Pages_stored_without_a_readable_scenario_stop_for_a_human()
    {
        var storyClient = new ScriptedStoryModelClient(ScenarioFixture());

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(storyClient, new StubImageService()).RunAsync(
                Request(resume: new CompositeResumeState(
                    // The job could not read the blob, so it passes nothing.
                    null,
                    new Dictionary<int, byte[]> { [1] = BasePng() },
                    new Dictionary<int, byte[]>())),
                CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.VisualScenarioFailed, failure.FailureCode);
        Assert.Equal(0, storyClient.Calls);
    }

    /// <summary>
    /// A replan that is allowed to go ahead draws all eight pages itself — there is nothing left to
    /// adopt, which is the condition under which replanning was permitted at all.
    /// </summary>
    [Fact]
    public async Task A_permitted_replan_draws_the_whole_book_under_the_new_scenario()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images).RunAsync(
            Request(resume: new CompositeResumeState(
                WithBekiInSceneThree(),
                new Dictionary<int, byte[]>(),
                // Bases with no pages of their own: they belong to the scenario being discarded, and
                // the reset in the pipeline drops them with it.
                new Dictionary<int, byte[]> { [1] = Png(SpreadWidth, SpreadHeight, red: 99) })),
            CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.All(result.Spreads, spread => Assert.False(spread.Adopted));
    }

    /// <summary>
    /// An adopted page still teaches the pages after it — from its BASE image, never its composite.
    ///
    /// Spread two introduces the story's creature. A resumed run that adopts spread two and redraws
    /// spread three used to send spread three with no continuity reference at all, which lets the
    /// creature be redesigned in the middle of a book where a reader sees both pages at once.
    /// </summary>
    [Fact]
    public async Task An_adopted_page_is_still_a_continuity_reference_for_the_pages_after_it()
    {
        var images = new StubImageService();

        // A base image that is identifiably spread two's, so the assertion is about which bytes
        // were attached and not merely how many.
        var spreadTwoBase = Png(1836, 857, red: 128);

        await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images).RunAsync(
            Request(resume: new CompositeResumeState(
                ScenarioFixture(),
                new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() },
                new Dictionary<int, byte[]> { [1] = BasePng(), [2] = spreadTwoBase })),
            CancellationToken.None);

        // Spread three is the first page redrawn, and it reuses the creature spread two introduced.
        Assert.Contains("Image 3 - continuity reference", images.Prompts[0]);
        Assert.Equal(3, images.ReferenceCounts[0]);
        Assert.Equal(spreadTwoBase, images.ContinuityImages[0]);
    }

    /// <summary>
    /// The composited page must never be the continuity reference. It has the approved Beki pasted
    /// onto it, and the continuity instruction tells the model to copy the named elements from the
    /// attached picture — so handing it a composite is handing it a picture of Beki, on the one
    /// pipeline whose entire promise is that no image model is ever shown her.
    /// </summary>
    [Fact]
    public async Task The_composited_page_is_never_sent_as_a_continuity_reference()
    {
        var images = new StubImageService();
        var composited = Png(1836, 857, red: 200);

        await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images).RunAsync(
            Request(resume: new CompositeResumeState(
                ScenarioFixture(),
                // The stored composite for spreads one and two…
                new Dictionary<int, byte[]> { [1] = composited, [2] = composited },
                // …and their bases, which are what continuity may use.
                new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() })),
            CancellationToken.None);

        Assert.All(images.ContinuityImages, image => Assert.NotEqual(composited, image));
    }

    /// <summary>
    /// An adopted page whose base was never stored is a continuity gap, and the run says so instead
    /// of quietly drawing the rest of the book without it.
    /// </summary>
    [Fact]
    public async Task An_adopted_page_with_no_stored_base_is_reported_as_a_continuity_gap()
    {
        var result = await Pipeline(
                new ScriptedStoryModelClient(ScenarioFixture()), new StubImageService())
            .RunAsync(
                Request(resume: new CompositeResumeState(
                    ScenarioFixture(),
                    new Dictionary<int, byte[]> { [2] = BasePng() },
                    new Dictionary<int, byte[]>())),
                CancellationToken.None);

        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("adopted without its base image"));
    }

    /// <summary>
    /// The most recent accepted appearance is the continuity reference, not the first one ever.
    ///
    /// Each spread is drawn from the one before it, so by spread seven the creature has drifted from
    /// where spread two left it. Matching spread seven against spread two asks a model to undo six
    /// pages of change in one step; matching it against spread six asks for one page's worth. The
    /// contract asks for "the most recent approved image", and keeping the first was the bug.
    /// </summary>
    [Fact]
    public async Task Continuity_tracks_the_most_recent_accepted_appearance()
    {
        var images = new StubImageService();

        await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        // Spreads 2, 3, 4, 7 and 8 all name Bafu, so each of them after the first is drawn against
        // the page immediately before it rather than against spread two forever.
        var third = images.ContinuityImages[2];
        var fourth = images.ContinuityImages[3];

        Assert.NotNull(third);
        Assert.NotNull(fourth);

        // The stub returns a distinct picture per call, so "the reference moved on" is checkable.
        Assert.NotEqual(third, fourth);

        // And what is kept is the NORMALIZED base — the spread-shaped picture Beki was pasted onto
        // — rather than the provider's 3:2 frame, so a later page is matched against the same
        // canvas it will itself be drawn to.
        Assert.Equal(
            SpreadArtCrop.CropToRatio(images.Returned[2], 15f / 7f),
            fourth);
    }

    /// <summary>
    /// The resume contract names the pipeline that drew the pages.
    ///
    /// Without it, flipping the composite flag between two attempts at the same pack adopts pages
    /// whose Beki an image model invented into a book whose Beki is an approved PNG — eight pages,
    /// two different characters, each page individually passing its own review.
    /// </summary>
    [Fact]
    public void The_resume_contract_distinguishes_the_two_pipelines()
    {
        var legacy = BekiFulfillmentManifest.CurrentContract(BookFormat.SpreadCount);
        var composite = BekiFulfillmentManifest.CurrentContract(
            BookFormat.SpreadCount, BekiCompositeContractTerms.Current("dinosaurs"));

        // A flag flip in either direction is a mismatch, which is what makes the manifest redraw.
        Assert.NotEqual(legacy, composite);
        Assert.False(legacy.SequenceEqual(composite));

        // The legacy contract is untouched by this change, so no book already in flight on the
        // previous path is invalidated by it.
        Assert.Equal(BookFormat.SpreadCount, legacy.Count);
        Assert.Equal(BookFormat.SpreadCount + 1, composite.Count);
        Assert.Equal(legacy, composite.Skip(1));

        // And the composite line carries the versions that decide what a page looks like.
        var terms = composite[0];
        Assert.StartsWith("composite|", terms);
        Assert.Contains(BekiCompositeConfig.Load().PoseRegistryVersion, terms);
        Assert.Contains(BekiCompositeConfig.Load().ConfigVersion, terms);
        Assert.Contains(MasterStoryPromptComposite.Version, terms);
        Assert.Contains(CompositeIllustrationPrompt.Version, terms);
    }

    /// <summary>
    /// The contract names the approved world PNG by its own hash, not by the registry's version.
    ///
    /// A world can be re-art-directed — a lighter palette, a redrawn skyline — inside the same
    /// registry version. A resumed run would then adopt spreads drawn against the old picture and
    /// draw the rest against the new one: two visual worlds in one book, every page individually
    /// fine and passing its own review. Only the file's hash catches that.
    /// </summary>
    [Fact]
    public void The_resume_contract_names_the_theme_reference_by_its_hash()
    {
        var dinosaurs = BekiCompositeContractTerms.Current("dinosaurs");
        var ocean = BekiCompositeContractTerms.Current("ocean");

        // The real hash from the shipped registry, not a version string.
        var hash = CompositeThemeReferences.RegisteredSha256("dinosaurs");
        Assert.Equal(64, hash.Length);
        Assert.Contains(hash, dinosaurs.ToString());

        // Two worlds, two contracts — so a book cannot resume across a change of theme artwork.
        Assert.NotEqual(dinosaurs.ToString(), ocean.ToString());
        Assert.NotEqual(
            BekiFulfillmentManifest.CurrentContract(BookFormat.SpreadCount, dinosaurs),
            BekiFulfillmentManifest.CurrentContract(BookFormat.SpreadCount, ocean));

        // And a re-arted world — same versions, different file — is a different contract too.
        var reArted = dinosaurs with { ThemeReferenceSha256 = new string('0', 64) };
        Assert.NotEqual(dinosaurs.ToString(), reArted.ToString());
        Assert.Equal(dinosaurs.PoseRegistryVersion, reArted.PoseRegistryVersion);
        Assert.Equal(dinosaurs.PipelineConfigVersion, reArted.PipelineConfigVersion);
    }

    // ---------------------------------------------------------------------------------------
    // The Visual Scenario's one retry
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task An_invalid_scenario_is_retried_once_with_the_reasons_appended()
    {
        var storyClient = new ScriptedStoryModelClient(WithBekiInSceneThree(), ScenarioFixture());
        var pipeline = Pipeline(storyClient, new StubImageService());

        var result = await pipeline.RunAsync(Request(), CancellationToken.None);

        Assert.Equal(2, storyClient.Calls);
        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        // The first ask goes out whole; the second is the same ask with the validator's list on the
        // end of it, not a rewritten instruction.
        Assert.StartsWith(storyClient.UserPrompts[0], storyClient.UserPrompts[1]);
        Assert.Contains(VisualScenarioProblemCodes.BekiInChildWorldScene, storyClient.UserPrompts[1]);
        Assert.Contains("The previous answer was rejected", storyClient.UserPrompts[1]);

        // And the system instruction is the contract's, unchanged between the two attempts.
        Assert.Equal(storyClient.SystemPrompts[0], storyClient.SystemPrompts[1]);
        Assert.Contains("You are the Visual Scenario Planner", storyClient.SystemPrompts[0]);
    }

    [Fact]
    public async Task Two_invalid_scenarios_stop_the_book_with_VISUAL_SCENARIO_FAILED()
    {
        var storyClient = new ScriptedStoryModelClient(
            WithBekiInSceneThree(), WithBekiInSceneThree());
        var images = new StubImageService();
        var pipeline = Pipeline(storyClient, images);

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            pipeline.RunAsync(Request(), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.VisualScenarioFailed, failure.FailureCode);
        Assert.Equal(2, storyClient.Calls);

        // Nothing was drawn. The whole point of validating before the image stage is that a bad
        // scenario costs one text call, not nine image calls.
        Assert.Equal(0, images.ImageCalls);
    }

    // ---------------------------------------------------------------------------------------
    // Minimal visual QA and its retry rules
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_base_image_failure_buys_exactly_one_new_image()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("MAIN_SCENE_BEAT", CompositeQaVerdict.ActionRegenerateBase));

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        // Nine calls for eight spreads: spread one was drawn twice and then passed.
        Assert.Equal(BookFormat.SpreadCount + 1, images.ImageCalls);
        Assert.Equal(2, result.Spreads[0].BaseAttempts);
        Assert.Equal(1, result.Spreads[1].BaseAttempts);
    }

    [Fact]
    public async Task A_placement_failure_is_re_composited_without_another_image_call()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("BEKI_INTEGRATION", CompositeQaVerdict.ActionRecompositeBeki));

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.Equal(1, result.Spreads[0].BaseAttempts);

        // The review ran twice for spread one and once for each of the others.
        Assert.Equal(BookFormat.SpreadCount + 1, images.ReviewCalls);
    }

    [Fact]
    public async Task A_second_failure_stops_the_book_with_IMAGE_QA_FAILED()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("CHILD_IDENTITY", CompositeQaVerdict.ActionRegenerateBase));
        images.Verdicts.Enqueue(Fail("CHILD_IDENTITY", CompositeQaVerdict.ActionHumanReview));

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageQaFailed, failure.FailureCode);
        Assert.Equal(1, failure.Page);

        // One regeneration, and no third attempt.
        Assert.Equal(2, images.ImageCalls);
    }

    /// <summary>
    /// The deterministic side of the QA contract, which runs before any of the above.
    /// </summary>
    [Fact]
    public void A_verdict_is_read_strictly_and_a_composite_is_checked_from_its_manifest()
    {
        Assert.True(CompositeMinimalQa.Parse(File.ReadAllText(FixturePath("spread_01_minimal_qa.json"))).IsValid);

        // A PASS carrying a failed check is not a pass; the schema's conditional says so and the
        // reader defers to the schema rather than reading `status` and stopping there.
        var contradictory = CompositeMinimalQa.Parse(
            """{"status":"PASS","failed_checks":["CAST_ERROR"],"recommended_action":"pass","notes":[]}""");
        Assert.False(contradictory.IsValid);

        // An invented category is not one of the nine.
        Assert.False(CompositeMinimalQa.Parse(
            """{"status":"FAIL","failed_checks":["UGLY"],"recommended_action":"human_review","notes":[]}""")
            .IsValid);

        // Prose is not a verdict.
        Assert.False(CompositeMinimalQa.Parse("Looks good to me!").IsValid);

        // A fenced verdict still is one: the wrapper is forgiven, the content is not.
        Assert.True(CompositeMinimalQa.Parse(
            "```json\n{\"status\":\"PASS\",\"failed_checks\":[],\"recommended_action\":\"pass\",\"notes\":[]}\n```")
            .IsValid);
    }

    [Fact]
    public void A_render_of_the_wrong_shape_never_reaches_the_compositor()
    {
        Assert.Empty(CompositeDeterministicChecks.BaseImageProblems(BasePng()));

        // 3:2, which is what the providers actually offer, normalizes with 30% of the height gone.
        Assert.Empty(CompositeDeterministicChecks.BaseImageProblems(Png(1536, 1024)));

        // A portrait render is not a spread, however good the picture is.
        Assert.NotEmpty(CompositeDeterministicChecks.BaseImageProblems(Png(1024, 1536)));

        Assert.NotEmpty(CompositeDeterministicChecks.BaseImageProblems([]));
        Assert.NotEmpty(CompositeDeterministicChecks.BaseImageProblems([1, 2, 3, 4]));
    }

    // ---------------------------------------------------------------------------------------
    // The printed canvas
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Beki is pasted onto the page that prints, not onto the frame the provider returned.
    ///
    /// The failure this prevents is arithmetic and invisible. The image models give 3:2; the spread
    /// prints at 15:7; the layout stage removes the difference. Compositing before that crop meant
    /// the configured 0.333 of the canvas became about 0.476 of the printed page — Beki half again
    /// the approved size — the manifest recorded coordinates for a canvas that was never a page, and
    /// the reviewer judged a frame with bands top and bottom that no reader would ever see.
    /// </summary>
    [Fact]
    public async Task The_base_is_normalized_to_the_printed_spread_before_Beki_is_pasted()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        // What the provider returned, and what was composited on.
        var provider = Image.Identify(images.Returned[0]);
        Assert.Equal(ProviderWidth, provider.Width);
        Assert.Equal(ProviderHeight, provider.Height);

        var canvas = result.Spreads[0].Manifest.Canvas;
        Assert.Equal(SpreadWidth, canvas.WidthPx);
        Assert.Equal(SpreadHeight, canvas.HeightPx);

        // The crop is centred and never stretched: the width is untouched and only the height moved.
        Assert.Equal(provider.Width, canvas.WidthPx);
        Assert.True(canvas.HeightPx < provider.Height);
        Assert.Equal(
            15.0 / 7.0,
            (double)canvas.WidthPx / canvas.HeightPx,
            CompositeDeterministicChecks.SpreadAspectTolerance);

        // The number the whole finding is about: Beki occupies the configured fraction OF THE
        // PRINTED PAGE. Composited on the provider frame she would have measured about 0.476 here.
        var layer = result.Spreads[0].Manifest.BekiLayer;
        var printedFraction = (double)layer.RenderedSizePx.HeightPx / canvas.HeightPx;

        Assert.Equal(0.333, printedFraction, 2);
        Assert.True(printedFraction < 0.40, $"Beki is {printedFraction:F3} of the printed page.");

        // And the base kept for storage and continuity is the normalized one, so a re-composite or
        // a resumed run works from the same canvas.
        var storedBase = Image.Identify(result.Spreads[0].BasePng);
        Assert.Equal(SpreadWidth, storedBase.Width);
        Assert.Equal(SpreadHeight, storedBase.Height);

        // The composited page is the same canvas as the base it was made from.
        var page = Image.Identify(result.Spreads[0].CompositePng);
        Assert.Equal(SpreadWidth, page.Width);
        Assert.Equal(SpreadHeight, page.Height);
    }

    /// <summary>
    /// A render already at the printed ratio passes through untouched — normalization trims what is
    /// in excess of the target and never crops for the sake of cropping.
    /// </summary>
    [Fact]
    public void A_spread_shaped_render_is_already_normalized()
    {
        var spread = SpreadShapedPng();

        Assert.Empty(CompositeDeterministicChecks.NormalizedSpreadProblems(spread));
        Assert.Equal(spread, SpreadArtCrop.CropToRatio(spread, 15f / 7f));

        // And the provider's own frame is not: this is what the check is for.
        Assert.NotEmpty(CompositeDeterministicChecks.NormalizedSpreadProblems(
            Png(ProviderWidth, ProviderHeight)));
    }

    // ---------------------------------------------------------------------------------------
    // The response schema the provider is actually sent
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The scenario request uses a shape a strict structured-output mode accepts.
    ///
    /// Sending the supplied Draft 2020-12 file was, on the default configuration, a book that could
    /// not be written at all: OpenAI's strict mode rejects prefixItems, a boolean items, minItems,
    /// maxItems and minLength, and the supplied schema uses all five — so both attempts died on the
    /// request and nothing was ever generated to validate.
    /// </summary>
    [Fact]
    public void The_scenario_request_schema_avoids_every_keyword_strict_mode_rejects()
    {
        var schema = CompositeVisualScenarioPrompt.ResponseSchema();
        var text = schema.GetRawText();

        foreach (var rejected in (string[])
                 ["prefixItems", "minItems", "maxItems", "minLength", "maxLength", "pattern",
                  "$defs", "$ref", "allOf", "const"])
        {
            Assert.DoesNotContain(rejected, text);
        }

        // "items": false is the other rejected form, and it is a shape rather than a keyword.
        Assert.DoesNotContain("\"items\":false", text.Replace(" ", string.Empty));

        // Every object closed and every property required, which strict mode demands of all of them.
        AssertStrictObject(schema);

        static void AssertStrictObject(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return;

            if (element.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "object")
            {
                Assert.True(element.TryGetProperty("additionalProperties", out var additional));
                Assert.False(additional.GetBoolean());

                var properties = element.GetProperty("properties")
                    .EnumerateObject().Select(p => p.Name).ToList();
                var required = element.GetProperty("required")
                    .EnumerateArray().Select(r => r.GetString()!).ToList();

                Assert.Equal(properties.OrderBy(n => n), required.OrderBy(n => n));
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    AssertStrictObject(property.Value);
                }
                else if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        AssertStrictObject(item);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The request shape and the supplied contract describe the same document: same property names,
    /// same nesting, same types. A request that asked for different field names would return an
    /// answer the validator could only reject.
    /// </summary>
    [Fact]
    public void The_scenario_request_schema_asks_for_the_supplied_contracts_own_fields()
    {
        var sent = CompositeVisualScenarioPrompt.ResponseSchema();

        using var supplied = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Assets", "BekiComposite", "contracts",
            VisualScenarioValidator.SchemaFileName)));

        Assert.Equal(Names(supplied.RootElement), Names(sent));

        Assert.Equal(
            Names(supplied.RootElement.GetProperty("properties").GetProperty("visual_lock")),
            Names(sent.GetProperty("properties").GetProperty("visual_lock")));

        Assert.Equal(
            Names(supplied.RootElement.GetProperty("properties").GetProperty("cover")),
            Names(sent.GetProperty("properties").GetProperty("cover")));

        // The spreads differ in form on purpose — eight prefixItems there, one items object here —
        // so the comparison is of the entry each of them describes.
        Assert.Equal(
            Names(supplied.RootElement.GetProperty("$defs").GetProperty("spreadBase")),
            Names(sent.GetProperty("properties").GetProperty("spreads").GetProperty("items")));

        // And the fixture the whole pipeline is built on satisfies both.
        Assert.True(VisualScenarioValidator.Validate(ScenarioFixture()).IsValid);

        static IEnumerable<string> Names(JsonElement schema) =>
            schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(n => n);
    }

    /// <summary>
    /// The supplied file stays the authority: an answer the request shape permits but the contract
    /// forbids is still a validation failure that spends the retry.
    ///
    /// Four recurring elements is exactly that answer — the request shape has no maxItems to state
    /// the limit, and the contract's maxItems of three does.
    /// </summary>
    [Fact]
    public async Task An_answer_the_request_shape_allows_but_the_contract_forbids_is_still_retried()
    {
        var storyClient = new ScriptedStoryModelClient(WithFourRecurringElements(), ScenarioFixture());

        var result = await Pipeline(storyClient, new StubImageService())
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(2, storyClient.Calls);
        Assert.Equal(3, result.Scenario.VisualLock!.RecurringElements!.Count);
        Assert.Contains(
            VisualScenarioProblemCodes.TooManyRecurringElements, storyClient.UserPrompts[1]);
    }

    // ---------------------------------------------------------------------------------------
    // The references are the picture
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Every composite image call demands that its references were actually sent.
    ///
    /// The OpenAI path retries the edit route and, when it still fails, quietly draws from the
    /// prompt alone. On the A5 flow that is the right trade — the prompt carries a written
    /// appearance description, so the hero comes back slightly off rather than wrong. On this path
    /// the child's likeness exists ONLY in the attached photograph, the world only in the approved
    /// theme reference, and a recurring creature only in the continuity image, so the same fallback
    /// returns a stranger in a generic world — which is then composited with the approved Beki,
    /// reviewed, stored and printed.
    /// </summary>
    [Fact]
    public async Task Every_composite_image_call_refuses_a_picture_drawn_without_its_references()
    {
        var images = new StubImageService();

        await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, images.StrictFlags.Count);
        Assert.All(images.StrictFlags, strict => Assert.True(strict));
    }

    /// <summary>
    /// And when the references genuinely cannot be sent, the book stops with the failure code for
    /// it rather than continuing with an unanchored picture.
    /// </summary>
    [Fact]
    public async Task A_reference_less_image_call_fails_the_book_with_IMAGE_GENERATION_FAILED()
    {
        var images = new StubImageService { FailWhenStrict = true };

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageGenerationFailed, failure.FailureCode);
        Assert.Equal(1, failure.Page);
        Assert.Equal(0, images.ImageCalls);
    }

    /// <summary>
    /// The strict flag is inert for every caller that does not ask for it — which is every caller
    /// but the composite pipeline. The legacy path keeps its fallback, because a book with a
    /// slightly-off hero beats a failed job when the prompt still describes the child.
    /// </summary>
    [Fact]
    public async Task The_previous_path_still_asks_for_pictures_the_old_way()
    {
        var images = new StubImageService();
        var generator = Generator(images, new SpyCompositePipeline(), compositeEnabled: false);

        await generator.IllustrateAsync(
            Plan(), Photo(), "image/png", BasePng(), null, CancellationToken.None);

        Assert.NotEmpty(images.StrictFlags);
        Assert.All(images.StrictFlags, strict => Assert.False(strict));
    }

    /// <summary>
    /// The router refuses a strict call carrying no references at all, whichever vendor would have
    /// drawn it — the half of the rule that is not about any one provider's fallback.
    /// </summary>
    [Fact]
    public async Task The_router_refuses_a_strict_call_with_nothing_attached()
    {
        var router = new AiServiceRouter(
            new StubImageService(), new NoOpIllustrationClient(),
            NullLogger<AiServiceRouter>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            router.GenerateStoryImageAsync(
                "draw", null, CancellationToken.None, "1536x1024", requireReferences: true));

        // And the same call without the flag is the behaviour every existing caller has.
        var drawn = await router.GenerateStoryImageAsync("draw", null, CancellationToken.None);
        Assert.NotEmpty(drawn);
    }

    // ---------------------------------------------------------------------------------------
    // A generated image that is not a picture
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The same header-versus-file trap as the photograph boundary, on the other side of the
    /// pipeline: a truncated response keeps its header and reports the right dimensions, so a check
    /// that read only the header passed it along and the run then died inside the normalization
    /// crop as an ImageSharp exception about a corrupt stream — with no failure code and no page
    /// number, on a book somebody paid for.
    /// </summary>
    [Fact]
    public void A_truncated_generated_image_is_caught_by_the_deterministic_check()
    {
        var truncated = TruncatedJpeg(ProviderWidth, ProviderHeight);

        // The trap: the header is intact and says exactly what a good render would say.
        var identified = Image.Identify(truncated);
        Assert.Equal(ProviderWidth, identified.Width);
        Assert.Equal(ProviderHeight, identified.Height);

        var problems = CompositeDeterministicChecks.BaseImageProblems(truncated);
        Assert.NotEmpty(problems);
        Assert.Contains("could not be decoded", problems[0]);
    }

    [Fact]
    public async Task A_truncated_generated_image_stops_the_page_with_IMAGE_GENERATION_FAILED()
    {
        var images = new StubImageService
        {
            NextImage = TruncatedJpeg(ProviderWidth, ProviderHeight),
        };

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(), CancellationToken.None));

        // A named code and a page, rather than a decoder exception from three steps later.
        Assert.Equal(CompositeFailureCodes.ImageGenerationFailed, failure.FailureCode);
        Assert.Equal(1, failure.Page);
        Assert.Contains("not usable", failure.Message);
    }

    // ---------------------------------------------------------------------------------------
    // The cover
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// No printer-approved cover geometry is configured anywhere in this application, so the
    /// composite cover fails with the word the contract names — and does not quietly reuse the
    /// interior sheet's bleed, which is the one substitution the contract forbids outright.
    /// </summary>
    [Fact]
    public async Task Without_cover_geometry_the_cover_stops_with_LAYOUT_FAILED()
    {
        var pipeline = Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), new StubImageService());

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            pipeline.DrawCoverAsync(
                Context(), new VisualScenarioV2(), Photo(), "image/png", CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.LayoutFailed, failure.FailureCode);
        Assert.Null(CompositeCoverGeometryResolver.TryResolve(new BekiPrintLayoutOptions()));
    }

    [Fact]
    public async Task A_composite_book_with_no_previewed_cover_stops_rather_than_shipping_without_one()
    {
        var images = new StubImageService();
        var generator = Generator(
            images,
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images),
            compositeEnabled: true);

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            generator.IllustrateAsync(
                Plan(), Photo(), "image/png", existingCover: null, onImage: null,
                cancellationToken: CancellationToken.None, existingSpreads: null,
                composite: Context()));

        Assert.Equal(CompositeFailureCodes.LayoutFailed, failure.FailureCode);

        // And it stopped before spending anything on the interior.
        Assert.Equal(0, images.ImageCalls);
    }

    // ---------------------------------------------------------------------------------------
    // Telemetry
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A composite page carries its per-attempt rows, because the fulfilment job's telemetry reads
    /// an empty list as "adopted from an earlier run and cost nothing".
    ///
    /// Without them, every composite book reported eight adoptions and zero image attempts — which
    /// is precisely the measurement the telemetry exists to take, inverted. An adopted page really
    /// does have no rows, and that is now the only thing that produces none.
    /// </summary>
    [Fact]
    public async Task Composite_pages_report_the_attempts_they_actually_cost()
    {
        var images = new StubImageService();

        // Spread one is refused once for a base fault, then passes; spread two passes first time.
        images.Verdicts.Enqueue(Fail("MAIN_SCENE_BEAT", CompositeQaVerdict.ActionRegenerateBase));

        var generator = Generator(
            images,
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images),
            compositeEnabled: true);

        var book = await generator.IllustrateAsync(
            Plan(), Photo(), "image/png",
            existingCover: BasePng(),
            onImage: null,
            cancellationToken: CancellationToken.None,
            existingSpreads: null,
            composite: Context());

        var first = book.Spreads[0];
        var second = book.Spreads[1];

        // Two cycles on spread one, and the refused one is kept: its verdict is the only record of
        // what was wrong with the picture that was thrown away.
        Assert.Equal(2, first.AttemptDetails.Count);
        Assert.False(first.AttemptDetails[0].Accepted);
        Assert.Contains("MAIN_SCENE_BEAT", first.AttemptDetails[0].Verdict);
        Assert.True(first.AttemptDetails[1].Accepted);
        Assert.Equal(2, first.Attempts);

        Assert.Single(second.AttemptDetails);
        Assert.True(second.AttemptDetails[0].Accepted);

        // No page reports itself as costing nothing.
        Assert.All(book.Spreads, spread => Assert.NotEmpty(spread.AttemptDetails));
    }

    /// <summary>
    /// A re-composite is a free second cycle, and the row says so: a zero generation time is the
    /// difference between "the retry was arithmetic" and "the retry was another image bill".
    /// </summary>
    [Fact]
    public async Task A_recomposite_records_a_cycle_that_generated_nothing()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("BEKI_INTEGRATION", CompositeQaVerdict.ActionRecompositeBeki));

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        var attempts = result.Spreads[0].Attempts;

        Assert.Equal(2, attempts.Count);
        Assert.True(attempts[0].GenerationMs >= 0);
        Assert.Equal(0, attempts[1].GenerationMs);
        Assert.True(attempts[1].Accepted);
    }

    /// <summary>
    /// The page's base image leaves the pipeline with its receipt, because a resumed run cannot
    /// reconstruct it and the composited page cannot stand in for it.
    /// </summary>
    [Fact]
    public async Task Every_composited_page_carries_its_base_image_out_for_storage()
    {
        var images = new StubImageService();
        var generator = Generator(
            images,
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images),
            compositeEnabled: true);

        var book = await generator.IllustrateAsync(
            Plan(), Photo(), "image/png", BasePng(), null, CancellationToken.None,
            existingSpreads: null, composite: Context());

        Assert.All(book.Composite!.Spreads, artifact => Assert.NotEmpty(artifact.BasePng));

        // And the base is not the page: one has Beki on it and the other is what the model drew.
        foreach (var spread in book.Spreads)
        {
            Assert.NotEqual(spread.Composition!.BasePng, spread.Image);
        }
    }

    // ---------------------------------------------------------------------------------------
    // The input boundary
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A truncated photograph is refused before anything is paid for.
    ///
    /// The header of a JPEG survives a dropped connection and reports a perfectly good width and
    /// height, so a check that read only the header passed the file and failed thousands of tokens
    /// later, inside an image call, after a story had been written and billed. Reading the pixels is
    /// the only version of "readable" that means anything.
    /// </summary>
    [Fact]
    public void A_truncated_photograph_is_refused_by_the_boundary()
    {
        Assert.Empty(InputNormalization.PhotoProblems(Jpeg(640, 480)));

        var truncated = TruncatedJpeg(640, 480);

        // The trap, stated as an assertion rather than as a claim in a comment: the header of this
        // file is entirely intact and reports the right dimensions, so a check that read only the
        // header — which is what Identify does — accepts it and the book proceeds.
        var identified = Image.Identify(truncated);
        Assert.Equal(640, identified.Width);
        Assert.Equal(480, identified.Height);

        // The pixels are not there, and reading them is what the boundary now does.
        var problems = InputNormalization.PhotoProblems(truncated);
        Assert.NotEmpty(problems);
        Assert.Contains("could not be decoded", problems[0]);
    }

    [Fact]
    public async Task A_truncated_photograph_stops_the_book_before_any_model_call()
    {
        var storyClient = new ScriptedStoryModelClient(ScenarioFixture());
        var images = new StubImageService();

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(storyClient, images).RunAsync(
                Request() with { ChildPhoto = TruncatedJpeg(640, 480) },
                CancellationToken.None));

        // Which is the whole point of checking here: nothing was written and nothing was drawn.
        Assert.Equal(CompositeFailureCodes.InvalidBookInput, failure.FailureCode);
        Assert.Equal(0, storyClient.Calls);
        Assert.Equal(0, images.ImageCalls);
    }

    // ---------------------------------------------------------------------------------------
    // The preview's planner
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// With the flag on, the preview's story is written by the composite planner — because the
    /// fulfilment job adopts that story rather than rewriting it, so this is the only moment the
    /// choice can be made.
    ///
    /// The failure it prevents is quiet and total: the composite branch in the illustrator always
    /// passes the previewed plan, so the composite planner was unreachable for every real book and
    /// every composite book was drawn from a story written by the prompt this path exists to avoid.
    ///
    /// The stored prompt version stays "v6" on purpose, and that is asserted too: it is the routing
    /// key BookFormat.IsPrintPlan reads to send the pack to the Beki fulfilment job, and a run
    /// stamped "composite-v1" would be routed to the legacy A5 generator instead.
    /// </summary>
    [Fact]
    public async Task With_the_flag_on_a_preview_is_written_by_the_composite_planner()
    {
        var story = new RecordingMasterStoryService();
        var runs = new RecordingRunRepository();

        await PreviewService(story, runs, compositeEnabled: true)
            .WriteBookAsync(runs.Run.Id, CancellationToken.None);

        Assert.Equal(1, story.CompositeCalls);
        Assert.Equal(0, story.LegacyCalls);

        // The four fields the composite planner may see, mapped from the preview's own input.
        Assert.Equal("3-5", story.LastCompositeInput!.AgeBand);
        Assert.Equal("girl", story.LastCompositeInput.Gender);
        Assert.Equal("dinosaurs", story.LastCompositeInput.ThemeId);

        // The routing key is untouched, so the pack still reaches the composite fulfilment job.
        Assert.Equal("v6", runs.SavedPromptVersion);
        Assert.True(BookFormat.IsPrintPlan(runs.SavedPromptVersion));

        // And the prompt actually stored is the composite one, which is the honest record of what
        // wrote the book.
        Assert.Contains("composite", runs.SavedSystemPrompt ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A preview whose portrait upload failed is written by the LEGACY planner, even with the flag
    /// on — because such a run can never take the composite route at all.
    ///
    /// The chain that made this a real harm: CreateAsync deliberately lets a preview continue when
    /// the upload fails, so the run arrives here with no PhotoBlobUrl; the composite plan carries
    /// no characterLock, because the composite pipeline reads the child's likeness out of the
    /// photograph; and at purchase BekiRunForAsync refuses the Beki route without a photo URL, so
    /// the book falls to the legacy generator — which then has no photograph, no appearance
    /// description and no character lock, and draws a child who is nobody in particular. The parent
    /// pays for it.
    /// </summary>
    [Fact]
    public async Task A_preview_whose_portrait_never_parked_keeps_the_legacy_planner()
    {
        var story = new RecordingMasterStoryService();
        var runs = new RecordingRunRepository();

        // The upload failed, so nothing was parked. Everything else about the run is unchanged.
        runs.Run.PhotoBlobUrl = null;

        await PreviewService(story, runs, compositeEnabled: true)
            .WriteBookAsync(runs.Run.Id, CancellationToken.None);

        Assert.Equal(0, story.CompositeCalls);
        Assert.Equal(1, story.LegacyCalls);

        // The legacy identity chain is what this run has left, and it is intact: the plan it wrote
        // carries a character lock for the illustrator to draw from.
        Assert.False(string.IsNullOrWhiteSpace(story.LastStory!.CharacterLock));
    }

    /// <summary>
    /// The composite planner also waits on the book-format switch, because that switch is what
    /// decides whether the purchase ever reaches the composite fulfilment job.
    ///
    /// With the pipeline flag on and the format switch off, BekiRunForAsync refuses the Beki route
    /// and the pack is drawn by the legacy A5 generator — so a composite-planned preview would be
    /// the book the parent reads and a legacy book would be the one they receive.
    /// </summary>
    [Fact]
    public async Task Without_the_book_format_switch_a_preview_keeps_the_legacy_planner()
    {
        var story = new RecordingMasterStoryService();
        var runs = new RecordingRunRepository();

        await PreviewService(story, runs, compositeEnabled: true, bookFormatEnabled: false)
            .WriteBookAsync(runs.Run.Id, CancellationToken.None);

        Assert.Equal(0, story.CompositeCalls);
        Assert.Equal(1, story.LegacyCalls);
    }

    /// <summary>
    /// A composite plan with the wrong number of spreads gets the corrective retry, rather than
    /// failing the preview outright.
    ///
    /// The count is the one rule the provider-safe request schema cannot state — strict mode
    /// rejects minItems and maxItems, so "exactly eight" survives only in a description — which
    /// makes a seven-spread answer both the likeliest fault and, until now, the one fault that
    /// skipped the retry built for exactly this.
    /// </summary>
    [Fact]
    public async Task A_composite_plan_with_too_few_spreads_is_corrected_rather_than_failed()
    {
        var story = new RecordingMasterStoryService { FirstPlanHasSevenSpreads = true };
        var runs = new RecordingRunRepository();

        await PreviewService(story, runs, compositeEnabled: true)
            .WriteBookAsync(runs.Run.Id, CancellationToken.None);

        Assert.Equal(2, story.CompositeCalls);
        Assert.Contains(story.LastCompositeProblems, problem => problem.Contains("spreads"));
        Assert.Equal(BookFormat.SpreadCount, story.LastStory!.Spreads.Count);
    }

    /// <summary>
    /// The story service itself hands a short plan back rather than throwing — which is the half of
    /// the fix the preview-level tests above cannot see, because they stub the service out.
    ///
    /// The count is the one rule the provider-safe request schema cannot state, so a seven-spread
    /// answer is a well-formed answer to the request that was made. Throwing here took that
    /// straight past the corrective retry the caller already owns for well-formed-and-wrong plans.
    /// </summary>
    [Fact]
    public async Task The_story_service_returns_a_short_composite_plan_for_its_caller_to_correct()
    {
        var client = new ScriptedStoryModelClient(CompositePlanJson(spreads: 7));
        var service = CompositeStoryService(client);

        var result = await service.WriteCompositePlanAsync(
            CompositeStoryInputFixture(), [], CancellationToken.None);

        // Returned, not thrown — and with the fault intact for the caller's validator to name.
        Assert.Equal(7, result.Story.Spreads.Count);
        Assert.Equal(1, client.Calls);

        // The composite plan carries no characterLock, and the read path supplies the empty string
        // rather than letting System.Text.Json refuse a perfectly correct answer.
        Assert.Equal(string.Empty, result.Story.CharacterLock);

        // BekiPlanValidator is what turns it into a problem the retry is sent.
        Assert.Contains(
            BekiPlanValidator.Validate(result.Story, BookFormat.SpreadCount),
            problem => problem.Contains("spreads"));
    }

    /// <summary>And the correction reaches the composite prompt, not v6's.</summary>
    [Fact]
    public async Task The_story_service_staples_corrections_onto_the_composite_prompt()
    {
        var client = new ScriptedStoryModelClient(CompositePlanJson(spreads: 8));
        var service = CompositeStoryService(client);

        await service.WriteCompositePlanAsync(
            CompositeStoryInputFixture(), ["Expected 8 spreads, got 7."], CancellationToken.None);

        Assert.Contains("Expected 8 spreads, got 7.", client.UserPrompts[0]);
        Assert.Contains("previous plan was rejected", client.UserPrompts[0]);

        // The composite system prompt, with its own Beki rule — not v6's.
        Assert.Contains($"every one of the {BookFormat.SpreadCount} spreads", client.SystemPrompts[0]);
    }

    [Fact]
    public async Task A_composite_plan_still_wrong_after_its_retry_fails_the_preview()
    {
        var story = new RecordingMasterStoryService { EverySpreadCountIsWrong = true };
        var runs = new RecordingRunRepository();

        await PreviewService(story, runs, compositeEnabled: true)
            .WriteBookAsync(runs.Run.Id, CancellationToken.None);

        // The preview fails rather than storing a seven-spread book; two attempts, no third.
        Assert.Equal(2, story.CompositeCalls);
        Assert.NotNull(runs.FailureMessage);
        Assert.Contains("still invalid after a retry", runs.FailureMessage!);
    }

    /// <summary>
    /// Beki on all eight spreads, because the illustration contract cannot describe a spread
    /// without her.
    ///
    /// The composite pipeline composites one approved pose per spread from a beki_action the
    /// scenario schema requires on every page, so the pictures carry her on all eight whatever the
    /// plan says. A plan listing her on five would ship a book whose stored cast list contradicts
    /// its own illustrations — an operator reads that the child is alone on spread four, and spread
    /// four has Beki in it.
    /// </summary>
    [Fact]
    public void A_composite_plan_that_leaves_Beki_off_a_spread_is_a_plan_problem()
    {
        var withBeki = Plan() with
        {
            Spreads = Plan().Spreads.Select(s => s with { Characters = ["child", "beki"] }).ToList()
        };

        Assert.Empty(CompositePlanRules.Problems(withBeki));

        var missing = withBeki with
        {
            Spreads = withBeki.Spreads
                .Select(s => s.Number == 4 ? s with { Characters = ["child"] } : s)
                .ToList()
        };

        var problems = CompositePlanRules.Problems(missing);
        Assert.Single(problems);
        Assert.Contains("Spread 4", problems[0]);
        Assert.Contains("beki", problems[0]);

        // The legacy validator is deliberately looser — Beki on the first, the last and three
        // others — and this stricter rule must not have become its rule.
        Assert.Empty(BekiPlanValidator.Validate(missing, BookFormat.SpreadCount)
            .Where(problem => problem.Contains("Spread 4")));
    }

    [Fact]
    public async Task A_composite_plan_missing_Beki_on_a_spread_gets_the_corrective_retry()
    {
        var story = new RecordingMasterStoryService { FirstPlanDropsBekiFromSpreadFour = true };
        var runs = new RecordingRunRepository();

        await PreviewService(story, runs, compositeEnabled: true)
            .WriteBookAsync(runs.Run.Id, CancellationToken.None);

        Assert.Equal(2, story.CompositeCalls);
        Assert.Contains(story.LastCompositeProblems, problem => problem.Contains("Spread 4"));
    }

    /// <summary>
    /// And the prompt asks for what the validator now requires — the two have to agree, or the
    /// retry is asking the model to satisfy a rule it was never given.
    /// </summary>
    [Fact]
    public void The_composite_prompt_asks_for_Beki_on_every_spread()
    {
        var system = MasterStoryPromptComposite.System(new CompositeStoryInput
        {
            ChildName = "ნინა",
            AgeBand = "3-5",
            Gender = "girl",
            ThemeId = "dinosaurs",
            Theme = AdventurePacks.Api.Domain.Enums.ThemeType.Dinosaurs,
        });

        // Matched on fragments that sit within one wrapped line of the prompt's raw string.
        Assert.Contains($"every one of the {BookFormat.SpreadCount} spreads", system);
        Assert.Contains("list exactly the id \"beki\" in every", system);
        Assert.Contains($"all {BookFormat.SpreadCount} spreads, without exception", system);

        // The v6 rule this replaced asked for the first, the last and three others.
        Assert.DoesNotContain("at least three other spreads", system);
    }

    [Fact]
    public async Task With_the_flag_off_a_preview_is_written_exactly_as_it_always_was()
    {
        var story = new RecordingMasterStoryService();
        var runs = new RecordingRunRepository();

        await PreviewService(story, runs, compositeEnabled: false)
            .WriteBookAsync(runs.Run.Id, CancellationToken.None);

        Assert.Equal(0, story.CompositeCalls);
        Assert.Equal(1, story.LegacyCalls);
        Assert.Equal("v6", runs.SavedPromptVersion);
    }

    /// <summary>
    /// A composite plan the validator objects to is corrected by the composite planner, never by
    /// v5/v6 — answering a composite plan's problems with a v6 plan would ship the English copy,
    /// the Extra Wish and the leaf spirit this path exists to keep out.
    /// </summary>
    [Fact]
    public async Task A_composite_plan_is_corrected_by_the_composite_planner()
    {
        var story = new RecordingMasterStoryService { FirstPlanIsInvalid = true };
        var runs = new RecordingRunRepository();

        await PreviewService(story, runs, compositeEnabled: true)
            .WriteBookAsync(runs.Run.Id, CancellationToken.None);

        Assert.Equal(2, story.CompositeCalls);
        Assert.Equal(0, story.LegacyRetryCalls);
        Assert.NotEmpty(story.LastCompositeProblems);
    }

    [Fact]
    public async Task An_input_the_boundary_refuses_stops_before_any_model_call()
    {
        var storyClient = new ScriptedStoryModelClient(ScenarioFixture());
        var images = new StubImageService();

        var context = new CompositeBookContext
        {
            JobId = Guid.NewGuid(),
            Input = new BookGenerationInput
            {
                ChildName = "ნინა",
                ChildAge = 5,
                ChildGender = "not_specified",
                ThemeId = "Dinosaurs",
                ChildPhotoRef = "books/nina/photo.jpg",
            }
        };

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(storyClient, images).RunAsync(
                Request(context), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.InvalidBookInput, failure.FailureCode);
        Assert.Equal(0, storyClient.Calls);
        Assert.Equal(0, images.ImageCalls);
    }

    // ---------------------------------------------------------------------------------------
    // The Gemini model slot (amendment B4)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A configured Visual Scenario model that silently did nothing is worse than no setting at
    /// all, and that is what the Gemini client used to do with every model argument.
    /// </summary>
    [Theory]
    [InlineData("gemini-3.1-pro", "gemini-3.1-pro")]
    [InlineData("gemini-2.5-flash", "gemini-2.5-flash")]
    [InlineData("", "gemini-under-test")]
    [InlineData("gpt-5.6-sol", "gemini-under-test")]
    [InlineData("GEMINI-CASED", "GEMINI-CASED")]
    public async Task An_explicitly_named_Gemini_model_reaches_the_request_and_nothing_else_does(
        string requested, string expected)
    {
        var handler = new CapturingHandler(TextResponse("{\"title\":\"ok\"}"));
        var options = new GeminiOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://gemini.test/v1beta",
            StoryModel = "gemini-under-test",
        };

        var client = new GeminiStoryModelClient(
            new GeminiInteractionsClient(
                new StubHttpClientFactory(handler),
                Options.Create(options),
                NullLogger<GeminiInteractionsClient>.Instance),
            Options.Create(options),
            Options.Create(new OpenAiOptions { LogPrompts = false }),
            NullLogger<GeminiStoryModelClient>.Instance);

        await client.CompleteAsync<TitleOnly>(
            requested, "s", "u", "plan",
            JsonDocument.Parse("{\"type\":\"object\"}").RootElement,
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(expected, body.RootElement.GetProperty("model").GetString());
    }

    // =======================================================================================
    // Harness
    // =======================================================================================

    private sealed record TitleOnly
    {
        public string Title { get; init; } = string.Empty;
    }

    private static string Section(string prompt, string from, string to)
    {
        var start = prompt.IndexOf(from, StringComparison.Ordinal) + from.Length;
        var end = prompt.IndexOf(to, start, StringComparison.Ordinal);
        return prompt[start..end];
    }

    /// <summary>The fixture with Beki named in a scene — the one fault that ruins a book.</summary>
    private static string WithBekiInSceneThree()
    {
        var scenario = JsonNode.Parse(ScenarioFixture())!;
        scenario["spreads"]![2]!["child_world_scene"] =
            "Beside Bafu, the child lifts the vine while Beki hovers close by.";
        return scenario.ToJsonString();
    }

    private static string Fail(string check, string action) =>
        $$"""
        {"status":"FAIL","failed_checks":["{{check}}"],"recommended_action":"{{action}}","notes":["a note"]}
        """;

    private static CompositeBookContext Context() => new()
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
    private static CompositeBookRequest Request(
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
    private static byte[] Photo() => Png(512, 512);

    /// <summary>
    /// What an image provider actually hands back: 3:2, and not the shape the book prints at.
    /// </summary>
    private const int ProviderWidth = 1536;
    private const int ProviderHeight = 1024;

    /// <summary>
    /// The same frame after a centred crop to the printed 15:7 spread — 1536 wide, so the height
    /// is 1536 ÷ (15/7) rounded, and roughly 30% of the provider's height is gone.
    /// </summary>
    private const int SpreadWidth = 1536;
    private const int SpreadHeight = 717;

    /// <summary>The shape the approved spread was composited on, so the geometry is comparable.</summary>
    private static byte[] BasePng() => Png(1836, 857);

    /// <summary>A picture already at the printed spread's ratio: normalization must not touch it.</summary>
    private static byte[] SpreadShapedPng() => Png(SpreadWidth, SpreadHeight);

    /// <param name="red">
    /// Paints the picture a distinguishable colour. Two blank images of the same size are the same
    /// bytes, which would make every "these are different pictures" assertion below vacuous.
    /// </param>
    private static byte[] Png(int width, int height, byte red = 0)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(red, 0, 0, 255));
        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// The fixture with a fourth recurring element: a shape the provider request permits — it has
    /// no maxItems — and the supplied contract forbids.
    /// </summary>
    private static string WithFourRecurringElements()
    {
        var scenario = JsonNode.Parse(ScenarioFixture())!;
        var elements = scenario["visual_lock"]!["recurring_elements"]!.AsArray();
        elements.Add("A fourth element, which the contract caps at three.");
        return scenario.ToJsonString();
    }

    /// <summary>The fixture with a different outfit lock — a scenario nothing would replan into.</summary>
    private static string WithOutfit(string outfit)
    {
        var scenario = JsonNode.Parse(ScenarioFixture())!;
        scenario["visual_lock"]!["child_outfit"] = outfit;
        return scenario.ToJsonString();
    }

    /// <summary>
    /// A plan in the shape the fulfilment job adopts: the Nina story's eight Georgian pages, taken
    /// from the same fixture the scenario was planned from.
    /// </summary>
    private static MasterStory Plan()
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

    private static CompositeBookPipeline Pipeline(
        IStoryModelClient storyClient, IOpenAiService images) =>
        new(storyClient,
            images,
            new StubMasterStoryService(),
            Options.Create(new BekiOptions { CompositePipelineEnabled = true }),
            Options.Create(new BekiPrintLayoutOptions()),
            NullLogger<CompositeBookPipeline>.Instance);

    private static BekiBookGenerator Generator(
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
    private sealed class SpyCompositePipeline : ICompositeBookPipeline
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
    }

    /// <summary>
    /// The text door, scripted. Hands back queued replies in order and records exactly what it was
    /// asked, because the retry's shape — the original prompt, whole, with the reasons appended —
    /// is as much under test as the answer.
    /// </summary>
    private sealed class ScriptedStoryModelClient(params string[] replies) : IStoryModelClient
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
    /// The image door, stubbed: a spread-shaped picture and a PASS verdict unless a test queues
    /// something else. It records the prompts and the number of references, which is how "never a
    /// Beki reference" is checked at the seam rather than in the builder.
    /// </summary>
    private sealed class StubImageService : IOpenAiService
    {
        public int ImageCalls { get; private set; }
        public int ReviewCalls { get; private set; }
        public List<string> Prompts { get; } = [];
        public List<int> ReferenceCounts { get; } = [];

        /// <summary>
        /// The third reference of each call, or null when there was none — which is how "the
        /// composite was never sent as a continuity reference" is checked at the seam rather than
        /// inferred from a comment.
        /// </summary>
        public List<byte[]?> ContinuityImages { get; } = [];

        /// <summary>Each call's answer, so a test can say which picture continuity kept.</summary>
        public List<byte[]> Returned { get; } = [];

        public Queue<string> Verdicts { get; } = new();

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

            // References are (child photo, theme, continuity) in that order, so the third is the
            // continuity image when there is one.
            ContinuityImages.Add(cast.Count >= 2 ? cast[1].Bytes : null);

            // The shape the providers actually return — 3:2, not the printed 15:7. Returning a
            // spread-shaped frame here would make the normalization step a no-op and every
            // geometry assertion below vacuous, which is exactly how the missing normalization
            // survived the first round of these tests.
            //
            // A distinct picture per call: identical bytes would make "continuity kept the most
            // recent one" unfalsifiable.
            var image = NextImage ?? Png(ProviderWidth, ProviderHeight, red: (byte)(10 + ImageCalls));
            Returned.Add(image);
            return Task.FromResult(image);
        }

        public Task<string> ReviewIllustrationAsync(
            byte[] imageBytes, string reviewPrompt,
            IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
            CancellationToken cancellationToken)
        {
            ReviewCalls++;
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
    private sealed class StubMasterStoryService : IMasterStoryService
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
    private static MasterStoryService CompositeStoryService(IStoryModelClient client) =>
        new(client,
            new StoryPolishClient(client, "stub-polish-model"),
            Options.Create(new OpenAiOptions { Model = "stub-story-model" }),
            Options.Create(new BekiOptions { CompositePipelineEnabled = true }),
            NullLogger<MasterStoryService>.Instance);

    private static CompositeStoryInput CompositeStoryInputFixture() => new()
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
    private static string CompositePlanJson(int spreads)
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

    /// <summary>A real JPEG, so truncating it truncates something a decoder actually walks.</summary>
    private static byte[] Jpeg(int width, int height)
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
    private static byte[] TruncatedJpeg(int width, int height)
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

    private static MasterBookService PreviewService(
        RecordingMasterStoryService story,
        RecordingRunRepository runs,
        bool compositeEnabled,
        bool bookFormatEnabled = true) =>
        new(runs,
            story,
            new StubImageService(),
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
    private sealed class RecordingMasterStoryService : IMasterStoryService
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
    private sealed class RecordingRunRepository : IMasterStoryRunRepository
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

    private sealed class StubBlobStorage : IBlobStorageService
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
    private sealed class NoOpIllustrationClient : IIllustrationClient
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

    private sealed class PassThroughNormalizer : IReferenceImageNormalizer
    {
        public NormalizedReferenceImage NormalizeForOpenAi(byte[] bytes, string? hintContentType = null) =>
            new(bytes, hintContentType ?? "image/png", "reference.png");

        public NormalizedReferenceImage NormalizeForStorageWebp(byte[] bytes, string? hintContentType = null) =>
            new(bytes, "image/webp", "illustration.webp");
    }

    private sealed class StubBackgroundJobClient : Hangfire.IBackgroundJobClient
    {
        public string Create(Hangfire.Common.Job job, Hangfire.States.IState state) => Guid.NewGuid().ToString();

        public bool ChangeState(string jobId, Hangfire.States.IState state, string? expectedState) => true;
    }

    /// <summary>The preview's cover path is not what these tests are about; it must simply not run.</summary>
    private sealed class SpyBekiBookGenerator : IBekiBookGenerator
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

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
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

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpResponseMessage TextResponse(string text)
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
