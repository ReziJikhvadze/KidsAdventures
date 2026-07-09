using System.Text.Json;
using AdventurePacks.Api.DTOs.StoryPath;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class StoryGraphAuthoringService(IStoryGraphRepository storyGraphRepository) : IStoryGraphAuthoringService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<ThemeType, (decimal X, decimal Y)[]> MapPositions =
        new Dictionary<ThemeType, (decimal X, decimal Y)[]>
        {
            [ThemeType.Airplanes] = [(43, 93), (49, 80), (45, 61), (49, 43), (52, 22)],
            [ThemeType.Dinosaurs] = [(37, 92), (42, 69), (48, 50), (44, 31), (46, 14)],
            [ThemeType.Space] = [(51, 90), (68, 71), (58, 53), (71, 39), (80, 22)],
            [ThemeType.Pirates] = [(18, 83), (33, 60), (50, 50), (66, 36), (82, 19)],
            [ThemeType.Animals] = [(40, 90), (44, 71), (47, 52), (54, 38), (63, 26)],
        };

    public async Task<IReadOnlyList<StoryGraphPathDto>> ListPathsAsync(string? theme, CancellationToken cancellationToken)
    {
        ThemeType? parsed = null;
        if (!string.IsNullOrWhiteSpace(theme))
        {
            if (!Enum.TryParse<ThemeType>(theme, true, out var t))
            {
                throw new InvalidOperationException("Invalid theme.");
            }

            parsed = t;
        }

        var paths = await storyGraphRepository.ListPathsAsync(parsed, cancellationToken);
        return paths.Select(MapPath).ToList();
    }

    public async Task<StoryGraphDetailResponse?> GetPathDetailAsync(Guid pathId, CancellationToken cancellationToken)
    {
        var path = await storyGraphRepository.GetPathByIdAsync(pathId, cancellationToken);
        if (path is null)
        {
            return null;
        }

        return await BuildDetailAsync(path, cancellationToken);
    }

    public async Task<StoryGraphPlayResponse?> GetActiveGraphForPlayAsync(
        ThemeType theme,
        Guid? childId,
        CancellationToken cancellationToken)
    {
        var path = await storyGraphRepository.GetActivePathAsync(theme, cancellationToken);
        if (path is null)
        {
            return null;
        }

        var detail = await BuildDetailAsync(path, cancellationToken);
        StoryGraphProgressDto? progressDto = null;

        if (childId is { } cid)
        {
            var progress = await storyGraphRepository.GetProgressAsync(cid, path.Id, cancellationToken);
            if (progress is not null)
            {
                progressDto = MapProgress(progress);
            }
        }

        return new StoryGraphPlayResponse
        {
            Graph = detail,
            Progress = progressDto,
            PathMode = "Graph"
        };
    }

    public async Task<StoryGraphPathDto> CreatePathAsync(CreateStoryGraphPathRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ThemeType>(request.Theme, true, out var theme))
        {
            throw new InvalidOperationException("Invalid theme.");
        }

        var path = new StoryPathGraph
        {
            Title = request.Title.Trim(),
            Theme = theme,
            Version = 1,
            IsActive = false
        };

        await storyGraphRepository.CreatePathAsync(path, cancellationToken);
        return MapPath(path);
    }

    public async Task<StoryGraphPathDto?> UpdatePathAsync(
        Guid pathId,
        UpdateStoryGraphPathRequest request,
        CancellationToken cancellationToken)
    {
        var path = await storyGraphRepository.GetPathByIdAsync(pathId, cancellationToken);
        if (path is null)
        {
            return null;
        }

        path.Title = request.Title.Trim();
        await storyGraphRepository.UpdatePathAsync(path, cancellationToken);
        return MapPath(path);
    }

    public async Task<bool> PublishPathAsync(Guid pathId, CancellationToken cancellationToken)
    {
        var path = await storyGraphRepository.GetPathByIdAsync(pathId, cancellationToken)
                   ?? throw new InvalidOperationException("Path not found.");

        if (path.StartNodeId is null)
        {
            throw new InvalidOperationException("Cannot publish a path without a start node.");
        }

        return await storyGraphRepository.PublishPathAsync(pathId, path.Theme, cancellationToken);
    }

    public async Task<StoryGraphNodeDto> CreateNodeAsync(
        Guid pathId,
        UpsertStoryGraphNodeRequest request,
        CancellationToken cancellationToken)
    {
        _ = await storyGraphRepository.GetPathByIdAsync(pathId, cancellationToken)
            ?? throw new InvalidOperationException("Path not found.");

        var node = new StoryGraphNode
        {
            StoryPathId = pathId,
            NodeKey = request.NodeKey.Trim(),
            NodeType = ParseNodeType(request.NodeType),
            Title = request.Title.Trim(),
            ContentJson = SerializeContent(request.Content),
            ProblemJson = SerializeProblem(request.Problem),
            RequiresParentApproval = request.RequiresParentApproval,
            MapPositionX = request.MapPositionX,
            MapPositionY = request.MapPositionY,
            SortOrder = request.SortOrder
        };

        await storyGraphRepository.CreateNodeAsync(node, cancellationToken);
        return MapNode(node, []);
    }

    public async Task<StoryGraphNodeDto?> UpdateNodeAsync(
        Guid pathId,
        Guid nodeId,
        UpsertStoryGraphNodeRequest request,
        CancellationToken cancellationToken)
    {
        var node = await storyGraphRepository.GetNodeByIdAsync(pathId, nodeId, cancellationToken);
        if (node is null)
        {
            return null;
        }

        node.NodeKey = request.NodeKey.Trim();
        node.NodeType = ParseNodeType(request.NodeType);
        node.Title = request.Title.Trim();
        node.ContentJson = SerializeContent(request.Content);
        node.ProblemJson = SerializeProblem(request.Problem);
        node.RequiresParentApproval = request.RequiresParentApproval;
        node.MapPositionX = request.MapPositionX;
        node.MapPositionY = request.MapPositionY;
        node.SortOrder = request.SortOrder;

        await storyGraphRepository.UpdateNodeAsync(node, cancellationToken);
        var choices = await storyGraphRepository.GetChoicesAsync(pathId, cancellationToken);
        var nodeChoices = choices.Where(c => c.FromNodeId == nodeId).ToList();
        return MapNode(node, nodeChoices);
    }

    public Task<bool> DeleteNodeAsync(Guid pathId, Guid nodeId, CancellationToken cancellationToken)
        => storyGraphRepository.DeleteNodeAsync(pathId, nodeId, cancellationToken);

    public async Task<StoryGraphChoiceDto> CreateChoiceAsync(
        Guid pathId,
        UpsertStoryGraphChoiceRequest request,
        CancellationToken cancellationToken)
    {
        _ = await storyGraphRepository.GetPathByIdAsync(pathId, cancellationToken)
            ?? throw new InvalidOperationException("Path not found.");

        var fromNode = await storyGraphRepository.GetNodeByIdAsync(pathId, request.FromNodeId, cancellationToken)
                       ?? throw new InvalidOperationException("From node not found.");
        var toNode = await storyGraphRepository.GetNodeByIdAsync(pathId, request.ToNodeId, cancellationToken)
                     ?? throw new InvalidOperationException("To node not found.");

        var choice = new StoryGraphChoice
        {
            StoryPathId = pathId,
            FromNodeId = fromNode.Id,
            ToNodeId = toNode.Id,
            ChoiceKey = request.ChoiceKey.Trim(),
            Label = request.Label.Trim(),
            ConsequenceTag = string.IsNullOrWhiteSpace(request.ConsequenceTag) ? null : request.ConsequenceTag.Trim(),
            SortOrder = request.SortOrder
        };

        await storyGraphRepository.CreateChoiceAsync(choice, cancellationToken);
        return MapChoice(choice);
    }

    public async Task<StoryGraphChoiceDto?> UpdateChoiceAsync(
        Guid pathId,
        Guid choiceId,
        UpsertStoryGraphChoiceRequest request,
        CancellationToken cancellationToken)
    {
        var choice = await storyGraphRepository.GetChoiceByIdAsync(pathId, choiceId, cancellationToken);
        if (choice is null)
        {
            return null;
        }

        choice.FromNodeId = request.FromNodeId;
        choice.ToNodeId = request.ToNodeId;
        choice.ChoiceKey = request.ChoiceKey.Trim();
        choice.Label = request.Label.Trim();
        choice.ConsequenceTag = string.IsNullOrWhiteSpace(request.ConsequenceTag) ? null : request.ConsequenceTag.Trim();
        choice.SortOrder = request.SortOrder;

        await storyGraphRepository.UpdateChoiceAsync(choice, cancellationToken);
        return MapChoice(choice);
    }

    public Task<bool> DeleteChoiceAsync(Guid pathId, Guid choiceId, CancellationToken cancellationToken)
        => storyGraphRepository.DeleteChoiceAsync(pathId, choiceId, cancellationToken);

    public async Task<StoryGraphDetailResponse> SeedLinearGraphAsync(ThemeType theme, CancellationToken cancellationToken)
    {
        var existing = await storyGraphRepository.GetActivePathAsync(theme, cancellationToken);
        if (existing is not null)
        {
            return await BuildDetailAsync(existing, cancellationToken);
        }

        var path = new StoryPathGraph
        {
            Title = $"{theme} Saga (Linear)",
            Theme = theme,
            Version = 1,
            IsActive = false
        };
        await storyGraphRepository.CreatePathAsync(path, cancellationToken);

        var positions = MapPositions[theme];
        var nodeIds = new Guid[5];
        for (var i = 0; i < 5; i++)
        {
            var content = new StoryNodeContent
            {
                Text = $"Chapter {i + 1} of the {theme} adventure awaits."
            };

            var node = new StoryGraphNode
            {
                StoryPathId = path.Id,
                NodeKey = $"chapter-{i}",
                NodeType = StoryGraphNodeType.Narrative,
                Title = $"Chapter {i + 1}",
                ContentJson = JsonSerializer.Serialize(content, JsonOptions),
                MapPositionX = positions[i].X,
                MapPositionY = positions[i].Y,
                SortOrder = i
            };
            nodeIds[i] = await storyGraphRepository.CreateNodeAsync(node, cancellationToken);
        }

        await storyGraphRepository.SetStartNodeAsync(path.Id, nodeIds[0], cancellationToken);
        path.StartNodeId = nodeIds[0];

        for (var i = 0; i < 4; i++)
        {
            var choice = new StoryGraphChoice
            {
                StoryPathId = path.Id,
                FromNodeId = nodeIds[i],
                ToNodeId = nodeIds[i + 1],
                ChoiceKey = "continue",
                Label = "Continue the journey",
                SortOrder = 0
            };
            await storyGraphRepository.CreateChoiceAsync(choice, cancellationToken);
        }

        await storyGraphRepository.PublishPathAsync(path.Id, theme, cancellationToken);
        path.IsActive = true;

        return await BuildDetailAsync(path, cancellationToken);
    }

    private async Task<StoryGraphDetailResponse> BuildDetailAsync(StoryPathGraph path, CancellationToken cancellationToken)
    {
        var nodes = await storyGraphRepository.GetNodesAsync(path.Id, cancellationToken);
        var choices = await storyGraphRepository.GetChoicesAsync(path.Id, cancellationToken);
        var choicesByFrom = choices.GroupBy(c => c.FromNodeId).ToDictionary(g => g.Key, g => g.ToList());

        return new StoryGraphDetailResponse
        {
            Path = MapPath(path),
            Nodes = nodes.Select(n => MapNode(n, choicesByFrom.GetValueOrDefault(n.Id, []))).ToList(),
            Choices = choices.Select(MapChoice).ToList()
        };
    }

    private static StoryGraphPathDto MapPath(StoryPathGraph path) => new()
    {
        Id = path.Id,
        Title = path.Title,
        Theme = path.Theme.ToString(),
        StartNodeId = path.StartNodeId,
        Version = path.Version,
        IsActive = path.IsActive,
        CreatedAt = path.CreatedAt,
        UpdatedAt = path.UpdatedAt
    };

    private static StoryGraphNodeDto MapNode(StoryGraphNode node, IReadOnlyList<StoryGraphChoice> outgoing) => new()
    {
        Id = node.Id,
        StoryPathId = node.StoryPathId,
        NodeKey = node.NodeKey,
        NodeType = ToApiNodeType(node.NodeType),
        Title = node.Title,
        Content = DeserializeContent(node.ContentJson),
        Problem = DeserializeProblem(node.ProblemJson),
        RequiresParentApproval = node.RequiresParentApproval,
        MapPositionX = node.MapPositionX,
        MapPositionY = node.MapPositionY,
        SortOrder = node.SortOrder,
        Choices = outgoing.Select(MapChoice).ToList()
    };

    private static StoryGraphChoiceDto MapChoice(StoryGraphChoice choice) => new()
    {
        Id = choice.Id,
        StoryPathId = choice.StoryPathId,
        FromNodeId = choice.FromNodeId,
        ToNodeId = choice.ToNodeId,
        ChoiceKey = choice.ChoiceKey,
        Label = choice.Label,
        ConsequenceTag = choice.ConsequenceTag,
        SortOrder = choice.SortOrder
    };

    private static StoryGraphProgressDto MapProgress(StoryPathGraphProgress progress)
    {
        IReadOnlyList<Guid> visited = [];
        try
        {
            visited = JsonSerializer.Deserialize<List<Guid>>(progress.VisitedNodeIdsJson, JsonOptions) ?? [];
        }
        catch
        {
            // ignore malformed JSON
        }

        return new StoryGraphProgressDto
        {
            ChildId = progress.ChildId,
            StoryPathId = progress.StoryPathId,
            CurrentNodeId = progress.CurrentNodeId,
            VisitedNodeIds = visited,
            UpdatedAt = progress.UpdatedAt
        };
    }

    private static StoryGraphNodeType ParseNodeType(string value) => value.ToLowerInvariant() switch
    {
        "decision" => StoryGraphNodeType.Decision,
        "problem_gate" => StoryGraphNodeType.ProblemGate,
        "campfire" => StoryGraphNodeType.Campfire,
        "parent_approval" => StoryGraphNodeType.ParentApproval,
        _ => StoryGraphNodeType.Narrative
    };

    private static string ToApiNodeType(StoryGraphNodeType type) => type switch
    {
        StoryGraphNodeType.Decision => "decision",
        StoryGraphNodeType.ProblemGate => "problem_gate",
        StoryGraphNodeType.Campfire => "campfire",
        StoryGraphNodeType.ParentApproval => "parent_approval",
        _ => "narrative"
    };

    private static string? SerializeContent(StoryNodeContentDto? content)
        => content is null ? null : JsonSerializer.Serialize(content, JsonOptions);

    private static string? SerializeProblem(ProblemDefinitionDto? problem)
        => problem is null ? null : JsonSerializer.Serialize(problem, JsonOptions);

    private static StoryNodeContentDto? DeserializeContent(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StoryNodeContentDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static ProblemDefinitionDto? DeserializeProblem(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProblemDefinitionDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
