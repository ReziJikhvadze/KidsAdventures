using AdventurePacks.Api.DTOs.Children;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/children")]
public sealed class ChildrenController(
    IChildRepository childRepository,
    IUserContextService userContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChildResponse>>> Get(CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var children = await childRepository.GetByUserIdAsync(userId, cancellationToken);
        return Ok(children.Select(Map).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ChildResponse>> Create([FromBody] CreateChildRequest request, CancellationToken cancellationToken)
    {
        var child = new Child
        {
            Id = Guid.NewGuid(),
            UserId = userContext.GetUserId(),
            Name = request.Name.Trim(),
            Age = request.Age,
            CreatedAt = DateTime.UtcNow
        };

        await childRepository.CreateAsync(child, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = child.Id }, Map(child));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateChildRequest request, CancellationToken cancellationToken)
    {
        var updated = await childRepository.UpdateAsync(new Child
        {
            Id = id,
            UserId = userContext.GetUserId(),
            Name = request.Name.Trim(),
            Age = request.Age
        }, cancellationToken);

        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await childRepository.DeleteAsync(id, userContext.GetUserId(), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private static ChildResponse Map(Child child) => new()
    {
        Id = child.Id,
        UserId = child.UserId,
        Name = child.Name,
        Age = child.Age,
        CreatedAt = child.CreatedAt
    };
}
