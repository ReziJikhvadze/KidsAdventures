namespace AdventurePacks.Api.Configuration.Options;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";
    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string Books3PriceId { get; set; } = string.Empty;
    public string Books5PriceId { get; set; } = string.Empty;
    public string Books15PriceId { get; set; } = string.Empty;
    public string SuccessUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
}
