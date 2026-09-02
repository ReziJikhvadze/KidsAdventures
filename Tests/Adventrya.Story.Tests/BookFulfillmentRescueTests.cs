using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
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
/// The one sanctioned way a Failed book comes back.
///
/// Both generation jobs now refuse to claim a pack that is Failed, and that refusal is the point:
/// Failed is written by the stale-generation sweep, from outside the process that died, and a job
/// that could claim its way out of it would make the verdict meaningless — a book declared
/// abandoned would be silently redrawn by the next requeue with nothing recording it had been lost.
///
/// Which left a hole exactly where it hurts most. A paid order being re-driven — by the console's
/// retry button or by the stalled-order sweep — re-queues generation for a book that is Pending or
/// Failed. After the guards, the Failed half of that queued a job which loaded the pack, refused
/// it, and returned: the retry looked like it had done something, and the book stayed Failed
/// forever. A parent had paid for it.
///
/// So the rescue says so in the row before it queues anything, and only queues if that transition
/// was really its to make.
/// </summary>
public class BookFulfillmentRescueTests
{
    [Fact]
    public async Task A_failed_book_that_still_has_its_story_is_revived_to_StoryReady()
    {
        /*
          StoryReady rather than Pending, and the distinction is a whole book.

          The legacy job short-circuits to illustrations only on StoryReady *and* a stored story;
          revived to Pending it would write a second story, and the parent would be handed a
          different book from the one they read and bought. The Beki job accepts either, so the
          status that protects the adopted story is the one to pick.
        */
        var world = new RescueWorld(AdventurePackStatus.Failed, generatedJson: "{\"pages\":[]}");

        await world.Service().FulfillAsync(world.Order, CancellationToken.None);

        Assert.Equal(AdventurePackStatus.StoryReady, world.Packs.Status);
        Assert.Equal(1, world.Jobs.Enqueued);

        // The failure reason goes with the failure. An admin was already paged with it; left on a
        // row that is being redrawn it is only an error the parent can see on a book being made.
        Assert.Null(world.Packs.ErrorMessage);

        // The story itself is not disturbed on the way through.
        Assert.Equal("{\"pages\":[]}", world.Packs.GeneratedJson);
    }

