using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Controllers;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// The operations console's own routes: the alarm evidence, the morning overview, the discount
/// codes, and who the customer list thinks is an admin.
///
/// Controller tests rather than service ones, for the reason the release API tests give: every
/// fault these answer reaches somebody as an HTTP response. A PNG served as
/// <c>application/octet-stream</c> is a review nobody can do in the row; a percentage the table
/// would refuse is a 500 on a form submission; a super-admin missing from the list is a "make
/// admin" button offered for somebody who already is one.
/// </summary>
public class AdminOpsApiTests
{
    private static readonly Guid Operator = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset Now =
        new(2026, 9, 2, 9, 14, 0, TimeSpan.Zero);

    // -- alarm evidence ------------------------------------------------------

    [Theory]
    [InlineData("beki/abc/spread-3.png", "image/png")]
    [InlineData("beki/abc/SPREAD-3.PNG", "image/png")]
    [InlineData("beki/abc/portrait.jpeg", "image/jpeg")]
    [InlineData("beki/abc/qa.json", "application/json")]
    [InlineData("beki/abc/press-interior.pdf", "application/pdf")]
    [InlineData("beki/abc/manifest", "application/octet-stream")]
    [InlineData("beki/abc/receipt.bin", "application/octet-stream")]
    public async Task Evidence_is_served_as_what_it_actually_is(string blob, string expected)
    {
        /*
          The content type decides whether an operator sees the picture in the row or is handed a
          download for a file their browser will not open. Nothing stores a media type — the
          evidence key is a blob name written by whichever stage raised the alarm — so it comes from
          the extension, and anything unrecognised is bytes rather than a guess that makes a browser
          try to render a receipt as JSON.
        */
        var alarm = Alarm(blob);
        var controller = ReleaseController(
            new AdminOpsAlarms { Rows = { alarm } },
            new AdminOpsBlobs { Bytes = [1, 2, 3] });

        var result = await controller.AlarmEvidence(alarm.Id, default);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(expected, file.ContentType);
    }

    [Fact]
    public async Task Evidence_is_named_after_the_file_rather_than_the_whole_storage_key()
    {
        var alarm = Alarm("beki/2f0c/spread-3-refused.png");
        var controller = ReleaseController(
            new AdminOpsAlarms { Rows = { alarm } }, new AdminOpsBlobs { Bytes = [1] });

        var file = Assert.IsType<FileStreamResult>(await controller.AlarmEvidence(alarm.Id, default));

        Assert.Equal("spread-3-refused.png", file.FileDownloadName);
    }

    [Fact]
    public async Task An_alarm_with_nothing_behind_it_is_a_404_rather_than_an_empty_file()
    {
        // A timing or bookkeeping incident has no artifact. Zero bytes with a 200 would render as a
        // broken image and read as "the evidence is corrupt", which is a different problem entirely.
        var alarm = Alarm(evidenceBlob: null);
        var controller = ReleaseController(new AdminOpsAlarms { Rows = { alarm } }, new AdminOpsBlobs());

        Assert.IsType<NotFoundObjectResult>(await controller.AlarmEvidence(alarm.Id, default));
    }

