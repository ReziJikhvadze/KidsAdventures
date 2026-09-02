using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

/// <summary>
/// The overview's eleven numbers, as three result sets in one command.
///
/// One round trip is the requirement, and not for speed: the panel says "as of 09:14" and links
/// each tile to the list behind it, so the figures have to have been true at the same moment. Four
/// separate calls would produce a panel where the alarm badge and the alarm list were taken
/// seconds apart — usually harmless, occasionally the reason somebody says the console is lying.
///
/// Grouped by table rather than by tile, because that is what makes each SELECT indexable: the
/// order money comes off <c>IX_Orders_Status</c>'s PaidAt, the book states off the status column,
/// the alarms off migration 035's (PackId, ReviewedAtUtc) index, and the parcels off
/// <c>IX_PrintOrders_Status_CreatedAt</c>.
/// </summary>
public sealed class AdminOverviewRepository(ISqlConnectionFactory connectionFactory)
    : IAdminOverviewRepository
{
    /// <summary>
    /// The statuses that mean "somebody is waiting for this book to be drawn".
    ///
    /// Built from the enum rather than typed as literals so a renamed member is a compile error
    /// instead of a tile that silently reads zero. Legacy <c>Generating</c> is in the set because
    /// the enum itself says to treat it as GeneratingStory, and a row written by the old
    /// single-phase job is still a parent waiting.
    /// </summary>
    private static readonly string GeneratingStatuses = Literals(
        AdventurePackStatus.Pending,
        AdventurePackStatus.Generating,
        AdventurePackStatus.GeneratingStory,
        AdventurePackStatus.StoryReady,
        AdventurePackStatus.GeneratingPdf);

    /// <summary>
    /// Money that came back is not money that came in.
    ///
    /// Cancelled and Refunded orders are excluded from every figure in the first result set,
    /// including the count: a refunded order is not a sale that happened today, it is a sale that
    /// un-happened, and a revenue tile that says otherwise is the one number on this screen
    /// somebody will quote to an accountant.
    /// </summary>
    private static readonly string SettledOrderStatuses = Literals(
        OrderStatus.Cancelled,
        OrderStatus.Refunded);

    public async Task<AdminOverviewCounts> GetCountsAsync(
        DateTime dayStartUtc,
        DateTime monthStartUtc,
        DateTime staleCutoffUtc,
        CancellationToken cancellationToken)
    {
        /*
          Three SELECTs, each one row.

          The first is filtered to the month and then counted twice with CASE, so "today" and "this
          month" come off one pass over one index range rather than two scans with different WHERE
          clauses. Midnight is always inside the month, so the month predicate covers both.

          The second is the only one that needs a thought about cost. Most AdventurePacks rows are
          Completed and would be scanned by a naive status filter; the WHERE therefore admits
          Completed only in the shape the awaiting-review tile is about — finished, Beki, and with
          no reading copy published — which is a small set by construction.

          "Stuck" uses COALESCE(GenerationHeartbeatUtc, CreatedAt) for the same reason the sweep
          does: the heartbeat column arrived after the books that are already stuck, and a NULL
          read as "recent" would hide exactly the rows the tile exists to surface.

          A failed book whose order was cancelled or refunded is not counted. Nobody is waiting on
          a book nobody is paying for, and the orders list's own needs-attention predicate makes
          the same exclusion — the tile links there, so it has to agree with it.
        */
        var sql = $"""
                   SELECT
                       COUNT(CASE WHEN o.PaidAt >= @DayStartUtc THEN 1 END) AS PaidTodayCount,
                       ISNULL(SUM(CASE WHEN o.PaidAt >= @DayStartUtc
                                       THEN CAST(o.TotalMinor AS BIGINT) END), 0) AS RevenueTodayMinor,
                       ISNULL(SUM(CAST(o.TotalMinor AS BIGINT)), 0) AS RevenueMonthMinor,
                       COUNT(*) AS OrdersMonthCount
                   FROM dbo.Orders o
                   WHERE o.PaidAt >= @MonthStartUtc
                     AND o.Status NOT IN ({SettledOrderStatuses});

                   SELECT
                       COUNT(CASE WHEN b.Status IN ({GeneratingStatuses})
                                  THEN 1 END) AS BooksGeneratingCount,
                       COUNT(CASE WHEN b.Status IN ({GeneratingStatuses})
                                   AND COALESCE(b.GenerationHeartbeatUtc, b.CreatedAt) < @StaleCutoffUtc
                                  THEN 1 END) AS BooksStuckCount,
                       COUNT(CASE WHEN b.Status = N'{nameof(AdventurePackStatus.Failed)}'
                                   AND b.OrderSettled = 0
                                  THEN 1 END) AS BooksFailedCount,
                       COUNT(CASE WHEN b.Status = N'{nameof(AdventurePackStatus.Completed)}'
                                   AND b.PdfUrl IS NULL
                                   AND b.GenerationPipeline = N'{GenerationPipelines.Beki}'
                                  THEN 1 END) AS AwaitingReviewCount
                   FROM (
                       -- SQL Server refuses a subquery inside an aggregate, so "is the order
                       -- settled" is decided per row here and only counted above.
                       SELECT p.Status, p.GenerationHeartbeatUtc, p.CreatedAt, p.PdfUrl,
                              p.GenerationPipeline,
                              CASE WHEN EXISTS (
                                       SELECT 1 FROM dbo.Orders o
                                       WHERE o.BookId = p.Id
                                         AND o.Status IN ({SettledOrderStatuses}))
                                   THEN 1 ELSE 0 END AS OrderSettled
                       FROM dbo.AdventurePacks p
                       WHERE p.Status IN ({GeneratingStatuses})
                          OR p.Status = N'{nameof(AdventurePackStatus.Failed)}'
                          OR (p.Status = N'{nameof(AdventurePackStatus.Completed)}' AND p.PdfUrl IS NULL)
                   ) b;

                   SELECT
                       (SELECT COUNT(*) FROM dbo.BekiAlarms
                        WHERE ReviewedAtUtc IS NULL) AS OpenAlarmCount,
                       (SELECT COUNT(*) FROM dbo.PrintOrders
                        WHERE Status = N'{nameof(PrintOrderStatus.AwaitingPrint)}') AS AwaitingPrint,
                       (SELECT COUNT(*) FROM dbo.PrintOrders
                        WHERE Status = N'{nameof(PrintOrderStatus.Printing)}') AS Printing,
                       (SELECT COUNT(*) FROM dbo.PrintOrders
                        WHERE Status = N'{nameof(PrintOrderStatus.Shipped)}') AS Shipped;
                   """;

        using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            new
            {
                DayStartUtc = dayStartUtc,
                MonthStartUtc = monthStartUtc,
                StaleCutoffUtc = staleCutoffUtc,
            },
            cancellationToken: cancellationToken));

        var money = await grid.ReadSingleAsync<MoneyRow>();
        var books = await grid.ReadSingleAsync<BookRow>();
        var queues = await grid.ReadSingleAsync<QueueRow>();

        return new AdminOverviewCounts(
            money.PaidTodayCount,
            money.RevenueTodayMinor,
            money.RevenueMonthMinor,
            money.OrdersMonthCount,
            books.BooksGeneratingCount,
            books.BooksStuckCount,
            books.BooksFailedCount,
            books.AwaitingReviewCount,
            queues.OpenAlarmCount,
            new AdminPrintQueueCounts(queues.AwaitingPrint, queues.Printing, queues.Shipped));
    }

    /// <summary>
    /// Enum members as a SQL list. Nothing user-supplied ever reaches it — the arguments are
    /// compile-time enum values — so the interpolation into the statement carries no injection
    /// surface, and the alternative (a parameter list Dapper expands twice in one batch) is harder
    /// to read for no gain.
    /// </summary>
    private static string Literals<T>(params T[] values) where T : struct, Enum =>
        string.Join(", ", values.Select(value => $"N'{value}'"));

    private sealed class MoneyRow
    {
        public int PaidTodayCount { get; set; }
        public long RevenueTodayMinor { get; set; }
        public long RevenueMonthMinor { get; set; }
        public int OrdersMonthCount { get; set; }
    }

    private sealed class BookRow
    {
        public int BooksGeneratingCount { get; set; }
        public int BooksStuckCount { get; set; }
        public int BooksFailedCount { get; set; }
        public int AwaitingReviewCount { get; set; }
    }

    private sealed class QueueRow
    {
        public int OpenAlarmCount { get; set; }
        public int AwaitingPrint { get; set; }
        public int Printing { get; set; }
        public int Shipped { get; set; }
    }
}
