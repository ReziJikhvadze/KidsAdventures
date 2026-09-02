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

    /// <summary>The child the book is about — what a parcel is actually identified by on a shelf.</summary>
    public string? HeroName { get; set; }

    /// <summary>
    /// Where the book itself got to.
    ///
    /// A parcel whose book is still generating, or Failed, must not go to a printer, and the queue
    /// could not say so: every row looked the same and the only way to find out was to open the
    /// order. It is one column on a join that was already being made.
    /// </summary>
    public string? BookStatus { get; set; }

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

    /// <summary>
    /// Whether a press interior exists to send to the binder.
    ///
    /// The URL used to be here, and it was a storage link handed to a browser — a link that
    /// outlives the request is a link that leaks a child's book, which is the rule every other
    /// file in this console already follows. The queue now says whether the file exists, and the
    /// file itself is downloaded through the order's own PDF route, which streams bytes.
    /// </summary>
    public bool HasPrintPdf { get; set; }

    /// <summary>
    /// True when the only file this book has is the READING copy, because no press file exists.
    ///
    /// The substitution itself is old and deliberate — a book made before the two renders were
    /// split has only one file, and a printer padding it as it always did beats an operator with
    /// nothing to send. What was wrong was that the substitution was silent: the queue offered a
    /// file labelled "print-ready" whose page count does not divide by four and whose spreads are
    /// laid out for a screen. Whoever forwards it to a binder is entitled to know which file it is.
    /// </summary>
    public bool PdfIsReadingCopyFallback { get; set; }

    public int TotalMinor { get; set; }
    public string TotalFormatted { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}

public sealed class AdminPrintQueueResponse
{
    public List<AdminPrintOrderResponse> Orders { get; set; } = [];

    /// <summary>Count per status, keyed by the status name, for the console's tab badges.</summary>
    public Dictionary<string, int> Counts { get; set; } = [];
}

/// <summary>
/// One row of the operations queue, as one SQL statement produces it.
///
/// It exists because the queue used to be built by loading each parcel and then asking for its
/// book, its buyer and its order one at a time — four round trips per row, fifty rows a page, and
/// a screen that got slower every week the business grew. The join was always available; nothing
/// on this row is derived from anything the parcel table does not already point at.
///
/// A row rather than the response class because Dapper materializes it, and the response carries
/// <see cref="DateTimeOffset"/> timestamps that Dapper cannot write into from a datetime2 column.
/// </summary>
public sealed class AdminPrintQueueRow
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid BookId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = string.Empty;

    public string? BookTitle { get; set; }
    public string? BookStatus { get; set; }

    /// <summary>
    /// <c>beki</c> or <c>legacy</c>. Not shown; it decides whether a missing press file is an
    /// incident (a composite book's press interior was withheld or never written) or simply a book
    /// made before the two renders were split apart.
    /// </summary>
    public string? BookPipeline { get; set; }

    public string? HeroName { get; set; }
    public bool HasPrintPdf { get; set; }
    public bool PdfIsReadingCopyFallback { get; set; }

    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string? PostalCode { get; set; }
    public string? Notes { get; set; }
    public string? TrackingCode { get; set; }

    public int TotalMinor { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
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
