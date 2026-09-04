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
/// The wiring itself: the deterministic rhythm, the image prompt against its approved document,
/// the appearance anchor, and the flag that chooses between the two pipelines.
///
/// One of the classes CompositePipelineTestBase serves; see it for the fixtures these use.
/// </summary>
public class CompositePipelineTests : CompositePipelineTestBase
{
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
    // v1.1: the centre of the canvas is no longer described as a fold
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The whole of the seam fix, asserted as an absence.
    ///
    /// The first real books came back with a full-height dark band painted down the exact centre —
    /// 35× the baseline column-brightness step, in raw model output, with no stitching code
    /// anywhere — because the prompt named a fold four times while forbidding one once. A model
    /// told there is a fold there paints a fold. So the words are gone from every string this
    /// pipeline sends an image model, and this test is what keeps them gone: the geometry has to be
    /// re-stated in neutral words rather than quietly restored.
    /// </summary>
    [Fact]
    public void No_string_the_image_model_receives_says_there_is_a_fold()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        var sent = new List<string>
        {
            CompositeIllustrationPrompt.CompositionBlockFor("LEFT"),
            CompositeIllustrationPrompt.CompositionBlockFor("RIGHT"),
            CompositeIllustrationPrompt.CentralZoneRule,
            CompositeIllustrationPrompt.AnchorInstruction,
            SpreadPrompt(scenario, page: 1),
            SpreadPrompt(scenario, page: 2, anchorAttached: true),
        };

        foreach (var prompt in sent)
        {
            foreach (var word in (string[])["fold", "gutter", "seam", "binding", "bend"])
            {
                Assert.DoesNotContain(word, prompt, StringComparison.OrdinalIgnoreCase);
            }
        }

        // And the exclusion is still stated, in the words that replaced them — an absence on its
        // own would also be satisfied by deleting the rule. Since v1.5 the centre is not a named
        // zone at all: naming it gave the veil an edge to stop at, so the rule is now about the
        // content and the treatment of the middle, not about a place.
        var spread = SpreadPrompt(scenario, page: 1);
        Assert.Contains("The middle of the canvas is ordinary painting", spread);
        Assert.Contains("no edges, boundaries, or change of treatment of its own", spread);
        Assert.Contains("one continuous unbroken painting", spread);
        Assert.DoesNotContain("low-information", spread);

        // The deterministic shot instructions ride into the same prompts from
        // pipeline_config_v1.json, so the no-fold rule binds them too: the supplier config's
        // page-7 entry said "without crowding the fold" long after the templates were de-folded,
        // and page 7 shipped with an unrepaired seam in both measured runs.
        for (var page = 1; page <= 8; page++)
        {
            foreach (var word in (string[])["fold", "gutter", "seam", "binding", "bend"])
            {
                Assert.DoesNotContain(
                    word, CompositeSpreadRhythm.ShotFor(page), StringComparison.OrdinalIgnoreCase);
            }
        }

