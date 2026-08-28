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
    /// <summary>
    /// The one saved view worth a name: money taken with nothing delivered.
    ///
    /// It used to be a count on an overview page, where it could be read and not acted on.
    /// As a filter it lands on the list that can actually open the order.
    ///
    /// "Nothing delivered" is two different failures, and the obvious half is the smaller one.
    /// An order can be stuck before fulfilment ever ran — Paid with no FulfilledAt — but it can
    /// also be marked Fulfilled and still have delivered nothing: fulfilment's job is to create
    /// the book and queue it, so an order whose generation then failed is Fulfilled with a Failed
    /// book behind it. That parent has paid and has no book either way, so both belong here.
    /// </summary>
    public const string PaidUnfulfilledFlag = "paid-unfulfilled";

    public async Task<AdminOrderListResponse> GetOrdersAsync(
        string? status,
        string? search,
        string? flag,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // The book and parcel columns are the answer to "did they actually get it", which is
        // what the list is opened to find out. LEFT JOIN throughout: an unpaid order has no
        // book, and a digital order has no parcel, and neither is a reason to drop the row.
        const string from = """
            FROM dbo.Orders o
            LEFT JOIN dbo.Users u ON u.Id = o.UserId
            LEFT JOIN dbo.AdventurePacks b ON b.Id = o.BookId
            LEFT JOIN dbo.PrintOrders pr ON pr.OrderId = o.Id
            """;

        const string where = """
            WHERE (@Status IS NULL OR o.Status = @Status)
              AND (@PaidUnfulfilled = 0
                   OR (o.Status = N'Paid' AND o.FulfilledAt IS NULL)
                   OR (o.Status IN (N'Paid', N'Fulfilled') AND b.Status = N'Failed'))
              AND (@Search IS NULL
                   OR u.Email LIKE @Like
                   OR u.PhoneNumber LIKE @Like
                   OR b.Title LIKE @Like
                   OR CAST(o.Id AS NVARCHAR(64)) LIKE @Like)
            """;

        var sql = $"""
            SELECT COUNT(*)
            {from}
            {where};

            SELECT o.Id, o.UserId, u.Email AS CustomerEmail, u.PhoneNumber AS CustomerPhone,
                   o.BookId, b.Title AS BookTitle, o.Type, o.Package, o.Status, o.Currency,
                   o.SubtotalMinor, o.DiscountMinor, o.TotalMinor, o.FailureReason,
                   o.Provider, o.ProviderPaymentIntentId,
                   o.CreatedAt, o.PaidAt, o.FulfilledAt,
                   b.Status AS BookStatus, b.LastReadAt,
                   CAST(CASE WHEN b.PdfUrl IS NOT NULL OR b.PrintPdfUrl IS NOT NULL
                             THEN 1 ELSE 0 END AS BIT) AS HasPdf,
                   pr.Status AS PrintStatus
            {from}
            {where}
            ORDER BY o.CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        var parameters = new
        {
            Status = string.IsNullOrWhiteSpace(status) ? null : status,
            PaidUnfulfilled =
                string.Equals(flag, PaidUnfulfilledFlag, StringComparison.OrdinalIgnoreCase) ? 1 : 0,
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

    public async Task<AdminOrderDetailResponse?> GetOrderDetailAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        // Four result sets in one round trip. The panel opens under a row someone just
        // clicked, so the cost that matters is the number of trips, not the number of rows.
        const string sql = """
            SELECT o.Id, o.UserId, u.Email AS CustomerEmail, u.PhoneNumber AS CustomerPhone,
                   o.BookId, b.Title AS BookTitle, o.Type, o.Package, o.Status, o.Currency,
                   o.SubtotalMinor, o.DiscountMinor, o.TotalMinor, o.FailureReason,
                   o.Provider, o.ProviderPaymentIntentId,
                   o.CreatedAt, o.PaidAt, o.FulfilledAt,
                   b.Status AS BookStatus, b.LastReadAt,
                   CAST(CASE WHEN b.PdfUrl IS NOT NULL OR b.PrintPdfUrl IS NOT NULL
                             THEN 1 ELSE 0 END AS BIT) AS HasPdf,
                   pr.Status AS PrintStatus
            FROM dbo.Orders o
            LEFT JOIN dbo.Users u ON u.Id = o.UserId
            LEFT JOIN dbo.AdventurePacks b ON b.Id = o.BookId
            LEFT JOIN dbo.PrintOrders pr ON pr.OrderId = o.Id
            WHERE o.Id = @OrderId;

            SELECT u.Id, u.Email, u.PhoneNumber, u.DisplayName, u.PreferredLanguage,
                   u.IsAdmin, u.CreatedAt,
                   (SELECT COUNT(*) FROM dbo.AdventurePacks p WHERE p.UserId = u.Id) AS BookCount,
                   (SELECT COUNT(*) FROM dbo.Orders x WHERE x.UserId = u.Id) AS OrderCount
            FROM dbo.Users u
            JOIN dbo.Orders o ON o.UserId = u.Id
            WHERE o.Id = @OrderId;

            SELECT b.Id, b.Title, c.Name AS HeroName, b.WorldId, b.Status, b.SequenceNumber,
                   b.StoryPageCount, b.StoryLanguage, b.CoverImageUrl, b.ProgressMessage,
                   b.ErrorMessage, b.CreatedAt, b.LastReadAt,
                   CAST(CASE WHEN b.PdfUrl IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS HasReadingPdf,
                   CAST(CASE WHEN b.PrintPdfUrl IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS HasPrintPdf
            FROM dbo.AdventurePacks b
            JOIN dbo.Orders o ON o.BookId = b.Id
            LEFT JOIN dbo.Characters c ON c.Id = b.PrimaryCharacterId
            WHERE o.Id = @OrderId;

            SELECT pr.Id, pr.Status, pr.RecipientName, pr.RecipientPhone, pr.City, pr.Region,
                   pr.AddressLine1, pr.AddressLine2, pr.PostalCode, pr.Notes, pr.TrackingCode,
                   pr.CreatedAt, pr.ShippedAt, pr.DeliveredAt
            FROM dbo.PrintOrders pr
            WHERE pr.OrderId = @OrderId;
            """;

        using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, new { OrderId = orderId }, cancellationToken: cancellationToken));

        var order = await grid.ReadFirstOrDefaultAsync<AdminOrderRow>();
        if (order is null)
        {
            return null;
        }

        return new AdminOrderDetailResponse
        {
            Order = order,
            Customer = await grid.ReadFirstOrDefaultAsync<AdminOrderCustomer>() ?? new AdminOrderCustomer(),
            Book = await grid.ReadFirstOrDefaultAsync<AdminOrderBook>(),
            Shipment = await grid.ReadFirstOrDefaultAsync<AdminOrderShipment>()
        };
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

        // Admins first: this list is now also how one is granted, and the people who already
        // have the role are the ones being checked on.
        var sql = $"""
            SELECT COUNT(*) FROM dbo.Users u {where};

            SELECT u.Id, u.Email, u.PhoneNumber, u.DisplayName, u.IsAdmin, u.CreatedAt,
                   (SELECT COUNT(*) FROM dbo.AdventurePacks p WHERE p.UserId = u.Id) AS BookCount,
                   (SELECT COUNT(*) FROM dbo.Orders o WHERE o.UserId = u.Id) AS OrderCount,
                   (SELECT ISNULL(SUM(CAST(o.TotalMinor AS BIGINT)), 0) FROM dbo.Orders o
                      WHERE o.UserId = u.Id AND o.Status IN (N'Paid', N'Fulfilled')) AS SpendMinor
            FROM dbo.Users u
            {where}
            ORDER BY u.IsAdmin DESC, u.CreatedAt DESC
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
}
