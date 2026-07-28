namespace AdventurePacks.Api.Domain.Entities;

/// <summary>
/// A discount code. Either a percentage off or a full discount, never both — the
/// database check constraint enforces that, so a total can only be computed one way.
/// </summary>
public sealed class PromoCode
{
    public Guid Id { get; set; }

    /// <summary>Stored upper-case; lookups normalise before they query.</summary>
    public string Code { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>1-100, or null when <see cref="IsFullDiscount"/> is set.</summary>
    public int? PercentOff { get; set; }

    /// <summary>Brings the total to zero, which skips the payment provider.</summary>
    public bool IsFullDiscount { get; set; }

    public int? MaxRedemptions { get; set; }
    public int RedemptionCount { get; set; }
    public bool OncePerUser { get; set; } = true;
    public DateTime? StartsAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsWithinWindow(DateTime utcNow) =>
        (StartsAt is null || StartsAt <= utcNow) && (ExpiresAt is null || ExpiresAt > utcNow);

    public bool HasRedemptionsLeft => MaxRedemptions is null || RedemptionCount < MaxRedemptions;

    public int DiscountFor(int subtotalMinor) =>
        IsFullDiscount ? subtotalMinor : GelPricing.PercentDiscount(subtotalMinor, PercentOff ?? 0);
}

public sealed class PromoRedemption
{
    public Guid Id { get; set; }
    public Guid PromoCodeId { get; set; }
    public Guid UserId { get; set; }
    public Guid OrderId { get; set; }
    public int DiscountMinor { get; set; }
    public DateTime RedeemedAt { get; set; } = DateTime.UtcNow;
}
