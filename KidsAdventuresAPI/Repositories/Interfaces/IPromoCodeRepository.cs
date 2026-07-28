namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IPromoCodeRepository
{
    /// <summary>Case-insensitive lookup; the caller passes the code as typed.</summary>
    Task<PromoCode?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    Task<PromoCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> HasUserRedeemedAsync(Guid promoCodeId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the redemption and bumps the code's counter in one transaction, and only
    /// if the cap still allows it. Returns false when the code ran out between the quote
    /// and the payment, or when this order already redeemed something.
    /// </summary>
    Task<bool> TryRedeemAsync(PromoRedemption redemption, CancellationToken cancellationToken);
}
