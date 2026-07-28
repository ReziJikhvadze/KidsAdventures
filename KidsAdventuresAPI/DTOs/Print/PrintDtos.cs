namespace AdventurePacks.Api.DTOs.Print;

/// <summary>
/// A Georgian delivery address. Postal codes and a second line are optional because
/// plenty of valid Georgian addresses have neither.
/// </summary>
public sealed class ShippingAddressRequest
{
    [Required, MaxLength(128)]
    public string RecipientName { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string RecipientPhone { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string City { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? Region { get; set; }

    [Required, MaxLength(256)]
    public string AddressLine1 { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? AddressLine2 { get; set; }

    [MaxLength(32)]
    public string? PostalCode { get; set; }

    [MaxLength(512)]
    public string? Notes { get; set; }

    /// <summary>Keep this address for next time, so the parent types it once.</summary>
    public bool SaveForLater { get; set; } = true;
}

public sealed class AddressResponse
{
    public Guid Id { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string? PostalCode { get; set; }
    public bool IsDefault { get; set; }

    /// <summary>Georgian delivery estimate for this city, e.g. "მიწოდება 4-5 სამუშაო დღეში".</summary>
    public string DeliveryEstimate { get; set; } = string.Empty;
}

public sealed class SaveAddressRequest
{
    public Guid? Id { get; set; }

    [Required, MaxLength(128)]
    public string RecipientName { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string RecipientPhone { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string City { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? Region { get; set; }

    [Required, MaxLength(256)]
    public string AddressLine1 { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? AddressLine2 { get; set; }

    [MaxLength(32)]
    public string? PostalCode { get; set; }

    public bool IsDefault { get; set; } = true;
}

/// <summary>What a parent sees about their parcel.</summary>
public sealed class PrintOrderResponse
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid BookId { get; set; }
    public string? BookTitle { get; set; }

    public PrintOrderStatus Status { get; set; }

    /// <summary>Georgian label for <see cref="Status"/>, ready to render.</summary>
    public string StatusLabel { get; set; } = string.Empty;

    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string? PostalCode { get; set; }
    public string? Notes { get; set; }

    public string? TrackingCode { get; set; }
    public string DeliveryEstimate { get; set; } = string.Empty;

    /// <summary>False once the parcel has shipped, which is when edits stop being possible.</summary>
    public bool CanEditAddress { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}

/// <summary>A row in the operations console, with the detail needed to pack a parcel.</summary>
public sealed class AdminPrintOrderResponse
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid BookId { get; set; }
    public string? BookTitle { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    public PrintOrderStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;

    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string? PostalCode { get; set; }
    public string? Notes { get; set; }
    public string? TrackingCode { get; set; }

    /// <summary>The print-ready PDF, once generation has produced one.</summary>
    public string? PdfUrl { get; set; }

    public int TotalMinor { get; set; }
    public string TotalFormatted { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}

public sealed class AdminPrintQueueResponse
{
    public List<AdminPrintOrderResponse> Orders { get; set; } = [];

    /// <summary>Count per status, keyed by the status name, for the console's tab badges.</summary>
    public Dictionary<string, int> Counts { get; set; } = [];
}

public sealed class UpdatePrintOrderStatusRequest
{
    /// <summary>"AwaitingPrint", "Printing", "Shipped", "Delivered" or "Cancelled".</summary>
    [Required, MaxLength(24)]
    public string Status { get; set; } = string.Empty;

    /// <summary>Courier tracking code. Required when moving to Shipped.</summary>
    [MaxLength(128)]
    public string? TrackingCode { get; set; }

    /// <summary>Set false to move the parcel without emailing the parent.</summary>
    public bool NotifyCustomer { get; set; } = true;
}
