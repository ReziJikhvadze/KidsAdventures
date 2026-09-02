using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// Where the portrait gets described, and when it does not.
///
/// The request parks the portrait and asks the model nothing: the vision call used to sit inside
/// the HTTP request the parent was waiting on, and the composite pipeline — which draws the child
/// from the photograph, not from a paragraph about it — never reads the answer. So the job
/// describes the portrait, once, for the one planner that reads it, writes the answer to the row
/// so a requeued attempt does not pay for it again, and skips it entirely on a composite run.
///
/// A saved hero arrives with the description their earlier book already paid for, and neither
/// the request nor the job asks the model the same question again.
/// </summary>
public class SavedHeroPreviewTests : CompositePipelineTestBase
{
    private static readonly byte[] Portrait = Png(64, 64);

    // -- the request -----------------------------------------------------

    [Fact]
    public async Task A_cached_appearance_skips_the_vision_call_and_lands_on_the_run()
    {
        var images = new CountingImages();
        var runs = new CapturingRuns();

        await Service(images, runs).StartAsync(
            Input(appearance: "  a five-year-old with brown curls  "), CancellationToken.None);

        Assert.Equal(0, images.DescribeCalls);
        var run = Assert.Single(runs.Created);
        Assert.Equal("a five-year-old with brown curls", run.AppearanceDescription);
        // The portrait is still parked: the illustrator draws from the face, not the paragraph.
        Assert.NotNull(run.PhotoBlobUrl);
    }

    [Fact]
    public async Task The_request_parks_the_portrait_and_asks_the_model_nothing()
    {
        // The whole point of the move: a parent's request is an upload and an insert, never a
        // vision call.
        var images = new CountingImages();
        var runs = new CapturingRuns();

        await Service(images, runs).StartAsync(Input(), CancellationToken.None);

        Assert.Equal(0, images.DescribeCalls);
        var run = Assert.Single(runs.Created);
        Assert.Null(run.AppearanceDescription);
        Assert.NotNull(run.PhotoBlobUrl);
    }

    [Fact]
    public async Task A_blank_cache_is_not_a_cache()
    {
        var images = new CountingImages();
        var runs = new CapturingRuns();

        await Service(images, runs).StartAsync(Input(appearance: "   "), CancellationToken.None);

        Assert.Null(Assert.Single(runs.Created).AppearanceDescription);
    }

    // -- the job ---------------------------------------------------------

    [Fact]
    public async Task The_job_describes_an_undescribed_portrait_once_and_writes_it_down()
    {
        var images = new CountingImages();
        var runs = new CapturingRuns();
        var story = new RecordingLegacyStoryService();
        var service = Service(images, runs, story);

        var runId = await service.StartAsync(Input(), CancellationToken.None);
        await service.WriteBookAsync(runId, CancellationToken.None);

        Assert.Equal(1, images.DescribeCalls);

        // On the row, and in the prompt: the legacy planner's identity chain starts here.
        Assert.Equal("a child", runs.SavedAppearanceDescription);
        Assert.Equal("a child", Assert.Single(story.Inputs).AppearanceDescription);
        Assert.Equal(MasterStoryRunStatus.Ready, runs.Status);
    }

    [Fact]
    public async Task A_cached_appearance_is_not_asked_for_again_by_the_job()
    {
        var images = new CountingImages();
        var runs = new CapturingRuns();
        var story = new RecordingLegacyStoryService();
        var service = Service(images, runs, story);

        var runId = await service.StartAsync(Input(appearance: "brown curls"), CancellationToken.None);
        await service.WriteBookAsync(runId, CancellationToken.None);

        Assert.Equal(0, images.DescribeCalls);
        Assert.Null(runs.SavedAppearanceDescription);
        Assert.Equal("brown curls", Assert.Single(story.Inputs).AppearanceDescription);
    }

    [Fact]
    public async Task A_requeued_job_finds_the_description_it_already_paid_for()
    {
        /*
          The reason the answer is persisted before the story call rather than carried in memory.
          The first attempt pays for the description and then dies in the story call; the second
          attempt reads the run back and must find the paragraph on the row, not buy it again.
        */
        var images = new CountingImages();
        var runs = new CapturingRuns();
        var story = new RecordingLegacyStoryService { FailFirst = true };
        var service = Service(images, runs, story);

        var runId = await service.StartAsync(Input(), CancellationToken.None);

        await service.WriteBookAsync(runId, CancellationToken.None);
        Assert.Equal(MasterStoryRunStatus.Failed, runs.Status);

        await service.WriteBookAsync(runId, CancellationToken.None);
        Assert.Equal(MasterStoryRunStatus.Ready, runs.Status);

        Assert.Equal(1, images.DescribeCalls);
        Assert.Equal(2, story.Inputs.Count);
        Assert.All(story.Inputs, input => Assert.Equal("a child", input.AppearanceDescription));
    }

