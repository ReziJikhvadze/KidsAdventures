namespace AdventurePacks.Api.DTOs.Auth;

public sealed class MagicLinkRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Where to drop the parent after sign-in, e.g. <c>/create</c>. Relative paths only.</summary>
    [MaxLength(256)]
    public string? ReturnPath { get; set; }
}

public sealed class VerifyMagicLinkRequest
{
    [Required, MinLength(32), MaxLength(256)]
    public string Token { get; set; } = string.Empty;

    /// <summary>Server-trustable id of a no-login teaser this parent generated (decides the welcome gift).</summary>
    public Guid? GuestPreviewId { get; set; }

    /// <summary>Fallback link to the teaser when only the story id survived the round-trip.</summary>
    public Guid? StoryId { get; set; }
}

public sealed class PhoneCodeRequest
{
    /// <summary>Any Georgian mobile format; normalised to E.164 server-side.</summary>
    [Required, MaxLength(32)]
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed class VerifyPhoneCodeRequest
{
    [Required, MaxLength(32)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, MinLength(4), MaxLength(10)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Server-trustable id of a no-login teaser this parent generated (decides the welcome gift).</summary>
    public Guid? GuestPreviewId { get; set; }

    /// <summary>Fallback link to the teaser when only the story id survived the round-trip.</summary>
    public Guid? StoryId { get; set; }
}

/// <summary>Result of asking for a link or a code. Never carries the secret in production.</summary>
public sealed class AuthChallengeResponse
{
    /// <summary>Masked destination, safe to render back to the parent as confirmation.</summary>
    public string Destination { get; set; } = string.Empty;

    public int ExpiresInSeconds { get; set; }

    /// <summary>Seconds until "resend" becomes available; drives the countdown in the sign-in panel.</summary>
    public int ResendAfterSeconds { get; set; }

    /// <summary>False when the dev/log sender handled it, so the UI can say no real SMS was sent.</summary>
    public bool DeliveryLive { get; set; }

    /// <summary>The code or link token, echoed only in development. Null otherwise.</summary>
    public string? DevSecret { get; set; }

    /// <summary>
    /// Full magic-link URL when secrets are exposed (dev / no live mail). Null for SMS
    /// challenges and whenever delivery is live.
    /// </summary>
    public string? DevUrl { get; set; }
}

public sealed class PasswordlessConfigResponse
{
    public bool MagicLinkEnabled { get; set; }
    public bool PhoneEnabled { get; set; }
    public bool SmsDeliveryLive { get; set; }
    public bool MagicLinkDeliveryLive { get; set; }
    public int OtpLength { get; set; }
    public int ResendCooldownSeconds { get; set; }
}
