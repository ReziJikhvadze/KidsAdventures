using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain;
using AdventurePacks.Api.DTOs.Subscriptions;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using DodoPayments.Client;
using DodoPayments.Client.Models.CheckoutSessions;
using DodoPayments.Client.Models.Payments;
using ApiCheckoutSessionResponse = AdventurePacks.Api.DTOs.Subscriptions.CheckoutSessionResponse;
using Stripe;
using Stripe.Checkout;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class SubscriptionService(
    IUserRepository userRepository,
    IBookCreditPurchaseRepository bookCreditPurchaseRepository,
    IAdventurePackRepository adventurePackRepository,
    IOptions<StripeOptions> stripeOptions,
    IOptions<DodoPaymentsOptions> dodoOptions,
    DodoPaymentsClient dodoClient) : ISubscriptionService
{
    private readonly StripeOptions _stripe = stripeOptions.Value;
    private readonly DodoPaymentsOptions _dodo = dodoOptions.Value;

    private bool UseDodo => _dodo.Enabled && !string.IsNullOrWhiteSpace(_dodo.ApiKey);

    private bool UseStripe =>
        _stripe.Enabled && !string.IsNullOrWhiteSpace(_stripe.SecretKey) && !UseDodo;

    public async Task<AccountBalanceResponse> GetAccountBalanceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken)
                   ?? throw new UnauthorizedAccessException("User not found.");

        var quota = await GetStoryQuotaAsync(userId, user.BookCredits, cancellationToken);

        return new AccountBalanceResponse
        {
            BookCredits = user.BookCredits,
            StoriesUsedThisMonth = quota.Used,
            StoriesAllowedThisMonth = quota.Allowed,
            StoriesRemainingThisMonth = quota.Remaining,
            WelcomeStoryRemaining = user.WelcomeStoryRemaining,
            SubscriptionType = user.SubscriptionType,
            HasUnlimitedPdf = false
        };
    }

    private async Task<(int Used, int Allowed, int Remaining)> GetStoryQuotaAsync(
        Guid userId,
        int bookCredits,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);
        var used = await adventurePackRepository.CountForMonthAsync(userId, monthStart, monthEnd, cancellationToken);
        var allowed = bookCredits;
        var remaining = Math.Max(0, allowed - used);
        return (used, allowed, remaining);
    }

    public async Task EnsureGenerationAllowedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken)
                   ?? throw new UnauthorizedAccessException("User not found.");

        if (user.WelcomeStoryRemaining > 0)
        {
            return;
        }

        var quota = await GetStoryQuotaAsync(userId, user.BookCredits, cancellationToken);
        if (quota.Remaining <= 0)
        {
            throw new InvalidOperationException(
                user.BookCredits > 0
                    ? "You've used all your purchased book credits for this month. Buy more credits to create another full 6-page story."
                    : "Your free 2-page welcome story was used. Buy book credits to unlock full 6-page illustrated adventures — PDF export stays free.");
        }
    }

    public Task EnsurePdfGenerationAllowedAsync(Guid userId, CancellationToken cancellationToken)
    {
        _ = userId;
        return Task.CompletedTask;
    }

    public Task<bool> TryChargePdfCreditAsync(Guid userId, Guid packId, CancellationToken cancellationToken)
    {
        _ = userId;
        _ = packId;
        _ = cancellationToken;
        return Task.FromResult(false);
    }

    public async Task RefundPdfCreditIfChargedAsync(Guid userId, Guid packId, CancellationToken cancellationToken)
    {
        var pack = await adventurePackRepository.GetByIdNoOwnershipAsync(packId, cancellationToken);
        if (pack is null || !pack.PdfCreditCharged)
        {
            return;
        }

        await userRepository.RefundBookCreditAsync(userId, cancellationToken);
        await adventurePackRepository.SetPdfCreditChargedAsync(packId, false, cancellationToken);
    }

    public async Task<ApiCheckoutSessionResponse> CreateCheckoutSessionAsync(
        Guid userId,
        string email,
        string planType,
        CancellationToken cancellationToken)
    {
        if (!BookPackPlans.IsSupported(planType))
        {
            throw new InvalidOperationException("Only Books3, Books5, and Books15 packs are supported.");
        }

        if (UseDodo)
        {
            return await CreateDodoCheckoutSessionAsync(userId, email, planType, cancellationToken);
        }

        if (UseStripe)
        {
            return await CreateStripeCheckoutSessionAsync(userId, email, planType, cancellationToken);
        }

        throw new InvalidOperationException("Payments are not configured. Set DodoPayments:Enabled and ApiKey.");
    }

    public async Task<AccountBalanceResponse> ConfirmCheckoutSessionAsync(
        Guid userId,
        string? sessionId,
        string? paymentId,
        CancellationToken cancellationToken)
    {
        if (UseDodo)
        {
            return await ConfirmDodoCheckoutAsync(userId, sessionId, paymentId, cancellationToken);
        }

        if (UseStripe)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new InvalidOperationException("Checkout session id is required.");
            }

            return await ConfirmStripeCheckoutSessionAsync(userId, sessionId, cancellationToken);
        }

        throw new InvalidOperationException("Payments are not configured.");
    }

    public async Task HandleWebhookAsync(string jsonPayload, string stripeSignature, CancellationToken cancellationToken)
    {
        if (!UseStripe || string.IsNullOrWhiteSpace(_stripe.WebhookSecret))
        {
            return;
        }

        StripeConfiguration.ApiKey = _stripe.SecretKey;

        var stripeEvent = EventUtility.ConstructEvent(jsonPayload, stripeSignature, _stripe.WebhookSecret);
        if (!string.Equals(stripeEvent.Type, "checkout.session.completed", StringComparison.Ordinal))
        {
            return;
        }

        var session = stripeEvent.Data.Object as Session;
        if (session?.Metadata is null
            || !session.Metadata.TryGetValue("userId", out var userIdRaw)
            || !Guid.TryParse(userIdRaw, out var userId))
        {
            return;
        }

        await TryFulfillStripeCheckoutSessionAsync(session, userId, cancellationToken);
    }

    public async Task HandleDodoWebhookAsync(
        string jsonPayload,
        string webhookId,
        string webhookSignature,
        string webhookTimestamp,
        CancellationToken cancellationToken)
    {
        if (!UseDodo)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_dodo.WebhookSecret))
        {
            StandardWebhookVerifier.Verify(
                jsonPayload,
                webhookId,
                webhookSignature,
                webhookTimestamp,
                _dodo.WebhookSecret);
        }

        using var document = JsonDocument.Parse(jsonPayload);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        var eventType = typeElement.GetString();
        if (!string.Equals(eventType, "payment.succeeded", StringComparison.Ordinal))
        {
            return;
        }

        if (!root.TryGetProperty("data", out var dataElement))
        {
            return;
        }

        var paymentId = ReadStringProperty(dataElement, "payment_id");
        var metadata = dataElement.TryGetProperty("metadata", out var metadataElement)
            ? metadataElement
            : default;

        if (metadata.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!TryReadMetadataGuid(metadata, "userId", out var userId)
            || !TryReadMetadataString(metadata, "planType", out var planType))
        {
            return;
        }

        await TryFulfillDodoPurchaseAsync(userId, paymentId ?? webhookId, planType, cancellationToken);
    }

    private async Task<ApiCheckoutSessionResponse> CreateDodoCheckoutSessionAsync(
        Guid userId,
        string email,
        string planType,
        CancellationToken cancellationToken)
    {
        var productId = BookPackPlans.GetDodoProductId(planType, _dodo);
        if (string.IsNullOrWhiteSpace(productId))
        {
            throw new InvalidOperationException($"Dodo product is not configured for {planType}.");
        }

        if (string.IsNullOrWhiteSpace(_dodo.SuccessUrl))
        {
            throw new InvalidOperationException("DodoPayments:SuccessUrl is not configured.");
        }

        var parameters = new CheckoutSessionCreateParams
        {
            ProductCart =
            [
                new ProductItemReq
                {
                    ProductID = productId,
                    Quantity = 1,
                },
            ],
            Customer = new NewCustomer
            {
                Email = email,
            },
            ReturnUrl = _dodo.SuccessUrl,
            CancelUrl = string.IsNullOrWhiteSpace(_dodo.CancelUrl) ? null : _dodo.CancelUrl,
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = userId.ToString(),
                ["planType"] = planType,
            },
        };

        var session = await dodoClient.CheckoutSessions.Create(parameters, cancellationToken);
        return new ApiCheckoutSessionResponse
        {
            SessionId = session.SessionID ?? string.Empty,
            CheckoutUrl = session.CheckoutUrl ?? string.Empty,
        };
    }

    private async Task<ApiCheckoutSessionResponse> CreateStripeCheckoutSessionAsync(
        Guid userId,
        string email,
        string planType,
        CancellationToken cancellationToken)
    {
        var priceId = BookPackPlans.GetPriceId(planType, _stripe);
        if (string.IsNullOrWhiteSpace(priceId))
        {
            throw new InvalidOperationException($"Stripe price is not configured for {planType}.");
        }

        StripeConfiguration.ApiKey = _stripe.SecretKey;

        var service = new SessionService();
        var session = await service.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = _stripe.SuccessUrl,
            CancelUrl = _stripe.CancelUrl,
            CustomerEmail = email,
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = 1
                }
            ],
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = userId.ToString(),
                ["planType"] = planType
            }
        }, cancellationToken: cancellationToken);

        return new ApiCheckoutSessionResponse
        {
            SessionId = session.Id,
            CheckoutUrl = session.Url ?? string.Empty
        };
    }

    private async Task<AccountBalanceResponse> ConfirmDodoCheckoutAsync(
        Guid userId,
        string? sessionId,
        string? paymentId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var fulfilled = await TryFulfillDodoCheckoutSessionAsync(userId, sessionId, cancellationToken);
            if (!fulfilled)
            {
                throw new InvalidOperationException("Checkout session could not be confirmed for this account.");
            }

            return await GetAccountBalanceAsync(userId, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(paymentId))
        {
            var payment = await dodoClient.Payments.Retrieve(
                new PaymentRetrieveParams { PaymentID = paymentId },
                cancellationToken);

            if (!string.Equals(payment.Status?.ToString(), "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Payment is not completed yet.");
            }

            var fulfilled = await TryFulfillDodoPaymentAsync(userId, payment, paymentId, cancellationToken);
            if (!fulfilled)
            {
                throw new InvalidOperationException("Checkout session could not be confirmed for this account.");
            }
            return await GetAccountBalanceAsync(userId, cancellationToken);
        }

        throw new InvalidOperationException("Checkout session id or payment id is required.");
    }

    private async Task<bool> TryFulfillDodoCheckoutSessionAsync(
        Guid expectedUserId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var status = await dodoClient.CheckoutSessions.Retrieve(
            new CheckoutSessionRetrieveParams { ID = sessionId },
            cancellationToken);

        if (!string.Equals(status.PaymentStatus?.ToString(), "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(status.PaymentID))
        {
            return false;
        }

        var payment = await dodoClient.Payments.Retrieve(
            new PaymentRetrieveParams { PaymentID = status.PaymentID },
            cancellationToken);

        return await TryFulfillDodoPaymentAsync(expectedUserId, payment, status.PaymentID, cancellationToken);
    }

    private async Task<bool> TryFulfillDodoPaymentAsync(
        Guid expectedUserId,
        Payment payment,
        string fulfillmentId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(payment.Status?.ToString(), "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (payment.Metadata is null
            || !TryReadMetadataGuidFromPayment(payment.Metadata, "userId", out var userId)
            || userId != expectedUserId
            || !TryReadMetadataStringFromPayment(payment.Metadata, "planType", out var planType)
            || !BookPackPlans.IsSupported(planType))
        {
            return false;
        }

        return await TryFulfillDodoPurchaseAsync(userId, fulfillmentId, planType, cancellationToken);
    }

    private async Task<bool> TryFulfillDodoPurchaseAsync(
        Guid userId,
        string fulfillmentId,
        string planType,
        CancellationToken cancellationToken)
    {
        var credits = BookPackPlans.GetCredits(planType);
        var recorded = await bookCreditPurchaseRepository.TryRecordPurchaseAsync(
            userId,
            fulfillmentId,
            credits,
            planType,
            cancellationToken);

        if (!recorded)
        {
            return true;
        }

        await userRepository.AddBookCreditsAsync(userId, credits, cancellationToken);
        return true;
    }

    private async Task<AccountBalanceResponse> ConfirmStripeCheckoutSessionAsync(
        Guid userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        StripeConfiguration.ApiKey = _stripe.SecretKey;

        var service = new SessionService();
        var session = await service.GetAsync(sessionId, cancellationToken: cancellationToken);

        if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Payment is not completed yet.");
        }

        await FulfillStripeCheckoutSessionAsync(session, userId, cancellationToken);
        return await GetAccountBalanceAsync(userId, cancellationToken);
    }

    private async Task FulfillStripeCheckoutSessionAsync(
        Session session,
        Guid expectedUserId,
        CancellationToken cancellationToken)
    {
        var fulfilled = await TryFulfillStripeCheckoutSessionAsync(session, expectedUserId, cancellationToken);
        if (!fulfilled)
        {
            throw new InvalidOperationException("Checkout session could not be confirmed for this account.");
        }
    }

    private async Task<bool> TryFulfillStripeCheckoutSessionAsync(
        Session session,
        Guid expectedUserId,
        CancellationToken cancellationToken)
    {
        if (session.Metadata is null
            || !session.Metadata.TryGetValue("userId", out var userIdRaw)
            || !Guid.TryParse(userIdRaw, out var userId)
            || userId != expectedUserId
            || !session.Metadata.TryGetValue("planType", out var planType)
            || !BookPackPlans.IsSupported(planType))
        {
            return false;
        }

        if (!string.Equals(session.Mode, "payment", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var credits = BookPackPlans.GetCredits(planType);
        var recorded = await bookCreditPurchaseRepository.TryRecordPurchaseAsync(
            userId,
            session.Id,
            credits,
            planType,
            cancellationToken);

        if (!recorded)
        {
            return true;
        }

        await userRepository.AddBookCreditsAsync(userId, credits, cancellationToken);
        return true;
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryReadMetadataGuid(JsonElement metadata, string key, out Guid value)
    {
        value = default;
        if (!metadata.TryGetProperty(key, out var element))
        {
            return false;
        }

        var raw = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        return Guid.TryParse(raw, out value);
    }

    private static bool TryReadMetadataString(JsonElement metadata, string key, out string value)
    {
        value = string.Empty;
        if (!metadata.TryGetProperty(key, out var element))
        {
            return false;
        }

        var raw = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw;
        return true;
    }

    private static bool TryReadMetadataGuidFromPayment(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        out Guid value)
    {
        value = default;
        return metadata.TryGetValue(key, out var raw) && Guid.TryParse(raw, out value);
    }

    private static bool TryReadMetadataStringFromPayment(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        out string value)
    {
        value = string.Empty;
        return metadata.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw) && (value = raw).Length > 0;
    }
}
