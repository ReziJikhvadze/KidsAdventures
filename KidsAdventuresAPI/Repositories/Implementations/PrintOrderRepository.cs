using AdventurePacks.Api.DTOs.Print;
using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class PrintOrderRepository(ISqlConnectionFactory connectionFactory) : IPrintOrderRepository
{
    private const string Columns = """
        Id, OrderId, BookId, UserId, RecipientName, RecipientPhone, City, Region,
        AddressLine1, AddressLine2, PostalCode, Notes, Status, TrackingCode,
        CreatedAt, UpdatedAt, ShippedAt, DeliveredAt
        """;

    public async Task<PrintOrder> CreateIfAbsentAsync(
        PrintOrder printOrder,
        CancellationToken cancellationToken)
    {
        // UX_PrintOrders_OrderId already forbids a second parcel per order; the NOT EXISTS
        // guard turns that from an exception into a no-op, because the caller is a
        // webhook that will be replayed.
        const string sql = """
                           INSERT INTO dbo.PrintOrders (
                               Id, OrderId, BookId, UserId, RecipientName, RecipientPhone, City, Region,
                               AddressLine1, AddressLine2, PostalCode, Notes, Status, TrackingCode,
                               CreatedAt, UpdatedAt, ShippedAt, DeliveredAt)
                           SELECT
                               @Id, @OrderId, @BookId, @UserId, @RecipientName, @RecipientPhone, @City, @Region,
                               @AddressLine1, @AddressLine2, @PostalCode, @Notes, @Status, @TrackingCode,
                               @CreatedAt, @UpdatedAt, @ShippedAt, @DeliveredAt
                           WHERE NOT EXISTS (SELECT 1 FROM dbo.PrintOrders WHERE OrderId = @OrderId);
                           """;

        printOrder.Id = printOrder.Id == Guid.Empty ? Guid.NewGuid() : printOrder.Id;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql, ToParameters(printOrder), cancellationToken: cancellationToken));

        return await GetByOrderIdAsync(printOrder.OrderId, cancellationToken) ?? printOrder;
    }

    public async Task<PrintOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var sql = $"SELECT TOP 1 {Columns} FROM dbo.PrintOrders WHERE Id = @Id;";
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<PrintOrderRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<PrintOrder?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var sql = $"SELECT TOP 1 {Columns} FROM dbo.PrintOrders WHERE Id = @Id AND UserId = @UserId;";
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<PrintOrderRow>(
            new CommandDefinition(sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<PrintOrder?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var sql = $"SELECT TOP 1 {Columns} FROM dbo.PrintOrders WHERE OrderId = @OrderId;";
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<PrintOrderRow>(
            new CommandDefinition(sql, new { OrderId = orderId }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<PrintOrder>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sql = $"SELECT {Columns} FROM dbo.PrintOrders WHERE UserId = @UserId ORDER BY CreatedAt DESC;";
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PrintOrderRow>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<PrintOrder>> GetByBookIdsAsync(
        IReadOnlyCollection<Guid> bookIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (bookIds.Count == 0)
        {
            return [];
        }

        var sql = $"""
                   SELECT {Columns}
                   FROM dbo.PrintOrders
                   WHERE UserId = @UserId AND BookId IN @BookIds
                   ORDER BY CreatedAt DESC;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PrintOrderRow>(new CommandDefinition(
            sql, new { UserId = userId, BookIds = bookIds }, cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// The operations queue with everything the console shows on a row, in one statement.
    ///
    /// LEFT JOIN throughout: a parcel whose book row has gone, or whose order was archived, is
    /// still a parcel somebody has to post, and dropping it from the queue would be the worst
    /// possible way to report that.
    /// </summary>
    public async Task<IReadOnlyList<AdminPrintQueueRow>> GetAdminQueueAsync(
        PrintOrderStatus? status,
        int limit,
        CancellationToken cancellationToken)
    {
        // Oldest first when filtering: the print queue is worked front to back. Newest
        // first for the unfiltered view, which is a browse rather than a work list.
        var sql = status is null
            ? $"{AdminQueueProjection} ORDER BY p.CreatedAt DESC;"
            : $"{AdminQueueProjection} WHERE p.Status = @Status ORDER BY p.CreatedAt ASC;";

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AdminPrintQueueRow>(new CommandDefinition(
            sql,
            new { Status = status?.ToString(), Limit = limit },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    /// <summary>
    /// One parcel in the queue's own shape — what the status-change route answers with, so the row
    /// the console puts back is built from the same projection as the row it replaces.
    /// </summary>
    public async Task<AdminPrintQueueRow?> GetAdminQueueRowAsync(Guid id, CancellationToken cancellationToken)
    {
        var sql = $"{AdminQueueProjection} WHERE p.Id = @Id;";

        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<AdminPrintQueueRow>(new CommandDefinition(
            sql, new { Id = id, Limit = 1 }, cancellationToken: cancellationToken));
    }

    private const string AdminQueueProjection = """
            SELECT TOP (@Limit)
                   p.Id, p.OrderId, p.BookId, p.UserId, p.Status,
                   p.RecipientName, p.RecipientPhone, p.City, p.Region,
                   p.AddressLine1, p.AddressLine2, p.PostalCode, p.Notes, p.TrackingCode,
                   p.CreatedAt, p.ShippedAt, p.DeliveredAt,
                   b.Title AS BookTitle, b.Status AS BookStatus,
                   b.GenerationPipeline AS BookPipeline,
                   CAST(CASE WHEN b.PrintPdfUrl IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS HasPrintPdf,
                   CAST(CASE WHEN b.PrintPdfUrl IS NULL AND b.PdfUrl IS NOT NULL
                             THEN 1 ELSE 0 END AS BIT) AS PdfIsReadingCopyFallback,
                   c.Name AS HeroName,
                   u.Email AS CustomerEmail, u.PhoneNumber AS CustomerPhone,
                   ISNULL(o.TotalMinor, 0) AS TotalMinor
            FROM dbo.PrintOrders p
            LEFT JOIN dbo.AdventurePacks b ON b.Id = p.BookId
            LEFT JOIN dbo.Characters c ON c.Id = b.PrimaryCharacterId
            LEFT JOIN dbo.Users u ON u.Id = p.UserId
            LEFT JOIN dbo.Orders o ON o.Id = p.OrderId
            """;

    public async Task<IReadOnlyDictionary<PrintOrderStatus, int>> GetAdminCountsAsync(
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT Status, COUNT(*) AS Count FROM dbo.PrintOrders GROUP BY Status;";
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<(string Status, int Count)>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        var counts = new Dictionary<PrintOrderStatus, int>();
        foreach (var (status, count) in rows)
        {
            if (Enum.TryParse<PrintOrderStatus>(status, out var parsed))
            {
                counts[parsed] = count;
            }
        }

        return counts;
    }

    public async Task<bool> UpdateStatusAsync(
        Guid id,
        PrintOrderStatus status,
        string? trackingCode,
        CancellationToken cancellationToken)
    {
        // ShippedAt and DeliveredAt are stamped on the first transition only, so
        // re-saving a Shipped parcel to correct its tracking code does not move the date.
        const string sql = """
                           UPDATE dbo.PrintOrders
                           SET Status = @Status,
                               TrackingCode = COALESCE(@TrackingCode, TrackingCode),
                               UpdatedAt = SYSUTCDATETIME(),
                               ShippedAt = CASE
                                   WHEN @Status IN (N'Shipped', N'Delivered') AND ShippedAt IS NULL
                                   THEN SYSUTCDATETIME() ELSE ShippedAt END,
                               DeliveredAt = CASE
                                   WHEN @Status = N'Delivered' AND DeliveredAt IS NULL
                                   THEN SYSUTCDATETIME() ELSE DeliveredAt END
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, Status = status.ToString(), TrackingCode = trackingCode },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> UpdateAddressAsync(PrintOrder printOrder, CancellationToken cancellationToken)
    {
        // Only while the parcel has not left: once it is Shipped the stored address is a
        // record of where it went, not an instruction.
        const string sql = """
                           UPDATE dbo.PrintOrders
                           SET RecipientName = @RecipientName,
                               RecipientPhone = @RecipientPhone,
                               City = @City,
                               Region = @Region,
                               AddressLine1 = @AddressLine1,
                               AddressLine2 = @AddressLine2,
                               PostalCode = @PostalCode,
                               Notes = @Notes,
                               UpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id AND Status IN (N'AwaitingPrint', N'Printing');
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql, ToParameters(printOrder), cancellationToken: cancellationToken));
        return affected > 0;
    }

    private static object ToParameters(PrintOrder printOrder) => new
    {
        printOrder.Id,
        printOrder.OrderId,
        printOrder.BookId,
        printOrder.UserId,
        printOrder.RecipientName,
        printOrder.RecipientPhone,
        printOrder.City,
        printOrder.Region,
        printOrder.AddressLine1,
        printOrder.AddressLine2,
        printOrder.PostalCode,
        printOrder.Notes,
        Status = printOrder.Status.ToString(),
        printOrder.TrackingCode,
        printOrder.CreatedAt,
        printOrder.UpdatedAt,
        printOrder.ShippedAt,
        printOrder.DeliveredAt
    };

    private static PrintOrder Map(PrintOrderRow row) => new()
    {
        Id = row.Id,
        OrderId = row.OrderId,
        BookId = row.BookId,
        UserId = row.UserId,
        RecipientName = row.RecipientName,
        RecipientPhone = row.RecipientPhone,
        City = row.City,
        Region = row.Region,
        AddressLine1 = row.AddressLine1,
        AddressLine2 = row.AddressLine2,
        PostalCode = row.PostalCode,
        Notes = row.Notes,
        Status = Enum.Parse<PrintOrderStatus>(row.Status),
        TrackingCode = row.TrackingCode,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
        ShippedAt = row.ShippedAt,
        DeliveredAt = row.DeliveredAt
    };

    private sealed class PrintOrderRow
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Guid BookId { get; set; }
        public Guid UserId { get; set; }
        public string RecipientName { get; set; } = string.Empty;
        public string RecipientPhone { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string AddressLine1 { get; set; } = string.Empty;
        public string? AddressLine2 { get; set; }
        public string? PostalCode { get; set; }
        public string? Notes { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? TrackingCode { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ShippedAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
    }
}