    [Fact]
    public async Task Evidence_for_an_alarm_that_does_not_exist_is_a_404()
    {
        var controller = ReleaseController(new AdminOpsAlarms(), new AdminOpsBlobs());

        Assert.IsType<NotFoundObjectResult>(await controller.AlarmEvidence(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task A_key_whose_blob_has_gone_is_a_404_rather_than_a_500()
    {
        // Storage can lose a file the row still names — a lifecycle rule, a container rebuilt. An
        // operator gets "the file is not there", not a stack trace.
        var alarm = Alarm("beki/abc/spread-1.png");
        var controller = ReleaseController(
            new AdminOpsAlarms { Rows = { alarm } }, new AdminOpsBlobs { Missing = true });

        Assert.IsType<NotFoundObjectResult>(await controller.AlarmEvidence(alarm.Id, default));
    }

    // -- the recent list -----------------------------------------------------

    [Fact]
    public async Task Asking_for_the_reviewed_ones_too_actually_asks_a_different_question()
    {
        /*
          open=false used to return the open list and say nothing about it, which made the console's
          "show the closed ones" toggle a control that did nothing. The point of the closed rows is
          that "has this happened before" is answerable: an incident somebody resolved last week is
          exactly what makes this week's identical one worth escalating.
        */
        var alarms = new AdminOpsAlarms { OpenCount = 3 };
        alarms.Rows.Add(Alarm("open.png"));
        alarms.Rows.Add(Alarm("closed.png", reviewedBy: "misho@beki.ge"));

        var controller = ReleaseController(alarms, new AdminOpsBlobs());

        var open = await Ok<AdminAlarmListResponse>(controller.Alarms(true, 100, default));
        var recent = await Ok<AdminAlarmListResponse>(controller.Alarms(false, 100, default));

        Assert.Equal(1, alarms.OpenCalls);
        Assert.Equal(1, alarms.RecentCalls);
        Assert.Single(open.Items);
        Assert.Equal(2, recent.Items.Count);

        // The badge means "how much work is waiting". Showing the closed rows does not change that.
        Assert.Equal(3, open.OpenCount);
        Assert.Equal(3, recent.OpenCount);
    }

    // -- overview ------------------------------------------------------------

    [Fact]
    public async Task The_overview_reports_every_counter_and_the_moment_it_was_taken()
    {
        var counts = new AdminOverviewCounts(
            PaidTodayCount: 4,
            RevenueTodayMinor: 15_960,
            RevenueMonthMinor: 402_300,
            OrdersMonthCount: 87,
            BooksGeneratingCount: 6,
            BooksStuckCount: 2,
            BooksFailedCount: 1,
            AwaitingReviewCount: 3,
            OpenAlarmCount: 9,
            PrintQueue: new AdminPrintQueueCounts(5, 2, 11));

        var reporting = new AdminOpsReporting();
        reporting.Orders.Items = [new AdminOrderRow { Id = Guid.NewGuid(), NeedsAttention = true }];

        var response = await Ok<AdminOverviewResponse>(
            OverviewController(counts, reporting).Overview(default));

        Assert.Equal(4, response.PaidTodayCount);
        Assert.Equal(15_960, response.RevenueTodayMinor);
        Assert.Equal(402_300, response.RevenueMonthMinor);
        Assert.Equal(87, response.OrdersMonthCount);
        Assert.Equal(6, response.BooksGeneratingCount);
        Assert.Equal(2, response.BooksStuckCount);
        Assert.Equal(1, response.BooksFailedCount);
        Assert.Equal(3, response.AwaitingReviewCount);
        Assert.Equal(9, response.OpenAlarmCount);
        Assert.Equal(5, response.PrintQueue.AwaitingPrint);
        Assert.Equal(2, response.PrintQueue.Printing);
        Assert.Equal(11, response.PrintQueue.Shipped);
        Assert.Single(response.RecentAttention);

        // Taken from the injected clock, so the panel's "as of" is the panel's own instant rather
        // than whenever the browser happened to render it.
        Assert.Equal(Now, response.GeneratedAtUtc);
    }

    [Fact]
    public async Task The_overview_reads_its_rows_through_the_list_it_links_to()
    {
        /*
          The recent panel must not be able to show a row the orders list would not. It therefore
          asks the SAME repository method the list uses, with the same saved-view flag, and takes
          the first page of eight.
        */
        var reporting = new AdminOpsReporting();

        await OverviewController(AdminOverviewCounts.Empty, reporting).Overview(default);

        Assert.Equal("needs-attention", reporting.LastFlag);
        Assert.Null(reporting.LastStatus);
        Assert.Null(reporting.LastSearch);
        Assert.Equal(1, reporting.LastPage);
        Assert.Equal(8, reporting.LastPageSize);
    }

    [Fact]
    public async Task The_stuck_cutoff_is_the_sweeps_own_silence_limit()
    {
        // A tile that counted "stuck" by a rule of its own would show a number the sweep then never
        // acts on. Budget plus the sweep's grace, from the same helper the sweep uses.
        var overview = new AdminOpsOverviewRepository(AdminOverviewCounts.Empty);
        var options = new BekiOptions { GenerationBudgetMinutes = 20 };

        await OverviewController(overview, new AdminOpsReporting(), options).Overview(default);

        Assert.Equal(
            Now.UtcDateTime - GenerationBudget.SweepSilenceLimit(options),
            overview.LastStaleCutoffUtc);

        // Midnight and the first of the month, UTC, which is what the console's label promises.
        Assert.Equal(new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), overview.LastDayStartUtc);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), overview.LastMonthStartUtc);
    }

