using System.Security.Cryptography;
using System.Text;

namespace AdventurePacks.Api.Domain.Story;

/// <summary>
/// The single source of truth for one book.
///
/// One rule governs the whole engine and is worth stating plainly: no stage may read anything
/// except this object. No stage re-derives a fact from prose. That constraint is what stops a
/// key existing in the text but not in the picture, and it is the difference between an engine
/// that remembers and one that is merely reminded.
/// </summary>
public sealed class BookState
{
    public required BookMeta Meta { get; init; }
    public required CastingBible Casting { get; init; }
    public required Inspiration Inspiration { get; init; }

    /// <summary>Carried in from earlier books in this child's series. Empty for a first book.</summary>
    public StoryMemory Memory { get; init; } = StoryMemory.Empty;

    public StoryBlueprint? Blueprint { get; set; }
    public IReadOnlyList<WrittenPage> Pages { get; set; } = [];
    public IReadOnlyList<ScenePlan> Scenes { get; set; } = [];
    public StoryAnalytics Analytics { get; set; } = new();
}

public sealed class BookMeta
{
    public required Guid BookId { get; init; }
    public required Guid ChildId { get; init; }
    public required string Language { get; init; }
    public required string WorldId { get; init; }
    public required int PageCount { get; init; }
    public required int ChildAge { get; init; }

    /// <summary>Which book this is in the child's series. 1 for a first adventure.</summary>
    public int ChapterNumber { get; init; } = 1;

    /// <summary>Engine version that produced this book, so old books stay readable after changes.</summary>
    public required string EngineVersion { get; init; }
}

/// <summary>One page of finished prose. The writer owns these and nothing else.</summary>
public sealed class WrittenPage
{
    public required int Page { get; init; }
    public required string Title { get; init; }

    /// <summary>The short line printed on the illustration.</summary>
    public required string Caption { get; init; }

    /// <summary>The read-aloud text.</summary>
    public required string Content { get; init; }
}

/// <summary>
/// An illustration, planned as structure rather than as a sentence.
///
/// Stored as fields and rendered to a prompt at the very end. A string would be unreviewable
/// and undiffable; this can be inspected field by field, and the vision reviewer can be handed
/// exactly the checklist it needs.
/// </summary>
public sealed class ScenePlan
{
    public required int Page { get; init; }
    public required string SceneDescription { get; init; }
    public required string Camera { get; init; }
    public required string Composition { get; init; }
    public required string Lighting { get; init; }
    public required string Mood { get; init; }

    public required IReadOnlyList<string> Foreground { get; init; }
    public required IReadOnlyList<string> Midground { get; init; }
    public required IReadOnlyList<string> Background { get; init; }

    /// <summary>Character ids that must be visible, taken from state rather than from the prose.</summary>
    public required IReadOnlyList<string> Characters { get; init; }

    /// <summary>Object ids that must be visible. This is why the key stays in the picture.</summary>
    public required IReadOnlyList<string> Props { get; init; }

    public required StoryEmotion Emotion { get; init; }
    public required string Action { get; init; }

    /// <summary>Which edge is left calm for the caption. Alternates so text never covers a face twice running.</summary>
    public required string TextSafeBand { get; init; }

    /// <summary>
    /// Fingerprint of everything that must not change about the hero's look on this page. The
    /// vision reviewer checks the image against these components, and a change between adjacent
    /// pages without a wardrobe beat is a blueprint bug caught before anything is drawn.
    /// </summary>
    public required VisualHash Hero { get; init; }
}

/// <summary>
/// A hash of the visual facts that must hold, plus the components that produced it so a failure
/// can say what actually differs rather than only that something did.
/// </summary>
public sealed record VisualHash(string Value, IReadOnlyList<string> Components)
{
    public static VisualHash For(StoryCharacter character, Outfit outfit)
    {
        var components = new List<string>
        {
            $"hair:{character.Appearance.HairColor.Trim().ToLowerInvariant()}",
            $"hairstyle:{character.Appearance.HairStyle.Trim().ToLowerInvariant()}",
            $"eyes:{character.Appearance.EyeColor.Trim().ToLowerInvariant()}",
            $"skin:{character.Appearance.SkinTone.Trim().ToLowerInvariant()}",
            outfit.ToCanonicalString()
        };

        var joined = string.Join("|", components);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return new VisualHash(Convert.ToHexString(bytes)[..12], components);
    }
}

/// <summary>
/// What the engine learned making this book.
///
/// Persisted for every generation so that prompt and rule changes can be judged against
/// thousands of real books instead of an impression. Without it, "the stories feel weak" stays
/// an opinion; with it, it becomes a number that moved.
/// </summary>
public sealed class StoryAnalytics
{
    public int PlannerRepairCount { get; set; }
    public int WriterRepairCount { get; set; }
    public int SceneRepairCount { get; set; }

    /// <summary>Every rule that fired, including ones that were repaired away.</summary>
    public List<string> RuleFailures { get; set; } = [];

    /// <summary>Pages the craft reviewer ranked lowest, worst first.</summary>
    public List<int> WeakestPages { get; set; } = [];

    /// <summary>Would a child ask for this page again, 0-10, averaged across pages.</summary>
    public double? DelightScore { get; set; }

    /// <summary>Share of pages containing spoken dialogue.</summary>
    public double DialogueRatio { get; set; }

    /// <summary>Share of pages carrying a joke or a piece of physical comedy.</summary>
    public double HumorDensity { get; set; }

    public Dictionary<string, int> EmotionDistribution { get; set; } = [];
    public Dictionary<string, int> PurposeDistribution { get; set; } = [];
    public Dictionary<string, int> EnergyDistribution { get; set; } = [];

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public long TotalMilliseconds { get; set; }

    /// <summary>Craft rules that were still failing when the book shipped. Tier two never blocks.</summary>
    public List<string> ShippedWithWarnings { get; set; } = [];
}