        // The v1 document said the opposite four times over. Read from the preserved fixture, so
        // this is a statement about what changed rather than about what we remember changing.
        var v1 = V1PromptFixture();
        Assert.Contains("center fold", v1);
        Assert.Contains("center-fold zone low-information", v1);
    }

    /// <summary>
    /// De-folding moved no geometry. The exclusion zone is the same strip, the integration zone is
    /// at the same two percentages, and the reserved third is the same third — because the numbers
    /// are what the compositor pastes Beki against, and a rewording that nudged them would put her
    /// over the scene on every page of every book.
    /// </summary>
    [Fact]
    public void De_folding_changed_the_wording_and_not_one_number()
    {
        var v1 = V1PromptFixture();
        var v11 = ResolvedPromptFixture();

        foreach (var geometry in (string[])
                 ["59.4% of the canvas width",
                  "45.8% of the canvas height", "final 15:7 crop",
                  "Keep all important content in the central horizontal band"])
        {
            Assert.Contains(geometry, v1);
            Assert.Contains(geometry, v11);
        }

        var config = BekiCompositeConfig.Load();
        Assert.Equal(0.594, config.StoryDefaultFor(BekiTextSide.Left).VisibleCenterX, 3);
        Assert.Equal(0.406, config.StoryDefaultFor(BekiTextSide.Right).VisibleCenterX, 3);
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

        var prompt = SpreadPrompt(scenario, page: 1);

        var approved = ResolvedPromptFixture();

        // The frame of the document, shared with the approved prompt word for word.
        Assert.StartsWith("Use case: illustration-story", prompt);
        foreach (var heading in (string[])
                 ["INPUT IMAGES", "SCENE", "CHILD LOCK", "CHILD IDENTITY LOCK",
                  "RECURRING ELEMENTS REQUIRED ON THIS IMAGE",
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
        Assert.Contains(scenario.VisualLock!.ChildOutfit!, prompt);

        // And the identity lock, which v1.1 added: the four attributes, and the sentence that keeps
        // the photograph the authority rather than these four phrases.
        //
        // Pinned as the whole block rather than line by line, because the block is interpolated
        // into a raw string literal and an indented rendering — every attribute preceded by twelve
        // spaces — would satisfy every Contains below while sending the model a prompt whose
        // sections no longer line up with the template's.
        Assert.Contains(
            "\nCHILD IDENTITY LOCK\nFace shape: round with a soft chin\nHair colour: dark brown\n"
            + "Hair style: shoulder-length wavy with a soft fringe\n"
            + "Eyebrows: soft, medium-thick, gently arched\nEye colour: brown\n"
            + "Skin tone: light warm\nGlasses: none\n"
            + "Distinctive features: light freckles across the nose; a dimple on the left cheek\n"
            + "The child is 1 years old in this book.", prompt);

        Assert.Contains("CHILD IDENTITY LOCK", approved);
        Assert.Contains("Hair colour: dark brown", prompt);
        Assert.Contains("Hair style: shoulder-length wavy with a soft fringe", prompt);
        Assert.Contains("Eye colour: brown", prompt);
        Assert.Contains("Skin tone: light warm", prompt);
        Assert.Contains(
            "Image 1 is the identity reference photograph and settles who this child is", prompt);
        Assert.Contains(
            "Image 1 is the identity reference photograph and settles who this child is", approved);

        // Deterministic, and from the config rather than from the model.
        Assert.Contains(CompositeSpreadRhythm.ShotFor(1), prompt);
        Assert.Contains(CompositeSpreadRhythm.ShotFor(1), approved);
        Assert.Contains("Keep the full left third quiet enough to set story text over", prompt);
        Assert.DoesNotContain("Keep the full right third", prompt);

        // Spread 1 is the page where the small dinosaur is deliberately unseen. A prompt carrying
        // his description would have drawn him, which is the whole reason relevance is computed.
        Assert.Contains("RECURRING ELEMENTS REQUIRED ON THIS IMAGE\nNone.", prompt);
        Assert.DoesNotContain("Bafu", prompt);

        // No third image is mentioned, because none is attached: spread 1 has no continuity
        // reference and is itself the page that produces the child appearance anchor.
        Assert.DoesNotContain("Image 3", prompt);
        Assert.DoesNotContain("Image 3", approved);

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

        // The panorama and the central exclusion the layout stage depends on. The wording has
        // been de-escalated twice against measured defects — v1 named a fold and the model
        // painted one; the v1.1 "central low-information zone" gave the veil a named edge to
        // stop at — so the live prompt now demands ordinary painting at the middle, while the
        // preserved fixture still carries the zone wording it was approved with.
        Assert.Contains("final 15:7 crop", prompt);
        Assert.Contains("The middle of the canvas is ordinary painting", prompt);
        Assert.Contains(
            "No face, hand, child, supporting character, or story-critical detail may sit at or "
            + "near the horizontal middle of the picture.", prompt);
        Assert.Contains(
            "Keep the narrow vertical strip at the exact centre of the canvas as a central "
            + "low-information zone", approved);
        Assert.DoesNotContain("center-fold zone low-information", prompt);
        Assert.DoesNotContain("low-information", prompt);
    }

    // ---------------------------------------------------------------------------------------
    // v1.1: the child appearance anchor, and the numbers the references are given
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The anchor leads on every spread but the first, and everything behind it is renumbered.
    ///
    /// The numbering is not presentation. The prompt names its inputs by position in the attached
    /// list, so an anchor written as Image 1 while the photograph is attached first tells the model
    /// to reproduce a photograph as though it were already a drawing — and to treat the drawing as
    /// the ground truth. Both shapes exist in every book, so both are pinned.
    ///
    /// v1.1 had the anchor third, behind the photograph and the world reference, and the books it
    /// produced still drifted on everything the owner listed. An image model weights the first
    /// reference hardest; the picture that already is the answer now holds that place.
    /// </summary>
    [Fact]
    public void The_anchor_leads_and_the_photograph_follows_it()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        // Spread 1: no anchor (it makes it), so the photograph leads exactly as it always did.
        var first = SpreadPrompt(scenario, page: 1);
        Assert.DoesNotContain("child appearance anchor", first);
        Assert.StartsWith("Image 1 - child identity reference photograph.", InputImages(first));
        Assert.Contains("Image 2 - approved", first);
        Assert.DoesNotContain("Image 3", first);

        // A later spread with the anchor and no continuity reference.
        var anchorOnly = SpreadPrompt(scenario, page: 2, anchorAttached: true);
        Assert.StartsWith("Image 1 - child appearance anchor", InputImages(anchorOnly));
        Assert.Contains("Image 2 - child identity reference photograph.", anchorOnly);
        Assert.Contains("Image 3 - approved", anchorOnly);
        Assert.DoesNotContain("Image 4", anchorOnly);

        // A later spread with both: the world reference and continuity each move back one.
        var both = SpreadPrompt(
            scenario, page: 3, anchorAttached: true, continuityElements: ["Bafu"]);
        Assert.StartsWith("Image 1 - child appearance anchor", InputImages(both));
        Assert.Contains("Image 2 - child identity reference photograph.", both);
        Assert.Contains("Image 3 - approved", both);
        Assert.Contains("Image 4 - continuity reference", both);

        // Continuity keeps its v1 clause under either number: it is not a picture of the child.
        Assert.Contains("Do not copy the child, Beki, pose, camera, layout, lighting, or "
                        + "background from this image.", both);

        // Spread 1 of a resumed book: no anchor, but a continuity reference — which is Image 3,
        // exactly as it was in v1.
        var continuityOnly = SpreadPrompt(scenario, page: 1, continuityElements: ["Bafu"]);
        Assert.Contains("Image 3 - continuity reference", continuityOnly);
        Assert.DoesNotContain("Image 4", continuityOnly);

        // The anchor asks for reproduction rather than resemblance, and names every attribute the
        // owner watched drift.
        Assert.Contains("Reproduce this exact rendered child", anchorOnly);
        foreach (var attribute in (string[])
                 ["same face and face shape", "same hair colour and style", "same eyebrows",
                  "same glasses or absence of glasses", "same eye colour", "same skin tone",
                  "same outfit down to its colours"])
        {
            Assert.Contains(attribute, anchorOnly);
        }

        Assert.Contains(
            "Give the child a new pose, camera angle and background as this page's scene requires.",
            anchorOnly);

        // And the photograph is still attached, still described as the ground truth.
        Assert.Contains("This photograph is the identity ground truth", anchorOnly);
    }

    /// <summary>
    /// The anchored shape, against the hand-resolved document for spread two — the same kind of
    /// evidence the first spread has, for the numbering case the first spread cannot show.
    /// </summary>
    [Fact]
    public void The_anchored_prompt_matches_the_approved_anchored_document()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        var prompt = SpreadPrompt(
            scenario,
            page: 2,
            anchorAttached: true,
            continuityElements: []);

        var approved = AnchoredPromptFixture();

        foreach (var line in (string[])
                 ["Image 1 - child appearance anchor",
                  "Image 2 - child identity reference photograph.",
                  "Image 3 - approved",
                  "Draw the outfit exactly as rendered in Image 1.",
                  "Image 2 is the identity reference photograph and settles who this child is",
                  "Glasses: none"])
        {
            Assert.Contains(line, prompt);
            Assert.Contains(line, approved);
        }

        // The outfit clause is the anchored spreads' alone: spread 1 has no Image 1 to point at.
        Assert.DoesNotContain(
            "Draw the outfit exactly as rendered in Image 1.", SpreadPrompt(scenario, page: 1));
    }

    /// <summary>
    /// The whole identity lock, on a page and in the approved document: eight attributes, and the
    /// glasses line present even when the answer is that there are none.
    ///
    /// "Glasses: none" is the line the owner's report is about at one end — a child who wears them
    /// losing them, or a child who does not gaining them, because nothing in the prompt ever
    /// mentioned glasses at all.
    /// </summary>
    [Fact]
    public void The_identity_lock_names_all_eight_attributes_including_absent_glasses()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;
        var prompt = SpreadPrompt(scenario, page: 1);
        var approved = ResolvedPromptFixture();

        foreach (var line in (string[])
                 ["Face shape: round with a soft chin",
                  "Hair colour: dark brown",
                  "Hair style: shoulder-length wavy with a soft fringe",
                  "Eyebrows: soft, medium-thick, gently arched",
                  "Eye colour: brown",
                  "Skin tone: light warm",
                  "Glasses: none",
                  "Distinctive features: light freckles across the nose; a dimple on the left cheek"])
        {
            Assert.Contains(line, prompt);
            Assert.Contains(line, approved);
        }

        // The eye colour is stated twice on purpose: once as an attribute and once as a rule about
        // every page, because it is the attribute that went wrong most often.
        Assert.Contains("The child's eyes are brown on every page.", prompt);

        // A child who does wear glasses gets them named the same way.
        var bespectacled = CompositeIllustrationPrompt.ForSpread(new CompositeSpreadPromptInput
        {
            Page = 1,
            ChildAge = 1,
            Theme = CompositeThemeReferences.For("dinosaurs"),
            ChildWorldScene = scenario.Spreads![0].ChildWorldScene!,
            ChildOutfit = scenario.VisualLock!.ChildOutfit!,
            IdentitySpec = IdentityFixture with { Glasses = "round thin gold frames" },
        });

        Assert.Contains("Glasses: round thin gold frames", bespectacled);
    }

    /// <summary>
    /// End to end: spread 1 is drawn with two references and no anchor, and every later spread
    /// carries the anchor — the same bytes, on all seven.
    /// </summary>
    [Fact]
    public async Task Every_spread_after_the_first_is_drawn_against_the_accepted_first_spread()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);

        // The page that makes the anchor cannot be shown one.
        Assert.Null(images.AnchorImages[0]);
        Assert.Equal(2, images.ReferenceCounts[0]);

        // Every later page is shown it, and it is the accepted base of spread one — not the
        // composited page, which has Beki pasted on it, and not the provider's uncropped frame.
        var anchor = result.Spreads[0].BasePng;

        for (var call = 1; call < images.ImageCalls; call++)
        {
            Assert.Equal(anchor, images.AnchorImages[call]);
            Assert.NotEqual(result.Spreads[0].CompositePng, images.AnchorImages[call]);
        }
    }

    /// <summary>
    /// The anchor is the base QA accepted, including when that is the second one the page was
    /// drawn — never the draft the reviewer refused.
    ///
    /// A refused base is precisely the picture the rest of the book must not be locked to: it was
    /// refused for something a reader would notice, and anchoring seven spreads to it would spread
    /// that fault across the book instead of containing it.
    /// </summary>
    [Fact]
    public async Task The_anchor_is_the_accepted_base_even_when_spread_one_was_redrawn()
    {
        var images = new StubImageService();
        images.Verdicts.Enqueue(Fail("MAIN_SCENE_BEAT", CompositeQaVerdict.ActionRegenerateBase));

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        // Nine image calls: spread one twice, then the seven that follow it.
        Assert.Equal(BookFormat.SpreadCount + 1, images.ImageCalls);
        Assert.Equal(2, result.Spreads[0].BaseAttempts);

        var refused = SpreadArtCrop.CropToRatio(images.Returned[0], 15f / 7f);
        var accepted = SpreadArtCrop.CropToRatio(images.Returned[1], 15f / 7f);

        Assert.Equal(accepted, result.Spreads[0].BasePng);
        Assert.NotEqual(refused, accepted);

        for (var call = 2; call < images.ImageCalls; call++)
        {
            Assert.Equal(accepted, images.AnchorImages[call]);
            Assert.NotEqual(refused, images.AnchorImages[call]);
        }
    }

    /// <summary>
    /// The reviewer is shown the anchor too, from spread two on — because the book that drifted
    /// passed all eight of its own reviews, each of which compared one page against a photograph
    /// with nothing to say about the other seven pages.
    /// </summary>
    [Fact]
    public async Task The_reviewer_is_shown_the_anchor_on_every_spread_after_the_first()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, images.ReviewCalls);

        // Spread one: the photograph alone, and no anchor sentence in the ask.
        Assert.Single(images.ReviewReferences[0]);
        Assert.DoesNotContain("Child appearance anchor", images.ReviewLabels[0]);
        Assert.DoesNotContain("Child appearance anchor:", images.ReviewPrompts[0]);

        for (var call = 1; call < images.ReviewCalls; call++)
        {
            Assert.Equal(2, images.ReviewReferences[call].Count);
            Assert.Contains("Child appearance anchor", images.ReviewLabels[call]);
            Assert.Equal(result.Spreads[0].BasePng, images.ReviewReferences[call][1].Bytes);
            Assert.Contains(
                "This page's child must be the same stylized child", images.ReviewPrompts[call]);
        }

        // And the criterion the reviewer is judging against names the four attributes.
        Assert.Contains(
            "or the child has materially different hair colour/style, eyebrows, face shape, skin "
            + "tone, or outfit details from the child appearance anchor", images.ReviewPrompts[1]);

        // The eye colour, named, on every page — the check the drifting books never had.
        Assert.All(
            images.ReviewPrompts,
            ask => Assert.Contains("The child's eyes must read as brown", ask));

        // And the glasses rule, stated in the direction this child needs it.
        Assert.All(
            images.ReviewPrompts,
            ask => Assert.Contains("This child wears no glasses", ask));

        // The spec itself reaches the reviewer, so "materially different eyebrows" is a comparison
        // it can actually make.
        Assert.All(
            images.ReviewPrompts,
            ask => Assert.Contains("Child identity spec for this book:", ask));
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

        // This characterization deliberately exercises the legacy, non-reference image door.
        var plan = Plan() with
        {
            Spreads = Plan().Spreads.Select(s => s with { Characters = ["child"] }).ToList(),
        };

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

        // Two references on page one — the photograph and the approved world — and never one
        // carrying Beki. Page two adds the child appearance anchor, and page three, which reuses
        // Bafu, adds the continuity reference behind it: four images, which is the template's own
        // limit and the last position this list has to give.
        Assert.Equal(2, images.ReferenceCounts[0]);
        Assert.Equal(3, images.ReferenceCounts[1]);
        Assert.Equal(4, images.ReferenceCounts[2]);
        Assert.DoesNotContain("Image 4", images.Prompts[1]);
        Assert.StartsWith("Image 1 - child appearance anchor", InputImages(images.Prompts[1]));
        Assert.Contains("Image 2 - child identity reference photograph.", images.Prompts[1]);
        Assert.Contains("Image 4 - continuity reference", images.Prompts[2]);
        Assert.All(images.ReferenceCounts, count => Assert.True(count <= 4, $"{count} references."));
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
}
