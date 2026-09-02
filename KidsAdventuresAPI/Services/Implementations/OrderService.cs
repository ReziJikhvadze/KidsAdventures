using System.Text.Json;

using Hangfire;
using Stripe;
using Stripe.Checkout;

using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Orders;
using AdventurePacks.Api.DTOs.Print;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Checkout and fulfilment for the Georgian product.
///
/// Three properties are worth stating outright, because the rest of the class follows
/// from them:
///
/// 1. The server prices everything. The client sends a package and a promo code, never an
///    amount, so a tampered request cannot buy a 79 GEL book for 1 tetri.
/// 2. Fulfilment is idempotent at the database, not in application logic. A gateway
///    delivers its "paid" callback more than once, the success page confirms in parallel,
///    and the sweeper retries: all three funnel through
///    <see cref="IOrderRepository.TryMarkPaidAsync"/>, and only the first writer proceeds.
/// 3. A zero-total order never touches a gateway at all. A GIFT100 code has nothing to
///    collect, and routing 0 GEL through a payment provider fails in ways that are hard to
///    explain to a parent.
///
/// Two gateways are wired in — BOG for Georgia, Stripe behind it. Which one a given order
/// used is recorded on the order rather than read from configuration, so flipping the switch
/// does not orphan the payments the other one is still holding.
/// </summary>
public sealed class OrderService(
    IOrderRepository orderRepository,
    IPromoCodeService promoCodeService,
    IBookFulfillmentService bookFulfillmentService,
    IAdventurePackRepository packRepository,
    ICharacterRepository characterRepository,
    IWorldProgressService worldProgressService,
    IPromoCodeRepository promoCodeRepository,
    IUserRepository userRepository,
    IAdminNotifier adminNotifier,
    IBackgroundJobClient backgroundJobClient,
    IBogPaymentClient bogClient,
    IOptions<StripeOptions> stripeOptions,
    IOptions<BogOptions> bogOptions,
    ILogger<OrderService> logger) : IOrderService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Matches <c>CK_BookCharacters_Position</c>.</summary>
    private const int MaxCastSize = 3;

    /// <summary>Fulfilment that has not finished in this long is treated as stalled.</summary>
    private static readonly TimeSpan StalledAfter = TimeSpan.FromMinutes(5);

    private readonly StripeOptions _stripe = stripeOptions.Value;
    private readonly BogOptions _bog = bogOptions.Value;

    /// <summary>
    /// Which gateway a new paid order goes to. BOG wins when it is switched on; Stripe is
    /// what remains behind it, and either way the choice is written onto the order so the
    /// webhook, the confirm poll and the sweeper all agree on who holds the money.
    /// </summary>
    private string PaymentProvider => _bog.Enabled ? OrderProviders.Bog : OrderProviders.Stripe;

    public async Task<QuoteResponse> QuoteAsync(
        Guid userId,
        QuoteRequest request,
        CancellationToken cancellationToken)
    {
        var type = ParseType(request.Type);

        // A print upgrade has only one shape, so whatever package the client sent is
        // irrelevant and must not be able to fail the quote.
        var package = type == OrderType.PrintUpgrade
            ? OrderPackage.Print
            : ParsePackage(request.Package);

        return await promoCodeService.QuoteAsync(userId, type, package, request.PromoCode, cancellationToken);
    }

    public async Task<CheckoutResponse> CreateBookOrderAsync(
        Guid userId,
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var draft = request.Draft
                    ?? throw new InvalidOperationException("წიგნის მონაცემები არ არის მითითებული.");

        var package = ParsePackage(request.Package);
        await ValidateDraftAsync(userId, draft, cancellationToken);

        // Only the Print package ships anything, so an address sent alongside a Digital
        // order is dropped rather than stored: no reason to keep a home address we will
        // never post to.
        var shipping = package == OrderPackage.Print
            ? RequireShippingAddress(request.ShippingAddress)
            : null;

        var priced = await promoCodeService.PriceAsync(
            userId, OrderType.NewBook, package, request.PromoCode, cancellationToken);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = OrderType.NewBook,
            Package = package,
            SubtotalMinor = priced.SubtotalMinor,
            DiscountMinor = priced.DiscountMinor,
            TotalMinor = priced.TotalMinor,
            PromoCodeId = priced.Promo?.Id,
            Status = OrderStatus.Pending,
            Provider = priced.IsFree ? OrderProviders.Promo : PaymentProvider,
            DraftJson = JsonSerializer.Serialize(draft, JsonOptions),
            ShippingJson = Serialize(shipping),
            CreatedAt = DateTime.UtcNow
        };

        await orderRepository.CreateAsync(order, cancellationToken);
        return await StartCheckoutAsync(order, request.ReturnPath, BookLineDescription(package), cancellationToken);
    }

    public async Task<CheckoutResponse> CreatePrintUpgradeOrderAsync(
        Guid userId,
        CreatePrintUpgradeOrderRequest request,
        CancellationToken cancellationToken)
    {
        var book = await packRepository.GetByIdAsync(request.BookId, userId, cancellationToken)
                   ?? throw new KeyNotFoundException("წიგნი ვერ მოიძებნა.");

        if (!book.IsFullyUnlocked)
        {
            throw new InvalidOperationException("ჯერ შეიძინე ციფრული წიგნი, შემდეგ დაამატე ბეჭდური.");
        }

        if (book.HasPrintEntitlement)
        {
            throw new InvalidOperationException("ამ წიგნის ბეჭდური ვერსია უკვე შეძენილია.");
        }

        var shipping = RequireShippingAddress(request.ShippingAddress);

        var priced = await promoCodeService.PriceAsync(
            userId, OrderType.PrintUpgrade, OrderPackage.Print, request.PromoCode, cancellationToken);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BookId = book.Id,
            Type = OrderType.PrintUpgrade,
            Package = OrderPackage.Print,
            SubtotalMinor = priced.SubtotalMinor,
            DiscountMinor = priced.DiscountMinor,
            TotalMinor = priced.TotalMinor,
            PromoCodeId = priced.Promo?.Id,
            Status = OrderStatus.Pending,
            Provider = priced.IsFree ? OrderProviders.Promo : PaymentProvider,
            ShippingJson = Serialize(shipping),
            CreatedAt = DateTime.UtcNow
        };

        await orderRepository.CreateAsync(order, cancellationToken);
        return await StartCheckoutAsync(order, request.ReturnPath, "ბეჭდური წიგნი", cancellationToken);
    }

    public async Task<IReadOnlyList<OrderResponse>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var orders = await orderRepository.GetByUserIdAsync(userId, cancellationToken);
        if (orders.Count == 0)
        {
            return [];
        }

        // Resolve the promo codes in one pass rather than once per order.
        var codesById = new Dictionary<Guid, string>();
        foreach (var promoCodeId in orders.Select(order => order.PromoCodeId).OfType<Guid>().Distinct())
        {
            var code = await promoCodeRepository.GetByIdAsync(promoCodeId, cancellationToken);
            if (code is not null)
            {
                codesById[promoCodeId] = code.Code;
            }
        }

        return orders.Select(order => ToResponse(order, Lookup(codesById, order.PromoCodeId))).ToList();
    }

    public async Task<OrderStatusResponse> GetStatusAsync(
        Guid userId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdForUserAsync(orderId, userId, cancellationToken)
                    ?? throw new KeyNotFoundException("შეკვეთა ვერ მოიძებნა.");
        return await BuildStatusAsync(order, cancellationToken);
    }

    public async Task<OrderStatusResponse> ConfirmAsync(
        Guid userId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdForUserAsync(orderId, userId, cancellationToken)
                    ?? throw new KeyNotFoundException("შეკვეთა ვერ მოიძებნა.");

        if (order.Status == OrderStatus.Pending && order.ProviderSessionId is { } sessionId)
        {
            if (IsBog(order))
            {
                var details = await bogClient.GetPaymentDetailsAsync(sessionId, cancellationToken);
                if (details is { IsPaid: true })
                {
                    await ApplyPaymentAsync(order, details.TransactionId, cancellationToken);
                }
                else if (details is { IsFailed: true })
                {
                    // A declined card comes back through the fail URL, and the receipt says so
                    // outright. Without this the order stays Pending and the generating screen
                    // polls forever, which reads to a parent as "it is still working".
                    await orderRepository.MarkFailedAsync(
                        order.Id, "გადახდა არ დასრულდა ან ვადა გაუვიდა.", cancellationToken);
                }
            }
            else
            {
                var session = await RetrieveSessionAsync(sessionId, cancellationToken);
                if (session is not null && IsSessionPaid(session))
                {
                    await ApplyPaymentAsync(order, session.PaymentIntentId, cancellationToken);
                }
            }
        }

        // Re-read: ApplyPaymentAsync may have moved the order on, and a webhook may have
        // done so concurrently.
        var refreshed = await orderRepository.GetByIdAsync(orderId, cancellationToken) ?? order;
        return await BuildStatusAsync(refreshed, cancellationToken);
    }

    public async Task<bool> CancelAsync(Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        return await orderRepository.TryCancelAsync(orderId, userId, cancellationToken);
    }

    public async Task HandleStripeWebhookAsync(
        string jsonPayload,
        string stripeSignature,
        CancellationToken cancellationToken)
    {
        if (!_stripe.Enabled || string.IsNullOrWhiteSpace(_stripe.WebhookSecret))
        {
            logger.LogWarning("Stripe webhook received while Stripe is not configured; ignoring.");
            return;
        }

        StripeConfiguration.ApiKey = _stripe.SecretKey;

        // Signature verification is the only thing standing between this endpoint and a
        // forged "payment succeeded", so a failure here must propagate as a 400 rather
        // than being swallowed.
        var stripeEvent = EventUtility.ConstructEvent(jsonPayload, stripeSignature, _stripe.WebhookSecret);

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
            case "checkout.session.async_payment_succeeded":
                await HandleSessionCompletedAsync(stripeEvent.Data.Object as Session, cancellationToken);
                break;

            case "checkout.session.expired":
            case "checkout.session.async_payment_failed":
                await HandleSessionFailedAsync(stripeEvent.Data.Object as Session, cancellationToken);
                break;

            default:
                logger.LogDebug("Ignoring Stripe event {EventType}.", stripeEvent.Type);
                break;
        }
    }

    public async Task<bool> HandleBogWebhookAsync(
        byte[] payload,
        string? signature,
        CancellationToken cancellationToken)
    {
        // Deliberately not gated on _bog.Enabled. That switch decides where the *next* order
        // goes; an order already at the payment page when it is flipped still gets paid, and
        // BOG does not redeliver after a 200 — acknowledging that callback without acting on
        // it would leave a parent charged with no book and nothing to retry it.
        //
        // The signature is the only thing separating this endpoint from a forged "payment
        // succeeded", so a callback that fails it is refused before anything is parsed. The
        // key is pinned in code, so this check needs no configuration to be sound.
        if (_bog.VerifyCallbackSignature && !bogClient.VerifyCallbackSignature(payload, signature))
        {
            logger.LogWarning("Rejected a BOG callback: the signature did not verify.");
            return false;
        }

        BogPaymentDetails? details;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            // The payment sits under "body"; other event types are not ours to act on.
            if (!root.TryGetProperty("body", out var body))
            {
                logger.LogDebug("Ignoring a BOG callback with no body.");
                return true;
            }

            details = BogPaymentClient.ParseDetails(body);
        }
        catch (JsonException ex)
        {
            // Malformed and correctly signed should not happen; retrying it forever would
            // achieve nothing, so it is accepted and logged rather than left to redeliver.
            logger.LogWarning(ex, "A BOG callback carried unreadable JSON.");
            return true;
        }

        if (details is null)
        {
            logger.LogWarning("A BOG callback carried no order id.");
            return true;
        }

        var order = await ResolveBogOrderAsync(details, cancellationToken);
        if (order is null)
        {
            logger.LogWarning("No order matched BOG order {BogOrderId}.", details.BogOrderId);
            return true;
        }

        // Whose payment this is, is a property of the order, not of today's configuration.
        if (!IsBog(order))
        {
            logger.LogWarning(
                "A BOG callback named order {OrderId}, which was taken by {Provider}; ignoring.",
                order.Id, order.Provider);
            return true;
        }

        if (details.IsPaid)
        {
            await ApplyPaymentAsync(order, details.TransactionId, cancellationToken);
        }
        else if (details.IsFailed && !order.IsPaid)
        {
            await orderRepository.MarkFailedAsync(
                order.Id, "გადახდა არ დასრულდა ან ვადა გაუვიდა.", cancellationToken);
        }
        else
        {
            logger.LogInformation(
                "BOG order {BogOrderId} reported status {Status}; nothing to do.",
                details.BogOrderId, details.StatusKey);
        }

        return true;
    }

    /// <summary>
    /// Our own id first, the gateway's second — same order of preference as the Stripe path,
    /// and for the same reason: <c>external_order_id</c> is the one field we control.
    /// </summary>
    private async Task<Order?> ResolveBogOrderAsync(
        BogPaymentDetails details,
        CancellationToken cancellationToken)
    {
        if (details.OrderId is { } orderId)
        {
            var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);

            // The callback must be about the payment this order actually started. Without
            // this, a signed callback for a 14 GEL order could be pointed at a 79 GEL one by
            // its external id alone.
            //
            // A null session id is not a mismatch: it means the callback outran the write
            // that records it, and refusing that would lose a real payment.
            if (order is not null &&
                (order.ProviderSessionId is null ||
                 string.Equals(order.ProviderSessionId, details.BogOrderId, StringComparison.Ordinal)))
            {
                return order;
            }

            if (order is not null)
            {
                logger.LogWarning(
                    "BOG callback for order {OrderId} names payment {BogOrderId}, but the order holds "
                    + "{HeldSessionId}; ignoring.",
                    order.Id, details.BogOrderId, order.ProviderSessionId);
                return null;
            }
        }

        return await orderRepository.GetByProviderSessionIdAsync(details.BogOrderId, cancellationToken);
    }

    public async Task RetryStalledFulfilmentAsync()
    {
        var stalled = await orderRepository.GetStalledPaidAsync(
            DateTime.UtcNow - StalledAfter, limit: 25, CancellationToken.None);

        // Enqueued rather than run here, so the sweep and the console's retry button contend
        // for the same per-order lock instead of racing each other into two books.
        foreach (var order in stalled)
        {
            backgroundJobClient.Enqueue<IOrderService>(service => service.FulfilOrderAsync(order.Id));
        }
    }

    public async Task FulfilOrderAsync(Guid orderId)
    {
        // Re-read under the lock. Whatever the caller believed about this order when it queued
        // the job, this is the row as it stands now — including the BookId a job that ran a
        // moment ago may just have written, which is the whole idempotency story.
        var order = await orderRepository.GetByIdAsync(orderId, CancellationToken.None);
        if (order is null || !await CanRedriveAsync(order, CancellationToken.None))
        {
            logger.LogDebug("Order {OrderId} needs no fulfilment; skipping.", orderId);
            return;
        }

        try
        {
            var bookId = await bookFulfillmentService.FulfillAsync(order, CancellationToken.None);
            await orderRepository.TryMarkFulfilledAsync(order.Id, CancellationToken.None);
            logger.LogInformation("Fulfilled order {OrderId}; book {BookId}.", order.Id, bookId);
        }
        catch (Exception ex)
        {
            // Left as Paid on purpose so the next sweep tries again; a paid order is
            // never marked Failed, because the money is real.
            logger.LogError(ex, "Fulfilling order {OrderId} failed.", order.Id);
            await orderRepository.MarkFailedAsync(order.Id, ex.Message, CancellationToken.None);
        }
    }

    public async Task<bool> RequeueFulfilmentAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);

        if (order is null || !await CanRedriveAsync(order, cancellationToken))
        {
            return false;
        }

        backgroundJobClient.Enqueue<IOrderService>(service => service.FulfilOrderAsync(orderId));
        logger.LogInformation("Admin re-queued fulfilment for order {OrderId}.", orderId);

        return true;
    }

    /// <summary>
    /// Whether this order may be driven through fulfilment again — the one rule the console's
    /// retry button and the background job both apply, so that a retry the operator is told was
    /// queued cannot be silently declined by the job it queued.
    ///
    /// Two of the three clauses are the old ones and unchanged. An unpaid order has nothing to
    /// deliver. A paid order that has not been fulfilled is the ordinary retry, and the reason
    /// this check exists at all is idempotency: the job re-reads under its lock.
    ///
    /// The third is the hole this closes. An order is marked fulfilled the moment generation is
    /// <em>enqueued</em>, not when a book comes out the other end — so every generation failure
    /// happens to an order that already says Fulfilled, and refusing those meant the one failure
    /// that always needs an operator was the one the operator had no working button for. Pack
    /// 7fc8faf4 is that case: paid, fulfilled, and a book that stopped on spread one.
    ///
    /// Narrow on purpose. A fulfilled order passes only when its book is really there and in one
    /// of two states nothing produces by accident: Failed — the sweep writes it from outside, the
    /// job writes it about itself, and both mean a person should decide — or unclaimed past the
    /// stalled allowance, which is a job that was never posted or never reached its claim. A
    /// fulfilled order whose book is fine or in flight still cannot be re-driven, because
    /// redrawing a finished book is how a parent ends up with a different one from the one they
    /// read.
    ///
    /// <see cref="BookFulfillmentService"/> does the rest: its revival branch moves the pack out
    /// of Failed — deliberately, and with a compare-and-set — before anything is enqueued, and its
    /// re-drive queues the job an unclaimed pack never got.
    ///
    /// The public overload on <see cref="IOrderService"/> is this same method behind a lookup,
    /// so the console and the job cannot drift apart.
    /// </summary>
    private async Task<bool> CanRedriveAsync(Order order, CancellationToken cancellationToken)
    {
        if (!order.IsPaid)
        {
            return false;
        }

        if (order.FulfilledAt is null)
        {
            return true;
        }

        if (order.BookId is not { } bookId)
        {
            return false;
        }

        /*
          And only for the order type that redriving actually helps.

          A fulfilled PrintUpgrade dispatches FulfillPrintUpgradeAsync, which grants the print
          entitlement and returns — it never touches generation, and never reaches the revival
          branch. Letting one through on a Failed book would answer the console with "queued",
          run a job that re-grants an entitlement the order already granted, and leave the pack
          exactly as Failed as it found it. The operator would be told the retry worked.
        */
        if (order.Type != OrderType.NewBook)
        {
            return false;
        }

        var book = await packRepository.GetByIdAsync(bookId, order.UserId, cancellationToken);
        if (book is null)
        {
            return false;
        }

        if (book.Status == AdventurePackStatus.Failed)
        {
            return true;
        }

        /*
          And a book no job ever claimed — the second state nothing else recovers.

          Fulfilment writes the pack, adopts the story into StoryReady, and only then enqueues the
          job; an order is stamped Fulfilled the moment that enqueue returns. A job that was never
          posted, or that died before its claim, leaves a paid book resting in the status
          fulfilment gave it: Pending on either pipeline, StoryReady on the Beki one. Neither is
          Failed, so the button refused them; neither is a working status, so the sweep never
          buried them; the parent polled forever.

          Legacy StoryReady is excluded because on that pipeline it is the finished book, and
          re-driving it would redraw what the parent has already read.

          "No live job" cannot be read off the row — a queued job and a lost one look the same
          until the claim — so it is judged by time, with the same allowance the stalled-order
          sweep gives a fulfilment before calling it stalled. A book unclaimed for longer than that
          is offered to a person; if the job was merely queued, both generation jobs refuse a pack
          another has already moved on, so the second is harmless.
        */
        var unclaimed = book.Status == AdventurePackStatus.Pending
                        || (book.IsBekiPipeline && book.Status == AdventurePackStatus.StoryReady);

        if (!unclaimed)
        {
            return false;
        }

        var lastSignalUtc = book.GenerationHeartbeatUtc ?? book.CreatedAt;
        return lastSignalUtc < DateTime.UtcNow - StalledAfter;
    }

    public async Task<bool> CanRedriveAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);
        return order is not null && await CanRedriveAsync(order, cancellationToken);
    }

    // -- checkout -----------------------------------------------------------

    private async Task<CheckoutResponse> StartCheckoutAsync(
        Order order,
        string? returnPath,
        string lineDescription,
        CancellationToken cancellationToken)
    {
        // BypassPayment is the testing switch: it takes the same route a free order
        // takes, so the end-to-end journey exercises the real fulfilment path rather than
        // a shortcut that only exists while testing.
        if (order.IsFree || _stripe.BypassPayment)
        {
            if (_stripe.BypassPayment && !order.IsFree)
            {
                logger.LogWarning(
                    "Payment bypass is ON: order {OrderId} for {TotalMinor} {Currency} is being "
                    + "fulfilled without collecting payment. This must be off in production.",
                    order.Id, order.TotalMinor, order.Currency);
            }

            // Nothing to collect. Mark it paid ourselves and fulfil inline, so the parent
            // goes straight from "GIFT100 applied" to a book being written.
            //
            // Once the order says Paid, the request token no longer applies — see
            // ApplyPaymentAsync. A free order is paid the moment that write lands, and a browser
            // that leaves during the seconds of fulfilment must not leave it half-made.
            await orderRepository.TryMarkPaidAsync(order.Id, null, cancellationToken);
            var refreshed = await orderRepository.GetByIdAsync(order.Id, CancellationToken.None) ?? order;
            var bookId = await FulfillPaidOrderAsync(refreshed, CancellationToken.None);

            return new CheckoutResponse
            {
                OrderId = order.Id,
                TotalMinor = 0,
                Currency = order.Currency,
                IsFree = true,
                BookId = bookId
            };
        }

        if (IsBog(order))
        {
            return await StartBogCheckoutAsync(order, returnPath, lineDescription, cancellationToken);
        }

        if (!_stripe.Enabled || string.IsNullOrWhiteSpace(_stripe.SecretKey))
        {
            logger.LogError(
                "Order {OrderId} routed to Stripe, but Stripe:Enabled is {Enabled} and Stripe:SecretKey is "
                + "{SecretState}. Set Bog__Enabled to send orders to BOG instead.",
                order.Id, _stripe.Enabled, string.IsNullOrWhiteSpace(_stripe.SecretKey) ? "empty" : "set");

            await orderRepository.MarkFailedAsync(order.Id, "Stripe is not configured.", cancellationToken);
            throw new InvalidOperationException("გადახდის სისტემა დროებით მიუწვდომელია. სცადე მოგვიანებით.");
        }

        var session = await CreateStripeSessionAsync(order, returnPath, lineDescription, cancellationToken);
        await orderRepository.AttachProviderSessionAsync(order.Id, session.Id, cancellationToken);

        return new CheckoutResponse
        {
            OrderId = order.Id,
            TotalMinor = order.TotalMinor,
            Currency = order.Currency,
            IsFree = false,
            CheckoutUrl = session.Url,
            ProviderSessionId = session.Id,
            BookId = order.BookId
        };
    }

    private async Task<CheckoutResponse> StartBogCheckoutAsync(
        Order order,
        string? returnPath,
        string lineDescription,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_bog.ClientId) || string.IsNullOrWhiteSpace(_bog.SecretKey))
        {
            /*
              Logged, and specifically. The parent is told the payment system is unavailable —
              which is all they can act on — and the Stripe branch says exactly the same thing,
              so from the browser the two are indistinguishable. Without this line the only
              evidence of which one refused is a FailureReason on a row nobody is looking at.
            */
            logger.LogError(
                "Order {OrderId} routed to BOG, but Bog:ClientId is {ClientIdState} and Bog:SecretKey is "
                + "{SecretState}. Both are required; set Bog__ClientId and Bog__SecretKey.",
                order.Id,
                string.IsNullOrWhiteSpace(_bog.ClientId) ? "empty" : "set",
                string.IsNullOrWhiteSpace(_bog.SecretKey) ? "empty" : "set");

            await orderRepository.MarkFailedAsync(order.Id, "BOG is not configured.", cancellationToken);
            throw new InvalidOperationException("გადახდის სისტემა დროებით მიუწვდომელია. სცადე მოგვიანებით.");
        }

        var user = await userRepository.GetByIdAsync(order.UserId, cancellationToken);

        var checkout = await bogClient.CreateOrderAsync(
            new BogOrderRequest(
                order.Id,
                order.TotalMinor,
                order.Currency,
                lineDescription,
                // BOG returns the parent to a plain URL — there is no session placeholder to
                // substitute, so the order id in the query is the whole of what comes back.
                BuildReturnUrl(BogSiteBaseUrl, _bog.SuccessPath, returnPath, order.Id, includeSessionId: false),
                BuildReturnUrl(BogSiteBaseUrl, _bog.CancelPath, returnPath, order.Id, includeSessionId: false),
                user?.Email),
            cancellationToken);

        await orderRepository.AttachProviderSessionAsync(order.Id, checkout.BogOrderId, cancellationToken);

        return new CheckoutResponse
        {
            OrderId = order.Id,
            TotalMinor = order.TotalMinor,
            Currency = order.Currency,
            IsFree = false,
            CheckoutUrl = checkout.RedirectUrl,
            ProviderSessionId = checkout.BogOrderId,
            BookId = order.BookId
        };
    }

    private async Task<Session> CreateStripeSessionAsync(
        Order order,
        string? returnPath,
        string lineDescription,
        CancellationToken cancellationToken)
    {
        StripeConfiguration.ApiKey = _stripe.SecretKey;

        var user = await userRepository.GetByIdAsync(order.UserId, cancellationToken);

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = BuildReturnUrl(_stripe.SiteBaseUrl, _stripe.SuccessPath, returnPath, order.Id, includeSessionId: true),
            CancelUrl = BuildReturnUrl(_stripe.SiteBaseUrl, _stripe.CancelPath, returnPath, order.Id, includeSessionId: false),
            ClientReferenceId = order.Id.ToString(),
            ExpiresAt = DateTime.UtcNow.AddMinutes(Math.Max(30, _stripe.SessionExpiryMinutes)),
            Locale = "auto",
            LineItems = [BuildLineItem(order, lineDescription)],
            Metadata = new Dictionary<string, string>
            {
                ["orderId"] = order.Id.ToString(),
                ["userId"] = order.UserId.ToString(),
                ["type"] = order.Type.ToString(),
                ["package"] = order.Package.ToString()
            }
        };

        if (!string.IsNullOrWhiteSpace(user?.Email))
        {
            options.CustomerEmail = user.Email;
        }

        // Apple Pay and Google Pay are surfaced by Stripe under the card payment method
        // whenever the browser and the verified domain allow it. Leaving PaymentMethodTypes
        // unset lets the dashboard's automatic methods decide, which is what makes the
        // wallets appear; pinning it to "card" is the opt-out.
        if (!_stripe.EnableWallets)
        {
            options.PaymentMethodTypes = ["card"];
        }

        var service = new SessionService();
        return await service.CreateAsync(options, cancellationToken: cancellationToken);
    }

    private SessionLineItemOptions BuildLineItem(Order order, string lineDescription)
    {
        // A discounted order is billed as an ad-hoc amount: our promo maths, not Stripe's
        // coupons, is the source of truth, and charging the catalogue Price would ignore
        // the discount entirely.
        var priceId = ResolvePriceId(order);
        if (order.DiscountMinor == 0 && !string.IsNullOrWhiteSpace(priceId))
        {
            return new SessionLineItemOptions { Price = priceId, Quantity = 1 };
        }

        if (!_stripe.AllowAdHocAmounts)
        {
            throw new InvalidOperationException(
                "ფასდაკლებული შეკვეთის დამუშავება ვერ მოხერხდა. სცადე პრომოკოდის გარეშე.");
        }

        return new SessionLineItemOptions
        {
            Quantity = 1,
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = order.Currency.ToLowerInvariant(),
                UnitAmount = order.TotalMinor,
                ProductData = new SessionLineItemPriceDataProductDataOptions
                {
                    Name = lineDescription
                }
            }
        };
    }

    private string? ResolvePriceId(Order order) => order.Type switch
    {
        OrderType.PrintUpgrade => NullIfBlank(_stripe.PrintUpgradePriceId),
        OrderType.NewBook when order.Package == OrderPackage.Print => NullIfBlank(_stripe.PrintPriceId),
        OrderType.NewBook => NullIfBlank(_stripe.DigitalPriceId),
        _ => null
    };

    // -- payment application ------------------------------------------------

    private async Task HandleSessionCompletedAsync(Session? session, CancellationToken cancellationToken)
    {
        if (session is null)
        {
            logger.LogWarning("Stripe session-completed event carried no session object.");
            return;
        }

        var order = await ResolveOrderAsync(session, cancellationToken);
        if (order is null)
        {
            logger.LogWarning("No order matched Stripe session {SessionId}.", session.Id);
            return;
        }

        if (!IsSessionPaid(session))
        {
            logger.LogInformation(
                "Stripe session {SessionId} completed with payment status {PaymentStatus}; not fulfilling.",
                session.Id, session.PaymentStatus);
            return;
        }

        await ApplyPaymentAsync(order, session.PaymentIntentId, cancellationToken);
    }

    private async Task HandleSessionFailedAsync(Session? session, CancellationToken cancellationToken)
    {
        if (session is null)
        {
            return;
        }

        var order = await ResolveOrderAsync(session, cancellationToken);
        if (order is null || order.IsPaid)
        {
            return;
        }

        await orderRepository.MarkFailedAsync(
            order.Id, "გადახდა არ დასრულდა ან ვადა გაუვიდა.", cancellationToken);
    }

    private async Task<Order?> ResolveOrderAsync(Session session, CancellationToken cancellationToken)
    {
        // ClientReferenceId first: it survives even when metadata is stripped, and the
        // ProviderSessionId lookup covers sessions created before that was set.
        if (Guid.TryParse(session.ClientReferenceId, out var byReference))
        {
            var order = await orderRepository.GetByIdAsync(byReference, cancellationToken);
            if (order is not null)
            {
                return order;
            }
        }

        if (session.Metadata is not null &&
            session.Metadata.TryGetValue("orderId", out var metadataOrderId) &&
            Guid.TryParse(metadataOrderId, out var byMetadata))
        {
            var order = await orderRepository.GetByIdAsync(byMetadata, cancellationToken);
            if (order is not null)
            {
                return order;
            }
        }

        return await orderRepository.GetByProviderSessionIdAsync(session.Id, cancellationToken);
    }

    /// <summary>
    /// Records payment and fulfils, exactly once. Every caller — webhook, success page,
    /// free-order path — goes through here.
    /// </summary>
    private async Task ApplyPaymentAsync(
        Order order,
        string? paymentIntentId,
        CancellationToken cancellationToken)
    {
        var firstToPay = await orderRepository.TryMarkPaidAsync(order.Id, paymentIntentId, cancellationToken);
        if (!firstToPay)
        {
            // Someone else already recorded the payment. They may still be mid-fulfilment,
            // or have crashed; either way the sweeper is the backstop, so return quietly.
            logger.LogDebug("Order {OrderId} was already paid; skipping duplicate fulfilment.", order.Id);
            return;
        }

        /*
          From here on, nothing is allowed to be cancelled by the caller.

          The token handed in belongs to an HTTP request — the success page's confirm, or a
          webhook delivery — and the browser behind the first of those can navigate away at any
          moment. The money is already taken; a fulfilment that stopped because the parent closed
          a tab left a paid order half-made, and half-made in the one shape the retry sweep could
          not tell from unfulfilled: a pack written, the order not yet pointing at it, a second
          book five minutes later. A paid order's fulfilment runs to its own end, or to its own
          exception, whatever the request does.
        */
        var refreshed = await orderRepository.GetByIdAsync(order.Id, CancellationToken.None) ?? order;

        // Behind the exactly-once guard, so the alert cannot double-send however many callers
        // race to record the same payment. Before fulfilment rather than after: the point of
        // the mail is that money arrived, which is true whether or not the book then builds.
        await adminNotifier.OrderPaidAsync(refreshed, CancellationToken.None);

        await FulfillPaidOrderAsync(refreshed, CancellationToken.None);
    }

    private async Task<Guid?> FulfillPaidOrderAsync(Order order, CancellationToken cancellationToken)
    {
        // The promo is burned only after the money is in, so an abandoned checkout never
        // consumes a limited code.
        await promoCodeService.TryRedeemAsync(order, cancellationToken);

        try
        {
            var bookId = await bookFulfillmentService.FulfillAsync(order, cancellationToken);
            await orderRepository.TryMarkFulfilledAsync(order.Id, cancellationToken);
            return bookId;
        }
        catch (Exception ex)
        {
            // Stays Paid: the sweeper retries. Failing the order here would strand a
            // parent who has already been charged.
            logger.LogError(ex, "Fulfilling paid order {OrderId} failed; leaving it for retry.", order.Id);
            await orderRepository.MarkFailedAsync(order.Id, ex.Message, cancellationToken);
            return order.BookId;
        }
    }

    // -- validation ---------------------------------------------------------

    private async Task ValidateDraftAsync(
        Guid userId,
        BookDraftRequest draft,
        CancellationToken cancellationToken)
    {
        var hero = await characterRepository.GetByIdAsync(draft.PrimaryCharacterId, userId, cancellationToken)
                   ?? throw new InvalidOperationException("მთავარი გმირი ვერ მოიძებნა.");

        if (!hero.IsPrimary)
        {
            throw new InvalidOperationException("მთავარი გმირი უნდა იყოს ბავშვი, რომელზეც წიგნია.");
        }

        var supporting = draft.SupportingCharacterIds
            .Where(id => id != hero.Id)
            .Distinct()
            .ToList();

        if (supporting.Count + 1 > MaxCastSize)
        {
            throw new InvalidOperationException($"წიგნში მაქსიმუმ {MaxCastSize} პერსონაჟია.");
        }

        if (supporting.Count > 0)
        {
            var owned = await characterRepository.GetByIdsAsync(supporting, userId, cancellationToken);
            if (owned.Count != supporting.Count)
            {
                throw new InvalidOperationException("ზოგიერთი პერსონაჟი ვერ მოიძებნა.");
            }
        }

        await worldProgressService.EnsureCanStartAsync(userId, hero.Id, draft.WorldId, cancellationToken);

        if (draft.ContinuesFromBookId is { } continuesFrom)
        {
            var previous = await packRepository.GetByIdAsync(continuesFrom, userId, cancellationToken)
                           ?? throw new InvalidOperationException("წინა წიგნი ვერ მოიძებნა.");

            if (!previous.IsFullyUnlocked)
            {
                throw new InvalidOperationException("გაგრძელება მხოლოდ შეძენილი წიგნიდან შეიძლება.");
            }
        }
    }

    // -- mapping ------------------------------------------------------------

    private async Task<OrderStatusResponse> BuildStatusAsync(Order order, CancellationToken cancellationToken)
    {
        var response = new OrderStatusResponse
        {
            OrderId = order.Id,
            Status = order.Status,
            BookId = order.BookId,
            FailureReason = order.FailureReason
        };

        if (order.BookId is { } bookId)
        {
            var book = await packRepository.GetByIdAsync(bookId, order.UserId, cancellationToken);
            if (book is not null)
            {
                /*
                  Two pipelines, two finishing lines — and now the row says which pipeline this is.

                  StoryReady is a finished book on the legacy path and a book with words and no
                  pictures on the Beki one. Counting both as ready sent a Beki parent to a reader
                  full of empty pages within seconds of paying, and then to a dashboard that showed
                  the book as done and stopped polling it. The correction needed a fact nobody had:
                  amendment B5's GenerationPipeline column, stamped by the code that chose the
                  pipeline rather than guessed at here.

                  A legacy book keeps exactly the rule it always had. There is no behaviour change
                  for the books already in production; what changes is that a Beki book is no longer
                  judged by a rule written for a different kind of book.
                */
                response.BookReady = book.IsFullyUnlocked
                    && (book.IsBekiPipeline
                        ? book.Status == AdventurePackStatus.Completed
                        : book.Status is AdventurePackStatus.StoryReady
                            or AdventurePackStatus.Completed);

                response.ProgressMessage = book.ProgressMessage;

                /*
                  The wait, described rather than only asserted.

                  Readiness alone told the generating screen to keep spinning and nothing else: no
                  percentage for a bar, no stage for a caption, no heartbeat to tell a slow book
                  from a silent one — and, after a hard reload from the bank with nothing but an
                  order id, no title, no world and no child's name, so the screen showed a generic
                  book for "შენი გმირი". Each of these is the pack's own column, copied as stored;
                  the hero's name is the one lookup, and it is best-effort below.
                */
                response.ProgressPercent = book.ProgressPercent;
                response.PackStatus = book.Status.ToString();
                response.HeartbeatUtc = book.GenerationHeartbeatUtc;
                response.Title = NullIfBlank(book.Title);
                response.WorldId = NullIfBlank(book.WorldId);
                response.CoverImageUrl = NullIfBlank(book.CoverImageUrl);
                response.ChildName = await HeroNameAsync(book.PrimaryCharacterId, order.UserId, cancellationToken);

                /*
                  The other end of the same question, and the one nobody was asking.

                  This response reported readiness and nothing else, so a book that had failed
                  looked exactly like a book that was slow: BookReady false, the order still
                  Fulfilled — it is marked fulfilled when generation is enqueued — and the progress
                  message frozen at whatever the dead job last wrote. Pack 7fc8faf4 sat on a paid
                  parent's screen at "18%" indefinitely for that reason.

                  The message is mapped, not copied. book.ErrorMessage is
                  "IMAGE_QA_FAILED (spread 1): …" and belongs to the operator; FailureReason above
                  stays the payment's own, so the two failures are never confused for each other.
                */
                if (book.Status == AdventurePackStatus.Failed)
                {
                    response.BookFailed = true;
                    response.ParentMessage = ParentFacingFailure.ToParentMessage(book.ErrorMessage);

                    // Overwrites the frozen mid-generation line for the books that already have
                    // one. New failures write a parent-safe line into the row itself, but every
                    // pack failed before that stopped mid-sentence, and this screen is where the
                    // parent reads it.
                    response.ProgressMessage = response.ParentMessage;
                }
            }
        }

        if (order.BookId is null && order.Type == OrderType.NewBook)
        {
            /*
              Paid, and the book row not there yet.

              This is the window between the payment landing and fulfilment writing the pack —
              seconds inline, or up to a sweep's interval after a fulfilment that died. The screen
              polling it used to get nothing at all: no line, no world, no name. It gets the same
              Georgian line the pack will open with, and what the frozen draft already knows about
              the book, so a reload from the bank shows the right child in the right world before
              the first row exists.
            */
            if (order.IsPaid)
            {
                response.ProgressMessage = "გადახდა მიღებულია — იწყება წიგნის შექმნა.";
            }

            if (TryReadDraft(order) is { } draft)
            {
                response.WorldId = NullIfBlank(draft.WorldId)?.Trim().ToLowerInvariant();
                response.ChildName = await HeroNameAsync(draft.PrimaryCharacterId, order.UserId, cancellationToken);
            }
        }

        return response;
    }

    /// <summary>
    /// The hero's name for the status screen. Null on any failure: a poll that answers "is my
    /// book ready" must not fail because a name lookup did.
    /// </summary>
    private async Task<string?> HeroNameAsync(Guid? characterId, Guid userId, CancellationToken cancellationToken)
    {
        if (characterId is not { } heroId)
        {
            return null;
        }

        try
        {
            var hero = await characterRepository.GetByIdAsync(heroId, userId, cancellationToken);
            return NullIfBlank(hero?.Name)?.Trim();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Could not resolve hero {CharacterId} for an order status; leaving the name blank.", heroId);
            return null;
        }
    }

    /// <summary>The frozen checkout draft, or null when the order has none or it will not parse.</summary>
    private static BookDraftRequest? TryReadDraft(Order order)
    {
        if (string.IsNullOrWhiteSpace(order.DraftJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BookDraftRequest>(order.DraftJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static OrderResponse ToResponse(Order order, string? promoCode) => new()
    {
        Id = order.Id,
        BookId = order.BookId,
        Type = order.Type,
        Package = order.Package,
        Currency = order.Currency,
        SubtotalMinor = order.SubtotalMinor,
        DiscountMinor = order.DiscountMinor,
        TotalMinor = order.TotalMinor,
        Status = order.Status,
        PromoCode = promoCode,
        FailureReason = order.FailureReason,
        CreatedAt = order.CreatedAt,
        PaidAt = order.PaidAt,
        FulfilledAt = order.FulfilledAt
    };

    // -- helpers ------------------------------------------------------------

    private async Task<Session?> RetrieveSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (!_stripe.Enabled || string.IsNullOrWhiteSpace(_stripe.SecretKey))
        {
            return null;
        }

        try
        {
            StripeConfiguration.ApiKey = _stripe.SecretKey;
            var service = new SessionService();
            return await service.GetAsync(sessionId, cancellationToken: cancellationToken);
        }
        catch (StripeException ex)
        {
            // A confirmation poll must not 500 the status endpoint; the webhook remains
            // the authoritative path.
            logger.LogWarning(ex, "Retrieving Stripe session {SessionId} failed.", sessionId);
            return null;
        }
    }

    private static bool IsBog(Order order) =>
        string.Equals(order.Provider, OrderProviders.Bog, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Falls back to the Stripe setting because it is the same site: two copies of one URL
    /// is a configuration trap, and the one that goes stale is the one nobody is watching.
    /// </summary>
    private string BogSiteBaseUrl => string.IsNullOrWhiteSpace(_bog.SiteBaseUrl)
        ? _stripe.SiteBaseUrl
        : _bog.SiteBaseUrl;

    private static bool IsSessionPaid(Session session) =>
        string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(session.PaymentStatus, "no_payment_required", StringComparison.OrdinalIgnoreCase);

    private static string BuildReturnUrl(
        string siteBaseUrl,
        string path,
        string? returnPath,
        Guid orderId,
        bool includeSessionId)
    {
        var baseUrl = (siteBaseUrl ?? string.Empty).TrimEnd('/');
        if (baseUrl.Length == 0)
        {
            throw new InvalidOperationException("გადახდის დაბრუნების მისამართი არ არის კონფიგურირებული.");
        }

        var target = SafeRelativePath(returnPath) ?? (path.StartsWith('/') ? path : "/" + path);

        // `/create#generating?orderId=…` puts the query inside the hash and breaks
        // stageFromHash / order restore. Keep query on the path, hash at the end.
        var hashIndex = target.IndexOf('#');
        var pathPart = hashIndex >= 0 ? target[..hashIndex] : target;
        var hashPart = hashIndex >= 0 ? target[hashIndex..] : string.Empty;
        if (pathPart.Length == 0)
        {
            pathPart = "/";
        }

        var separator = pathPart.Contains('?') ? '&' : '?';
        var url = $"{baseUrl}{pathPart}{separator}orderId={orderId}";

        // Stripe substitutes the placeholder itself, so it must survive escaping intact.
        if (includeSessionId)
        {
            url = $"{url}&session_id={{CHECKOUT_SESSION_ID}}";
        }

        return url + hashPart;
    }

    /// <summary>Only same-origin relative paths are honoured, so a return URL cannot be a redirect.</summary>
    private static string? SafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.Trim();
        return trimmed.StartsWith('/') && !trimmed.StartsWith("//") ? trimmed : null;
    }

    private static string BookLineDescription(OrderPackage package) => package == OrderPackage.Print
        ? "პერსონალური წიგნი — ბეჭდური და ციფრული"
        : "პერსონალური წიგნი — ციფრული";

    private static OrderType ParseType(string? value) =>
        Enum.TryParse<OrderType>((value ?? string.Empty).Trim(), ignoreCase: true, out var type)
            ? type
            : throw new InvalidOperationException("შეკვეთის ტიპი არასწორია.");

    private static OrderPackage ParsePackage(string? value) =>
        Enum.TryParse<OrderPackage>((value ?? string.Empty).Trim(), ignoreCase: true, out var package)
            ? package
            : throw new InvalidOperationException("პაკეტი არასწორია.");

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? Serialize(ShippingAddressRequest? address) =>
        address is null ? null : JsonSerializer.Serialize(address, JsonOptions);

    /// <summary>
    /// A print order with no address is unfulfillable, so it is rejected at checkout
    /// rather than becoming a paid parcel nobody can post.
    /// </summary>
    private static ShippingAddressRequest RequireShippingAddress(ShippingAddressRequest? address)
    {
        if (address is null ||
            string.IsNullOrWhiteSpace(address.RecipientName) ||
            string.IsNullOrWhiteSpace(address.RecipientPhone) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.AddressLine1))
        {
            throw new InvalidOperationException("ბეჭდური წიგნისთვის მიწოდების მისამართი აუცილებელია.");
        }

        return address;
    }

    private static string? Lookup(IReadOnlyDictionary<Guid, string> map, Guid? key) =>
        key is { } id && map.TryGetValue(id, out var value) ? value : null;
}
