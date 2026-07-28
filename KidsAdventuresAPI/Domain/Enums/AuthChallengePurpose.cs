namespace AdventurePacks.Api.Domain.Enums;

public enum AuthChallengePurpose
{
    /// <summary>One-time link emailed to a parent; the secret is a 256-bit token.</summary>
    MagicLink = 0,

    /// <summary>Six-digit code sent by SMS to a Georgian mobile number.</summary>
    PhoneOtp = 1
}
