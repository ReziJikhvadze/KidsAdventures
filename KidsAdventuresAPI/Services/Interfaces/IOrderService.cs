using Hangfire;

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
    /// BOG's callback. Takes the raw bytes because the signature is over exactly what was
    /// posted, and re-serializing a parsed body would not reproduce them.
    ///
    /// Returns false only when the signature fails, so the endpoint can answer 400: BOG
    /// redelivers on 5xx, and redelivering a forged payload achieves nothing.
    /// </summary>
    Task<bool> HandleBogWebhookAsync(
        byte[] payload,
        string? signature,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retries fulfilment for orders that were paid but never produced a book, so a crash
    /// between the two does not leave a parent charged and empty-handed. Run on a schedule.
    /// </summary>
    Task RetryStalledFulfilmentAsync();

    /// <summary>
    /// Fulfils one paid order, as a background job — the single door every retry goes through.
    ///
    /// It exists to hold the lock. <c>Order.BookId</c> is fulfilment's idempotency marker, but it
    /// is read off an <see cref="Order"/> loaded before the work starts: two callers that each
    /// loaded the row while it was still null both see "no book yet" and both make one. That was
    /// survivable while the five-minute sweep was the only caller; an operator with a retry button
    /// can race it deliberately.
    ///
    /// So the sweep and the console both enqueue this, and Hangfire's per-order lock lets exactly
    /// one of them run. The order is re-read inside, after the lock, which is what makes the
    /// marker mean anything.
    /// </summary>
    [DisableConcurrentExecution("order-fulfil:{0}", 1800)]
    Task FulfilOrderAsync(Guid orderId);

    /// <summary>
    /// Queues a retry for one paid order, on demand from the operations console.
    ///
    /// Returns false when the order is not in a state that can be retried — unpaid, or already
    /// fulfilled — so the console can say which, rather than reporting a queued job that will
    /// quietly decide to do nothing.
    /// </summary>
    Task<bool> RequeueFulfilmentAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether <see cref="RequeueFulfilmentAsync"/> would accept this order right now — the same
    /// rule, asked without acting on it, so the console can decide whether to show a retry
    /// button rather than offer one that answers "no".
    ///
    /// False for an order that does not exist.
    /// </summary>
    Task<bool> CanRedriveAsync(Guid orderId, CancellationToken cancellationToken);
}
