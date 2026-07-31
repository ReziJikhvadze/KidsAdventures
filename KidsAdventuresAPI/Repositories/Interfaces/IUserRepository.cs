namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    Task<User?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<User?> GetByConfirmationTokenAsync(string token, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(User user, CancellationToken cancellationToken);

    /// <summary>Development-only cleanup of seeded demo accounts and everything hanging off them.</summary>
    Task<int> PurgeDemoAccountsAsync(string emailSuffix, CancellationToken cancellationToken);
    Task<bool> UpdateSubscriptionTypeAsync(Guid userId, SubscriptionType subscriptionType, CancellationToken cancellationToken);
    Task<bool> ConfirmEmailAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Links a freshly verified mobile number to an account that does not have one yet.</summary>
    Task<bool> AttachPhoneNumberAsync(Guid userId, string phoneNumber, CancellationToken cancellationToken);

    /// <summary>Links a freshly verified email to an account that signed up by phone.</summary>
    Task<bool> AttachEmailAsync(Guid userId, string email, CancellationToken cancellationToken);

    Task UpdateProfileAsync(Guid userId, string? displayName, string? preferredLanguage, CancellationToken cancellationToken);

    Task AddBookCreditsAsync(Guid userId, int credits, CancellationToken cancellationToken);
    Task<bool> TryConsumeBookCreditAsync(Guid userId, CancellationToken cancellationToken);
    Task RefundBookCreditAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> TryConsumeWelcomeStoryAsync(Guid userId, CancellationToken cancellationToken);
    Task RefundWelcomeStoryAsync(Guid userId, CancellationToken cancellationToken);
}
