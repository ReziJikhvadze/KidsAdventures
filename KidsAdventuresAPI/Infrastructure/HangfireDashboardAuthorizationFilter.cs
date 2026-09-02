using System.Security.Cryptography;
using AdventurePacks.Api.Configuration.Options;
using Hangfire.Dashboard;

namespace AdventurePacks.Api.Infrastructure;

/// <summary>
/// A short-lived, signed ticket that lets a browser tab open the job dashboard.
///
/// The dashboard is server-rendered middleware, not an API route: the browser navigates to it in a
/// new tab, and a navigation cannot carry an Authorization header. So the console asks the API for
/// a cookie first and opens the tab only once that has succeeded.
///
/// The cookie is <c>{userId}|{expiresUnix}|{hmac}</c> — a statement plus proof that this server
/// made it. Nothing is stored: the expiry is inside the signed payload, so a ticket cannot be
/// extended by editing it and there is no session table to keep, sweep, or get wrong. The key is
/// the JWT signing key, because a deployment that can mint tokens can mint these, and one secret
/// with one lifecycle is one thing to rotate.
///
/// An hour. Long enough to watch a book through a retry, short enough that a laptop left open in a
/// cafe is not a permanent door into the queue.
/// </summary>
public static class HangfireSessionCookie
{
    public const string Name = "beki_hangfire";

    /// <summary>
    /// Scoped to the dashboard, so the ticket is not attached to every API call the console makes.
    /// A credential that travels further than the one thing it unlocks is a credential with a
    /// larger blast radius for no benefit.
    /// </summary>
    public const string CookiePath = "/hangfire";

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    /// <summary>Mints a ticket for one admin, valid until <paramref name="expiresAt"/>.</summary>
    public static string Issue(Guid userId, DateTimeOffset expiresAt, string signingKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKey);

        var payload = Payload(userId, expiresAt);
        return $"{payload}|{Sign(payload, signingKey)}";
    }

    /// <summary>
    /// Whether a cookie is one this server issued and has not yet expired.
    ///
    /// The signature is checked before the clock, and compared in fixed time. Neither matters much
    /// for a dashboard ticket — the attacker who could exploit a timing difference on an HMAC hex
    /// string has easier options — but a verifier written the careless way is the one that gets
    /// copied to somewhere it does matter.
    ///
    /// Every failure is the same answer: false. A verifier that distinguished "expired" from
    /// "forged" would be telling whoever is probing it which half they got right.
    /// </summary>
    public static bool TryVerify(
        string? cookie, string? signingKey, DateTimeOffset now, out Guid userId)
    {
        userId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(cookie) || string.IsNullOrWhiteSpace(signingKey))
        {
            return false;
        }

        var parts = cookie.Split('|');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!Guid.TryParse(parts[0], out var subject))
        {
            return false;
        }

        if (!long.TryParse(
                parts[1],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var expiresUnix))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(Sign($"{parts[0]}|{parts[1]}", signingKey));
        var presented = Encoding.UTF8.GetBytes(parts[2]);

        // FixedTimeEquals returns false for a length mismatch rather than throwing, which covers a
        // truncated or padded signature without a separate branch.
        if (!CryptographicOperations.FixedTimeEquals(expected, presented))
        {
            return false;
        }

        if (DateTimeOffset.FromUnixTimeSeconds(expiresUnix) <= now)
        {
            return false;
        }

        userId = subject;
        return true;
    }

    private static string Payload(Guid userId, DateTimeOffset expiresAt) =>
        $"{userId:D}|{expiresAt.ToUnixTimeSeconds()}";

    private static string Sign(string payload, string signingKey) =>
        Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(signingKey),
                    Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
}

/// <summary>
/// Who may see the job queue.
///
/// Two doors, and both of them are the operations role. A request that arrives already
/// authenticated in the Admin role is let through — that is an API client with a bearer token, and
/// it has already proved more than this needs. A browser tab has no header to prove anything with,
/// so it presents the ticket <c>POST /api/admin/hangfire-session</c> gave it, which is only ever
/// issued to an admin behind that same policy.
///
/// This used to admit any authenticated user at all. The dashboard shows every job in the system,
/// can requeue and delete them, and "signed in" is not the same claim as "runs this shop".
/// </summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.IsInRole(UserRoles.Admin))
        {
            return true;
        }

        // Resolved from the request rather than injected: the filter is constructed by hand in the
        // dashboard options at startup, before there is a provider to ask.
        var signingKey = httpContext.RequestServices
            .GetService<IOptions<JwtOptions>>()?.Value.SecretKey;

        return httpContext.Request.Cookies.TryGetValue(HangfireSessionCookie.Name, out var cookie)
               && HangfireSessionCookie.TryVerify(cookie, signingKey, DateTimeOffset.UtcNow, out _);
    }
}
