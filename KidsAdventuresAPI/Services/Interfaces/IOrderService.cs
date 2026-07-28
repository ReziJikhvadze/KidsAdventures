using AdventurePacks.Api.DTOs.Orders;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IOrderService
{
    Task<QuoteResponse> QuoteAsync(Guid userId, QuoteRequest request, CancellationToken cancellationToken);

    /// <summary>Starts checkout for a brand-new book.</summary>
    Task<CheckoutResponse> CreateBookOrderAsync(
        Guid userId,
        CreateOrderRequest request,
        CancellationToken cancellationToken);

    /// <summary>Starts checkout for a printed copy of a book already owned digitally.</summary>
    Task<CheckoutResponse> CreatePrintUpgradeOrderAsync(
        Guid userId,
        CreatePrintUpgradeOrderRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderResponse>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<OrderStatusResponse> GetStatusAsync(Guid userId, Guid orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Reconciles an order against the provider when the parent lands back on the success
    /// page. Belt to the webhook's braces: whichever arrives first wins, and the other is
    /// a no-op.
    /// </summary>
    Task<OrderStatusResponse> ConfirmAsync(Guid userId, Guid orderId, CancellationToken cancellationToken);

    Task<bool> CancelAsync(Guid userId, Guid orderId, CancellationToken cancellationToken);

    Task HandleStripeWebhookAsync(
        string jsonPayload,
        string stripeSignature,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retries fulfilment for orders that were paid but never produced a book, so a crash
    /// between the two does not leave a parent charged and empty-handed. Run on a schedule.
    /// </summary>
    Task RetryStalledFulfilmentAsync();
}
