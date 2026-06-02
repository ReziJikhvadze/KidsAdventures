namespace AdventurePacks.Api.Domain.Entities;

public sealed class Subscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    [MaxLength(100)]
    public string StripeCustomerId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string StripeSubscriptionId { get; set; } = string.Empty;

    public SubscriptionType PlanType { get; set; } = SubscriptionType.Free;
    public DateTime ActiveUntil { get; set; } = DateTime.UtcNow;
}
