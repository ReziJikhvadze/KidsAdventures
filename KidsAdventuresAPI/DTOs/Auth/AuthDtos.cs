namespace AdventurePacks.Api.DTOs.Auth;

public sealed class RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Optional reCAPTCHA token; required only when reCAPTCHA is enabled server-side.</summary>
    [MaxLength(4096)]
    public string? RecaptchaToken { get; set; }

    /// <summary>Legacy localStorage hint. Non-authoritative; server-side preview tracking takes precedence.</summary>
    public bool UsedGuestPreview { get; set; }

    /// <summary>Server-trustable id of a no-login teaser this parent generated (decides the welcome gift).</summary>
    public Guid? GuestPreviewId { get; set; }

    /// <summary>Fallback link to the teaser when only the story id survived the round-trip.</summary>
    public Guid? StoryId { get; set; }
}

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string Password { get; set; } = string.Empty;
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

public sealed class EmailStatusRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;
}

public sealed class EmailStatusResponse
{
    /// <summary>True when an account already exists for this email (sign in), false when it's new (create).</summary>
    public bool Exists { get; set; }

    /// <summary>True when the existing account was created via Google and has no password.</summary>
    public bool IsGoogleAccount { get; set; }
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

public sealed class GoogleLoginRequest
{
    [Required, MinLength(10)]
    public string IdToken { get; set; } = string.Empty;

    /// <summary>Legacy localStorage hint. Non-authoritative; server-side preview tracking takes precedence.</summary>
    public bool UsedGuestPreview { get; set; }

    /// <summary>Server-trustable id of a no-login teaser this parent generated (decides the welcome gift).</summary>
    public Guid? GuestPreviewId { get; set; }

    /// <summary>Fallback link to the teaser when only the story id survived the round-trip.</summary>
    public Guid? StoryId { get; set; }
}

public sealed class AuthConfigResponse
{
    public bool GoogleEnabled { get; set; }

    public string? GoogleClientId { get; set; }

    public bool RecaptchaEnabled { get; set; }

    public string? RecaptchaSiteKey { get; set; }
}
