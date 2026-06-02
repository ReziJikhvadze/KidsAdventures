using AdventurePacks.Api.DTOs.FamilyMembers;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/family-members")]
public sealed class FamilyMembersController(
    IFamilyMemberRepository familyMemberRepository,
    IBlobStorageService blobStorageService,
    IUserContextService userContext) : ControllerBase
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedMimeTypes = ["image/jpeg", "image/png", "image/webp"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FamilyMemberResponse>>> Get(CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var members = await familyMemberRepository.GetByUserIdAsync(userId, cancellationToken);
        return Ok(members.Select(Map).ToList());
    }

    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSizeBytes)]
    public async Task<ActionResult<FamilyMemberResponse>> Create([FromForm] CreateFamilyMemberRequest request, IFormFile? photo, CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var existingCount = await familyMemberRepository.CountByChildIdAsync(request.ChildId, userId, cancellationToken);
        if (existingCount >= 6)
        {
            return BadRequest("Maximum 6 family members per child.");
        }

        var photoUrl = await UploadPhotoIfAnyAsync(userId, request.ChildId, photo, cancellationToken);
        var entity = new FamilyMember
        {
            Id = Guid.NewGuid(),
            ChildId = request.ChildId,
            Name = request.Name.Trim(),
            Relationship = request.Relationship.Trim(),
            PhotoUrl = photoUrl,
            CreatedAt = DateTime.UtcNow
        };

        await familyMemberRepository.CreateAsync(entity, cancellationToken);
        return Ok(Map(entity));
    }

    [HttpPut("{id:guid}")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSizeBytes)]
    public async Task<IActionResult> Update(Guid id, [FromForm] UpdateFamilyMemberRequest request, IFormFile? photo, CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var existing = await familyMemberRepository.GetByIdAsync(id, userId, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var photoUrl = existing.PhotoUrl;
        if (photo is not null)
        {
            photoUrl = await UploadPhotoIfAnyAsync(userId, existing.ChildId, photo, cancellationToken);
        }

        existing.Name = request.Name.Trim();
        existing.Relationship = request.Relationship.Trim();
        existing.PhotoUrl = photoUrl;

        var updated = await familyMemberRepository.UpdateAsync(existing, userId, cancellationToken);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await familyMemberRepository.DeleteAsync(id, userContext.GetUserId(), cancellationToken);
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
        var blobName = $"{userId}/children/{childId}/family/{Guid.NewGuid()}-{photo.FileName}";
        return await blobStorageService.UploadAsync(blobName, ms.ToArray(), photo.ContentType, cancellationToken);
    }

    private static FamilyMemberResponse Map(FamilyMember x) => new()
    {
        Id = x.Id,
        ChildId = x.ChildId,
        Name = x.Name,
        Relationship = x.Relationship,
        PhotoUrl = x.PhotoUrl,
        CreatedAt = x.CreatedAt
    };
}
