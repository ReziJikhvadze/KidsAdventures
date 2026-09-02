using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// What a waived refusal actually leaves behind, from the one place that can write a blob.
///
/// The fulfilment job is the MAIN source of alarms in this system — a waived spread, a waived gate,
/// a lost completion — and the console it feeds has exactly two affordances per row: a link to the
/// order and a button that downloads that order's handback so a person can look at the evidence.
/// Both of those hang off things this method writes, and both had been quietly broken: every alarm
/// carried a null order id, and two waived checks on one spread wrote their pictures to the same
/// blob name.
///
/// Exercised at the method rather than through <see cref="BekiPackFulfillment.ProcessAsync"/> on
/// purpose. Reaching it that way means driving a whole book — nine image calls' worth of doubles,
/// a composer, a print preparer — to observe two uploads and one row, and a test at that distance
/// would not have caught either fault: an overwritten blob and a null column both look exactly like
/// success from the outside.
/// </summary>
public class BekiAlarmEvidenceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PackId = Guid.NewGuid();
    private static readonly Guid OrderId = Guid.NewGuid();

    /// <summary>
    /// Two waived checks on one spread leave two pictures — review finding 5.
    ///
    /// The centre-fold measurement and the reviewer's opinion are a pair that genuinely co-occurs:
    /// a fold the measurement dislikes is often a page the reviewer dislikes too, and under the
    /// owner's ruling both ship. Both uploads went to <c>spread-NN-failed.png</c>, which knows the
    /// page and not the check, so the second replaced the first — leaving two alarms, one picture,
    /// and no way to tell which alarm the surviving picture was about. The operator opening the
    /// centre-fold alarm was shown the image_qa evidence and had nothing saying so.
    /// </summary>
    [Fact]
    public async Task Two_waived_checks_on_one_spread_each_keep_their_own_picture()
    {
        var blobs = new PolicyFakeBlobs();
        var alarms = new RecordingAlarms();
        var job = Job(blobs, alarms);

        await job.RecordWaiverAsync(
            Pack(), Waiver(BekiReleaseChecks.CentreFold, 3, [1, 1, 1]), CancellationToken.None);
        await job.RecordWaiverAsync(
            Pack(), Waiver(BekiReleaseChecks.ImageQa, 3, [2, 2, 2]), CancellationToken.None);

        var fold = BekiPackBlobs.WaivedEvidenceName(
            UserId, PackId, BekiReleaseChecks.CentreFold, 3);
        var qa = BekiPackBlobs.WaivedEvidenceName(UserId, PackId, BekiReleaseChecks.ImageQa, 3);

        Assert.NotEqual(fold, qa);
        Assert.Equal(new byte[] { 1, 1, 1 }, blobs.Get(fold));
        Assert.Equal(new byte[] { 2, 2, 2 }, blobs.Get(qa));

        // And the terminal-failure name is left to the terminal-failure path. A book that SHIPPED
        // has not failed a spread, and a picture filed under "failed" would say it had.
        Assert.False(blobs.Has(BekiPackBlobs.FailedSpreadName(UserId, PackId, 3)));

        // Two incidents, two rows, each pointing at its own paperwork.
        Assert.Equal(2, alarms.Raised.Count);
        Assert.Equal(
            BekiPackBlobs.PolicyWaiverName(UserId, PackId, BekiReleaseChecks.CentreFold, 3),
            alarms.Raised[0].EvidenceBlob);
        Assert.Equal(
            BekiPackBlobs.PolicyWaiverName(UserId, PackId, BekiReleaseChecks.ImageQa, 3),
            alarms.Raised[1].EvidenceBlob);
    }

    /// <summary>
    /// A waiver alarm names the order that paid for the book — review finding 4.
    ///
    /// Every alarm this job raised passed null for the order id and nothing ever backfilled it, so
    /// the console's order link read "—" and its evidence button — which downloads the ORDER's
    /// handback zip — was not rendered at all. That was true of the single largest source of alarms
    /// in the system: an operator's worklist of waived spreads was a list of things they could read
    /// the one-line detail of and nothing else.
    /// </summary>
    [Fact]
    public async Task A_waiver_alarm_names_the_order_that_paid_for_the_book()
    {
        var alarms = new RecordingAlarms();
        var orders = new PaidOrders(OrderId);

        await Job(new PolicyFakeBlobs(), alarms, orders).RecordWaiverAsync(
            Pack(), Waiver(BekiReleaseChecks.ImageQa, 5, [7]), CancellationToken.None);

        Assert.Equal(OrderId, Assert.Single(alarms.Raised).OrderId);
    }

    /// <summary>
    /// One lookup per job, however many pages the policy waives.
    ///
    /// A book can waive a check on every spread and the cover, and the answer cannot change while
    /// the job runs. A round trip apiece would be nine queries for one fact — paid on the path that
    /// exists precisely because the book is already in trouble.
    /// </summary>
    [Fact]
    public async Task The_order_is_looked_up_once_however_many_pages_are_waived()
    {
        var orders = new PaidOrders(OrderId);
        var job = Job(new PolicyFakeBlobs(), new RecordingAlarms(), orders);

        for (var spread = 1; spread <= 8; spread++)
        {
            await job.RecordWaiverAsync(
                Pack(), Waiver(BekiReleaseChecks.CentreFold, spread, [1]), CancellationToken.None);
        }

        Assert.Equal(1, orders.Lookups);
    }

    /// <summary>
    /// A book with no paid order still gets its alarm, and a lookup that throws still gets its alarm.
    ///
    /// Both states are real — a re-drive, a staging run, a database that blinked — and in every one
    /// of them the incident is the thing worth keeping. An alarm that failed to record because its
    /// order link could not be resolved would be this campaign's own fault class arriving through
    /// the door marked "recording that we removed it".
    /// </summary>
    [Fact]
    public async Task An_unresolvable_order_costs_the_alarm_its_link_and_not_its_existence()
    {
        var withoutOrder = new RecordingAlarms();
        await Job(new PolicyFakeBlobs(), withoutOrder, new PaidOrders(null)).RecordWaiverAsync(
            Pack(), Waiver(BekiReleaseChecks.QaUnreadable, 2, [1]), CancellationToken.None);

        Assert.Null(Assert.Single(withoutOrder.Raised).OrderId);

        var unreachable = new RecordingAlarms();
        await Job(new PolicyFakeBlobs(), unreachable, new PaidOrders(null) { Throw = true })
            .RecordWaiverAsync(
                Pack(), Waiver(BekiReleaseChecks.QaUnreadable, 2, [1]), CancellationToken.None);

        Assert.Null(Assert.Single(unreachable.Raised).OrderId);
    }

    // ==============================================================================================
    // Harness
    // ==============================================================================================

    /// <summary>
    /// The fulfilment job with only the collaborators this method touches.
    ///
    /// The rest are null, deliberately and visibly: <see cref="BekiPackFulfillment.RecordWaiverAsync"/>
    /// reaches storage, the alarms and the orders table and nothing else, and a wall of throwing
    /// doubles for a generator, a composer and a run repository would say the opposite — that this
    /// path might touch them. If it ever does, the test fails with a NullReferenceException, which
    /// is the right way for that to be discovered.
    /// </summary>
    private static BekiPackFulfillment Job(
        PolicyFakeBlobs blobs, IBekiAlarmService alarms, IOrderRepository? orders = null) =>
        new(packRepository: null!,
            masterStoryRunRepository: null!,
            blobStorage: blobs,
            generator: null!,
            composer: null!,
            adminNotifier: null!,
            emailService: null!,
            userRepository: null!,
            bekiOptions: Options.Create(new BekiOptions()),
            logger: NullLogger<BekiPackFulfillment>.Instance,
            alarms: alarms,
            orders: orders);

    private static AdventurePack Pack() => new() { Id = PackId, UserId = UserId };

    private static CompositePolicyWaiver Waiver(string checkId, int page, byte[] png) =>
        new(checkId, page, $"{checkId} refused page {page}", png, $$"""{"check":"{{checkId}}"}""");

    private sealed class RecordingAlarms : IBekiAlarmService
    {
        public List<BekiAlarmRaise> Raised { get; } = [];

        public Task RaiseAsync(BekiAlarmRaise raise, CancellationToken ct)
        {
            Raised.Add(raise);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>([]);

        public Task<IReadOnlyList<BekiAlarm>> ListRecentAsync(int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>([]);

        public Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>([]);

        public Task<BekiAlarm?> GetAsync(Guid alarmId, CancellationToken ct) =>
            Task.FromResult<BekiAlarm?>(null);

        public Task<bool> ReviewAsync(
            Guid alarmId, string reviewedBy, string resolution, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<int> CountOpenAsync(CancellationToken ct) => Task.FromResult(Raised.Count);
    }

    /// <summary>
    /// The orders table, answering the one question the fulfilment job asks it and counting how
    /// often it was asked.
    /// </summary>
    private sealed class PaidOrders(Guid? orderId) : IOrderRepository
    {
        public int Lookups { get; private set; }

        public bool Throw { get; init; }

        public Task<IReadOnlyList<Order>> GetPaidForBookAsync(Guid bookId, CancellationToken ct)
        {
            Lookups++;

            if (Throw)
            {
                throw new InvalidOperationException("the orders table is unreachable");
            }

            return Task.FromResult<IReadOnlyList<Order>>(
                orderId is { } id
                    ? [new Order { Id = id, BookId = bookId, Status = OrderStatus.Paid }]
                    : []);
        }

        public Task<Guid> CreateAsync(Order order, CancellationToken ct) => throw new NotSupportedException();
        public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Order?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Order?> GetByProviderSessionIdAsync(string providerSessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetByUserIdAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task AttachProviderSessionAsync(Guid id, string providerSessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task SetBookIdAsync(Guid id, Guid bookId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryMarkPaidAsync(Guid id, string? providerPaymentIntentId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryMarkFulfilledAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkFailedAsync(Guid id, string reason, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryCancelAsync(Guid id, Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetStalledPaidAsync(DateTime paidBeforeUtc, int limit, CancellationToken ct) => throw new NotSupportedException();
    }
}
