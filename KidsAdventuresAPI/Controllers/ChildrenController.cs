using AdventurePacks.Api.DTOs.Children;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/children")]
public sealed class ChildrenController(
    IChildRepository childRepository,
    IBlobStorageService blobStorageService,
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

        var child = new Child
        {
            Id = childId,
            UserId = userId,
            Name = request.Name.Trim(),
            Age = request.Age,
            PhotoUrl = photoUrl,
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
        var blobName = $"{userId}/children/{childId}/hero-{Guid.NewGuid()}-{photo.FileName}";
        return await blobStorageService.UploadAsync(blobName, ms.ToArray(), photo.ContentType, cancellationToken);
    }

    private static ChildResponse Map(Child child) => new()
    {
        Id = child.Id,
        UserId = child.UserId,
        Name = child.Name,
        Age = child.Age,
        PhotoUrl = child.PhotoUrl,
        CreatedAt = child.CreatedAt
    };
}
