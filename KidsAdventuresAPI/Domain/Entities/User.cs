namespace AdventurePacks.Api.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;

    /// <summary>Null for accounts created through a magic link, phone code, or Google.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>E.164, Georgian mobile. Null until the parent verifies a code.</summary>
    public string? PhoneNumber { get; set; }

    public bool PhoneConfirmed { get; set; }
    public string PreferredLanguage { get; set; } = "ka";
    public string? DisplayName { get; set; }
    public bool IsAdmin { get; set; }
    public SubscriptionType SubscriptionType { get; set; } = SubscriptionType.Free;
    public int BookCredits { get; set; }
    public int WelcomeStoryRemaining { get; set; }
    public bool EmailConfirmed { get; set; }
    public string? EmailConfirmationToken { get; set; }
    public DateTime? EmailConfirmationExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True when the only way into this account is a link or a code.</summary>
    public bool IsPasswordless => string.IsNullOrEmpty(PasswordHash);
}
