using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Controllers;
using AdventurePacks.Api.Domain;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// The API half of the parent-first release policy: what the console can set, and what a parent is
/// told when the machinery is holding their book.
///
/// These are controller tests rather than service ones on purpose. Every fault this campaign is
/// answering reached somebody through an HTTP response — an English 400 rendered on a shelf, a
/// finished book listed as ready, a legacy illustrator started by opening a page — and the place to
/// pin that is the boundary the browser actually talks to.
/// </summary>
public class BekiReleaseApiTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // -- release policy ------------------------------------------------------

    [Fact]
    public async Task The_policy_screen_shows_every_check_even_when_nobody_has_set_one()
    {
        /*
          The settings table is built from the policy's own defaults laid over with stored rows,
          because the store holds only overrides. Rendered from the store alone this screen would
          open empty on a fresh install and tell an operator there is nothing here to configure —
          which is the opposite of true, and is how a policy ends up being changed in SQL.
        */
        var controller = ReleaseController(new FakePolicy());

        var response = await Ok<AdminReleasePolicyResponse>(controller.ReleasePolicy(default));

        Assert.NotEmpty(response.Checks);
        Assert.All(response.Checks, check => Assert.True(check.IsDefault));
        Assert.All(response.Checks, check => Assert.Null(check.UpdatedBy));

        // The board is the policy's, not a second copy kept in the API.
        Assert.Equal(BekiReleasePolicySnapshot.Defaults.Settings.Count, response.Checks.Count);
    }

    [Fact]
    public async Task A_stored_decision_wins_over_the_default_and_says_who_made_it()
    {
        var policy = new FakePolicy();
        policy.Stored.Add(new BekiReleaseCheckSetting(
            "PRESS_COLOR", "all", BekiReleaseSeverity.Flag, "misho@beki.ge", DateTimeOffset.UnixEpoch));

        var response = await Ok<AdminReleasePolicyResponse>(ReleaseController(policy).ReleasePolicy(default));

        var row = response.Checks.Single(check =>
            check.CheckId == "PRESS_COLOR" && check.DeliverableClass == "all");

        Assert.Equal(BekiReleaseSeverity.Flag, row.Severity);
        Assert.False(row.IsDefault);
        Assert.Equal("misho@beki.ge", row.UpdatedBy);
    }

    [Fact]
    public async Task A_row_the_defaults_do_not_know_about_is_shown_rather_than_dropped()
    {
        // A check minted by a later campaign, or a row somebody inserted by hand. Hiding it would
        // leave a rule in force with no screen that admits it exists.
        var policy = new FakePolicy();
        policy.Stored.Add(new BekiReleaseCheckSetting(
            "SOME_FUTURE_CHECK", "all", BekiReleaseSeverity.Blocker, "someone", DateTimeOffset.UnixEpoch));

        var response = await Ok<AdminReleasePolicyResponse>(ReleaseController(policy).ReleasePolicy(default));

        Assert.Contains(response.Checks, check => check.CheckId == "SOME_FUTURE_CHECK");
    }

    [Fact]
    public async Task Human_review_is_reported_from_the_snapshot_rather_than_from_the_row()
    {
        var policy = new FakePolicy();
        Assert.False((await Ok<AdminReleasePolicyResponse>(
            ReleaseController(policy).ReleasePolicy(default))).HumanReviewRequired);

        policy.Stored.Add(new BekiReleaseCheckSetting(
            BekiReleaseChecks.HumanReview, "all", BekiReleaseSeverity.Blocker, "misho", null));

        Assert.True((await Ok<AdminReleasePolicyResponse>(
            ReleaseController(policy).ReleasePolicy(default))).HumanReviewRequired);
    }

    [Theory]
    [InlineData(null, "flag")]
    [InlineData("image_qa", "maybe")]
    [InlineData("image_qa", null)]
    public async Task A_malformed_policy_change_is_refused_rather_than_guessed(string? checkId, string? severity)
    {
        var result = await ReleaseController(new FakePolicy()).SetReleasePolicy(
            new AdminSetReleasePolicyRequest { CheckId = checkId, Severity = severity }, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_deliverable_class_the_store_would_refuse_is_refused_here_instead()
    {
        // Left to the CHECK constraint this is a 500 on a settings click, which tells an operator
        // the console is broken when what happened is that they named a deliverable that does not
        // exist.
        var result = await ReleaseController(new FakePolicy()).SetReleasePolicy(
            new AdminSetReleasePolicyRequest
            {
                CheckId = "QR",
                DeliverableClass = "paperback",
                Severity = BekiReleaseSeverity.Flag,
            },
            default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_policy_change_records_the_admin_and_reports_what_it_released()
    {
        /*
          Amendment B7. Loosening a check is a promise to the families whose finished books are
          sitting withheld under the old rule, and the number in the response is how an operator
          learns whether their click reached anybody.

          The number comes from the setting call itself. It used to come from a SECOND reconciliation
          this action ran on top of the one the setting had already started in the background — two
          scans over the same books, and the figure shown was only the share this one won the race
          for. (Review finding 3; the count-is-not-halved half of it is pinned below.)
        */
        var policy = new FakePolicy { WithheldPublished = 3 };

        var response = await Ok<AdminReleasePolicyUpdateResponse>(
            ReleaseController(policy).SetReleasePolicy(
                new AdminSetReleasePolicyRequest
                {
                    CheckId = "QR",
                    DeliverableClass = "press",
                    Severity = BekiReleaseSeverity.Flag,
                },
                default));

        Assert.Equal(3, response.PublishedPacks);
        Assert.Equal("QR", response.Setting.CheckId);
        Assert.Equal("press", response.Setting.DeliverableClass);
        Assert.Equal(BekiReleaseSeverity.Flag, response.Setting.Severity);

        // WHO, from the authenticated admin rather than from the body: a rule change nobody can be
        // asked about is not a decision.
        Assert.Equal("operator@beki.ge", response.Setting.UpdatedBy);
        Assert.Equal(("QR", "press", BekiReleaseSeverity.Flag, "operator@beki.ge"), policy.LastSet);
        Assert.Equal(1, policy.SetCalls);
    }

    /// <summary>
    /// The controller reports the setting call's own number and does not go looking for a second
    /// one — review finding 3, at the boundary.
    ///
    /// It used to await a reconciliation of its own on top of the one the policy service had already
    /// started in the background: two scans over the same withheld set, concurrently. Every write in
    /// both is compare-and-set, so nothing published twice — what was wrong was the number on the
    /// screen. An operator who loosened a check and released nine books could be told four, because
    /// the background copy reached the other five first, and "four" is what they take away about
    /// whether the switch works.
    ///
    /// The other half of the fix is a compile-time fact this file can only state by not stating it:
    /// <see cref="AdminReleaseController"/> no longer takes an <c>IBekiReleaseReconciliation</c> at
    /// all, so there is no second scan for it to start.
    /// </summary>
    [Fact]
    public async Task A_policy_change_reports_the_number_the_one_scan_produced()
    {
        var policy = new FakePolicy { WithheldPublished = 9 };

        var response = await Ok<AdminReleasePolicyUpdateResponse>(
            ReleaseController(policy).SetReleasePolicy(
                new AdminSetReleasePolicyRequest
                {
                    CheckId = "PRESS_RESOLUTION",
                    DeliverableClass = BekiReleaseSeverity.AllClasses,
                    Severity = BekiReleaseSeverity.Flag,
                },
                default));

        Assert.Equal(1, policy.SetCalls);
        Assert.Equal(9, response.PublishedPacks);
    }

    // -- alarms --------------------------------------------------------------

    [Fact]
    public async Task The_alarm_count_is_the_number_that_exists_not_the_number_returned()
    {
        // The header badge has to still mean something when there are four hundred of them and the
        // page holds one.
        var alarms = new FakeAlarms { Open = { Alarm("image_qa"), Alarm("centre_fold") }, OpenCount = 41 };

        var response = await Ok<AdminAlarmListResponse>(
            ReleaseController(alarms: alarms).Alarms(open: true, limit: 1, default));

        Assert.Equal(41, response.OpenCount);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task Reviewing_an_alarm_records_the_signed_in_operator()
    {
        var alarms = new FakeAlarms { Reviewed = true };

        var result = await ReleaseController(alarms: alarms).ReviewAlarm(
            Guid.NewGuid(), new AdminReviewAlarmRequest { Resolution = "fixed" }, default);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("operator@beki.ge", alarms.LastReviewedBy);
        Assert.Equal("fixed", alarms.LastResolution);
    }

    [Fact]
    public async Task Reviewing_an_alarm_that_does_not_exist_is_a_404_not_a_silent_success()
    {
        var result = await ReleaseController(alarms: new FakeAlarms { Reviewed = false }).ReviewAlarm(
            Guid.NewGuid(), new AdminReviewAlarmRequest(), default);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // -- the parent's side ---------------------------------------------------

    [Fact]
    public async Task A_beki_book_never_starts_the_legacy_illustrator_by_being_opened()
    {
        /*
          Amendment B5's guard, and the reason the column exists.

          Opening a StoryReady book's detail page starts per-page illustration. On the legacy
          pipeline that is the point. On this one StoryReady is a stage inside a job that is still
          running and about to draw eight spreads itself, so a second illustrator started here would
          spend money on art nobody will ever see.
        */
        var pack = Pack(AdventurePackStatus.StoryReady, GenerationPipelines.Beki);
        var generation = new FakeGeneration();

        await PacksController(pack, generation).GetById(pack.Id, default);

        Assert.Equal(0, generation.PreviewIllustrationsQueued);
    }

    [Fact]
    public async Task A_legacy_book_still_starts_illustrating_when_it_is_opened()
    {
        var pack = Pack(AdventurePackStatus.StoryReady, GenerationPipelines.Legacy);
        var generation = new FakeGeneration();

        await PacksController(pack, generation).GetById(pack.Id, default);

        Assert.Equal(1, generation.PreviewIllustrationsQueued);
    }

    [Fact]
    public async Task A_beki_book_short_of_Completed_is_reported_as_still_being_made()
    {
        var pack = Pack(AdventurePackStatus.StoryReady, GenerationPipelines.Beki);

        var detail = await Ok<AdventurePackDetailResponse>(
            PacksController(pack).GetById(pack.Id, default));

        Assert.True(detail.GenerationPending);
        Assert.Equal(GenerationPipelines.Beki, detail.GenerationPipeline);

        // And it is described rather than left blank: an empty page list drew a cover, a back cover
        // and nothing between them, which reads as a book that failed.
        Assert.False(string.IsNullOrWhiteSpace(detail.ProgressMessage));
        Assert.DoesNotContain("Pack", detail.ProgressMessage);
    }

    [Fact]
    public async Task A_legacy_book_at_StoryReady_is_not_pending()
    {
        var pack = Pack(AdventurePackStatus.StoryReady, GenerationPipelines.Legacy);

        var detail = await Ok<AdventurePackDetailResponse>(
            PacksController(pack).GetById(pack.Id, default));

        Assert.False(detail.GenerationPending);
    }

    [Fact]
    public async Task A_finished_book_whose_file_is_held_says_so_on_the_list()
    {
        var pack = Pack(AdventurePackStatus.Completed, GenerationPipelines.Beki);

        var rows = await Ok<IReadOnlyList<AdventurePackResponse>>(
            PacksController(pack, held: BekiDownloadHeld.Review).Get(default));

        Assert.Equal(BekiDownloadHeld.Review, rows.Single().DownloadHeld);
    }

    [Fact]
    public async Task A_published_book_is_not_asked_why_it_is_held()
    {
        // The question reads a stored verdict out of blob storage. Asking it about every finished
        // book on a shelf would be one blob read per card for an answer that is always null.
        var pack = Pack(AdventurePackStatus.Completed, GenerationPipelines.Beki);
        pack.PdfUrl = "packs/reading.pdf";

        var status = new FakeDownloadStatus { Held = BekiDownloadHeld.Gates };
        var rows = await Ok<IReadOnlyList<AdventurePackResponse>>(
            PacksController(pack, downloadStatus: status).Get(default));

        Assert.Null(rows.Single().DownloadHeld);
        Assert.Equal(0, status.Asked);
    }

    [Fact]
    public async Task The_download_of_a_withheld_book_answers_in_Georgian_and_says_it_is_held()
    {
        /*
          The download lie, pinned.

          This route returned the bare English string "Pack is not ready." as a 400 body, and the
          reader rendered whatever came back — so a parent whose finished book was waiting on a
          reviewer read an untranslated sentence with no subject.
        */
        var pack = Pack(AdventurePackStatus.Completed, GenerationPipelines.Beki);

        var result = await PacksController(pack, held: BekiDownloadHeld.Review).Download(pack.Id, default);

        var body = Assert.IsType<BadRequestObjectResult>(result).Value!;
        var message = (string)body.GetType().GetProperty("message")!.GetValue(body)!;
        var held = body.GetType().GetProperty("downloadHeld")!.GetValue(body);

        Assert.Equal(BekiDownloadHeld.Review, held);
        Assert.DoesNotContain("Pack", message);
        Assert.DoesNotContain("ready", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("წიგნი", message);
    }

    [Fact]
    public async Task A_book_still_being_made_gets_the_waiting_message_not_the_withheld_one()
    {
        // Two different waits, and the book's own state decides which. One has pictures behind it;
        // the other has a person.
        var pack = Pack(AdventurePackStatus.StoryReady, GenerationPipelines.Beki);

        var result = await PacksController(pack).Download(pack.Id, default);

        var body = Assert.IsType<BadRequestObjectResult>(result).Value!;
        var message = (string)body.GetType().GetProperty("message")!.GetValue(body)!;

        Assert.Null(body.GetType().GetProperty("downloadHeld")!.GetValue(body));
        Assert.Contains("მზადდება", message);
    }

    // -- fixtures ------------------------------------------------------------

    private static AdventurePack Pack(AdventurePackStatus status, string pipeline) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Owner,
        Status = status,
        GenerationPipeline = pipeline,
        AccessLevel = BookAccessLevel.Full,
        Theme = ThemeType.Dinosaurs,
        Title = "ზუკა და დინოზავრები",
    };

    private static BekiAlarm Alarm(string checkId) => new(
        Guid.NewGuid(), Guid.NewGuid(), null, Owner, checkId, BekiReleaseSeverity.Flag,
        "detail", null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null, null);

    private static AdminReleaseController ReleaseController(
        FakePolicy? policy = null,
        FakeAlarms? alarms = null) =>
        new(policy ?? new FakePolicy(),
            alarms ?? new FakeAlarms(),
            new FakeUserContext(),
            NullLogger<AdminReleaseController>.Instance);

    private static AdventurePacksController PacksController(
        AdventurePack pack,
        FakeGeneration? generation = null,
        string? held = null,
        FakeDownloadStatus? downloadStatus = null) =>
        new(generation ?? new FakeGeneration(),
            new FakePackReads(pack),
            new FakeCast(),
            new FakeBlobs(),
            new FakeUserContext(),
            new FakeRateLimiter(),
            new FakeMasterBooks(),
            downloadStatus ?? new FakeDownloadStatus { Held = held },
            Options.Create(new ClientIpOptions()),
            new FakeCharacters(),
            NullLogger<AdventurePacksController>.Instance);

    private static async Task<T> Ok<T>(Task<ActionResult<T>> action)
    {
        var result = await action;
        return (T)Assert.IsType<OkObjectResult>(result.Result).Value!;
    }

    private sealed class FakeUserContext : IUserContextService
    {
        public Guid GetUserId() => Owner;

        public string GetEmail() => "operator@beki.ge";
    }

    /// <summary>
    /// The policy service, which is also where the reconciliation lives now.
    ///
    /// <see cref="SetAsync"/> returning the count is the shape review finding 3 settled on: one scan
    /// per policy change, run by the thing that changed the policy, and its own number handed back.
    /// The controller no longer has a reconciliation to call at all, which is why there is no longer
    /// a double for one in this file.
    /// </summary>
    private sealed class FakePolicy : IBekiReleasePolicyService
    {
        public List<BekiReleaseCheckSetting> Stored { get; } = [];

        public int WithheldPublished { get; init; }

        public int SetCalls { get; private set; }

        public (string CheckId, string Class, string Severity, string By)? LastSet { get; private set; }

        public Task<BekiReleasePolicySnapshot> SnapshotAsync(CancellationToken ct) =>
            Task.FromResult(new BekiReleasePolicySnapshot(
                BekiReleasePolicySnapshot.Defaults.Settings.Concat(Stored)));

        public Task<IReadOnlyList<BekiReleaseCheckSetting>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiReleaseCheckSetting>>(Stored);

        public Task<int> SetAsync(
            string checkId, string deliverableClass, string severity, string updatedBy, CancellationToken ct)
        {
            SetCalls++;
            LastSet = (checkId, deliverableClass, severity, updatedBy);
            return Task.FromResult(WithheldPublished);
        }
    }

    private sealed class FakeAlarms : IBekiAlarmService
    {
        public List<BekiAlarm> Open { get; } = [];

        public int OpenCount { get; init; }

        public bool Reviewed { get; init; }

        public string? LastReviewedBy { get; private set; }

        public string? LastResolution { get; private set; }

        public Task RaiseAsync(BekiAlarmRaise raise, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>(Open);

        public Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>(Open);

        public Task<bool> ReviewAsync(Guid alarmId, string reviewedBy, string resolution, CancellationToken ct)
        {
            LastReviewedBy = reviewedBy;
            LastResolution = resolution;
            return Task.FromResult(Reviewed);
        }

        public Task<int> CountOpenAsync(CancellationToken ct) => Task.FromResult(OpenCount);
    }

    private sealed class FakeDownloadStatus : IBekiDownloadStatusService
    {
        public string? Held { get; init; }

        public int Asked { get; private set; }

        public Task<string?> DownloadHeldReasonAsync(Guid userId, Guid packId, CancellationToken ct)
        {
            Asked++;
            return Task.FromResult(Held);
        }
    }

    private sealed class FakeGeneration : IAdventureGenerationService
    {
        public int PreviewIllustrationsQueued { get; private set; }

        public Task EnsurePreviewIllustrationQueuedAsync(Guid adventurePackId, CancellationToken ct)
        {
            PreviewIllustrationsQueued++;
            return Task.CompletedTask;
        }

        public Task<GuestPreviewResult> GenerateGuestPreviewAsync(GuestPreviewInput input, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task QueueIllustrationAsync(Guid userId, Guid packId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task QueuePdfGenerationAsync(Guid userId, Guid packId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ProcessStoryGenerationAsync(Guid adventurePackId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ProcessPreviewIllustrationAsync(Guid adventurePackId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ProcessFreeSampleIllustrationAsync(Guid adventurePackId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ProcessPdfGenerationAsync(Guid adventurePackId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    /// <summary>Reads only. Nothing in these tests writes a pack, and a write here would be a bug.</summary>
    private sealed class FakePackReads(AdventurePack pack) : IAdventurePackRepository
    {
        public Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct) =>
            Task.FromResult<AdventurePack?>(id == pack.Id && userId == pack.UserId ? pack : null);

        public Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AdventurePack>>(userId == pack.UserId ? [pack] : []);

        public Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(
            Guid characterId, Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> SetAccessLevelAsync(Guid id, BookAccessLevel accessLevel, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task UpdateBookPresentationAsync(
            Guid id, string? title, string? coverImageUrl, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<int> CountForMonthAsync(
            Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> UpdateStatusAsync(
            Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl,
            string? errorMessage, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> TryUpdateStatusAsync(
            Guid id, AdventurePackStatus expectedStatus, AdventurePackStatus status,
            string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(
            DateTime cutoffUtc, int limit, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> TryFailStaleGenerationAsync(
            Guid id, AdventurePackStatus expectedStatus, DateTime cutoffUtc, string errorMessage,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> TryFailAsync(
            Guid id, AdventurePackStatus expectedStatus, string errorMessage, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task UpdateProgressAsync(
            Guid id, string? progressMessage, int? progressPercent, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task UpdatePreviewIllustrationAsync(
            Guid id, PreviewIllustrationStatus status, string? illustrationUrl, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> TryClaimPreviewIllustrationGenerationAsync(
            Guid id, int staleAfterMinutes, CancellationToken ct) => throw new NotSupportedException();

        public Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> UpdateGeneratedJsonAsync(Guid id, string generatedJson, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SetGenerationPipelineAsync(Guid id, string pipeline, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AdventurePack>> ListWithheldBekiPacksAsync(
            int limit, BekiWithheldCursor? after, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeCast : IBookCastResolver
    {
        public Task<BookCast> ResolveAsync(AdventurePack book, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task CacheAppearanceAsync(
            Guid userId, BookCastMember member, string appearanceDescription, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeBlobs : IBlobStorageService
    {
        public Task<string> UploadAsync(
            string blobName, byte[] bytes, string contentType, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Stream> DownloadAsync(string blobName, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(string blobName, CancellationToken ct) => Task.FromResult(false);

        public Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRateLimiter : IGuestRateLimiter
    {
        public bool TryAcquire(string clientKey) => true;
    }

    private sealed class FakeMasterBooks : IMasterBookService
    {
        public Task<Guid> StartAsync(GuestPreviewInput input, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task WriteBookAsync(Guid runId, CancellationToken ct) => throw new NotSupportedException();

        public Task<MasterStoryRun?> GetAsync(Guid runId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid runId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    /// <summary>None of these routes touches a character; the saved-hero lookup is on the preview start.</summary>
    private sealed class FakeCharacters : ICharacterRepository
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
}
