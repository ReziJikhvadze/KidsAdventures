using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The fulfilment job on the composite path, at the seams the audit of 2026-09-01 found loose.
///
/// Six findings, one harness. The preview cover is no longer fetched for a path that never ships
/// it (F1). The cover wrap is started the moment the pipeline announces the anchor and awaited where
/// it used to be drawn, with a failure on either side handled exactly as before (F5). The press
/// tail runs on its own clock, and running out of it withholds the printer's files rather than
/// failing a book the family can already read (F4). Every stage of the tail moves the progress bar
/// and names itself on the row (F7). Adopted spreads hand the pipeline their stored QA records so
/// the version guard has something to guard (F12). And the opening claim is a compare-and-set that
/// does no work when it loses (F14).
///
/// Everything is faked; nothing here calls a model. The composed documents are stub bytes, so print
/// and digital preparation refuse the way the cover-projection harness's do, and the gates read a
/// NOT_RELEASABLE book that still ships to the family — which is the owner's policy, and not the
/// subject of any test below.
/// </summary>
public class CompositePipelineFulfillmentTests
{
    [Fact]
    public async Task Print_artwork_fallback_does_not_bypass_corrupt_customer_pdf_validation()
    {
        var world = new PackWorld { MalformedUpscale = true };
        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);
        Assert.StartsWith("PRINT_PREFLIGHT_FAILED: CUSTOMER_PDF_INVALID", world.Packs.FailureReason);
        Assert.Equal(AdventurePackStatus.Failed, world.Packs.Status);
        Assert.Null(world.Packs.PdfUrl);
        Assert.Null(world.Packs.PrintPdfUrl);
    }

    /// <summary>
    /// A press gate the MEASUREMENT failed still ships the family's book, and the four ways the
    /// diagnostics around it can themselves break do not change that.
    ///
    /// The fixture is a real twelve-page canonical document with no artwork on it, so
    /// PRESS_RESOLUTION fails for the honest reason — every page owes a full-sheet raster and none
    /// carries one — rather than because a tool was missing. That state no longer exists: there is
    /// nothing to configure and nothing to forget to install, so the old "missing-upscaler" theory
    /// case is gone with it.
    /// </summary>
    [Theory]
    [InlineData("normalizer-output-undecodable")]
    [InlineData("alarm-unavailable")]
    [InlineData("print-status-unavailable")]
    [InlineData("waiver-alarm-unavailable")]
    public async Task Print_only_failures_complete_the_customer_book_with_a_download(string failure)
    {
        var world = new PackWorld
        {
            MalformedUpscale = failure == "normalizer-output-undecodable",
            WaiverAlarmFails = failure == "waiver-alarm-unavailable",
        };
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalScreenBook();
        world.Alarms.ThrowOnRaise = failure == "alarm-unavailable";
        world.Blobs.FailPrintStatus = failure == "print-status-unavailable";

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.Null(world.Packs.FailureReason);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
        Assert.Equal($"https://blob.test/{BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId)}",
            world.Packs.PdfUrl);
        Assert.Null(world.Packs.PrintPdfUrl);
        Assert.Equal(world.Composer.CanonicalPdf,
            world.Blobs.Uploaded[BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId)]);
        var release = BekiReleaseGateReport.TryParse(Encoding.UTF8.GetString(
            world.Blobs.Uploaded[BekiPackBlobs.ReleaseGatesName(world.UserId, world.PackId)]))!;
        Assert.True(release.CustomerPdfMayPublish);
        Assert.False(release.PrintReady);
        Assert.DoesNotContain(world.Alarms.Raised, alarm => alarm.CheckId == "PRINT_PREPARATION_HELD");

        // The preflight is a real measurement of the real document, not a sentence about a raster:
        // the gate names the pages, and nothing in it blames a tool that was never involved.
        var preflight = Encoding.UTF8.GetString(
            world.Blobs.Uploaded[BekiPackBlobs.CanonicalPreflightName(world.UserId, world.PackId)]);
        Assert.Contains("Printing has not been requested", preflight, StringComparison.Ordinal);
        Assert.DoesNotContain("upscaler", preflight, StringComparison.OrdinalIgnoreCase);

        if (failure == "print-status-unavailable")
        {
            // No press-status document, so the gates have nothing of this stage's to read — the
            // book still ships and the alarm above is still raised. (The release evaluator reads
            // the press verdict from that document alone; that is its own long-standing rule.)
            Assert.Equal(1, world.Blobs.PrintStatusFailures);
            Assert.DoesNotContain(BekiPackBlobs.PressStatusName(world.UserId, world.PackId),
                world.Blobs.Uploaded.Keys);
            return;
        }

        Assert.Contains("PRESS_RESOLUTION", release.FailingGates);

        // The press-status record answers the two questions separately: which gates the output
        // failed, and what went wrong on the way to producing it.
        var pressStatus = Encoding.UTF8.GetString(
            world.Blobs.Uploaded[BekiPackBlobs.PressStatusName(world.UserId, world.PackId)]);
        Assert.DoesNotContain("\"upscaler_configured\"", pressStatus, StringComparison.Ordinal);
        Assert.Contains("\"print_prep_mode\":\"deterministic_lanczos\"", pressStatus, StringComparison.Ordinal);

        using var status = JsonDocument.Parse(pressStatus);
        var problems = status.RootElement.GetProperty("preparation_problems")
            .EnumerateArray().Select(problem => problem.GetString()!).ToList();

        Assert.Empty(problems); // The normalizer is not called during customer delivery.
        Assert.False(world.Composer.LastPrepareForPrint);
    }

    /// <summary>
    /// A raster whose normalization failed and whose composed output measures correctly is not a
    /// failed gate and not a hold — amendment A2's whole point.
    ///
    /// The composer here hands back a document that satisfies every locked size, exactly as the
    /// real one does when the original artwork was already the right shape. The press stage's own
    /// trouble is recorded, and the file that comes out is judged on what it is.
    /// </summary>
    [Fact]
    public async Task Even_print_sized_input_waits_for_an_explicit_admin_print_request()
    {
        var world = new PackWorld { MalformedUpscale = true };
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.Null(world.Packs.FailureReason);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);

        // No gate failed and nothing was held, even though a raster's preparation did fail.
        Assert.DoesNotContain(world.Alarms.Raised, alarm => alarm.CheckId == "PRINT_PREPARATION_HELD");

        using var status = JsonDocument.Parse(Encoding.UTF8.GetString(
            world.Blobs.Uploaded[BekiPackBlobs.PressStatusName(world.UserId, world.PackId)]));
        Assert.Contains(status.RootElement.GetProperty("failed_gates").EnumerateArray(), g => g.GetString() == "PRESS_RESOLUTION");
        Assert.Equal("withheld", status.RootElement.GetProperty("interior").GetString());
        Assert.Empty(status.RootElement.GetProperty("preparation_problems").EnumerateArray());

        var release = BekiReleaseGateReport.TryParse(Encoding.UTF8.GetString(
            world.Blobs.Uploaded[BekiPackBlobs.ReleaseGatesName(world.UserId, world.PackId)]))!;
        Assert.Contains("PRESS_RESOLUTION", release.FailingGates);
    }

    // =======================================================================================
    // F1 — the preview cover is not fetched on the composite path
    // =======================================================================================

    [Fact]
    public async Task The_composite_path_never_downloads_the_previewed_cover()
    {
        var world = new PackWorld();

        await world.Run();

        Assert.DoesNotContain(PackWorld.PreviewCoverUrl, world.Blobs.Downloaded);

        // And the book still has its one cover master, made from the wrap.
        Assert.Contains(
            BekiPackBlobs.CoverWrapCompositeName(world.UserId, world.PackId), world.Blobs.Uploaded.Keys);
        Assert.Equal(AdventurePackStatus.Failed, world.Packs.Status);
    }

    // =======================================================================================
    // F5 — the wrap draws beside the spreads
    // =======================================================================================

    /// <summary>
    /// When the pipeline announces the anchor, the job starts the wrap right there — while the
    /// illustrator is still running — and draws it to the announced identity and anchor.
    /// </summary>
    [Fact]
    public async Task The_wrap_starts_when_the_anchor_is_announced_and_is_awaited_after_the_spreads()
    {
        var world = new PackWorld { AnnounceAnchor = true };

        await world.Run();

        Assert.True(world.Generator.WrapStartedDuringIllustrate, "the wrap was not started from the anchor hook.");
        Assert.Equal(1, world.Generator.WrapCalls);
        Assert.Equal(CompositePipelineTestBase.IdentityFixture, world.Generator.WrapIdentity);
        Assert.Equal(PackWorld.AnnouncedAnchor, world.Generator.WrapAnchor);

        // The same master, the same receipt check, the same reader repoint as the serial path.
        var wrapName = BekiPackBlobs.CoverWrapCompositeName(world.UserId, world.PackId);
        Assert.Contains(wrapName, world.Blobs.Uploaded.Keys);
        Assert.Null(world.Composer.ReadingWrap);
        Assert.Equal(
            $"https://blob.test/{BekiPackBlobs.CoverFrontName(world.UserId, world.PackId)}",
            world.Packs.CoverImageUrl);
    }

    /// <summary>
    /// A wrap that fails while drawing beside the spreads fails the book with the code it always
    /// had, after the spreads — not as a spread failure, and not silently.
    /// </summary>
    [Fact]
    public async Task A_wrap_that_fails_beside_the_spreads_fails_the_book_with_the_same_code()
    {
        var world = new PackWorld { AnnounceAnchor = true, WrapFails = true };

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.NotNull(world.Packs.FailureReason);
        Assert.StartsWith(CompositeFailureCodes.LayoutFailed, world.Packs.FailureReason);
        Assert.Contains("cover wrap", world.Packs.FailureReason);

        // The spreads were all stored before the wrap's outcome was read, as they always were.
        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            Assert.Contains(
                BekiPackBlobs.SpreadName(world.UserId, world.PackId, spread), world.Blobs.Uploaded.Keys);
        }

        Assert.Equal(1, world.Notifier.Notifications);
    }

    /// <summary>
    /// When the spreads fail with a wrap still drawing beside them, the wrap is stopped and the
    /// book fails of the spreads — the wrap's own outcome is neither the reason nor left running.
    /// </summary>
    [Fact]
    public async Task A_spread_failure_stops_the_wrap_that_was_started_beside_it()
    {
        var world = new PackWorld
        {
            AnnounceAnchor = true,
            WrapHangs = true,
            SpreadsFail = new CompositePipelineException(
                CompositeFailureCodes.ImageQaFailed, "spread 3 was refused.")
            {
                Page = 3,
            },
        };

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.NotNull(world.Packs.FailureReason);
        Assert.StartsWith($"{CompositeFailureCodes.ImageQaFailed} (spread 3)", world.Packs.FailureReason);

        Assert.True(world.Generator.WrapStartedDuringIllustrate);
        Assert.True(world.Generator.WrapTokenCancelled, "the wrap was left drawing after the book failed.");
    }

    // =======================================================================================
    // The cover the parent previewed is the cover they get (owner, 2026-09-06)
    // =======================================================================================

    /// <summary>
    /// A book whose preview run drew the REAL cover adopts it whole — the wrap, the Visual Scenario
    /// it was planned from, and the child identity spec it was drawn to — and buys none of the three
    /// again.
    ///
    /// This is the owner's ask, stated as behaviour: "create the cover first — the REAL cover — and
    /// when we proceed to the book, reuse it." What made the old arrangement improper was not the
    /// cost but the identity of the picture: the preview drew one cover and the purchased book drew
    /// a different one, so the book that arrived was not the book that was chosen.
    /// </summary>
    [Fact]
    public async Task A_run_that_already_holds_the_real_cover_has_it_adopted_rather_than_redrawn()
    {
        var world = new PackWorld { AnnounceAnchor = true };
        world.SeedPreviewCover();

        await world.Run();

        // Not one wrap call. The anchor hook was never even wired up, so there was nothing to start
        // beside the spreads and nothing to await after them.
        Assert.Equal(0, world.Generator.WrapCalls);
        Assert.False(world.Generator.WrapStartedDuringIllustrate);

        // And the two documents reached the pipeline as things to adopt, so neither the identity
        // call nor the scenario call is bought a second time for this book.
        var resume = world.Generator.Resume!;
        Assert.Equal(PreviewWrapFixture.ScenarioJson, resume.ScenarioJson);
        Assert.Equal(PreviewWrapFixture.IdentityJson, resume.IdentitySpecJson);

        // Spread one is drawn against the cover's own base — child and world, no Beki on it — which
        // is what makes the book's child the child on its own cover.
        Assert.Equal(PreviewWrapFixture.BasePng, world.Generator.Context!.CoverAnchorBasePng);

        // The pack's cover blobs ARE the run's, file for file.
        Assert.Equal(
            PreviewWrapFixture.CompositePng,
            world.Blobs.Uploaded[BekiPackBlobs.CoverWrapCompositeName(world.UserId, world.PackId)]);
        Assert.Equal(
            PreviewWrapFixture.BasePng,
            world.Blobs.Uploaded[BekiPackBlobs.CoverWrapBaseName(world.UserId, world.PackId)]);
        Assert.Equal(
            PreviewWrapFixture.ReceiptJson,
            Encoding.UTF8.GetString(
                world.Blobs.Uploaded[BekiPackBlobs.CoverCompositionName(world.UserId, world.PackId)]));
        Assert.Equal(
            PreviewWrapFixture.GenerationJson,
            Encoding.UTF8.GetString(
                world.Blobs.Uploaded[BekiPackBlobs.CoverWrapGenerationName(world.UserId, world.PackId)]));
        Assert.Equal(
            PreviewWrapFixture.IdentityJson,
            Encoding.UTF8.GetString(
                world.Blobs.Uploaded[BekiPackBlobs.IdentitySpecName(world.UserId, world.PackId)]));

        // The reader's cover is cut from those same bytes, as it is on every composite book.
        Assert.Equal(
            $"https://blob.test/{BekiPackBlobs.CoverFrontName(world.UserId, world.PackId)}",
            world.Packs.CoverImageUrl);

        // The provenance is still the wrap master — it IS one — and the verdict names the preview it
        // came from, so an operator holding the book can find the screen the parent chose it on.
        var cover = Manifest(world).Cover!;
        Assert.Equal(BekiCoverRecord.WrapMaster, cover.PromptVersion);
        Assert.Equal($"adopted from preview run {world.RunId}", cover.Verdict);

        // And the run's own copies are untouched: the pack got a copy, not a move.
        foreach (var name in BekiRunBlobs.CoverArtifacts(world.RunId))
        {
            Assert.Contains(name, world.Blobs.Uploaded.Keys);
        }
    }

    /// <summary>
    /// A run with no stored cover — every book drawn before this existed, and every one whose
    /// preview wrap failed — is fulfilled exactly as it was: the wrap is drawn beside the spreads and
    /// the scenario and the identity spec are planned and derived by the pipeline.
    /// </summary>
    [Fact]
    public async Task A_run_with_no_previewed_cover_draws_its_own_exactly_as_before()
    {
        var world = new PackWorld { AnnounceAnchor = true };

        await world.Run();

        Assert.Equal(1, world.Generator.WrapCalls);
        Assert.True(world.Generator.WrapStartedDuringIllustrate);

        // Nothing to adopt, so the pipeline plans and derives its own — and spread one draws from
        // the lock and the photograph, which is the condition its prompt is written for.
        Assert.Null(world.Generator.Resume!.ScenarioJson);
        Assert.Null(world.Generator.Resume.IdentitySpecJson);
        Assert.Null(world.Generator.Context!.CoverAnchorBasePng);

        Assert.Equal(
            StubGenerator.WrapComposite,
            world.Blobs.Uploaded[BekiPackBlobs.CoverWrapCompositeName(world.UserId, world.PackId)]);

        Assert.DoesNotContain("adopted", Manifest(world).Cover!.Verdict, StringComparison.Ordinal);
    }

    /// <summary>
    /// An operator's "redraw the cover" still redraws it. The redraw deletes the pack's wrap and
    /// leaves the manifest — cover record and all — so the requeued job finds a book that has had a
    /// cover of its own and draws the new one that was asked for, instead of quietly restoring the
    /// previewed picture the operator was trying to replace.
    ///
    /// And the run's copies survive the deletion, which is what makes the redraw reversible by
    /// nothing worse than a whole-book redraw.
    /// </summary>
    [Fact]
    public async Task An_operators_cover_redraw_is_not_undone_by_the_previewed_cover()
    {
        var world = new PackWorld { AnnounceAnchor = true };
        world.SeedPreviewCover();

        await world.Run();
        Assert.Equal(0, world.Generator.WrapCalls);

        var redraw = await world.Redraw().RequestAsync(
            new BekiRegenerationRequest(
                world.PackId, BekiRegenerationScopes.Cover, null, "the wrap is wrong", "an operator"),
            CancellationToken.None);

        Assert.True(redraw.Queued, redraw.Message);
        Assert.Equal(1, world.Jobs.Enqueued);

        // The pack's whole cover master and every derivation of it are gone…
        foreach (var name in (string[])
                 [
                     BekiPackBlobs.CoverWrapCompositeName(world.UserId, world.PackId),
                     BekiPackBlobs.CoverWrapBaseName(world.UserId, world.PackId),
                     BekiPackBlobs.CoverCompositionName(world.UserId, world.PackId),
                     BekiPackBlobs.CoverWrapGenerationName(world.UserId, world.PackId),
                     BekiPackBlobs.CoverFrontName(world.UserId, world.PackId),
                 ])
        {
            Assert.DoesNotContain(name, world.Blobs.Uploaded.Keys);
        }

        // …and the run's copies are not: the redraw deletes the pack's names, and the preview's live
        // under the run's own prefix. A parent's preview screen keeps working through a redraw.
        foreach (var name in BekiRunBlobs.CoverArtifacts(world.RunId))
        {
            Assert.Contains(name, world.Blobs.Uploaded.Keys);
        }

        /*
          And the manifest survives with its cover record, which is what makes the redraw stick.

          The requeued job reads that record, sees a book that has had a cover of its own, and does
          not adopt — so it draws the new wrap the operator asked for instead of quietly restoring
          the previewed picture they were trying to replace. A cover-scope redraw deliberately
          leaves the manifest alone; the whole-book scope deletes it, and adopting there is right,
          because "redraw the book" is about the pages.
        */
        Assert.Contains(
            BekiPackBlobs.ManifestName(world.UserId, world.PackId), world.Blobs.Uploaded.Keys);
        Assert.Equal(BekiCoverRecord.WrapMaster, Manifest(world).Cover!.PromptVersion);
    }

    /// <summary>The pack's fulfilment manifest as this run left it.</summary>
    private static BekiFulfillmentManifest Manifest(PackWorld world) =>
        JsonSerializer.Deserialize<BekiFulfillmentManifest>(
            world.Blobs.Uploaded[BekiPackBlobs.ManifestName(world.UserId, world.PackId)],
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    // =======================================================================================
    // F7 — the tail is visible
    // =======================================================================================

    [Fact]
    public async Task Progress_moves_through_every_stage_of_the_tail_and_speaks_georgian()
    {
        var world = new PackWorld();

        await world.Run();

        var percents = world.Packs.Progress.Select(step => step.Percent ?? -1).ToList();

        // Never backwards. This harness returns a deliberately corrupt PDF, so customer
        // validation must still stop publication even though print preparation is optional.
        Assert.Equal(percents.OrderBy(percent => percent), percents);
        Assert.Contains(85, percents);
        Assert.DoesNotContain(100, percents);
        Assert.StartsWith("PRINT_PREFLIGHT_FAILED", world.Packs.FailureReason);

        // The parent's screen and the admin read the same line, and it is in the book's language.
        Assert.All(world.Packs.Progress, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Message));
            Assert.Contains(step.Message!, letter => letter is >= 'Ⴀ' and <= 'ჿ');
        });
    }

    // =======================================================================================
    // F4 — the press tail's own clock
    // =======================================================================================

    /// <summary>
    /// The press tail running out of time withholds the printer's files and completes the book.
    ///
    /// The clock fired here is EVERY clock — the job's thirty minutes as well as the tail's own —
    /// which is the shape of the defect: a slow upscaler after the reading copy was stored used to
    /// land in the catch-all as a budget failure, mark a finished book Failed, page an operator and
    /// write to the parent. Now the tail's expiry is recorded as a withholding, the gates still
    /// judge the book, and nothing after the reading copy runs under the job's clock at all.
    /// </summary>
    [Fact]
    public async Task A_print_timeout_reaches_customer_validation_without_publishing_corrupt_stub_bytes()
    {
        var world = new PackWorld { PressStalls = true };

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.StartsWith("PRINT_PREFLIGHT_FAILED: CUSTOMER_PDF_INVALID", world.Packs.FailureReason);
        Assert.Equal(AdventurePackStatus.Failed, world.Packs.Status);
        Assert.Null(world.Packs.PrintPdfUrl);
        Assert.Null(world.Packs.PdfUrl);
        Assert.Equal(1, world.Notifier.Notifications);
    }

    /// <summary>
    /// The job's own clock firing during the press tail is simply not observed: the tail is not
    /// running under it, and the finish line is not either.
    /// </summary>
    [Fact]
    public async Task The_drawing_clock_expiring_during_print_does_not_skip_customer_validation()
    {
        var world = new PackWorld { JobClockFiresDuringPress = true };

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.NotNull(world.Packs.FailureReason);
        Assert.StartsWith("PRINT_PREFLIGHT_FAILED: CUSTOMER_PDF_INVALID", world.Packs.FailureReason);
        Assert.Equal(AdventurePackStatus.Failed, world.Packs.Status);
        Assert.Empty(world.Alarms.Raised);
        Assert.Equal(1, world.Notifier.Notifications);
    }

    [Fact]
    public void The_press_budget_has_a_default_and_falls_back_to_it()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), BekiPackFulfillment.PressBudgetFor(new BekiOptions()));
        Assert.Equal(TimeSpan.FromMinutes(15), BekiPackFulfillment.PressBudgetFor(new BekiOptions { PressBudgetMinutes = 0 }));
        Assert.Equal(TimeSpan.FromMinutes(15), BekiPackFulfillment.PressBudgetFor(new BekiOptions { PressBudgetMinutes = -3 }));
        Assert.Equal(TimeSpan.FromMinutes(25), BekiPackFulfillment.PressBudgetFor(new BekiOptions { PressBudgetMinutes = 25 }));

        // The image phase's default is the deployed value rather than the two it lagged at.
        Assert.Equal(4, new BekiOptions().SpreadConcurrency);
    }

    // =======================================================================================
    // F12 — adopted spreads carry their QA records to the pipeline
    // =======================================================================================

    [Fact]
    public async Task Adopted_spreads_hand_the_pipeline_their_stored_QA_records()
    {
        var world = new PackWorld();
        world.SeedStoredBook(qaFor: [1, 2, 3]);

        await world.Run();

        var resume = world.Generator.Resume!;

        // Every stored page was adopted…
        Assert.Equal(BookFormat.SpreadCount, resume.Spreads.Count);

        // …and exactly the pages with a stored verdict carry one, byte for byte.
        Assert.Equal([1, 2, 3], resume.SpreadQaJson.Keys.OrderBy(page => page));
        Assert.Equal(PackWorld.StoredQa(2), resume.SpreadQaJson[2]);
    }

    /// <summary>
    /// A book stored before the records existed hands over an EMPTY map — which the pipeline reads
    /// as "this caller keeps no QA", the branch a pre-campaign book is meant to take — rather than a
    /// map that would redraw every page.
    /// </summary>
    [Fact]
    public async Task A_stored_book_with_no_QA_records_hands_over_none()
    {
        var world = new PackWorld();
        world.SeedStoredBook(qaFor: []);

        await world.Run();

        Assert.Equal(BookFormat.SpreadCount, world.Generator.Resume!.Spreads.Count);
        Assert.Empty(world.Generator.Resume.SpreadQaJson);
    }

    // =======================================================================================
    // F14 — the claim is a compare-and-set
    // =======================================================================================

    [Fact]
    public async Task A_claim_that_loses_to_another_writer_does_no_work_and_overwrites_nothing()
    {
        var world = new PackWorld();

        // Between the job's read and its claim, the sweep buries the book.
        world.Packs.BeforeClaim = () => world.Packs.Force(
            AdventurePackStatus.Failed, "GENERATION_STALLED: swept", "https://blob.test/earlier.pdf");

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        // The sweep's verdict — status, reason and the columns beside them — is untouched.
        Assert.Equal(AdventurePackStatus.Failed, world.Packs.Status);
        Assert.Equal("GENERATION_STALLED: swept", world.Packs.ErrorMessage);
        Assert.Equal("https://blob.test/earlier.pdf", world.Packs.PdfUrl);

        // Nothing was drawn, stored, failed or paged.
        Assert.Equal(0, world.Generator.IllustrateCalls);
        Assert.Empty(world.Blobs.Uploaded);
        Assert.Null(world.Packs.FailureReason);
        Assert.Equal(0, world.Notifier.Notifications);
    }

    // =======================================================================================
    // F19 — bytes this run already holds are not fetched back
    // =======================================================================================

    /// <summary>
    /// The photograph is downloaded once and the finals not at all.
    ///
    /// Two round trips the job used to make against arrays it was already holding: the manifest's
    /// private reference re-downloaded the portrait purely to hash it, and render validation
    /// re-downloaded each final immediately after uploading it. Neither can return anything
    /// different from what is in hand — the uploader was handed that exact array — so both were
    /// latency spent to learn nothing, on the tail of a job a parent is watching.
    ///
    /// A final this run did NOT produce is still fetched from storage; that path is not exercised
    /// here because print preparation refuses on the harness's stub bytes, which is the same
    /// reason the two press finals never reach validation at all.
    /// </summary>
    [Fact]
    public async Task Nothing_this_run_is_already_holding_is_downloaded_again()
    {
        var world = new PackWorld();

        await world.Run();

        var downloads = world.Blobs.Downloaded.ToList();

        // Once, for the illustrator. The manifest's private reference hashes the same array.
        Assert.Equal(1, downloads.Count(url => url == PackWorld.PhotoUrl));

        // Corrupt customer bytes are never stored or published by the fallback.
        var readingPdf = BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId);
        Assert.DoesNotContain(readingPdf, world.Blobs.Uploaded.Keys);
        Assert.DoesNotContain(readingPdf, downloads);

        Assert.Contains(BekiPackBlobs.ManifestName(world.UserId, world.PackId), world.Blobs.Uploaded.Keys);
    }

    // =======================================================================================
    // Harness
    // =======================================================================================

    internal sealed class PackWorld
    {
        public const string PreviewCoverUrl = "https://blob.test/master-runs/preview/cover";

        /// <summary>Where the child's photograph is stored, as the preview run recorded it.</summary>
        public const string PhotoUrl = "https://blob.test/portrait.png";

        /// <summary>The anchor the stubbed pipeline announces, distinct from anything else in storage.</summary>
        public static readonly byte[] AnnouncedAnchor = [0x89, (byte)'P', (byte)'N', (byte)'G', 42, 42, 42];

        public Guid PackId { get; } = Guid.NewGuid();

        public Guid RunId { get; } = Guid.NewGuid();

        public Guid UserId { get; } = Guid.NewGuid();

        public FakePacks Packs { get; }

        public FakeBlobs Blobs { get; } = new();

        public RecordingComposer Composer { get; } = new();

        public CountingNotifier Notifier { get; } = new();

        public RecordingEmailService Email { get; } = new();

        public RecordingAlarms Alarms { get; } = new();

        public ManualTimeProvider Clock { get; } = new();

        /// <summary>The paid order behind this book — what the stored-art paths read the plan from.</summary>
        public FakeOrders Orders { get; }

        /// <summary>The queue a redraw hands the fulfilment job back to.</summary>
        public RecordingJobs Jobs { get; } = new();

        /// <summary>Whether the stubbed illustrator announces an anchor the way the real pipeline does.</summary>
        public bool AnnounceAnchor { get; init; }

        public bool TestingFlow { get; init; }

        public bool WrapFails { get; init; }

        /// <summary>The wrap waits on its token until somebody cancels it.</summary>
        public bool WrapHangs { get; init; }

        /// <summary>Thrown by the illustrator after it has announced the anchor.</summary>
        public Exception? SpreadsFail { get; init; }

        /// <summary>The upscaler fires every clock and then waits on its token — the press tail stalls.</summary>
        public bool PressStalls { get; init; }

        /// <summary>The upscaler fires the JOB's clock alone and answers normally.</summary>
        public bool JobClockFiresDuringPress { get; init; }

        public bool MalformedUpscale { get; init; }

        public bool WaiverAlarmFails { get; init; }

        public PackWorld()
        {
            Packs = new FakePacks(new AdventurePack
            {
                Id = PackId,
                UserId = UserId,
                Theme = ThemeType.Dinosaurs,
                Status = AdventurePackStatus.StoryReady,
                CoverImageUrl = PreviewCoverUrl,
                CreatedAt = DateTime.UtcNow,
                // What this book actually is. It was left at the default, which made the row say
                // "legacy" about a pack the composite job draws — invisible until something asked
                // the pack which pipeline it belonged to, as an operator's redraw does.
                GenerationPipeline = GenerationPipelines.Beki,
            });
            Orders = new FakeOrders(PackId, RunId);
        }

        public async Task Run()
        {
            await Job().ProcessAsync(PackId, RunId, CancellationToken.None);

            Assert.StartsWith("PRINT_PREFLIGHT_FAILED", Packs.FailureReason);
        }

        private StubGenerator? _generator;

        public StubGenerator Generator => _generator ??= new StubGenerator(this);

        /// <param name="strictPolicy">
        /// Judge under <see cref="BekiReleasePolicySnapshot.Strict"/> instead of the shipped
        /// defaults — every check a blocker, which is the cheapest way to reach a verdict that
        /// withholds the customer PDF.
        /// </param>
        /// <param name="compositePipeline">Off is a book from the previous pipeline.</param>
        public BekiPackFulfillment Job(bool strictPolicy = false, bool compositePipeline = true, bool? testingFlow = null,
            IMasterStoryRunRepository? runs = null, IFastPreviewService? fastPreview = null) =>
            new(Packs,
                runs ?? new FakeRuns(RunId, UserId),
                Blobs,
                Generator,
                Composer,
                Notifier,
                Email,
                new SingleUserRepository(),
                Options.Create(new BekiOptions { CompositePipelineEnabled = compositePipeline, InsertBekiInGeneration = false, TestingFlow = testingFlow ?? TestingFlow }),
                NullLogger<BekiPackFulfillment>.Instance,
                Clock,
                pressUpscaler: new ScriptedUpscaler(this),
                releasePolicy: strictPolicy ? new StrictPolicy() : null,
                alarms: Alarms,
                reconciliation: WaiverAlarmFails ? new BrokenWaiverAlarms() : null,
                orders: Orders, fastPreview: fastPreview);

        /// <summary>
        /// An operator's redraw of THIS book, over the same storage and the same pack row.
        ///
        /// No lock is passed on purpose: both services fall back to
        /// <see cref="InProcessBekiPackLock"/>, whose dictionary is static, so a redraw and a
        /// re-preparation of the same book meet on one semaphore exactly as they meet on one
        /// Hangfire lock in the deployment. Wiring a lock in here would prove only that a lock
        /// somebody passed works.
        /// </summary>
        public BekiRegeneration Redraw() =>
            new(Packs,
                Orders,
                new FakeRuns(RunId, UserId),
                Blobs,
                Alarms,
                Jobs,
                Options.Create(new BekiOptions()),
                NullLogger<BekiRegeneration>.Instance,
                Clock);

        public static string StoredQa(int page) => $$"""
            {"page": {{page}}, "qa_prompt_version": "{{CompositeMinimalQa.Version}}",
             "status": "PASS", "recommended_action": "ship"}
            """;

        /// <summary>
        /// The cover the parent previewed, in storage under the preview run: the six artifacts a
        /// print-format preview leaves behind when it draws the REAL cover.
        ///
        /// Not part of the default world, deliberately. A run stored before this feature existed has
        /// none of them, and every other test in this file is about the book that draws its own
        /// wrap — which must go on behaving exactly as it did.
        /// </summary>
        public void SeedPreviewCover()
        {
            Blobs.Seed(BekiRunBlobs.CoverWrapBaseName(RunId), PreviewWrapFixture.BasePng);
            Blobs.Seed(BekiRunBlobs.CoverWrapCompositeName(RunId), PreviewWrapFixture.CompositePng);
            Blobs.Seed(
                BekiRunBlobs.CoverCompositionName(RunId),
                Encoding.UTF8.GetBytes(PreviewWrapFixture.ReceiptJson));
            Blobs.Seed(
                BekiRunBlobs.CoverWrapGenerationName(RunId),
                Encoding.UTF8.GetBytes(PreviewWrapFixture.GenerationJson));
            Blobs.Seed(
                BekiRunBlobs.ScenarioName(RunId),
                Encoding.UTF8.GetBytes(PreviewWrapFixture.ScenarioJson));
            Blobs.Seed(
                BekiRunBlobs.IdentitySpecName(RunId),
                Encoding.UTF8.GetBytes(PreviewWrapFixture.IdentityJson));
        }

        /// <summary>
        /// A whole earlier attempt in storage: eight spreads with their bases and receipts, the
        /// scenario, the identity spec, and a QA record for the pages asked for.
        /// </summary>
        public void SeedStoredBook(IReadOnlyList<int> qaFor)
        {
            var entries = new List<BekiFulfillmentManifestEntry>();
            var compositions = new List<BekiCompositionManifestEntry>();

            for (var page = 1; page <= BookFormat.SpreadCount; page++)
            {
                var spreadName = BekiPackBlobs.SpreadName(UserId, PackId, page);
                var baseName = BekiPackBlobs.SpreadBaseName(UserId, PackId, page);
                var receiptName = BekiPackBlobs.CompositionManifestName(UserId, PackId, page);

                Blobs.Seed(spreadName, [(byte)page]);
                Blobs.Seed(baseName, [(byte)page, 0]);
                Blobs.Seed(receiptName, Encoding.UTF8.GetBytes(PressReceipt([(byte)page, 0], [(byte)page])));

                entries.Add(new BekiFulfillmentManifestEntry(page, $"https://blob.test/{spreadName}"));
                compositions.Add(new BekiCompositionManifestEntry(
                    page, $"https://blob.test/{receiptName}", "pose_01_neutral_hover",
                    new string('a', 64), $"https://blob.test/{baseName}"));

                if (qaFor.Contains(page))
                {
                    Blobs.Seed(BekiPackBlobs.SpreadQaName(UserId, PackId, page), Encoding.UTF8.GetBytes(StoredQa(page)));
                }
            }

            var scenarioName = BekiPackBlobs.ScenarioName(UserId, PackId);
            var identityName = BekiPackBlobs.IdentitySpecName(UserId, PackId);

            Blobs.Seed(scenarioName, Encoding.UTF8.GetBytes(StubGenerator.ScenarioJson));
            Blobs.Seed(identityName, Encoding.UTF8.GetBytes(
                CompositeChildIdentity.ToStoredJson(CompositePipelineTestBase.IdentityFixture)));

            var manifest = new BekiFulfillmentManifest
            {
                IllustrationContract = BekiFulfillmentManifest.CurrentContract(
                    BookFormat.SpreadCount, BekiCompositeContractTerms.Current("dinosaurs")),
                Entries = entries,
                Compositions = compositions,
                ScenarioUrl = $"https://blob.test/{scenarioName}",
                IdentitySpecUrl = $"https://blob.test/{identityName}",
            };

            Blobs.Seed(
                BekiPackBlobs.ManifestName(UserId, PackId),
                JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
    }

    /// <summary>
    /// The illustrator, stubbed at the two seams under test: what it announces, and what the job
    /// handed it to resume from.
    /// </summary>
    internal sealed class StubGenerator(PackWorld world) : IBekiBookGenerator
    {
        public static readonly byte[] WrapComposite = [0x89, (byte)'P', (byte)'N', (byte)'G', 7, 7];

        public static string ScenarioJson => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "nina_dinosaurs", "visual_scenario_output_v2.json"));

        private readonly TaskCompletionSource _wrapEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int IllustrateCalls { get; private set; }

        public int WrapCalls { get; private set; }

        public bool WrapStartedDuringIllustrate { get; private set; }

        public bool WrapTokenCancelled { get; private set; }

        public ChildIdentitySpec? WrapIdentity { get; private set; }

        public byte[]? WrapAnchor { get; private set; }

        /// <summary>What the job handed this run to resume from.</summary>
        public CompositeResumeState? Resume { get; private set; }

        /// <summary>
        /// The whole context the job built for this book.
        ///
        /// Recorded because the adoption of a previewed cover is visible here and nowhere else: the
        /// wrap's base arrives as the anchor spread's own appearance reference, and the two adopted
        /// documents arrive on the resume state.
        /// </summary>
        public CompositeBookContext? Context { get; private set; }

        public Task<BekiBookResult> GenerateAsync(
            MasterStoryInput input, byte[] childPhoto, string childPhotoContentType,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BekiImageResult> DrawCoverAsync(
            MasterStory plan, byte[] childPhoto, string childPhotoContentType,
            CancellationToken cancellationToken, CompositeBookContext? composite = null) =>
            throw new NotSupportedException("the composite path must not ask for a reader-facing cover.");

        public async Task<CompositeCoverWrap> DrawCoverWrapAsync(
            VisualScenarioV2 scenario, byte[] childPhoto, string childPhotoContentType,
            CompositeBookContext composite, ChildIdentitySpec identity, byte[]? childAnchor,
            CancellationToken cancellationToken)
        {
            WrapCalls++;
            WrapIdentity = identity;
            WrapAnchor = childAnchor;
            _wrapEntered.TrySetResult();

            if (world.WrapHangs)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    WrapTokenCancelled = true;
                    throw;
                }
            }

            if (world.WrapFails)
            {
                throw new BekiLayoutException(
                    CompositeFailureCodes.LayoutFailed,
                    "the cover wrap could not be drawn in this deployment.");
            }

            var receipt = PressReceipt([0x89, (byte)'P', (byte)'N', (byte)'G', 1], WrapComposite);

            return new CompositeCoverWrap(
                [0x89, (byte)'P', (byte)'N', (byte)'G', 1], WrapComposite, receipt,
                "pose_01_neutral_hover", "wrap prompt");
        }

        public async Task<BekiBookResult> IllustrateAsync(
            MasterStory plan, byte[] childPhoto, string childPhotoContentType, byte[]? existingCover,
            Func<BekiImageResult, Task>? onImage, CancellationToken cancellationToken,
            IReadOnlyDictionary<int, byte[]>? existingSpreads = null,
            CompositeBookContext? composite = null)
        {
            IllustrateCalls++;
            Resume = composite?.Resume;
            Context = composite;

            if (world.AnnounceAnchor && composite?.OnAnchorAccepted is { } announce)
            {
                var scenario = VisualScenarioValidator.Validate(ScenarioJson).Scenario!;

                await announce(new CompositeAnchorAccepted(
                    scenario, CompositePipelineTestBase.IdentityFixture, PackWorld.AnnouncedAnchor));

                // The job is expected to have started the wrap inside the hook: this waits for the
                // wrap to be ENTERED while the illustrator is still running, which is the claim.
                try
                {
                    await _wrapEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                    WrapStartedDuringIllustrate = true;
                }
                catch (TimeoutException)
                {
                    WrapStartedDuringIllustrate = false;
                }
            }

            if (world.SpreadsFail is { } failure)
            {
                throw failure;
            }

            var spreads = Enumerable.Range(1, world.TestingFlow ? BekiOptions.TestingSpreadCount : BookFormat.SpreadCount)
                .Select(number => new BekiImageResult
                {
                    SpreadNumber = number,
                    Image = [(byte)number],
                    Accepted = true,
                    Verdict = "PASS (pass)",
                    Attempts = 1,
                    Prompt = "spread prompt",
                })
                .ToList();

            return new BekiBookResult
            {
                Plan = plan,
                AppearanceDescription = string.Empty,
                Cover = new BekiImageResult
                {
                    Image = existingCover ?? [],
                    Accepted = true,
                    Verdict = "not drawn here",
                    Attempts = 0,
                    Prompt = string.Empty,
                },
                Spreads = spreads,
                Warnings = [],
                Composite = new CompositeBookArtifacts
                {
                    ScenarioJson = ScenarioJson,
                    ReviewJson = """{"needs_human_reading": false}""",
                    Identity = CompositePipelineTestBase.IdentityFixture,
                    Anchor = [1, 2, 3, 4],
                    Spreads = spreads
                        .Select(spread => new CompositeSpreadArtifact(
                            spread.SpreadNumber!.Value, "pose_01_neutral_hover",
                            PressReceipt([(byte)spread.SpreadNumber.Value, 0], spread.Image),
                            new string('0', 64), BasePng: [(byte)spread.SpreadNumber.Value, 0])
                        {
                            QaJson = PackWorld.StoredQa(spread.SpreadNumber!.Value),
                        })
                        .ToList(),
                },
            };
        }
    }

    /// <summary>
    /// The shipped preparer, with a clock hook in front of it.
    ///
    /// It used to be an unconfigured external super-resolver, which is no longer a state this
    /// product has: the deterministic normalizer is always available and always runs. So this
    /// delegates to the real one and keeps only what the tests actually script — a stage that
    /// stalls until a clock fires, and a stage that returns bytes nothing can decode.
    /// </summary>
    internal sealed class ScriptedUpscaler(PackWorld world) : IPressUpscaler
    {
        public bool IsConfigured => true;

        private readonly DeterministicLanczosNormalizer _real = new();

        public async Task<PressUpscaleResult> UpscaleAsync(
            byte[] png, int targetWidth, int targetHeight, CancellationToken cancellationToken)
        {
            if (world.PressStalls)
            {
                world.Clock.FireAll();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (world.JobClockFiresDuringPress)
            {
                world.Clock.FireFirst();
            }

            if (world.MalformedUpscale)
                return new PressUpscaleResult(true, [1, 2, 3], "broken-test-normalizer", 4d,
                    1, 1, targetWidth, targetHeight, null);

            return await _real.UpscaleAsync(png, targetWidth, targetHeight, cancellationToken);
        }
    }

    /// <summary>A timer nobody has to wait for — see GenerationBudgetTests for the original.</summary>
    internal sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            lock (_timers)
            {
                _timers.Add(timer);
            }

            return timer;
        }

        /// <summary>Every deadline passes, now.</summary>
        public void FireAll()
        {
            ManualTimer[] snapshot;
            lock (_timers)
            {
                snapshot = _timers.ToArray();
            }

            foreach (var timer in snapshot)
            {
                timer.Fire();
            }
        }

        /// <summary>The earliest-created deadline — the job's own — passes, and no other.</summary>
        public void FireFirst()
        {
            ManualTimer? first;
            lock (_timers)
            {
                first = _timers.FirstOrDefault();
            }

            first?.Fire();
        }

        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            public void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    internal sealed class BrokenWaiverAlarms : IBekiReleaseReconciliation
    {
        public Task RaiseWaiverAlarmsAsync(Guid packId, Guid userId, Guid? orderId,
            BekiReleaseGateReport report, CancellationToken ct) =>
            throw new IOException("Test waiver alarm service unavailable");
        public Task<BekiReconcileResult> ReconcilePackAsync(Guid packId, string reason, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<int> ReconcileWithheldAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BekiPublishOutcome> PublishUnlockedFilesAsync(
            AdventurePack pack, BekiReleaseGateReport report, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    /// <summary>The background queue, counted rather than run — nothing here draws anything.</summary>
    internal sealed class RecordingJobs : Hangfire.IBackgroundJobClient
    {
        public int Enqueued { get; private set; }

        public string Create(Hangfire.Common.Job job, Hangfire.States.IState state)
        {
            Enqueued++;
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, Hangfire.States.IState state, string? expectedState) => true;
    }

    internal sealed class RecordingAlarms : IBekiAlarmService
    {
        public bool ThrowOnRaise { get; set; }
        public Task<IReadOnlyList<BekiAlarm>> ListRecentAsync(int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<BekiAlarm?> GetAsync(Guid alarmId, CancellationToken ct) => throw new NotSupportedException();
        public List<BekiAlarmRaise> Raised { get; } = [];

        public Task RaiseAsync(BekiAlarmRaise raise, CancellationToken ct)
        {
            Raised.Add(raise);
            if (ThrowOnRaise) throw new IOException("Test alarm service unavailable");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>([]);

        public Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>([]);

        public Task<bool> ReviewAsync(Guid alarmId, string reviewedBy, string resolution, CancellationToken ct) =>
            throw new NotSupportedException();

        /// <summary>Every automatic closure this run asked for: which book, which check, and why.</summary>
        public List<(Guid PackId, string CheckId, string Resolution)> Resolved { get; } = [];

        public Task ResolveForPackAsync(Guid packId, string checkId, string resolution, CancellationToken ct)
        {
            Resolved.Add((packId, checkId, resolution));
            return Task.CompletedTask;
        }

        public Task<int> CountOpenAsync(CancellationToken ct) => Task.FromResult(Raised.Count);
    }

    internal sealed class RecordingComposer : IBekiPdfComposer
    {
        public string? RestoredLayout { get; private set; }
        public void RestorePreviewLayout(string title, byte[] wrap, string json) => RestoredLayout = json;

        public bool LastPrepareForPrint { get; private set; }
        public bool LastTestingFlow { get; private set; }
        public int LastSpreadCount { get; private set; }
        public byte[] CanonicalPdf { get; set; } = [0x25, 0x50, 0x44, 0x46];
        public byte[]? ReadingWrap { get; private set; }

        /// <summary>
        /// Runs inside composition, which is the one place a test can stand while the press stage
        /// is between "started" and "has written anything" — where the concurrency guard lives.
        /// </summary>
        public Action? OnCompose { get; set; }

        // Deliberately corrupt PDF: print trouble must reach customer validation, which must
        // still refuse invalid bytes instead of marking this stubbed book Completed.
        public BekiComposedBook ComposeCanonicalWithReceipts(
            MasterStory plan, byte[] wrapComposite, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null)
        {
            OnCompose?.Invoke();
            LastPrepareForPrint = personalization!.PrepareForPrint;
            LastTestingFlow = personalization.TestingFlow;
            LastSpreadCount = spreads.Count;
            return new BekiComposedBook(CanonicalPdf, Receipts("canonical"));
        }

        public BekiComposedBook ComposeWithReceipts(
            MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null) =>
            new([0x25, 0x50, 0x44, 0x46], Receipts("press"));

        public BekiComposedBook ComposeReading(
            MasterStory plan, byte[] wrapComposite, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null)
        {
            ReadingWrap = wrapComposite;
            return new BekiComposedBook([0x25, 0x50, 0x44, 0x46], Receipts("reading"));
        }

        public BekiComposedBook ComposeInteriorWithReceipts(
            MasterStory plan, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null) =>
            new([0x25, 0x50, 0x44, 0x46, 0x2D], Receipts("interior"));

        public BekiComposedBook ComposeCoverPressWithReceipts(string title, byte[] wrapComposite) =>
            new([0x25, 0x50, 0x44, 0x46, 0x2D], Receipts("cover"));

        public byte[] CropFrontBoard(byte[] wrapPng) => [.. wrapPng, 0xF1];

        public byte[] CropBackBoard(byte[] wrapPng) => [.. wrapPng, 0xB1];

        public IReadOnlyList<byte[]> RenderPages(
            MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null) => throw new NotSupportedException();

        /// <summary>
        /// The receipts this composer returns for every mode — internal because the rollback list
        /// is built from a candidate's receipts, so a test asking "is every published name covered"
        /// has to be able to build the same ones.
        /// </summary>
        internal static BekiLayoutReceipts Receipts(string mode) => new(
            mode,
            new[] { "cover-front", "endpaper-front", "intro", "credits", "endpaper-rear", "cover-back" }
                .Select((role, index) => new BekiLayoutPageReceipt(
                    index + 1, role, 220, 200, 0,
                    ImageSha256: [new string('e', 64)],
                    Wash: null,
                    Typography: [new BekiTypographyRecord(role, "Noto Sans Georgian", 12, 1.3, "#241A33")],
                    TextLines: ["ერთი სტრიქონი"],
                    TextProbe: null))
                .ToList());
    }

    /// <summary>A blob store that remembers what it was given, and what it was asked for.</summary>
    internal sealed class FakeBlobs : IBlobStorageService
    {
        public bool FailPrintStatus { get; set; }
        public int PrintStatusFailures { get; private set; }

        /// <summary>
        /// Storage that refuses a named blob, armed by the test whenever it likes.
        ///
        /// A predicate rather than a set of names because the interesting failures are ordered:
        /// "this upload works, and the SAME name fails when the rollback comes back to it" is the
        /// only way to reach a book whose canonical PDF has been replaced by a candidate that then
        /// could not be undone.
        /// </summary>
        public Func<string, bool>? FailUpload { get; set; }
        public ConcurrentDictionary<string, byte[]> Uploaded { get; } = new(StringComparer.Ordinal);

        public ConcurrentBag<string> Downloaded { get; } = [];

        /// <summary>
        /// Every upload in the order it happened, which the dictionary cannot say. What it is for
        /// is asking a finished operation "which names did you write?" — the question behind the
        /// rollback list, where a document the publish step writes and the snapshot does not know
        /// about is the whole defect.
        /// </summary>
        public ConcurrentQueue<string> UploadOrder { get; } = new();

        /// <summary>What was deleted, in order. Seeded blobs really do disappear.</summary>
        public ConcurrentQueue<string> Deleted { get; } = new();

        public void Seed(string blobName, byte[] bytes) => Uploaded[blobName] = bytes;

        public Task<string> UploadAsync(
            string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken)
        {
            if (FailPrintStatus && blobName.EndsWith("-press-status.json", StringComparison.Ordinal))
            {
                PrintStatusFailures++;
                throw new IOException("Test print status storage unavailable");
            }
            if (FailUpload?.Invoke(blobName) == true)
            {
                throw new IOException("Test blob storage unavailable for " + blobName);
            }
            Uploaded[blobName] = bytes;
            UploadOrder.Enqueue(blobName);
            return Task.FromResult($"https://blob.test/{blobName}");
        }

        public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken)
        {
            Downloaded.Add(blobName);
            return Task.FromResult<Stream>(new MemoryStream(
                Uploaded.TryGetValue(blobName, out var bytes) ? bytes : []));
        }

        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult(Uploaded.ContainsKey(blobName));

        public Task<byte[]> DownloadBytesFromStoredUrlAsync(
            string storedUrl, CancellationToken cancellationToken)
        {
            Downloaded.Add(storedUrl);

            var name = storedUrl.Replace("https://blob.test/", string.Empty, StringComparison.Ordinal);

            return Task.FromResult(Uploaded.TryGetValue(name, out var bytes) ? bytes : [1, 1, 1, 1]);
        }

        /// <summary>
        /// A delete that really deletes. It used to answer true and keep the bytes, which made
        /// every "and then it was gone" assertion unfalsifiable — including the rollback's removal
        /// of a receipt the book never had.
        /// </summary>
        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken)
        {
            var name = storedUrl.Replace("https://blob.test/", string.Empty, StringComparison.Ordinal);
            Deleted.Enqueue(name);
            return Task.FromResult(Uploaded.TryRemove(name, out _));
        }

        // No companion kept: the rendition is an optimisation, and its absence is the
        // ordinary answer the first time anybody asks.
        public Task<byte[]?> TryDownloadBesideAsync(
            string storedUrl, string suffix, CancellationToken cancellationToken) =>
            Task.FromResult<byte[]?>(null);

        public Task UploadBesideAsync(
            string storedUrl, string suffix, byte[] bytes, string contentType,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>The pack row: compare-and-set the way the real one is, remembering its progress line.</summary>
    internal sealed class FakePacks(AdventurePack seed) : IAdventurePackRepository
    {
        private readonly AdventurePack _pack = seed;
        private readonly object _gate = new();
        private readonly List<(string? Message, int? Percent)> _progress = [];

        public string? CoverImageUrl => _pack.CoverImageUrl;

        public string? PdfUrl => _pack.PdfUrl;

        public string? ErrorMessage => _pack.ErrorMessage;

        public AdventurePackStatus Status => _pack.Status;

        public string? PrintPdfUrl { get; private set; }

        public bool PrintPdfUrlWritten { get; private set; }

        public string? FailureReason { get; private set; }

        /// <summary>Runs inside the claim, after the job has decided what it expects to find.</summary>
        public Action? BeforeClaim { get; set; }

        public IReadOnlyList<(string? Message, int? Percent)> Progress
        {
            get
            {
                lock (_gate)
                {
                    return _progress.ToList();
                }
            }
        }

        public void Force(AdventurePackStatus status, string? error, string? pdfUrl)
        {
            _pack.Status = status;
            _pack.ErrorMessage = error;
            _pack.PdfUrl = pdfUrl;
        }

        public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<AdventurePack?>(_pack);

        /// <summary>The unconditional write, which the job must not use to claim a pack any more.</summary>
        public Task<bool> UpdateStatusAsync(
            Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl,
            string? errorMessage, CancellationToken cancellationToken) =>
            throw new NotSupportedException("the claim must be a compare-and-set.");

        public Task<bool> TryUpdateStatusAsync(
            Guid id, AdventurePackStatus expected, AdventurePackStatus status, string? generatedJson,
            string? pdfUrl, string? errorMessage, CancellationToken cancellationToken)
        {
            if (status == AdventurePackStatus.GeneratingStory)
            {
                BeforeClaim?.Invoke();
            }

            if (_pack.Status != expected)
            {
                return Task.FromResult(false);
            }

            _pack.Status = status;
            _pack.PdfUrl = pdfUrl;
            _pack.GeneratedJson = generatedJson;
            _pack.ErrorMessage = errorMessage;
            return Task.FromResult(true);
        }

        public Task<bool> TryFailAsync(
            Guid id, AdventurePackStatus expectedStatus, string errorMessage,
            CancellationToken cancellationToken)
        {
            if (_pack.Status != expectedStatus)
            {
                return Task.FromResult(false);
            }

            FailureReason = errorMessage;
            _pack.Status = AdventurePackStatus.Failed;
            _pack.ErrorMessage = errorMessage;
            return Task.FromResult(true);
        }

        public Task UpdateProgressAsync(
            Guid id, string? progressMessage, int? progressPercent, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _progress.Add((progressMessage, progressPercent));
            }

            return Task.CompletedTask;
        }

        public Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken)
        {
            PrintPdfUrl = printPdfUrl;
            PrintPdfUrlWritten = true;
            return Task.CompletedTask;
        }

        public Task UpdateBookPresentationAsync(
            Guid id, string? title, string? coverImageUrl, CancellationToken cancellationToken)
        {
            _pack.Title = title ?? _pack.Title;
            _pack.CoverImageUrl = coverImageUrl ?? _pack.CoverImageUrl;
            return Task.CompletedTask;
        }

        public Task SetGenerationPipelineAsync(Guid id, string pipeline, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AdventurePack>> ListWithheldBekiPacksAsync(
            int limit, BekiWithheldCursor? after, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdventurePack>>([]);

        // Everything else the interface declares and this job never calls.
        public Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(Guid characterId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetAccessLevelAsync(Guid id, BookAccessLevel accessLevel, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(DateTime cutoffUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryFailStaleGenerationAsync(Guid id, AdventurePackStatus expected, DateTime cutoffUtc, string errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdatePreviewIllustrationAsync(Guid id, PreviewIllustrationStatus status, string? illustrationUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryClaimPreviewIllustrationGenerationAsync(Guid id, int staleAfterMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateGeneratedJsonAsync(Guid id, string generatedJson, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    internal sealed class FakeRuns(Guid runId, Guid userId) : IMasterStoryRunRepository
    {
        public Task SaveAppearanceDescriptionAsync(Guid id, string appearanceDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        private readonly MasterStoryRun _run = new()
        {
            Id = runId,
            // The owner, which is what makes the run the one this pack's paid order bought: the
            // stored-art paths refuse a plan that belongs to somebody else.
            UserId = userId,
            ChildName = "ნინა",
            Age = 5,
            Gender = "girl",
            Theme = nameof(ThemeType.Dinosaurs),
            SpreadCount = BookFormat.SpreadCount,
            StoryLanguage = "ka",
            PhotoBlobUrl = PackWorld.PhotoUrl,
            CoverImageUrl = PackWorld.PreviewCoverUrl,
            StoryJson = JsonSerializer.Serialize(Plan(), StoryJson.Options),
            // A printing plan, which is what the composite job is given. Stated because a redraw
            // asks the run this question before it will queue anything.
            PromptVersion = "v5",
        };

        public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<MasterStoryRun?>(_run);

        public Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<MasterStoryRunProgress?>(null);
        public Task SetProgressAsync(Guid id, string status, string? progressMessage, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SavePromptsAsync(Guid id, string model, string promptVersion, string systemPrompt, string userPrompt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveStoryAsync(Guid id, string storyJson, string contentJson, int promptTokens, int completionTokens, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<int> AttachToUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(1);
        public Task<IReadOnlyList<MasterStoryRunSummary>> ListUnboughtForUserAsync(
            Guid userId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MasterStoryRunSummary>>([]);
        public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExpiredMasterStoryRun>>([]);
        public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    /// <summary>
    /// The one paid order behind this book, pointing at the preview run whose plan it bought.
    ///
    /// It exists because the stored-art paths (recovery and print re-preparation) find the plan the
    /// same way the real system does — through the order's draft — rather than through a shortcut
    /// that would not exist in production.
    /// </summary>
    internal sealed class FakeOrders(Guid packId, Guid runId) : IOrderRepository
    {
        public Guid? CoverRevisionId { get; set; }
        public Guid OrderId { get; } = Guid.NewGuid();

        public Task<IReadOnlyList<Order>> GetPaidForBookAsync(Guid bookId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Order>>(bookId == packId
                ? [new Order
                    {
                        Id = OrderId,
                        BookId = packId,
                        Status = OrderStatus.Paid,
                        DraftJson = JsonSerializer.Serialize(
                            new { previewBookId = runId, coverRevisionId = CoverRevisionId }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    }]
                : []);

        public Task<Guid> CreateAsync(Order order, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Order?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Order?> GetByProviderSessionIdAsync(string providerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AttachProviderSessionAsync(Guid id, string providerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetBookIdAsync(Guid id, Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryMarkPaidAsync(Guid id, string? providerPaymentIntentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryMarkFulfilledAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkFailedAsync(Guid id, string reason, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryCancelAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetStalledPaidAsync(DateTime paidBeforeUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Every check a blocker — the shipped Strict board, read as a policy service.</summary>
    internal sealed class StrictPolicy : IBekiReleasePolicyService
    {
        public Task<BekiReleasePolicySnapshot> SnapshotAsync(CancellationToken ct) =>
            Task.FromResult(BekiReleasePolicySnapshot.Strict);

        public Task<IReadOnlyList<BekiReleaseCheckSetting>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiReleaseCheckSetting>>([]);

        public Task<int> SetAsync(
            string checkId, string deliverableClass, string severity, string updatedBy, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    internal sealed class CountingNotifier : IAdminNotifier
    {
        public int Notifications { get; private set; }

        public Task BookFailedAsync(Guid packId, string reason, CancellationToken cancellationToken)
        {
            Notifications++;
            return Task.CompletedTask;
        }

        public Task OrderPaidAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PrintOrderPlacedAsync(PrintOrder printOrder, string? bookTitle, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    internal static string PressReceipt(byte[] basePng, byte[] composite)
    {
        var fixture = JsonSerializer.Deserialize<BekiCompositionManifest>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "nina_dinosaurs", "spread_01_composition_manifest.json")))!;
        return (fixture with
        {
            BaseImage = new BekiCompositionFile
            {
                File = "base.png", Sha256 = Convert.ToHexString(SHA256.HashData(basePng)).ToLowerInvariant(),
            },
            Output = new BekiCompositionFile
            {
                File = "composite.png", Sha256 = Convert.ToHexString(SHA256.HashData(composite)).ToLowerInvariant(),
            },
        }).ToJson();
    }

    private static MasterStory Plan() => new()
    {
        Concept = new StoryConcept
        {
            Title = "ბაფუს ბილიკი",
            Outline = Enumerable.Range(1, BookFormat.SpreadCount).Select(n => $"beat {n}").ToList(),
        },
        Spreads = Enumerable.Range(1, BookFormat.SpreadCount).Select(number => new StorySpread
        {
            Number = number,
            Title = string.Empty,
            Caption = string.Empty,
            Text = $"ნინა და ბეკი - გვერდი {number}.",
            Characters = ["child", "beki"],
            Objects = [],
            Illustration = new IllustrationBrief { Scene = "The child in the valley." },
        }).ToList(),
        CharacterLock = string.Empty,
        Cover = new IllustrationBrief { Scene = "The child at the valley's edge." },
        WorldLock = "A warm golden valley.",
        Cast = [],
        Objects = [],
    };
}


/// <summary>
/// Canonical twelve-page books built offline, in the two states the press stage moves between.
///
/// Shared by the fulfilment suite and the re-preparation suite because they are two halves of one
/// story: a book whose rasters are the generated frames has printing held, and the same book with
/// its rasters normalized does not. Two suites building that from two fixtures would be two suites
/// that can drift apart about what "the same book" means.
/// </summary>
internal static class BekiCanonicalBookFixtures
{
    private static readonly Dictionary<string, byte[]> CanonicalBooks = new(StringComparer.Ordinal);

    private static readonly object CanonicalBooksGate = new();

    /// <summary>
    /// The twelve-page canonical book as it comes out of the composer BEFORE anything normalizes
    /// its rasters: the generated frames, 1536 × 735 px on the cover wrap and 1536 × 717 px on each
    /// spread, which are the exact sizes the stored bases of a real book have.
    ///
    /// Placed on the locked sheets they are the defect PRESS_RESOLUTION exists to name — 87 PPI
    /// where 300 is owed, and a pixel count nowhere near the locked one — and they are the right
    /// shape, so nothing else fires and the gate list says exactly one thing.
    /// </summary>
    internal static byte[] CanonicalScreenBook() => CanonicalBook(1536, 735, 1536, 717);

    /// <summary>
    /// The same book after normalization: every page carries its locked full-sheet raster, 6047 ×
    /// 2894 px on the 512 × 245 mm wrap and 5315 × 2480 px on each 450 × 210 mm spread.
    ///
    /// It exists for one question: when the press stage's own preparation goes wrong but the
    /// document that comes out is nevertheless correct, does anything get held?
    /// </summary>
    internal static byte[] CanonicalPressBook() => CanonicalBook(
        BekiPressRaster.CoverWidthPx, BekiPressRaster.CoverHeightPx,
        BekiPressRaster.InteriorWidthPx, BekiPressRaster.InteriorHeightPx);

    /// <summary>
    /// A canonical twelve-page document with one full-sheet raster per page at the sizes asked for,
    /// the credits QR on page 11, and nothing else the gates care about.
    ///
    /// Cached, because the press-sized variant embeds two thirteen-megapixel rasters and they are
    /// the same two every time. One raster per page and no second image object, because the vector
    /// logo check counts them: the cover owes exactly one.
    /// </summary>
    /// <summary>The press book with the credits QR left off — a document that renders and fails.</summary>
    internal static byte[] CanonicalPressBookWithoutQr() => CanonicalBook(
        BekiPressRaster.CoverWidthPx, BekiPressRaster.CoverHeightPx,
        BekiPressRaster.InteriorWidthPx, BekiPressRaster.InteriorHeightPx, withQr: false);

    private static byte[] CanonicalBook(
        int coverWidthPx, int coverHeightPx, int spreadWidthPx, int spreadHeightPx, bool withQr = true)
    {
        var key = $"{coverWidthPx}x{coverHeightPx}/{spreadWidthPx}x{spreadHeightPx}/{withQr}";

        lock (CanonicalBooksGate)
        {
            if (CanonicalBooks.TryGetValue(key, out var cached)) return cached.ToArray();

            QuestPDF.Settings.License = LicenseType.Community;

            // Flat colour on purpose: the gate measures pixel counts and placement, never content,
            // and a gradient at press size costs seconds of encoding for nothing.
            var wrap = QuestPDF.Infrastructure.Image.FromBinaryData(Flat(coverWidthPx, coverHeightPx));
            var spread = QuestPDF.Infrastructure.Image.FromBinaryData(Flat(spreadWidthPx, spreadHeightPx));

            var pdf = Document.Create(document =>
            {
                for (var number = 1; number <= 12; number++)
                {
                    var page = number;
                    document.Page(descriptor =>
                    {
                        descriptor.Size(page == 1 ? 512f : 450f, page == 1 ? 245f : 210f, Unit.Millimetre);
                        descriptor.Margin(0);
                        descriptor.Content().Layers(layers =>
                        {
                            layers.PrimaryLayer()
                                .Image(page == 1 ? wrap : spread).FitUnproportionally().UseOriginalImage();
                            layers.Layer().PaddingTop(10, Unit.Millimetre).PaddingLeft(10, Unit.Millimetre)
                                .Text($"Offline fixture page {page}").FontSize(20);
                            if (page == 11 && withQr)
                            {
                                layers.Layer().PaddingTop(40, Unit.Millimetre).PaddingLeft(10, Unit.Millimetre)
                                    .Width(40, Unit.Millimetre).Height(40, Unit.Millimetre)
                                    .Svg(QrSvg("https://beki.ge"));
                            }
                        });
                    });
                }
            }).GeneratePdf();

            CanonicalBooks[key] = pdf;
            return pdf.ToArray();
        }
    }

    private static byte[] Flat(int width, int height)
    {
        using var image = new SixLabors.ImageSharp.Image<Rgb24>(width, height, new Rgb24(214, 205, 188));
        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    /// <summary>The credits page's own QR generator, at its own settings.</summary>
    private static string QrSvg(string url)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(url.Trim(), QRCoder.QRCodeGenerator.ECCLevel.Q);
        return new QRCoder.SvgQRCode(data).GetGraphic(
            pixelsPerModule: 16, darkColorHex: "#000000", lightColorHex: "#FFFFFF", drawQuietZones: true);
    }
}
