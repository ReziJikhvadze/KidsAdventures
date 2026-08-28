namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// Bank of Georgia's e-commerce gateway — the Georgian half of checkout.
///
/// When <see cref="Enabled"/> is true every paid order is routed here instead of Stripe.
/// Both providers stay registered: an order records which one took the money, so orders
/// created before the switch can still be confirmed and refunded against Stripe.
/// </summary>
public sealed class BogOptions
{
    public const string SectionName = "Bog";

    public bool Enabled { get; set; }

    /// <summary>OPAY client id, used as the OAuth client_id.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OPAY secret key, used as the OAuth client_secret.</summary>
    public string SecretKey { get; set; } = string.Empty;

    public string AuthUrl { get; set; } = "https://oauth2.bog.ge/auth/realms/bog/protocol/openid-connect/token";

    public string ApiBaseUrl { get; set; } = "https://api.bog.ge/payments/v1";

    /// <summary>
    /// Absolute base URL of the site the parent came from, used to build the return links.
    /// Falls back to <c>Stripe:SiteBaseUrl</c> when blank — it is the same site, and having
    /// to keep two copies of it in step is a trap.
    /// </summary>
    public string SiteBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Absolute HTTPS URL of <c>POST /api/payments/bog/webhook</c> on this API.
    ///
    /// Not derived from <see cref="SiteBaseUrl"/>: the API and the frontend are separate
    /// App Services, so the callback host is not the site host. BOG rejects a plain-HTTP
    /// callback, and a missing one leaves payment confirmation resting entirely on the
    /// parent coming back to the success page.
    /// </summary>
    public string CallbackUrl { get; set; } = string.Empty;

    public string SuccessPath { get; set; } = "/create";

    public string CancelPath { get; set; } = "/create";

    /// <summary>How long the payment page stays open, in minutes. BOG allows 2 to 1440.</summary>
    public int TtlMinutes { get; set; } = 15;

    /// <summary>Payment page language: "ka" or "en".</summary>
    public string Language { get; set; } = "ka";

    /// <summary>
    /// The callback signature is the only thing separating this endpoint from a forged
    /// "payment succeeded", so it is verified by default. Turn it off only to replay a
    /// captured payload locally, never anywhere a real card works.
    /// </summary>
    public bool VerifyCallbackSignature { get; set; } = true;
}
