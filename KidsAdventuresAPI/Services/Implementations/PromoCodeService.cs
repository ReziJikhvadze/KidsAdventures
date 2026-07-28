using AdventurePacks.Api.DTOs.Orders;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class PromoCodeService(
    IPromoCodeRepository promoCodeRepository,
    ILogger<PromoCodeService> logger) : IPromoCodeService
{
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
        CancellationToken cancellationToken)
    {
        var subtotal = GelPricing.SubtotalFor(type, GelPricing.PackageFor(type, package));

        if (string.IsNullOrWhiteSpace(promoCode))
        {
            return new PricedOrder(subtotal, 0, subtotal, null, null);
        }

        var trimmed = promoCode.Trim();
        var code = await promoCodeRepository.GetByCodeAsync(trimmed, cancellationToken);
        if (code is null)
        {
            return new PricedOrder(subtotal, 0, subtotal, null, Invalid(trimmed, Messages.Unknown));
        }

        var rejection = await RejectionReasonAsync(code, userId, cancellationToken);
        if (rejection is not null)
        {
            return new PricedOrder(subtotal, 0, subtotal, null, Invalid(code.Code, rejection, code));
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

        return new PricedOrder(subtotal, discount, subtotal - discount, code, quote);
    }

    public async Task<QuoteResponse> QuoteAsync(
        Guid userId,
        OrderType type,
        OrderPackage package,
        string? promoCode,
        CancellationToken cancellationToken)
    {
        var priced = await PriceAsync(userId, type, package, promoCode, cancellationToken);
        return new QuoteResponse
        {
            Currency = GelPricing.Currency,
            SubtotalMinor = priced.SubtotalMinor,
            DiscountMinor = priced.DiscountMinor,
            TotalMinor = priced.TotalMinor,
            IsFree = priced.IsFree,
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
