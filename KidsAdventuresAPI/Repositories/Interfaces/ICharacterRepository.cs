namespace AdventurePacks.Api.Repositories.Interfaces;

public interface ICharacterRepository
{
    Task<IReadOnlyList<Character>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Heroes only: the children who each own an adventure map.</summary>
    Task<IReadOnlyList<Character>> GetHeroesAsync(Guid userId, CancellationToken cancellationToken);

    Task<Character?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    /// <summary>Resolves several ids at once, filtered to the owner. Used when casting a book.</summary>
    Task<IReadOnlyList<Character>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        Guid userId,
        CancellationToken cancellationToken);

    Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(Character character, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(Character character, CancellationToken cancellationToken);

    Task UpdateAppearanceCacheAsync(
        Guid id,
        Guid userId,
        string? appearanceDescription,
        string? appearancePhotoUrl,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    /// <summary>True when the character already appears in a book, which makes deletion unsafe.</summary>
    Task<bool> IsCastInAnyBookAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Ids of this account's characters that appear in at least one book. One round trip
    /// so listing the library does not ask the same question once per character.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetCastCharacterIdsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The cast of a book, ordered by billing position.</summary>
    Task<IReadOnlyList<Character>> GetByBookIdAsync(Guid bookId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the cast of a book. Order is billing order, and at most three are
    /// accepted — the same cap the <c>BookCharacters</c> position check enforces.
    /// </summary>
    Task SetBookCastAsync(Guid bookId, IReadOnlyList<Guid> characterIds, CancellationToken cancellationToken);
}
