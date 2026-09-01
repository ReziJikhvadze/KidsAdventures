using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The alarms, which are the other half of the owner's ruling.
///
/// "Problems become admin alarms to check later, not blocks" is one sentence with two obligations.
/// The first — that the book ships — belongs to the pipeline and the gates. The second is this one:
/// that nothing is waived quietly. Every one of these tests is about a way that promise could be
/// broken while appearing to be kept — an alarm that duplicates until nobody reads the list, one
/// that stays closed after the fault recurs, one that takes a book down with it when the table is
/// unreachable, or a blocker that pages nobody.
/// </summary>
public class BekiAlarmTests
{
    private static readonly Guid PackId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    /// <summary>
    /// A first sighting is a row and a page; the same incident again moves the timestamp and pages
    /// nobody. An operator who is told four times about one waived spread stops reading the address.
    /// </summary>
    [Fact]
    public async Task A_repeated_incident_is_one_row_and_one_page()
    {
        var repository = new FakeAlarmRepository();
        var notifier = new CountingAdminNotifier();
        var alarms = new BekiAlarmService(
            repository, notifier, NullLogger<BekiAlarmService>.Instance);

        await alarms.RaiseAsync(Raise(BekiReleaseSeverity.Blocker), CancellationToken.None);
        await alarms.RaiseAsync(Raise(BekiReleaseSeverity.Blocker), CancellationToken.None);
        await alarms.RaiseAsync(Raise(BekiReleaseSeverity.Blocker), CancellationToken.None);

        var row = Assert.Single(repository.Rows);
        Assert.Equal(3, row.Raisings);
        Assert.Equal(1, notifier.Pages);
    }

    /// <summary>
    /// The identity is <c>(PackId, CheckId, EvidenceKey)</c>, so the same check on two pages is two
    /// incidents — which is what makes the list a worklist rather than a counter.
    /// </summary>
    [Fact]
    public async Task The_same_check_on_two_pages_is_two_alarms()
    {
        var repository = new FakeAlarmRepository();
        var alarms = new BekiAlarmService(
            repository, new CountingAdminNotifier(), NullLogger<BekiAlarmService>.Instance);

        await alarms.RaiseAsync(
            Raise(BekiReleaseSeverity.Flag, evidenceKey: BekiAlarmEvidence.ForAttempt("image_qa", 3)),
            CancellationToken.None);
        await alarms.RaiseAsync(
            Raise(BekiReleaseSeverity.Flag, evidenceKey: BekiAlarmEvidence.ForAttempt("image_qa", 7)),
            CancellationToken.None);

        Assert.Equal(2, repository.Rows.Count);
    }

    /// <summary>
    /// A reviewed alarm that happens again REOPENS — amendment B4. Leaving it closed would hide a
    /// recurrence behind somebody's earlier decision that a one-off was acceptable, and the reviewer
    /// stays on the row so the history is still readable.
    /// </summary>
    [Fact]
    public async Task A_reviewed_alarm_that_recurs_reopens_and_keeps_who_looked()
    {
        var repository = new FakeAlarmRepository();
        var notifier = new CountingAdminNotifier();
        var alarms = new BekiAlarmService(
            repository, notifier, NullLogger<BekiAlarmService>.Instance);

        await alarms.RaiseAsync(Raise(BekiReleaseSeverity.Blocker), CancellationToken.None);

        var open = Assert.Single(await alarms.ListOpenAsync(50, CancellationToken.None));
        Assert.True(await alarms.ReviewAsync(
            open.Id, "misho@example.test", BekiAlarmResolutions.WontFix, CancellationToken.None));
        Assert.Equal(0, await alarms.CountOpenAsync(CancellationToken.None));

        await alarms.RaiseAsync(Raise(BekiReleaseSeverity.Blocker), CancellationToken.None);

        var reopened = Assert.Single(await alarms.ListOpenAsync(50, CancellationToken.None));
        Assert.Null(reopened.Resolution);
        Assert.Null(reopened.ReviewedAtUtc);
        Assert.Equal("misho@example.test", reopened.ReviewedBy);
        Assert.True(reopened.IsOpen);

        // A recurrence after a review is news, so it pages again — unlike a repeat of an open one.
        Assert.Equal(2, notifier.Pages);
    }

    /// <summary>Flags stay in the console: they are the normal state of a healthy system here.</summary>
    [Fact]
    public async Task A_flag_is_recorded_and_pages_nobody()
    {
        var repository = new FakeAlarmRepository();
        var notifier = new CountingAdminNotifier();
        var alarms = new BekiAlarmService(
            repository, notifier, NullLogger<BekiAlarmService>.Instance);

        await alarms.RaiseAsync(Raise(BekiReleaseSeverity.Flag), CancellationToken.None);

        Assert.Single(repository.Rows);
        Assert.Equal(0, notifier.Pages);
    }

