using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// A second book for a child the account already knows.
///
/// The preview start describes the portrait with a vision call and parks the bytes for the
/// illustrator. A saved hero arrives with the description their earlier book already paid for,
/// and the run must be written from it without asking the model the same question again — while
/// a portrait nobody has described yet still goes to the model exactly as before.
/// </summary>
public class SavedHeroPreviewTests : CompositePipelineTestBase
{
    private static readonly byte[] Portrait = Png(64, 64);

    [Fact]
    public async Task A_cached_appearance_skips_the_vision_call_and_lands_on_the_run()
    {
        var images = new CountingImages();
        var runs = new CapturingRuns();

        await Service(images, runs).StartAsync(
            new GuestPreviewInput
            {
                ChildName = "ვეკო",
                Age = 5,
                Theme = ThemeType.Dinosaurs,
                PhotoBytes = Portrait,
                PhotoContentType = "image/png",
                AppearanceDescription = "  a five-year-old with brown curls  ",
            },
            CancellationToken.None);

        Assert.Equal(0, images.DescribeCalls);
        var run = Assert.Single(runs.Created);
        Assert.Equal("a five-year-old with brown curls", run.AppearanceDescription);
        // The portrait is still parked: the illustrator draws from the face, not the paragraph.
        Assert.NotNull(run.PhotoBlobUrl);
    }

    [Fact]
    public async Task A_portrait_without_a_cache_is_still_described_once()
    {
        var images = new CountingImages();
        var runs = new CapturingRuns();

        await Service(images, runs).StartAsync(
            new GuestPreviewInput
            {
                ChildName = "ვეკო",
                Age = 5,
                Theme = ThemeType.Dinosaurs,
                PhotoBytes = Portrait,
                PhotoContentType = "image/png",
            },
            CancellationToken.None);

        Assert.Equal(1, images.DescribeCalls);
        Assert.Equal("a child", Assert.Single(runs.Created).AppearanceDescription);
    }

    [Fact]
    public async Task A_blank_cache_is_not_a_cache()
    {
        var images = new CountingImages();
        var runs = new CapturingRuns();

        await Service(images, runs).StartAsync(
            new GuestPreviewInput
            {
                ChildName = "ვეკო",
                Age = 5,
                Theme = ThemeType.Dinosaurs,
                PhotoBytes = Portrait,
                PhotoContentType = "image/png",
                AppearanceDescription = "   ",
            },
            CancellationToken.None);

        Assert.Equal(1, images.DescribeCalls);
    }

    private static MasterBookService Service(CountingImages images, CapturingRuns runs) =>
        new(runs,
            new StubMasterStoryService(),
            images,
            new StubBlobStorage(),
            new PassThroughNormalizer(),
            new StubBackgroundJobClient(),
            new SpyBekiBookGenerator(),
            Microsoft.Extensions.Options.Options.Create(new AdventurePacks.Api.Configuration.Options.BekiOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MasterBookService>.Instance);

    private sealed class CountingImages : IOpenAiService
    {
        public int DescribeCalls { get; private set; }

        public Task<AdventureContentDto> GenerateAdventureContentAsync(
            AdventureGenerationInput input, Guid adventureId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<byte[]> GenerateStoryImageAsync(
            string imagePrompt, StoryImageReference? reference, CancellationToken cancellationToken,
            string? imageSize = null, bool requireReferences = false) =>
            throw new NotSupportedException();

        public Task<string> ReviewIllustrationAsync(
            byte[] imageBytes, string reviewPrompt,
            IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> DescribeCharacterFromPhotoAsync(
            byte[] imageBytes, string contentType, string promptText, CancellationToken cancellationToken)
        {
            DescribeCalls++;
            return Task.FromResult("a child");
        }

        public Task<string> CompleteTextAsync(string promptText, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingRuns : IMasterStoryRunRepository
    {
        public List<MasterStoryRun> Created { get; } = [];

        public Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken)
        {
            Created.Add(run);
            return Task.CompletedTask;
        }

        public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Created.FirstOrDefault(run => run.Id == id));

        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<MasterStoryRunProgress?>(null);

        public Task SetProgressAsync(Guid id, string status, string? progressMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SavePromptsAsync(
            Guid id, string model, string promptVersion, string systemPrompt, string userPrompt,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveStoryAsync(
            Guid id, string storyJson, string contentJson, int promptTokens, int completionTokens,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExpiredMasterStoryRun>>([]);

        public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
