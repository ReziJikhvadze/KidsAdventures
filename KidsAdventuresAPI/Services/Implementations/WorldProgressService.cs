using AdventurePacks.Api.DTOs.Worlds;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Resolves a child's adventure map.
///
/// The rules, in one place so the map, the journey guard and order fulfilment cannot
/// disagree:
///
/// 1. Before the first book, every world is open. The first journey is a free choice —
///    that is the whole point of the "where does the big world begin?" screen.
/// 2. Once a world has been completed, the map becomes a path: completed worlds stay
///    completed, any world explicitly granted stays unlocked, the lowest-ordered
///    remaining world becomes <see cref="WorldState.Next"/>, and the rest are locked.
/// 3. "Next" is never stored. Deriving it means the map is always consistent with the
///    books that actually exist, even if a row was written by an older release.
/// </summary>
public sealed class WorldProgressService(
    IWorldRepository worldRepository,
    ICharacterRepository characterRepository,
    IAdventurePackRepository packRepository,
    ILogger<WorldProgressService> logger) : IWorldProgressService
{
    public async Task<IReadOnlyList<WorldResponse>> GetCatalogueAsync(CancellationToken cancellationToken)
    {
        var worlds = await worldRepository.GetActiveAsync(cancellationToken);
        return worlds
            .Select(world => new WorldResponse { Id = world.Id, Name = world.Name, SortOrder = world.SortOrder })
            .ToList();
    }

    public async Task<AdventureMapResponse> GetMapAsync(
        Guid userId,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var character = await characterRepository.GetByIdAsync(characterId, userId, cancellationToken)
                        ?? throw new KeyNotFoundException("პერსონაჟი ვერ მოიძებნა.");

        var worlds = await worldRepository.GetActiveAsync(cancellationToken);
        var progress = await worldRepository.GetProgressAsync(characterId, cancellationToken);
        var books = await packRepository.GetByCharacterIdAsync(characterId, userId, cancellationToken);
        var cast = await LoadLatestCastAsync(character, books, cancellationToken);

        return BuildMap(character, worlds, progress, books, cast);
    }

    public async Task<IReadOnlyList<AdventureMapResponse>> GetMapsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var heroes = await characterRepository.GetHeroesAsync(userId, cancellationToken);
        if (heroes.Count == 0)
        {
            return [];
        }

        var worlds = await worldRepository.GetActiveAsync(cancellationToken);
        var allProgress = await worldRepository.GetProgressForUserAsync(userId, cancellationToken);
        var progressByCharacter = allProgress.GroupBy(row => row.CharacterId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<UserWorldProgress>)group.ToList());

        var maps = new List<AdventureMapResponse>(heroes.Count);
        foreach (var hero in heroes)
        {
            var books = await packRepository.GetByCharacterIdAsync(hero.Id, userId, cancellationToken);
            var progress = progressByCharacter.TryGetValue(hero.Id, out var rows) ? rows : [];
            var cast = await LoadLatestCastAsync(hero, books, cancellationToken);
            maps.Add(BuildMap(hero, worlds, progress, books, cast));
        }

        return maps;
    }

    /// <summary>
    /// The cast of the newest paid book, which is what a continuation carries forward.
    /// Falls back to the hero alone, so the first continuation still has someone to bring.
    /// </summary>
    private async Task<IReadOnlyList<Character>> LoadLatestCastAsync(
        Character hero,
        IReadOnlyList<AdventurePack> books,
        CancellationToken cancellationToken)
    {
        var latest = LatestUnlockedBook(books);
        if (latest is null)
        {
            return [];
        }

        var cast = await characterRepository.GetByBookIdAsync(latest.Id, cancellationToken);
        return cast.Count > 0 ? cast : [hero];
    }

    public async Task EnsureCanStartAsync(
        Guid userId,
        Guid characterId,
        string worldId,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeWorldId(worldId);
        if (!await worldRepository.ExistsAsync(normalized, cancellationToken))
        {
            throw new InvalidOperationException("ასეთი სამყარო არ არსებობს.");
        }

        var map = await GetMapAsync(userId, characterId, cancellationToken);
        var node = map.Worlds.FirstOrDefault(world => world.WorldId == normalized);
        if (node is null || !node.CanStart)
        {
            throw new InvalidOperationException("ეს სამყარო ჯერ დაკეტილია. დაასრულე მიმდინარე თავგადასავალი.");
        }
    }

    public async Task MarkStartedAsync(
        Guid userId,
        Guid characterId,
        string worldId,
        CancellationToken cancellationToken)
    {
        await worldRepository.UnlockAsync(userId, characterId, NormalizeWorldId(worldId), cancellationToken);
    }

    public async Task MarkCompletedAsync(
        Guid userId,
        Guid characterId,
        string worldId,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeWorldId(worldId);
        await worldRepository.CompleteAsync(userId, characterId, normalized, bookId, cancellationToken);

        // Opening the following world on completion, rather than only deriving it at read
        // time, leaves a durable record of what the child earned — deriving alone would
        // silently re-lock a world if the ordering ever changed.
        var worlds = await worldRepository.GetActiveAsync(cancellationToken);
        var progress = await worldRepository.GetProgressAsync(characterId, cancellationToken);
        var nextWorld = ResolveNextWorld(worlds, progress);
        if (nextWorld is not null)
        {
            await worldRepository.UnlockAsync(userId, characterId, nextWorld.Id, cancellationToken);
        }

        logger.LogInformation(
            "World {WorldId} completed for character {CharacterId} by book {BookId}; next is {NextWorldId}.",
            normalized, characterId, bookId, nextWorld?.Id ?? "(none)");
    }

    // -- map resolution -----------------------------------------------------

    private static AdventureMapResponse BuildMap(
        Character character,
        IReadOnlyList<World> worlds,
        IReadOnlyList<UserWorldProgress> progress,
        IReadOnlyList<AdventurePack> books,
        IReadOnlyList<Character> latestCast)
    {
        var progressByWorld = progress
            .GroupBy(row => row.WorldId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        // Only paid books count towards the map: a preview that was never bought has
        // not taken the child anywhere.
        var unlockedBooks = books.Where(book => book.IsFullyUnlocked).ToList();
        var bookById = unlockedBooks.ToDictionary(book => book.Id);

        var completedWorldIds = progressByWorld.Values
            .Where(row => row.State == WorldState.Completed)
            .Select(row => row.WorldId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var isFirstJourney = completedWorldIds.Count == 0;
        var nextWorld = isFirstJourney ? null : ResolveNextWorld(worlds, progress);

        var nodes = new List<WorldNodeResponse>(worlds.Count);
        foreach (var world in worlds)
        {
            progressByWorld.TryGetValue(world.Id, out var row);

            var state = ResolveState(world, row, isFirstJourney, nextWorld);
            var book = row?.BookId is { } bookId && bookById.TryGetValue(bookId, out var found)
                ? found
                : unlockedBooks.LastOrDefault(candidate =>
                    string.Equals(candidate.WorldId, world.Id, StringComparison.OrdinalIgnoreCase));

            nodes.Add(new WorldNodeResponse
            {
                WorldId = world.Id,
                Name = world.Name,
                SortOrder = world.SortOrder,
                State = state,
                CanStart = state is WorldState.Unlocked or WorldState.Next,
                BookId = book?.Id,
                BookTitle = book?.Title,
                CoverImageUrl = book?.CoverImageUrl,
                SequenceNumber = book?.SequenceNumber,
                CompletedAt = row?.CompletedAt
            });
        }

        return new AdventureMapResponse
        {
            CharacterId = character.Id,
            CharacterName = character.Name,
            IsFirstJourney = isFirstJourney,
            CompletedCount = completedWorldIds.Count,
            TotalWorlds = worlds.Count,
            NextWorldId = nextWorld?.Id,
            Worlds = nodes,
            Continuation = BuildContinuation(unlockedBooks, nextWorld, latestCast)
        };
    }

    private static WorldState ResolveState(
        World world,
        UserWorldProgress? row,
        bool isFirstJourney,
        World? nextWorld)
    {
        if (row?.State == WorldState.Completed)
        {
            return WorldState.Completed;
        }

        // Rule 1: the first journey is a free choice across the whole map.
        if (isFirstJourney)
        {
            return WorldState.Unlocked;
        }

        if (nextWorld is not null && string.Equals(nextWorld.Id, world.Id, StringComparison.OrdinalIgnoreCase))
        {
            return WorldState.Next;
        }

        return row?.State == WorldState.Unlocked ? WorldState.Unlocked : WorldState.Locked;
    }

    /// <summary>Lowest-ordered world the child has not finished yet.</summary>
    private static World? ResolveNextWorld(IReadOnlyList<World> worlds, IReadOnlyList<UserWorldProgress> progress)
    {
        var completed = progress
            .Where(row => row.State == WorldState.Completed)
            .Select(row => row.WorldId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return worlds.FirstOrDefault(world => !completed.Contains(world.Id));
    }

    private static AdventurePack? LatestUnlockedBook(IReadOnlyList<AdventurePack> books) =>
        books
            .Where(book => book.IsFullyUnlocked)
            .OrderByDescending(book => book.SequenceNumber)
            .ThenByDescending(book => book.CreatedAt)
            .FirstOrDefault();

    private static ContinuationResponse? BuildContinuation(
        IReadOnlyList<AdventurePack> unlockedBooks,
        World? nextWorld,
        IReadOnlyList<Character> latestCast)
    {
        var latest = LatestUnlockedBook(unlockedBooks);
        if (latest is null)
        {
            return null;
        }

        return new ContinuationResponse
        {
            FromBookId = latest.Id,
            FromBookTitle = latest.Title,
            FromWorldId = latest.WorldId,
            FromSequenceNumber = latest.SequenceNumber,
            NextSequenceNumber = latest.SequenceNumber + 1,
            SuggestedWorldId = nextWorld?.Id,
            CarryForwardCharacters = latestCast
                .OrderByDescending(member => member.IsPrimary)
                .Select(member => new ContinuationCharacter
                {
                    Id = member.Id,
                    Name = member.Name,
                    CharacterType = member.CharacterType,
                    Relationship = member.Relationship,
                    IsPrimary = member.IsPrimary
                })
                .ToList()
        };
    }

    private static string NormalizeWorldId(string worldId)
    {
        var normalized = (worldId ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("აირჩიე სამყარო.");
        }

        return normalized;
    }
}