    [Fact]
    public async Task A_failed_book_with_no_story_is_revived_to_Pending()
    {
        // Nothing to protect, and the legacy job needs to write one — which is what Pending asks
        // it to do.
        var world = new RescueWorld(AdventurePackStatus.Failed, generatedJson: null);

        await world.Service().FulfillAsync(world.Order, CancellationToken.None);

        Assert.Equal(AdventurePackStatus.Pending, world.Packs.Status);
        Assert.Equal(1, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task A_revived_book_is_left_in_a_status_the_generation_job_will_claim()
    {
        // The whole bug in one assertion: whatever the rescue writes has to be something
        // ProcessAsync and the legacy job are willing to pick up, or the queued job loads the
        // pack, refuses it, and the retry achieves nothing.
        foreach (var story in new[] { null, "{\"pages\":[]}" })
        {
            var world = new RescueWorld(AdventurePackStatus.Failed, story);

            await world.Service().FulfillAsync(world.Order, CancellationToken.None);

            Assert.Contains(
                world.Packs.Status,
                new[]
                {
                    AdventurePackStatus.Pending,
                    AdventurePackStatus.StoryReady,
                    AdventurePackStatus.GeneratingStory,
                    AdventurePackStatus.GeneratingPdf
                });
        }
    }

    [Fact]
    public async Task A_pending_book_is_requeued_exactly_as_before()
    {
        // Pending is already claimable, so the rescue has nothing to say about it. This pins that
        // the new branch did not quietly start rewriting statuses that were fine.
        var world = new RescueWorld(AdventurePackStatus.Pending, generatedJson: null);

        await world.Service().FulfillAsync(world.Order, CancellationToken.None);

        Assert.Equal(AdventurePackStatus.Pending, world.Packs.Status);
        Assert.Equal(0, world.Packs.Transitions);
        Assert.Equal(1, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task A_book_that_stopped_being_failed_first_is_not_requeued()
    {
        /*
          Two rescues racing, or a rescue racing the book's own recovery.

          The compare-and-set is what makes the second one harmless: it finds the row already moved
          on, and — the part that matters — it does not queue a job anyway. A blind enqueue here
          would put a second worker behind a book that is already being drawn.
        */
        var world = new RescueWorld(AdventurePackStatus.Failed, generatedJson: null);
        world.Packs.OnBeforeTransition = () => world.Packs.Force(AdventurePackStatus.GeneratingStory);

        await world.Service().FulfillAsync(world.Order, CancellationToken.None);

        Assert.Equal(AdventurePackStatus.GeneratingStory, world.Packs.Status);
        Assert.Equal(0, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task A_revived_book_goes_back_to_failed_if_the_queue_will_not_take_it()
    {
        /*
          Otherwise the rescue is worse than the failure it was fixing.

          Only Pending and Failed are re-driven at all, so a book left revived to StoryReady with no
          job behind it would fall outside the set any later retry looks at — stuck, with nothing
          running and nothing that would ever notice. Failed is the status that keeps it rescuable,
          so a queue that refuses the job puts it back.
        */
        var world = new RescueWorld(AdventurePackStatus.Failed, generatedJson: "{\"pages\":[]}");
        world.Jobs.ThrowOnEnqueue = new InvalidOperationException("the queue is down");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Service().FulfillAsync(world.Order, CancellationToken.None));

        Assert.Equal(AdventurePackStatus.Failed, world.Packs.Status);
        Assert.Contains("could not queue", world.Packs.ErrorMessage);
    }

    [Fact]
    public async Task A_book_that_is_being_drawn_is_not_touched_at_all()
    {
        // The re-drive is idempotent by status, and always was: a book already in flight gets its
        // unlock re-applied and nothing else.
        var world = new RescueWorld(AdventurePackStatus.GeneratingStory, generatedJson: null);

        await world.Service().FulfillAsync(world.Order, CancellationToken.None);

        Assert.Equal(AdventurePackStatus.GeneratingStory, world.Packs.Status);
        Assert.Equal(0, world.Packs.Transitions);
        Assert.Equal(0, world.Jobs.Enqueued);
    }

    // -- a Beki book with no job ----------------------------------------------

    [Fact]
    public async Task A_beki_book_left_at_story_ready_with_no_job_is_requeued()
    {
        /*
          The second state a paid book can be stranded in, and until now the invisible one.

          Adoption writes StoryReady before generation is enqueued, and the cast, the map and the
          enqueue itself can all still throw. The re-drive looked only at Pending and Failed, so a
          Beki book left here was skipped: nothing queued, the order stamped Fulfilled, and a
          parent polling "შეკვეთა მიღებულია…" for good. The Beki job claims StoryReady, so queuing
          it is the first attempt's missing last step — and the row itself is not touched.
        */
        var world = new RescueWorld(AdventurePackStatus.StoryReady, "{\"pages\":[]}", GenerationPipelines.Beki);

        await world.Service().FulfillAsync(world.Order, CancellationToken.None);

        Assert.Equal(1, world.Jobs.Enqueued);
        Assert.Equal(AdventurePackStatus.StoryReady, world.Packs.Status);
        Assert.Equal(0, world.Packs.Transitions);
        Assert.Equal("{\"pages\":[]}", world.Packs.GeneratedJson);
    }

    [Fact]
    public async Task A_legacy_book_at_story_ready_is_a_finished_book_and_is_left_alone()
    {
        // On the legacy pipeline the words and the pictures arrive together, so StoryReady is the
        // book the parent already reads. Re-driving it would hand them a different one.
        var world = new RescueWorld(AdventurePackStatus.StoryReady, "{\"pages\":[]}", GenerationPipelines.Legacy);

        await world.Service().FulfillAsync(world.Order, CancellationToken.None);

        Assert.Equal(0, world.Jobs.Enqueued);
        Assert.Equal(0, world.Packs.Transitions);
        Assert.Equal(AdventurePackStatus.StoryReady, world.Packs.Status);
    }

    [Fact]
    public async Task A_beki_book_whose_queue_refuses_stays_where_the_next_retry_will_find_it()
    {
        // Nothing to put back: StoryReady on the Beki pipeline is now inside the set a re-drive
        // looks at, so the row is left exactly as it was and the failure surfaces to the caller —
        // which leaves the order Paid, for the sweep.
        var world = new RescueWorld(AdventurePackStatus.StoryReady, "{\"pages\":[]}", GenerationPipelines.Beki);
        world.Jobs.ThrowOnEnqueue = new InvalidOperationException("the queue is down");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Service().FulfillAsync(world.Order, CancellationToken.None));

        Assert.Equal(AdventurePackStatus.StoryReady, world.Packs.Status);
        Assert.Equal(0, world.Packs.Transitions);
    }

    // -- harness ---------------------------------------------------------

    /// <summary>
    /// A paid order whose book already exists — the shape every retry route arrives in. The draft
    /// is deliberately absent: reading it is wrapped in its own try, so the rescue falls to the
    /// legacy pipeline with a warning, which keeps this harness to the two collaborators the
    /// behaviour under test actually uses.
    /// </summary>
    private sealed class RescueWorld
    {
        public Guid BookId { get; } = Guid.NewGuid();
        public Order Order { get; }
        public FakePacks Packs { get; }
        public FakeJobs Jobs { get; } = new();

        public RescueWorld(
            AdventurePackStatus status,
            string? generatedJson,
            string pipeline = GenerationPipelines.Legacy)
        {
            var userId = Guid.NewGuid();

            Packs = new FakePacks(new AdventurePack
            {
                Id = BookId,
                UserId = userId,
                Status = status,
                GeneratedJson = generatedJson,
                GenerationPipeline = pipeline,
                ErrorMessage = status == AdventurePackStatus.Failed
                    ? "GENERATION_STALLED: nothing has been written to this book for 45 minutes."
                    : null,
                AccessLevel = BookAccessLevel.Full,
                CreatedAt = DateTime.UtcNow
            });

            Order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BookId = BookId,
                Type = OrderType.NewBook,
                Package = OrderPackage.Digital,
                Status = OrderStatus.Paid
            };
        }

        public BookFulfillmentService Service() =>
            new(new ThrowingOrders(),
                Packs,
                new ThrowingCharacters(),
                new ThrowingWorldProgress(),
                new ThrowingPrintOrders(),
                new ThrowingBlobs(),
                new ThrowingRuns(),
                Jobs,
                Options.Create(new BekiOptions()),
                NullLogger<BookFulfillmentService>.Instance);
    }

    private sealed class FakeJobs : IBackgroundJobClient
    {
        public int Enqueued { get; private set; }
        public Exception? ThrowOnEnqueue { get; set; }

        public string Create(Job job, IState state)
        {
            if (ThrowOnEnqueue is { } failure)
            {
                throw failure;
            }

            Enqueued++;
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }

    /// <summary>
    /// One row, with a real compare-and-set. Only the four calls this path makes do anything; the
    /// rest are refused so a change of behaviour shows up as a failure rather than as silence.
    /// </summary>
    private sealed class FakePacks(AdventurePack seed) : IAdventurePackRepository
    {
        private readonly AdventurePack _pack = seed;

        public AdventurePackStatus Status => _pack.Status;
        public string? ErrorMessage => _pack.ErrorMessage;
        public string? GeneratedJson => _pack.GeneratedJson;

        /// <summary>How many status transitions were attempted and won.</summary>
        public int Transitions { get; private set; }

        /// <summary>Simulates another writer moving the row between the read and the write.</summary>
        public Action? OnBeforeTransition { get; set; }

        public void Force(AdventurePackStatus status) => _pack.Status = status;

        public Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<AdventurePack?>(_pack);

        public Task<bool> TryUpdateStatusAsync(
            Guid id, AdventurePackStatus expectedStatus, AdventurePackStatus status,
            string? generatedJson, string? pdfUrl, string? errorMessage,
            CancellationToken cancellationToken)
        {
            OnBeforeTransition?.Invoke();

            if (_pack.Status != expectedStatus)
            {
                return Task.FromResult(false);
            }

            _pack.Status = status;
            _pack.GeneratedJson = generatedJson;
            _pack.PdfUrl = pdfUrl;
            _pack.ErrorMessage = errorMessage;
            Transitions++;
            return Task.FromResult(true);
        }

        public Task<bool> SetAccessLevelAsync(Guid id, BookAccessLevel accessLevel, CancellationToken cancellationToken)
        {
            _pack.AccessLevel = accessLevel;
            return Task.FromResult(true);
        }

        public Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken cancellationToken)
        {
            _pack.HasPrintEntitlement = true;
            return Task.FromResult(true);
        }

        public Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(Guid characterId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateBookPresentationAsync(Guid id, string? title, string? coverImageUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
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

        // B5's discriminator and B7's withheld sweep. Neither is this double's subject: the pipeline
        // stamp is recorded so a test can read it back, and no test here asks for withheld books.
        public string? StampedPipeline { get; private set; }

        public Task SetGenerationPipelineAsync(Guid id, string pipeline, CancellationToken cancellationToken)
        {
            StampedPipeline = pipeline;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AdventurePack>> ListWithheldBekiPacksAsync(int limit, BekiWithheldCursor? after, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdventurePack>>([]);
    }

    // The collaborators this path never reaches. Refusing rather than returning nothing, so a
    // change that starts calling one of them fails here instead of passing quietly.

    private sealed class ThrowingOrders : IOrderRepository
    {
        public Task<Guid> CreateAsync(Order order, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Order?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Order?> GetByProviderSessionIdAsync(string providerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetPaidForBookAsync(Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AttachProviderSessionAsync(Guid id, string providerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetBookIdAsync(Guid id, Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryMarkPaidAsync(Guid id, string? providerPaymentIntentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryMarkFulfilledAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkFailedAsync(Guid id, string reason, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryCancelAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetStalledPaidAsync(DateTime paidBeforeUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingCharacters : ICharacterRepository
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

    private sealed class ThrowingWorldProgress : IWorldProgressService
    {
        public Task<IReadOnlyList<WorldResponse>> GetCatalogueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventureMapResponse> GetMapAsync(Guid userId, Guid characterId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventureMapResponse>> GetMapsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnsureCanStartAsync(Guid userId, Guid characterId, string worldId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkStartedAsync(Guid userId, Guid characterId, string worldId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkCompletedAsync(Guid userId, Guid characterId, string worldId, Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingPrintOrders : IPrintOrderService
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

    private sealed class ThrowingBlobs : IBlobStorageService
    {
        public Task<string> UploadAsync(string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingRuns : IMasterStoryRunRepository
    {
        public Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetProgressAsync(Guid id, string status, string? progressMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SavePromptsAsync(Guid id, string model, string promptVersion, string systemPrompt, string userPrompt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveStoryAsync(Guid id, string storyJson, string contentJson, int promptTokens, int completionTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAppearanceDescriptionAsync(Guid id, string appearanceDescription, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
