using System.Text.Json;

using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Sends the sign-in code through wifisher's HTTP gateway.
///
/// The documented call is <c>POST /api/v2/send</c> with the key in an <c>api-key</c> header and
/// <c>from</c>, <c>to</c> and <c>content</c> as form fields. There is a GET form of the same call
/// that takes the key in the query string; this uses the POST one on purpose, because a key in a
/// URL ends up in proxy logs, browser history and any error report that quotes the request line.
/// </summary>
public sealed class WifisherSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<WifisherSmsOptions> options,
    ILogger<WifisherSmsSender> logger) : ISmsSender
{
    public const string HttpClientName = "WifisherSms";

    /// <summary>
    /// What the parent is told when the gateway will not take the message. Deliberately says
    /// nothing about why: this string is returned to the browser verbatim by the exception
    /// middleware, and "you don't have enough balance to send" is Beki's problem, not theirs.
    /// </summary>
    private const string DeliveryFailedMessage =
        "კოდის გაგზავნა ვერ მოხერხდა. სცადე ხელახლა ან შედი ელფოსტით.";

    private readonly WifisherSmsOptions _options = options.Value;

    public string ProviderName => "wifisher";

    public bool IsLive => true;

    public async Task SendAsync(
        string e164PhoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        // The gateway's own examples are bare digits — 995595079020 — and it reads a leading "+"
        // as part of the number rather than as notation.
        var destination = e164PhoneNumber.TrimStart('+');

        using var form = new MultipartFormDataContent
        {
            { new StringContent(_options.Sender), "from" },
            { new StringContent(destination), "to" },
            { new StringContent(message), "content" }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "send") { Content = form };
        request.Headers.TryAddWithoutValidation("api-key", _options.ApiKey);

        var client = httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        string body;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(
                ex,
                "wifisher did not answer for {Phone}.",
                GeorgianPhoneNumber.Mask(e164PhoneNumber));
            throw new InvalidOperationException(DeliveryFailedMessage);
        }

        using (response)
        {
            // The gateway reports its own failures in the body — an invalid key comes back as
            // {"status":401,"success":false,...} — so the HTTP status alone is not the answer.
            // Neither is the body alone: only a successful status carrying the documented success
            // shape counts as sent. Everything else, a body that will not parse included, is a
            // failure, because nothing here can confirm that a message went out.
            if (!TryReadOutcome(body, out var reportedSuccess, out var detail))
            {
                logger.LogError(
                    "wifisher answered HTTP {Status} for {Phone} with a body this cannot read "
                    + "({Length} bytes).",
                    (int)response.StatusCode,
                    GeorgianPhoneNumber.Mask(e164PhoneNumber),
                    body.Length);

                // The body itself only at Debug: an unexpected one may be echoing the request
                // back, and the request carries the code.
                logger.LogDebug("wifisher body was: {Body}", Trim(body));
                throw new InvalidOperationException(DeliveryFailedMessage);
            }

            if (!response.IsSuccessStatusCode || !reportedSuccess)
            {
                logger.LogError(
                    "wifisher refused the message for {Phone}: {Detail} "
                    + "(HTTP {Status}, success={Success}).",
                    GeorgianPhoneNumber.Mask(e164PhoneNumber),
                    detail,
                    (int)response.StatusCode,
                    reportedSuccess);
                throw new InvalidOperationException(DeliveryFailedMessage);
            }

            logger.LogInformation(
                "wifisher accepted the message for {Phone} ({Detail}).",
                GeorgianPhoneNumber.Mask(e164PhoneNumber),
                detail);
        }
    }

    /// <summary>
    /// Reads the gateway's verdict out of its JSON. Returns false when the body is not the shape
    /// this knows, which the caller treats as a failed send rather than an assumed success.
    /// </summary>
    private static bool TryReadOutcome(string body, out bool succeeded, out string detail)
    {
        succeeded = false;
        detail = string.Empty;

        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("success", out var success) ||
                success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            succeeded = success.GetBoolean();

            // Every TryGetProperty below is preceded by a ValueKind check, because it throws on
            // anything that is not an object — and "error":"Api key not valid", a string where
            // the documentation shows an object, is exactly the shape a gateway sends on a bad
            // day. That exception would leave here as a 400 carrying an English .NET message to
            // a Georgian parent, which is the one thing this class exists to prevent.
            if (succeeded)
            {
                detail =
                    root.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Object
                    && data.TryGetProperty("client_id", out var clientId)
                        ? $"client_id {clientId}"
                        : "no client_id returned";
                return true;
            }

            detail =
                root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var errorMessage)
                    ? errorMessage.GetString() ?? "no message"
                    : "no error message";
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Keeps an unexpected body readable in a log line without pasting a page into it.</summary>
    private static string Trim(string body) =>
        body.Length <= 300 ? body : string.Concat(body.AsSpan(0, 300), "…");
}
