namespace AdventurePacks.Api.DTOs.Subscriptions;

public sealed class CreateCheckoutSessionRequest
{
    [Required]
    public string PlanType { get; set; } = "Books5";
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
    [Required]
    public string SessionId { get; set; } = string.Empty;
}
