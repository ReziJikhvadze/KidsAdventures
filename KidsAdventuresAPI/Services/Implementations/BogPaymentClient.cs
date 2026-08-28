using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// A thin client over BOG's e-commerce API: OAuth token, create order, read receipt,
/// verify callback.
///
/// Registered as a singleton so the access token is fetched once every few minutes rather
/// than once per checkout — BOG's tokens are short-lived and the auth endpoint is the
/// slowest hop in the flow.
/// </summary>
public sealed class BogPaymentClient(
    IHttpClientFactory httpClientFactory,
    IOptions<BogOptions> options,
    ILogger<BogPaymentClient> logger) : IBogPaymentClient
{
    public const string HttpClientName = "Bog";

    /// <summary>
    /// BOG's callback signing key, published at
    /// https://api.bog.ge/docs/payments/standard-process/callback. Pinned in code on
    /// purpose: a key fetched over the network could be swapped by whoever is already in a
    /// position to forge the callback it is meant to authenticate.
    /// </summary>
    private const string CallbackPublicKeyPem =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAu4RUyAw3+CdkS3ZNILQh\n" +
        "zHI9Hemo+vKB9U2BSabppkKjzjjkf+0Sm76hSMiu/HFtYhqWOESryoCDJoqffY0Q\n" +
        "1VNt25aTxbj068QNUtnxQ7KQVLA+pG0smf+EBWlS1vBEAFbIas9d8c9b9sSEkTrr\n" +
        "TYQ90WIM8bGB6S/KLVoT1a7SnzabjoLc5Qf/SLDG5fu8dH8zckyeYKdRKSBJKvhx\n" +
        "tcBuHV4f7qsynQT+f2UYbESX/TLHwT5qFWZDHZ0YUOUIvb8n7JujVSGZO9/+ll/g\n" +
        "4ZIWhC1MlJgPObDwRkRd8NFOopgxMcMsDIZIoLbWKhHVq67hdbwpAq9K9WMmEhPn\n" +
        "PwIDAQAB\n" +
        "-----END PUBLIC KEY-----";

    /// <summary>Renew a little before BOG says the token dies, so a call never starts on a stale one.</summary>
    private static readonly TimeSpan TokenSafetyMargin = TimeSpan.FromSeconds(30);

    private readonly BogOptions _bog = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTime _accessTokenExpiresAt = DateTime.MinValue;

    public async Task<BogCheckout> CreateOrderAsync(BogOrderRequest request, CancellationToken cancellationToken)
    {
        var payload = new
        {
            callback_url = _bog.CallbackUrl,
            external_order_id = request.OrderId.ToString(),
            capture = "automatic",
            ttl = Math.Clamp(_bog.TtlMinutes, 2, 1440),
            purchase_units = new
            {
                currency = request.Currency,
                total_amount = ToMajorUnits(request.TotalMinor),
                basket = new[]
                {
                    new
                    {
                        product_id = request.OrderId.ToString(),
                        description = request.Description,
                        quantity = 1,
                        unit_price = ToMajorUnits(request.TotalMinor)
                    }
                }
            },
            redirect_urls = new
            {
                success = request.SuccessUrl,
                fail = request.FailUrl
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint("ecommerce/orders"))
        {
            Content = JsonContent.Create(payload)
        };

        message.Headers.AcceptLanguage.ParseAdd(string.IsNullOrWhiteSpace(_bog.Language) ? "ka" : _bog.Language);

        // Our own order id doubles as the idempotency key: a retried create-order returns the
        // existing payment page instead of putting up a second one for the same money.
        message.Headers.TryAddWithoutValidation("Idempotency-Key", request.OrderId.ToString());

        using var response = await SendAuthorizedAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "BOG rejected the order for {OrderId}: {StatusCode} {Body}",
                request.OrderId, (int)response.StatusCode, body);
            throw new InvalidOperationException("გადახდის გვერდის შექმნა ვერ მოხერხდა.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var bogOrderId = root.TryGetProperty("id", out var id) ? id.GetString() : null;
        var redirectUrl = root.TryGetProperty("_links", out var links) &&
                          links.TryGetProperty("redirect", out var redirect) &&
                          redirect.TryGetProperty("href", out var href)
            ? href.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(bogOrderId) || string.IsNullOrWhiteSpace(redirectUrl))
        {
            logger.LogError(
                "BOG accepted the order for {OrderId} but returned no redirect: {Body}",
                request.OrderId, body);
            throw new InvalidOperationException("გადახდის გვერდის შექმნა ვერ მოხერხდა.");
        }

        return new BogCheckout(bogOrderId, redirectUrl);
    }

    public async Task<BogPaymentDetails?> GetPaymentDetailsAsync(
        string bogOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Get, Endpoint($"receipt/{Uri.EscapeDataString(bogOrderId)}"));

            using var response = await SendAuthorizedAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Reading BOG receipt {BogOrderId} failed: {StatusCode} {Body}",
                    bogOrderId, (int)response.StatusCode, body);
                return null;
            }

            using var document = JsonDocument.Parse(body);
            return ParseDetails(document.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       or InvalidOperationException)
        {
            // The confirmation poll runs on the status endpoint a parent is watching; the
            // callback is the authoritative path, so a failure here is reported, not thrown.
            logger.LogWarning(ex, "Reading BOG receipt {BogOrderId} failed.", bogOrderId);
            return null;
        }
    }

    public bool VerifyCallbackSignature(byte[] rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        // Outside the try on purpose: an unreadable pinned key is a fault in this build, and
        // swallowing it here would turn every callback into a silent "bad signature" — a
        // gateway that had quietly stopped paying anyone, with nothing in the logs but noise.
        using var rsa = RSA.Create();
        rsa.ImportFromPem(CallbackPublicKeyPem);

        try
        {
            var signature = Convert.FromBase64String(signatureHeader.Trim());
            return rsa.VerifyData(rawBody, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            logger.LogWarning(ex, "A BOG callback carried an unreadable signature.");
            return false;
        }
    }

    /// <summary>
    /// Reads the fields we act on out of a receipt, or out of a callback's <c>body</c>: BOG
    /// sends the same shape to both, which is why the callback needs no second round trip.
    /// </summary>
    public static BogPaymentDetails? ParseDetails(JsonElement element)
    {
        var bogOrderId = element.TryGetProperty("order_id", out var id) ? id.GetString() : null;
        if (string.IsNullOrWhiteSpace(bogOrderId))
        {
            return null;
        }

        var statusKey = element.TryGetProperty("order_status", out var status) &&
                        status.TryGetProperty("key", out var key)
            ? key.GetString() ?? string.Empty
            : string.Empty;

        var transactionId = element.TryGetProperty("payment_detail", out var detail) &&
                            detail.TryGetProperty("transaction_id", out var transaction)
            ? transaction.GetString()
            : null;

        Guid? orderId = element.TryGetProperty("external_order_id", out var external) &&
                        Guid.TryParse(external.GetString(), out var parsed)
            ? parsed
            : null;

        return new BogPaymentDetails(bogOrderId, orderId, statusKey, transactionId);
    }

    // -- transport ----------------------------------------------------------

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpRequestMessage message,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await GetAccessTokenAsync(client, cancellationToken));

        return await client.SendAsync(message, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(HttpClient client, CancellationToken cancellationToken)
    {
        if (_accessToken is { } cached && DateTime.UtcNow < _accessTokenExpiresAt)
        {
            return cached;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the lock: several checkouts can queue on it, and only the first
            // needs to spend a round trip on the auth endpoint.
            if (_accessToken is { } stillValid && DateTime.UtcNow < _accessTokenExpiresAt)
            {
                return stillValid;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _bog.AuthUrl)
            {
                Content = new FormUrlEncodedContent(
                    new Dictionary<string, string> { ["grant_type"] = "client_credentials" })
            };

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_bog.ClientId}:{_bog.SecretKey}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("BOG authentication failed: {StatusCode} {Body}", (int)response.StatusCode, body);
                throw new InvalidOperationException("გადახდის სისტემასთან დაკავშირება ვერ მოხერხდა.");
            }

            using var document = JsonDocument.Parse(body);
            var token = document.RootElement.TryGetProperty("access_token", out var accessToken)
                ? accessToken.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("გადახდის სისტემასთან დაკავშირება ვერ მოხერხდა.");
            }

            _accessToken = token;
            _accessTokenExpiresAt = DateTime.UtcNow + ReadLifetime(document.RootElement) - TokenSafetyMargin;

            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// BOG documents <c>expires_in</c> as seconds, but its own example carries a
    /// millisecond-scale number, which read as seconds would cache a dead token for
    /// centuries. Anything outside a plausible range means "assume a short life and re-ask".
    /// </summary>
    private static TimeSpan ReadLifetime(JsonElement root)
    {
        var fallback = TimeSpan.FromMinutes(5);

        if (!root.TryGetProperty("expires_in", out var expiresIn) ||
            !expiresIn.TryGetInt64(out var seconds))
        {
            return fallback;
        }

        return seconds is > 60 and <= 86400 ? TimeSpan.FromSeconds(seconds) : fallback;
    }

    private string Endpoint(string path) => $"{_bog.ApiBaseUrl.TrimEnd('/')}/{path}";

    private static decimal ToMajorUnits(int minor) => Math.Round(minor / 100m, 2);
}
