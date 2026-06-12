namespace AdventurePacks.Api.Configuration.Options;

public sealed class DodoPaymentsOptions
{
    public const string SectionName = "DodoPayments";

    /// <summary>When true, checkout and webhooks use Dodo instead of Stripe.</summary>
    public bool Enabled { get; set; }

    public string ApiKey { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>When true, calls https://test.dodopayments.com instead of live.</summary>
    public bool UseTestMode { get; set; } = true;

    public string Books3ProductId { get; set; } = string.Empty;

    public string Books5ProductId { get; set; } = string.Empty;

    public string Books15ProductId { get; set; } = string.Empty;

    /// <summary>Redirect after successful payment (Dodo may append payment_id and status).</summary>
    public string SuccessUrl { get; set; } = string.Empty;

    public string CancelUrl { get; set; } = string.Empty;
}