    // -- promo codes ---------------------------------------------------------

    [Fact]
    public async Task A_new_code_is_stored_upper_cased_and_starts_active()
    {
        var repository = new AdminOpsPromoCodes();

        var result = await PromoController(repository).CreatePromoCode(
            new AdminCreatePromoCodeRequest { Code = " beki2026 ", DiscountPercent = 25 },
            default);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var row = Assert.IsType<AdminPromoCodeRow>(created.Value);

        Assert.Equal("BEKI2026", row.Code);
        Assert.Equal(25, row.DiscountPercent);
        Assert.False(row.IsFullDiscount);
        Assert.True(row.IsActive);
        Assert.Equal(0, row.RedemptionCount);
        Assert.Equal("BEKI2026", Assert.Single(repository.Rows).Code);
    }

    [Fact]
    public async Task A_free_code_carries_no_percentage()
    {
        var repository = new AdminOpsPromoCodes();

        var result = await PromoController(repository).CreatePromoCode(
            new AdminCreatePromoCodeRequest { Code = "GIFT", IsFullDiscount = true },
            default);

        var row = Assert.IsType<AdminPromoCodeRow>(Assert.IsType<CreatedResult>(result.Result).Value);

        Assert.True(row.IsFullDiscount);
        Assert.Null(row.DiscountPercent);
    }

    [Theory]
    // The table's CHECK constraint refuses all four of these. Left to it, they arrive as a 500 on a
    // form submission, which tells an operator the console is broken.
    [InlineData(null, 20, false)]
    [InlineData("", 20, false)]
    [InlineData("SUMMER", 0, false)]
    [InlineData("SUMMER", 101, false)]
    [InlineData("SUMMER", null, false)]
    [InlineData("SUMMER", 50, true)]
    [InlineData("ზაფხული", 50, false)]
    public async Task A_discount_that_cannot_exist_is_refused_with_a_reason(
        string? code, int? percent, bool full)
    {
        var result = await PromoController(new AdminOpsPromoCodes()).CreatePromoCode(
            new AdminCreatePromoCodeRequest
            {
                Code = code,
                DiscountPercent = percent,
                IsFullDiscount = full,
            },
            default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_name_that_already_exists_is_a_409_rather_than_an_overwrite()
    {
        // The existing code may have been handed to a thousand people. Nothing about a create form
        // may quietly redefine what it is worth.
        var repository = new AdminOpsPromoCodes();
        repository.Rows.Add(new PromoCode { Id = Guid.NewGuid(), Code = "BEKI2026", PercentOff = 10 });

        var result = await PromoController(repository).CreatePromoCode(
            new AdminCreatePromoCodeRequest { Code = "beki2026", DiscountPercent = 90 },
            default);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(10, Assert.Single(repository.Rows).PercentOff);
    }

    [Fact]
    public async Task Switching_a_code_off_does_not_clear_anything_the_body_did_not_mention()
    {
        /*
          The console's off switch sends { "isActive": false } and nothing else. Read as a
          replacement, that would blank the expiry and the cap of every code somebody paused — which
          is the kind of edit nobody notices until a paused campaign is switched back on and turns
          out to be unlimited and open-ended.
        */
        var expires = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var repository = new AdminOpsPromoCodes();
        repository.Rows.Add(new PromoCode
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Code = "AUTUMN",
            PercentOff = 15,
            MaxRedemptions = 200,
            RedemptionCount = 37,
            ExpiresAt = expires,
            IsActive = true,
        });

        var result = await PromoController(repository).UpdatePromoCode(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            new AdminUpdatePromoCodeRequest { IsActive = false },
            default);

        var row = Assert.IsType<AdminPromoCodeRow>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(row.IsActive);
        Assert.Equal(200, row.MaxRedemptions);
        Assert.Equal(new DateTimeOffset(expires), row.ValidUntilUtc);

        // And the counter is untouched: it is part of the price of orders that already happened.
        Assert.Equal(37, row.RedemptionCount);
    }

    [Fact]
    public async Task An_explicit_null_clears_the_expiry_rather_than_being_ignored()
    {
        var repository = new AdminOpsPromoCodes();
        repository.Rows.Add(new PromoCode
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Code = "OPEN",
            IsFullDiscount = true,
            ExpiresAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true,
        });

        var result = await PromoController(repository).UpdatePromoCode(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            new AdminUpdatePromoCodeRequest { ValidUntilUtc = null },
            default);

        var row = Assert.IsType<AdminPromoCodeRow>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Null(row.ValidUntilUtc);
    }

