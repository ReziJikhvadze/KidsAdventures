using AdventurePacks.Api.DTOs.Print;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

/// <summary>
/// The print-fulfilment console. Gated by <see cref="AuthorizationPolicies.Admin"/>, which
/// is granted from <c>Users.IsAdmin</c> — every route here exposes another customer's
/// name, phone number and home address.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin")]
public sealed class AdminController(IPrintOrderService printOrderService) : ControllerBase
{
    /// <summary>
    /// The work queue. <paramref name="status"/> defaults to the parcels waiting to be
    /// printed, which is what operations opens the screen to do.
    /// </summary>
    [HttpGet("print-orders")]
    public async Task<ActionResult<AdminPrintQueueResponse>> GetPrintQueue(
        [FromQuery] string? status,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var queue = await printOrderService.GetAdminQueueAsync(
            status ?? nameof(PrintOrderStatus.AwaitingPrint),
            limit <= 0 ? 50 : limit,
            cancellationToken);
        return Ok(queue);
    }

    [HttpPut("print-orders/{id:guid}/status")]
    public async Task<ActionResult<AdminPrintOrderResponse>> UpdateStatus(
        Guid id,
        [FromBody] UpdatePrintOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await printOrderService.UpdateStatusAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }
}
