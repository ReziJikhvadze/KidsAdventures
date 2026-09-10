using AdventurePacks.Api.Domain;
using AdventurePacks.Api.DTOs.Orders;

namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>The outcome of pricing an order, with the promo already applied.</summary>
public sealed record PricedOrder(
    int SubtotalMinor,
    int DiscountMinor,
    int TotalMinor,
    PromoCode? Promo,
    PromoQuote? Quote,
    int GiftWrapMinor = 0,
    int Quantity = 1,
    int DeliveryMinor = 0,
    DeliveryOption Delivery = DeliveryOption.None)
{
    public bool IsFree => TotalMinor == 0;

    /// <summary>Whether wrapping was actually charged — the request asked and the package allowed it.</summary>
    public bool GiftWrap => GiftWrapMinor > 0;

    /// <summary>The delivery this order was priced for, by the name a request and a row carry.</summary>
    public string? DeliveryName => Delivery == DeliveryOption.None ? null : Delivery.ToString();
}

public interface IPromoCodeService
{
    /// <summary>
    /// Prices a package, applying the promo when it is usable. An unusable code never
    /// throws: the quote comes back with the full price and a Georgian explanation, so
    /// the checkout panel can show the reason inline instead of as an error.
    /// </summary>
    Task<PricedOrder> PriceAsync(
        Guid userId,
        OrderType type,
        OrderPackage package,
        string? promoCode,
        bool giftWrap,
        int quantity,
        DeliveryChoice delivery,
        CancellationToken cancellationToken);

    Task<QuoteResponse> QuoteAsync(
        Guid userId,
        OrderType type,
        OrderPackage package,
        string? promoCode,
        bool giftWrap,
        int quantity,
        DeliveryChoice delivery,
        CancellationToken cancellationToken);

    /// <summary>
    /// Burns the code against a paid order. Idempotent per order, so a replayed webhook
    /// cannot consume a limited code twice.
    /// </summary>
    Task<bool> TryRedeemAsync(Order order, CancellationToken cancellationToken);
}
