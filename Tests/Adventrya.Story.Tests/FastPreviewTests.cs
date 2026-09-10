using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Adventrya.Story.Tests;

public sealed class FastPreviewTests(ITestOutputHelper output) : CompositePipelineTestBase
{
    [Fact]
    public void Preview_version_fits_the_persisted_sql_column()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "KidsAdventuresAPI", "Data", "Scripts")))
            root = root.Parent;
        Assert.NotNull(root);
        var schema = File.ReadAllText(Path.Combine(root.FullName, "KidsAdventuresAPI", "Data", "Scripts",
            "030_MasterStoryRunPromptVersion.sql"));
        var column = System.Text.RegularExpressions.Regex.Match(schema, @"PromptVersion\s+NVARCHAR\((\d+)\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.True(column.Success, "The persisted prompt version column must be checked.");
        Assert.InRange(FastPreviewPlan.Version.Length, 1, int.Parse(column.Groups[1].Value));
    }

    [Theory]
    [InlineData("clouds", "ღრუბლების ქალაქი")]
    [InlineData("space", "ვარსკვლავების გზა")]
    [InlineData("forest", "მოჯადოებული ტყის მეგობრები")]
    [InlineData("ocean", "მბრწყინავი კუნძულის საიდუმლო")]
    [InlineData("magic", "სინათლის ქალაქის კარიბჭე")]
    [InlineData("dinosaurs", "დინოზავრების ხეობა")]
    public void All_six_themes_have_fixed_titles_and_cover_only_plans(string theme, string title)
    {
        Assert.Equal($"ნინა და {title}", FastPreviewPlan.Title("ნინა", theme));
        var plan = FastPreviewPlan.Plan(theme);
        Assert.NotNull(CompositePreviewCoverPlan.TryRead(plan.Json));
        Assert.Empty(plan.Scenario.Spreads!);
        Assert.Empty(plan.Scenario.VisualLock!.RecurringElements!);
        Assert.Contains("Do not draw Beki", FastPreviewPlan.Prompt(5, "girl", theme));
        Assert.NotEmpty(CompositeThemeReferences.For(theme).Bytes);
    }

    [Fact]
    public async Task Preview_pipeline_buys_one_image_and_no_identity_story_or_planner_calls()
    {
        var text = new ScriptedStoryModelClient();
        var images = new StubImageService { NextImage = Png(1200, 576) };
        var wrap = await Pipeline(text, images).DrawFastPreviewCoverAsync(Context(), Photo(), CancellationToken.None);
        Assert.Equal(1, images.ImageCalls);
        Assert.Equal(0, images.IdentityCalls);
        Assert.Equal(0, text.Calls);
        Assert.NotEmpty(wrap.CompositePng);
        Assert.Contains(FastPreviewPlan.Version, wrap.GenerationReceiptJson);
    }

    [Fact]
    public async Task Preview_job_does_not_write_story_and_paid_job_does_not_draw_cover()
    {
        var runs = new Runs();
        var run = runs.Add();
        var story = new RecordingMasterStoryService();
        var images = new StubImageService { NextImage = Png(1200, 576) };
        var blobs = new RecordingPreviewBlobs { Photo = Photo() };
        var fake = new FastStub(run);
        var master = new MasterBookService(runs, story, images, blobs, new PassThroughNormalizer(),
            new StubBackgroundJobClient(), new SpyBekiBookGenerator(),
            Options.Create(new BekiOptions { BookFormatEnabled = true, CompositePipelineEnabled = true }),
            NullLogger<MasterBookService>.Instance, fastPreview: fake);
        await master.WriteBookAsync(run.Id, CancellationToken.None);
        Assert.Equal(1, fake.Calls);
        Assert.Null(run.StoryJson);
        Assert.Equal(0, story.CompositeCalls);
        Assert.Equal(0, story.LegacyCalls);
        Assert.Equal(0, images.ImageCalls);
        // An unclaimed preview must never be usable as the paid generation entry point.
        await Assert.ThrowsAsync<InvalidOperationException>(() => master.EnsurePaidStoryAsync(run.Id, CancellationToken.None));
        run.UserId = Guid.NewGuid(); run.PackId = Guid.NewGuid();
        await master.EnsurePaidStoryAsync(run.Id, CancellationToken.None);
        Assert.NotNull(run.StoryJson);
        Assert.Equal(FastPreviewPlan.Version, run.PromptVersion);
        Assert.Equal(fake.Revision.Title, JsonSerializer.Deserialize<MasterStory>(run.StoryJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Concept.Title);
        Assert.Equal(0, images.ImageCalls);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task Real_local_composition_is_immutable_reusable_and_checkout_pins_the_revision()
    {
        var runs = new Runs();
        var run = runs.Add();
        var images = new StubImageService { NextImage = Png(1200, 576) };
        var text = new ScriptedStoryModelClient();
        var blobs = new RecordingPreviewBlobs { Photo = Photo() };
        var composer = new BekiPdfComposer(Options.Create(new BekiPrintLayoutOptions()));
        var preview = new FastPreviewService(Pipeline(text, images), composer, blobs, runs,
            new PassThroughNormalizer(), Options.Create(new BekiOptions()), NullLogger<FastPreviewService>.Instance);
        await preview.GenerateAsync(run, CancellationToken.None);
        var original = await preview.ReadAsync(run.Id, CancellationToken.None);
        Assert.Equal(1, images.ImageCalls);
        Assert.Equal(0, images.IdentityCalls);
        Assert.Equal(0, text.Calls);
        Assert.Null(run.StoryJson);
        Assert.Equal(MasterStoryRunStatus.Ready, run.Status);
        await preview.GenerateAsync(run, CancellationToken.None);
        Assert.Equal(original, await preview.ReadAsync(run.Id, CancellationToken.None));
        Assert.Equal(1, images.ImageCalls);

        var userId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => preview.ValidatePurchaseAsync(userId,
            run.Id, Guid.NewGuid(), "dinosaurs", run.ChildName, CancellationToken.None));
        await preview.ValidatePurchaseAsync(userId, run.Id, original.CoverRevisionId, "dinosaurs", run.ChildName, CancellationToken.None);
        Assert.Equal(userId, run.UserId);
        Assert.Null(run.ExpiresAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => preview.ValidatePurchaseAsync(Guid.NewGuid(),
            run.Id, original.CoverRevisionId, "dinosaurs", run.ChildName, CancellationToken.None));

        var renamed = runs.Add("ზუკა");
        renamed.UserPrompt = run.Id.ToString();
        await preview.GenerateAsync(renamed, CancellationToken.None);
        var second = await preview.ReadAsync(renamed.Id, CancellationToken.None);
        Assert.Equal(original.MasterSha256, second.MasterSha256);
        Assert.Equal(original.BaseSha256, second.BaseSha256);
        Assert.NotEqual(original.CoverRevisionId, second.CoverRevisionId);
        Assert.NotEqual(original.FrontSha256, second.FrontSha256);
        Assert.Equal(1, images.ImageCalls);
        Assert.Equal(original, await preview.ReadAsync(run.Id, CancellationToken.None));

        // A different renderer instance adopts the stored title position and typography.
        var paidComposer = new BekiPdfComposer(Options.Create(new BekiPrintLayoutOptions()));
        var wrap = blobs.Uploaded[BekiRunBlobs.CoverWrapCompositeName(run.Id)];
        paidComposer.RestorePreviewLayout(original.Title, wrap, original.LayoutJson);
        var paidCover = paidComposer.ComposeCoverPressWithReceipts(original.Title, wrap);
        var previewCover = composer.ComposeCoverPressWithReceipts(original.Title, wrap);
        Assert.Equal(JsonSerializer.Serialize(previewCover.Receipts), JsonSerializer.Serialize(paidCover.Receipts));
        Assert.Equal(1, images.ImageCalls);
        var bookPlan = BekiLayoutFixture.EightSpreadPlan();
        bookPlan = bookPlan with { Concept = bookPlan.Concept with { Title = original.Title } };
        var spreads = bookPlan.Spreads.Select(page => new BekiSpreadArtwork(page.Number, Png(1200, 560))).ToList();
        var reading = paidComposer.ComposeReading(bookPlan, wrap, spreads,
            BekiLayoutFixture.Personalization() with { ChildName = run.ChildName, Age = run.Age, PrepareForPrint = false });
        Assert.NotEmpty(reading.Pdf);
        Assert.Equal(1, images.ImageCalls);

        var dir = Path.Combine(Path.GetTempPath(), "beki-fast-preview-proof");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "cover.png"), blobs.Uploaded[BekiRunBlobs.CoverName(run.Id)]);
        File.WriteAllBytes(Path.Combine(dir, "intro.png"), blobs.Uploaded[FastPreviewPlan.IntroName(run.Id)]);
        File.WriteAllBytes(Path.Combine(dir, "cover.pdf"), paidCover.Pdf);
        File.WriteAllBytes(Path.Combine(dir, "reading.pdf"), reading.Pdf);
        output.WriteLine("Stubbed preview: 1 image call, 0 identity calls, 0 text calls; rename and resume: 0 additional calls.");
        output.WriteLine("Preview-to-checkout: revision validated and cover composition receipts unchanged. Proof: " + dir);
        blobs.Uploaded[BekiRunBlobs.CoverWrapCompositeName(run.Id)] = [1, 2, 3];
        await Assert.ThrowsAnyAsync<Exception>(() => preview.ValidatePurchaseAsync(userId,
            run.Id, original.CoverRevisionId, "dinosaurs", run.ChildName, CancellationToken.None));
        Assert.Equal(1, images.ImageCalls);
    }

    [Fact]
    public async Task Paid_fulfillment_adopts_fast_preview_without_identity_or_cover_regeneration()
    {
        var world = new CompositePipelineFulfillmentTests.PackWorld();
        world.SeedPreviewCover();
        var runs = new Runs(); var run = runs.Add();
        runs.Items.Remove(run.Id); run.Id = world.RunId; runs.Items.Add(run.Id, run);
        run.UserId = world.UserId; run.PackId = world.PackId;
        run.StoryJson = JsonSerializer.Serialize(Plan(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var service = new FastStub(run)
        {
            Revision = new FastPreviewRevision(run.Id, Guid.NewGuid(), "dinosaurs", Plan().Concept.Title,
                "", FastPreviewPlan.Sha(PreviewWrapFixture.CompositePng), FastPreviewPlan.Sha(PreviewWrapFixture.BasePng),
                FastPreviewPlan.Version, "", FastPreviewPlan.Model,
                CompositePreviewCoverPlan.Create(VisualScenarioValidator.Validate(PreviewWrapFixture.ScenarioJson).Scenario!).Json,
                "frozen-layout", "", "", "")
        };
        world.Orders.CoverRevisionId = service.Revision.CoverRevisionId;
        await world.Job(runs: runs, fastPreview: service).ProcessAsync(world.PackId, world.RunId, CancellationToken.None);
        Assert.Equal(0, world.Generator.WrapCalls);
        Assert.Equal(PreviewWrapFixture.BasePng, world.Generator.Context!.CoverAnchorBasePng);
        Assert.Equal("frozen-layout", world.Composer.RestoredLayout);
        Assert.Equal(PreviewWrapFixture.CompositePng,
            world.Blobs.Uploaded[BekiPackBlobs.CoverWrapCompositeName(world.UserId, world.PackId)]);
    }

    [Fact]
    public async Task A_square_image_is_rejected_without_cropping_the_child_or_retrying()
    {
        var images = new StubImageService { NextImage = Png(1024, 1024) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Pipeline(new ScriptedStoryModelClient(), images)
            .DrawFastPreviewCoverAsync(Context(), Photo(), CancellationToken.None));
        Assert.Equal(1, images.ImageCalls);
    }

    [Fact]
    public async Task An_uncertain_image_attempt_never_rebuys_the_cover_on_job_restart()
    {
        var runs = new Runs(); var run = runs.Add();
        var images = new StubImageService { NextImage = Png(1200, 576) };
        var blobs = new RecordingPreviewBlobs { Photo = Photo() };
        blobs.Uploaded[$"master-runs/{run.Id:N}/cover-attempt.json"] = Encoding.UTF8.GetBytes("{}");
        var preview = new FastPreviewService(Pipeline(new ScriptedStoryModelClient(), images),
            new BekiPdfComposer(Options.Create(new BekiPrintLayoutOptions())), blobs, runs,
            new PassThroughNormalizer(), Options.Create(new BekiOptions()), NullLogger<FastPreviewService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => preview.GenerateAsync(run, CancellationToken.None));
        Assert.Equal(0, images.ImageCalls);
        Assert.NotEqual(MasterStoryRunStatus.Ready, run.Status);
    }

    internal sealed class Runs : IMasterStoryRunRepository
    {
        public Dictionary<Guid, MasterStoryRun> Items { get; } = [];
        public MasterStoryRun Add(string name = "ნინა")
        {
            var run = new MasterStoryRun { Id = Guid.NewGuid(), ChildName = name, Age = 5, Gender = "girl",
                Theme = "Dinosaurs", PromptVersion = FastPreviewPlan.Version, PhotoBlobUrl = "https://blob.test/photo.png",
                ExpiresAt = DateTime.UtcNow.AddDays(1), SpreadCount = 8 };
            Items.Add(run.Id, run); return run;
        }
        public Task CreateAsync(MasterStoryRun run, CancellationToken ct) { Items.Add(run.Id, run); return Task.CompletedTask; }
        public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(id));
        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken ct) => Task.FromResult<MasterStoryRunProgress?>(null);
        public Task SetProgressAsync(Guid id, string status, string? message, CancellationToken ct) { Items[id].Status = status; return Task.CompletedTask; }
        public Task SavePromptsAsync(Guid id, string model, string version, string system, string user, CancellationToken ct)
        { Items[id].PromptVersion = version; return Task.CompletedTask; }
        public Task SaveStoryAsync(Guid id, string story, string content, int input, int output, CancellationToken ct)
        { Items[id].StoryJson = story; Items[id].ContentJson = content; return Task.CompletedTask; }
        public Task SaveAppearanceDescriptionAsync(Guid id, string appearance, CancellationToken ct) => Task.CompletedTask;
        public Task SaveCoverAsync(Guid id, string url, CancellationToken ct) { Items[id].CoverImageUrl = url; return Task.CompletedTask; }
        public Task MarkReadyAsync(Guid id, string content, CancellationToken ct)
        { Items[id].Status = MasterStoryRunStatus.Ready; Items[id].ContentJson = content; return Task.CompletedTask; }
        public Task MarkFailedAsync(Guid id, string error, CancellationToken ct) { Items[id].Status = MasterStoryRunStatus.Failed; return Task.CompletedTask; }
        public Task ClaimAsync(Guid id, Guid user, Guid? pack, CancellationToken ct)
        { Items[id].UserId = user; Items[id].PackId = pack; Items[id].ExpiresAt = null; return Task.CompletedTask; }
        public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<ExpiredMasterStoryRun>>([]);
        public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class FastStub(MasterStoryRun run) : IFastPreviewService
    {
        public int Calls;
        public FastPreviewRevision Revision { get; init; } = new(run.Id, Guid.NewGuid(), "dinosaurs", FastPreviewPlan.Title(run.ChildName, "dinosaurs"),
            "", "", "", FastPreviewPlan.Version, "", FastPreviewPlan.Model, FastPreviewPlan.Plan("dinosaurs").Json, "", "", "", "");
        public Task GenerateAsync(MasterStoryRun run, CancellationToken ct) { Calls++; return Task.CompletedTask; }
        public Task<FastPreviewRevision> ReadAsync(Guid id, CancellationToken ct) => Task.FromResult(Revision);
        public Task ValidatePurchaseAsync(Guid user, Guid id, Guid? revision, string theme, string name, CancellationToken ct, int? age = null, string? gender = null) => Task.CompletedTask;
    }
}
