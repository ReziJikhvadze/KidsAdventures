using System.Security.Cryptography;
using System.Text;
using AdventurePacks.Api.DTOs.Characters;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using Microsoft.Net.Http.Headers;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/characters")]
public sealed class CharactersController(
    ICharacterService characterService,
    ICharacterRepository characterRepository,
    IBlobStorageService blobStorageService,
    IReferenceImageNormalizer referenceImageNormalizer,
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
    ///
    /// Not the stored file. What is kept is a lossless PNG sized for the image model — 2.3 MB was
    /// measured on a real child — and it was being sent whole to draw an avatar the size of a
    /// thumbnail: 1.74 seconds on the parent's connection against 132 milliseconds for everything
    /// else about that child. This answers with the same picture as lossy WebP, which is what a
    /// screen needs and a fraction of the bytes; the model keeps reading the PNG server-side.
    ///
    /// It is also told to revalidate rather than to expire. A portrait changes rarely but visibly,
    /// so a browser that keeps one for an hour is showing the wrong face after a parent replaces
    /// it. The tag is the stored blob's name, which carries a fresh id per upload, so an unchanged
    /// photo costs a 304 and no picture at all.
    /// </summary>
    [HttpGet("{id:guid}/photo")]
    public async Task<IActionResult> GetPhoto(Guid id, CancellationToken cancellationToken)
    {
        var character = await characterRepository.GetByIdAsync(id, userContext.GetUserId(), cancellationToken);
        if (character is null || string.IsNullOrWhiteSpace(character.PhotoUrl))
        {
            return NotFound();
        }

        var etag = PortraitETag(character.PhotoUrl);
        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.ETag = etag.ToString();
        if (MatchesPortraitETag(Request, etag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(character.PhotoUrl, cancellationToken);
            var display = referenceImageNormalizer.NormalizeForStorageWebp(bytes, ContentTypeFor(character.PhotoUrl));
            return File(display.Bytes, display.ContentType);
        }
        catch
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Bumped whenever what this endpoint sends back changes shape — a different encoder, quality
    /// or size. The tag below is a promise that the bytes are unchanged, and the stored blob is not
    /// the bytes: browsers holding last year's rendition would keep it forever on a 304 otherwise.
    /// </summary>
    private const string PortraitRenditionVersion = "webp1";

    /// <summary>
    /// The stored blob's name and the rendition, hashed. Every upload writes a new name, so the tag
    /// changes exactly when the picture does — and hashing keeps the storage layout out of a header.
    /// </summary>
    internal static EntityTagHeaderValue PortraitETag(string photoUrl) =>
        new($"\"{Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{PortraitRenditionVersion}:{photoUrl}")))[..32]}\"");

    /// <summary>
    /// Whether the caller already holds this picture.
    ///
    /// Parsed rather than string-compared: `If-None-Match` may carry a list, may be `*`, and may
    /// come back weakened (`W/"…"`) through a proxy. A conditional GET compares weakly, and an
    /// exact match on the raw header quietly answers "changed" to all three.
    /// </summary>
    internal static bool MatchesPortraitETag(HttpRequest request, EntityTagHeaderValue etag)
    {
        var offered = request.GetTypedHeaders().IfNoneMatch;
        return offered is { Count: > 0 }
            && offered.Any(one => one.Equals(EntityTagHeaderValue.Any)
                || etag.Compare(one, useStrongComparison: false));
    }

    private static string ContentTypeFor(string url) => url switch
    {
        _ when url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) => "image/png",
        _ when url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) => "image/webp",
        _ => "image/jpeg"
    };
}
