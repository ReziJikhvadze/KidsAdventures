namespace AdventurePacks.Api.DTOs.Subscriptions;

public sealed class CreateCheckoutSessionRequest
{
    [Required]
    public string PlanType { get; set; } = "Books5";

    /// <summary>Optional payment provider: "stripe" or "dodo". Defaults to the server's preferred provider.</summary>
    public string? Provider { get; set; }
}

public sealed class CheckoutSessionResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = string.Empty;
}

public sealed class AccountBalanceResponse
{
    public int BookCredits { get; set; }
    public int StoriesUsedThisMonth { get; set; }
    public int StoriesAllowedThisMonth { get; set; }
    public int StoriesRemainingThisMonth { get; set; }
    public int WelcomeStoryRemaining { get; set; }
    public SubscriptionType SubscriptionType { get; set; }
    public bool HasUnlimitedPdf { get; set; }
}

public sealed class ConfirmCheckoutSessionRequest
{
    public string? SessionId { get; set; }

    public string? PaymentId { get; set; }

    /// <summary>Optional payment provider: "stripe" or "dodo". Inferred from ids when omitted.</summary>
    public string? Provider { get; set; }
}
