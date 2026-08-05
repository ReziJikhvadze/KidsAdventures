namespace AdventurePacks.Api.Domain.Story;

/// <summary>
/// The shape of a book: spreads, not loose pages.
/// </summary>
public static class BookFormat
{
    /// <summary>Scenes in a book. Each becomes two printed pages.</summary>
    public const int SpreadCount = 8;

    /// <summary>What a reader counts: an illustration page and a text page per spread.</summary>
    public const int PageCount = SpreadCount * 2;

    /// <summary>Illustrations to generate: the cover plus one per spread.</summary>
    public const int ImageCount = SpreadCount + 1;
}

/// <summary>
/// Everything one call returns: the concept, the book and the illustration briefs, in one shape.
///
/// A single call is deliberate. When the author of the story also writes the image prompts, the
/// pictures cannot describe a different book from the words, because the same pass produced
/// both. Splitting them is what let a fox be called ბუბუ on one page and ბუ on the next: the
/// name lived only inside generated text, and nothing carried it forward.
///
/// <see cref="CharacterLock"/> is the piece that matters most. It is written once and then
/// repeated verbatim into every illustration prompt, so appearance is quoted rather than
/// remembered.
/// </summary>
public sealed record MasterStory
{
    public required StoryConcept Concept { get; init; }

    /// <summary>The book itself. Text and picture are bound together per spread.</summary>
    public required IReadOnlyList<StorySpread> Spreads { get; init; }

    /// <summary>
    /// The hero's unchanging visual description, in English, for image generation. Repeated
    /// word for word in every prompt — this is what stops hair, clothing and face drifting
    /// between pages.
    /// </summary>
    public required string CharacterLock { get; init; }

    /// <summary>The cover illustration, which carries no story text of its own.</summary>
    public required IllustrationBrief Cover { get; init; }
}

/// <summary>
/// One scene: the picture and the words that belong to it, in a single object.
///
/// They are paired here rather than kept in two lists matched by index, because a list of
/// twelve pages beside a list of twelve prompts is one off-by-one away from putting the wrong
/// picture beside the wrong words — a mistake nobody would notice until a parent did.
///
/// A spread prints as two facing pages: illustration on one side, text on the other, so text
/// never covers a face. On a phone the reader shows both on one screen, because a child should
/// never turn to a page that is only words.
/// </summary>
public sealed record StorySpread
{
    /// <summary>1-based scene number. Printed pages are (2n-1) and 2n.</summary>
    public required int Number { get; init; }

    public required string Title { get; init; }

    /// <summary>The short line under or beside the picture.</summary>
    public required string Caption { get; init; }

    /// <summary>The read-aloud text. It has a whole page, so it can breathe.</summary>
    public required string Text { get; init; }

    public required IllustrationBrief Illustration { get; init; }
}

public sealed record StoryConcept
{
    public required string Title { get; init; }

    /// <summary>The whole book in one sentence.</summary>
    public required string Logline { get; init; }

    /// <summary>The skill or value this book is built around, as it was handed to the model.</summary>
    public required string LearningGoal { get; init; }

    public required string HeroDescription { get; init; }

    /// <summary>Five to eight beats. What the model committed to before writing.</summary>
    public required IReadOnlyList<string> Outline { get; init; }

    /// <summary>Why this suits the child's age. Written for a parent to read, not for us.</summary>
    public required string AgeRationale { get; init; }
}

/// <summary>
/// One illustration, planned and prompted in the same breath as the words it belongs to.
/// </summary>
public sealed record IllustrationBrief
{
    /// <summary>Which moment of the text this picture shows.</summary>
    public required string Moment { get; init; }

    public required string Action { get; init; }
    public required string Emotion { get; init; }
    public required string Environment { get; init; }

    /// <summary>Full body, medium shot or close-up, with the camera angle.</summary>
    public required string Shot { get; init; }

    /// <summary>Details the picture must contain for the story to read without the words.</summary>
    public required IReadOnlyList<string> EssentialDetails { get; init; }

    /// <summary>
    /// The finished English prompt, with <see cref="MasterStory.CharacterLock"/> already
    /// inside it. Sent to the image model as written.
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>What must not appear. Identity drift first, artefacts second.</summary>
    public required string NegativePrompt { get; init; }
}
