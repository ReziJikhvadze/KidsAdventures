namespace AdventurePacks.Api.DTOs.Admin;

/// <summary>
/// The numbers an operator opens the console to check, before they have clicked anything.
///
/// Every figure here is a door: each one is rendered as a tile that navigates to the list which
/// contains exactly those rows, and the predicate behind the tile is the same predicate behind the
/// list's filter. That is the whole discipline of this response — a dashboard whose count disagrees
/// with the screen it links to teaches an operator to distrust both, and then the console is
/// decoration.
///
/// Day and month are UTC calendar boundaries, and the console labels the tile "დღეს (UTC)" rather
/// than pretending otherwise. Tbilisi is UTC+4 all year, so a Georgian morning's first four hours
/// belong to yesterday's figure; saying so on the tile is cheaper and more honest than a timezone
/// the rest of the system does not have.
/// </summary>
/// <param name="RecentAttention">
/// The newest handful of orders the "needs attention" filter selects — read through the same
/// repository method the orders list uses, so the overview cannot show a row the list would not.
/// </param>
/// <param name="GeneratedAtUtc">
/// When these counts were taken. The console polls, and a stale panel that cannot say it is stale
/// is a panel somebody makes a decision from.
/// </param>
public sealed record AdminOverviewResponse(
    int PaidTodayCount,
    long RevenueTodayMinor,
    long RevenueMonthMinor,
    int OrdersMonthCount,
    int BooksGeneratingCount,
    int BooksStuckCount,
    int BooksFailedCount,
    int AwaitingReviewCount,
    int OpenAlarmCount,
    AdminPrintQueueCounts PrintQueue,
    IReadOnlyList<AdminOrderRow> RecentAttention,
    DateTimeOffset GeneratedAtUtc);

/// <summary>
/// Where the parcels are: three of the five print statuses, which are the three that are work.
///
/// Delivered and Cancelled are deliberately absent. They are the two states nobody has to do
/// anything about, and a queue tile that counts finished parcels grows for ever and stops meaning
/// "how much is outstanding".
/// </summary>
public sealed record AdminPrintQueueCounts(int AwaitingPrint, int Printing, int Shipped);

/// <summary>
/// Everything the overview reads out of SQL, in the shape one round trip returns it.
///
/// Separate from <see cref="AdminOverviewResponse"/> because the response also carries the recent
/// rows, which come from the orders repository rather than from this query — and because a
/// repository that returned the wire contract would make the controller a pass-through with no
/// place left to say what "today" means.
/// </summary>
public sealed record AdminOverviewCounts(
    int PaidTodayCount,
    long RevenueTodayMinor,
    long RevenueMonthMinor,
    int OrdersMonthCount,
    int BooksGeneratingCount,
    int BooksStuckCount,
    int BooksFailedCount,
    int AwaitingReviewCount,
    int OpenAlarmCount,
    AdminPrintQueueCounts PrintQueue)
{
    /// <summary>All zeroes — what an installation with no orders yet honestly reports.</summary>
    public static readonly AdminOverviewCounts Empty =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, new AdminPrintQueueCounts(0, 0, 0));
}
