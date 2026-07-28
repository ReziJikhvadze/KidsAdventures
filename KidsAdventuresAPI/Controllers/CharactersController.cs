using AdventurePacks.Api.DTOs.Characters;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/characters")]
public sealed class CharactersController(
    ICharacterService characterService,
    ICharacterRepository characterRepository,
    IBlobStorageService blobStorageService,
    IUserContextService userContext) : ControllerBase
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CharacterResponse>>> List(CancellationToken cancellationToken)
    {
        var characters = await characterService.ListAsync(userContext.GetUserId(), cancellationToken);
        return Ok(characters);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CharacterResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var character = await characterService.GetAsync(userContext.GetUserId(), id, cancellationToken);
        return character is null ? NotFound() : Ok(character);
    }

    // The IFormFile parameter would otherwise make this multipart-only, but most characters
    // arrive without a portrait and shouldn't have to be wrapped in a multipart envelope.
    [HttpPost]
    [Consumes("multipart/form-data", "application/x-www-form-urlencoded")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSizeBytes)]
    public async Task<ActionResult<CharacterResponse>> Create(
        [FromForm] SaveCharacterRequest request,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        var character = await characterService.CreateAsync(
            userContext.GetUserId(), request, photo, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = character.Id }, character);
    }

    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data", "application/x-www-form-urlencoded")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSizeBytes)]
    public async Task<ActionResult<CharacterResponse>> Update(
        Guid id,
        [FromForm] SaveCharacterRequest request,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        var character = await characterService.UpdateAsync(
            userContext.GetUserId(), id, request, photo, cancellationToken);
        return Ok(character);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await characterService.DeleteAsync(userContext.GetUserId(), id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// Streams the portrait through the API rather than exposing the blob URL, so a
    /// child's photo is never reachable without a valid session.
    /// </summary>
    [HttpGet("{id:guid}/photo")]
    public async Task<IActionResult> GetPhoto(Guid id, CancellationToken cancellationToken)
    {
        var character = await characterRepository.GetByIdAsync(id, userContext.GetUserId(), cancellationToken);
        if (character is null || string.IsNullOrWhiteSpace(character.PhotoUrl))
        {
            return NotFound();
        }

        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(character.PhotoUrl, cancellationToken);
            return File(bytes, ContentTypeFor(character.PhotoUrl));
        }
        catch
        {
            return NotFound();
        }
    }

    private static string ContentTypeFor(string url) => url switch
    {
        _ when url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) => "image/png",
        _ when url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) => "image/webp",
        _ => "image/jpeg"
    };
}
