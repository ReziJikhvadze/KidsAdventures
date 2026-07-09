using AdventurePacks.Api.DTOs.Children;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/children")]
public sealed class ChildrenController(
    IChildRepository childRepository,
    IBlobStorageService blobStorageService,
    IReferenceImageNormalizer referenceImageNormalizer,
    IUserContextService userContext) : ControllerBase
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedMimeTypes = ["image/jpeg", "image/png", "image/webp"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChildResponse>>> Get(CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var children = await childRepository.GetByUserIdAsync(userId, cancellationToken);
        return Ok(children.Select(Map).ToList());
    }

    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSizeBytes)]
    public async Task<ActionResult<ChildResponse>> Create([FromForm] CreateChildRequest request, IFormFile? photo, CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var childId = Guid.NewGuid();
        var photoUrl = await UploadPhotoIfAnyAsync(userId, childId, photo, cancellationToken);

        var (personalizationType, avatarConfigJson, appearanceDescription) = ResolvePersonalization(request, photoUrl);

        var child = new Child
        {
            Id = childId,
            UserId = userId,
            Name = request.Name.Trim(),
            Age = request.Age,
            PhotoUrl = photoUrl,
            PersonalizationType = personalizationType,
            AvatarConfigJson = avatarConfigJson,
            AppearanceDescription = appearanceDescription,
            CreatedAt = DateTime.UtcNow
        };

        await childRepository.CreateAsync(child, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = child.Id }, Map(child));
    }

    [HttpPut("{id:guid}")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSizeBytes)]
    public async Task<IActionResult> Update(Guid id, [FromForm] UpdateChildRequest request, IFormFile? photo, CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var existing = await childRepository.GetByIdAsync(id, userId, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var photoUrl = existing.PhotoUrl;
        if (photo is not null && photo.Length > 0)
        {
            photoUrl = await UploadPhotoIfAnyAsync(userId, id, photo, cancellationToken);
        }

        existing.Name = request.Name.Trim();
        existing.Age = request.Age;
        existing.PhotoUrl = photoUrl;

        var updated = await childRepository.UpdateAsync(existing, cancellationToken);
        return updated ? NoContent() : NotFound();
    }

    [HttpGet("{id:guid}/photo")]
    public async Task<IActionResult> GetPhoto(Guid id, CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var child = await childRepository.GetByIdAsync(id, userId, cancellationToken);
        if (child is null || string.IsNullOrWhiteSpace(child.PhotoUrl))
        {
            return NotFound();
        }

        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(child.PhotoUrl, cancellationToken);
            var contentType = child.PhotoUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : child.PhotoUrl.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                    ? "image/webp"
                    : "image/jpeg";
            return File(bytes, contentType);
        }
        catch
        {
            return NotFound();
        }
    }

    [HttpGet("{id:guid}/hero-portrait")]
    public async Task<IActionResult> GetHeroPortrait(Guid id, CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var child = await childRepository.GetByIdAsync(id, userId, cancellationToken);
        if (child is null || string.IsNullOrWhiteSpace(child.HeroPortraitUrl))
        {
            return NotFound();
        }

        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(child.HeroPortraitUrl, cancellationToken);
            return File(bytes, "image/webp");
        }
        catch
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await childRepository.DeleteAsync(id, userContext.GetUserId(), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private async Task<string?> UploadPhotoIfAnyAsync(Guid userId, Guid childId, IFormFile? photo, CancellationToken cancellationToken)
    {
        if (photo is null || photo.Length == 0)
        {
            return null;
        }

        if (photo.Length > MaxFileSizeBytes)
        {
            throw new InvalidOperationException("File too large. Max 5 MB.");
        }

        if (!AllowedMimeTypes.Contains(photo.ContentType))
        {
            throw new InvalidOperationException("Unsupported file type.");
        }

        await using var stream = photo.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        var normalized = referenceImageNormalizer.NormalizeForOpenAi(ms.ToArray(), photo.ContentType);
        var blobName = $"{userId}/children/{childId}/hero-{Guid.NewGuid()}.png";
        return await blobStorageService.UploadAsync(
            blobName,
            normalized.Bytes,
            normalized.ContentType,
            cancellationToken);
    }

    private static (string? PersonalizationType, string? AvatarConfigJson, string? AppearanceDescription) ResolvePersonalization(
        CreateChildRequest request,
        string? photoUrl)
    {
        var type = request.PersonalizationType?.Trim().ToLowerInvariant();
        if (type is not ("avatar" or "photo"))
        {
            type = photoUrl is not null ? "photo" : null;
        }

        if (type == "avatar")
        {
            var config = AvatarPromptBuilder.TryParse(request.AvatarConfigJson)
                         ?? new AvatarConfig();
            var json = AvatarPromptBuilder.Serialize(config);
            var appearance = AvatarPromptBuilder.BuildAppearanceDescription(config, request.Age);
            return ("avatar", json, appearance);
        }

        if (type == "photo" || photoUrl is not null)
        {
            return ("photo", null, null);
        }

        return (null, null, null);
    }

    private static ChildResponse Map(Child child) => new()
    {
        Id = child.Id,
        UserId = child.UserId,
        Name = child.Name,
        Age = child.Age,
        PhotoUrl = child.PhotoUrl,
        PersonalizationType = child.PersonalizationType,
        AvatarConfigJson = child.AvatarConfigJson,
        HeroPortraitUrl = child.HeroPortraitUrl,
        CreatedAt = child.CreatedAt
    };
}
