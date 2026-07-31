using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Controllers;

/// <summary>
/// The operations screens: the overview tiles, the order list, and the customer list.
///
/// Every query here deliberately crosses the per-user boundary that the customer-facing
/// controllers enforce, which is precisely why it is gated behind the Admin policy and
/// kept in its own controller rather than added as a flag to the parent-facing ones.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin")]
public sealed class AdminOverviewController(IAdminReportingRepository reporting) : ControllerBase
{
    /// <summary>Tiles for the operations overview, plus the queue that needs attention.</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<AdminOverviewResponse>> Overview(
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        if (days is < 1 or > 365)
        {
            return BadRequest(new { message = "days must be between 1 and 365." });
        }

        return Ok(await reporting.GetOverviewAsync(DateTime.UtcNow.AddDays(-days), cancellationToken));
    }

    /// <summary>
    /// Order list across all customers. Paged rather than unbounded — an admin list that
    /// selects every row is fine on day one and a timeout by year two.
    /// </summary>
    [HttpGet("orders")]
    public async Task<ActionResult<AdminOrderListResponse>> Orders(
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 100);

        if (!string.IsNullOrWhiteSpace(status) &&
            !Enum.TryParse<OrderStatus>(status, ignoreCase: true, out _))
        {
            return BadRequest(new { message = "Unknown order status." });
        }

        return Ok(await reporting.GetOrdersAsync(status, search, page, pageSize, cancellationToken));
    }

    /// <summary>Parent accounts with their spend and book counts.</summary>
    [HttpGet("customers")]
    public async Task<ActionResult<AdminCustomerListResponse>> Customers(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(await reporting.GetCustomersAsync(search, page, pageSize, cancellationToken));
    }

    /// <summary>The generation queue: books that are still working, or that failed.</summary>
    [HttpGet("production")]
    public async Task<ActionResult<AdminProductionListResponse>> Production(
        [FromQuery] bool includeCompleted = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(await reporting.GetProductionAsync(includeCompleted, page, pageSize, cancellationToken));
    }
}
