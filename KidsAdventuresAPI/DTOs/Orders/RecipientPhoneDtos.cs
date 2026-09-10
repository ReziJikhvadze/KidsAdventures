namespace AdventurePacks.Api.DTOs.Orders;

/// <summary>
/// Whether checkout still has to ask about this number, and what to lay out if it does.
/// </summary>
public sealed class RecipientPhoneStatusResponse
{
    /// <summary>Masked, so a status call cannot be used to read a number back out of the server.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>True when this parent has already proved this number, or when it is their own.</summary>
    public bool Verified { get; set; }

    /// <summary>
    /// How many boxes to draw. Read off the server rather than assumed, so the panel is always
    /// the shape of the code that was actually sent.
    /// </summary>
    public int OtpLength { get; set; }

    /// <summary>The countdown that goes under "resend".</summary>
    public int ResendCooldownSeconds { get; set; }
}

public sealed class RecipientPhoneCodeRequest
{
    [Required, MaxLength(32)]
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed class VerifyRecipientPhoneRequest
{
    [Required, MaxLength(32)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, MaxLength(16)]
    public string Code { get; set; } = string.Empty;
}
