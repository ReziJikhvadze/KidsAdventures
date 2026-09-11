using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Orders;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class PromoCodeService(
    IPromoCodeRepository promoCodeRepository,
    IOptions<StripeOptions> stripeOptions,
    ILogger<PromoCodeService> logger) : IPromoCodeService
{
    private readonly StripeOptions _stripe = stripeOptions.Value;

    private static class Messages
    {
        public const string Unknown = "ასეთი პრომოკოდი არ არსებობს.";
        public const string Expired = "პრომოკოდს ვადა გაუვიდა.";
        public const string NotStarted = "პრომოკოდი ჯერ არ არის აქტიური.";
        public const string Exhausted = "პრომოკოდი ამოიწურა.";
        public const string AlreadyUsed = "ამ პრომოკოდი უკვე გამოიყენე.";
        public const string Inactive = "პრომოკოდი გათიშულია.";
    }

    public async Task<PricedOrder> PriceAsync(
        Guid userId,
        OrderType type,
        OrderPackage package,
        string? promoCode,
        bool giftWrap,
        int quantity,
        DeliveryChoice delivery,
        CancellationToken cancellationToken)
    {
        var effectivePackage = GelPricing.PackageFor(type, package);
        /* Clamped once, here, and carried on the result: everything downstream — the order row,
           the print queue, the line the parent reads — has to mean the same number. */
        var copies = GelPricing.QuantityFor(type, effectivePackage, quantity);
        /*
          Wrapping is part of the subtotal, not a surcharge bolted on after the discount.

          A percentage code therefore takes its cut of the wrapping too, and a full-discount
          code makes the whole order free rather than leaving five lari to collect. Either of
          those is defensible; what is not is a total the parent cannot arrive at themselves
          from the lines they were shown.
        */
        var wrapping = GelPricing.GiftWrapFor(effectivePackage, giftWrap, copies);
        /* Same rule, same reason: only a parcel is delivered, and the figure is the server's. */
        var courier = GelPricing.DeliveryFor(effectivePackage, delivery);
        var shipped = courier > 0 || delivery.Option != DeliveryOption.None
            ? delivery.Option
            : DeliveryOption.None;
        var subtotal = GelPricing.SubtotalFor(type, effectivePackage, giftWrap, copies, delivery);

        if (string.IsNullOrWhiteSpace(promoCode))
        {
            return new PricedOrder(subtotal, 0, subtotal, null, null, wrapping, copies, courier, shipped);
        }

        var trimmed = promoCode.Trim();
        var code = await promoCodeRepository.GetByCodeAsync(trimmed, cancellationToken);
        if (code is null)
        {
            return new PricedOrder(subtotal, 0, subtotal, null, Invalid(trimmed, Messages.Unknown), wrapping, copies, courier, shipped);
        }

        var rejection = await RejectionReasonAsync(code, userId, cancellationToken);
        if (rejection is not null)
        {
            return new PricedOrder(
                subtotal, 0, subtotal, null, Invalid(code.Code, rejection, code), wrapping, copies, courier, shipped);
        }

        var discount = code.DiscountFor(subtotal);
        var quote = new PromoQuote
        {
            Code = code.Code,
            Description = code.Description,
            IsValid = true,
            PercentOff = code.PercentOff,
            IsFullDiscount = code.IsFullDiscount,
            DiscountMinor = discount,
            Message = code.Description
        };

        return new PricedOrder(subtotal, discount, subtotal - discount, code, quote, wrapping, copies, courier, shipped);
    }

    public async Task<QuoteResponse> QuoteAsync(
        Guid userId,
        OrderType type,
        OrderPackage package,
        string? promoCode,
        bool giftWrap,
        int quantity,
        DeliveryChoice delivery,
        CancellationToken cancellationToken)
    {
        var priced = await PriceAsync(
            userId, type, package, promoCode, giftWrap, quantity, delivery, cancellationToken);
        return new QuoteResponse
        {
            Currency = GelPricing.Currency,
            SubtotalMinor = priced.SubtotalMinor,
            DeliveryMinor = priced.DeliveryMinor,
            DeliveryOption = priced.DeliveryName,
            DeliveryMinDays = GeorgianDelivery.For(priced.Delivery).MinDays,
            DeliveryMaxDays = GeorgianDelivery.For(priced.Delivery).MaxDays,
            DiscountMinor = priced.DiscountMinor,
            TotalMinor = priced.TotalMinor,
            GiftWrapMinor = priced.GiftWrapMinor,
            Quantity = priced.Quantity,
            // While payment is bypassed the quote reports free, so the checkout screen
            // stops asking for a card it will never charge. The prices themselves are
            // untouched — only the collection step is skipped.
            IsFree = priced.IsFree || _stripe.BypassPayment,
            Promo = priced.Quote
        };
    }

    public async Task<bool> TryRedeemAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.PromoCodeId is not { } promoCodeId || order.DiscountMinor <= 0)
        {
            return false;
        }

        var redeemed = await promoCodeRepository.TryRedeemAsync(new PromoRedemption
        {
            PromoCodeId = promoCodeId,
            UserId = order.UserId,
            OrderId = order.Id,
            DiscountMinor = order.DiscountMinor
        }, cancellationToken);

        if (!redeemed)
        {
            // Either a replay, or the code's cap filled up after we quoted it. Neither is
            // worth failing a paid order over — the parent was already charged the
            // discounted amount, so we honour it and record the discrepancy.
            logger.LogInformation(
                "Promo {PromoCodeId} not recorded for order {OrderId}; already redeemed or exhausted.",
                promoCodeId, order.Id);
        }

        return redeemed;
    }

    private async Task<string?> RejectionReasonAsync(
        PromoCode code,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        if (!code.IsActive)
        {
            return Messages.Inactive;
        }

        if (code.StartsAt is { } startsAt && startsAt > now)
        {
            return Messages.NotStarted;
        }

        if (code.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            return Messages.Expired;
        }

        if (!code.HasRedemptionsLeft)
        {
            return Messages.Exhausted;
        }

        if (code.OncePerUser &&
            await promoCodeRepository.HasUserRedeemedAsync(code.Id, userId, cancellationToken))
        {
            return Messages.AlreadyUsed;
        }

        return null;
    }

    private static PromoQuote Invalid(string code, string message, PromoCode? known = null) => new()
    {
        Code = known?.Code ?? code.ToUpperInvariant(),
        Description = known?.Description,
        IsValid = false,
        PercentOff = known?.PercentOff,
        IsFullDiscount = known?.IsFullDiscount ?? false,
        DiscountMinor = 0,
        Message = message
    };
}
