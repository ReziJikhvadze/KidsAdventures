using AdventurePacks.Api.DTOs.Print;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

/// <summary>The parent's side of print fulfilment: where is my parcel, and where should it go.</summary>
[ApiController]
[Authorize]
[Route("api/print-orders")]
public sealed class PrintOrdersController(
    IPrintOrderService printOrderService,
    IUserContextService userContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PrintOrderResponse>>> List(CancellationToken cancellationToken)
    {
        return Ok(await printOrderService.ListForUserAsync(userContext.GetUserId(), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PrintOrderResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var printOrder = await printOrderService.GetForUserAsync(userContext.GetUserId(), id, cancellationToken);
        return printOrder is null ? NotFound() : Ok(printOrder);
    }

    /// <summary>Corrects the delivery address, which is only possible until the parcel ships.</summary>
    [HttpPut("{id:guid}/address")]
    public async Task<ActionResult<PrintOrderResponse>> UpdateAddress(
        Guid id,
        [FromBody] ShippingAddressRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await printOrderService.UpdateAddressAsync(
            userContext.GetUserId(), id, request, cancellationToken);
        return Ok(updated);
    }
}

/// <summary>The reusable address book behind "use saved address" at checkout.</summary>
[ApiController]
[Authorize]
[Route("api/addresses")]
public sealed class AddressesController(
    IPrintOrderService printOrderService,
    IUserContextService userContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AddressResponse>>> List(CancellationToken cancellationToken)
    {
        return Ok(await printOrderService.ListAddressesAsync(userContext.GetUserId(), cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<AddressResponse>> Save(
        [FromBody] SaveAddressRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await printOrderService.SaveAddressAsync(userContext.GetUserId(), request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await printOrderService.DeleteAddressAsync(userContext.GetUserId(), id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
