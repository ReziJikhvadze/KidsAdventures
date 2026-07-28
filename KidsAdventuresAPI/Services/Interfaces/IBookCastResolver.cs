namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>One member of a book's cast, in the shape generation needs.</summary>
public sealed class BookCastMember
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>Georgian relationship text for a supporting role; null for the hero.</summary>
    public string? Relationship { get; init; }

    public string? PhotoUrl { get; init; }
    public string? AppearanceDescription { get; init; }
    public string? AppearancePhotoUrl { get; init; }

    /// <summary>True when this member's row lives in <c>Characters</c> rather than the legacy tables.</summary>
    public bool IsCharacter { get; init; }
}

/// <summary>The people a book is about, hero first.</summary>
public sealed class BookCast
{
    public required BookCastMember Hero { get; init; }

    /// <summary>The child's age in whole years, which sets the reading level.</summary>
    public int HeroAge { get; init; }

    public IReadOnlyList<BookCastMember> Supporting { get; init; } = [];
}

/// <summary>
/// Resolves who a book stars.
///
/// New books cast up to three rows from <c>Characters</c>; books written before that
/// table existed cast one <c>Child</c> plus their <c>FamilyMembers</c>. Generation should
/// not have to know which era a book comes from, so the difference stops here.
/// </summary>
public interface IBookCastResolver
{
    Task<BookCast> ResolveAsync(AdventurePack book, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a freshly derived appearance description against whichever table the member
    /// came from, so the next illustration reuses it instead of paying for another vision call.
    /// </summary>
    Task CacheAppearanceAsync(
        Guid userId,
        BookCastMember member,
        string appearanceDescription,
        CancellationToken cancellationToken);
}
