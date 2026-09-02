using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

/// <summary>
/// The one thing the console needs that is not JSON: permission for a browser tab.
///
/// Everything else in this API is called with a bearer token the console holds in memory. The job
/// dashboard is not called — it is navigated to, in a new tab, by the browser itself, and a
/// navigation carries no headers the console can set. So the console asks here first, gets a
/// short-lived signed cookie scoped to <c>/hangfire</c>, and only then opens the tab.
///
/// Behind the same Admin policy as everything else, which is what makes the cookie trustworthy:
/// the dashboard filter accepts the ticket precisely because nothing but an admin can obtain one.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin")]
public sealed class AdminSessionController(
    IOptions<JwtOptions> jwtOptions,
    IUserContextService userContext,
    ILogger<AdminSessionController> logger) : ControllerBase
{
    [HttpPost("hangfire-session")]
    public IActionResult HangfireSession()
    {
        var signingKey = jwtOptions.Value.SecretKey;
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            // Unreachable in a booted process — AddAdventurePacksAuth refuses to start without a
            // key — but a ticket signed with an empty secret would be a ticket anybody can forge,
            // and that is not a failure mode worth leaving to an assumption.
            logger.LogError("A Hangfire dashboard session was requested and Jwt:SecretKey is empty.");
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "სამუშაოების პანელი ამჟამად მიუწვდომელია." });
        }

        var userId = userContext.GetUserId();
        var expiresAt = DateTimeOffset.UtcNow.Add(HangfireSessionCookie.Lifetime);

        Response.Cookies.Append(
            HangfireSessionCookie.Name,
            HangfireSessionCookie.Issue(userId, expiresAt, signingKey),
            new CookieOptions
            {
                HttpOnly = true,

                // Secure only when the request itself is HTTPS. Hard-coding it would mean the
                // cookie is silently dropped on a developer's http://localhost and the dashboard
                // simply refuses to open with nothing to explain why.
                Secure = Request.IsHttps,

                // Lax rather than Strict: the tab is opened by a top-level navigation from the
                // console, which Lax allows and Strict would not.
                SameSite = SameSiteMode.Lax,
                Path = HangfireSessionCookie.CookiePath,
                Expires = expiresAt,

                // Not a consent question. This cookie exists only because an operator clicked a
                // button asking for exactly this, and it carries nothing about anybody.
                IsEssential = true,
            });

        logger.LogInformation(
            "Admin {UserId} opened a job dashboard session, valid until {ExpiresAt:o}.",
            userId, expiresAt);

        return NoContent();
    }
}