    /// <summary>
    /// An alarms table that will not write must never take a book down with it.
    ///
    /// Every caller of RaiseAsync is mid-flight with good artwork in hand. A book that failed because
    /// the recording of a waiver failed would be this campaign's own fault class, arriving through
    /// the door marked "recording that we removed it".
    /// </summary>
    [Fact]
    public async Task A_failing_alarm_store_never_reaches_the_caller()
    {
        var alarms = new BekiAlarmService(
            new FakeAlarmRepository { Throw = true }, new CountingAdminNotifier(),
            NullLogger<BekiAlarmService>.Instance);

        var thrown = await Record.ExceptionAsync(() =>
            alarms.RaiseAsync(Raise(BekiReleaseSeverity.Blocker), CancellationToken.None));

        Assert.Null(thrown);
    }

    /// <summary>
    /// A resolution the database would refuse is normalised on the way in rather than thrown at a
    /// console click. The weakest true statement — somebody looked — is the right default.
    /// </summary>
    [Fact]
    public async Task An_unrecognised_resolution_becomes_acknowledged()
    {
        var repository = new FakeAlarmRepository();
        var alarms = new BekiAlarmService(
            repository, new CountingAdminNotifier(), NullLogger<BekiAlarmService>.Instance);

        await alarms.RaiseAsync(Raise(BekiReleaseSeverity.Flag), CancellationToken.None);
        var open = Assert.Single(await alarms.ListOpenAsync(50, CancellationToken.None));

        await alarms.ReviewAsync(open.Id, "misho", "looked at it I guess", CancellationToken.None);

        Assert.Equal(
            BekiAlarmResolutions.Acknowledged,
            Assert.Single(repository.Rows).Alarm.Resolution);
    }

    /// <summary>
    /// The evidence key is derived from the blob's NAME, not its bytes.
    ///
    /// A refused spread's pixels change on every attempt, so hashing them would make each retry a
    /// new incident — exactly the duplication the key exists to prevent. The name is stable for the
    /// incident and different for every page and every book.
    /// </summary>
    [Fact]
    public void An_evidence_key_is_stable_for_an_incident_and_distinct_between_pages()
    {
        var three = BekiPackBlobs.FailedSpreadName(UserId, PackId, 3);
        var seven = BekiPackBlobs.FailedSpreadName(UserId, PackId, 7);

        Assert.Equal(BekiAlarmEvidence.ForBlob(three), BekiAlarmEvidence.ForBlob(three));
        Assert.NotEqual(BekiAlarmEvidence.ForBlob(three), BekiAlarmEvidence.ForBlob(seven));

        // And it fits the column: NVARCHAR(128), so a key that grew with the blob path would start
        // truncating and start colliding.
        Assert.True(BekiAlarmEvidence.ForBlob(three).Length <= 128);
        Assert.True(
            BekiAlarmEvidence.ForAttempt("image_qa", 3, "a-very-long-discriminator").Length <= 128);
    }

    /// <summary>One book's alarms, reviewed ones included: the order page shows the history.</summary>
    [Fact]
    public async Task A_packs_alarms_include_the_ones_already_reviewed()
    {
        var repository = new FakeAlarmRepository();
        var alarms = new BekiAlarmService(
            repository, new CountingAdminNotifier(), NullLogger<BekiAlarmService>.Instance);

        await alarms.RaiseAsync(
            Raise(BekiReleaseSeverity.Flag, evidenceKey: "a"), CancellationToken.None);
        await alarms.RaiseAsync(
            Raise(BekiReleaseSeverity.Flag, evidenceKey: "b"), CancellationToken.None);

        var first = (await alarms.ListOpenAsync(50, CancellationToken.None))[0];
        await alarms.ReviewAsync(
            first.Id, "misho", BekiAlarmResolutions.Fixed, CancellationToken.None);

        Assert.Equal(2, (await alarms.ListForPackAsync(PackId, CancellationToken.None)).Count);
        Assert.Single(await alarms.ListOpenAsync(50, CancellationToken.None));
    }

    /// <summary>
    /// A later sighting fills in an order id the first one did not have — review finding 4's
    /// repair path, and the reason the statement COALESCEs in that direction.
    ///
    /// Rows raised before the fulfilment job resolved the order carry a null there, and the console
    /// keys both of its affordances — the order link and the evidence download — off that column.
    /// A re-raise is the cheapest chance this system gets to fix them, and it must never do the
    /// opposite: an incident re-raised from a path that does not know the order (the withheld sweep,
    /// which walks packs rather than orders) must not blank out a link that is already there.
    /// </summary>
    [Fact]
    public async Task A_later_sighting_fills_in_a_missing_order_and_never_clears_one()
    {
        var orderId = Guid.NewGuid();
        var repository = new FakeAlarmRepository();
        var alarms = new BekiAlarmService(
            repository, new CountingAdminNotifier(), NullLogger<BekiAlarmService>.Instance);

        // As the pipeline raised it before anything looked the order up.
        await alarms.RaiseAsync(Raise(BekiReleaseSeverity.Flag), CancellationToken.None);
        Assert.Null(Assert.Single(repository.Rows).Alarm.OrderId);

        // The same incident, seen again by something that knows which order paid for the book.
        await alarms.RaiseAsync(
            Raise(BekiReleaseSeverity.Flag, orderId: orderId), CancellationToken.None);
        Assert.Equal(orderId, Assert.Single(repository.Rows).Alarm.OrderId);

        // And again from a path that does not know it. The link stays.
        await alarms.RaiseAsync(Raise(BekiReleaseSeverity.Flag), CancellationToken.None);
        Assert.Equal(orderId, Assert.Single(repository.Rows).Alarm.OrderId);
    }

