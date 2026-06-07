namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IBookCreditPurchaseRepository
{
    Task<bool> TryRecordPurchaseAsync(
        Guid userId,
        string stripeSessionId,
        int creditsAdded,
        string planType,
        CancellationToken cancellationToken);
}