    [Fact]
    public async Task Editing_a_code_that_does_not_exist_is_a_404()
    {
        var result = await PromoController(new AdminOpsPromoCodes()).UpdatePromoCode(
            Guid.NewGuid(), new AdminUpdatePromoCodeRequest { IsActive = true }, default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task The_list_reports_the_stored_window_as_UTC_instants()
    {
        // A Kind=Unspecified DateTime is a date the browser shifts by its own offset. Tbilisi is
        // UTC+4, so an expiry stored at midnight rendered as 04:00 the same day — a code that
        // looked like it lasted four hours longer than it does.
        var repository = new AdminOpsPromoCodes();
        repository.Rows.Add(new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = "WINDOW",
            PercentOff = 30,
            StartsAt = new DateTime(2026, 9, 1, 0, 0, 0),
            ExpiresAt = new DateTime(2026, 9, 30, 0, 0, 0),
            CreatedAt = new DateTime(2026, 8, 20, 12, 0, 0),
        });

        var rows = await Ok<IReadOnlyList<AdminPromoCodeRow>>(
            PromoController(repository).PromoCodes(default));

        var row = Assert.Single(rows);
        Assert.Equal(TimeSpan.Zero, row.ValidFromUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, row.ValidUntilUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, row.CreatedAtUtc.Offset);
    }

    // -- customers -----------------------------------------------------------

    [Fact]
    public async Task A_configured_super_admin_is_listed_as_an_admin()
    {
        /*
          The setting exists so the console cannot be locked shut, and the demotion endpoint already
          refuses to take the role off one of these accounts. A list that showed them as an ordinary
          customer would offer a "make admin" button for somebody who already is one — and on an
          installation whose only admin is configured rather than stored, it would read as though
          nobody holds the role at all.
        */
        var reporting = new AdminOpsReporting();
        reporting.Customers.Items =
        [
            new AdminCustomerRow { Id = Guid.NewGuid(), Email = "  Misho@Beki.GE ", IsAdmin = false },
            new AdminCustomerRow { Id = Guid.NewGuid(), Email = "parent@example.com", IsAdmin = false },
            new AdminCustomerRow { Id = Guid.NewGuid(), Email = null, IsAdmin = true },
        ];

        var controller = new AdminUsersController(
            reporting,
            new AdminOpsUsers(),
            new AdminOpsUserContext(),
            Options.Create(new AdminOptions { SuperAdminEmails = ["misho@beki.ge"] }),
            NullLogger<AdminUsersController>.Instance);

        var response = await Ok<AdminCustomerListResponse>(
            controller.Customers(null, 1, 25, default));

        Assert.True(response.Items[0].IsAdmin);
        Assert.False(response.Items[1].IsAdmin);

        // The stored column still wins where it says yes; the configuration only ever adds.
        Assert.True(response.Items[2].IsAdmin);
    }

    // -- helpers -------------------------------------------------------------

    private static BekiAlarm Alarm(string? evidenceBlob, string? reviewedBy = null) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        null,
        Operator,
        "image_qa",
        BekiReleaseSeverity.Flag,
        "a spread the reviewer refused",
        evidenceBlob,
        Now.AddDays(-1),
        Now,
        reviewedBy,
        reviewedBy is null ? null : Now,
        reviewedBy is null ? null : BekiAlarmResolutions.Acknowledged);

