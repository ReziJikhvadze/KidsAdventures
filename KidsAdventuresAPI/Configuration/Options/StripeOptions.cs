namespace AdventurePacks.Api.Configuration.Options;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>When false, Stripe checkout and webhooks are disabled (use DodoPayments instead).</summary>
    public bool Enabled { get; set; }

    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Stripe Price ID (price_...) for the single $4.99 one-book purchase.</summary>
    public string BookPriceId { get; set; } = string.Empty;
    public string SuccessUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
}
