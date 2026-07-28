namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IOrderRepository
{
    Task<Guid> CreateAsync(Order order, CancellationToken cancellationToken);

    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Order?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    Task<Order?> GetByProviderSessionIdAsync(string providerSessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Paid or fulfilled orders for a book. Used to tell what a parent already owns.</summary>
    Task<IReadOnlyList<Order>> GetPaidForBookAsync(Guid bookId, CancellationToken cancellationToken);

    Task AttachProviderSessionAsync(Guid id, string providerSessionId, CancellationToken cancellationToken);

    Task SetBookIdAsync(Guid id, Guid bookId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves Pending to Paid, once. Returns false when the order was already paid, which
    /// is what makes webhook replays and the success-page confirmation safe to run twice.
    /// </summary>
    Task<bool> TryMarkPaidAsync(
        Guid id,
        string? providerPaymentIntentId,
        CancellationToken cancellationToken);

    /// <summary>Moves Paid to Fulfilled, once.</summary>
    Task<bool> TryMarkFulfilledAsync(Guid id, CancellationToken cancellationToken);

    Task MarkFailedAsync(Guid id, string reason, CancellationToken cancellationToken);

    Task<bool> TryCancelAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Orders that were paid but whose fulfilment never completed. The sweeper retries
    /// these, so a crash between "money taken" and "book generated" is not permanent.
    /// </summary>
    Task<IReadOnlyList<Order>> GetStalledPaidAsync(DateTime paidBeforeUtc, int limit, CancellationToken cancellationToken);
}
