namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// The wifisher SMS gateway — https://sms-api.wifisher.com — which carries the one-time
/// codes for signing in by phone.
///
/// <see cref="ApiKey"/> and <see cref="Sender"/> are secrets in the sense that matters here:
/// whoever holds them can send SMS on Beki's name and spend Beki's balance. Neither is ever
/// committed. Locally they come from <c>dotnet user-secrets</c>, in production from the Azure
/// app settings, and when either is missing the flow falls back to the logging sender rather
/// than pretending a code went out.
/// </summary>
public sealed class WifisherSmsOptions
{
    public const string SectionName = "WifisherSms";

    /// <summary>
    /// Off by default so that a deployment that has not been given credentials logs its codes
    /// instead of failing every sign-in.
    /// </summary>
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://sms-api.wifisher.com/api/v2/";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The name the SMS appears to come from. The gateway validates it against the names
    /// registered to the account and answers <c>Invalid sender name</c> for anything else, so
    /// this cannot be invented here — it has to match what wifisher has on file.
    /// </summary>
    public string Sender { get; set; } = string.Empty;

    /// <summary>A parent is watching a spinner while this call is out.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured =>
        Enabled && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Sender);
}
