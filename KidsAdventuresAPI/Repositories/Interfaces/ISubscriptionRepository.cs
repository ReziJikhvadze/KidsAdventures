namespace AdventurePacks.Api.Repositories.Interfaces;

public interface ISubscriptionRepository
{
    Task<Subscription?> GetCurrentByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task UpsertAsync(Subscription subscription, CancellationToken cancellationToken);
}
