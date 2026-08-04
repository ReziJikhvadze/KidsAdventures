using System.Text.Json.Serialization;

namespace AdventurePacks.Api.Domain.Beki;

/// <summary>
/// A complete Beki book, mirroring <c>story-output-v1.schema.json</c>.
///
/// Fields ending <c>Ka</c> are reader-facing Georgian; fields ending <c>En</c> are
/// production metadata for the visual pipeline and QA, and are never printed.
/// </summary>
public sealed class BekiStoryOutput
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    public string RequestId { get; set; } = string.Empty;
    public string TitleKa { get; set; } = string.Empty;
    public string ChildName { get; set; } = string.Empty;
    public string AgeBand { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public BekiCover Cover { get; set; } = new();

    /// <summary>Exactly 12, ordered 1..12. The cover is not one of them.</summary>
    public List<BekiStoryPage> StoryPages { get; set; } = [];

    /// <summary>Stored apart from page 12's prose so the QR layout can place it independently.</summary>
    public string Page12CtaKa { get; set; } = string.Empty;

    public BekiStoryCustomization StoryCustomization { get; set; } = new();
    public BekiContinuationMemory ContinuationMemory { get; set; } = new();

    /// <summary>Null straight out of the generator; the reviewer fills it in.</summary>
    public BekiReviewMetadata? ReviewMetadata { get; set; }
}

public sealed class BekiCover
{
    public string? SubtitleKa { get; set; }

    /// <summary>Narrative description of the cover moment. Not an image prompt.</summary>
    public string CoverSceneSummaryEn { get; set; } = string.Empty;

    public List<string> FeaturedCharacters { get; set; } = [];
}

public sealed class BekiStoryPage
{
    public int PageNumber { get; set; }

    /// <summary>The only text printed on this page.</summary>
    public string StoryTextKa { get; set; } = string.Empty;

    public string NarrativeBeatEn { get; set; } = string.Empty;

    /// <summary>Narrative metadata that later seeds the page scene spec. Not an image prompt.</summary>
    public string SceneSummaryEn { get; set; } = string.Empty;

    /// <summary>The exact cast. The visual pipeline forbids adding or removing anyone.</summary>
    public List<string> CharactersPresent { get; set; } = [];

    public bool BekiPresent { get; set; }

    /// <summary>What the child does here that matters — the audit trail for "the child is the hero".</summary>
    public string ChildAgencyEn { get; set; } = string.Empty;

    /// <summary>invitation | curiosity | choice | discovery | consequence | relationship | humor | setback | reveal | resolution | continuation_reveal</summary>
    public string PageTurnFunction { get; set; } = string.Empty;

    public string? ContinuityFromPreviousEn { get; set; }
}

public sealed class BekiStoryCustomization
{
    public bool ExtraWishUsed { get; set; }
    public string ExtraWishIntegrationEn { get; set; } = string.Empty;
    public string ThirdPartyHandlingEn { get; set; } = string.Empty;
    public List<string> SafetyAdaptationsEn { get; set; } = [];
    public List<string> ContinuityAdaptationsEn { get; set; } = [];

    /// <summary>The 3–5 pages where Beki meaningfully appears. Must match the pages flagged <c>bekiPresent</c>.</summary>
    public List<int> BekiPages { get; set; } = [];
}

/// <summary>What the next book is allowed to assume already happened.</summary>
public sealed class BekiContinuationMemory
{
    public string BookSummaryKa { get; set; } = string.Empty;
    public string WorldStateEn { get; set; } = string.Empty;
    public List<string> LocationsDiscovered { get; set; } = [];
    public List<string> CharactersIntroduced { get; set; } = [];
    public List<string> ReturningCharacters { get; set; } = [];
    public List<string> RelationshipUpdatesEn { get; set; } = [];
    public List<string> ImportantObjects { get; set; } = [];
    public List<string> ChildStrengthsShownKa { get; set; } = [];
    public List<string> PromisesMadeKa { get; set; } = [];
    public string ResolvedThreadKa { get; set; } = string.Empty;
    public List<string> OpenThreadsKa { get; set; } = [];

    /// <summary>Must be the same hook the child actually reads on page 12.</summary>
    public string NextChapterHookKa { get; set; } = string.Empty;

    /// <summary>Formulas used recently, so the next book does not reach for them again.</summary>
    public List<string> RecentPlotPatternsToAvoidEn { get; set; } = [];
}

public sealed class BekiReviewMetadata
{
    /// <summary>approved_without_changes | revised | needs_human_review</summary>
    public string Status { get; set; } = string.Empty;

    public List<BekiReviewIssue> IssuesFound { get; set; } = [];
    public List<string> ChangesMadeEn { get; set; } = [];
    public List<string> HumanReviewFlagsEn { get; set; } = [];
}

public sealed class BekiReviewIssue
{
    public string Code { get; set; } = string.Empty;

    /// <summary>low | medium | high | critical</summary>
    public string Severity { get; set; } = string.Empty;

    public int? PageNumber { get; set; }
    public string DescriptionEn { get; set; } = string.Empty;
}
