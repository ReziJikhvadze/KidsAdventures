namespace AdventurePacks.Api.DTOs.Admin;

public sealed class AdminOrderListResponse
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<AdminOrderRow> Items { get; set; } = [];
}

/// <summary>
/// One line of the order list.
///
/// The four fields after <see cref="FulfilledAt"/> are not about the order at all — they
/// describe the book it bought, and they are here because the question the list is opened to
/// answer is "did this customer actually get anything". Carrying them on the row costs one
/// more join and saves opening every order to find the one that went wrong.
/// </summary>
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

    /// <summary>Which gateway took the money: "Bog", "Stripe", or "Promo" for a free order.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// The gateway's own reference for the payment — BOG's <c>transaction_id</c>, Stripe's
    /// payment intent. It is the number to quote when asking a bank what happened to a
    /// customer's money, and until now the only place it existed was a database column.
    /// </summary>
    public string? ProviderPaymentIntentId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? FulfilledAt { get; set; }

    /// <summary>Where the book itself got to — Completed, Failed, still generating.</summary>
    public string? BookStatus { get; set; }

    /// <summary>When the parent last opened the digital book. Null means they never have.</summary>
    public DateTime? LastReadAt { get; set; }

    /// <summary>True once either the reading or the print PDF has been built.</summary>
    public bool HasPdf { get; set; }

    /// <summary>Where the parcel is, for a print order. Null for digital-only.</summary>
    public string? PrintStatus { get; set; }
}

/// <summary>
/// Everything about one order, for the panel that opens under its row: who bought it, what
/// they got, and where the parcel is. Assembled from four tables in one round trip, because a
/// panel that opens is a panel someone is waiting on.
/// </summary>
public sealed class AdminOrderDetailResponse
{
    public AdminOrderRow Order { get; set; } = new();
    public AdminOrderCustomer Customer { get; set; } = new();

    /// <summary>Null when the order never produced a book — an unpaid or failed order.</summary>
    public AdminOrderBook? Book { get; set; }

    /// <summary>Null for a digital-only order.</summary>
    public AdminOrderShipment? Shipment { get; set; }
}

public sealed class AdminOrderCustomer
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? DisplayName { get; set; }
    public string? PreferredLanguage { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>How many books and orders this parent has in total, this one included.</summary>
    public int BookCount { get; set; }
    public int OrderCount { get; set; }
}

public sealed class AdminOrderBook
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string? HeroName { get; set; }
    public string? WorldId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }
    public int StoryPageCount { get; set; }
    public string? StoryLanguage { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? ProgressMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastReadAt { get; set; }

    /// <summary>
    /// Whether a PDF exists to download. The URLs themselves are not returned: they are blob
    /// paths this API resolves, and an admin downloads through
    /// <c>GET /api/admin/orders/{id}/pdf</c> rather than by being handed storage internals.
    /// </summary>
    public bool HasReadingPdf { get; set; }
    public bool HasPrintPdf { get; set; }
}

public sealed class AdminOrderShipment
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string? PostalCode { get; set; }
    public string? Notes { get; set; }
    public string? TrackingCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
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
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Body of <c>PUT /api/admin/users/{id}/admin</c>.</summary>
public sealed class UpdateUserAdminRequest
{
    public bool IsAdmin { get; set; }
}
