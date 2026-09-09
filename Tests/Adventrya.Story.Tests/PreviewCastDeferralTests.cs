using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Xunit;

namespace Adventrya.Story.Tests;

public class PreviewCastDeferralTests : CompositePipelineTestBase
{
    [Fact]
    public async Task Preview_plans_only_the_cover_from_opening_pages_without_cast_or_spread_work()
    {
        var client = new ScriptedStoryModelClient(ScenarioFixture());
        var images = new StubImageService();
        var preview = await Pipeline(client, images).PlanPreviewCoverAsync(Context(), Plan(), Photo(), CancellationToken.None);
        Assert.Equal(1, client.Calls);
        Assert.Equal(CompositePreviewCoverPlan.System, Assert.Single(client.SystemPrompts));
        using var input = JsonDocument.Parse(Assert.Single(client.UserPrompts));
        Assert.Equal(2, input.RootElement.GetProperty("opening_story_pages").GetArrayLength());
        Assert.False(input.RootElement.TryGetProperty("story_pages", out _));
        Assert.Empty(preview.Scenario.Spreads!);
        Assert.Empty(preview.Scenario.VisualLock!.RecurringElements!);
        Assert.NotNull(CompositePreviewCoverPlan.TryRead(preview.Json));
        Assert.False(VisualScenarioValidator.Validate(preview.Json).IsValid);
        Assert.Equal(0, images.ImageCalls);
        Assert.Equal(0, images.IdentityCalls);
        var schema = CompositePreviewCoverPlan.Schema().GetRawText();
        Assert.DoesNotContain("recurring_elements", schema);
        Assert.DoesNotContain("spreads", schema);
    }

    [Fact]
    public async Task Full_book_analyzes_cast_once_preserving_preview_cover_outfit_and_identity()
    {
        var original = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;
        var preview = CompositePreviewCoverPlan.Create(original with
        {
            VisualLock = original.VisualLock! with { ChildOutfit = "A simple blue romper with soft white shoes." },
        });
        var client = new ScriptedStoryModelClient(ScenarioFixture());
        var images = new StubImageService();
        string? persisted = null;
        var result = await Pipeline(client, images, insertBekiInGeneration: true).RunAsync(
            Request(resume: new CompositeResumeState(preview.Json, new Dictionary<int, byte[]>(), new Dictionary<int, byte[]>())
            { IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture) },
            onScenario: json =>
            {
                Assert.Equal(0, images.ImageCalls);
                persisted = json;
                return Task.CompletedTask;
            }), CancellationToken.None);
        Assert.Equal(1, client.Calls);
        Assert.Equal(CompositeVisualScenarioPrompt.SystemInstruction, client.SystemPrompts[0]);
        Assert.Equal(0, images.IdentityCalls);
        Assert.Equal(8, images.ImageCalls);
        Assert.Equal(8, result.Scenario.Spreads!.Count);
        Assert.NotEmpty(result.Scenario.VisualLock!.RecurringElements!);
        Assert.Equal(preview.Scenario.Cover, result.Scenario.Cover);
        Assert.Equal(preview.Scenario.VisualLock!.ChildOutfit, result.Scenario.VisualLock.ChildOutfit);
        Assert.True(CompositePreviewCoverPlan.MatchesFullScenario(preview.Json, persisted!));
        Assert.Null(CompositePreviewCoverPlan.TryRead(persisted));

        // A restart adopts the completed full-book analysis and fixed source images.
        var resumedImages = new StubImageService();
        var resumedClient = new ScriptedStoryModelClient();
        await Pipeline(resumedClient, resumedImages, insertBekiInGeneration: true).RunAsync(
            Request(resume: new CompositeResumeState(persisted,
                result.Spreads.ToDictionary(spread => spread.Page, spread => spread.CompositePng),
                result.Spreads.ToDictionary(spread => spread.Page, spread => spread.BasePng))
            { IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture) }), CancellationToken.None);
        Assert.Equal(0, resumedClient.Calls);
        Assert.Equal(0, resumedImages.ImageCalls);
    }

    [Fact]
    public async Task Fulfillment_reuses_a_cover_only_preview_without_buying_another_cover()
    {
        var world = new CompositePipelineFulfillmentTests.PackWorld();
        world.SeedPreviewCover();
        var preview = CompositePreviewCoverPlan.Create(VisualScenarioValidator.Validate(PreviewWrapFixture.ScenarioJson).Scenario!);
        world.Blobs.Seed(BekiRunBlobs.ScenarioName(world.RunId), Encoding.UTF8.GetBytes(preview.Json));
        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);
        Assert.Equal(0, world.Generator.WrapCalls);
        Assert.Equal(PreviewWrapFixture.BasePng, world.Generator.Context!.CoverAnchorBasePng);
        Assert.Equal(preview.Json, world.Generator.Resume!.ScenarioJson);
        Assert.Equal(PreviewWrapFixture.CompositePng,
            world.Blobs.Uploaded[BekiPackBlobs.CoverWrapCompositeName(world.UserId, world.PackId)]);
    }

    [Fact]
    public void Changed_cover_or_outfit_cannot_reuse_a_preview_cover()
    {
        var original = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;
        var preview = CompositePreviewCoverPlan.Create(original);
        var changed = original with { VisualLock = original.VisualLock! with { ChildOutfit = "A green coat and brown boots." } };
        Assert.False(CompositePreviewCoverPlan.MatchesFullScenario(preview.Json, JsonSerializer.Serialize(changed)));
        Assert.Null(CompositePreviewCoverPlan.TryRead("{}"));
    }
}
