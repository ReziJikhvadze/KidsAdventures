namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(User user, CancellationToken cancellationToken);
    Task<bool> UpdateSubscriptionTypeAsync(Guid userId, SubscriptionType subscriptionType, CancellationToken cancellationToken);
}
