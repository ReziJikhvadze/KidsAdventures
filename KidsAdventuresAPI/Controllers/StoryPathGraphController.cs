using AdventurePacks.Api.DTOs.StoryPath;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

/// <summary>
/// Authoring + read API for interactive story path graphs (nodes + branching choices).
/// Legacy linear chapter flow remains on <see cref="StoryPathController"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/story-path/graph")]
public sealed class StoryPathGraphController(IStoryGraphAuthoringService storyGraphAuthoringService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StoryGraphPathDto>>> List(
        [FromQuery] string? theme,
        CancellationToken cancellationToken)
    {
        var paths = await storyGraphAuthoringService.ListPathsAsync(theme, cancellationToken);
        return Ok(paths);
    }

    [HttpGet("active")]
    public async Task<ActionResult<StoryGraphPlayResponse>> GetActive(
        [FromQuery] string theme,
        [FromQuery] Guid? childId,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ThemeType>(theme, true, out var parsed))
        {
            return BadRequest("Invalid theme.");
        }

        var graph = await storyGraphAuthoringService.GetActiveGraphForPlayAsync(parsed, childId, cancellationToken);
        return graph is null ? NotFound() : Ok(graph);
    }

    [HttpGet("{pathId:guid}")]
    public async Task<ActionResult<StoryGraphDetailResponse>> Get(Guid pathId, CancellationToken cancellationToken)
    {
        var detail = await storyGraphAuthoringService.GetPathDetailAsync(pathId, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost]
    public async Task<ActionResult<StoryGraphPathDto>> Create(
        [FromBody] CreateStoryGraphPathRequest request,
        CancellationToken cancellationToken)
    {
        var path = await storyGraphAuthoringService.CreatePathAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { pathId = path.Id }, path);
    }

    [HttpPut("{pathId:guid}")]
    public async Task<ActionResult<StoryGraphPathDto>> Update(
        Guid pathId,
        [FromBody] UpdateStoryGraphPathRequest request,
        CancellationToken cancellationToken)
    {
        var path = await storyGraphAuthoringService.UpdatePathAsync(pathId, request, cancellationToken);
        return path is null ? NotFound() : Ok(path);
    }

    [HttpPost("{pathId:guid}/publish")]
    public async Task<IActionResult> Publish(Guid pathId, CancellationToken cancellationToken)
    {
        try
        {
            var published = await storyGraphAuthoringService.PublishPathAsync(pathId, cancellationToken);
            return published ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{pathId:guid}/nodes")]
    public async Task<ActionResult<StoryGraphNodeDto>> CreateNode(
        Guid pathId,
        [FromBody] UpsertStoryGraphNodeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var node = await storyGraphAuthoringService.CreateNodeAsync(pathId, request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { pathId }, node);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{pathId:guid}/nodes/{nodeId:guid}")]
    public async Task<ActionResult<StoryGraphNodeDto>> UpdateNode(
        Guid pathId,
        Guid nodeId,
        [FromBody] UpsertStoryGraphNodeRequest request,
        CancellationToken cancellationToken)
    {
        var node = await storyGraphAuthoringService.UpdateNodeAsync(pathId, nodeId, request, cancellationToken);
        return node is null ? NotFound() : Ok(node);
    }

    [HttpDelete("{pathId:guid}/nodes/{nodeId:guid}")]
    public async Task<IActionResult> DeleteNode(Guid pathId, Guid nodeId, CancellationToken cancellationToken)
    {
        var deleted = await storyGraphAuthoringService.DeleteNodeAsync(pathId, nodeId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("{pathId:guid}/choices")]
    public async Task<ActionResult<StoryGraphChoiceDto>> CreateChoice(
        Guid pathId,
        [FromBody] UpsertStoryGraphChoiceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var choice = await storyGraphAuthoringService.CreateChoiceAsync(pathId, request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { pathId }, choice);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{pathId:guid}/choices/{choiceId:guid}")]
    public async Task<ActionResult<StoryGraphChoiceDto>> UpdateChoice(
        Guid pathId,
        Guid choiceId,
        [FromBody] UpsertStoryGraphChoiceRequest request,
        CancellationToken cancellationToken)
    {
        var choice = await storyGraphAuthoringService.UpdateChoiceAsync(pathId, choiceId, request, cancellationToken);
        return choice is null ? NotFound() : Ok(choice);
    }

    [HttpDelete("{pathId:guid}/choices/{choiceId:guid}")]
    public async Task<IActionResult> DeleteChoice(Guid pathId, Guid choiceId, CancellationToken cancellationToken)
    {
        var deleted = await storyGraphAuthoringService.DeleteChoiceAsync(pathId, choiceId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>Dev/bootstrap helper: seeds the legacy 5-chapter linear path as a published graph for a theme.</summary>
    [HttpPost("seed-linear/{theme}")]
    public async Task<ActionResult<StoryGraphDetailResponse>> SeedLinear(string theme, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ThemeType>(theme, true, out var parsed))
        {
            return BadRequest("Invalid theme.");
        }

        var detail = await storyGraphAuthoringService.SeedLinearGraphAsync(parsed, cancellationToken);
        return Ok(detail);
    }
}
