namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// The wifisher SMS gateway — https://sms-api.wifisher.com — which carries the one-time
/// codes for signing in by phone.
///
/// <see cref="ApiKey"/> is the secret: whoever holds it can send SMS on Beki's name and spend
/// Beki's balance. It is never committed — locally from <c>dotnet user-secrets</c>, in production
/// from the Azure app settings — and without it the flow falls back to the logging sender rather
/// than pretending a code went out.
///
/// <see cref="Sender"/> is not a secret and so has a default here. It is printed on every message
/// that arrives, and one fewer app setting is one fewer thing for a deployment to get wrong.
/// </summary>
public sealed class WifisherSmsOptions
{
    public const string SectionName = "WifisherSms";

    /// <summary>
    /// Who Beki's messages come from, everywhere. Held here rather than in a settings file so
    /// there is one copy of it: a file that repeated it would bind over this and the default
    /// would never actually run.
    /// </summary>
    public const string DefaultSender = "Beki";

    /// <summary>
    /// Off by default so that a deployment that has not been given credentials logs its codes
    /// instead of failing every sign-in.
    /// </summary>
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://sms-api.wifisher.com/api/v2/";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The name the SMS appears to come from.
    ///
    /// The key and this are not the same thing, though it is easy to assume they are: the key
    /// identifies the account and therefore decides which names are <em>allowed</em>, while this
    /// says which of them to use. wifisher checks it against the names registered to the account
    /// and answers <c>Invalid sender name</c> (error code 4) for anything else — which is the
    /// first failure to expect if "Beki" was never registered with them.
    /// </summary>
    public string Sender { get; set; } = DefaultSender;

    /// <summary>A parent is watching a spinner while this call is out.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured =>
        Enabled && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Sender);
}
