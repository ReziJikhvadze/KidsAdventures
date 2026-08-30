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

    /// <summary>
    /// v2.2: where each recurring element stands on this page — the cross-spread object-state
    /// contract the supplier's audit mandated after the lantern book. The rejected book showed
    /// its key object in the child's hand a page before its discovery and again a page after it
    /// was left in the nest; nothing in the plan said either was wrong, because the plan had no
    /// words for an object's state. Null on scenarios planned before the amendment, which stay
    /// valid; every new scenario states one entry per recurring element per spread.
    /// </summary>
    [JsonPropertyName("props")]
    public IReadOnlyList<VisualScenarioProp>? Props { get; init; }
}

/// <summary>
/// One recurring element's state on one spread.
///
/// The states are the audit's own contract, plus the two a real book needs beside it. An object
/// the story picks up and leaves runs the chain NOT_FOUND → FOUND → CARRIED → PLACED →
/// NO_LONGER_CARRIED. A companion creature is AMBIENT wherever it appears — a friend is not
/// carried. ABSENT means "not in this picture" and is legal for anything at any time, because a
/// carried object can sit in a pocket for a page without the plan lying about it.
/// </summary>
public sealed record VisualScenarioProp
{
    /// <summary>The recurring element, exactly as visual_lock.recurring_elements states it.</summary>
    [JsonPropertyName("element")]
    public string? Element { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }
}

/// <summary>The prop-state vocabulary, spelled once.</summary>
public static class VisualScenarioPropStates
{
    public const string NotFound = "NOT_FOUND";
    public const string Found = "FOUND";
    public const string Carried = "CARRIED";
    public const string Placed = "PLACED";
    public const string NoLongerCarried = "NO_LONGER_CARRIED";
    public const string Ambient = "AMBIENT";
    public const string Absent = "ABSENT";

    /// <summary>The carried-object chain, in story order. Position is the state's stage.</summary>
    public static readonly IReadOnlyList<string> Chain =
        [NotFound, Found, Carried, Placed, NoLongerCarried];

    public static readonly IReadOnlyList<string> All =
        [NotFound, Found, Carried, Placed, NoLongerCarried, Ambient, Absent];

    /// <summary>States that put the element in the picture.</summary>
    public static bool Visible(string state) =>
        state is Found or Carried or Placed or Ambient;

    /// <summary>States that forbid the element from the picture outright.</summary>
    public static bool Forbidden(string state) =>
        state is NotFound or NoLongerCarried;
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
