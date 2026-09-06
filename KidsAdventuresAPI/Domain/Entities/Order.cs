namespace AdventurePacks.Api.Domain.Entities;

/// <summary>
/// One purchase. Replaces the book-credit wallet: there is no balance to top up, a
/// parent buys a specific book and that order is what authorises its generation.
/// </summary>
public sealed class Order
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Null until the book row exists; set for a print upgrade from the start.</summary>
    public Guid? BookId { get; set; }

    public OrderType Type { get; set; }
    public OrderPackage Package { get; set; }
    public string Currency { get; set; } = GelPricing.Currency;

    public int SubtotalMinor { get; set; }
    public int DiscountMinor { get; set; }
    public int TotalMinor { get; set; }

    public Guid? PromoCodeId { get; set; }

    /// <summary>
    /// Whether the parcel is to be wrapped, and therefore whether the five lari in
    /// <see cref="SubtotalMinor"/> was for that. Kept on the order rather than only in the
    /// total because the print queue has to know what to do with the parcel weeks later.
    /// </summary>
    public bool GiftWrap { get; set; }

    /// <summary>
    /// How many printed copies were bought. Always 1 for a digital order and for a print
    /// upgrade. The print queue reads it: one parcel, this many books in it.
    /// </summary>
    public int Quantity { get; set; } = 1;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>
    /// "Bog" or "Stripe", or "Promo" when a full-discount code skipped the provider
    /// entirely. Recorded per order rather than read from configuration, so switching
    /// gateways does not strand the orders the previous one is still holding.
    /// </summary>
    public string Provider { get; set; } = OrderProviders.Stripe;

    public string? ProviderSessionId { get; set; }
    public string? ProviderPaymentIntentId { get; set; }

    /// <summary>
    /// The create-journey draft, frozen at checkout. Fulfilment reads the book to build
    /// from here, so a webhook arriving hours later needs nothing from the client.
    /// </summary>
    public string? DraftJson { get; set; }

    /// <summary>
    /// The delivery address, frozen at checkout, for a print or print-upgrade order.
    /// Null for a digital-only order.
    /// </summary>
    public string? ShippingJson { get; set; }

    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public DateTime? FulfilledAt { get; set; }

    public bool IsPaid => Status is OrderStatus.Paid or OrderStatus.Fulfilled;

    /// <summary>A GIFT100 order: nothing to collect, so no provider is involved.</summary>
    public bool IsFree => TotalMinor == 0;
}

public static class OrderProviders
{
    public const string Stripe = "Stripe";

    /// <summary>Bank of Georgia's e-commerce gateway.</summary>
    public const string Bog = "Bog";

    /// <summary>Used when a full-discount promo brought the total to zero.</summary>
    public const string Promo = "Promo";
}