    private static AdminReleaseController ReleaseController(
        AdminOpsAlarms alarms, AdminOpsBlobs blobs) =>
        new(new AdminOpsPolicy(),
            alarms,
            blobs,
            new AdminOpsUserContext(),
            NullLogger<AdminReleaseController>.Instance);

    private static AdminOverviewController OverviewController(
        AdminOverviewCounts counts, AdminOpsReporting reporting) =>
        OverviewController(new AdminOpsOverviewRepository(counts), reporting, new BekiOptions());

    private static AdminOverviewController OverviewController(
        AdminOpsOverviewRepository overview, AdminOpsReporting reporting, BekiOptions options) =>
        new(overview, reporting, Options.Create(options), new AdminOpsClock(Now));

    private static AdminPromoController PromoController(AdminOpsPromoCodes repository) =>
        new(repository, new AdminOpsUserContext(), NullLogger<AdminPromoController>.Instance);

    private static async Task<T> Ok<T>(Task<ActionResult<T>> action)
    {
        var result = await action;
        return (T)Assert.IsType<OkObjectResult>(result.Result).Value!;
    }

    private sealed class AdminOpsClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AdminOpsUserContext : IUserContextService
    {
        public Guid GetUserId() => Operator;

        public string GetEmail() => "operator@beki.ge";
    }

    /// <summary>
    /// Alarms in memory, keeping the two lists honestly separate: the open one filters, the recent
    /// one does not, and each records that it was asked — which is the only way to catch a
    /// controller that answers "show me the closed ones" with the open list.
    /// </summary>
    private sealed class AdminOpsAlarms : IBekiAlarmService
    {
        public List<BekiAlarm> Rows { get; } = [];

        public int OpenCount { get; init; }

        public int OpenCalls { get; private set; }

        public int RecentCalls { get; private set; }

