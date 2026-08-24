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

    /// <summary>
    /// Whether a plan was written for the printing book format — cast list, per-spread character
    /// placement, print geometry. This is the gate fulfilment and the cover path key on; it is
    /// about the format (sizes matter), not any particular prompt version, and every new version
    /// of the printing flow joins this list.
    /// </summary>
    public static bool IsPrintPlan(string? promptVersion) =>
        string.Equals(promptVersion, "v5", StringComparison.OrdinalIgnoreCase)
        || string.Equals(promptVersion, "v6", StringComparison.OrdinalIgnoreCase);
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

    /*
      Everything below belongs to the Beki format and is deliberately optional.

      Not `required`, and never made `required`: System.Text.Json throws when a required property
      is missing, and MasterBookService deserialises StoryJson written by earlier runs to resume a
      book whose process died. A required field added here would turn every in-flight book at
      deploy time into an unresumable one — paid for, half generated, unrecoverable. Absent means
      "an A5 book wrote this", and absent has to keep working forever.
    */

    /// <summary>The English title. Null for A5 books, which are written in one language.</summary>
    public string? TitleEn { get; init; }

    /// <summary>
    /// The recurring characters this particular book invented, each with one short visual
    /// description. Null for A5 books, where <see cref="CharacterLock"/> carries every character
    /// in one paragraph and no character has an identity of its own.
    /// </summary>
    public IReadOnlyList<StoryCastMember>? Cast { get; init; }

    /// <summary>
    /// The recurring objects this particular book invented. A key object shipped as a glowing
    /// sphere in one spread and a crescent cradle in another because nothing carried its design
    /// forward. Null for A5 books.
    /// </summary>
    public IReadOnlyList<StoryObjectItem>? Objects { get; init; }
}

/// <summary>
/// One recurring object, created for this book only.
/// </summary>
public sealed record StoryObjectItem
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string VisualDescription { get; init; } = string.Empty;
}

/// <summary>
/// One recurring character, created for this book only.
///
/// The id matters more than it looks: it is what ties a character named in a spread to the first
/// accepted illustration it appeared in, which then becomes that character's anchor for every
/// later spread. Without a stable id there is nothing to key an anchor by, and continuity falls
/// back to hoping the model remembers.
/// </summary>
public sealed record StoryCastMember
{
    /// <summary>Stable within one book: char_01, char_02. Referenced by spread.characters.</summary>
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>One short, concrete sentence. Used verbatim on the character's first appearance.</summary>
    public string VisualDescription { get; init; } = string.Empty;
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

    /// <summary>
    /// The same words in English. Null for A5 books — see the note on <see cref="MasterStory"/>
    /// for why this can never become required.
    /// </summary>
    public string? TextEn { get; init; }

    /// <summary>
    /// Which characters are in this spread: "child" plus any cast ids. Null for A5 books, whose
    /// prompts describe the cast in prose rather than listing it.
    /// </summary>
    public IReadOnlyList<string>? Characters { get; init; }

    /// <summary>
    /// Which recurring objects are in this spread. Null for A5 books.
    /// </summary>
    public IReadOnlyList<string>? Objects { get; init; }
}

/// <summary>
/// What the model committed to before writing. Deliberately small: the logline, the stated
/// learning goal, the hero description and the age rationale were all asked for, stored and
/// never read by anything, and every one of them was output a reader had to wait for.
/// </summary>
public sealed record StoryConcept
{
    public required string Title { get; init; }

    /// <summary>Five to eight beats. The one piece of planning worth having written down.</summary>
    public required IReadOnlyList<string> Outline { get; init; }
}

/// <summary>
/// One illustration, planned and prompted in the same breath as the words it belongs to.
/// </summary>
/// <summary>
/// One picture, described in the only terms that change between pictures.
///
/// The model used to return the finished prompt — style, format, photograph instruction and
/// character description included — for all nine illustrations. Two thirds of a book's output
/// was those paragraphs typed out again. Everything invariant is assembled by
/// <see cref="Services.Story.IllustrationPrompt"/> now, so this holds only the scene.
/// </summary>
public sealed record IllustrationBrief
{
    /// <summary>This picture and nothing else: moment, action, feeling, place, shot, details.</summary>
    public required string Scene { get; init; }

    /// <summary>
    /// Only what would go wrong in this particular picture. The standing exclusions — identity
    /// drift, artefacts, text — are added to every prompt regardless.
    /// </summary>
    public string Avoid { get; init; } = string.Empty;
}
