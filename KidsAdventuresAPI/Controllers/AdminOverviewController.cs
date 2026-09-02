using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Repositories.Implementations;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Controllers;

/// <summary>
/// The screen the console opens on: is anybody waiting on me right now?
///
/// This used to be a redirect to the orders list, which answers a different question. A list says
/// what happened to one family's book; it does not say that three books have been silent for forty
/// minutes and a parcel has been sitting in the print queue since Friday. An operator who has to
/// assemble that by paging through filters does it on the days they remember to.
///
/// Every tile is a door. The count and the list it navigates to come from the same predicates —
/// the book states from one SQL statement here, the recent rows from the orders repository's own
/// needs-attention filter — because a dashboard whose figure disagrees with the screen behind it
/// teaches an operator to trust neither.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin")]
public sealed class AdminOverviewController(
    IAdminOverviewRepository overview,
    IAdminReportingRepository reporting,
    IOptions<BekiOptions> bekiOptions,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>
    /// How many of the attention rows the panel carries. Eight, because this is a summary with a
    /// link to the full list underneath it, and a summary that needs scrolling is the list.
    /// </summary>
    private const int RecentAttentionRows = 8;

    [HttpGet("overview")]
    public async Task<ActionResult<AdminOverviewResponse>> Overview(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // UTC calendar boundaries, and the console's tile says "(UTC)" out loud. Tbilisi is UTC+4
        // all year, so a Georgian morning's first four hours count against yesterday; labelling
        // that is cheaper and more honest than inventing a timezone the rest of the system does
        // not have and then disagreeing with every other timestamp on the screen.
        var dayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // The sweep's own silence limit, not a number of our own. The "stuck" tile has to mean the
        // same thing as the sweep that will eventually bury these books, or an operator sees a
        // count that never matches what the sweep then does about it.
        var staleCutoff = now.UtcDateTime - GenerationBudget.SweepSilenceLimit(bekiOptions.Value);

        var counts = await overview.GetCountsAsync(
            dayStart, monthStart, staleCutoff, cancellationToken);

        // Read through the orders repository rather than re-derived here: the panel must not be
        // able to show a row the list it links to would not.
        var attention = await reporting.GetOrdersAsync(
            null,
            null,
            AdminReportingRepository.NeedsAttentionFlag,
            1,
            RecentAttentionRows,
            cancellationToken);

        return Ok(new AdminOverviewResponse(
            counts.PaidTodayCount,
            counts.RevenueTodayMinor,
            counts.RevenueMonthMinor,
            counts.OrdersMonthCount,
            counts.BooksGeneratingCount,
            counts.BooksStuckCount,
            counts.BooksFailedCount,
            counts.AwaitingReviewCount,
            counts.OpenAlarmCount,
            counts.PrintQueue,
            attention.Items,
            now));
    }
}
