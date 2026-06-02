namespace AdventurePacks.Api.DTOs.Subscriptions;

public sealed class CreateCheckoutSessionRequest
{
    [Required]
    public string PlanType { get; set; } = "Premium";
}

public sealed class CheckoutSessionResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = string.Empty;
}
