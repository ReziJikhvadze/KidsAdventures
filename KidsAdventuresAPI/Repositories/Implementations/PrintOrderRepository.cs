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

    public async Task<IReadOnlyList<PrintOrder>> GetForAdminAsync(
        PrintOrderStatus? status,
        int limit,
        CancellationToken cancellationToken)
    {
        // Oldest first when filtering: the print queue is worked front to back. Newest
        // first for the unfiltered view, which is a browse rather than a work list.
        var sql = status is null
            ? $"""
               SELECT TOP (@Limit) {Columns}
               FROM dbo.PrintOrders
               ORDER BY CreatedAt DESC;
               """
            : $"""
               SELECT TOP (@Limit) {Columns}
               FROM dbo.PrintOrders
               WHERE Status = @Status
               ORDER BY CreatedAt ASC;
               """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PrintOrderRow>(new CommandDefinition(
            sql,
            new { Status = status?.ToString(), Limit = limit },
            cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

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
