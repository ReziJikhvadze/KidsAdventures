using System.Net;

namespace AdventurePacks.Api.Infrastructure;

/// <summary>
/// The caller's address, for keying a rate limit on an endpoint anyone can reach.
///
/// The naive reading — first entry of <c>X-Forwarded-For</c>, fall back to the connection —
/// is the entry the caller wrote. A client sending a different value on every request gets a
/// fresh bucket every time, which turns a ceiling into no ceiling at all; on an endpoint that
/// spends money per call, that is the whole limit gone.
///
/// So the header is read from the right. Each proxy appends what it saw, so with one hop in
/// front the last entry is the address that hop actually observed, and any invented prefix is
/// left behind. A request that arrives with fewer entries than the configured chain did not
/// come through it, and is keyed by its connection address rather than by anything it claimed.
/// </summary>
public static class ClientIpAddress
{
    private const string ForwardedForHeader = "X-Forwarded-For";

    /// <summary>Never null, so a limiter key always exists even for a request we cannot place.</summary>
    public static string Resolve(HttpContext context, int trustedProxyHops)
    {
        var hops = Math.Max(1, trustedProxyHops);

        // The header may arrive as several header lines as well as several comma-separated
        // entries; both mean the same thing and the order across them is the order of the hops.
        var entries = context.Request.Headers[ForwardedForHeader]
            .SelectMany(value => (value ?? string.Empty).Split(','))
            .Select(entry => entry.Trim())
            .Where(entry => entry.Length > 0)
            .ToArray();

        if (entries.Length >= hops && TryParseAddress(entries[^hops], out var forwarded))
        {
            return forwarded;
        }

        return context.Connection.RemoteIpAddress is { } remote
            ? Normalize(remote)
            : "unknown";
    }

    /// <summary>
    /// Entries carry a port often enough to matter — App Service writes one — and keeping it
    /// would give the same visitor a new key per connection.
    /// </summary>
    private static bool TryParseAddress(string entry, out string address)
    {
        if (IPAddress.TryParse(entry, out var bare))
        {
            address = Normalize(bare);
            return true;
        }

        if (IPEndPoint.TryParse(entry, out var endpoint))
        {
            address = Normalize(endpoint.Address);
            return true;
        }

        address = string.Empty;
        return false;
    }

    /// <summary>
    /// One visitor, one key: the same IPv4 client reaches a dual-stack socket as
    /// <c>::ffff:1.2.3.4</c> on some paths and <c>1.2.3.4</c> on others.
    /// </summary>
    private static string Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
}
