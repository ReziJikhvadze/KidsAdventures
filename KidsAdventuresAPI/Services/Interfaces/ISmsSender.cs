namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>
/// Outbound SMS. Georgian gateways (Magti, SMSOffice, Geo SMS) all speak slightly
/// different HTTP dialects, so the whole OTP flow is written against this seam and
/// a real gateway drops in as one more implementation without touching auth code.
/// </summary>
public interface ISmsSender
{
    /// <summary>Shown in logs and in the dev banner so it is obvious which sender is wired up.</summary>
    string ProviderName { get; }

    /// <summary>
    /// False for the development sender that only logs. The auth endpoints use this to
    /// decide whether it is safe to echo the code back to the caller.
    /// </summary>
    bool IsLive { get; }

    Task SendAsync(string e164PhoneNumber, string message, CancellationToken cancellationToken = default);
}
