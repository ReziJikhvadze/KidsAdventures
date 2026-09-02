namespace AdventurePacks.Api.DTOs.Admin;

/// <summary>
/// One discount code, as the console shows it.
///
/// The names are the console's, not the table's: <c>PercentOff</c> is <c>discountPercent</c> here
/// and <c>StartsAt</c>/<c>ExpiresAt</c> are <c>validFromUtc</c>/<c>validUntilUtc</c>. That is a
/// deliberate translation rather than an oversight — the entity's names date from the checkout and
/// the console's say what an operator is looking at — and the timestamps become
/// <see cref="DateTimeOffset"/> on the way out, because a Kind=Unspecified DateTime is a date the
/// browser silently shifts by its own offset.
/// </summary>
/// <param name="RedemptionCount">
/// How many orders have burned this code. Read-only from here: it is part of the price of orders
/// that already happened, and nothing in this console may edit it.
/// </param>
public sealed record AdminPromoCodeRow(
    Guid Id,
    string Code,
    int? DiscountPercent,
    bool IsFullDiscount,
    int? MaxRedemptions,
    int RedemptionCount,
    bool OncePerUser,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Body of <c>POST /api/admin/promo-codes</c>.
///
/// A code is either a percentage off or free, never both — the table's CHECK constraint says so,
/// and this endpoint refuses the contradiction with a Georgian 400 rather than letting it arrive
/// as a 500 out of a form submission.
/// </summary>
public sealed class AdminCreatePromoCodeRequest
{
    /// <summary>Upper-cased before it is stored; lookups at checkout normalise the same way.</summary>
    public string? Code { get; set; }

    /// <summary>1–100. Must be absent when <see cref="IsFullDiscount"/> is set.</summary>
    public int? DiscountPercent { get; set; }

    /// <summary>Brings the total to zero, which skips the payment provider entirely.</summary>
    public bool IsFullDiscount { get; set; }

    /// <summary>Null means unlimited. One or more when set.</summary>
    public int? MaxRedemptions { get; set; }

    /// <summary>Defaults to true, which is what almost every campaign code wants.</summary>
    public bool OncePerUser { get; set; } = true;

    public DateTimeOffset? ValidFromUtc { get; set; }

    public DateTimeOffset? ValidUntilUtc { get; set; }
}

/// <summary>
/// Body of <c>PUT /api/admin/promo-codes/{id}</c> — a patch, not a replacement.
///
/// The three fields distinguish "not mentioned" from "explicitly null", which a plain nullable
/// property cannot: <c>{ "isActive": false }</c> must switch a code off without also clearing its
/// expiry, and <c>{ "validUntilUtc": null }</c> must make a code open-ended rather than being read
/// as "leave it alone". The setters record that they ran, and System.Text.Json only calls a setter
/// for a property the body actually carried.
///
/// Nothing here can change the discount itself. A code that has been handed out is a promise about
/// a price; the way to change the price is a new code.
/// </summary>
public sealed class AdminUpdatePromoCodeRequest
{
    private bool? _isActive;
    private int? _maxRedemptions;
    private DateTimeOffset? _validUntilUtc;

    public bool? IsActive
    {
        get => _isActive;
        set { _isActive = value; IsActiveSpecified = true; }
    }

    public int? MaxRedemptions
    {
        get => _maxRedemptions;
        set { _maxRedemptions = value; MaxRedemptionsSpecified = true; }
    }

    public DateTimeOffset? ValidUntilUtc
    {
        get => _validUntilUtc;
        set { _validUntilUtc = value; ValidUntilUtcSpecified = true; }
    }

    [JsonIgnore]
    public bool IsActiveSpecified { get; private set; }

    [JsonIgnore]
    public bool MaxRedemptionsSpecified { get; private set; }

    [JsonIgnore]
    public bool ValidUntilUtcSpecified { get; private set; }
}
