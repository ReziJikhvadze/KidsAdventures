using System.Security.Cryptography;
using System.Text;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>Verifies Dodo Payments webhooks (Standard Webhooks spec).</summary>
internal static class StandardWebhookVerifier
{
    public static void Verify(
        string payload,
        string webhookId,
        string webhookTimestamp,
        string webhookSignature,
        string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Webhook secret is not configured.");
        }

        if (string.IsNullOrWhiteSpace(webhookId)
            || string.IsNullOrWhiteSpace(webhookTimestamp)
            || string.IsNullOrWhiteSpace(webhookSignature))
        {
            throw new InvalidOperationException("Missing webhook signature headers.");
        }

        var secretBytes = DecodeSecret(secret);
        var signedContent = $"{webhookId}.{webhookTimestamp}.{payload}";
        var expected = ComputeHmacSha256Base64(secretBytes, signedContent);

        var signatures = webhookSignature.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in signatures)
        {
            var comma = part.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0)
            {
                continue;
            }

            var version = part[..comma];
            var signature = part[(comma + 1)..];
            if (!string.Equals(version, "v1", StringComparison.Ordinal))
            {
                continue;
            }

            if (FixedTimeEquals(signature, expected))
            {
                return;
            }
        }

        throw new InvalidOperationException("Webhook signature verification failed.");
    }

    private static byte[] DecodeSecret(string secret)
    {
        var normalized = secret.StartsWith("whsec_", StringComparison.Ordinal)
            ? secret["whsec_".Length..]
            : secret;

        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Webhook secret is not valid base64.", ex);
        }
    }

    private static string ComputeHmacSha256Base64(byte[] secretBytes, string content)
    {
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var hash = HMACSHA256.HashData(secretBytes, contentBytes);
        return Convert.ToBase64String(hash);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return aBytes.Length == bBytes.Length && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
