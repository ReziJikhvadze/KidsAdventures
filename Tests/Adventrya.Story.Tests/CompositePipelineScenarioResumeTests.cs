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
/// Resuming a run against a stored scenario: what may be adopted, what forces a replan, and what
/// the continuity references are after one.
///
/// One of the classes CompositePipelineTestBase serves; see it for the fixtures these use.
/// </summary>
public class CompositePipelineScenarioResumeTests : CompositePipelineTestBase
{
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
            new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() })
        {
            // Adopted artwork needs an adoptable identity spec, or the run discards it and redraws
            // the book — which is a different test's subject. This one is about the scenario.
            IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
        };

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
                new Dictionary<int, byte[]> { [1] = BasePng(), [2] = spreadTwoBase })
            {
                IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
            }),
            CancellationToken.None);

        // Spread three is the first page redrawn, and it reuses the creature spread two introduced.
        // With spread one adopted, its stored base is the anchor, so continuity is the fourth image.
        Assert.Contains("Image 4 - continuity reference", images.Prompts[0]);
        Assert.Equal(4, images.ReferenceCounts[0]);
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
                new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() })
            {
                IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
            }),
            CancellationToken.None);

        Assert.All(images.ContinuityImages, image => Assert.NotEqual(composited, image));
    }

    /// <summary>
    /// An adopted page whose base was never stored is a continuity gap, and the run says so instead
    /// of quietly drawing the rest of the book without it.
    ///
    /// A gap rather than a redraw, and the difference is which base is missing. Spread one's base
    /// is the anchor for the whole book, so losing it discards the artwork; a later page's base is
    /// only the continuity reference for the creature that page introduced, so losing it costs
    /// continuity on the pages that reuse the creature and nothing else.
    /// </summary>
    [Fact]
    public async Task An_adopted_page_with_no_stored_base_is_reported_as_a_continuity_gap()
    {
        var result = await Pipeline(
                new ScriptedStoryModelClient(ScenarioFixture()), new StubImageService())
            .RunAsync(
                Request(resume: new CompositeResumeState(
                    ScenarioFixture(),
                    new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() },
                    // Spread one kept its base — the anchor — and spread two did not.
                    new Dictionary<int, byte[]> { [1] = BasePng() })
                {
                    IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
                }),
                CancellationToken.None);

        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("adopted without its base image"));

        // And it stayed a gap: both pages are still adopted.
        Assert.True(result.Spreads[0].Adopted);
        Assert.True(result.Spreads[1].Adopted);
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
        Assert.Contains(CompositeChildIdentity.Version, terms);
    }

    /// <summary>
    /// A book half-drawn under v1 does not get finished under v1.1 — it is redrawn.
    ///
    /// Both amendments are reasons to: pages drawn from a prompt that described a fold have the
    /// band painted in, and pages drawn before the identity lock existed are of a child nothing
    /// pinned down. Mixing either with their v1.1 replacements produces one book of two kinds of
    /// page, each of which passed its own review. The prompt version in the contract is what turns
    /// that into a redraw, and this is the test that says so.
    /// </summary>
    [Fact]
    public void A_book_drawn_under_the_v1_prompts_is_redrawn_rather_than_finished()
    {
        var current = BekiCompositeContractTerms.Current("dinosaurs");

        Assert.Equal("child-world-image-v1.5", CompositeIllustrationPrompt.Version);
        Assert.Equal("minimal-visual-qa-v1.5", CompositeMinimalQa.Version);
        Assert.Equal("child-identity-spec-v1.2", CompositeChildIdentity.Version);

        // The two v1 shapes an in-flight book could have been written under.
        var underV1Image = current with { ImagePromptVersion = "child-world-image-v1.3" };
        var underNoIdentity = current with { IdentityPromptVersion = string.Empty };

        Assert.NotEqual(current.ToString(), underV1Image.ToString());
        Assert.NotEqual(current.ToString(), underNoIdentity.ToString());

        Assert.False(
            BekiFulfillmentManifest.CurrentContract(BookFormat.SpreadCount, underV1Image)
                .SequenceEqual(BekiFulfillmentManifest.CurrentContract(
                    BookFormat.SpreadCount, current)));

        // And the legacy path's own contract is still untouched by any of it, so no book in flight
        // on the previous pipeline is invalidated by this campaign.
        Assert.Equal(
            BookFormat.SpreadCount,
            BekiFulfillmentManifest.CurrentContract(BookFormat.SpreadCount).Count);
    }

    /// <summary>
    /// The identity spec is stored on the manifest as a URL and never as the attributes, and a
    /// legacy manifest is still byte-identical to the ones written before this campaign.
    /// </summary>
    [Fact]
    public void The_manifest_points_at_the_identity_spec_and_never_carries_it()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var composite = JsonSerializer.Serialize(
            new BekiFulfillmentManifest
            {
                IllustrationContract = ["a"],
                Entries = [new BekiFulfillmentManifestEntry(1, "https://blob/spread-01.png")],
                ScenarioUrl = "https://blob/visual-scenario.json",
                IdentitySpecUrl = "https://blob/child-identity.json",
            },
            options);

        Assert.Contains("identitySpecUrl", composite);
        Assert.Contains("child-identity.json", composite);
        Assert.DoesNotContain("hair", composite, StringComparison.OrdinalIgnoreCase);

        var legacy = JsonSerializer.Serialize(
            new BekiFulfillmentManifest
            {
                IllustrationContract = ["a"],
                Entries = [new BekiFulfillmentManifestEntry(1, "https://blob/spread-01.png")],
            },
            options);

        Assert.DoesNotContain("identitySpecUrl", legacy);
        Assert.DoesNotContain("scenarioUrl", legacy);
        Assert.DoesNotContain("compositions", legacy);
        Assert.DoesNotContain("reviewUrl", legacy);
    }

    /// <summary>
    /// The composite review reaches the pack's stored record: the document under the pack's own
    /// prefix, the URL on the manifest, the counts in telemetry — and it survives a resume that drew
    /// nothing.
    ///
    /// Three things are being pinned, and each one is a way the supplier's handback package or the
    /// admin would otherwise have to grep a log for what a completed book is actually like.
    ///
    /// That the review exists on a finished book at all, with its counts. That a fully-adopted
    /// resume still produces one — the count describes the book that ships, not the pages this
    /// attempt happened to draw, so a run that adopted all eight spreads has as much to record as
    /// the run that drew them. And that the telemetry projection carries no prose: the Georgian
    /// flags quote the story, and the story is where the child's name is.
    /// </summary>
    [Fact]
    public async Task The_review_lands_on_the_pack_record_and_survives_a_fully_adopted_resume()
    {
        var flagged = Plan() with
        {
            Spreads = Plan().Spreads
                .Select((spread, index) => index == 1
                    ? spread with { Text = "თემო-ს გაუხარდა და ბილიკი გამოჩნდა." }
                    : spread)
                .ToList(),
        };

        var repetitive = WithBekiActions(
            "Beki hovers quietly nearby.", "Beki hovers quietly nearby.",
            "Beki hovers quietly nearby.", "Beki hovers quietly nearby.",
            "Beki points toward the path.", "Beki claps for the child.",
            "Beki listens attentively.", "Beki welcomes the child.");

        var stored = Enumerable.Range(1, BookFormat.SpreadCount)
            .ToDictionary(page => page, _ => BasePng());

        var result = await Pipeline(new ScriptedStoryModelClient(), new StubImageService()).RunAsync(
            Request(resume: new CompositeResumeState(repetitive, stored, stored)
            {
                IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
                AnchorBasePng = BasePng(),
            }) with { ExistingPlan = flagged },
            CancellationToken.None);

        // Nothing was drawn — and there is still a review, because the review is about the book.
        Assert.Equal(0, result.SpreadsDrawnThisRun);
        Assert.NotNull(result.Artifacts.Review);
        Assert.NotNull(result.Artifacts.ReviewJson);
        Assert.Same(result.Review, result.Artifacts.Review);

        Assert.Equal(4, result.Review.PoseSelectionFallbacks);
        var flag = Assert.Single(result.Review.GeorgianFlags);
        Assert.Equal("hyphenated_name_suffix", flag.RuleId);
        Assert.Equal("spread 2", flag.Location);

        /*
          The document the fulfilment job stores under the pack's own prefix carries the prose,
          because it sits beside the story it quotes.

          Read as JSON rather than searched as text: the serializer escapes Georgian to \uXXXX, so a
          substring search for the name would fail here and — worse — would pass on the telemetry
          document below for the wrong reason, proving escaping rather than omission.
        */
        using var document = JsonDocument.Parse(result.Artifacts.ReviewJson!);
        var storedFlag = document.RootElement.GetProperty("georgian_flags")[0];

        Assert.Equal("თემო-ს", storedFlag.GetProperty("found").GetString());
        Assert.Contains("თემო-ს", storedFlag.GetProperty("excerpt").GetString()!);
        Assert.Equal(4, document.RootElement.GetProperty("pose_selection_fallback").GetInt32());

        // The blob it is stored as, and the manifest field that points at it — additive, and the
        // URL rather than the content.
        var packId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var reviewUrl = $"https://blob/{BekiPackBlobs.CompositeReviewName(userId, packId)}";

        Assert.EndsWith("/composite-review.json", BekiPackBlobs.CompositeReviewName(userId, packId));

        var manifest = JsonSerializer.Serialize(
            new BekiFulfillmentManifest
            {
                IllustrationContract = ["a"],
                Entries = [new BekiFulfillmentManifestEntry(1, "https://blob/spread-01.png")],
                ReviewUrl = reviewUrl,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("reviewUrl", manifest);
        Assert.Contains("composite-review.json", manifest);
        Assert.DoesNotContain("თემო", manifest);

        // A manifest written before this field existed still reads, and still resumes.
        var older = JsonSerializer.Deserialize<BekiFulfillmentManifest>(
            """{"illustrationContract":["a"],"entries":[{"spreadNumber":1,"storedUrl":"u"}]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(older);
        Assert.Null(older!.ReviewUrl);

        /*
          And the telemetry projection: the numbers, the URL, and not one word of the book.

          The Georgian flag's matched text is a window into the story, and the hyphenated-suffix
          rule finds the child's name with a suffix stuck on it — which is exactly what telemetry,
          the document read across packs, must not carry.
        */
        using var telemetry = JsonDocument.Parse(JsonSerializer.Serialize(
            result.Review.ToTelemetry(reviewUrl),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var measured = telemetry.RootElement;

        Assert.Equal(4, measured.GetProperty("poseSelectionFallback").GetInt32());
        Assert.Equal(1, measured.GetProperty("georgianFlagCount").GetInt32());
        Assert.Equal(reviewUrl, measured.GetProperty("reviewUrl").GetString());
        Assert.True(measured.GetProperty("needsHumanReading").GetBoolean());

        // Which rule, and which page to open. That is the whole of what a comparison document needs.
        var measuredFlag = measured.GetProperty("georgianFlags")[0];
        Assert.Equal("hyphenated_name_suffix", measuredFlag.GetProperty("ruleId").GetString());
        Assert.Equal("spread 2", measuredFlag.GetProperty("location").GetString());

        // And not the words. Asserted as absent properties rather than as an absent substring,
        // because the substring would also be absent if it were merely escaped.
        Assert.False(measuredFlag.TryGetProperty("found", out _));
        Assert.False(measuredFlag.TryGetProperty("excerpt", out _));

        Assert.DoesNotContain(
            Strings(measured), value => value.Contains("თემო", StringComparison.Ordinal));

        // Nothing about the child either, in either document — the review never carried the spec.
        foreach (var attribute in (string[])[IdentityFixture.HairColor, IdentityFixture.EyeColor])
        {
            Assert.DoesNotContain(
                Strings(measured), value => value.Contains(attribute, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                Strings(document.RootElement),
                value => value.Contains(attribute, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// A resumed run's review is the union of what this attempt saw and what the earlier attempt
    /// recorded about the pages this one adopted — not a reset.
    ///
    /// The defect: a resume rebuilds the review from what it can see, and a shot advisory is not
    /// something it can see. It belongs to a page's review, and an adopted page was reviewed by the
    /// attempt that drew it; the pose-vocabulary retry is the same shape of fact, because a resumed
    /// run adopts the scenario and never re-asks for one. The fulfilment job then overwrites
    /// composite-review.json with the rebuilt record, so an earlier attempt's observations became
    /// silence — on pages nobody was going to look at again.
    /// </summary>
    [Fact]
    public async Task A_resumed_review_keeps_what_the_earlier_attempt_recorded()
    {
        var storedReview = new CompositeBookReview
        {
            PoseRegistryVersion = "beki-pose-registry-v1",
            PoseKeywordRevision = "v1.1",
            ScenarioPromptVersion = CompositeVisualScenarioPrompt.Version,
            PoseSelectionFallbacks = 0,
            DistinctPoses = 8,
            PoseVocabularyRetrySpent = true,
            GeorgianChecklistVersion = CompositeGeorgianCheck.ChecklistVersion,
            ShotAdvisories =
            [
                new CompositeShotAdvisory(
                    3, CompositeSpreadRhythm.ShotFor(3), "A close-up where a wide view was asked for."),
                new CompositeShotAdvisory(
                    7, CompositeSpreadRhythm.ShotFor(7), "A wide view where a close one was asked for."),
            ],
        }.ToJson();

        // Seven pages adopted; spread 7 is the one this attempt redraws.
        var adopted = Enumerable.Range(1, BookFormat.SpreadCount)
            .Where(page => page != 7)
            .ToDictionary(page => page, _ => BasePng());

        var storyClient = new ScriptedStoryModelClient();

        var result = await Pipeline(storyClient, new StubImageService()).RunAsync(
            Request(resume: new CompositeResumeState(ScenarioFixture(), adopted, adopted)
            {
                IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
                AnchorBasePng = BasePng(),
                ReviewJson = storedReview,
            }),
            CancellationToken.None);

        Assert.Equal(1, result.SpreadsDrawnThisRun);

        // The adopted page's advisory survives: this attempt never looked at spread 3.
        var kept = Assert.Single(result.Review.ShotAdvisories, a => a.Page == 3);
        Assert.Contains("close-up", kept.ReviewerNote);

        // Spread 7 was redrawn and reviewed here, and this review found nothing — so the stale note
        // about the picture it replaced is gone. A note about an image nobody will ever see is
        // worse than no note.
        Assert.DoesNotContain(result.Review.ShotAdvisories, a => a.Page == 7);

        // And the retry the earlier attempt spent is still on the book's record, though this run
        // adopted the scenario and never asked for one — which is precisely why it could not have
        // known without being told.
        Assert.True(result.Review.PoseVocabularyRetrySpent);
        Assert.Equal(0, storyClient.Calls);

        // The document the fulfilment job overwrites is the merged one.
        using var document = JsonDocument.Parse(result.Artifacts.ReviewJson!);
        Assert.True(document.RootElement.GetProperty("pose_vocabulary_retry_spent").GetBoolean());
        Assert.Equal(1, document.RootElement.GetProperty("shot_advisories").GetArrayLength());
    }

    /// <summary>
    /// A stored review this build cannot read is nothing to merge, and nothing more: the book is
    /// unaffected.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    public async Task An_unreadable_stored_review_costs_the_book_nothing(string? stored)
    {
        var adopted = Enumerable.Range(1, BookFormat.SpreadCount)
            .ToDictionary(page => page, _ => BasePng());

        var result = await Pipeline(new ScriptedStoryModelClient(), new StubImageService()).RunAsync(
            Request(resume: new CompositeResumeState(ScenarioFixture(), adopted, adopted)
            {
                IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
                AnchorBasePng = BasePng(),
                ReviewJson = stored,
            }),
            CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);
        Assert.Empty(result.Review.ShotAdvisories);
        Assert.False(result.Review.PoseVocabularyRetrySpent);
    }

    /// <summary>
    /// The keyword revision is a term of the resume contract, so a keyword amendment redraws a
    /// half-drawn book rather than finishing it under a second table.
    ///
    /// The choice, stated: pin it. A keyword revision deliberately leaves `registry_version`
    /// untouched — no pixel, hash, priority order or forced pose moves, and pipeline_config_v1.json
    /// pins that string — so the version alone cannot see the change. What the revision does change
    /// is which approved pose a sentence selects: "Beki claps happily" was the neutral hover under
    /// v1.0 and is the celebrate pose under v1.1. A resume that adopted pages composited under the
    /// old table while compositing the rest under the new one binds one book from two readings of
    /// one scenario, every page individually correct.
    ///
    /// The alternative — recovering each adopted page's pose from its stored composition manifest
    /// and auditing from those — was rejected: it is more code for a worse answer, since it lets the
    /// mixed book ship and merely describes it accurately.
    /// </summary>
    [Fact]
    public void A_keyword_revision_change_redraws_rather_than_mixing_two_pose_tables()
    {
        var current = BekiCompositeContractTerms.Current("dinosaurs");

        // The installed table, on the contract.
        Assert.Equal(BekiPoseRegistry.Load().KeywordRevision, current.PoseKeywordRevision);
        Assert.Contains(current.PoseKeywordRevision, current.ToString());

        // A book half-composited under the pack as delivered is not finished under the amendment.
        var underV10Keywords = current with { PoseKeywordRevision = "v1.0" };

        Assert.NotEqual(current.ToString(), underV10Keywords.ToString());
        Assert.False(
            BekiFulfillmentManifest.CurrentContract(BookFormat.SpreadCount, underV10Keywords)
                .SequenceEqual(BekiFulfillmentManifest.CurrentContract(
                    BookFormat.SpreadCount, current)));

        // And the pack revision genuinely does not move with it, which is why the term is needed:
        // without it these two contracts would be identical.
        Assert.Equal(current.PoseRegistryVersion, underV10Keywords.PoseRegistryVersion);

        // The legacy path's contract is still untouched by any of it.
        Assert.Equal(
            BookFormat.SpreadCount,
            BekiFulfillmentManifest.CurrentContract(BookFormat.SpreadCount).Count);
    }

    /// <summary>
    /// A layout failure keeps its agreed code in the reason stored against the pack and sent to the
    /// admin — it used to fall through to the bare message.
    ///
    /// The code has to come first because of who reads the string: support sees it on the pack and
    /// the admin notification carries it, and every other failure on this path opens with a code
    /// somebody can look up. "The approved endpaper pattern is not in the published output." is a
    /// fine second half and a useless first one.
    /// </summary>
    [Theory]
    [InlineData(CompositeFailureCodes.TextOverflow, "Spread 4's copy does not fit at any permitted size.")]
    [InlineData(CompositeFailureCodes.LayoutFailed, "The approved endpaper pattern is not in the published output.")]
    public void A_layout_failure_keeps_its_code_in_the_stored_reason(string code, string message)
    {
        var reason = BekiPackFulfillment.CodedFailureReason(new BekiLayoutException(code, message));

        Assert.NotNull(reason);
        Assert.StartsWith(code, reason!, StringComparison.Ordinal);
        Assert.Equal($"{code}: {message}", reason);
    }

    /// <summary>
    /// The pipeline's own failures are formatted exactly as they were, page included — the layout
    /// case was added beside them and did not move them.
    /// </summary>
    [Fact]
    public void The_pipeline_failure_reasons_are_unchanged_and_still_name_the_page()
    {
        var page = BekiPackFulfillment.CodedFailureReason(
            new CompositePipelineException(CompositeFailureCodes.ImageQaFailed, "Spread 7 was refused.")
            {
                Page = 7,
            });

        Assert.Equal($"{CompositeFailureCodes.ImageQaFailed} (spread 7): Spread 7 was refused.", page);

        var book = BekiPackFulfillment.CodedFailureReason(
            new CompositePipelineException(CompositeFailureCodes.StoryFailed, "The story call failed."));

        Assert.Equal($"{CompositeFailureCodes.StoryFailed}: The story call failed.", book);

        // Everything else still falls back to the bare message, exactly as it always did.
        Assert.Null(BekiPackFulfillment.CodedFailureReason(new InvalidOperationException("plain")));
    }
}
