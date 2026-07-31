using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Interfaces;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class GoogleAuthService(
    IOptions<GoogleAuthOptions> options,
    IHttpClientFactory httpClientFactory) : IGoogleAuthService
{
    private readonly GoogleAuthOptions _options = options.Value;

    public async Task<GoogleTokenPayload> ValidateCredentialAsync(
        string? idToken,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new InvalidOperationException("Google sign-in is not configured.");
        }

        if (!string.IsNullOrWhiteSpace(idToken))
        {
            return await ValidateIdTokenAsync(idToken, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            return await ValidateAccessTokenAsync(accessToken, cancellationToken);
        }

        throw new UnauthorizedAccessException("Google credential is required.");
    }

    private async Task<GoogleTokenPayload> ValidateIdTokenAsync(string idToken, CancellationToken cancellationToken)
    {
        var payload = await GoogleJsonWebSignature.ValidateAsync(
            idToken,
            new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [_options.ClientId],
            });

        cancellationToken.ThrowIfCancellationRequested();

        return new GoogleTokenPayload
        {
            Email = payload.Email ?? string.Empty,
            EmailVerified = payload.EmailVerified,
            Subject = payload.Subject,
        };
    }

    private async Task<GoogleTokenPayload> ValidateAccessTokenAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();

        // Confirm the token was issued for this OAuth client before trusting profile claims.
        using (var tokenInfoResponse = await client.GetAsync(
                   $"https://oauth2.googleapis.com/tokeninfo?access_token={Uri.EscapeDataString(accessToken)}",
                   cancellationToken))
        {
            if (!tokenInfoResponse.IsSuccessStatusCode)
            {
                throw new UnauthorizedAccessException("Invalid Google access token.");
            }

            var tokenInfo = await tokenInfoResponse.Content.ReadFromJsonAsync<GoogleTokenInfo>(cancellationToken)
                            ?? throw new UnauthorizedAccessException("Invalid Google access token.");

            var audienceOk =
                string.Equals(tokenInfo.Aud, _options.ClientId, StringComparison.Ordinal)
                || string.Equals(tokenInfo.Azp, _options.ClientId, StringComparison.Ordinal);

            if (!audienceOk)
            {
                throw new UnauthorizedAccessException("Google token audience mismatch.");
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new UnauthorizedAccessException("Could not load Google profile.");
        }

        var profile = await response.Content.ReadFromJsonAsync<GoogleUserInfo>(cancellationToken)
                      ?? throw new UnauthorizedAccessException("Could not load Google profile.");

        return new GoogleTokenPayload
        {
            Email = profile.Email ?? string.Empty,
            EmailVerified = profile.EmailVerified,
            Subject = profile.Sub ?? string.Empty,
        };
    }

    private sealed class GoogleTokenInfo
    {
        [JsonPropertyName("aud")]
        public string? Aud { get; set; }

        [JsonPropertyName("azp")]
        public string? Azp { get; set; }
    }

    private sealed class GoogleUserInfo
    {
        [JsonPropertyName("sub")]
        public string? Sub { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("email_verified")]
        public bool EmailVerified { get; set; }
    }
}