        public Task RaiseAsync(BekiAlarmRaise raise, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(int limit, CancellationToken ct)
        {
            OpenCalls++;
            return Task.FromResult<IReadOnlyList<BekiAlarm>>(
                Rows.Where(alarm => alarm.IsOpen).Take(limit).ToList());
        }

        public Task<IReadOnlyList<BekiAlarm>> ListRecentAsync(int limit, CancellationToken ct)
        {
            RecentCalls++;
            return Task.FromResult<IReadOnlyList<BekiAlarm>>(Rows.Take(limit).ToList());
        }

        public Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>(
                Rows.Where(alarm => alarm.PackId == packId).ToList());

        public Task<BekiAlarm?> GetAsync(Guid alarmId, CancellationToken ct) =>
            Task.FromResult(Rows.FirstOrDefault(alarm => alarm.Id == alarmId));

        public Task<bool> ReviewAsync(
            Guid alarmId, string reviewedBy, string resolution, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<int> CountOpenAsync(CancellationToken ct) => Task.FromResult(OpenCount);
    }

    private sealed class AdminOpsBlobs : IBlobStorageService
    {
        public byte[] Bytes { get; init; } = [];

        /// <summary>Storage has lost a file the alarm row still names.</summary>
        public bool Missing { get; init; }

        public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken) =>
            Missing
                ? throw new FileNotFoundException(blobName)
                : Task.FromResult<Stream>(new MemoryStream(Bytes));

        public Task<string> UploadAsync(
            string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult(!Missing);

        public Task<byte[]> DownloadBytesFromStoredUrlAsync(
            string storedUrl, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteByStoredUrlAsync(
            string storedUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>The policy the release controller needs to exist; no test here touches it.</summary>
    private sealed class AdminOpsPolicy : IBekiReleasePolicyService
    {
        public Task<BekiReleasePolicySnapshot> SnapshotAsync(CancellationToken ct) =>
            Task.FromResult(BekiReleasePolicySnapshot.Defaults);

        public Task<IReadOnlyList<BekiReleaseCheckSetting>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiReleaseCheckSetting>>([]);

        public Task<int> SetAsync(
            string checkId, string deliverableClass, string severity, string updatedBy,
            CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class AdminOpsOverviewRepository(AdminOverviewCounts counts)
        : IAdminOverviewRepository
    {
        public DateTime LastDayStartUtc { get; private set; }

        public DateTime LastMonthStartUtc { get; private set; }

        public DateTime LastStaleCutoffUtc { get; private set; }

        public Task<AdminOverviewCounts> GetCountsAsync(
            DateTime dayStartUtc,
            DateTime monthStartUtc,
            DateTime staleCutoffUtc,
            CancellationToken cancellationToken)
        {
            LastDayStartUtc = dayStartUtc;
            LastMonthStartUtc = monthStartUtc;
            LastStaleCutoffUtc = staleCutoffUtc;
            return Task.FromResult(counts);
        }
    }

    private sealed class AdminOpsReporting : IAdminReportingRepository
    {
        public AdminOrderListResponse Orders { get; } = new();

        public AdminCustomerListResponse Customers { get; } = new();

        public string? LastStatus { get; private set; }

        public string? LastSearch { get; private set; }

        public string? LastFlag { get; private set; }

        public int LastPage { get; private set; }

        public int LastPageSize { get; private set; }

        public Task<AdminOrderListResponse> GetOrdersAsync(
            string? status, string? search, string? flag, int page, int pageSize,
            CancellationToken cancellationToken)
        {
            LastStatus = status;
            LastSearch = search;
            LastFlag = flag;
            LastPage = page;
            LastPageSize = pageSize;
            return Task.FromResult(Orders);
        }

        public Task<AdminOrderDetailResponse?> GetOrderDetailAsync(
            Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AdminCustomerListResponse> GetCustomersAsync(
            string? search, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(Customers);
    }

    private sealed class AdminOpsPromoCodes : IPromoCodeRepository
    {
        public List<PromoCode> Rows { get; } = [];

        public Task<PromoCode?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.FirstOrDefault(row =>
                string.Equals(row.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase)));

        public Task<PromoCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

        public Task<IReadOnlyList<PromoCode>> ListAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PromoCode>>(Rows.ToList());

        public Task<bool> CreateAsync(PromoCode promoCode, CancellationToken cancellationToken)
        {
            if (Rows.Any(row =>
                    string.Equals(row.Code, promoCode.Code, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(false);
            }

            Rows.Add(promoCode);
            return Task.FromResult(true);
        }

        public Task<bool> UpdateAdminFieldsAsync(
            Guid id, bool isActive, int? maxRedemptions, DateTime? expiresAt,
            CancellationToken cancellationToken)
        {
            var row = Rows.FirstOrDefault(candidate => candidate.Id == id);
            if (row is null)
            {
                return Task.FromResult(false);
            }

            row.IsActive = isActive;
            row.MaxRedemptions = maxRedemptions;
            row.ExpiresAt = expiresAt;
            return Task.FromResult(true);
        }

        public Task<bool> HasUserRedeemedAsync(
            Guid promoCodeId, Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryRedeemAsync(
            PromoRedemption redemption, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// The users table, which the customers list never touches — every method throws so a
    /// controller that started reading it would fail loudly rather than pass.
    /// </summary>
    private sealed class AdminOpsUsers : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<User?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<User?> GetByConfirmationTokenAsync(string token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid> CreateAsync(User user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> PurgeDemoAccountsAsync(string emailSuffix, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> UpdateSubscriptionTypeAsync(
            Guid userId, SubscriptionType subscriptionType, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ConfirmEmailAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SetAdminAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> AttachPhoneNumberAsync(
            Guid userId, string phoneNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> AttachEmailAsync(Guid userId, string email, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateProfileAsync(
            Guid userId, string? displayName, string? preferredLanguage, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddBookCreditsAsync(Guid userId, int credits, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryConsumeBookCreditAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RefundBookCreditAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryConsumeWelcomeStoryAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RefundWelcomeStoryAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
