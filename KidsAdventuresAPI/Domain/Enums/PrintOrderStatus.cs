namespace AdventurePacks.Api.Domain.Enums;

/// <summary>
/// Where a physical parcel is. Mirrors <c>CK_PrintOrders_Status</c>; the names are
/// persisted as strings, so renaming a member is a schema change.
/// </summary>
public enum PrintOrderStatus
{
    /// <summary>Paid and queued, waiting for the print run.</summary>
    AwaitingPrint = 0,

    Printing = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4
}
