using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

/// <summary>
/// The age x theme story matrix. Gated by <see cref="AuthorizationPolicies.Admin"/> —
/// these rows steer what every child is told, so they are not editable by a parent account.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin/story-rules")]
public sealed class AdminStoryRulesController(
    IStoryRuleRepository storyRuleRepository,
    IUserContextService userContext) : ControllerBase
{
    /// <summary>
    /// The whole grid in one call — the admin screen renders bands x themes and needs every
    /// cell, including the untuned ones, to give each a stable id to save against.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<StoryRuleMatrixResponse>> Get(CancellationToken cancellationToken)
    {
        var rules = await storyRuleRepository.GetAllAsync(cancellationToken);

        return Ok(new StoryRuleMatrixResponse
        {
            AgeBands = StoryAgeBands.All,
            Themes = Enum.GetNames<ThemeType>(),
            Cells = rules.Select(ToResponse).ToList()
        });
    }

    /// <summary>
    /// Updates one cell. Clearing a field (sending null) hands that aspect back to the
    /// built-in age guidance rather than pinning it to a default.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StoryRuleResponse>> Update(
        Guid id,
        [FromBody] UpdateStoryRuleRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await storyRuleRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (request.ScarinessLimit is { } scariness && scariness is < 0 or > 3)
        {
            return BadRequest(new { message = "ScarinessLimit must be between 0 and 3." });
        }

        if (request.MaxWordsPerPage is { } words && words <= 0)
        {
            return BadRequest(new { message = "MaxWordsPerPage must be greater than zero." });
        }

        if (request.MaxSentenceWords is { } sentence && sentence <= 0)
        {
            return BadRequest(new { message = "MaxSentenceWords must be greater than zero." });
        }

        if (!string.IsNullOrWhiteSpace(request.VocabularyLevel) &&
            request.VocabularyLevel.Trim().ToLowerInvariant() is not ("simple" or "standard" or "rich"))
        {
            return BadRequest(new { message = "VocabularyLevel must be simple, standard or rich." });
        }

        existing.MaxWordsPerPage = request.MaxWordsPerPage;
        existing.MaxSentenceWords = request.MaxSentenceWords;
        existing.VocabularyLevel = string.IsNullOrWhiteSpace(request.VocabularyLevel)
            ? null
            : request.VocabularyLevel.Trim().ToLowerInvariant();
        existing.ScarinessLimit = request.ScarinessLimit;
        existing.ExtraGuidance = string.IsNullOrWhiteSpace(request.ExtraGuidance)
            ? null
            : request.ExtraGuidance.Trim();
        existing.IsActive = request.IsActive;
        existing.UpdatedByUserId = userContext.GetUserId();

        await storyRuleRepository.UpdateAsync(existing, cancellationToken);

        var refreshed = await storyRuleRepository.GetByIdAsync(id, cancellationToken) ?? existing;
        return Ok(ToResponse(refreshed));
    }

    private static StoryRuleResponse ToResponse(StoryRule rule) => new()
    {
        Id = rule.Id,
        AgeBand = rule.AgeBand,
        Theme = rule.Theme,
        MaxWordsPerPage = rule.MaxWordsPerPage,
        MaxSentenceWords = rule.MaxSentenceWords,
        VocabularyLevel = rule.VocabularyLevel,
        ScarinessLimit = rule.ScarinessLimit,
        ExtraGuidance = rule.ExtraGuidance,
        IsActive = rule.IsActive,
        UpdatedAt = rule.UpdatedAt
    };
}
