namespace AdventurePacks.Api.DTOs.Subscriptions;

public sealed class CreateCheckoutSessionRequest
{
    [Required]
    public string PlanType { get; set; } = "Book1";

    /// <summary>Optional payment provider: "stripe" or "dodo". Defaults to the server's preferred provider.</summary>
    public string? Provider { get; set; }

    /// <summary>Optional id of the specific book this purchase should unlock/illustrate (per-book checkout).</summary>
    public string? AdventurePackId { get; set; }
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
