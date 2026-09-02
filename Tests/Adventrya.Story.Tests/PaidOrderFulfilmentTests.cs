using System.Text;
using System.Text.Json;

using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.DTOs.Orders;
using AdventurePacks.Api.DTOs.Print;
using AdventurePacks.Api.DTOs.Worlds;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Interfaces;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// What happens to a paid order between the money landing and the book row existing.
///
/// Two faults lived in that gap. Fulfilment ran under the HTTP request's token, so a parent who
/// navigated away from the success page mid-confirm could cancel the making of a book they had
/// paid for; and the pack and the order's pointer to it were two writes, so a fulfilment that died
/// between them left a real pack no order pointed at — and the stalled-order sweep, seeing a paid
/// order with no BookId, made a second one. A third fault sat beside them: the preview run was
/// claimed only when adoption succeeded, so a failed adoption left the run to the guest purge, and
/// a paid retry that then found no run was quietly re-routed to the legacy pipeline.
///
/// The doubles here answer the way the SQL does where it matters — the paid-once write is a real
/// compare-and-set — and refuse everything else, so a change that starts reaching for a
/// collaborator these paths must not touch fails here rather than passing quietly.
/// </summary>
public class PaidOrderFulfilmentTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // -- the request token stops at the payment -------------------------------

    [Fact]
    public async Task Fulfilment_of_a_paid_order_does_not_run_under_the_requests_token()
    {
        /*
          The browser leaves the moment fulfilment starts — a parent closing the tab on the
          success page — and the book is made anyway. The token fulfilment receives is one nobody
          can cancel, and the order reaches Fulfilled.
        */
        var world = new ConfirmWorld();
        using var request = new CancellationTokenSource();
        world.Fulfilment.OnCall = request.Cancel;

        var status = await world.Service().ConfirmAsync(world.Order.UserId, world.Order.Id, request.Token);

        var token = Assert.Single(world.Fulfilment.Tokens);
        Assert.False(token.CanBeCanceled);
        Assert.False(token.IsCancellationRequested);

        Assert.Equal(OrderStatus.Fulfilled, world.Orders.Current.Status);
        Assert.Equal(OrderStatus.Fulfilled, status.Status);
        Assert.Equal(1, world.Notifier.OrderPaidCalls);
    }

    [Fact]
    public async Task The_webhook_path_fulfils_under_no_ones_token_either()
    {
        // Same shape, other door: BOG's callback is an HTTP request too, and the bank's client
        // can drop it just as a browser can.
        var world = new ConfirmWorld();
        using var request = new CancellationTokenSource();
        world.Fulfilment.OnCall = request.Cancel;

        var accepted = await world.Service().HandleBogWebhookAsync(
            world.CompletedCallback(), signature: null, request.Token);

        Assert.True(accepted);
        var token = Assert.Single(world.Fulfilment.Tokens);
        Assert.False(token.CanBeCanceled);
        Assert.Equal(OrderStatus.Fulfilled, world.Orders.Current.Status);
    }

    [Fact]
    public async Task A_second_confirmation_of_the_same_payment_fulfils_nothing()
    {
        // The paid-once write is the whole idempotency story, and moving the token did not move
        // it: the second caller loses the compare-and-set and returns quietly.
        var world = new ConfirmWorld();

        await world.Service().ConfirmAsync(world.Order.UserId, world.Order.Id, CancellationToken.None);
        await world.Service().HandleBogWebhookAsync(world.CompletedCallback(), null, CancellationToken.None);

        Assert.Single(world.Fulfilment.Tokens);
        Assert.Equal(1, world.Notifier.OrderPaidCalls);
    }

    [Fact]
    public async Task A_paid_order_still_without_its_book_row_tells_the_parent_it_is_starting()
    {
        // The status the generating screen polls in the gap: not ready, not failed, and a
        // Georgian line rather than a blank.
        var world = new ConfirmWorld();

        var status = await world.Service().ConfirmAsync(world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.False(status.BookReady);
        Assert.False(status.BookFailed);
        Assert.Null(status.BookId);
        Assert.Null(status.FailureReason);
        Assert.False(string.IsNullOrWhiteSpace(status.ProgressMessage));
    }

    // -- the pack and its pointer are one write --------------------------------

    [Fact]
    public async Task The_pack_and_the_orders_pointer_to_it_are_written_together()
    {
        /*
          The order repository's own SetBookIdAsync is refused by this double. If fulfilment still
          reached for it, this would throw; instead the pack repository is handed the order id and
          commits both in one transaction.
        */
        var world = new CreateWorld();

        var bookId = await world.Service().FulfillAsync(world.Order, CancellationToken.None);

        var created = Assert.Single(world.Packs.Created);
        Assert.Equal(bookId, created.Pack.Id);
        Assert.Equal(world.Order.Id, created.OrderId);
        Assert.Equal(1, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task A_link_the_repository_refuses_leaves_no_book_and_queues_nothing()
    {
        // The transaction lost — another fulfilment of the same order got there first. Nothing
        // was created, nothing is queued, and the failure surfaces so the order stays Paid for
        // whoever did win.
        var world = new CreateWorld();
        world.Packs.RefuseLink = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Service().FulfillAsync(world.Order, CancellationToken.None));

        Assert.Empty(world.Packs.Created);
        Assert.Equal(0, world.Jobs.Enqueued);
        Assert.Empty(world.Runs.Claims);
    }

    // -- the preview run is claimed the moment the paid book exists ----------------

    [Fact]
    public async Task The_preview_run_is_claimed_even_when_adoption_then_fails()
    {
        /*
          The purge-versus-adoption fault. Adoption is best effort and this preview's stored story
          will not parse, so it fails and the book goes on to be written fresh. The claim must
          have happened anyway: it clears the run's expiry, and without it the hourly purge deletes
          the portrait a Beki retry needs.
        */
        var world = new CreateWorld(previewContentJson: "{not json");

        var bookId = await world.Service().FulfillAsync(world.Order, CancellationToken.None);

        var claim = Assert.Single(world.Runs.Claims);
        Assert.Equal(world.RunId, claim.RunId);
        Assert.Equal(world.Order.UserId, claim.UserId);
        Assert.Equal(bookId, claim.PackId);

        // Adoption did fail: the pack was never moved to StoryReady.
        Assert.DoesNotContain(AdventurePackStatus.StoryReady, world.Packs.StatusWrites);
        Assert.Equal(1, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task The_preview_run_is_claimed_before_adoption_reads_it()
    {
        // Order matters: the claim protects the row adoption is about to read. Recorded as a
        // sequence so a refactor that moves the claim back inside adoption fails here.
        var world = new CreateWorld(previewContentJson: ValidPreviewJson());

        await world.Service().FulfillAsync(world.Order, CancellationToken.None);

        Assert.Single(world.Runs.Claims);
        Assert.True(world.Runs.ClaimedBeforeFirstReadAfterCreate,
            "the run must be claimed before adoption reads it");
        Assert.Contains(AdventurePackStatus.StoryReady, world.Packs.StatusWrites);
    }

    [Fact]
    public async Task A_claim_that_fails_does_not_fail_the_paid_order()
    {
        // Best effort, logged. A paid book is not lost to bookkeeping about a preview row.
        var world = new CreateWorld(previewContentJson: ValidPreviewJson());
        world.Runs.ThrowOnClaim = new InvalidOperationException("the run table is locked");

        var bookId = await world.Service().FulfillAsync(world.Order, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, bookId);
        Assert.Equal(1, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task A_draft_with_no_preview_claims_nothing()
    {
        var world = new CreateWorld(previewBookId: false);

        await world.Service().FulfillAsync(world.Order, CancellationToken.None);

        Assert.Empty(world.Runs.Claims);
        Assert.Equal(1, world.Jobs.Enqueued);
    }

    private static string ValidPreviewJson() => JsonSerializer.Serialize(new AdventureContentDto
    {
        Title = "ბექა და ცისარტყელას ხიდი",
        ChildName = "ბექა",
        StoryPages = [new StoryPageDto { Title = "პირველი გვერდი", Content = "ერთხელ..." }]
    }, Web);

    // -- harness: the success page and the webhook -------------------------------

    /// <summary>
    /// A BOG order the bank says is paid, confirmed from the success page or by callback. The
    /// fulfilment behind it is a double that records the token it was given.
    /// </summary>
    private sealed class ConfirmWorld
    {
        public Order Order { get; }
        public PaidOnceOrders Orders { get; }
        public RecordingFulfilment Fulfilment { get; } = new();
        public RecordingNotifier Notifier { get; } = new();

        private const string BogOrderId = "9a4b2f87-3253-48f0-9200-82acd11e7964";

        public ConfirmWorld()
        {
            Order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Type = OrderType.NewBook,
                Package = OrderPackage.Digital,
                TotalMinor = 7900,
                Status = OrderStatus.Pending,
                Provider = OrderProviders.Bog,
                ProviderSessionId = BogOrderId
            };

            Orders = new PaidOnceOrders(Order);
        }

        public byte[] CompletedCallback() => Encoding.UTF8.GetBytes($$"""
            {
              "body": {
                "order_id": "{{BogOrderId}}",
                "external_order_id": "{{Order.Id}}",
                "order_status": { "key": "completed", "value": "Completed" },
                "payment_detail": { "transaction_id": "24080100123456", "code": "100" }
              }
            }
            """);

        public OrderService Service() =>
            new(Orders,
                new QuietPromoCodes(),
                Fulfilment,
                new RefusingPacks(),
                new RefusingCharacters(),
                new RefusingWorldProgress(),
                new RefusingPromoCodeRepository(),
                new SingleUserRepository(),
                Notifier,
                new FakeJobs(),
                new PaidBog(BogOrderId, Order.Id),
                Options.Create(new StripeOptions()),
                // The signature check is the bank's public key; it is its own test.
                Options.Create(new BogOptions { Enabled = true, VerifyCallbackSignature = false }),
                NullLogger<OrderService>.Instance);
    }

    private sealed class RecordingFulfilment : IBookFulfillmentService
    {
        public List<CancellationToken> Tokens { get; } = [];
        public Action? OnCall { get; set; }

        public Task<Guid> FulfillAsync(Order order, CancellationToken cancellationToken)
        {
            Tokens.Add(cancellationToken);
            OnCall?.Invoke();
            return Task.FromResult(Guid.NewGuid());
        }
    }

    /// <summary>One order with the real paid-once and fulfilled-once compare-and-sets.</summary>
    private sealed class PaidOnceOrders(Order seed) : IOrderRepository
    {
        public Order Current => seed;

        public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Order?>(id == seed.Id ? seed : null);

        public Task<Order?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<Order?>(id == seed.Id && userId == seed.UserId ? seed : null);

        public Task<bool> TryMarkPaidAsync(Guid id, string? providerPaymentIntentId, CancellationToken cancellationToken)
        {
            if (id != seed.Id || seed.Status != OrderStatus.Pending)
            {
                return Task.FromResult(false);
            }

            seed.Status = OrderStatus.Paid;
            seed.PaidAt = DateTime.UtcNow;
            seed.ProviderPaymentIntentId = providerPaymentIntentId ?? seed.ProviderPaymentIntentId;
            return Task.FromResult(true);
        }

        public Task<bool> TryMarkFulfilledAsync(Guid id, CancellationToken cancellationToken)
        {
            if (id != seed.Id || seed.Status != OrderStatus.Paid)
            {
                return Task.FromResult(false);
            }

            seed.Status = OrderStatus.Fulfilled;
            seed.FulfilledAt = DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task MarkFailedAsync(Guid id, string reason, CancellationToken cancellationToken)
        {
            seed.FailureReason = reason;
            return Task.CompletedTask;
        }

        public Task<Guid> CreateAsync(Order order, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Order?> GetByProviderSessionIdAsync(string providerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetPaidForBookAsync(Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AttachProviderSessionAsync(Guid id, string providerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetBookIdAsync(Guid id, Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryCancelAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetStalledPaidAsync(DateTime paidBeforeUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PaidBog(string bogOrderId, Guid orderId) : IBogPaymentClient
    {
        public Task<BogPaymentDetails?> GetPaymentDetailsAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult<BogPaymentDetails?>(
                id == bogOrderId ? new BogPaymentDetails(bogOrderId, orderId, "completed", "24080100123456") : null);

        public bool VerifyCallbackSignature(byte[] payload, string? signature) => throw new NotSupportedException();
        public Task<BogCheckout> CreateOrderAsync(BogOrderRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingNotifier : IAdminNotifier
    {
        public int OrderPaidCalls { get; private set; }

        public Task OrderPaidAsync(Order order, CancellationToken cancellationToken)
        {
            OrderPaidCalls++;
            return Task.CompletedTask;
        }

        public Task BookFailedAsync(Guid packId, string reason, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PrintOrderPlacedAsync(PrintOrder printOrder, string? bookTitle, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class QuietPromoCodes : IPromoCodeService
    {
        public Task<bool> TryRedeemAsync(Order order, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<PricedOrder> PriceAsync(Guid userId, OrderType type, OrderPackage package, string? promoCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<QuoteResponse> QuoteAsync(Guid userId, OrderType type, OrderPackage package, string? promoCode, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    // -- harness: the create path ---------------------------------------------

    /// <summary>
    /// A paid order for a new book, with the hero and the preview run it names. Every write the
    /// create path makes is recorded; the order repository refuses to link a book on its own.
    /// </summary>
    private sealed class CreateWorld
    {
        public Guid RunId { get; } = Guid.NewGuid();
        public Order Order { get; }
        public CreatingPacks Packs { get; } = new();
        public RecordingRuns Runs { get; }
        public FakeJobs Jobs { get; } = new();

        private readonly Guid _heroId = Guid.NewGuid();

        public CreateWorld(string? previewContentJson = null, bool previewBookId = true)
        {
            var userId = Guid.NewGuid();

            var draft = new BookDraftRequest
            {
                PrimaryCharacterId = _heroId,
                WorldId = "dinosaurs",
                BookLanguage = "ka",
                PreviewBookId = previewBookId ? RunId : null
            };

            Order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = OrderType.NewBook,
                Package = OrderPackage.Digital,
                Status = OrderStatus.Paid,
                PaidAt = DateTime.UtcNow,
                DraftJson = JsonSerializer.Serialize(draft, Web)
            };

            Runs = new RecordingRuns(new MasterStoryRun
            {
                Id = RunId,
                Status = MasterStoryRunStatus.Ready,
                ChildName = "ბექა",
                ContentJson = previewContentJson
            });
        }

        public BookFulfillmentService Service() =>
            new(new LinkRefusingOrders(),
                Packs,
                new OneHero(_heroId, Order.UserId),
                new QuietWorldProgress(),
                new RefusingPrintOrders(),
                new RefusingBlobs(),
                Runs,
                Jobs,
                // Legacy routing on purpose: the claim is about the run row, not the pipeline, and
                // it must happen on either.
                Options.Create(new BekiOptions { BookFormatEnabled = false }),
                NullLogger<BookFulfillmentService>.Instance);
    }

    /// <summary>Records the atomic create-and-link, and every status the path writes after it.</summary>
    private sealed class CreatingPacks : IAdventurePackRepository
    {
        public List<(AdventurePack Pack, Guid OrderId)> Created { get; } = [];
        public List<AdventurePackStatus> StatusWrites { get; } = [];
        public bool RefuseLink { get; set; }

        public Task<Guid> CreatePendingForOrderAsync(AdventurePack pack, Guid orderId, CancellationToken cancellationToken)
        {
            if (RefuseLink)
            {
                throw new InvalidOperationException($"Order {orderId} already has a book.");
            }

            Created.Add((pack, orderId));
            return Task.FromResult(pack.Id);
        }

        public Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken cancellationToken) => Task.FromResult(1);

        public Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken)
        {
            StatusWrites.Add(status);
            return Task.FromResult(true);
        }

        public Task UpdateBookPresentationAsync(Guid id, string? title, string? coverImageUrl, CancellationToken cancellationToken) => Task.CompletedTask;

        // The two-statement shape this file exists to retire.
        public Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken) =>
            throw new NotSupportedException("the create path must link the order in the same write");

        public Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(Guid characterId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetAccessLevelAsync(Guid id, BookAccessLevel accessLevel, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryUpdateStatusAsync(Guid id, AdventurePackStatus expectedStatus, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(DateTime cutoffUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryFailStaleGenerationAsync(Guid id, AdventurePackStatus expectedStatus, DateTime cutoffUtc, string errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryFailAsync(Guid id, AdventurePackStatus expectedStatus, string errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressAsync(Guid id, string? progressMessage, int? progressPercent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdatePreviewIllustrationAsync(Guid id, PreviewIllustrationStatus status, string? illustrationUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryClaimPreviewIllustrationGenerationAsync(Guid id, int staleAfterMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateGeneratedJsonAsync(Guid id, string generatedJson, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetGenerationPipelineAsync(Guid id, string pipeline, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> ListWithheldBekiPacksAsync(int limit, BekiWithheldCursor? after, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>One stored run; records claims, and whether the claim came before adoption read it.</summary>
    private sealed class RecordingRuns(MasterStoryRun run) : IMasterStoryRunRepository
    {
        public Task SaveAppearanceDescriptionAsync(Guid id, string appearanceDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public List<(Guid RunId, Guid UserId, Guid? PackId)> Claims { get; } = [];
        public Exception? ThrowOnClaim { get; set; }

        private bool _readAfterClaim;
        private bool _readBeforeClaim;

        /// <summary>
        /// True when every read of the run after the pack existed came after the claim. The
        /// pipeline decision reads the run before the pack is created; adoption reads it after.
        /// </summary>
        public bool ClaimedBeforeFirstReadAfterCreate => Claims.Count > 0 && _readAfterClaim && !_readBeforeClaim;

        public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            if (id != run.Id)
            {
                return Task.FromResult<MasterStoryRun?>(null);
            }

            if (Claims.Count > 0)
            {
                _readAfterClaim = true;
            }
            else if (_seenPipelineRead)
            {
                // A second unclaimed read is adoption running before the claim.
                _readBeforeClaim = true;
            }

            _seenPipelineRead = true;
            return Task.FromResult<MasterStoryRun?>(run);
        }

        private bool _seenPipelineRead;

        public Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken)
        {
            if (ThrowOnClaim is { } failure)
            {
                throw failure;
            }

            Claims.Add((id, userId, packId));
            return Task.CompletedTask;
        }

        public Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetProgressAsync(Guid id, string status, string? progressMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SavePromptsAsync(Guid id, string model, string promptVersion, string systemPrompt, string userPrompt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveStoryAsync(Guid id, string storyJson, string contentJson, int promptTokens, int completionTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class OneHero(Guid heroId, Guid ownerId) : ICharacterRepository
    {
        public Task<Character?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<Character?>(id == heroId && userId == ownerId
                ? new Character { Id = heroId, UserId = ownerId, Name = "ბექა", IsPrimary = true }
                : null);

        public Task SetBookCastAsync(Guid bookId, IReadOnlyList<Guid> characterIds, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<Character>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetHeroesAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetHeroPortraitUrlsAsync(Guid userId, IReadOnlyCollection<Guid> characterIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> CreateAsync(Character character, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(Character character, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateAppearanceCacheAsync(Guid id, Guid userId, string? appearanceDescription, string? appearancePhotoUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsCastInAnyBookAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<Guid>> GetCastCharacterIdsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetByBookIdAsync(Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class QuietWorldProgress : IWorldProgressService
    {
        public Task MarkStartedAsync(Guid userId, Guid characterId, string worldId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkCompletedAsync(Guid userId, Guid characterId, string worldId, Guid bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<WorldResponse>> GetCatalogueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventureMapResponse> GetMapAsync(Guid userId, Guid characterId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventureMapResponse>> GetMapsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnsureCanStartAsync(Guid userId, Guid characterId, string worldId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Refuses to link a book on its own: the create path must not call it any more.</summary>
    private sealed class LinkRefusingOrders : IOrderRepository
    {
        public Task SetBookIdAsync(Guid id, Guid bookId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("the order is linked inside the pack's own transaction now");

        public Task<Guid> CreateAsync(Order order, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Order?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Order?> GetByProviderSessionIdAsync(string providerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetPaidForBookAsync(Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AttachProviderSessionAsync(Guid id, string providerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryMarkPaidAsync(Guid id, string? providerPaymentIntentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryMarkFulfilledAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkFailedAsync(Guid id, string reason, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryCancelAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetStalledPaidAsync(DateTime paidBeforeUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    // -- refusing doubles shared by both harnesses ------------------------------

    private sealed class FakeJobs : IBackgroundJobClient
    {
        public int Enqueued { get; private set; }

        public string Create(Job job, IState state)
        {
            Enqueued++;
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }

    private sealed class RefusingPacks : IAdventurePackRepository
    {
        public Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(Guid characterId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetAccessLevelAsync(Guid id, BookAccessLevel accessLevel, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateBookPresentationAsync(Guid id, string? title, string? coverImageUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryUpdateStatusAsync(Guid id, AdventurePackStatus expectedStatus, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(DateTime cutoffUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryFailStaleGenerationAsync(Guid id, AdventurePackStatus expectedStatus, DateTime cutoffUtc, string errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryFailAsync(Guid id, AdventurePackStatus expectedStatus, string errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressAsync(Guid id, string? progressMessage, int? progressPercent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdatePreviewIllustrationAsync(Guid id, PreviewIllustrationStatus status, string? illustrationUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryClaimPreviewIllustrationGenerationAsync(Guid id, int staleAfterMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateGeneratedJsonAsync(Guid id, string generatedJson, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetGenerationPipelineAsync(Guid id, string pipeline, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> ListWithheldBekiPacksAsync(int limit, BekiWithheldCursor? after, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RefusingCharacters : ICharacterRepository
    {
        public Task<IReadOnlyList<Character>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetHeroesAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetHeroPortraitUrlsAsync(Guid userId, IReadOnlyCollection<Guid> characterIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Character?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> CreateAsync(Character character, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(Character character, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateAppearanceCacheAsync(Guid id, Guid userId, string? appearanceDescription, string? appearancePhotoUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsCastInAnyBookAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<Guid>> GetCastCharacterIdsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetByBookIdAsync(Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetBookCastAsync(Guid bookId, IReadOnlyList<Guid> characterIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RefusingWorldProgress : IWorldProgressService
    {
        public Task<IReadOnlyList<WorldResponse>> GetCatalogueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventureMapResponse> GetMapAsync(Guid userId, Guid characterId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventureMapResponse>> GetMapsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnsureCanStartAsync(Guid userId, Guid characterId, string worldId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkStartedAsync(Guid userId, Guid characterId, string worldId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkCompletedAsync(Guid userId, Guid characterId, string worldId, Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RefusingPromoCodeRepository : IPromoCodeRepository
    {
        public Task<IReadOnlyList<PromoCode>> ListAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CreateAsync(PromoCode promoCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateAdminFieldsAsync( Guid id, bool isActive, int? maxRedemptions, DateTime? expiresAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PromoCode?> GetByCodeAsync(string code, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PromoCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasUserRedeemedAsync(Guid promoCodeId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryRedeemAsync(PromoRedemption redemption, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RefusingPrintOrders : IPrintOrderService
    {
        public Task<PrintOrder?> CreateForPaidOrderAsync(Order order, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PrintOrderResponse>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PrintOrderResponse?> GetForUserAsync(Guid userId, Guid printOrderId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PrintOrderResponse> UpdateAddressAsync(Guid userId, Guid printOrderId, ShippingAddressRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AddressResponse>> ListAddressesAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AddressResponse> SaveAddressAsync(Guid userId, SaveAddressRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAddressAsync(Guid userId, Guid addressId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdminPrintQueueResponse> GetAdminQueueAsync(string? status, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdminPrintOrderResponse?> UpdateStatusAsync(Guid printOrderId, UpdatePrintOrderStatusRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RefusingBlobs : IBlobStorageService
    {
        public Task<string> UploadAsync(string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
