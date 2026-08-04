using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Beki;
using AdventurePacks.Api.Services.Beki;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

/// <summary>
/// The Beki 12-page series format.
///
/// Separate from AdventurePacksController on purpose: this is a different product shape
/// with its own storage, pipeline and lifecycle, and it runs behind a feature flag while
/// the existing flow stays the default. Merging the two routes would make it impossible to
/// turn one off.
/// </summary>
[ApiController]
[Authorize]
[Route("api/beki/stories")]
public sealed class BekiStoriesController(
    IBekiStoryService storyService,
    IUserContextService userContext,
    IOptions<BekiOptions> options,
    ILogger<BekiStoriesController> logger) : ControllerBase
{
    private readonly BekiOptions _options = options.Value;

    /// <summary>Queues a book. Generation runs in the background; poll the status endpoint.</summary>
    [HttpPost]
    public async Task<ActionResult<BekiStoryStatusResponse>> Create(
        [FromBody] CreateBekiStoryRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "The Beki pipeline is not enabled." });
        }

        if (string.IsNullOrWhiteSpace(request.ChildName))
        {
            return BadRequest(new { message = "Child's name is required." });
        }

        // The prompts adapt vocabulary, cast size and suspense to three bands covering 2-10.
        // Outside that range there is no band to write for.
        if (request.Age is < 2 or > 10)
        {
            return BadRequest(new { message = "Beki books are written for ages 2 to 10." });
        }

        if (string.IsNullOrWhiteSpace(request.Theme))
        {
            return BadRequest(new { message = "A theme is required." });
        }

        if (request.SupportingCharacters.Count > 4)
        {
            return BadRequest(new { message = "At most 4 supporting characters." });
        }

        if (request.ExtraWish is { Length: > 1000 })
        {
            return BadRequest(new { message = "The extra wish is too long (max 1000 characters)." });
        }

        try
        {
            var status = await storyService.CreateAsync(userContext.GetUserId(), request, cancellationToken);
            return Accepted(status);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Beki story creation rejected");
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Light poll target — deliberately excludes the book body.</summary>
    [HttpGet("{id:guid}/status")]
    public async Task<ActionResult<BekiStoryStatusResponse>> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        var status = await storyService.GetStatusAsync(userContext.GetUserId(), id, cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>The full book. Returns 409 while it is still being written.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BekiStoryResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var story = await storyService.GetStoryAsync(userContext.GetUserId(), id, cancellationToken);
        if (story is null)
        {
            return NotFound();
        }

        if (story.Story is null)
        {
            return StatusCode(StatusCodes.Status409Conflict, new
            {
                message = "This book is not ready yet.",
                status = story.Status,
            });
        }

        return Ok(story);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BekiStoryStatusResponse>>> List(CancellationToken cancellationToken)
    {
        var stories = await storyService.ListAsync(userContext.GetUserId(), cancellationToken);
        return Ok(stories);
    }
}
