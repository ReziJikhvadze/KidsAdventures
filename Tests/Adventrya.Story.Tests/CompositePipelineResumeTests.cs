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
/// Resuming against a stored identity spec and anchor, and the Visual Scenario’s one retry.
///
/// One of the classes CompositePipelineTestBase serves; see it for the fixtures these use.
/// </summary>
public class CompositePipelineResumeTests : CompositePipelineTestBase
{
    // ---------------------------------------------------------------------------------------
    // Resume: the identity spec and the anchor
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A resumed run adopts the stored spec rather than deriving a second one.
    ///
    /// The stored spec here says something the model would not have answered, which is the only way
    /// to tell "adopted" from "re-derived and happened to agree". The failure it prevents is the
    /// scenario's failure in another field: the four attributes go into every image prompt, so a
    /// second derivation gives the redrawn half of a book a different child from the adopted half.
    /// </summary>
    [Fact]
    public async Task A_resumed_run_draws_against_the_stored_identity_spec()
    {
        var images = new StubImageService();

        var stored = CompositeChildIdentity.ToStoredJson(IdentityFixture with
        {
            HairColor = "auburn",
            EyeColor = "grey-green",
        });

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images).RunAsync(
            Request(resume: new CompositeResumeState(
                ScenarioFixture(),
                new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() },
                new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() })
            {
                IdentitySpecJson = stored,
                AnchorBasePng = BasePng(),
            }),
            CancellationToken.None);

        // No identity call at all: this book's child was already read.
        Assert.Equal(0, images.IdentityCalls);
        Assert.All(images.Prompts, prompt => Assert.Contains("Hair colour: auburn", prompt));
        Assert.All(images.Prompts, prompt => Assert.Contains("Eye colour: grey-green", prompt));

        // And the run finished the book against it.
        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);
    }

    /// <summary>
    /// A spec stored by a different derivation prompt is not adopted. It is not corrupt — it is a
    /// good answer to a question this deployment no longer asks — and the honest response is to ask
    /// again rather than to draw seven spreads to last month's description of the child.
    /// </summary>
    [Fact]
    public void A_spec_from_another_derivation_version_is_not_adopted()
    {
        var current = CompositeChildIdentity.ToStoredJson(IdentityFixture);
        Assert.NotNull(CompositeChildIdentity.TryReadStored(current));

        var older = current.Replace(CompositeChildIdentity.Version, "child-identity-spec-v1.1");
        Assert.Null(CompositeChildIdentity.TryReadStored(older));

        Assert.Null(CompositeChildIdentity.TryReadStored(null));
        Assert.Null(CompositeChildIdentity.TryReadStored("not json"));
        Assert.Null(CompositeChildIdentity.TryReadStored("""{"hair_color":"dark brown"}"""));
    }

    /// <summary>
    /// A resumed run that adopts spread one adopts its base as the anchor, and draws the rest of
    /// the book against it without redrawing the page.
    /// </summary>
    [Fact]
    public async Task A_resumed_run_anchors_on_the_stored_first_spread()
    {
        var images = new StubImageService();
        var storedAnchor = Png(SpreadWidth, SpreadHeight, red: 77);

        await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images).RunAsync(
            Request(resume: new CompositeResumeState(
                ScenarioFixture(),
                new Dictionary<int, byte[]> { [1] = BasePng() },
                new Dictionary<int, byte[]> { [1] = storedAnchor })
            {
                IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
                AnchorBasePng = storedAnchor,
            }),
            CancellationToken.None);

        // Seven pages redrawn, spread one adopted, and every one of the seven anchored to the
        // stored base rather than to a fresh spread one.
        Assert.Equal(BookFormat.SpreadCount - 1, images.ImageCalls);
        Assert.All(images.AnchorImages, anchor => Assert.Equal(storedAnchor, anchor));
        Assert.All(
            images.Prompts,
            prompt => Assert.StartsWith("Image 1 - child appearance anchor", InputImages(prompt)));
    }

    /// <summary>
    /// A stored spread one whose base image is gone takes the whole book down with it: every
    /// adopted page is discarded and all eight are redrawn under one fresh anchor.
    ///
    /// Redrawing only spread one was the tempting repair and it was wrong. The stored pages were
    /// drawn against an anchor this attempt cannot see; a fresh spread one is a fresh stylization
    /// of the same child; so the pages redrawn would match the new anchor and the pages adopted
    /// would keep the old one — one book, two children, every page passing its own review. Eight
    /// images is what one book costs.
    /// </summary>
    [Fact]
    public async Task A_stored_book_whose_anchor_base_is_gone_is_redrawn_whole()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images).RunAsync(
            Request(resume: new CompositeResumeState(
                ScenarioFixture(),
                new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng(), [3] = BasePng() },
                // Spreads two and three kept their bases; spread one — the anchor — did not.
                new Dictionary<int, byte[]> { [2] = BasePng(), [3] = BasePng() })
            {
                IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
            }),
            CancellationToken.None);

        // Nothing adopted, everything redrawn.
        Assert.All(result.Spreads, spread => Assert.False(spread.Adopted));
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);

        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("child appearance anchor for the whole book"));

        // One anchor for the whole book, and it is this run's own spread one.
        Assert.Null(images.AnchorImages[0]);
        Assert.All(
            images.AnchorImages.Skip(1),
            anchor => Assert.Equal(result.Spreads[0].BasePng, anchor));

        // The scenario was kept rather than replanned: the outfit the book was sold with survives
        // a redraw of the artwork.
        Assert.All(images.Prompts, prompt =>
            Assert.Contains(result.Scenario.VisualLock!.ChildOutfit!, prompt));
    }

    /// <summary>
    /// The same rule when spread one was never stored at all but later pages were — a resume that
    /// would otherwise draw a fresh anchor and then adopt pages that predate it.
    /// </summary>
    [Fact]
    public async Task Stored_pages_without_a_stored_first_spread_are_redrawn_whole()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images).RunAsync(
            Request(resume: new CompositeResumeState(
                ScenarioFixture(),
                new Dictionary<int, byte[]> { [2] = BasePng(), [3] = BasePng() },
                new Dictionary<int, byte[]> { [2] = BasePng(), [3] = BasePng() })
            {
                IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
            }),
            CancellationToken.None);

        Assert.All(result.Spreads, spread => Assert.False(spread.Adopted));
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
    }

    // ---------------------------------------------------------------------------------------
    // Resume: adopted artwork requires an adoptable identity spec
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Stored pages with no usable identity spec are discarded and the book is redrawn whole.
    ///
    /// This is the counterpart of the anchor rule, and it closes the same hole from the other side:
    /// deriving a second spec while keeping pages drawn under the first describes the same child
    /// two ways — one set of pages with the hair the first derivation saw, one with the hair the
    /// second did. A second derivation is a second opinion about a photograph, not a recovery of
    /// the first, so there is no reading of a missing spec under which the two halves match.
    /// </summary>
    [Fact]
    public async Task Stored_pages_with_no_usable_identity_spec_are_redrawn_whole()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images).RunAsync(
            Request(resume: new CompositeResumeState(
                ScenarioFixture(),
                new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() },
                new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() })
            {
                // The blob is gone, so the job passed nothing — exactly what a failed download of
                // the stored spec leaves behind.
                IdentitySpecJson = null,
                AnchorBasePng = BasePng(),
            }),
            CancellationToken.None);

        // One derivation, and all eight pages drawn under it.
        Assert.Equal(1, images.IdentityCalls);
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.All(result.Spreads, spread => Assert.False(spread.Adopted));

        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("child identity spec is missing"));

        // And the stale anchor went with the artwork: every page after the first is matched to
        // this run's own spread one, not to the discarded book's.
        Assert.Null(images.AnchorImages[0]);
        Assert.All(
            images.AnchorImages.Skip(1),
            anchor => Assert.Equal(result.Spreads[0].BasePng, anchor));
    }

    /// <summary>
    /// The same when the stored spec was written by a derivation prompt this deployment no longer
    /// uses. It is not a corrupt file — it is a good answer to a question we stopped asking — and
    /// adopting pages drawn to it while redrawing the rest to a new one is the same split book.
    /// </summary>
    [Fact]
    public async Task Stored_pages_whose_spec_came_from_an_older_prompt_are_redrawn_whole()
    {
        var images = new StubImageService();

        var older = CompositeChildIdentity.ToStoredJson(IdentityFixture)
            .Replace(CompositeChildIdentity.Version, "child-identity-spec-v1.1");

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images).RunAsync(
            Request(resume: new CompositeResumeState(
                ScenarioFixture(),
                new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() },
                new Dictionary<int, byte[]> { [1] = BasePng(), [2] = BasePng() })
            {
                IdentitySpecJson = older,
                AnchorBasePng = BasePng(),
            }),
            CancellationToken.None);

        Assert.Equal(1, images.IdentityCalls);
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.All(result.Spreads, spread => Assert.False(spread.Adopted));
    }

    /// <summary>
    /// And the rule does not fire on a first attempt: nothing is stored, so nothing is discarded
    /// and no warning is raised about artwork that never existed.
    /// </summary>
    [Fact]
    public async Task A_first_attempt_derives_a_spec_without_discarding_anything()
    {
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(1, images.IdentityCalls);
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("discarded"));
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
}
