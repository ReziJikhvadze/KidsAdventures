using System.Text.Json;

using Stripe;
using Stripe.Checkout;

using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Orders;
using AdventurePacks.Api.DTOs.Print;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Checkout and fulfilment for the Georgian product.
///
/// Three properties are worth stating outright, because the rest of the class follows
/// from them:
///
/// 1. The server prices everything. The client sends a package and a promo code, never an
///    amount, so a tampered request cannot buy a 79 GEL book for 1 tetri.
/// 2. Fulfilment is idempotent at the database, not in application logic. Stripe delivers
///    <c>checkout.session.completed</c> more than once, the success page confirms in
///    parallel, and the sweeper retries: all three funnel through
///    <see cref="IOrderRepository.TryMarkPaidAsync"/>, and only the first writer proceeds.
/// 3. A zero-total order never touches Stripe. A GIFT100 code has nothing to collect, and
///    routing 0 GEL through a payment provider fails in ways that are hard to explain to
///    a parent.
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
    IOptions<StripeOptions> stripeOptions,
    ILogger<OrderService> logger) : IOrderService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Matches <c>CK_BookCharacters_Position</c>.</summary>
    private const int MaxCastSize = 3;

    /// <summary>Fulfilment that has not finished in this long is treated as stalled.</summary>
    private static readonly TimeSpan StalledAfter = TimeSpan.FromMinutes(5);

    private readonly StripeOptions _stripe = stripeOptions.Value;

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
            Provider = priced.IsFree ? OrderProviders.Promo : OrderProviders.Stripe,
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
            Provider = priced.IsFree ? OrderProviders.Promo : OrderProviders.Stripe,
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
            var session = await RetrieveSessionAsync(sessionId, cancellationToken);
            if (session is not null && IsSessionPaid(session))
            {
                await ApplyPaymentAsync(order, session.PaymentIntentId, cancellationToken);
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

    public async Task RetryStalledFulfilmentAsync()
    {
        var stalled = await orderRepository.GetStalledPaidAsync(
            DateTime.UtcNow - StalledAfter, limit: 25, CancellationToken.None);

        foreach (var order in stalled)
        {
            try
            {
                var bookId = await bookFulfillmentService.FulfillAsync(order, CancellationToken.None);
                await orderRepository.TryMarkFulfilledAsync(order.Id, CancellationToken.None);
                logger.LogInformation(
                    "Retried fulfilment for stalled order {OrderId}; book {BookId}.", order.Id, bookId);
            }
            catch (Exception ex)
            {
                // Left as Paid on purpose so the next sweep tries again; a paid order is
                // never marked Failed, because the money is real.
                logger.LogError(ex, "Retrying fulfilment for order {OrderId} failed.", order.Id);
                await orderRepository.MarkFailedAsync(order.Id, ex.Message, CancellationToken.None);
            }
        }
    }

    // -- checkout -----------------------------------------------------------

    private async Task<CheckoutResponse> StartCheckoutAsync(
        Order order,
        string? returnPath,
        string lineDescription,
        CancellationToken cancellationToken)
    {
        if (order.IsFree)
        {
            // Nothing to collect. Mark it paid ourselves and fulfil inline, so the parent
            // goes straight from "GIFT100 applied" to a book being written.
            await orderRepository.TryMarkPaidAsync(order.Id, null, cancellationToken);
            var refreshed = await orderRepository.GetByIdAsync(order.Id, cancellationToken) ?? order;
            var bookId = await FulfillPaidOrderAsync(refreshed, cancellationToken);

            return new CheckoutResponse
            {
                OrderId = order.Id,
                TotalMinor = 0,
                Currency = order.Currency,
                IsFree = true,
                BookId = bookId
            };
        }

        if (!_stripe.Enabled || string.IsNullOrWhiteSpace(_stripe.SecretKey))
        {
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
            SuccessUrl = BuildReturnUrl(_stripe.SuccessPath, returnPath, order.Id, includeSessionId: true),
            CancelUrl = BuildReturnUrl(_stripe.CancelPath, returnPath, order.Id, includeSessionId: false),
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

        var refreshed = await orderRepository.GetByIdAsync(order.Id, cancellationToken) ?? order;
        await FulfillPaidOrderAsync(refreshed, cancellationToken);
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
                response.BookReady = book.IsFullyUnlocked && book.Status == AdventurePackStatus.StoryReady;
                response.ProgressMessage = book.ProgressMessage;
            }
        }

        return response;
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

    private static bool IsSessionPaid(Session session) =>
        string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(session.PaymentStatus, "no_payment_required", StringComparison.OrdinalIgnoreCase);

    private string BuildReturnUrl(string path, string? returnPath, Guid orderId, bool includeSessionId)
    {
        var baseUrl = (_stripe.SiteBaseUrl ?? string.Empty).TrimEnd('/');
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
