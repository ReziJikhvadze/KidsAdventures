using AdventurePacks.Api.DTOs.Subscriptions;

namespace AdventurePacks.Api.Services.Interfaces;

public interface ISubscriptionService
{
    Task<AccountBalanceResponse> GetAccountBalanceAsync(Guid userId, CancellationToken cancellationToken);
    Task EnsureGenerationAllowedAsync(Guid userId, CancellationToken cancellationToken);
    Task EnsurePdfGenerationAllowedAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> TryChargePdfCreditAsync(Guid userId, Guid packId, CancellationToken cancellationToken);
    Task RefundPdfCreditIfChargedAsync(Guid userId, Guid packId, CancellationToken cancellationToken);
    Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(Guid userId, string email, string planType, CancellationToken cancellationToken);
    Task<AccountBalanceResponse> ConfirmCheckoutSessionAsync(Guid userId, string sessionId, CancellationToken cancellationToken);
    Task HandleWebhookAsync(string jsonPayload, string stripeSignature, CancellationToken cancellationToken);
}
