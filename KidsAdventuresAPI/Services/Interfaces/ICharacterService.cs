using AdventurePacks.Api.DTOs.Characters;

namespace AdventurePacks.Api.Services.Interfaces;

public interface ICharacterService
{
    Task<IReadOnlyList<CharacterResponse>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<CharacterResponse?> GetAsync(Guid userId, Guid characterId, CancellationToken cancellationToken);

    Task<CharacterResponse> CreateAsync(
        Guid userId,
        SaveCharacterRequest request,
        IFormFile? photo,
        CancellationToken cancellationToken);

    Task<CharacterResponse> UpdateAsync(
        Guid userId,
        Guid characterId,
        SaveCharacterRequest request,
        IFormFile? photo,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid userId, Guid characterId, CancellationToken cancellationToken);

    /// <summary>
    /// Validates and stores the cast of a book: at most three characters, all owned by the
    /// caller, with the hero billed first.
    /// </summary>
    Task<IReadOnlyList<CharacterResponse>> SetBookCastAsync(
        Guid userId,
        Guid bookId,
        IReadOnlyList<Guid> characterIds,
        CancellationToken cancellationToken);
}
