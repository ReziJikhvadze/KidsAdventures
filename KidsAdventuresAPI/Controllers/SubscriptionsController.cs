using AdventurePacks.Api.DTOs.Subscriptions;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Route("api/subscriptions")]
public sealed class SubscriptionsController(
    ISubscriptionService subscriptionService,
    IUserContextService userContext) : ControllerBase
{
    [Authorize]
    [HttpGet("account")]
    public async Task<ActionResult<AccountBalanceResponse>> GetAccount(CancellationToken cancellationToken)
    {
        var balance = await subscriptionService.GetAccountBalanceAsync(userContext.GetUserId(), cancellationToken);
        return Ok(balance);
    }

    [Authorize]
    [HttpPost("confirm-checkout")]
    public async Task<ActionResult<AccountBalanceResponse>> ConfirmCheckout(
        [FromBody] ConfirmCheckoutSessionRequest request,
        CancellationToken cancellationToken)
    {
        var balance = await subscriptionService.ConfirmCheckoutSessionAsync(
            userContext.GetUserId(),
            request.SessionId,
            cancellationToken);
        return Ok(balance);
    }

    [Authorize]
    [HttpPost("create-checkout-session")]
    public async Task<ActionResult<CheckoutSessionResponse>> CreateCheckoutSession([FromBody] CreateCheckoutSessionRequest request, CancellationToken cancellationToken)
    {
        var response = await subscriptionService.CreateCheckoutSessionAsync(
            userContext.GetUserId(),
            userContext.GetEmail(),
            request.PlanType,
            cancellationToken);

        return Ok(response);
    }

    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(HttpContext.Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].ToString();

        await subscriptionService.HandleWebhookAsync(payload, signature, cancellationToken);
        return Ok();
    }
}
