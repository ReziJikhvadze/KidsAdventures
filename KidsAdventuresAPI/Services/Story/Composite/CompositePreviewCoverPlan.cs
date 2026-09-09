using System.Text.Json;
using System.Text.Json.Nodes;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>Preview owns only the cover and outfit. Cast design and spread planning belong to fulfillment.</summary>
public static class CompositePreviewCoverPlan
{
    public const string Version = "preview-cover-plan-v1";
    public const string System = """
        Plan only the preview cover of a personalized children's book from its opening story pages.
        Return one simple child outfit and a cover with front_child_world_scene, beki_action and back_environment.
        Do not analyze, inventory or design the supporting cast. Do not plan story spreads or recurring elements.
        Write clear English sentences. Refer to the protagonist as "the child" without describing the child's face or body traits.
        The outfit is simple, appropriate to the age and theme, with no logos, readable text or face covering.
        The front shows one inviting opening-story moment, with the child active, full body visible from head to shoes,
        breathing room around them and a calm lower area for the title below the face. Use a few prominent environment
        details appropriate to the approved theme. Do not reveal the ending or introduce a new supporting character.
        Do not mention Beki in the front scene: Beki is added separately. The beki_action is a short sentence naming
        Beki and an inviting gesture, with no description of her body or design. The back continues the same landscape
        without any characters. No readable text, labels, frames, panels, dimensions or printing instructions in the art.
        """;

    public static JsonElement Schema() => JsonSerializer.SerializeToElement(new
    {
        type = "object", additionalProperties = false, required = new[] { "visual_lock", "cover" },
        properties = new
        {
            visual_lock = new
            {
                type = "object", additionalProperties = false, required = new[] { "child_outfit" },
                properties = new { child_outfit = new { type = "string" } },
            },
            cover = new
            {
                type = "object", additionalProperties = false,
                required = new[] { "front_child_world_scene", "beki_action", "back_environment" },
                properties = new
                {
                    front_child_world_scene = new { type = "string" },
                    beki_action = new { type = "string" },
                    back_environment = new { type = "string" },
                },
            },
        },
    });

    public static CompositeScenarioPlan Create(VisualScenarioV2 source)
    {
        var plan = source with
        {
            VisualLock = source.VisualLock is null ? null : source.VisualLock with { RecurringElements = [] },
            Spreads = [],
        };
        var problems = VisualScenarioValidator.CoverProblems(plan);
        if (problems.Count > 0)
            throw new InvalidOperationException(string.Join(" ", problems));
        var node = JsonSerializer.SerializeToNode(plan, CompositeJson.Options)!.AsObject();
        node["planning_scope"] = Version;
        return new CompositeScenarioPlan(plan, node.ToJsonString());
    }

    public static VisualScenarioV2? TryRead(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var node = JsonNode.Parse(json);
            if (node?["planning_scope"]?.GetValue<string>() != Version) return null;
            var plan = node.Deserialize<VisualScenarioV2>(CompositeJson.Options);
            return plan is not null && plan.Spreads is { Count: 0 }
                && plan.VisualLock?.RecurringElements is { Count: 0 }
                && VisualScenarioValidator.CoverProblems(plan).Count == 0 ? plan : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    public static string ApplyToFullScenario(string json, VisualScenarioV2 preview)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node["visual_lock"]!["child_outfit"] = preview.VisualLock!.ChildOutfit;
        node["cover"] = JsonSerializer.SerializeToNode(preview.Cover, CompositeJson.Options);
        return node.ToJsonString();
    }

    public static bool MatchesFullScenario(string previewJson, string fullJson)
    {
        if (string.Equals(previewJson, fullJson, StringComparison.Ordinal)) return true;
        var preview = TryRead(previewJson);
        var full = VisualScenarioValidator.Validate(fullJson).Scenario;
        return preview is not null && full is not null
            && preview.VisualLock!.ChildOutfit == full.VisualLock!.ChildOutfit
            && preview.Cover == full.Cover;
    }
}
