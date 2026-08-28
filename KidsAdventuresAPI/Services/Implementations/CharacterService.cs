using AdventurePacks.Api.Domain;
using AdventurePacks.Api.DTOs.Characters;
using AdventurePacks.Api.Repositories.Implementations;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class CharacterService(
    ICharacterRepository characterRepository,
    IBlobStorageService blobStorageService,
    IReferenceImageNormalizer referenceImageNormalizer) : ICharacterService
{
    private const long MaxPhotoBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Not a product rule so much as an abuse ceiling: a family has a handful of people
    /// and pets, and an unbounded library would let one account fill blob storage.
    /// </summary>
    private const int MaxCharactersPerAccount = 16;

    private static readonly HashSet<string> AllowedMimeTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    public async Task<IReadOnlyList<CharacterResponse>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var characters = await characterRepository.GetByUserIdAsync(userId, cancellationToken);
        var cast = await characterRepository.GetCastCharacterIdsAsync(userId, cancellationToken);

        // One query for the whole list rather than one per child: this is what the shelf reads
        // to draw its avatars, and a family with six characters would otherwise pay six.
        var portraits = await characterRepository.GetHeroPortraitUrlsAsync(
            userId, characters.Select(character => character.Id).ToList(), cancellationToken);

        return characters
            .Select(character => Map(
                character,
                canDelete: !cast.Contains(character.Id),
                heroPortraitUrl: portraits.GetValueOrDefault(character.Id)))
            .ToList();
    }

    public async Task<CharacterResponse?> GetAsync(Guid userId, Guid characterId, CancellationToken cancellationToken)
    {
        var character = await characterRepository.GetByIdAsync(characterId, userId, cancellationToken);
        if (character is null)
        {
            return null;
        }

        var isCast = await characterRepository.IsCastInAnyBookAsync(characterId, cancellationToken);
        return Map(character, canDelete: !isCast);
    }

    public async Task<CharacterResponse> CreateAsync(
        Guid userId,
        SaveCharacterRequest request,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        var normalized = Validate(request);

        var count = await characterRepository.CountByUserIdAsync(userId, cancellationToken);
        if (count >= MaxCharactersPerAccount)
        {
            throw new InvalidOperationException(
                $"შენახულია მაქსიმალური რაოდენობის პერსონაჟი ({MaxCharactersPerAccount}). წაშალე ერთი, რომ ახალი დაამატო.");
        }

        var characterId = Guid.NewGuid();
        var photoUrl = await UploadPhotoAsync(userId, characterId, photo, cancellationToken);

        var character = new Character
        {
            Id = characterId,
            UserId = userId,
            Name = normalized.Name,
            BirthDate = normalized.BirthDate,
            Gender = normalized.Gender,
            EyeColor = normalized.EyeColor,
            CharacterType = normalized.CharacterType,
            Relationship = normalized.Relationship,
            IsPrimary = request.IsPrimary,
            PhotoUrl = photoUrl,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await characterRepository.CreateAsync(character, cancellationToken);
        return Map(character, canDelete: true);
    }

    public async Task<CharacterResponse> UpdateAsync(
        Guid userId,
        Guid characterId,
        SaveCharacterRequest request,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        var normalized = Validate(request);

        var existing = await characterRepository.GetByIdAsync(characterId, userId, cancellationToken)
                       ?? throw new KeyNotFoundException("პერსონაჟი ვერ მოიძებნა.");

        var photoUrl = existing.PhotoUrl;
        if (photo is not null && photo.Length > 0)
        {
            photoUrl = await UploadPhotoAsync(userId, characterId, photo, cancellationToken);
        }
        else if (request.RemovePhoto)
        {
            photoUrl = null;
        }

        existing.Name = normalized.Name;
        existing.BirthDate = normalized.BirthDate;
        existing.Gender = normalized.Gender;
        existing.EyeColor = normalized.EyeColor;
        existing.CharacterType = normalized.CharacterType;
        existing.Relationship = normalized.Relationship;
        existing.IsPrimary = request.IsPrimary;
        existing.PhotoUrl = photoUrl;

        if (!await characterRepository.UpdateAsync(existing, cancellationToken))
        {
            throw new KeyNotFoundException("პერსონაჟი ვერ მოიძებნა.");
        }

        // The UPDATE clears the cache in SQL when the portrait changed; mirror that here
        // so the response does not claim an appearance profile that no longer exists.
        if (photoUrl is null || !string.Equals(photoUrl, existing.AppearancePhotoUrl, StringComparison.Ordinal))
        {
            existing.AppearanceDescription = null;
            existing.AppearancePhotoUrl = null;
        }

        var isCast = await characterRepository.IsCastInAnyBookAsync(characterId, cancellationToken);
        return Map(existing, canDelete: !isCast);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid characterId, CancellationToken cancellationToken)
    {
        var existing = await characterRepository.GetByIdAsync(characterId, userId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        // Books keep a foreign key to their cast, and a delivered storybook must keep
        // naming the people in it. Removing the row would orphan the printed product.
        if (await characterRepository.IsCastInAnyBookAsync(characterId, cancellationToken))
        {
            throw new InvalidOperationException(
                "ეს პერსონაჟი უკვე მონაწილეობს შექმნილ წიგნში, ამიტომ ვერ წაიშლება.");
        }

        return await characterRepository.DeleteAsync(characterId, userId, cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterResponse>> SetBookCastAsync(
        Guid userId,
        Guid bookId,
        IReadOnlyList<Guid> characterIds,
        CancellationToken cancellationToken)
    {
        var requested = characterIds.Distinct().ToList();
        if (requested.Count == 0)
        {
            throw new InvalidOperationException("წიგნს სულ მცირე ერთი პერსონაჟი სჭირდება.");
        }

        if (requested.Count > CharacterRepository.MaxCharactersPerBook)
        {
            throw new InvalidOperationException(
                $"წიგნში მაქსიმუმ {CharacterRepository.MaxCharactersPerBook} პერსონაჟია.");
        }

        var owned = await characterRepository.GetByIdsAsync(requested, userId, cancellationToken);
        if (owned.Count != requested.Count)
        {
            // Covers both "not yours" and "does not exist" without telling the caller which.
            throw new InvalidOperationException("ზოგიერთი პერსონაჟი ვერ მოიძებნა.");
        }

        await characterRepository.SetBookCastAsync(bookId, requested, cancellationToken);

        var byId = owned.ToDictionary(character => character.Id);
        return requested.Select(id => Map(byId[id], canDelete: false)).ToList();
    }

    // -- validation ---------------------------------------------------------

    private readonly record struct NormalizedCharacter(
        string Name,
        DateOnly? BirthDate,
        string? Gender,
        string? EyeColor,
        string CharacterType,
        string? Relationship);

    private static NormalizedCharacter Validate(SaveCharacterRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new InvalidOperationException("მიუთითე პერსონაჟის სახელი.");
        }

        var type = CharacterTraits.Normalize(request.CharacterType) ?? CharacterTraits.TypeChild;
        if (!CharacterTraits.Types.Contains(type))
        {
            throw new InvalidOperationException("პერსონაჟის ტიპი არასწორია.");
        }

        var gender = CharacterTraits.Normalize(request.Gender);
        if (gender is not null && !CharacterTraits.Genders.Contains(gender))
        {
            throw new InvalidOperationException("აირჩიე, პერსონაჟი გოგოა თუ ბიჭი.");
        }

        if (gender is null && CharacterTraits.RequiresGender(type))
        {
            throw new InvalidOperationException("აირჩიე, პერსონაჟი გოგოა თუ ბიჭი.");
        }

        var eyeColor = CharacterTraits.Normalize(request.EyeColor);
        if (eyeColor is not null && !CharacterTraits.EyeColors.Contains(eyeColor))
        {
            throw new InvalidOperationException("თვალის ფერი არასწორია.");
        }

        var relationship = string.IsNullOrWhiteSpace(request.Relationship) ? null : request.Relationship.Trim();
        if (!request.IsPrimary && relationship is null)
        {
            throw new InvalidOperationException("აირჩიე, ვინ არის დამატებითი პერსონაჟი მთავარი გმირისთვის.");
        }

        var birthDate = request.BirthDate;
        if (request.IsPrimary && birthDate is null)
        {
            throw new InvalidOperationException("მიუთითე ბავშვის დაბადების თარიღი.");
        }

        if (birthDate is { } date)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (date > today)
            {
                throw new InvalidOperationException("დაბადების თარიღი მომავალში ვერ იქნება.");
            }

            if (date < today.AddYears(-120))
            {
                throw new InvalidOperationException("დაბადების თარიღი არასწორია.");
            }
        }

        return new NormalizedCharacter(name, birthDate, gender, eyeColor, type, relationship);
    }

    // -- photos -------------------------------------------------------------

    private async Task<string?> UploadPhotoAsync(
        Guid userId,
        Guid characterId,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        if (photo is null || photo.Length == 0)
        {
            return null;
        }

        if (photo.Length > MaxPhotoBytes)
        {
            throw new InvalidOperationException("ფოტო ძალიან დიდია. მაქსიმუმ 5 MB.");
        }

        if (!AllowedMimeTypes.Contains(photo.ContentType))
        {
            throw new InvalidOperationException("ფოტოს ფორმატი მხარდაუჭერელია. გამოიყენე JPEG, PNG ან WebP.");
        }

        await using var stream = photo.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        var normalized = referenceImageNormalizer.NormalizeForOpenAi(buffer.ToArray(), photo.ContentType);
        var blobName = $"{userId}/characters/{characterId}/portrait-{Guid.NewGuid()}.png";
        return await blobStorageService.UploadAsync(
            blobName,
            normalized.Bytes,
            normalized.ContentType,
            cancellationToken);
    }

    /// <summary>
    /// <paramref name="heroPortraitUrl"/> is supplied only by the list path, which is what the
    /// shelf draws from. The single-character paths leave it null rather than each paying for
    /// its own lookup for a field their callers do not render.
    /// </summary>
    private static CharacterResponse Map(
        Character character,
        bool canDelete,
        string? heroPortraitUrl = null) => new()
    {
        Id = character.Id,
        Name = character.Name,
        BirthDate = character.BirthDate,
        Age = character.AgeYears,
        Gender = character.Gender,
        EyeColor = character.EyeColor,
        CharacterType = character.CharacterType,
        Relationship = character.Relationship,
        IsPrimary = character.IsPrimary,
        PhotoUrl = character.PhotoUrl,
        HeroPortraitUrl = heroPortraitUrl,
        HasAppearanceProfile = !string.IsNullOrWhiteSpace(character.AppearanceDescription),
        CanDelete = canDelete,
        CreatedAt = character.CreatedAt,
        UpdatedAt = character.UpdatedAt
    };
}
