using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class BookCastResolver(
    ICharacterRepository characterRepository,
    IChildRepository childRepository,
    IFamilyMemberRepository familyMemberRepository) : IBookCastResolver
{
    /// <summary>Used when a character has no birth date, which only legacy rows should hit.</summary>
    private const int FallbackHeroAge = 6;

    public async Task<BookCast> ResolveAsync(AdventurePack book, CancellationToken cancellationToken)
    {
        if (book.PrimaryCharacterId is { } heroId)
        {
            var cast = await ResolveFromCharactersAsync(book, heroId, cancellationToken);
            if (cast is not null)
            {
                return cast;
            }
        }

        return await ResolveFromLegacyAsync(book, cancellationToken);
    }

    public async Task CacheAppearanceAsync(
        Guid userId,
        BookCastMember member,
        string appearanceDescription,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(member.PhotoUrl))
        {
            return;
        }

        if (member.IsCharacter)
        {
            await characterRepository.UpdateAppearanceCacheAsync(
                member.Id, userId, appearanceDescription, member.PhotoUrl, cancellationToken);
            return;
        }

        // Legacy rows: only the child carries a cache; family photos are described per run,
        // which is what the old model did and is not worth back-porting a column for.
        await childRepository.UpdateAppearanceCacheAsync(
            member.Id, userId, appearanceDescription, member.PhotoUrl, cancellationToken);
    }

    private async Task<BookCast?> ResolveFromCharactersAsync(
        AdventurePack book,
        Guid heroId,
        CancellationToken cancellationToken)
    {
        var members = await characterRepository.GetByBookIdAsync(book.Id, cancellationToken);

        // A book whose cast row never got written still has its hero on the pack itself,
        // so fall back to that rather than failing the generation.
        var hero = members.FirstOrDefault(member => member.Id == heroId)
                   ?? await characterRepository.GetByIdAsync(heroId, book.UserId, cancellationToken);

        if (hero is null)
        {
            return null;
        }

        var supporting = members
            .Where(member => member.Id != hero.Id)
            .Select(ToMember)
            .ToList();

        return new BookCast
        {
            Hero = ToMember(hero),
            HeroAge = hero.AgeYears ?? FallbackHeroAge,
            Supporting = supporting
        };
    }

    private async Task<BookCast> ResolveFromLegacyAsync(AdventurePack book, CancellationToken cancellationToken)
    {
        var childId = book.ChildId
                      ?? throw new InvalidOperationException("წიგნს მთავარი გმირი არ აქვს მიბმული.");

        var child = await childRepository.GetByIdAsync(childId, book.UserId, cancellationToken)
                    ?? throw new InvalidOperationException("მთავარი გმირი ვერ მოიძებნა.");

        var familyMembers = await familyMemberRepository.GetByChildIdAsync(childId, book.UserId, cancellationToken);

        return new BookCast
        {
            Hero = new BookCastMember
            {
                Id = child.Id,
                Name = child.Name,
                Relationship = null,
                PhotoUrl = child.PhotoUrl,
                AppearanceDescription = child.AppearanceDescription,
                AppearancePhotoUrl = child.AppearancePhotoUrl,
                IsCharacter = false
            },
            HeroAge = child.Age,
            Supporting = familyMembers.Select(member => new BookCastMember
            {
                Id = member.Id,
                Name = member.Name,
                Relationship = member.Relationship,
                PhotoUrl = member.PhotoUrl,
                AppearanceDescription = null,
                AppearancePhotoUrl = null,
                IsCharacter = false
            }).ToList()
        };
    }

    private static BookCastMember ToMember(Character character) => new()
    {
        Id = character.Id,
        Name = character.Name,
        Relationship = character.IsPrimary ? null : character.Relationship,
        PhotoUrl = character.PhotoUrl,
        AppearanceDescription = character.AppearanceDescription,
        AppearancePhotoUrl = character.AppearancePhotoUrl,
        IsCharacter = true
    };
}