    private static BekiAlarmRaise Raise(
        string severity, string? evidenceKey = null, Guid? orderId = null) => new(
        PackId,
        orderId,
        UserId,
        BekiReleaseChecks.ImageQa,
        severity,
        "the reviewer refused spread 3 and the policy flags this check",
        BekiPackBlobs.FailedSpreadName(UserId, PackId, 3),
        evidenceKey ?? BekiAlarmEvidence.ForAttempt(BekiReleaseChecks.ImageQa, 3));

    // ==============================================================================================
    // Doubles
    // ==============================================================================================

    /// <summary>
    /// The alarms table's semantics in memory: the unique key, the re-raise, the reopen.
    ///
    /// The SQL is where these actually live, and this double exists to hold the service to the same
    /// contract the statement implements — so that a change to either without the other shows up as
    /// a test that no longer describes the system.
    /// </summary>
    private sealed class FakeAlarmRepository : IBekiAlarmRepository
    {
        public List<StoredAlarm> Rows { get; } = [];

        public bool Throw { get; set; }

        public Task<BekiAlarmRaiseOutcome> RaiseAsync(
            BekiAlarmRaise raise, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("the alarms table is unreachable");
            }

            var existing = Rows.FirstOrDefault(row =>
                row.Alarm.PackId == raise.PackId
                && row.Alarm.CheckId == raise.CheckId
                && row.EvidenceKey == raise.EvidenceKey);

            if (existing is null)
            {
                Rows.Add(new StoredAlarm(
                    new BekiAlarm(
                        Guid.NewGuid(), raise.PackId, raise.OrderId, raise.UserId, raise.CheckId,
                        raise.Severity, raise.Detail, raise.EvidenceBlob,
                        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null),
                    raise.EvidenceKey));

                return Task.FromResult(BekiAlarmRaiseOutcome.Inserted);
            }

            var wasReviewed = existing.Alarm.ReviewedAtUtc is not null;

            existing.Alarm = existing.Alarm with
            {
                LastSeenUtc = DateTimeOffset.UtcNow,
                Severity = raise.Severity,
                Detail = raise.Detail,
                // A known order fills an empty column and never clears a full one — the statement's
                // COALESCE(OrderId, @OrderId), which is what repairs the rows raised before the
                // fulfilment job looked the order up. (Review finding 4.)
                OrderId = existing.Alarm.OrderId ?? raise.OrderId,
                // The reviewer stays; the closure does not.
                ReviewedAtUtc = null,
                Resolution = null,
            };
            existing.Raisings++;

            return Task.FromResult(
                wasReviewed ? BekiAlarmRaiseOutcome.Reopened : BekiAlarmRaiseOutcome.Touched);
        }

        public Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>(
                Rows.Where(row => row.Alarm.ReviewedAtUtc is null)
                    .Select(row => row.Alarm)
                    .Take(limit)
                    .ToList());

        public Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>(
                Rows.Where(row => row.Alarm.PackId == packId).Select(row => row.Alarm).ToList());

        public Task<bool> ReviewAsync(
            Guid alarmId, string reviewedBy, string resolution, CancellationToken cancellationToken)
        {
            var row = Rows.FirstOrDefault(
                candidate => candidate.Alarm.Id == alarmId && candidate.Alarm.ReviewedAtUtc is null);

            if (row is null)
            {
                return Task.FromResult(false);
            }

            row.Alarm = row.Alarm with
            {
                ReviewedBy = reviewedBy,
                ReviewedAtUtc = DateTimeOffset.UtcNow,
                Resolution = resolution,
            };

            return Task.FromResult(true);
        }

        public Task<int> CountOpenAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Rows.Count(row => row.Alarm.ReviewedAtUtc is null));

        internal sealed class StoredAlarm(BekiAlarm alarm, string evidenceKey)
        {
            public BekiAlarm Alarm { get; set; } = alarm;

            public string EvidenceKey { get; } = evidenceKey;

            /// <summary>How many times this incident has been reported, which is not a column — it
            /// is here so a test can say "one row, three sightings" without reading timestamps.</summary>
            public int Raisings { get; set; } = 1;
        }
    }

    private sealed class CountingAdminNotifier : IAdminNotifier
    {
        public int Pages { get; private set; }

        public Task BookFailedAsync(Guid packId, string reason, CancellationToken cancellationToken)
        {
            Pages++;
            return Task.CompletedTask;
        }

        public Task OrderPaidAsync(Order order, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PrintOrderPlacedAsync(
            PrintOrder printOrder, string? bookTitle, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
