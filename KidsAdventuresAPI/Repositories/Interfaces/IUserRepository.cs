namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    Task<User?> GetByConfirmationTokenAsync(string token, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(User user, CancellationToken cancellationToken);
    Task<bool> UpdateSubscriptionTypeAsync(Guid userId, SubscriptionType subscriptionType, CancellationToken cancellationToken);
    Task<bool> ConfirmEmailAsync(Guid userId, CancellationToken cancellationToken);
    Task AddBookCreditsAsync(Guid userId, int credits, CancellationToken cancellationToken);
    Task<bool> TryConsumeBookCreditAsync(Guid userId, CancellationToken cancellationToken);
    Task RefundBookCreditAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> TryConsumeWelcomeStoryAsync(Guid userId, CancellationToken cancellationToken);
    Task RefundWelcomeStoryAsync(Guid userId, CancellationToken cancellationToken);
}
