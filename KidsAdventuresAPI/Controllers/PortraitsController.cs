using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Portraits;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Services.Beki;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

/// <summary>
/// Checks a portrait the moment it is chosen.
///
/// Deliberately not part of the preview endpoints. Those read the photo when a book is already
/// being generated, which is minutes after the parent picked the file and long past the point
/// where "choose a different photo" is a reasonable thing to say. This one answers while the
/// file picker is still fresh in mind, and answers nothing else — it stores no photo and starts
/// no work.
/// </summary>
[ApiController]
[Route("api/portraits")]
public sealed class PortraitsController(
    IPortraitGate portraitGate,
    IGuestRateLimiter guestRateLimiter,
    IOptions<ClientIpOptions> clientIpOptions,
    ILogger<PortraitsController> logger) : ControllerBase
{
    /// <summary>
    /// Anonymous, because the photo is chosen before anyone has signed in — the journey asks for
    /// an account at checkout, not at the form.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("check")]
    // Roughly a megabyte of base64 is what a downscaled portrait comes to. This ceiling is well
    // above that and below the point where a request is rejected by the host before the action
    // runs, which would arrive at the browser as an unexplained CORS error.
    [RequestSizeLimit(12_000_000)]
    public async Task<ActionResult<PortraitCheckResponse>> Check(
        [FromBody] PortraitCheckRequest request,
        CancellationToken cancellationToken)
    {
        // The same limiter as the free preview, on its own key. Sharing the bucket would let a
        // handful of retried photos eat a visitor's preview allowance, and separate keys give
        // each anonymous endpoint its own ceiling without a second implementation.
        if (!guestRateLimiter.TryAcquire("portrait:" + GetClientKey()))
        {
            logger.LogWarning("Portrait check rate limit reached for a client.");
            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                new PortraitCheckResponse
                {
                    Accepted = false,
                    Reason = PortraitGateReasons.Unavailable,
                    Message = PortraitGateReasons.MessageFor(PortraitGateReasons.Unavailable),
                });
        }

        // A body we cannot decode comes back as a verdict rather than a 400. It is the same thing
        // to the parent — this photo will not do, pick another — and it keeps the browser on one
        // path instead of two that end in the same sentence.
        if (!PortraitDataUrl.TryDecode(request.PhotoDataUrl, out var bytes, out var contentType))
        {
            return Ok(ToResponse(PortraitVerdict.Fail(PortraitGateReasons.Unreadable)));
        }

        var verdict = await portraitGate.InspectAsync(bytes, contentType, cancellationToken);
        return Ok(ToResponse(verdict));
    }

    private static PortraitCheckResponse ToResponse(PortraitVerdict verdict) => new()
    {
        Accepted = verdict.Accepted,
        Reason = verdict.Reason,
        Message = verdict.Message,
    };

    /// <summary>
    /// Behind a proxy the connection address is the proxy, so the forwarded header has to be
    /// read — but only the entry the nearest hop wrote. Taking the caller's own entry would
    /// hand every request a fresh key, and this endpoint pays a vision model per call.
    /// </summary>
    private string GetClientKey() =>
        ClientIpAddress.Resolve(HttpContext, clientIpOptions.Value.TrustedProxyHops);
}
