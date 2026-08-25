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

    /// <summary>
    /// Grants or removes the operations role. The change reaches a session only at its next token.
    ///
    /// A removal refuses to take the last admin away, and says so by returning false rather than
    /// by trusting the caller to have counted first — the count and the write have to be one
    /// statement or two simultaneous demotions can empty the role between them.
    /// </summary>
    Task<bool> SetAdminAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken);

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
