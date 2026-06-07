namespace AdventurePacks.Api.DTOs.Auth;

public sealed class RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string Password { get; set; } = string.Empty;
}

public sealed class RegisterResponse
{
    public string Message { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string Email { get; set; } = string.Empty;
    public SubscriptionType SubscriptionType { get; set; }
    public int BookCredits { get; set; }
    public int StoriesUsedThisMonth { get; set; }
    public int StoriesAllowedThisMonth { get; set; }
    public int StoriesRemainingThisMonth { get; set; }
    public int WelcomeStoryRemaining { get; set; }
}

public sealed class SessionInfoResponse
{
    public string Email { get; set; } = string.Empty;
    public int BookCredits { get; set; }
    public int StoriesUsedThisMonth { get; set; }
    public int StoriesAllowedThisMonth { get; set; }
    public int StoriesRemainingThisMonth { get; set; }
    public int WelcomeStoryRemaining { get; set; }
    public SubscriptionType SubscriptionType { get; set; }
    public bool HasUnlimitedPdf { get; set; }
}

public sealed class ConfirmEmailRequest
{
    [Required, MinLength(32), MaxLength(128)]
    public string Token { get; set; } = string.Empty;
}

public sealed class ConfirmEmailResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
