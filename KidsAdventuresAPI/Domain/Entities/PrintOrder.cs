namespace AdventurePacks.Api.Domain.Entities;

/// <summary>
/// A physical parcel to print and ship.
///
/// The address is copied onto the row rather than referenced, because a parent
/// editing their saved address next month must not rewrite where a parcel already
/// in transit was sent.
/// </summary>
public sealed class PrintOrder
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

    public PrintOrderStatus Status { get; set; } = PrintOrderStatus.AwaitingPrint;
    public string? TrackingCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}

/// <summary>
/// The reusable address book behind "use saved address". Distinct from the copy on
/// <see cref="PrintOrder"/>: this one is allowed to change.
/// </summary>
public sealed class UserAddress
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string? PostalCode { get; set; }

    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
