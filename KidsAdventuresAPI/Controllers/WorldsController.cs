using AdventurePacks.Api.DTOs.Worlds;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Route("api/worlds")]
public sealed class WorldsController(
    IWorldProgressService worldProgressService,
    IUserContextService userContext) : ControllerBase
{
    /// <summary>The world catalogue. Anonymous, because the landing page lists it before sign-in.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<WorldResponse>>> GetCatalogue(CancellationToken cancellationToken)
    {
        return Ok(await worldProgressService.GetCatalogueAsync(cancellationToken));
    }

    /// <summary>Every hero's adventure map, for the parent dashboard.</summary>
    [Authorize]
    [HttpGet("maps")]
    public async Task<ActionResult<IReadOnlyList<AdventureMapResponse>>> GetMaps(CancellationToken cancellationToken)
    {
        return Ok(await worldProgressService.GetMapsAsync(userContext.GetUserId(), cancellationToken));
    }

    /// <summary>One hero's adventure map: which worlds are done, open, next, or still locked.</summary>
    [Authorize]
    [HttpGet("maps/{characterId:guid}")]
    public async Task<ActionResult<AdventureMapResponse>> GetMap(
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var map = await worldProgressService.GetMapAsync(userContext.GetUserId(), characterId, cancellationToken);
        return Ok(map);
    }
}
