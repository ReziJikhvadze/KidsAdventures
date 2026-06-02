using AdventurePacks.Api.DTOs.Subscriptions;

namespace AdventurePacks.Api.Services.Interfaces;

public interface ISubscriptionService
{
    Task EnsureGenerationAllowedAsync(Guid userId, CancellationToken cancellationToken);
    Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(Guid userId, string email, string planType, CancellationToken cancellationToken);
    Task HandleWebhookAsync(string jsonPayload, string stripeSignature, CancellationToken cancellationToken);
}
