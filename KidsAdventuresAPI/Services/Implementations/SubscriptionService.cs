using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Subscriptions;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using Stripe;
using Stripe.Checkout;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class SubscriptionService(
    IUserRepository userRepository,
    ISubscriptionRepository subscriptionRepository,
    IAdventurePackRepository adventurePackRepository,
    IOptions<StripeOptions> stripeOptions) : ISubscriptionService
{
    private readonly StripeOptions _stripe = stripeOptions.Value;

    public async Task EnsureGenerationAllowedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken)
                   ?? throw new UnauthorizedAccessException("User not found.");

        if (user.SubscriptionType == SubscriptionType.Premium)
        {
            return;
        }

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);
        var used = await adventurePackRepository.CountForMonthAsync(userId, monthStart, monthEnd, cancellationToken);

        if (used >= 1)
        {
            throw new InvalidOperationException("Free plan limit reached: 1 pack per month.");
        }
    }

    public async Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(Guid userId, string email, string planType, CancellationToken cancellationToken)
    {
        if (!string.Equals(planType, "Premium", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only Premium plan checkout is supported.");
        }

        StripeConfiguration.ApiKey = _stripe.SecretKey;

        var service = new SessionService();
        var session = await service.CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription",
            SuccessUrl = _stripe.SuccessUrl,
            CancelUrl = _stripe.CancelUrl,
            CustomerEmail = email,
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = _stripe.PremiumPriceId,
                    Quantity = 1
                }
            ],
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = userId.ToString()
            }
        }, cancellationToken: cancellationToken);

        return new CheckoutSessionResponse
        {
            SessionId = session.Id,
            CheckoutUrl = session.Url ?? string.Empty
        };
    }

    public async Task HandleWebhookAsync(string jsonPayload, string stripeSignature, CancellationToken cancellationToken)
    {
        StripeConfiguration.ApiKey = _stripe.SecretKey;

        var stripeEvent = EventUtility.ConstructEvent(jsonPayload, stripeSignature, _stripe.WebhookSecret);
        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
            {
                var session = stripeEvent.Data.Object as Session;
                if (session?.Metadata is null || !session.Metadata.TryGetValue("userId", out var userIdRaw) || !Guid.TryParse(userIdRaw, out var userId))
                {
                    return;
                }

                var subscriptionId = session.SubscriptionId ?? string.Empty;
                var customerId = session.CustomerId ?? string.Empty;
                await ActivatePremiumAsync(userId, customerId, subscriptionId, cancellationToken);
                break;
            }
            case "customer.subscription.deleted":
            {
                var sub = stripeEvent.Data.Object as Stripe.Subscription;
                if (sub?.Metadata is null || !sub.Metadata.TryGetValue("userId", out var userIdRaw) || !Guid.TryParse(userIdRaw, out var userId))
                {
                    return;
                }

                await userRepository.UpdateSubscriptionTypeAsync(userId, SubscriptionType.Free, cancellationToken);
                break;
            }
        }
    }

    private async Task ActivatePremiumAsync(Guid userId, string stripeCustomerId, string stripeSubscriptionId, CancellationToken cancellationToken)
    {
        _ = await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found for subscription activation.");

        await userRepository.UpdateSubscriptionTypeAsync(userId, SubscriptionType.Premium, cancellationToken);

        await subscriptionRepository.UpsertAsync(new Domain.Entities.Subscription
        {
            UserId = userId,
            StripeCustomerId = stripeCustomerId,
            StripeSubscriptionId = stripeSubscriptionId,
            PlanType = SubscriptionType.Premium,
            ActiveUntil = DateTime.UtcNow.AddYears(10)
        }, cancellationToken);
    }
}
