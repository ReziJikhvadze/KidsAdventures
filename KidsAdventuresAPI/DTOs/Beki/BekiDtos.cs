using AdventurePacks.Api.Domain.Beki;

namespace AdventurePacks.Api.DTOs.Beki;

/// <summary>
/// What the client sends to start a book. Deliberately smaller than
/// <see cref="BekiStoryInput"/>: the book number, continuation mode, previous memory and
/// creative seed are all derived server-side, because a client that could set them could
/// rewrite a child's series history.
/// </summary>
public sealed class CreateBekiStoryRequest
{
    /// <summary>The saved child this book is about. Drives series continuity.</summary>
    public Guid? CharacterId { get; set; }

    public string ChildName { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Gender { get; set; } = "not_specified";
    public string? EyeColor { get; set; }
    public List<string> Interests { get; set; } = [];
    public string Theme { get; set; } = string.Empty;

    /// <summary>The parent's own words. Treated as story data, never as instructions.</summary>
    public string? ExtraWish { get; set; }

    public List<BekiSupportingCharacterDto> SupportingCharacters { get; set; } = [];

    /// <summary>Omit to accept the production default of <c>originalize</c>.</summary>
    public string? ThirdPartyCharacterMode { get; set; }

    public bool FearReframingAllowed { get; set; }

    /// <summary>
    /// Client-supplied idempotency key. A retried submit returns the original book instead
    /// of generating — and paying for — a second one.
    /// </summary>
    public string? RequestId { get; set; }
}

public sealed class BekiSupportingCharacterDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>Poll-friendly status. Kept small: the client hits this every few seconds.</summary>
public sealed class BekiStoryStatusResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsReady { get; set; }
    public string? TitleKa { get; set; }
    public int BookNumber { get; set; }
    public string? FailureReason { get; set; }
    public string? ReviewStatus { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>The full book, returned only once generation has succeeded.</summary>
public sealed class BekiStoryResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public int BookNumber { get; set; }
    public BekiStoryOutput? Story { get; set; }
}