    [Fact]
    public async Task A_composite_run_never_asks_for_a_description()
    {
        // The composite plan carries no appearance description by design — the child's likeness
        // reaches the illustrator as the photograph itself — so the vision call would be money
        // for an answer nothing reads.
        var images = new CountingImages();
        var runs = new CapturingRuns();
        var story = new ScriptedCompositeStoryService(Plan());
        var service = Service(images, runs, story, new BekiOptions
        {
            CompositePipelineEnabled = true,
            BookFormatEnabled = true,
        });

        var runId = await service.StartAsync(Input(), CancellationToken.None);
        await service.WriteBookAsync(runId, CancellationToken.None);

        // The planner was reached, so the describe step had its chance and declined.
        Assert.True(story.Calls >= 1, "the composite planner should have been asked for a plan");
        Assert.Equal(0, images.DescribeCalls);
        Assert.Null(runs.SavedAppearanceDescription);
    }

    [Fact]
    public async Task A_run_whose_portrait_did_not_park_is_written_without_a_description()
    {
        // Nothing to describe, and the legacy planner still writes the book — which is how the
        // very first version of this flow worked.
        var images = new CountingImages();
        var runs = new CapturingRuns { LoseTheUpload = true };
        var story = new RecordingLegacyStoryService();
        var service = Service(images, runs, story);

        var runId = await service.StartAsync(Input(), CancellationToken.None);
        await service.WriteBookAsync(runId, CancellationToken.None);

        Assert.Equal(0, images.DescribeCalls);
        Assert.Null(Assert.Single(story.Inputs).AppearanceDescription);
        Assert.Equal(MasterStoryRunStatus.Ready, runs.Status);
    }

    // -- harness ---------------------------------------------------------

    private static GuestPreviewInput Input(string? appearance = null) => new()
    {
        ChildName = "ნინა",
        Age = 5,
        Gender = "girl",
        Theme = ThemeType.Dinosaurs,
        PhotoBytes = Portrait,
        PhotoContentType = "image/png",
        AppearanceDescription = appearance,
    };

    private static MasterBookService Service(
        CountingImages images,
        CapturingRuns runs,
        IMasterStoryService? story = null,
        BekiOptions? beki = null) =>
        new(runs,
            story ?? new StubMasterStoryService(),
            images,
            runs.Blobs,
            new PassThroughNormalizer(),
            new StubBackgroundJobClient(),
            new SpyBekiBookGenerator(),
            Options.Create(beki ?? new BekiOptions()),
            NullLogger<MasterBookService>.Instance);

    /// <summary>A legacy (A5, v1) planner that records what it was asked to write from.</summary>
    private sealed class RecordingLegacyStoryService : IMasterStoryService
    {
        public List<MasterStoryInput> Inputs { get; } = [];

        /// <summary>Throw on the first call, succeed on the second — a job that died mid-story.</summary>
        public bool FailFirst { get; init; }

        public string ModelName => "test-model";

        public string PromptVersion => "v1";

        public (string System, string User) BuildPrompts(MasterStoryInput input) => ("system", "user");

        public Task<MasterStoryResult> WriteAsync(MasterStoryInput input, CancellationToken cancellationToken)
        {
            Inputs.Add(input);

            if (FailFirst && Inputs.Count == 1)
            {
                throw new InvalidOperationException("the model refused");
            }

            return Task.FromResult(new MasterStoryResult
            {
                Story = LegacyPlan(),
                SystemPrompt = "system",
                UserPrompt = "user",
                Model = ModelName,
                PromptTokens = 1,
                CompletionTokens = 1,
            });
        }

