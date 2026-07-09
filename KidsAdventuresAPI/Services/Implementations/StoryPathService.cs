using System.Text.Json;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.DTOs.StoryPath;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class StoryPathService(
    IChildRepository childRepository,
    IStoryPathRepository storyPathRepository,
    IAdventurePackRepository adventurePackRepository,
    IAdventureGenerationService adventureGenerationService) : IStoryPathService
{
    /// <summary>A Story Path world is a saga of 5 chapters — each chapter is its own full 6-page story.</summary>
    private const int ChapterCount = 5;

    private static readonly ThemeType[] ThemeOrder =
    [
        ThemeType.Airplanes,
        ThemeType.Dinosaurs,
        ThemeType.Space,
        ThemeType.Pirates,
        ThemeType.Animals
    ];

    private static readonly IReadOnlyDictionary<ThemeType, string> AchievementKeys = new Dictionary<ThemeType, string>
    {
        [ThemeType.Airplanes] = "sky_explorer",
        [ThemeType.Dinosaurs] = "dino_explorer",
        [ThemeType.Space] = "star_voyager",
        [ThemeType.Pirates] = "treasure_captain",
        [ThemeType.Animals] = "wildlife_friend"
    };

    private static readonly IReadOnlyDictionary<ThemeType, string> AchievementLabels = new Dictionary<ThemeType, string>
    {
        [ThemeType.Airplanes] = "Sky Explorer",
        [ThemeType.Dinosaurs] = "Dino Explorer",
        [ThemeType.Space] = "Star Voyager",
        [ThemeType.Pirates] = "Treasure Captain",
        [ThemeType.Animals] = "Wildlife Friend"
    };

    public async Task<StoryPathOverviewResponse> GetOverviewAsync(Guid userId, Guid childId, CancellationToken cancellationToken)
    {
        await EnsureChildAsync(userId, childId, cancellationToken);

        var worlds = new List<StoryPathWorldDto>();
        foreach (var theme in ThemeOrder)
        {
            worlds.Add(await BuildWorldAsync(childId, theme, cancellationToken));
        }

        var achievements = await GetAchievementsInternalAsync(childId, cancellationToken);
        return new StoryPathOverviewResponse
        {
            ChildId = childId,
            Worlds = worlds,
            Achievements = achievements
        };
    }

    public async Task<StoryPathWorldResponse?> GetWorldAsync(Guid userId, Guid childId, ThemeType theme, CancellationToken cancellationToken)
    {
        await EnsureChildAsync(userId, childId, cancellationToken);
        var world = await BuildWorldAsync(childId, theme, cancellationToken);
        var achievements = await GetAchievementsInternalAsync(childId, cancellationToken);
        return new StoryPathWorldResponse
        {
            ChildId = childId,
            World = world,
            Achievements = achievements
        };
    }

    public async Task<IReadOnlyList<StoryPathAchievementDto>> GetAchievementsAsync(
        Guid userId,
        Guid childId,
        CancellationToken cancellationToken)
    {
        await EnsureChildAsync(userId, childId, cancellationToken);
        return await GetAchievementsInternalAsync(childId, cancellationToken);
    }

    public async Task<ConfirmCampfireResponse?> ConfirmCampfireAsync(
        Guid userId,
        ConfirmCampfireRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureChildAsync(userId, request.ChildId, cancellationToken);

        var progress = await storyPathRepository.GetNodeProgressAsync(
            request.ChildId,
            request.AdventurePackId,
            cancellationToken);

        var node = progress.FirstOrDefault(p => p.NodeIndex == request.NodeIndex);
        if (node is null || node.Status != StoryPathNodeStatus.Unlocked)
        {
            return null;
        }

        await storyPathRepository.UpdateNodeStatusAsync(
            request.ChildId,
            request.AdventurePackId,
            request.NodeIndex,
            StoryPathNodeStatus.Complete,
            DateTime.UtcNow,
            cancellationToken);

        var pageCount = progress.Count;
        if (request.NodeIndex < pageCount - 1)
        {
            await storyPathRepository.UpdateNodeStatusAsync(
                request.ChildId,
                request.AdventurePackId,
                request.NodeIndex + 1,
                StoryPathNodeStatus.Unlocked,
                null,
                cancellationToken);
        }

        var world = await BuildWorldAsync(request.ChildId, node.Theme, cancellationToken);
        return new ConfirmCampfireResponse { World = world };
    }

    public async Task<GenerateChapterResponse> GenerateChapterAsync(
        Guid userId,
        ThemeType theme,
        int chapterIndex,
        GenerateChapterRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureChildAsync(userId, request.ChildId, cancellationToken);
        ValidateChapterIndex(chapterIndex);

        var chapters = await EnsureChaptersAsync(request.ChildId, theme, cancellationToken);
        var chapter = chapters.FirstOrDefault(c => c.ChapterIndex == chapterIndex)
            ?? throw new InvalidOperationException("Chapter not found.");

        var existingPack = chapter.AdventurePackId is { } existingPackId
            ? await adventurePackRepository.GetByIdNoOwnershipAsync(existingPackId, cancellationToken)
            : null;

        // Allow (re)generation when there's no pack yet, or the previous attempt failed.
        if (existingPack is null || existingPack.Status == AdventurePackStatus.Failed)
        {
            if (chapter.Status != StoryPathNodeStatus.Unlocked)
            {
                throw new InvalidOperationException("This chapter is not unlocked yet.");
            }

            var previousPack = chapterIndex > 0
                ? chapters.FirstOrDefault(c => c.ChapterIndex == chapterIndex - 1)?.AdventurePackId
                : null;

            string? storyLanguage = null;
            if (previousPack is { } previousPackId)
            {
                var prev = await adventurePackRepository.GetByIdNoOwnershipAsync(previousPackId, cancellationToken);
                storyLanguage = prev?.StoryLanguage;
            }

            var newPackId = await adventureGenerationService.QueueGenerationAsync(
                userId,
                new GenerateAdventurePackRequest
                {
                    ChildId = request.ChildId,
                    Theme = theme,
                    StoryLanguage = storyLanguage,
                    ChapterIndex = chapterIndex,
                    PreviousChapterPackId = previousPack
                },
                cancellationToken);

            await storyPathRepository.SetChapterPackAsync(request.ChildId, theme, chapterIndex, newPackId, cancellationToken);
        }

        var world = await BuildWorldAsync(request.ChildId, theme, cancellationToken);
        return new GenerateChapterResponse { World = world };
    }

    public async Task<CompleteChapterResponse?> CompleteChapterAsync(
        Guid userId,
        ThemeType theme,
        int chapterIndex,
        CompleteChapterRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureChildAsync(userId, request.ChildId, cancellationToken);
        ValidateChapterIndex(chapterIndex);

        var chapters = await storyPathRepository.GetChaptersAsync(request.ChildId, theme, cancellationToken);
        var chapter = chapters.FirstOrDefault(c => c.ChapterIndex == chapterIndex);
        if (chapter is null || chapter.AdventurePackId is null)
        {
            return null;
        }

        StoryPathAchievementDto? newAchievement = null;

        if (chapter.Status != StoryPathNodeStatus.Complete)
        {
            await storyPathRepository.UpdateChapterStatusAsync(
                request.ChildId,
                theme,
                chapterIndex,
                StoryPathNodeStatus.Complete,
                DateTime.UtcNow,
                cancellationToken);

            if (chapterIndex < ChapterCount - 1)
            {
                await storyPathRepository.UpdateChapterStatusAsync(
                    request.ChildId,
                    theme,
                    chapterIndex + 1,
                    StoryPathNodeStatus.Unlocked,
                    null,
                    cancellationToken);
            }

            if (chapterIndex == ChapterCount - 1)
            {
                var awarded = await storyPathRepository.TryAwardAchievementAsync(
                    request.ChildId,
                    theme,
                    AchievementKeys[theme],
                    cancellationToken);
                if (awarded is not null)
                {
                    newAchievement = MapAchievement(awarded);
                }
            }
        }

        var world = await BuildWorldAsync(request.ChildId, theme, cancellationToken);
        var nextTheme = GetNextTheme(theme);
        var suggestNext = chapterIndex == ChapterCount - 1
                          && nextTheme is not null
                          && !await storyPathRepository.HasReadablePackForThemeAsync(request.ChildId, nextTheme.Value, cancellationToken);

        return new CompleteChapterResponse
        {
            World = world,
            NewAchievement = newAchievement,
            NextTheme = nextTheme?.ToString(),
            SuggestNextWorld = suggestNext
        };
    }

    private async Task<StoryPathWorldDto> BuildWorldAsync(Guid childId, ThemeType theme, CancellationToken cancellationToken)
    {
        var chapters = await EnsureChaptersAsync(childId, theme, cancellationToken);
        var nodes = new List<StoryPathNodeDto>();

        foreach (var chapter in chapters.OrderBy(c => c.ChapterIndex))
        {
            var node = new StoryPathNodeDto
            {
                ChapterIndex = chapter.ChapterIndex,
                AdventurePackId = chapter.AdventurePackId,
                ParentConfirmedAt = chapter.ParentConfirmedAt
            };

            if (chapter.Status == StoryPathNodeStatus.Complete)
            {
                node.Status = "Complete";
                if (chapter.AdventurePackId is { } completedPackId)
                {
                    var completedPack = await adventurePackRepository.GetByIdNoOwnershipAsync(completedPackId, cancellationToken);
                    node.Title = DeserializeContent(completedPack?.GeneratedJson)?.Title;
                }
            }
            else if (chapter.AdventurePackId is null)
            {
                node.Status = chapter.Status == StoryPathNodeStatus.Locked ? "Locked" : "Unlocked";
            }
            else
            {
                var pack = await adventurePackRepository.GetByIdNoOwnershipAsync(chapter.AdventurePackId.Value, cancellationToken);
                if (pack is null || pack.Status == AdventurePackStatus.Failed)
                {
                    node.Status = "Unlocked";
                }
                else
                {
                    var content = DeserializeContent(pack.GeneratedJson);
                    if (IsPackReadable(pack, content))
                    {
                        node.Status = "ReadyToRead";
                        node.Title = content?.Title;
                        if (content?.StoryPages.Count > 0)
                        {
                            node.CoverIllustrationUrl = $"/api/adventure-packs/{pack.Id}/illustrations/0";
                            await EnsurePageProgressAsync(childId, pack, theme, content.StoryPages.Count, cancellationToken);
                        }
                    }
                    else
                    {
                        node.Status = "Generating";
                        node.Title = content?.Title;
                    }
                }
            }

            nodes.Add(node);
        }

        var isComplete = nodes.Count == ChapterCount && nodes[^1].Status == "Complete";

        return new StoryPathWorldDto
        {
            Theme = theme.ToString(),
            HasReadablePack = true,
            IsWorldComplete = isComplete,
            Nodes = nodes
        };
    }

    /// <summary>Ensures the 5 chapter slots exist for this child+theme, migrating a legacy single-pack world (if any) into Chapter 0.</summary>
    private async Task<IReadOnlyList<StoryPathChapter>> EnsureChaptersAsync(Guid childId, ThemeType theme, CancellationToken cancellationToken)
    {
        var existing = await storyPathRepository.GetChaptersAsync(childId, theme, cancellationToken);
        if (existing.Count >= ChapterCount)
        {
            return existing;
        }

        var legacyPack = existing.Count == 0
            ? await storyPathRepository.GetLatestReadablePackAsync(childId, theme, cancellationToken)
            : null;

        var rows = new List<StoryPathChapter>();
        StoryPathNodeStatus? previousStatus = null;

        for (var i = 0; i < ChapterCount; i++)
        {
            var existingChapter = existing.FirstOrDefault(c => c.ChapterIndex == i);
            if (existingChapter is not null)
            {
                previousStatus = existingChapter.Status;
                continue;
            }

            StoryPathNodeStatus status;
            Guid? packId = null;

            if (i == 0 && legacyPack is not null)
            {
                packId = legacyPack.Id;
                var legacyProgress = await storyPathRepository.GetNodeProgressAsync(childId, legacyPack.Id, cancellationToken);
                status = legacyProgress.Count > 0 && legacyProgress.All(p => p.Status == StoryPathNodeStatus.Complete)
                    ? StoryPathNodeStatus.Complete
                    : StoryPathNodeStatus.Unlocked;
            }
            else if (i == 0)
            {
                status = StoryPathNodeStatus.Unlocked;
            }
            else
            {
                status = previousStatus == StoryPathNodeStatus.Complete
                    ? StoryPathNodeStatus.Unlocked
                    : StoryPathNodeStatus.Locked;
            }

            rows.Add(new StoryPathChapter
            {
                Id = Guid.NewGuid(),
                ChildId = childId,
                Theme = theme,
                ChapterIndex = i,
                AdventurePackId = packId,
                Status = status,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            previousStatus = status;
        }

        if (rows.Count > 0)
        {
            await storyPathRepository.CreateChaptersBatchAsync(rows, cancellationToken);
        }

        return await storyPathRepository.GetChaptersAsync(childId, theme, cancellationToken);
    }

    private async Task EnsurePageProgressAsync(
        Guid childId,
        AdventurePack pack,
        ThemeType theme,
        int pageCount,
        CancellationToken cancellationToken)
    {
        var existing = await storyPathRepository.GetNodeProgressAsync(childId, pack.Id, cancellationToken);
        if (existing.Count > 0)
        {
            return;
        }

        var rows = Enumerable.Range(0, pageCount).Select(index => new StoryPathNodeProgress
        {
            Id = Guid.NewGuid(),
            ChildId = childId,
            AdventurePackId = pack.Id,
            Theme = theme,
            NodeIndex = index,
            Status = index == 0 ? StoryPathNodeStatus.Unlocked : StoryPathNodeStatus.Locked,
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        await storyPathRepository.CreateNodeProgressBatchAsync(rows, cancellationToken);
    }

    private static void ValidateChapterIndex(int chapterIndex)
    {
        if (chapterIndex < 0 || chapterIndex >= ChapterCount)
        {
            throw new InvalidOperationException("Invalid chapter index.");
        }
    }

    private async Task<IReadOnlyList<StoryPathAchievementDto>> GetAchievementsInternalAsync(
        Guid childId,
        CancellationToken cancellationToken)
    {
        var rows = await storyPathRepository.GetAchievementsAsync(childId, cancellationToken);
        return rows.Select(MapAchievement).ToList();
    }

    private async Task EnsureChildAsync(Guid userId, Guid childId, CancellationToken cancellationToken)
    {
        var child = await childRepository.GetByIdAsync(childId, userId, cancellationToken);
        if (child is null)
        {
            throw new InvalidOperationException("Child not found.");
        }
    }

    private static StoryPathAchievementDto MapAchievement(StoryPathAchievement achievement)
    {
        var label = AchievementLabels.TryGetValue(achievement.Theme, out var value)
            ? value
            : achievement.AchievementKey;
        return new StoryPathAchievementDto
        {
            Theme = achievement.Theme.ToString(),
            AchievementKey = achievement.AchievementKey,
            Label = label,
            EarnedAt = achievement.EarnedAt
        };
    }

    private static AdventureContentDto? DeserializeContent(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AdventureContentDto>(json);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPackReadable(AdventurePack pack, AdventureContentDto? content)
    {
        if (pack.Status == AdventurePackStatus.Failed)
        {
            return false;
        }

        var pages = content?.StoryPages ?? [];
        if (pages.Count == 0)
        {
            return false;
        }

        if (pack.Status == AdventurePackStatus.Completed)
        {
            return true;
        }

        return pack.Status == AdventurePackStatus.StoryReady &&
               pages.All(p => !string.IsNullOrWhiteSpace(p.IllustrationUrl));
    }

    private static ThemeType? GetNextTheme(ThemeType current)
    {
        var index = Array.IndexOf(ThemeOrder, current);
        if (index < 0 || index >= ThemeOrder.Length - 1)
        {
            return null;
        }

        return ThemeOrder[index + 1];
    }
}
