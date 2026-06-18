using System.Net.Http.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class RecaptchaVerifier(
    IHttpClientFactory httpClientFactory,
    IOptions<RecaptchaOptions> options,
    ILogger<RecaptchaVerifier> logger) : IRecaptchaVerifier
{
    private readonly RecaptchaOptions _options = options.Value;

    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.SecretKey);

    public async Task<bool> VerifyAsync(string? token, CancellationToken cancellationToken)
    {
        // Scaffold mode: reCAPTCHA disabled -> always allow.
        if (!IsEnabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = _options.SecretKey,
                ["response"] = token,
            });

            var response = await client.PostAsync(
                "https://www.google.com/recaptcha/api/siteverify",
                content,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken);
            if (result is null || !result.Success)
            {
                return false;
            }

            // v3 returns a score; v2 checkbox omits it (treated as pass).
            return result.Score is null || result.Score >= _options.MinimumScore;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "reCAPTCHA verification failed.");
            return false;
        }
    }

    private sealed class SiteVerifyResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("success")]
        public bool Success { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("score")]
        public double? Score { get; set; }
    }
}
