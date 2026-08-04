using System.Text.Json.Serialization;

namespace AdventurePacks.Api.Domain.Beki;

/// <summary>
/// The request payload handed to the Beki Story Generator, mirroring
/// <c>story-input-v1.schema.json</c>.
///
/// Every parent-authored field here is story *data*, never an instruction: the prompts
/// state that boundary explicitly, and nothing in this object is interpolated into a
/// system message. The child's photo is deliberately absent — it belongs to the visual
/// pipeline only, so the writing model never sees a child's face.
/// </summary>
public sealed class BekiStoryInput
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "1.0";

    public required string RequestId { get; init; }
    public required string ChildName { get; init; }
    public required int Age { get; init; }

    /// <summary>One of <c>2-4</c>, <c>5-7</c>, <c>8-10</c>. Must agree with <see cref="Age"/>.</summary>
    public required string AgeBand { get; init; }

    /// <summary>girl | boy | nonbinary | not_specified</summary>
    public required string Gender { get; init; }

    public string? EyeColor { get; init; }
    public IReadOnlyList<string> Interests { get; init; } = [];
    public required string Theme { get; init; }
    public string? ExtraWish { get; init; }
    public IReadOnlyList<BekiSupportingCharacter> SelectedSupportingCharacters { get; init; } = [];
    public required int BookNumber { get; init; }

    /// <summary>first_book | continue_previous_chapter | new_adventure_same_universe | new_world_with_existing_relationships</summary>
    public required string ContinuationMode { get; init; }

    public int PageCount { get; init; } = BekiStoryConstants.PageCount;
    public string Language { get; init; } = "ka";

    /// <summary>licensed | private_test | originalize | exclude. Production default is originalize.</summary>
    public required string ThirdPartyCharacterMode { get; init; }

    public bool FearReframingAllowed { get; init; }

    /// <summary>Chosen by the backend, never by the model — see <see cref="Services.Beki.BekiCreativeSeedPool"/>.</summary>
    public BekiCreativeSeed? CreativeSeed { get; init; }

    /// <summary>Null for book 1; otherwise the distilled memory of everything already established.</summary>
    public BekiPreviousStoryMemory? PreviousStoryMemory { get; init; }
}

public sealed class BekiSupportingCharacter
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Relationship { get; init; }
    public string? Description { get; init; }
}

public sealed class BekiCreativeSeed
{
    public required string SeedId { get; init; }
    public required string Tone { get; init; }
    public required string StoryHook { get; init; }
    public required string SceneAnchor { get; init; }
}

/// <summary>
/// What the child's world already remembers. This is what turns a shelf of unrelated
/// books into one continuing series, and it is also what stops every book reaching for
/// the same glowing portal — see <see cref="RecentPlotPatternsToAvoidEn"/>.
/// </summary>
public sealed class BekiPreviousStoryMemory
{
    public string RelationshipWithBekiEn { get; init; } = string.Empty;
    public IReadOnlyList<string> KnownCompanions { get; init; } = [];
    public IReadOnlyList<string> WorldsVisited { get; init; } = [];
    public IReadOnlyList<string> WorldRulesEn { get; init; } = [];
    public IReadOnlyList<string> ImportantObjects { get; init; } = [];
    public IReadOnlyList<string> PromisesKa { get; init; } = [];
    public IReadOnlyList<string> ResolvedThreadsKa { get; init; } = [];
    public IReadOnlyList<string> OpenThreadsKa { get; init; } = [];
    public IReadOnlyList<string> RecentPlotPatternsToAvoidEn { get; init; } = [];
    public string? LastChapterHookKa { get; init; }
}
