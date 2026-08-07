using System.Text.Json.Serialization;

namespace AdventurePacks.Api.DTOs.AdventurePacks;

public class AdventurePackResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ThemeType Theme { get; set; }
    public AdventurePackStatus Status { get; set; }
    public string? PdfUrl { get; set; }
    public string? ProgressMessage { get; set; }

    /// <summary>0-100 while a job is running, null otherwise. Drives the loader bar.</summary>
    public int? ProgressPercent { get; set; }

    public string? ErrorMessage { get; set; }
    public string? StoryLanguage { get; set; }
    public PreviewIllustrationStatus PreviewIllustrationStatus { get; set; }
    public int StoryPageCount { get; set; }
    public bool IsWelcomeGiftStory { get; set; }
    public DateTime CreatedAt { get; set; }

    // -- book model --------------------------------------------------------

    public string? WorldId { get; set; }
    public Guid? PrimaryCharacterId { get; set; }
    public Guid? SeriesId { get; set; }
    public int SequenceNumber { get; set; }
    public Guid? ContinuesFromBookId { get; set; }
    public BookAccessLevel AccessLevel { get; set; }

    /// <summary>False while only the free sample is readable.</summary>
    public bool IsUnlocked { get; set; }

    public bool HasPrintEntitlement { get; set; }
    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// The book's own title. The library list renders this on every card, so it belongs on
    /// the list shape and not only on the detail one — without it a card falls back to a
    /// generic world title with a placeholder hero name.
    /// </summary>
    public string? Title { get; set; }
}

public sealed class AdventurePackDetailResponse : AdventurePackResponse
{
    public string? ChildName { get; set; }

    /// <summary>
    /// Every page of the story, including the ones past the free allowance. Pages beyond it
    /// are flagged <see cref="StoryPageContentDto.IsLocked"/> and carry no illustration.
    /// </summary>
    public List<StoryPageContentDto> StoryPages { get; set; } = [];

    /// <summary>How many of the returned pages are locked (illustration withheld).</summary>
    public int LockedPageCount { get; set; }

    /// <summary>
    /// True when this book is eight spreads rather than pages that each carry art and text.
    /// The reader needs it at the book level: a page from an older book deserialises with
    /// IsTextOnlyPage false, which is indistinguishable from the illustration side of a spread,
    /// and treating one as the other would strip every older book of its words.
    /// </summary>
    public bool IsSpreadBook { get; set; }
}

/// <summary>
/// Result of the free, no-login teaser: a cover image, the title, and the first page — plus the full
/// story JSON so it can be saved to the account verbatim after the parent signs in.
/// </summary>
public sealed class GuestPreviewResult
{
    public string Title { get; set; } = string.Empty;
    public string ChildName { get; set; } = string.Empty;
    public string FirstPageTitle { get; set; } = string.Empty;
    public string FirstPageText { get; set; } = string.Empty;
    public string CoverImageDataUrl { get; set; } = string.Empty;
    public ThemeType Theme { get; set; }

    /// <summary>Server-side id of this teaser; sent back during sign-up so the welcome gift is trustable.</summary>
    public Guid GuestPreviewId { get; set; }

    /// <summary>Identity of the generated story; fallback link for entitlement when the previewId is lost.</summary>
    public Guid StoryId { get; set; }

    /// <summary>Serialized AdventureContentDto for the whole story, replayed into the account on sign-in.</summary>
    public string StoryJson { get; set; } = string.Empty;
}

public sealed class StoryPageContentDto
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Short evocative phrase (3-8 words) shown overlaid on the illustration.</summary>
    public string Caption { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
    public string? IllustrationUrl { get; set; }
    public bool IsIllustrated { get; set; }

    /// <summary>
    /// True for the prose half of a spread, which prints facing a picture rather than over one.
    /// The reader needs this: it draws art and copy on the same page, so without it a page whose
    /// only text is a caption puts that caption across the illustration.
    /// </summary>
    public bool IsTextOnlyPage { get; set; }

    /// <summary>
    /// True for a page past the free preview allowance. The text is still returned — a
    /// preview is meant to read as a real book, and the story costs the same to generate
    /// either way — but the illustration is withheld and the reader blurs the artwork.
    /// Note this makes the story text readable to anyone who inspects the response;
    /// the illustrations, not the prose, are what the purchase gates.
    /// </summary>
    public bool IsLocked { get; set; }
}

public sealed class MasterStoryRunStartedDto
{
    [JsonPropertyName("runId")]
    public Guid RunId { get; set; }
}

/// <summary>
/// What the browser sees while a book is being written, and what it gets when one is finished.
/// </summary>
public sealed class MasterStoryRunStatusDto
{
    [JsonPropertyName("runId")]
    public Guid RunId { get; set; }

    /// <summary>Pending | Writing | Illustrating | Ready | Failed</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("progressMessage")]
    public string? ProgressMessage { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("childName")]
    public string? ChildName { get; set; }

    [JsonPropertyName("coverImageUrl")]
    public string? CoverImageUrl { get; set; }

    [JsonPropertyName("firstPageTitle")]
    public string? FirstPageTitle { get; set; }

    [JsonPropertyName("firstPageText")]
    public string? FirstPageText { get; set; }

    /// <summary>Sixteen, for a book of eight spreads.</summary>
    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }
}

public sealed class AdventureContentDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = string.Empty;

    [JsonPropertyName("childName")]
    public string ChildName { get; set; } = string.Empty;

    [JsonPropertyName("storyPages")]
    public List<StoryPageDto> StoryPages { get; set; } = [];

    /// <summary>
    /// The hero's unchanging visual description, quoted verbatim into every illustration prompt.
    /// Stored with the book rather than recomputed, so a picture redrawn months later matches the
    /// ones beside it.
    /// </summary>
    [JsonPropertyName("characterLock")]
    public string? CharacterLock { get; set; }

    /// <summary>
    /// Left over from a format that had worksheets and a certificate. Nothing writes or reads
    /// them, and they were being serialised into every stored book as an empty list and an empty
    /// object. Omitted when empty so a new book stops carrying them.
    /// </summary>
    [JsonPropertyName("activities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<ActivityDto>? Activities { get; set; }

    [JsonPropertyName("certificate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CertificateDto? Certificate { get; set; }
}

public sealed class StoryPageDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Short evocative phrase (3-8 words) shown overlaid on the illustration.</summary>
    [JsonPropertyName("caption")]
    public string Caption { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("illustrationUrl")]
    public string? IllustrationUrl { get; set; }

    /// <summary>
    /// True for the prose half of a spread, which prints facing a picture and carries none of
    /// its own. Named for the exception rather than the rule on purpose: books written before
    /// spreads existed have no such field, and absent must keep meaning "illustrate this page"
    /// or every book already in the library would lose its artwork.
    /// </summary>
    [JsonPropertyName("isTextOnlyPage")]
    public bool IsTextOnlyPage { get; set; }

    /// <summary>
    /// The illustration prompt written by the same pass that wrote the story, with the character
    /// lock already inside it. Present only on books from the master call; when it is missing the
    /// prompt is rebuilt from the page, which is how every earlier book was drawn.
    /// </summary>
    [JsonPropertyName("imagePrompt")]
    public string? ImagePrompt { get; set; }

    [JsonPropertyName("negativePrompt")]
    public string? NegativePrompt { get; set; }

    /// <summary>Illustration bytes (set after OpenAI image generation; not part of story JSON).</summary>
    [JsonIgnore]
    public byte[]? ImageBytes { get; set; }
}

public sealed class ActivityDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public sealed class CertificateDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
