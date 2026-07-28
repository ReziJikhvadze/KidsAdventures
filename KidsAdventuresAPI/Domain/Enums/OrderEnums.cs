namespace AdventurePacks.Api.Domain.Enums;

/// <summary>What the parent is buying. Mirrors <c>CK_Orders_Type</c>.</summary>
public enum OrderType
{
    /// <summary>A new book: pay first, then the full story is generated.</summary>
    NewBook = 0,

    /// <summary>A printed copy of a book that was already bought digitally.</summary>
    PrintUpgrade = 1
}

/// <summary>Mirrors <c>CK_Orders_Package</c>.</summary>
public enum OrderPackage
{
    /// <summary>Reader plus PDF.</summary>
    Digital = 0,

    /// <summary>Reader, PDF, and a printed hardback posted to an address.</summary>
    Print = 1
}

/// <summary>
/// Mirrors <c>CK_Orders_Status</c>.
///
/// <see cref="Paid"/> and <see cref="Fulfilled"/> are deliberately distinct: money has
/// arrived long before a six-page illustrated book exists, and the reader must be able
/// to tell "we have your payment, the story is being written" from "here it is".
/// </summary>
public enum OrderStatus
{
    Pending = 0,
    Paid = 1,
    Fulfilled = 2,
    Failed = 3,
    Cancelled = 4,
    Refunded = 5
}
