using AdventurePacks.Api.DTOs.StoryPath;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/story-path")]
public sealed class StoryPathController(
    IStoryPathService storyPathService,
    IStoryPathRepository storyPathRepository,
    IUserContextService userContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StoryPathOverviewResponse>> GetOverview(
        [FromQuery] Guid childId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = userContext.GetUserId();
            var result = await storyPathService.GetOverviewAsync(userId, childId, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("{theme}")]
    public async Task<ActionResult<StoryPathWorldResponse>> GetWorld(
        string theme,
        [FromQuery] Guid childId,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ThemeType>(theme, ignoreCase: true, out var themeType))
        {
            return BadRequest(new { message = "Invalid theme." });
        }

        try
        {
            var userId = userContext.GetUserId();
            var result = await storyPathService.GetWorldAsync(userId, childId, themeType, cancellationToken);
            if (result is null)
            {
                return NotFound();
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("achievements")]
    public async Task<ActionResult<IReadOnlyList<StoryPathAchievementDto>>> GetAchievements(
        [FromQuery] Guid childId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = userContext.GetUserId();
            var result = await storyPathService.GetAchievementsAsync(userId, childId, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("{theme}/campfire-prompt")]
    public async Task<ActionResult<object>> GetCampfirePrompt(
        string theme,
        [FromQuery] int nodeIndex,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ThemeType>(theme, ignoreCase: true, out var themeType))
        {
            return BadRequest(new { message = "Invalid theme." });
        }

        var prompt = await storyPathRepository.GetActiveCampfirePromptAsync(themeType, nodeIndex, cancellationToken);
        return Ok(new { prompt = prompt ?? GetFallbackPrompt(themeType, nodeIndex) });
    }

    [HttpPost("confirm-campfire")]
    public async Task<ActionResult<ConfirmCampfireResponse>> ConfirmCampfire(
        [FromBody] ConfirmCampfireRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = userContext.GetUserId();
            var result = await storyPathService.ConfirmCampfireAsync(userId, request, cancellationToken);
            if (result is null)
            {
                return BadRequest(new { message = "Node is not ready for campfire confirmation." });
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("{theme}/chapters/{chapterIndex:int}/generate")]
    public async Task<ActionResult<GenerateChapterResponse>> GenerateChapter(
        string theme,
        int chapterIndex,
        [FromBody] GenerateChapterRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ThemeType>(theme, ignoreCase: true, out var themeType))
        {
            return BadRequest(new { message = "Invalid theme." });
        }

        try
        {
            var userId = userContext.GetUserId();
            var result = await storyPathService.GenerateChapterAsync(userId, themeType, chapterIndex, request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{theme}/chapters/{chapterIndex:int}/complete")]
    public async Task<ActionResult<CompleteChapterResponse>> CompleteChapter(
        string theme,
        int chapterIndex,
        [FromBody] CompleteChapterRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ThemeType>(theme, ignoreCase: true, out var themeType))
        {
            return BadRequest(new { message = "Invalid theme." });
        }

        try
        {
            var userId = userContext.GetUserId();
            var result = await storyPathService.CompleteChapterAsync(userId, themeType, chapterIndex, request, cancellationToken);
            if (result is null)
            {
                return BadRequest(new { message = "Chapter is not ready to be completed." });
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    private static string GetFallbackPrompt(ThemeType theme, int nodeIndex)
    {
        if (nodeIndex >= 5)
        {
            return $"You finished the {theme.ToString().ToLowerInvariant()} world! What was your favorite part of the adventure?";
        }

        return $"Talk together about what happened on this page of the {theme.ToString().ToLowerInvariant()} story. What surprised you?";
    }
}
