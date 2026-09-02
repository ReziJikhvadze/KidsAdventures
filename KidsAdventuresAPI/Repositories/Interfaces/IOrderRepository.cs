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

    /// <summary>
    /// An operator's own mark on an order — refunded, or cancelled — written only while the order
    /// is still in one of the statuses that transition allows.
    ///
    /// Its own method rather than a reuse of <see cref="MarkFailedAsync"/> or
    /// <see cref="TryCancelAsync"/>, and the difference is the point of both of those. MarkFailedAsync
    /// refuses to move a paid order at all, which is right for a generation failure and wrong for a
    /// refund. TryCancelAsync is scoped to the owning user, because it backs a parent cancelling
    /// their own checkout; an operator is not the owner and never will be.
    ///
    /// The allowed set is passed in rather than hard-coded so that the rule the console explains to
    /// the operator and the rule the UPDATE enforces are the same list, and false means the order
    /// moved between the two — two admins clicking at once, or a webhook landing mid-decision.
    ///
    /// A default that refuses rather than an abstract member, the way
    /// <see cref="IAdventurePackRepository.UpdateTitleAsync"/> is: exactly one caller writes this,
    /// and every test double of this wide interface should not have to say it does not. False is
    /// the safe default — the console reports the refusal, and nothing has been written.
    /// </summary>
    Task<bool> TrySetAdminStatusAsync(
        Guid id,
        OrderStatus status,
        IReadOnlyCollection<OrderStatus> allowedFrom,
        string? failureReason,
        CancellationToken cancellationToken) => Task.FromResult(false);
}
