namespace AdventurePacks.Api.DTOs.Admin;

public sealed class AdminOverviewResponse
{
    public int OrdersInWindow { get; set; }
    public int PaidOrdersInWindow { get; set; }

    /// <summary>Minor units, GEL. Fulfilled and paid orders only.</summary>
    public long RevenueMinorInWindow { get; set; }

    public int NewCustomersInWindow { get; set; }
    public int BooksGeneratedInWindow { get; set; }

    /// <summary>Books that failed generation — the queue an operator must act on.</summary>
    public int BooksFailed { get; set; }

    /// <summary>Books still generating, including ones that may be stuck.</summary>
    public int BooksInFlight { get; set; }

    /// <summary>Orders paid but never fulfilled — money taken with nothing delivered.</summary>
    public int PaidButUnfulfilled { get; set; }

    public int PrintOrdersAwaiting { get; set; }
}

public sealed class AdminOrderListResponse
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<AdminOrderRow> Items { get; set; } = [];
}

public sealed class AdminOrderRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public Guid? BookId { get; set; }
    public string? BookTitle { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Package { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = GelPricing.Currency;
    public int SubtotalMinor { get; set; }
    public int DiscountMinor { get; set; }
    public int TotalMinor { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? FulfilledAt { get; set; }
}

public sealed class AdminCustomerListResponse
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<AdminCustomerRow> Items { get; set; } = [];
}

public sealed class AdminCustomerRow
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? DisplayName { get; set; }
    public int BookCount { get; set; }
    public int OrderCount { get; set; }
    public long SpendMinor { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AdminProductionListResponse
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<AdminProductionRow> Items { get; set; } = [];
}

public sealed class AdminProductionRow
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? WorldId { get; set; }
    public int SequenceNumber { get; set; }
    public string? HeroName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? ProgressMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}
