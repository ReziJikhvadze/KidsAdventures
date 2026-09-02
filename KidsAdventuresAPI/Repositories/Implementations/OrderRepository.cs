using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class OrderRepository(ISqlConnectionFactory connectionFactory) : IOrderRepository
{
    private const string Columns = """
        Id, UserId, BookId, Type, Package, Currency, SubtotalMinor, DiscountMinor, TotalMinor,
        PromoCodeId, Status, Provider, ProviderSessionId, ProviderPaymentIntentId, DraftJson,
        ShippingJson, FailureReason, CreatedAt, PaidAt, FulfilledAt
        """;

    public async Task<Guid> CreateAsync(Order order, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO dbo.Orders (
                               Id, UserId, BookId, Type, Package, Currency, SubtotalMinor, DiscountMinor, TotalMinor,
                               PromoCodeId, Status, Provider, ProviderSessionId, ProviderPaymentIntentId, DraftJson,
                               ShippingJson, FailureReason, CreatedAt, PaidAt, FulfilledAt)
                           VALUES (
                               @Id, @UserId, @BookId, @Type, @Package, @Currency, @SubtotalMinor, @DiscountMinor, @TotalMinor,
                               @PromoCodeId, @Status, @Provider, @ProviderSessionId, @ProviderPaymentIntentId, @DraftJson,
                               @ShippingJson, @FailureReason, @CreatedAt, @PaidAt, @FulfilledAt);
                           """;
        order.Id = order.Id == Guid.Empty ? Guid.NewGuid() : order.Id;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, ToParameters(order), cancellationToken: cancellationToken));
        return order.Id;
    }

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var sql = $"SELECT TOP 1 {Columns} FROM dbo.Orders WHERE Id = @Id;";
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<OrderRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<Order?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var sql = $"SELECT TOP 1 {Columns} FROM dbo.Orders WHERE Id = @Id AND UserId = @UserId;";
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<OrderRow>(
            new CommandDefinition(sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<Order?> GetByProviderSessionIdAsync(
        string providerSessionId,
        CancellationToken cancellationToken)
    {
        var sql = $"SELECT TOP 1 {Columns} FROM dbo.Orders WHERE ProviderSessionId = @ProviderSessionId;";
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<OrderRow>(
            new CommandDefinition(sql, new { ProviderSessionId = providerSessionId }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<Order>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sql = $"SELECT {Columns} FROM dbo.Orders WHERE UserId = @UserId ORDER BY CreatedAt DESC;";
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<OrderRow>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<Order>> GetPaidForBookAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT {Columns}
                   FROM dbo.Orders
                   WHERE BookId = @BookId AND Status IN (N'Paid', N'Fulfilled')
                   ORDER BY CreatedAt ASC;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<OrderRow>(
            new CommandDefinition(sql, new { BookId = bookId }, cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

    public async Task AttachProviderSessionAsync(
        Guid id,
        string providerSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.Orders
                           SET ProviderSessionId = @ProviderSessionId
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, ProviderSessionId = providerSessionId },
            cancellationToken: cancellationToken));
    }

    public async Task SetBookIdAsync(Guid id, Guid bookId, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE dbo.Orders SET BookId = @BookId WHERE Id = @Id;";
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = id, BookId = bookId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> TryMarkPaidAsync(
        Guid id,
        string? providerPaymentIntentId,
        CancellationToken cancellationToken)
    {
        // The Status = 'Pending' predicate is the whole idempotency story: Stripe sends
        // checkout.session.completed more than once, and the success page confirms in
        // parallel, but only the first writer sees a row to update.
        const string sql = """
                           UPDATE dbo.Orders
                           SET Status = N'Paid',
                               PaidAt = SYSUTCDATETIME(),
                               ProviderPaymentIntentId = COALESCE(@ProviderPaymentIntentId, ProviderPaymentIntentId),
                               FailureReason = NULL
                           WHERE Id = @Id AND Status = N'Pending';
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, ProviderPaymentIntentId = providerPaymentIntentId },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> TryMarkFulfilledAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.Orders
                           SET Status = N'Fulfilled', FulfilledAt = SYSUTCDATETIME()
                           WHERE Id = @Id AND Status = N'Paid';
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task MarkFailedAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        // A paid order is never downgraded to Failed: the money is real even when
        // generation broke, and fulfilment is retried instead.
        const string sql = """
                           UPDATE dbo.Orders
                           SET Status = CASE WHEN Status = N'Pending' THEN N'Failed' ELSE Status END,
                               FailureReason = @Reason
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = id, Reason = Truncate(reason, 512) }, cancellationToken: cancellationToken));
    }

    public async Task<bool> TryCancelAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.Orders
                           SET Status = N'Cancelled'
                           WHERE Id = @Id AND UserId = @UserId AND Status = N'Pending';
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<IReadOnlyList<Order>> GetStalledPaidAsync(
        DateTime paidBeforeUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP (@Limit) {Columns}
                   FROM dbo.Orders
                   WHERE Status = N'Paid' AND PaidAt < @PaidBefore
                   ORDER BY PaidAt ASC;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<OrderRow>(new CommandDefinition(
            sql, new { PaidBefore = paidBeforeUtc, Limit = limit }, cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

    public async Task<bool> TrySetAdminStatusAsync(
        Guid id,
        OrderStatus status,
        IReadOnlyCollection<OrderStatus> allowedFrom,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        if (allowedFrom.Count == 0)
        {
            return false;
        }

        // Compare-and-set on the whole allowed set, in one statement. The console has already
        // checked the transition and told the operator why it would be refused; this is the check
        // that survives two admins clicking at the same moment, and a webhook landing between the
        // read and the write.
        const string sql = """
                           UPDATE dbo.Orders
                           SET Status = @Status,
                               FailureReason = COALESCE(@Reason, FailureReason)
                           WHERE Id = @Id AND Status IN @AllowedFrom;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = id,
                Status = status.ToString(),
                AllowedFrom = allowedFrom.Select(allowed => allowed.ToString()).ToArray(),
                Reason = failureReason is null ? null : Truncate(failureReason, 512),
            },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    private static object ToParameters(Order order) => new
    {
        order.Id,
        order.UserId,
        order.BookId,
        Type = order.Type.ToString(),
        Package = order.Package.ToString(),
        order.Currency,
        order.SubtotalMinor,
        order.DiscountMinor,
        order.TotalMinor,
        order.PromoCodeId,
        Status = order.Status.ToString(),
        order.Provider,
        order.ProviderSessionId,
        order.ProviderPaymentIntentId,
        order.DraftJson,
        order.ShippingJson,
        order.FailureReason,
        order.CreatedAt,
        order.PaidAt,
        order.FulfilledAt
    };

    private static Order Map(OrderRow row) => new()
    {
        Id = row.Id,
        UserId = row.UserId,
        BookId = row.BookId,
        Type = Enum.Parse<OrderType>(row.Type),
        Package = Enum.Parse<OrderPackage>(row.Package),
        Currency = row.Currency,
        SubtotalMinor = row.SubtotalMinor,
        DiscountMinor = row.DiscountMinor,
        TotalMinor = row.TotalMinor,
        PromoCodeId = row.PromoCodeId,
        Status = Enum.Parse<OrderStatus>(row.Status),
        Provider = row.Provider,
        ProviderSessionId = row.ProviderSessionId,
        ProviderPaymentIntentId = row.ProviderPaymentIntentId,
        DraftJson = row.DraftJson,
        ShippingJson = row.ShippingJson,
        FailureReason = row.FailureReason,
        CreatedAt = row.CreatedAt,
        PaidAt = row.PaidAt,
        FulfilledAt = row.FulfilledAt
    };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private sealed class OrderRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid? BookId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Package { get; set; } = string.Empty;
        public string Currency { get; set; } = GelPricing.Currency;
        public int SubtotalMinor { get; set; }
        public int DiscountMinor { get; set; }
        public int TotalMinor { get; set; }
        public Guid? PromoCodeId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Provider { get; set; } = OrderProviders.Stripe;
        public string? ProviderSessionId { get; set; }
        public string? ProviderPaymentIntentId { get; set; }
        public string? DraftJson { get; set; }
        public string? ShippingJson { get; set; }
        public string? FailureReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? FulfilledAt { get; set; }
    }
}