        public Task<MasterStoryResult> RetryPlanWithCorrectionsAsync(
            MasterStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MasterStoryResult> WriteCompositePlanAsync(
            CompositeStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static MasterStory LegacyPlan() => new()
    {
        Concept = new StoryConcept { Title = "ოქროსფერი ფოთოლი", Outline = ["beat"] },
        TitleEn = "The Golden Leaf",
        CharacterLock = "A child with dark hair.",
        WorldLock = "A warm valley.",
        Cover = new IllustrationBrief { Scene = "The child at the valley's edge." },
        Spreads = Enumerable.Range(1, BookFormat.SpreadCount).Select(number => new StorySpread
        {
            Number = number,
            Title = string.Empty,
            Caption = string.Empty,
            Text = $"ქართული ტექსტი {number}",
            TextEn = $"Georgian text {number}",
            Illustration = new IllustrationBrief { Scene = $"Scene {number}" }
        }).ToList()
    };

    private sealed class CountingImages : IOpenAiService
    {
        public int DescribeCalls { get; private set; }

        public Task<AdventureContentDto> GenerateAdventureContentAsync(
            AdventureGenerationInput input, Guid adventureId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        // The cover is not this file's subject; it must simply fail the way a cover is allowed to.
        public Task<byte[]> GenerateStoryImageAsync(
            string imagePrompt, StoryImageReference? reference, CancellationToken cancellationToken,
            string? imageSize = null, bool requireReferences = false, string? imageQuality = null) =>
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

    /// <summary>
    /// The run table as a row store: what was created, and what each later write changed. Reads
    /// hand back a fresh copy built from those writes rather than the object the job mutated, so
    /// that "a requeued job finds it on the row" is a statement about persistence, not about a
    /// shared reference.
    /// </summary>
    private sealed class CapturingRuns : IMasterStoryRunRepository
    {
        public List<MasterStoryRun> Created { get; } = [];
        public string? SavedAppearanceDescription { get; private set; }
        public string Status { get; private set; } = MasterStoryRunStatus.Pending;
        public string? Error { get; private set; }

        private string? _storyJson;
        private string? _contentJson;

        /// <summary>Make the portrait upload fail, so the run arrives with no PhotoBlobUrl.</summary>
        public bool LoseTheUpload { get; init; }

        public IBlobStorageService Blobs => new PortraitBlobs(this);

        public Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken)
        {
            Created.Add(run);
            return Task.CompletedTask;
        }

        public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var created = Created.FirstOrDefault(run => run.Id == id);
            if (created is null)
            {
                return Task.FromResult<MasterStoryRun?>(null);
            }

            return Task.FromResult<MasterStoryRun?>(new MasterStoryRun
            {
                Id = created.Id,
                Status = Status,
                ChildName = created.ChildName,
                BirthDate = created.BirthDate,
                Age = created.Age,
                Gender = created.Gender,
                Theme = created.Theme,
                EyeColor = created.EyeColor,
                ExtraWishes = created.ExtraWishes,
                AppearanceDescription = SavedAppearanceDescription ?? created.AppearanceDescription,
                PhotoBlobUrl = created.PhotoBlobUrl,
                StoryLanguage = created.StoryLanguage,
                SpreadCount = created.SpreadCount,
                StoryJson = _storyJson,
                ContentJson = _contentJson,
            });
        }

        public Task SaveAppearanceDescriptionAsync(
            Guid id, string appearanceDescription, CancellationToken cancellationToken)
        {
            SavedAppearanceDescription = appearanceDescription;
            return Task.CompletedTask;
        }

        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<MasterStoryRunProgress?>(null);

        public Task SetProgressAsync(Guid id, string status, string? progressMessage, CancellationToken cancellationToken)
        {
            Status = status;
            return Task.CompletedTask;
        }

        public Task SavePromptsAsync(
            Guid id, string model, string promptVersion, string systemPrompt, string userPrompt,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveStoryAsync(
            Guid id, string storyJson, string contentJson, int promptTokens, int completionTokens,
            CancellationToken cancellationToken)
        {
            _storyJson = storyJson;
            _contentJson = contentJson;
            return Task.CompletedTask;
        }

        public Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken)
        {
            Status = MasterStoryRunStatus.Ready;
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken)
        {
            Status = MasterStoryRunStatus.Failed;
            Error = error;
            return Task.CompletedTask;
        }

        public Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExpiredMasterStoryRun>>([]);

        public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        /// <summary>Blob storage that parks the portrait — or refuses to, when the test says so.</summary>
        private sealed class PortraitBlobs(CapturingRuns runs) : IBlobStorageService
        {
            public Task<string> UploadAsync(
                string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken) =>
                runs.LoseTheUpload
                    ? throw new IOException("storage is away")
                    : Task.FromResult($"https://blob.test/{blobName}");

            public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken) =>
                Task.FromResult<Stream>(new MemoryStream());

            public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) =>
                Task.FromResult(false);

            public Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
                Task.FromResult(Portrait);

            public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
                Task.FromResult(true);
        }
    }
}
