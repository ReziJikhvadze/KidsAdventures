using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class StoryGraphRepository(ISqlConnectionFactory connectionFactory) : IStoryGraphRepository
{
    private const string PathColumns =
        "Id, Title, Theme, StartNodeId, Version, IsActive, CreatedAt, UpdatedAt";

    private const string NodeColumns =
        "Id, StoryPathId, NodeKey, NodeType, Title, ContentJson, ProblemJson, RequiresParentApproval, MapPositionX, MapPositionY, SortOrder, CreatedAt, UpdatedAt";

    private const string ChoiceColumns =
        "Id, StoryPathId, FromNodeId, ToNodeId, ChoiceKey, Label, ConsequenceTag, SortOrder, CreatedAt";

    public async Task<StoryPathGraph?> GetActivePathAsync(ThemeType theme, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP 1 {PathColumns}
                   FROM StoryPaths
                   WHERE Theme = @Theme AND IsActive = 1
                   ORDER BY Version DESC, UpdatedAt DESC;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<StoryPathGraphRow>(
            new CommandDefinition(sql, new { Theme = theme.ToString() }, cancellationToken: cancellationToken));
        return row is null ? null : MapPath(row);
    }

    public async Task<IReadOnlyList<StoryPathGraph>> ListPathsAsync(ThemeType? theme, CancellationToken cancellationToken)
    {
        var sql = theme is null
            ? $"""
               SELECT {PathColumns}
               FROM StoryPaths
               ORDER BY Theme, Version DESC, UpdatedAt DESC;
               """
            : $"""
               SELECT {PathColumns}
               FROM StoryPaths
               WHERE Theme = @Theme
               ORDER BY Version DESC, UpdatedAt DESC;
               """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<StoryPathGraphRow>(
            new CommandDefinition(sql, new { Theme = theme?.ToString() }, cancellationToken: cancellationToken));
        return rows.Select(MapPath).ToList();
    }

    public async Task<StoryPathGraph?> GetPathByIdAsync(Guid pathId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP 1 {PathColumns}
                   FROM StoryPaths
                   WHERE Id = @PathId;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<StoryPathGraphRow>(
            new CommandDefinition(sql, new { PathId = pathId }, cancellationToken: cancellationToken));
        return row is null ? null : MapPath(row);
    }

    public async Task<Guid> CreatePathAsync(StoryPathGraph path, CancellationToken cancellationToken)
    {
        path.Id = path.Id == Guid.Empty ? Guid.NewGuid() : path.Id;
        const string sql = """
                           INSERT INTO StoryPaths (Id, Title, Theme, StartNodeId, Version, IsActive, CreatedAt, UpdatedAt)
                           VALUES (@Id, @Title, @Theme, @StartNodeId, @Version, @IsActive, @CreatedAt, @UpdatedAt);
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            path.Id,
            path.Title,
            Theme = path.Theme.ToString(),
            path.StartNodeId,
            path.Version,
            path.IsActive,
            path.CreatedAt,
            path.UpdatedAt
        }, cancellationToken: cancellationToken));
        return path.Id;
    }

    public async Task<bool> UpdatePathAsync(StoryPathGraph path, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE StoryPaths
                           SET Title = @Title,
                               UpdatedAt = @UpdatedAt
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            path.Id,
            path.Title,
            UpdatedAt = DateTime.UtcNow
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> SetStartNodeAsync(Guid pathId, Guid startNodeId, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE StoryPaths
                           SET StartNodeId = @StartNodeId,
                               UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @PathId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { PathId = pathId, StartNodeId = startNodeId }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> PublishPathAsync(Guid pathId, ThemeType theme, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var tx = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE StoryPaths
                SET IsActive = 0, UpdatedAt = SYSUTCDATETIME()
                WHERE Theme = @Theme AND Id <> @PathId;
                """,
                new { Theme = theme.ToString(), PathId = pathId },
                transaction: tx,
                cancellationToken: cancellationToken));

            var affected = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE StoryPaths
                SET IsActive = 1, UpdatedAt = SYSUTCDATETIME()
                WHERE Id = @PathId;
                """,
                new { PathId = pathId },
                transaction: tx,
                cancellationToken: cancellationToken));

            tx.Commit();
            return affected > 0;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<bool> DeactivatePathsForThemeAsync(ThemeType theme, Guid exceptPathId, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE StoryPaths
                           SET IsActive = 0, UpdatedAt = SYSUTCDATETIME()
                           WHERE Theme = @Theme AND Id <> @ExceptPathId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Theme = theme.ToString(),
            ExceptPathId = exceptPathId
        }, cancellationToken: cancellationToken));
        return true;
    }

    public async Task<IReadOnlyList<StoryGraphNode>> GetNodesAsync(Guid pathId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT {NodeColumns}
                   FROM StoryNodes
                   WHERE StoryPathId = @PathId
                   ORDER BY SortOrder, NodeKey;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<StoryGraphNodeRow>(
            new CommandDefinition(sql, new { PathId = pathId }, cancellationToken: cancellationToken));
        return rows.Select(MapNode).ToList();
    }

    public async Task<StoryGraphNode?> GetNodeByIdAsync(Guid pathId, Guid nodeId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP 1 {NodeColumns}
                   FROM StoryNodes
                   WHERE StoryPathId = @PathId AND Id = @NodeId;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<StoryGraphNodeRow>(
            new CommandDefinition(sql, new { PathId = pathId, NodeId = nodeId }, cancellationToken: cancellationToken));
        return row is null ? null : MapNode(row);
    }

    public async Task<Guid> CreateNodeAsync(StoryGraphNode node, CancellationToken cancellationToken)
    {
        node.Id = node.Id == Guid.Empty ? Guid.NewGuid() : node.Id;
        const string sql = """
                           INSERT INTO StoryNodes
                               (Id, StoryPathId, NodeKey, NodeType, Title, ContentJson, ProblemJson,
                                RequiresParentApproval, MapPositionX, MapPositionY, SortOrder, CreatedAt, UpdatedAt)
                           VALUES
                               (@Id, @StoryPathId, @NodeKey, @NodeType, @Title, @ContentJson, @ProblemJson,
                                @RequiresParentApproval, @MapPositionX, @MapPositionY, @SortOrder, @CreatedAt, @UpdatedAt);
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            node.Id,
            node.StoryPathId,
            node.NodeKey,
            NodeType = ToDbNodeType(node.NodeType),
            node.Title,
            node.ContentJson,
            node.ProblemJson,
            node.RequiresParentApproval,
            node.MapPositionX,
            node.MapPositionY,
            node.SortOrder,
            node.CreatedAt,
            node.UpdatedAt
        }, cancellationToken: cancellationToken));
        return node.Id;
    }

    public async Task<bool> UpdateNodeAsync(StoryGraphNode node, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE StoryNodes
                           SET NodeKey = @NodeKey,
                               NodeType = @NodeType,
                               Title = @Title,
                               ContentJson = @ContentJson,
                               ProblemJson = @ProblemJson,
                               RequiresParentApproval = @RequiresParentApproval,
                               MapPositionX = @MapPositionX,
                               MapPositionY = @MapPositionY,
                               SortOrder = @SortOrder,
                               UpdatedAt = @UpdatedAt
                           WHERE Id = @Id AND StoryPathId = @StoryPathId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            node.Id,
            node.StoryPathId,
            node.NodeKey,
            NodeType = ToDbNodeType(node.NodeType),
            node.Title,
            node.ContentJson,
            node.ProblemJson,
            node.RequiresParentApproval,
            node.MapPositionX,
            node.MapPositionY,
            node.SortOrder,
            UpdatedAt = DateTime.UtcNow
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> DeleteNodeAsync(Guid pathId, Guid nodeId, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var tx = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM StoryChoices WHERE StoryPathId = @PathId AND (FromNodeId = @NodeId OR ToNodeId = @NodeId);",
                new { PathId = pathId, NodeId = nodeId },
                transaction: tx,
                cancellationToken: cancellationToken));

            var affected = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM StoryNodes WHERE StoryPathId = @PathId AND Id = @NodeId;",
                new { PathId = pathId, NodeId = nodeId },
                transaction: tx,
                cancellationToken: cancellationToken));

            tx.Commit();
            return affected > 0;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<StoryGraphChoice>> GetChoicesAsync(Guid pathId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT {ChoiceColumns}
                   FROM StoryChoices
                   WHERE StoryPathId = @PathId
                   ORDER BY SortOrder, Label;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<StoryGraphChoiceRow>(
            new CommandDefinition(sql, new { PathId = pathId }, cancellationToken: cancellationToken));
        return rows.Select(MapChoice).ToList();
    }

    public async Task<StoryGraphChoice?> GetChoiceByIdAsync(Guid pathId, Guid choiceId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP 1 {ChoiceColumns}
                   FROM StoryChoices
                   WHERE StoryPathId = @PathId AND Id = @ChoiceId;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<StoryGraphChoiceRow>(
            new CommandDefinition(sql, new { PathId = pathId, ChoiceId = choiceId }, cancellationToken: cancellationToken));
        return row is null ? null : MapChoice(row);
    }

    public async Task<Guid> CreateChoiceAsync(StoryGraphChoice choice, CancellationToken cancellationToken)
    {
        choice.Id = choice.Id == Guid.Empty ? Guid.NewGuid() : choice.Id;
        const string sql = """
                           INSERT INTO StoryChoices
                               (Id, StoryPathId, FromNodeId, ToNodeId, ChoiceKey, Label, ConsequenceTag, SortOrder, CreatedAt)
                           VALUES
                               (@Id, @StoryPathId, @FromNodeId, @ToNodeId, @ChoiceKey, @Label, @ConsequenceTag, @SortOrder, @CreatedAt);
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, choice, cancellationToken: cancellationToken));
        return choice.Id;
    }

    public async Task<bool> UpdateChoiceAsync(StoryGraphChoice choice, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE StoryChoices
                           SET FromNodeId = @FromNodeId,
                               ToNodeId = @ToNodeId,
                               ChoiceKey = @ChoiceKey,
                               Label = @Label,
                               ConsequenceTag = @ConsequenceTag,
                               SortOrder = @SortOrder
                           WHERE Id = @Id AND StoryPathId = @StoryPathId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, choice, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> DeleteChoiceAsync(Guid pathId, Guid choiceId, CancellationToken cancellationToken)
    {
        const string sql = """
                           DELETE FROM StoryChoices
                           WHERE StoryPathId = @PathId AND Id = @ChoiceId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { PathId = pathId, ChoiceId = choiceId }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<StoryPathGraphProgress?> GetProgressAsync(Guid childId, Guid pathId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP 1
                               Id, ChildId, StoryPathId, CurrentNodeId, VisitedNodeIdsJson,
                               ChoiceHistoryJson, ProblemResolvedJson, ParentApprovedNodeIdsJson, UpdatedAt
                           FROM StoryPathGraphProgress
                           WHERE ChildId = @ChildId AND StoryPathId = @StoryPathId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<StoryPathGraphProgress>(
            new CommandDefinition(sql, new { ChildId = childId, StoryPathId = pathId }, cancellationToken: cancellationToken));
    }

    public async Task UpsertProgressAsync(StoryPathGraphProgress progress, CancellationToken cancellationToken)
    {
        progress.Id = progress.Id == Guid.Empty ? Guid.NewGuid() : progress.Id;
        const string sql = """
                           MERGE StoryPathGraphProgress AS target
                           USING (SELECT @ChildId AS ChildId, @StoryPathId AS StoryPathId) AS source
                           ON target.ChildId = source.ChildId AND target.StoryPathId = source.StoryPathId
                           WHEN MATCHED THEN
                               UPDATE SET
                                   CurrentNodeId = @CurrentNodeId,
                                   VisitedNodeIdsJson = @VisitedNodeIdsJson,
                                   ChoiceHistoryJson = @ChoiceHistoryJson,
                                   ProblemResolvedJson = @ProblemResolvedJson,
                                   ParentApprovedNodeIdsJson = @ParentApprovedNodeIdsJson,
                                   UpdatedAt = @UpdatedAt
                           WHEN NOT MATCHED THEN
                               INSERT (Id, ChildId, StoryPathId, CurrentNodeId, VisitedNodeIdsJson,
                                       ChoiceHistoryJson, ProblemResolvedJson, ParentApprovedNodeIdsJson, UpdatedAt)
                               VALUES (@Id, @ChildId, @StoryPathId, @CurrentNodeId, @VisitedNodeIdsJson,
                                       @ChoiceHistoryJson, @ProblemResolvedJson, @ParentApprovedNodeIdsJson, @UpdatedAt);
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            progress.Id,
            progress.ChildId,
            progress.StoryPathId,
            progress.CurrentNodeId,
            progress.VisitedNodeIdsJson,
            progress.ChoiceHistoryJson,
            progress.ProblemResolvedJson,
            progress.ParentApprovedNodeIdsJson,
            UpdatedAt = DateTime.UtcNow
        }, cancellationToken: cancellationToken));
    }

    private static StoryPathGraph MapPath(StoryPathGraphRow row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Theme = Enum.Parse<ThemeType>(row.Theme),
        StartNodeId = row.StartNodeId,
        Version = row.Version,
        IsActive = row.IsActive,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };

    private static StoryGraphNode MapNode(StoryGraphNodeRow row) => new()
    {
        Id = row.Id,
        StoryPathId = row.StoryPathId,
        NodeKey = row.NodeKey,
        NodeType = ParseNodeType(row.NodeType),
        Title = row.Title,
        ContentJson = row.ContentJson,
        ProblemJson = row.ProblemJson,
        RequiresParentApproval = row.RequiresParentApproval,
        MapPositionX = row.MapPositionX,
        MapPositionY = row.MapPositionY,
        SortOrder = row.SortOrder,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };

    private static StoryGraphChoice MapChoice(StoryGraphChoiceRow row) => new()
    {
        Id = row.Id,
        StoryPathId = row.StoryPathId,
        FromNodeId = row.FromNodeId,
        ToNodeId = row.ToNodeId,
        ChoiceKey = row.ChoiceKey,
        Label = row.Label,
        ConsequenceTag = row.ConsequenceTag,
        SortOrder = row.SortOrder,
        CreatedAt = row.CreatedAt
    };

    private static StoryGraphNodeType ParseNodeType(string value) => value.ToLowerInvariant() switch
    {
        "decision" => StoryGraphNodeType.Decision,
        "problem_gate" => StoryGraphNodeType.ProblemGate,
        "campfire" => StoryGraphNodeType.Campfire,
        "parent_approval" => StoryGraphNodeType.ParentApproval,
        _ => StoryGraphNodeType.Narrative
    };

    private static string ToDbNodeType(StoryGraphNodeType type) => type switch
    {
        StoryGraphNodeType.Decision => "decision",
        StoryGraphNodeType.ProblemGate => "problem_gate",
        StoryGraphNodeType.Campfire => "campfire",
        StoryGraphNodeType.ParentApproval => "parent_approval",
        _ => "narrative"
    };

    private sealed class StoryPathGraphRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Theme { get; set; } = string.Empty;
        public Guid? StartNodeId { get; set; }
        public int Version { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class StoryGraphNodeRow
    {
        public Guid Id { get; set; }
        public Guid StoryPathId { get; set; }
        public string NodeKey { get; set; } = string.Empty;
        public string NodeType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? ContentJson { get; set; }
        public string? ProblemJson { get; set; }
        public bool RequiresParentApproval { get; set; }
        public decimal? MapPositionX { get; set; }
        public decimal? MapPositionY { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class StoryGraphChoiceRow
    {
        public Guid Id { get; set; }
        public Guid StoryPathId { get; set; }
        public Guid FromNodeId { get; set; }
        public Guid ToNodeId { get; set; }
        public string ChoiceKey { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string? ConsequenceTag { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
