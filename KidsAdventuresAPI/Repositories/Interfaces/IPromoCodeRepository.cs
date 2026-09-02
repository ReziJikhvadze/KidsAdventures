namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IPromoCodeRepository
{
    /// <summary>Case-insensitive lookup; the caller passes the code as typed.</summary>
    Task<PromoCode?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    Task<PromoCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Every code, newest first — the admin list.
    ///
    /// Unpaged deliberately. Promo codes are a hand-written set of campaign names, not a growing
    /// log; there are tens of them, and a page control over a list that short is furniture. If a
    /// deployment ever has thousands, the console will say so by being slow, which is a better
    /// signal than a paginator nobody ever needed.
    /// </summary>
    Task<IReadOnlyList<PromoCode>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a code. False when one with that name already exists — the unique index is the
    /// arbiter, so two operators creating <c>BEKI2026</c> at once produce one code and one 409
    /// rather than a duplicate-key 500.
    /// </summary>
    Task<bool> CreateAsync(PromoCode promoCode, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the three things an operator may change about a live code: whether it works, how
    /// many more times, and until when.
    ///
    /// The discount itself and the redemption count are not writable from here. A code that has
    /// been handed out is a promise about a price, and the counter is part of the price of orders
    /// that already happened. False when there is no such code.
    /// </summary>
    Task<bool> UpdateAdminFieldsAsync(
        Guid id,
        bool isActive,
        int? maxRedemptions,
        DateTime? expiresAt,
        CancellationToken cancellationToken);

    Task<bool> HasUserRedeemedAsync(Guid promoCodeId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the redemption and bumps the code's counter in one transaction, and only
    /// if the cap still allows it. Returns false when the code ran out between the quote
    /// and the payment, or when this order already redeemed something.
    /// </summary>
    Task<bool> TryRedeemAsync(PromoRedemption redemption, CancellationToken cancellationToken);
}
