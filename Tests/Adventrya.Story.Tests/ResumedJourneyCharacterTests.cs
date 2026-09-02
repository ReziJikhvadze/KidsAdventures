using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.Characters;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Implementations;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// A journey resumed in a new tab: the character is created from the preview run's parked
/// portrait, because the browser that opened the emailed sign-in link never held the file.
///
/// The one thing that must not happen is a stranger naming a guest run and receiving a child's
/// photograph on their own character. A run claimed by somebody else is refused; an unclaimed
/// run or one already claimed by this parent is copied, together with the description the run
/// paid for.
/// </summary>
public class ResumedJourneyCharacterTests : CompositePipelineTestBase
{
    private static readonly Guid Parent = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Stranger = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task An_unclaimed_run_lends_its_portrait_and_its_description()
    {
        var runs = new OneRun(new MasterStoryRun
        {
            Id = Guid.NewGuid(),
            UserId = null,
            PhotoBlobUrl = "https://blob.test/master-runs/x/portrait",
            AppearanceDescription = "brown curls, freckles",
        });
        var characters = new CapturingCharacters();

        var created = await Service(characters, runs).CreateAsync(
            Parent, Request(runs.Run.Id), photo: null, CancellationToken.None);

        var stored = Assert.Single(characters.Created);
        Assert.NotNull(stored.PhotoUrl);
        Assert.Contains($"{Parent}/characters/{stored.Id}/portrait-", stored.PhotoUrl);
        Assert.Equal("brown curls, freckles", stored.AppearanceDescription);
        Assert.Equal(stored.PhotoUrl, stored.AppearancePhotoUrl);
        Assert.Equal(stored.PhotoUrl, created.PhotoUrl);
        Assert.True(created.HasAppearanceProfile);
    }

    [Fact]
    public async Task A_run_claimed_by_this_parent_is_theirs_to_copy()
    {
        var runs = new OneRun(new MasterStoryRun
        {
            Id = Guid.NewGuid(),
            UserId = Parent,
            PhotoBlobUrl = "https://blob.test/master-runs/x/portrait",
        });
        var characters = new CapturingCharacters();

        await Service(characters, runs).CreateAsync(Parent, Request(runs.Run.Id), null, CancellationToken.None);

        Assert.NotNull(Assert.Single(characters.Created).PhotoUrl);
    }

    [Fact]
    public async Task A_run_claimed_by_somebody_else_gives_nothing_away()
    {
        var runs = new OneRun(new MasterStoryRun
        {
            Id = Guid.NewGuid(),
            UserId = Stranger,
            PhotoBlobUrl = "https://blob.test/master-runs/x/portrait",
            AppearanceDescription = "a child",
        });
        var characters = new CapturingCharacters();

        var created = await Service(characters, runs).CreateAsync(
            Parent, Request(runs.Run.Id), null, CancellationToken.None);

        // The character still exists — a refused run is not an error — but bare.
        var stored = Assert.Single(characters.Created);
        Assert.Null(stored.PhotoUrl);
        Assert.Null(stored.AppearanceDescription);
        Assert.False(created.HasAppearanceProfile);
    }

    [Fact]
    public async Task An_unknown_run_is_a_character_without_a_photo()
    {
        var characters = new CapturingCharacters();

        await Service(characters, new OneRun(null)).CreateAsync(
            Parent, Request(Guid.NewGuid()), null, CancellationToken.None);

        Assert.Null(Assert.Single(characters.Created).PhotoUrl);
    }

    private static SaveCharacterRequest Request(Guid runId) => new()
    {
        Name = "ვეკო",
        BirthDate = new DateOnly(2021, 3, 4),
        Gender = "girl",
        CharacterType = "child",
        IsPrimary = true,
        PortraitRunId = runId,
    };

    private static CharacterService Service(CapturingCharacters characters, OneRun runs) =>
        new(characters, new StubBlobStorage(), new PassThroughNormalizer(), runs);

    private sealed class OneRun(MasterStoryRun? run) : IMasterStoryRunRepository
    {
        public MasterStoryRun Run => run ?? throw new InvalidOperationException("no run");

        public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(run is not null && run.Id == id ? run : null);

        public Task CreateAsync(MasterStoryRun r, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<MasterStoryRunProgress?>(null);
        public Task SetProgressAsync(Guid id, string status, string? progressMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task SavePromptsAsync(Guid id, string model, string promptVersion, string systemPrompt, string userPrompt, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task SaveStoryAsync(Guid id, string storyJson, string contentJson, int promptTokens, int completionTokens, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAppearanceDescriptionAsync(Guid id, string appearanceDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExpiredMasterStoryRun>>([]);
        public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class CapturingCharacters : ICharacterRepository
    {
        public List<Character> Created { get; } = [];

        public Task<Guid> CreateAsync(Character character, CancellationToken cancellationToken)
        {
            Created.Add(character);
            return Task.FromResult(character.Id);
        }

        public Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<IReadOnlyList<Character>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetHeroesAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetHeroPortraitUrlsAsync(Guid userId, IReadOnlyCollection<Guid> characterIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Character?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(Character character, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateAppearanceCacheAsync(Guid id, Guid userId, string? appearanceDescription, string? appearancePhotoUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsCastInAnyBookAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<Guid>> GetCastCharacterIdsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetByBookIdAsync(Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetBookCastAsync(Guid bookId, IReadOnlyList<Guid> characterIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
