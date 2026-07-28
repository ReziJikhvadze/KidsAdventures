using AdventurePacks.Api.DTOs.Orders;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/orders")]
public sealed class OrdersController(
    IOrderService orderService,
    IUserContextService userContext) : ControllerBase
{
    /// <summary>Prices a package with a promo code applied, without creating anything.</summary>
    [HttpPost("quote")]
    public async Task<ActionResult<QuoteResponse>> Quote(
        [FromBody] QuoteRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await orderService.QuoteAsync(userContext.GetUserId(), request, cancellationToken));
    }

    /// <summary>
    /// Starts checkout for a new book. Returns a provider URL, or — when a full-discount
    /// code made it free — an already-paid order whose book is being generated.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CheckoutResponse>> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var checkout = await orderService.CreateBookOrderAsync(
            userContext.GetUserId(), request, cancellationToken);
        return Ok(checkout);
    }

    /// <summary>Starts checkout for a printed copy of a book already owned digitally.</summary>
    [HttpPost("print-upgrade")]
    public async Task<ActionResult<CheckoutResponse>> CreatePrintUpgrade(
        [FromBody] CreatePrintUpgradeOrderRequest request,
        CancellationToken cancellationToken)
    {
        var checkout = await orderService.CreatePrintUpgradeOrderAsync(
            userContext.GetUserId(), request, cancellationToken);
        return Ok(checkout);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> List(CancellationToken cancellationToken)
    {
        return Ok(await orderService.ListAsync(userContext.GetUserId(), cancellationToken));
    }

    /// <summary>What the generating screen polls.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderStatusResponse>> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await orderService.GetStatusAsync(userContext.GetUserId(), id, cancellationToken));
    }

    /// <summary>
    /// Reconciles against the provider when the parent returns from checkout, so the book
    /// starts even if the webhook is delayed.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<OrderStatusResponse>> Confirm(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await orderService.ConfirmAsync(userContext.GetUserId(), id, cancellationToken));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var cancelled = await orderService.CancelAsync(userContext.GetUserId(), id, cancellationToken);
        return cancelled ? NoContent() : Conflict(new { message = "შეკვეთა ვერ გაუქმდა." });
    }
}

[ApiController]
[AllowAnonymous]
[Route("api/payments")]
public sealed class PaymentWebhooksController(
    IOrderService orderService,
    ILogger<PaymentWebhooksController> logger) : ControllerBase
{
    /// <summary>
    /// Stripe's callback. Anonymous by necessity — the signature header, not a session, is
    /// what authenticates it.
    /// </summary>
    [HttpPost("stripe/webhook")]
    public async Task<IActionResult> Stripe(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].ToString();

        try
        {
            await orderService.HandleStripeWebhookAsync(payload, signature, cancellationToken);
            return Ok();
        }
        catch (global::Stripe.StripeException ex)
        {
            // A bad signature is a 400: Stripe retries on 5xx, and retrying a forged or
            // malformed payload forever achieves nothing.
            logger.LogWarning(ex, "Rejected a Stripe webhook.");
            return BadRequest();
        }
    }
}
