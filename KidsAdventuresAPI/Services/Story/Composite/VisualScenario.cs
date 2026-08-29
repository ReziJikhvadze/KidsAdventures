using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// How the composite pipeline's contract objects travel.
///
/// Separate from <see cref="StoryJson"/> on purpose. That configuration camel-cases property
/// names for the story engine's own schemas; these documents are the illustration supplier's, they
/// are snake_case, and every property here names its wire form explicitly. The options are kept
/// beside the models so a caller cannot serialise a scenario with the wrong policy and produce
/// JSON that the supplied schema rejects for a reason that has nothing to do with the book.
/// </summary>
public static class CompositeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // Names come from the attributes, not from a policy: the two must never disagree.
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static readonly JsonSerializerOptions Readable = new(Options) { WriteIndented = true };
}

/// <summary>
/// What must look the same in every picture of this book.
///
/// Small deliberately. The outfit is here because the child is generated nine times from a
/// photograph and nothing else would keep the clothes the same; the recurring elements are here
/// because a story's own invented creature or object drifts between spreads when each prompt
/// describes it afresh. The contract caps the list at three — a longer one is a book description,
/// not a lock, and the image model spends its attention on the tail of it.
/// </summary>
public sealed record VisualLock
{
    [JsonPropertyName("child_outfit")]
    public string? ChildOutfit { get; init; }

    [JsonPropertyName("recurring_elements")]
    public IReadOnlyList<string>? RecurringElements { get; init; }
}

/// <summary>
/// The cover, planned as three separate things because it is printed as one continuous sheet and
/// generated as two different jobs.
///
/// <see cref="FrontChildWorldScene"/> goes to the image model. <see cref="BekiAction"/> does not —
/// it only chooses which approved Beki PNG is composited afterwards. <see cref="BackEnvironment"/>
/// is the same world with nobody in it.
/// </summary>
public sealed record VisualScenarioCover
{
    [JsonPropertyName("front_child_world_scene")]
    public string? FrontChildWorldScene { get; init; }

    [JsonPropertyName("beki_action")]
    public string? BekiAction { get; init; }

    [JsonPropertyName("back_environment")]
    public string? BackEnvironment { get; init; }
}

/// <summary>
/// One spread's plan, split along the line the whole pipeline is built on.
///
/// <see cref="ChildWorldScene"/> is sent verbatim to the image model and must not mention Beki;
/// <see cref="BekiAction"/> is read only by code, to pick a pose. The approved manual test only
/// succeeded once Beki was removed from the generated scene, and the handoff is explicit that the
/// separation must exist in the model's own output rather than being manufactured afterwards by
/// deleting the word "Beki" from a sentence.
/// </summary>
public sealed record VisualScenarioSpread
{
    [JsonPropertyName("page")]
    public int Page { get; init; }

    [JsonPropertyName("child_world_scene")]
    public string? ChildWorldScene { get; init; }

    [JsonPropertyName("beki_action")]
    public string? BekiAction { get; init; }
}

/// <summary>
/// One text-model call's whole answer: the book-level lock, the cover, and eight spreads.
///
/// Every member is nullable, which is unusual for a domain type and correct for this one. This is
/// what a model returned, before validation; a missing <c>child_outfit</c> has to be
/// representable so the validator can name it, rather than throwing inside the deserialiser and
/// costing the caller the difference between "the model got it wrong" and "our parser did".
/// </summary>
public sealed record VisualScenarioV2
{
    [JsonPropertyName("visual_lock")]
    public VisualLock? VisualLock { get; init; }

    [JsonPropertyName("cover")]
    public VisualScenarioCover? Cover { get; init; }

    [JsonPropertyName("spreads")]
    public IReadOnlyList<VisualScenarioSpread>? Spreads { get; init; }
}
