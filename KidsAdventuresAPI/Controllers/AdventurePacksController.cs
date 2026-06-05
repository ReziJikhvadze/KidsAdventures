using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/adventure-packs")]
public sealed class AdventurePacksController(
    IAdventureGenerationService generationService,
    IAdventurePackRepository adventurePackRepository,
    IBlobStorageService blobStorageService,
    IUserContextService userContext,
    ILogger<AdventurePacksController> logger) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<ActionResult<object>> Generate([FromBody] GenerateAdventurePackRequest request, CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var packId = await generationService.QueueGenerationAsync(userId, request, cancellationToken);
        return Accepted(new { id = packId, status = AdventurePackStatus.Pending.ToString() });
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdventurePackResponse>>> Get(CancellationToken cancellationToken)
    {
        var rows = await adventurePackRepository.GetByUserIdAsync(userContext.GetUserId(), cancellationToken);
        return Ok(rows.Select(Map).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdventurePackResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var row = await adventurePackRepository.GetByIdAsync(id, userContext.GetUserId(), cancellationToken);
        return row is null ? NotFound() : Ok(Map(row));
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var row = await adventurePackRepository.GetByIdAsync(id, userContext.GetUserId(), cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        if (row.Status != AdventurePackStatus.Completed || string.IsNullOrWhiteSpace(row.PdfUrl))
        {
            return BadRequest("Pack is not ready.");
        }

        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(row.PdfUrl, cancellationToken);
            var fileName = $"adventure-pack-{row.Id}.pdf";
            return File(bytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PDF blob missing for pack {PackId}. Stored url: {PdfUrl}", id, row.PdfUrl);
            return NotFound("PDF file was not found in storage. Please generate a new pack.");
        }
    }

    private static AdventurePackResponse Map(AdventurePack x) => new()
    {
        Id = x.Id,
        UserId = x.UserId,
        ChildId = x.ChildId,
        Theme = x.Theme,
        Status = x.Status,
        PdfUrl = x.PdfUrl,
        ProgressMessage = x.ProgressMessage,
        ErrorMessage = x.ErrorMessage,
        StoryLanguage = x.StoryLanguage,
        CreatedAt = x.CreatedAt
    };
}
