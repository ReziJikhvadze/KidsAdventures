using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain;
using AdventurePacks.Api.DTOs.Subscriptions;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using Stripe;
using Stripe.Checkout;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class SubscriptionService(
    IUserRepository userRepository,
    IBookCreditPurchaseRepository bookCreditPurchaseRepository,
    IAdventurePackRepository adventurePackRepository,
    IOptions<StripeOptions> stripeOptions) : ISubscriptionService
{
    private readonly StripeOptions _stripe = stripeOptions.Value;

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

    public async Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(
        Guid userId,
        string email,
        string planType,
        CancellationToken cancellationToken)
    {
        if (!BookPackPlans.IsSupported(planType))
        {
            throw new InvalidOperationException("Only Books3, Books5, and Books15 packs are supported.");
        }

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

        return new CheckoutSessionResponse
        {
            SessionId = session.Id,
            CheckoutUrl = session.Url ?? string.Empty
        };
    }

    public async Task<AccountBalanceResponse> ConfirmCheckoutSessionAsync(
        Guid userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Checkout session id is required.");
        }

        StripeConfiguration.ApiKey = _stripe.SecretKey;

        var service = new SessionService();
        var session = await service.GetAsync(sessionId, cancellationToken: cancellationToken);

        if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Payment is not completed yet.");
        }

        await FulfillPaidCheckoutSessionAsync(session, userId, cancellationToken);
        return await GetAccountBalanceAsync(userId, cancellationToken);
    }

    public async Task HandleWebhookAsync(string jsonPayload, string stripeSignature, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_stripe.WebhookSecret))
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

        await TryFulfillPaidCheckoutSessionAsync(session, userId, cancellationToken);
    }

    private async Task FulfillPaidCheckoutSessionAsync(
        Session session,
        Guid expectedUserId,
        CancellationToken cancellationToken)
    {
        var fulfilled = await TryFulfillPaidCheckoutSessionAsync(session, expectedUserId, cancellationToken);
        if (!fulfilled)
        {
            throw new InvalidOperationException("Checkout session could not be confirmed for this account.");
        }
    }

    private async Task<bool> TryFulfillPaidCheckoutSessionAsync(
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
}
