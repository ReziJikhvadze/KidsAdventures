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
/// The response schema the provider is sent, the references, telemetry, the input boundary, and
/// the preview's planner.
///
/// One of the classes CompositePipelineTestBase serves; see it for the fixtures these use.
/// </summary>
public class CompositePipelinePreviewTests : CompositePipelineTestBase
{
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

    /*
      A_composite_book_with_no_previewed_cover_stops_rather_than_shipping_without_one used to stand
      here, and it asserted the defect rather than the rule.

      It required the BOOK path to stop with LAYOUT_FAILED when there was no previewed cover to
      adopt — which is what it did, by opening with the reader-facing cover call above. But that
      cover is a stated failure on every run, and the composite book never ships it: its one cover
      master is the wrap, cut from the accepted anchor downstream in fulfilment. So the assertion
      described a paid book failing before its first spread over a picture nobody wanted, and the
      rule it should have been describing is the opposite one.

      The rule now lives in CompositeCoverWithoutPreviewTests: with or without a previewed cover,
      the book path draws eight spreads, asks for no reader-facing cover, and carries an empty (or
      adopted) cover slot with zero attempts. Without_cover_geometry_the_cover_stops_with_
      LAYOUT_FAILED above is untouched — a caller that asks for that cover directly still gets the
      refusal, which is the half of the old behaviour that was always right.
    */

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

        // Two model calls now: the writer, then R12b's editing pass. The stub answers the second
        // with "{}", which is not a book — so nothing is merged and the written plan comes back
        // untouched, fault and all, which is exactly the behaviour this test is about.
        Assert.Equal(2, client.Calls);
        Assert.Contains("You are an editor of Georgian children's books", client.SystemPrompts[1]);

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

    /// <summary>
    /// A plan that misspells the child's name gets the corrective retry, on the path where the
    /// observed defect of 2026-09-01 was first written.
    ///
    /// The preview is where the story is settled: the fulfilment job adopts it, so a name spelled
    /// wrongly here is a name spelled wrongly on the cover, in the pack row and in the PDF's
    /// metadata. The child is ნინა and the first plan titles the book „ნინოს დაკარგული ბილიკი“ —
    /// one Georgian letter, exactly the shape of ვეკო written ველო.
    /// </summary>
    [Fact]
    public async Task A_composite_plan_that_misspells_the_child_gets_the_corrective_retry()
    {
        var story = new RecordingMasterStoryService { FirstPlanMisspellsTheChild = true };
        var runs = new RecordingRunRepository();

        await PreviewService(story, runs, compositeEnabled: true)
            .WriteBookAsync(runs.Run.Id, CancellationToken.None);

        Assert.Equal(2, story.CompositeCalls);

        // The correction carries both words, because a planner told only that "the name is wrong"
        // has nothing to act on.
        Assert.Contains(story.LastCompositeProblems, problem =>
            problem.Contains("ნინო", StringComparison.Ordinal)
            && problem.Contains("ნინა", StringComparison.Ordinal));

        Assert.Null(runs.FailureMessage);
        Assert.Equal("ბაფუს დაკარგული ბილიკი", story.LastStory!.Concept.Title);
    }

    /// <summary>
    /// And a name still wrong after the retry fails the preview rather than storing a book that
    /// calls the child by a name that is not theirs. Blocker-only on this path: the preview has no
    /// pack, no blob container and no alarms table to waive into.
    /// </summary>
    [Fact]
    public async Task A_misspelled_name_still_wrong_after_its_retry_fails_the_preview()
    {
        var story = new RecordingMasterStoryService { EveryPlanMisspellsTheChild = true };
        var runs = new RecordingRunRepository();

        await PreviewService(story, runs, compositeEnabled: true)
            .WriteBookAsync(runs.Run.Id, CancellationToken.None);

        Assert.Equal(2, story.CompositeCalls);
        Assert.NotNull(runs.FailureMessage);
        Assert.Contains("still invalid after a retry", runs.FailureMessage!);
        Assert.Contains("ნინო", runs.FailureMessage!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The legacy printing planner is held to it too, and answers with its own retry. Nothing about
    /// a misspelled name is composite: every printing format takes the name as an input and prints
    /// it on the cover.
    /// </summary>
    [Fact]
    public async Task The_legacy_printing_planner_is_held_to_the_name_as_well()
    {
        var story = new RecordingMasterStoryService { FirstPlanMisspellsTheChild = true };
        var runs = new RecordingRunRepository();

        // No parked portrait, so this preview can never take the composite route and is written by
        // the v6 planner — the branch the composite ladder does not serve.
        runs.Run.PhotoBlobUrl = null;

        await PreviewService(story, runs, compositeEnabled: true)
            .WriteBookAsync(runs.Run.Id, CancellationToken.None);

        Assert.Equal(0, story.CompositeCalls);
        Assert.Equal(1, story.LegacyCalls);
        Assert.Equal(1, story.LegacyRetryCalls);
        Assert.Null(runs.FailureMessage);
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
}
