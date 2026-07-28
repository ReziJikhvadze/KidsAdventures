namespace AdventurePacks.Api.Configuration.Options;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>
    /// When false, only zero-total orders can complete: a paid checkout has nowhere to
    /// send the parent, so the order service refuses rather than creating a dead order.
    /// </summary>
    public bool Enabled { get; set; }

    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Price ID for the 14 GEL digital book.</summary>
    public string DigitalPriceId { get; set; } = string.Empty;

    /// <summary>Price ID for the 79 GEL printed-plus-digital book.</summary>
    public string PrintPriceId { get; set; } = string.Empty;

    /// <summary>Price ID for the 65 GEL print upgrade of a book already bought digitally.</summary>
    public string PrintUpgradePriceId { get; set; } = string.Empty;

    /// <summary>
    /// Absolute base URL of the site, used to build the provider return URLs. Without it
    /// Stripe has nowhere to send the parent back to.
    /// </summary>
    public string SiteBaseUrl { get; set; } = string.Empty;

    /// <summary>Frontend path that polls the order after the provider returns.</summary>
    public string SuccessPath { get; set; } = "/create";

    public string CancelPath { get; set; } = "/create";

    /// <summary>
    /// Apple Pay and Google Pay ride on the "card" payment method: Stripe surfaces the
    /// wallet automatically when the browser supports it and the domain is verified, so
    /// there is nothing extra to request here. Set false only to force a card form.
    /// </summary>
    public bool EnableWallets { get; set; } = true;

    /// <summary>
    /// Discounts are applied to our own totals, not Stripe coupons, so a discounted order
    /// is billed as an ad-hoc line item instead of the catalogue Price. Turn this off only
    /// if every discount is mirrored as a Stripe coupon.
    /// </summary>
    public bool AllowAdHocAmounts { get; set; } = true;

    /// <summary>How long a checkout session stays open. Stripe's own minimum is 30 minutes.</summary>
    public int SessionExpiryMinutes { get; set; } = 60;
}
