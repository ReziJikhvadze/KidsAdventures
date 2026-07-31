using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

/// <summary>
/// Read-only cross-customer queries for the operations screens.
///
/// Kept apart from the per-user repositories on purpose: everything here intentionally
/// omits a UserId predicate, and that is a property worth being able to see in one file
/// rather than hunting for across a dozen methods.
/// </summary>
public sealed class AdminReportingRepository(ISqlConnectionFactory connectionFactory)
    : IAdminReportingRepository
{
    public async Task<AdminOverviewResponse> GetOverviewAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken)
    {
        // One round trip: eight independent counts as scalar subqueries. Each is cheap and
        // the screen wants them together.
        const string sql = """
            SELECT
              (SELECT COUNT(*) FROM dbo.Orders WHERE CreatedAt >= @Since) AS OrdersInWindow,
              (SELECT COUNT(*) FROM dbo.Orders WHERE CreatedAt >= @Since
                 AND Status IN (N'Paid', N'Fulfilled')) AS PaidOrdersInWindow,
              (SELECT ISNULL(SUM(CAST(TotalMinor AS BIGINT)), 0) FROM dbo.Orders
                 WHERE CreatedAt >= @Since AND Status IN (N'Paid', N'Fulfilled')) AS RevenueMinorInWindow,
              (SELECT COUNT(*) FROM dbo.Users WHERE CreatedAt >= @Since) AS NewCustomersInWindow,
              (SELECT COUNT(*) FROM dbo.AdventurePacks WHERE CreatedAt >= @Since) AS BooksGeneratedInWindow,
              (SELECT COUNT(*) FROM dbo.AdventurePacks WHERE Status = N'Failed') AS BooksFailed,
              (SELECT COUNT(*) FROM dbo.AdventurePacks
                 WHERE Status IN (N'Pending', N'Generating', N'GeneratingStory', N'GeneratingPdf')) AS BooksInFlight,
              (SELECT COUNT(*) FROM dbo.Orders WHERE Status = N'Paid') AS PaidButUnfulfilled,
              (SELECT COUNT(*) FROM dbo.PrintOrders WHERE Status IN (N'Pending', N'Queued')) AS PrintOrdersAwaiting;
            """;

        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<AdminOverviewResponse>(
            new CommandDefinition(sql, new { Since = sinceUtc }, cancellationToken: cancellationToken));
    }

    public async Task<AdminOrderListResponse> GetOrdersAsync(
        string? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        const string where = """
            WHERE (@Status IS NULL OR o.Status = @Status)
              AND (@Search IS NULL
                   OR u.Email LIKE @Like
                   OR u.PhoneNumber LIKE @Like
                   OR b.Title LIKE @Like
                   OR CAST(o.Id AS NVARCHAR(64)) LIKE @Like)
            """;

        var sql = $"""
            SELECT COUNT(*)
            FROM dbo.Orders o
            LEFT JOIN dbo.Users u ON u.Id = o.UserId
            LEFT JOIN dbo.AdventurePacks b ON b.Id = o.BookId
            {where};

            SELECT o.Id, o.UserId, u.Email AS CustomerEmail, u.PhoneNumber AS CustomerPhone,
                   o.BookId, b.Title AS BookTitle, o.Type, o.Package, o.Status, o.Currency,
                   o.SubtotalMinor, o.DiscountMinor, o.TotalMinor, o.FailureReason,
                   o.CreatedAt, o.PaidAt, o.FulfilledAt
            FROM dbo.Orders o
            LEFT JOIN dbo.Users u ON u.Id = o.UserId
            LEFT JOIN dbo.AdventurePacks b ON b.Id = o.BookId
            {where}
            ORDER BY o.CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        var parameters = new
        {
            Status = string.IsNullOrWhiteSpace(status) ? null : status,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Like = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%",
            Offset = (page - 1) * pageSize,
            PageSize = pageSize
        };

        using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var total = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<AdminOrderRow>()).ToList();

        return new AdminOrderListResponse { Total = total, Page = page, PageSize = pageSize, Items = items };
    }

    public async Task<AdminCustomerListResponse> GetCustomersAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        const string where = """
            WHERE (@Search IS NULL
                   OR u.Email LIKE @Like
                   OR u.PhoneNumber LIKE @Like
                   OR u.DisplayName LIKE @Like)
            """;

        var sql = $"""
            SELECT COUNT(*) FROM dbo.Users u {where};

            SELECT u.Id, u.Email, u.PhoneNumber, u.DisplayName, u.CreatedAt,
                   (SELECT COUNT(*) FROM dbo.AdventurePacks p WHERE p.UserId = u.Id) AS BookCount,
                   (SELECT COUNT(*) FROM dbo.Orders o WHERE o.UserId = u.Id) AS OrderCount,
                   (SELECT ISNULL(SUM(CAST(o.TotalMinor AS BIGINT)), 0) FROM dbo.Orders o
                      WHERE o.UserId = u.Id AND o.Status IN (N'Paid', N'Fulfilled')) AS SpendMinor
            FROM dbo.Users u
            {where}
            ORDER BY u.CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        var parameters = new
        {
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Like = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%",
            Offset = (page - 1) * pageSize,
            PageSize = pageSize
        };

        using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var total = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<AdminCustomerRow>()).ToList();

        return new AdminCustomerListResponse { Total = total, Page = page, PageSize = pageSize, Items = items };
    }

    public async Task<AdminProductionListResponse> GetProductionAsync(
        bool includeCompleted,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // Default view is the work queue: anything not finished. Failed first, because that
        // is the row an operator has to do something about.
        const string where = """
            WHERE (@IncludeCompleted = 1
                   OR p.Status NOT IN (N'Completed', N'StoryReady'))
            """;

        var sql = $"""
            SELECT COUNT(*) FROM dbo.AdventurePacks p {where};

            SELECT p.Id, p.Title, p.Status, p.WorldId, p.SequenceNumber,
                   c.Name AS HeroName, u.Email AS CustomerEmail,
                   p.ProgressMessage, p.ErrorMessage, p.CreatedAt
            FROM dbo.AdventurePacks p
            LEFT JOIN dbo.Users u ON u.Id = p.UserId
            LEFT JOIN dbo.Characters c ON c.Id = p.PrimaryCharacterId
            {where}
            ORDER BY CASE WHEN p.Status = N'Failed' THEN 0 ELSE 1 END, p.CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        var parameters = new
        {
            IncludeCompleted = includeCompleted ? 1 : 0,
            Offset = (page - 1) * pageSize,
            PageSize = pageSize
        };

        using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var total = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<AdminProductionRow>()).ToList();

        return new AdminProductionListResponse { Total = total, Page = page, PageSize = pageSize, Items = items };
    }
}
