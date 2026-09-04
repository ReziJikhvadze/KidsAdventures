using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Controllers;
using AdventurePacks.Api.Domain;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Extensions;
using AdventurePacks.Api.DTOs.Orders;
using AdventurePacks.Api.DTOs.Print;
using AdventurePacks.Api.Repositories.Implementations;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// The operations console's own boundary: the filters an operator selects, the two files they can
/// download, the marks they can put on an order by hand, and the one button that spends money.
///
/// Controller and service tests rather than SQL ones, deliberately. Nothing here can reach a
/// database, so every query in the repositories is reviewed rather than executed; what these
/// pin is the layer above it, which is where the decisions live — which flag is accepted, which
/// transition is refused, which blobs a redraw deletes and in what order it claims the pack.
/// </summary>
public class AdminConsoleApiTests
{
    private static readonly Guid Owner = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PackId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OrderId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RunId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    // -- the saved views -------------------------------------------------------------------

    [Theory]
    [InlineData("paid-unfulfilled")]
    [InlineData("needs-attention")]
    [InlineData("generating")]
    [InlineData("stuck")]
    [InlineData("awaiting-review")]
    [InlineData("failed")]
    public async Task Every_saved_view_the_console_offers_reaches_the_query(string flag)
    {
        /*
          The four new filters are the whole point of the screen: an operator opens the console to
          see what is stuck, and a name the API accepts but the SQL does not implement would show
          them the full list — which reads as "nothing is wrong".
        */
        var reporting = new FakeReporting();

        var result = await Controller(reporting).Orders(flag: flag, cancellationToken: default);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(flag, reporting.LastFlag);
    }

    [Fact]
    public async Task A_filter_nobody_implements_is_refused_rather_than_ignored()
    {
        var reporting = new FakeReporting();

        var result = await Controller(reporting).Orders(flag: "on-fire", cancellationToken: default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(0, reporting.Calls);
    }

    [Theory]
    [InlineData("Refunded")]
    [InlineData("refunded")]
    [InlineData("Cancelled")]
    [InlineData("Pending")]
    public async Task Every_order_status_including_the_two_an_operator_writes_is_a_valid_filter(string status)
    {
        // Refunded and Cancelled exist as filters because they are now states this console PRODUCES.
        // A list that cannot be narrowed to them is a list in which yesterday's refunds are lost.
        var reporting = new FakeReporting();

        var result = await Controller(reporting).Orders(status: status, cancellationToken: default);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(status, reporting.LastStatus);
    }

    [Fact]
    public async Task A_status_that_is_not_one_is_a_400_and_never_reaches_SQL()
    {
        var reporting = new FakeReporting();

        var result = await Controller(reporting).Orders(status: "Bananas", cancellationToken: default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(0, reporting.Calls);
    }

    // -- what the detail panel knows ----------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_retry_button_follows_the_services_own_answer(bool canRedrive)
    {
        /*
          The server answers this, never the browser — and the console asks the service rather
          than keeping a copy of its rule. A retry button enabled by a rule the client invented is
          a button that reports "queued" and is then silently declined by the job it queued; the
          fulfilled-and-Failed case (every generation failure happens to an order that already says
          Fulfilled) is exactly the one a copied rule got wrong. The rule itself is pinned where it
          lives, in PaidOrderFulfilmentTests; here only the wiring is under test.
        */
        var reporting = new FakeReporting
        {
            Detail = new AdminOrderDetailResponse
            {
                Order = new AdminOrderRow
                {
                    Id = OrderId,
                    Status = nameof(OrderStatus.Fulfilled),
                    FulfilledAt = DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
                    BookStatus = "Failed",
                    BookId = PackId,
                    Type = nameof(OrderType.NewBook),
                    Package = nameof(OrderPackage.Digital),
                },
            },
        };

        var detail = await Detail(Controller(reporting, canRedrive: canRedrive));

        Assert.Equal(canRedrive, detail.CanRetry);
    }

    [Fact]
    public async Task The_panel_says_which_pictures_exist_rather_than_guessing_from_the_status()
    {
        /*
          The interesting case is precisely the one the status cannot describe: a book that stopped
          on spread five has four pictures an operator can look at and judge, and until now the
          console could only report "Failed".
        */
        var blobs = new FakeBlobs(
        [
            BekiPackBlobs.SpreadName(Owner, PackId, 1),
            BekiPackBlobs.SpreadName(Owner, PackId, 2),
            BekiPackBlobs.SpreadName(Owner, PackId, 5),
            BekiPackBlobs.CoverFrontName(Owner, PackId),
            BekiPackBlobs.ContactSheetName(Owner, PackId, BekiPackBlobs.DigitalRenderArtifact),
        ]);

        var pack = Pack(GenerationPipelines.Beki, AdventurePackStatus.Failed, DateTime.UtcNow);

        var detail = await Detail(Controller(blobs: blobs, packs: new FakePacks(pack)));

        Assert.Equal([1, 2, 5], detail.Book!.SpreadsAvailable);
        Assert.True(detail.Book.HasCoverImage);
        Assert.True(detail.Book.HasContactSheet);
    }

    [Fact]
    public async Task A_book_with_no_pictures_at_all_says_so_without_falling_over()
    {
        var pack = Pack(GenerationPipelines.Beki, AdventurePackStatus.Failed, DateTime.UtcNow);
        pack.CoverImageUrl = null;

        var detail = await Detail(Controller(blobs: new FakeBlobs([]), packs: new FakePacks(pack)));

        Assert.Empty(detail.Book!.SpreadsAvailable);
        Assert.False(detail.Book.HasCoverImage);
        Assert.False(detail.Book.HasContactSheet);
    }

    // -- the two files ---------------------------------------------------------------------

    [Fact]
    public async Task A_digital_order_downloads_the_reading_copy_when_no_kind_is_asked_for()
    {
        var (controller, _) = PdfController(package: OrderPackage.Digital, reading: true, print: true);

        var file = Assert.IsType<FileContentResult>(await controller.OrderPdf(OrderId, null));

        Assert.Equal("reading-bytes", Encoding.UTF8.GetString(file.FileContents));
        Assert.EndsWith("-READING-COPY-not-print.pdf", file.FileDownloadName);
    }

    [Fact]
    public async Task A_print_order_downloads_the_press_file_when_no_kind_is_asked_for()
    {
        // Default print-order downloads require the approved manufacturing slot.
        var (controller, _) = PdfController(package: OrderPackage.Print, reading: true, print: true);

        var file = Assert.IsType<FileContentResult>(await controller.OrderPdf(OrderId, null));

        Assert.Equal("print-bytes", Encoding.UTF8.GetString(file.FileContents));
        Assert.EndsWith("-book.pdf", file.FileDownloadName);
    }

    [Fact]
    public async Task The_kind_overrides_the_package_in_both_directions()
    {
        // The selector enforces independent customer and manufacturing permissions.
        var (digital, _) = PdfController(package: OrderPackage.Digital, reading: true, print: true);
        var (print, _) = PdfController(package: OrderPackage.Print, reading: true, print: true);

        var asPrint = Assert.IsType<FileContentResult>(await digital.OrderPdf(OrderId, "print"));
        var asReading = Assert.IsType<FileContentResult>(await print.OrderPdf(OrderId, "reading"));

        Assert.Equal("print-bytes", Encoding.UTF8.GetString(asPrint.FileContents));
        Assert.Equal("reading-bytes", Encoding.UTF8.GetString(asReading.FileContents));
    }

    [Fact]
    public async Task A_held_print_download_never_substitutes_the_customer_pdf()
    {
        var (controller, _) = PdfController(package: OrderPackage.Print, reading: true, print: false);

        Assert.IsType<ConflictObjectResult>(await controller.OrderPdf(OrderId, "print"));
        Assert.IsType<ConflictObjectResult>(await controller.OrderPdf(OrderId, null));
        var reading = Assert.IsType<FileContentResult>(await controller.OrderPdf(OrderId, "reading"));
        Assert.Equal("reading-bytes", Encoding.UTF8.GetString(reading.FileContents));
        Assert.EndsWith("-READING-COPY-not-print.pdf", reading.FileDownloadName);
    }

    [Fact]
    public async Task A_book_with_neither_file_is_a_409_rather_than_a_404()
    {
        // The book exists and the file does not exist YET, which is a different thing to tell an
        // operator: one of the two has a button next to it.
        var (controller, _) = PdfController(package: OrderPackage.Digital, reading: false, print: false);

        Assert.IsType<ConflictObjectResult>(await controller.OrderPdf(OrderId, null));
    }

    // -- the marks an operator writes by hand -------------------------------------------------

    [Fact]
    public async Task A_refund_is_recorded_against_the_operator_who_made_it()
    {
        var orders = new FakeOrders(Order(OrderStatus.Paid)) { Written = true };
        var controller = Controller(orders: orders);

        var result = await controller.SetOrderStatus(
            OrderId, new AdminSetOrderStatusRequest { Status = "Refunded", Note = "ბანკმა დააბრუნა" }, default);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(OrderStatus.Refunded, orders.LastStatus);

        // The note is a person's sentence and is marked as one: the failure-reason column is the
        // same one generation failures land in, and the two must never be read as each other.
        Assert.StartsWith("admin:operator@beki.ge", orders.LastReason);
        Assert.Contains("ბანკმა დააბრუნა", orders.LastReason);
    }

    [Theory]
    [InlineData(OrderStatus.Paid, "Refunded", true)]
    [InlineData(OrderStatus.Fulfilled, "Refunded", true)]
    [InlineData(OrderStatus.Pending, "Refunded", false)]
    [InlineData(OrderStatus.Cancelled, "Refunded", false)]
    [InlineData(OrderStatus.Pending, "Cancelled", true)]
    [InlineData(OrderStatus.Paid, "Cancelled", true)]
    [InlineData(OrderStatus.Fulfilled, "Cancelled", false)]
    public async Task Only_the_transitions_that_are_true_about_the_money_are_allowed(
        OrderStatus from, string to, bool allowed)
    {
        /*
          Refunded only from Paid or Fulfilled: a refund is a statement about money that was
          actually taken, and marking an unpaid order refunded puts a lie in the ledger.

          Cancelled only from Pending or Paid: a fulfilled order has a book behind it, and
          cancelling that would be a refund with the parent's copy left in their library.
        */
        var orders = new FakeOrders(Order(from)) { Written = true };

        var result = await Controller(orders: orders).SetOrderStatus(
            OrderId, new AdminSetOrderStatusRequest { Status = to }, default);

        if (allowed)
        {
            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, orders.Writes);
        }
        else
        {
            Assert.IsType<ConflictObjectResult>(result);
            Assert.Equal(0, orders.Writes);
        }
    }

    [Fact]
    public async Task The_allowed_set_the_operator_was_told_about_is_the_one_the_write_enforces()
    {
        // Checked twice on purpose: here so the refusal has a sentence attached, and in the UPDATE
        // so two admins clicking at once cannot produce a refund of a cancelled order.
        var orders = new FakeOrders(Order(OrderStatus.Paid)) { Written = true };

        await Controller(orders: orders).SetOrderStatus(
            OrderId, new AdminSetOrderStatusRequest { Status = "Refunded" }, default);

        Assert.Equal([OrderStatus.Paid, OrderStatus.Fulfilled], orders.LastAllowedFrom);
    }

    [Fact]
    public async Task Cancelling_an_order_cancels_the_parcel_that_would_otherwise_be_posted()
    {
        // Without this a cancelled order leaves its parcel in the print queue, and a book is
        // printed and posted to somebody who is not being charged for it.
        var orders = new FakeOrders(Order(OrderStatus.Paid)) { Written = true };
        var parcels = new FakePrintOrders { Cancelled = true };

        await Controller(orders: orders, printOrders: parcels).SetOrderStatus(
            OrderId, new AdminSetOrderStatusRequest { Status = "Cancelled" }, default);

        Assert.Equal(OrderId, parcels.CancelledFor);
    }

    [Fact]
    public async Task A_refund_leaves_the_parcel_alone()
    {
        // A refunded print order may already be with a courier, and the parcel is a record of
        // where it went rather than an instruction. Only a cancellation cascades.
        var orders = new FakeOrders(Order(OrderStatus.Fulfilled)) { Written = true };
        var parcels = new FakePrintOrders();

        await Controller(orders: orders, printOrders: parcels).SetOrderStatus(
            OrderId, new AdminSetOrderStatusRequest { Status = "Refunded" }, default);

        Assert.Null(parcels.CancelledFor);
    }

    [Theory]
    [InlineData("Paid")]
    [InlineData("Fulfilled")]
    [InlineData("")]
    [InlineData("nonsense")]
    public async Task No_other_status_may_be_written_by_hand(string status)
    {
        // Every other transition is written by the thing that knows it happened. A console that
        // could set any status is a console that can tell the system a payment arrived.
        var orders = new FakeOrders(Order(OrderStatus.Pending));

        var result = await Controller(orders: orders).SetOrderStatus(
            OrderId, new AdminSetOrderStatusRequest { Status = status }, default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, orders.Writes);
    }

    [Fact]
    public async Task Marking_an_order_that_does_not_exist_is_a_404()
    {
        var result = await Controller(orders: new FakeOrders(null)).SetOrderStatus(
            OrderId, new AdminSetOrderStatusRequest { Status = "Cancelled" }, default);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // -- the redraw: what it refuses -----------------------------------------------------------

    [Fact]
    public async Task A_legacy_book_is_refused_rather_than_queued_at_the_wrong_pipeline()
    {
        /*
          The legacy pipeline draws per page on demand: no spreads, no cover wrap, no resumable
          manifest. Queuing the Beki job at one would start a composite run against a book whose
          preview plan was never written for it — and it would spend money doing so.
        */
        var world = World(pipeline: GenerationPipelines.Legacy);

        var result = await world.Regeneration.RequestAsync(Request(BekiRegenerationScopes.Book), default);

        Assert.Equal(BekiRegenerationStatus.Refused, result.Status);
        Assert.Equal(0, world.Jobs.Enqueued);
        Assert.Empty(world.Blobs.Deleted);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
    }

    [Fact]
    public async Task A_book_with_a_live_job_behind_it_is_refused_rather_than_raced()
    {
        // Two runs drawing one pack is the duplicate spend Hangfire's per-pack lock exists to
        // prevent, and deleting a spread underneath a running job is asking for it politely.
        var world = World(status: AdventurePackStatus.GeneratingStory, heartbeatMinutesAgo: 1);

        var result = await world.Regeneration.RequestAsync(Request(BekiRegenerationScopes.Book), default);

        Assert.Equal(BekiRegenerationStatus.Refused, result.Status);
        Assert.Equal(0, world.Jobs.Enqueued);
        Assert.Empty(world.Blobs.Deleted);
    }

    [Fact]
    public async Task A_job_that_has_gone_quiet_for_longer_than_the_sweep_tolerates_may_be_redrawn()
    {
        // The books most in need of this are exactly the ones stuck in a working status forever.
        // The line between "running" and "abandoned" is the sweep's own, so the console and the
        // sweep cannot disagree about which books have something running behind them.
        var world = World(status: AdventurePackStatus.StoryReady, heartbeatMinutesAgo: 240);

        var result = await world.Regeneration.RequestAsync(Request(BekiRegenerationScopes.Book), default);

        Assert.Equal(BekiRegenerationStatus.Queued, result.Status);
        Assert.Equal(1, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task A_book_whose_preview_run_is_gone_is_refused_before_anything_is_deleted()
    {
        // Without the run there is no plan and no portrait, and the job would throw on its first
        // line — after this method had already deleted the pictures it was going to redraw.
        var world = World(runExists: false);

        var result = await world.Regeneration.RequestAsync(Request(BekiRegenerationScopes.Book), default);

        Assert.Equal(BekiRegenerationStatus.Refused, result.Status);
        Assert.Empty(world.Blobs.Deleted);
        Assert.Equal(0, world.Jobs.Enqueued);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
    }

    [Fact]
    public async Task A_book_that_moved_between_the_read_and_the_write_loses_the_race_harmlessly()
    {
        /*
          Claim first, delete second, and this is the test that says why. If the sweep buried this
          pack, or another admin clicked a second earlier, the compare-and-set fails and nothing
          has been deleted. The reverse order would delete eight pictures and then discover it was
          not allowed to.
        */
        var world = World();
        world.Packs.OnBeforeTransition = () => world.Packs.Force(AdventurePackStatus.GeneratingStory);

        var result = await world.Regeneration.RequestAsync(Request(BekiRegenerationScopes.Book), default);

        Assert.Equal(BekiRegenerationStatus.Refused, result.Status);
        Assert.Empty(world.Blobs.Deleted);
        Assert.Equal(0, world.Jobs.Enqueued);
    }

    [Theory]
    [InlineData("everything", 1, "")]
    [InlineData("spread", null, "it is wrong")]
    [InlineData("spread", 9, "it is wrong")]
    [InlineData("spread", 0, "it is wrong")]
    [InlineData("book", null, "   ")]
    public async Task A_malformed_request_never_reaches_storage(string scope, int? spread, string reason)
    {
        var world = World();

        var result = await world.Regeneration.RequestAsync(
            new BekiRegenerationRequest(PackId, scope, spread, reason, "operator@beki.ge"), default);

        Assert.Equal(BekiRegenerationStatus.Refused, result.Status);
        Assert.Empty(world.Blobs.Deleted);
        Assert.Equal(0, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task A_book_that_does_not_exist_is_a_not_found_rather_than_a_refusal()
    {
        var world = World(packExists: false);

        var result = await world.Regeneration.RequestAsync(Request(BekiRegenerationScopes.Book), default);

        Assert.Equal(BekiRegenerationStatus.NotFound, result.Status);
    }

    // -- the redraw: what it does --------------------------------------------------------------

    [Fact]
    public async Task Redrawing_one_spread_deletes_that_spread_and_leaves_the_others_alone()
    {
        var world = World();

        var result = await world.Regeneration.RequestAsync(
            Request(BekiRegenerationScopes.Spread, spread: 3), default);

        Assert.Equal(BekiRegenerationStatus.Queued, result.Status);

        // The page and everything that describes it: the composite, the pre-composite base the
        // next spread's continuity is drawn against, the reviewer's record and the receipt.
        Assert.Contains(BekiPackBlobs.SpreadName(Owner, PackId, 3), world.Blobs.Deleted);
        Assert.Contains(BekiPackBlobs.SpreadBaseName(Owner, PackId, 3), world.Blobs.Deleted);
        Assert.Contains(BekiPackBlobs.SpreadQaName(Owner, PackId, 3), world.Blobs.Deleted);
        Assert.Contains(BekiPackBlobs.CompositionManifestName(Owner, PackId, 3), world.Blobs.Deleted);

        Assert.DoesNotContain(BekiPackBlobs.SpreadName(Owner, PackId, 2), world.Blobs.Deleted);
        Assert.DoesNotContain(BekiPackBlobs.SpreadName(Owner, PackId, 4), world.Blobs.Deleted);

        // And the manifest stays, so the seven pages that are not being redrawn are adopted rather
        // than drawn a second time. The resume path drops an entry whose blob will not download,
        // which is the whole of "redraw this spread".
        Assert.DoesNotContain(BekiPackBlobs.ManifestName(Owner, PackId), world.Blobs.Deleted);
        Assert.DoesNotContain(BekiPackBlobs.CoverWrapCompositeName(Owner, PackId), world.Blobs.Deleted);
    }

    [Fact]
    public async Task Redrawing_the_cover_takes_every_derivation_of_the_one_master_with_it()
    {
        // There is exactly one cover master (audit P0-01) and everything a person sees is cut from
        // it. A derivation that outlived the master would be the second cover design the supplier
        // rejected the package for.
        var world = World();

        await world.Regeneration.RequestAsync(Request(BekiRegenerationScopes.Cover), default);

        Assert.Contains(BekiPackBlobs.CoverWrapCompositeName(Owner, PackId), world.Blobs.Deleted);
        Assert.Contains(BekiPackBlobs.CoverWrapBaseName(Owner, PackId), world.Blobs.Deleted);
        Assert.Contains(BekiPackBlobs.CoverCompositionName(Owner, PackId), world.Blobs.Deleted);
        Assert.Contains(BekiPackBlobs.CoverFrontName(Owner, PackId), world.Blobs.Deleted);
        Assert.Contains(BekiPackBlobs.CoverPdfName(Owner, PackId), world.Blobs.Deleted);

        Assert.DoesNotContain(BekiPackBlobs.SpreadName(Owner, PackId, 1), world.Blobs.Deleted);
    }

    [Fact]
    public async Task Redrawing_the_whole_book_takes_the_manifest_and_every_page()
    {
        var world = World();

        await world.Regeneration.RequestAsync(Request(BekiRegenerationScopes.Book), default);

        Assert.Contains(BekiPackBlobs.ManifestName(Owner, PackId), world.Blobs.Deleted);
        Assert.Contains(BekiPackBlobs.CoverWrapCompositeName(Owner, PackId), world.Blobs.Deleted);

        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            Assert.Contains(BekiPackBlobs.SpreadName(Owner, PackId, spread), world.Blobs.Deleted);
        }
    }

    [Theory]
    [InlineData("book")]
    [InlineData("cover")]
    [InlineData("spread")]
    public async Task A_redraw_never_touches_the_words_the_plan_or_the_child(string scope)
    {
        /*
          The line this class exists to hold. The story, the Visual Scenario, the identity spec and
          the photograph are the book's IDENTITY, not its rendering: replanning them would dress
          the child differently and rewrite words a parent has already read. A redraw is meant to
          give them the same book, drawn properly.
        */
        var world = World();

        await world.Regeneration.RequestAsync(Request(scope, spread: 1), default);

        Assert.DoesNotContain(BekiPackBlobs.StoryName(Owner, PackId), world.Blobs.Deleted);
        Assert.DoesNotContain(BekiPackBlobs.ScenarioName(Owner, PackId), world.Blobs.Deleted);
        Assert.DoesNotContain(BekiPackBlobs.IdentitySpecName(Owner, PackId), world.Blobs.Deleted);
        Assert.DoesNotContain(Photo, world.Blobs.Deleted);
    }

    [Theory]
    [InlineData("book")]
    [InlineData("cover")]
    [InlineData("spread")]
    public async Task Every_redraw_takes_the_verdict_and_the_sheets_with_it(string scope)
    {
        /*
          The most dangerous artifact to leave behind. A stored release verdict describes a
          rendering that is about to stop existing, and the reconciliation would happily publish a
          book on the strength of gates evaluated against pictures that have been deleted.
        */
        var world = World();

        await world.Regeneration.RequestAsync(Request(scope, spread: 1), default);

        Assert.Contains(BekiPackBlobs.ReleaseGatesName(Owner, PackId), world.Blobs.Deleted);
        Assert.Contains(
            BekiPackBlobs.ContactSheetName(Owner, PackId, BekiPackBlobs.DigitalRenderArtifact),
            world.Blobs.Deleted);
        Assert.Contains(BekiPackBlobs.ReadingPdfName(Owner, PackId), world.Blobs.Deleted);
    }

    [Fact]
    public async Task A_redraw_unpublishes_both_files_and_says_what_is_happening()
    {
        /*
          A book being redrawn must not stay downloadable. The parent opens the old one halfway
          through, and the release gates publish over it minutes later — two different books under
          one link, with nothing recording which one the child was read.
        */
        var world = World();

        await world.Regeneration.RequestAsync(Request(BekiRegenerationScopes.Book), default);

        Assert.Equal(AdventurePackStatus.GeneratingStory, world.Packs.Status);
        Assert.Null(world.Packs.Pack.PdfUrl);
        Assert.Null(world.Packs.Pack.PrintPdfUrl);
        Assert.Equal(BekiRegeneration.ProgressMessage, world.Packs.Pack.ProgressMessage);
        Assert.Equal(0, world.Packs.Pack.ProgressPercent);
    }

    [Fact]
    public async Task A_redraw_is_queued_against_the_same_pack_and_the_same_preview_run()
    {
        // The same job, not a second pipeline. Everything a redraw is comes from the resume path
        // the fulfilment job already has: adopt what downloads, draw what does not.
        var world = World();

        await world.Regeneration.RequestAsync(Request(BekiRegenerationScopes.Book), default);

        Assert.Equal(1, world.Jobs.Enqueued);
        Assert.Equal(nameof(IBekiPackFulfillment.ProcessAsync), world.Jobs.LastMethod);
        Assert.Equal([PackId, RunId], world.Jobs.LastArguments);
    }

    [Fact]
    public async Task A_redraw_leaves_a_row_naming_the_operator_the_scope_and_the_reason()
    {
        /*
          Every one of these costs real money at an image API, and a spend with no stated cause is
          one nobody can account for at the end of the month. A flag rather than a blocker: nothing
          is broken and nobody needs paging — this row is the audit trail, not an incident.
        */
        var world = World();

        await world.Regeneration.RequestAsync(
            new BekiRegenerationRequest(
                PackId, BekiRegenerationScopes.Spread, 5, "beki is the wrong colour", "misho@beki.ge"),
            default);

        var alarm = Assert.Single(world.Alarms.Raised);

        Assert.Equal(BekiRegeneration.AlarmCheckId, alarm.CheckId);
        Assert.Equal(BekiReleaseSeverity.Flag, alarm.Severity);
        Assert.Equal(PackId, alarm.PackId);
        Assert.Equal(OrderId, alarm.OrderId);
        Assert.Contains("misho@beki.ge", alarm.Detail);
        Assert.Contains("spread 5", alarm.Detail);
        Assert.Contains("beki is the wrong colour", alarm.Detail);
    }

    [Fact]
    public async Task Two_redraws_of_one_book_are_two_rows_rather_than_one_that_moved()
    {
        // Everywhere else in this system deduplication is the point. Here each request is its own
        // event, and collapsing them would hide the third redraw of a book somebody keeps paying
        // for — which is exactly the row an owner would want to see.
        var world = World();

        await world.Regeneration.RequestAsync(Request(BekiRegenerationScopes.Cover), default);
        world.Packs.Force(AdventurePackStatus.Completed);
        world.Clock.Advance(TimeSpan.FromMinutes(5));
        await world.Regeneration.RequestAsync(Request(BekiRegenerationScopes.Cover), default);

        Assert.Equal(2, world.Alarms.Raised.Count);
        Assert.NotEqual(world.Alarms.Raised[0].EvidenceKey, world.Alarms.Raised[1].EvidenceKey);
    }

    // -- the redraw, through the route ---------------------------------------------------------

    [Theory]
    [InlineData(BekiRegenerationStatus.Queued, typeof(AcceptedResult))]
    [InlineData(BekiRegenerationStatus.NotFound, typeof(NotFoundObjectResult))]
    [InlineData(BekiRegenerationStatus.Refused, typeof(ConflictObjectResult))]
    public async Task The_route_answers_with_the_code_that_matches_the_outcome(
        BekiRegenerationStatus status, Type expected)
    {
        // 409 rather than 400 for a refusal: the request was well formed and the book's state said
        // no, which is a thing the operator can wait out or act on.
        var regeneration = new FakeRegeneration(new BekiRegenerationResult(status, "…"));

        var result = await Controller(regeneration: regeneration).RegenerateBook(
            PackId,
            new AdminRegenerateBookRequest { Scope = "book", Reason = "why" },
            default);

        Assert.IsType(expected, result);
    }

    [Fact]
    public async Task The_route_records_the_signed_in_operator_rather_than_trusting_the_body()
    {
        // A spend nobody can be asked about is not an authorisation. The body has no field for it,
        // and that is deliberate.
        var regeneration = new FakeRegeneration(
            new BekiRegenerationResult(BekiRegenerationStatus.Queued, "ok"));

        await Controller(regeneration: regeneration).RegenerateBook(
            PackId, new AdminRegenerateBookRequest { Scope = "cover", Reason = "smudged" }, default);

        Assert.Equal("operator@beki.ge", regeneration.Last!.Operator);
        Assert.Equal("cover", regeneration.Last.Scope);
        Assert.Equal("smudged", regeneration.Last.Reason);
    }

    // -- failure codes ---------------------------------------------------------------------

    [Theory]
    [InlineData("GENERATION_STALLED the job went quiet.", "GENERATION_STALLED")]
    [InlineData("IMAGE_GENERATION_FAILED (spread 3)", "IMAGE_GENERATION_FAILED")]
    [InlineData("LAYOUT_FAILED: the composer refused.", "LAYOUT_FAILED")]
    [InlineData("GENERATION_BUDGET_EXCEEDED", "GENERATION_BUDGET_EXCEEDED")]
    public async Task A_stored_failure_yields_the_code_an_operator_can_group_on(string message, string code)
    {
        await Task.CompletedTask;
        Assert.Equal(code, AdminFailureCode.From(message));
    }

    [Theory]
    [InlineData("The operation was canceled.")]
    [InlineData("Something went wrong drawing the book")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_message_that_is_only_a_sentence_yields_no_code(string? message)
    {
        // "The" is not a failure code. A first word returned as one would put a chip on the screen
        // that groups unrelated incidents under an English article.
        await Task.CompletedTask;
        Assert.Null(AdminFailureCode.From(message));
    }

    [Fact]
    public async Task The_panel_carries_the_whole_alarm_history_newest_first()
    {
        // The question a panel is opened with is "has this happened to this book before", and a
        // list of only the OPEN ones answers "is it happening right now" — the same shape, a
        // different question, and the reviewed rows are the half that says whether anyone acted.
        var alarms = new RecordingRaises
        {
            ForPack =
            {
                Alarm("centre_fold", DateTimeOffset.Parse("2026-08-01T00:00:00Z")),
                Alarm("image_qa", DateTimeOffset.Parse("2026-09-01T00:00:00Z")),
                Alarm("press_file_missing", DateTimeOffset.Parse("2026-08-15T00:00:00Z")),
            },
        };

        var detail = await Detail(Controller(alarms: alarms));

        Assert.Equal(
            ["image_qa", "press_file_missing", "centre_fold"],
            detail.Alarms.Select(alarm => alarm.CheckId));
    }

    // -- registration --------------------------------------------------------------------

    [Fact]
    public void The_redraw_service_resolves_the_way_it_does_at_startup()
    {
        /*
          ValidateOnBuild is what Development uses, and a missing dependency here would otherwise
          surface as a Production 500 on the one console button that spends money — Production has
          validation off, so the first thing to discover it would be an operator's click.
        */
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<BekiOptions>();
        services.AddScoped<IAdventurePackRepository>(_ => new FakePacks(null));
        services.AddScoped<IOrderRepository>(_ => new FakeOrders(null));
        services.AddScoped<IMasterStoryRunRepository>(_ => new FakeRuns(null));
        services.AddScoped<IBlobStorageService>(_ => new FakeBlobs([]));
        services.AddScoped<IBekiAlarmService>(_ => new RecordingRaises());
        services.AddSingleton<IBackgroundJobClient>(_ => new RecordingJobs());

        services.AddAdminServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBekiRegeneration>());
    }

    // -- fixtures --------------------------------------------------------------------------

    private const string Photo = "portraits/child.png";

    private static BekiAlarm Alarm(string checkId, DateTimeOffset lastSeen) => new(
        Guid.NewGuid(), PackId, OrderId, Owner, checkId, BekiReleaseSeverity.Flag,
        "detail", null, DateTimeOffset.UnixEpoch, lastSeen, null, null, null);

    private static async Task<AdminOrderDetailResponse> Detail(AdminOrdersController controller)
    {
        var result = await controller.OrderDetail(OrderId, default);
        return (AdminOrderDetailResponse)Assert.IsType<OkObjectResult>(result.Result).Value!;
    }

    private static BekiRegenerationRequest Request(string scope, int? spread = null) =>
        new(PackId, scope, spread ?? (scope == BekiRegenerationScopes.Spread ? 1 : null),
            "the hero is wrong", "operator@beki.ge");

    private static Order Order(OrderStatus status) => new()
    {
        Id = OrderId,
        UserId = Owner,
        BookId = PackId,
        Type = OrderType.NewBook,
        Package = OrderPackage.Digital,
        Status = status,
        DraftJson = JsonSerializer.Serialize(
            new BookDraftRequest { WorldId = "dinosaurs", PreviewBookId = RunId },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)),
    };

    private static AdventurePack Pack(string pipeline, AdventurePackStatus status, DateTime heartbeat) => new()
    {
        Id = PackId,
        UserId = Owner,
        Status = status,
        GenerationPipeline = pipeline,
        GeneratedJson = """{"title":"ნინა და დინოზავრები","storyPages":[]}""",
        PdfUrl = "packs/reading.pdf",
        PrintPdfUrl = "packs/press.pdf",
        CreatedAt = heartbeat,
        GenerationHeartbeatUtc = heartbeat,
        AccessLevel = BookAccessLevel.Full,
    };

    /// <summary>
    /// One book, its storage, its order and its preview run, wired to a real
    /// <see cref="BekiRegeneration"/>. Everything the redraw touches is observable; everything it
    /// must not touch throws.
    /// </summary>
    private static RedrawWorld World(
        string pipeline = GenerationPipelines.Beki,
        AdventurePackStatus status = AdventurePackStatus.Completed,
        int heartbeatMinutesAgo = 60,
        bool packExists = true,
        bool runExists = true)
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-09-02T08:00:00Z"));
        var pack = Pack(pipeline, status, clock.GetUtcNow().UtcDateTime.AddMinutes(-heartbeatMinutesAgo));

        var packs = new FakePacks(packExists ? pack : null);
        var blobs = new FakeBlobs(Everything());
        var alarms = new RecordingRaises();
        var jobs = new RecordingJobs();

        var regeneration = new BekiRegeneration(
            packs,
            new FakeOrders(Order(OrderStatus.Fulfilled)),
            new FakeRuns(runExists ? Run() : null),
            blobs,
            alarms,
            jobs,
            Options.Create(new BekiOptions()),
            NullLogger<BekiRegeneration>.Instance,
            clock);

        return new RedrawWorld(regeneration, packs, blobs, alarms, jobs, clock);
    }

    private sealed record RedrawWorld(
        BekiRegeneration Regeneration,
        FakePacks Packs,
        FakeBlobs Blobs,
        RecordingRaises Alarms,
        RecordingJobs Jobs,
        TestClock Clock);

    /// <summary>Everything one finished composite book has in storage, by name.</summary>
    private static IEnumerable<string> Everything()
    {
        yield return BekiPackBlobs.ManifestName(Owner, PackId);
        yield return BekiPackBlobs.StoryName(Owner, PackId);
        yield return BekiPackBlobs.ScenarioName(Owner, PackId);
        yield return BekiPackBlobs.IdentitySpecName(Owner, PackId);
        yield return BekiPackBlobs.ReleaseGatesName(Owner, PackId);
        yield return BekiPackBlobs.ReadingPdfName(Owner, PackId);
        yield return BekiPackBlobs.InteriorPdfName(Owner, PackId);
        yield return BekiPackBlobs.CoverWrapCompositeName(Owner, PackId);
        yield return BekiPackBlobs.CoverWrapBaseName(Owner, PackId);
        yield return BekiPackBlobs.CoverCompositionName(Owner, PackId);
        yield return BekiPackBlobs.CoverFrontName(Owner, PackId);
        yield return BekiPackBlobs.CoverPdfName(Owner, PackId);
        yield return Photo;

        foreach (var artifact in BekiPackBlobs.RenderedArtifacts)
        {
            yield return BekiPackBlobs.ContactSheetName(Owner, PackId, artifact);
        }

        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            yield return BekiPackBlobs.SpreadName(Owner, PackId, spread);
            yield return BekiPackBlobs.SpreadBaseName(Owner, PackId, spread);
            yield return BekiPackBlobs.SpreadQaName(Owner, PackId, spread);
            yield return BekiPackBlobs.CompositionManifestName(Owner, PackId, spread);
        }
    }

    private static MasterStoryRun Run() => new()
    {
        Id = RunId,
        UserId = Owner,
        PackId = PackId,
        StoryJson = """{"concept":{}}""",
        PhotoBlobUrl = Photo,
        PromptVersion = "v6",
    };

    /// <summary>The service's own retry rule, answered by a switch: the console asks, it does not mirror.</summary>
    private sealed class FakeOrderService(bool canRedrive) : IOrderService
    {
        public Task<bool> CanRedriveAsync(Guid orderId, CancellationToken cancellationToken) => Task.FromResult(canRedrive);
        public Task<QuoteResponse> QuoteAsync(Guid userId, QuoteRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CheckoutResponse> CreateBookOrderAsync(Guid userId, CreateOrderRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CheckoutResponse> CreatePrintUpgradeOrderAsync(Guid userId, CreatePrintUpgradeOrderRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrderResponse>> ListAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OrderStatusResponse> GetStatusAsync(Guid userId, Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OrderStatusResponse> ConfirmAsync(Guid userId, Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelAsync(Guid userId, Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task HandleStripeWebhookAsync(string jsonPayload, string stripeSignature, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HandleBogWebhookAsync(byte[] payload, string? signature, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RetryStalledFulfilmentAsync() => throw new NotSupportedException();
        public Task FulfilOrderAsync(Guid orderId) => throw new NotSupportedException();
        public Task<bool> RequeueFulfilmentAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static AdminOrdersController Controller(
        FakeReporting? reporting = null,
        FakeOrders? orders = null,
        FakePrintOrders? printOrders = null,
        FakeBlobs? blobs = null,
        FakeRegeneration? regeneration = null,
        FakePacks? packs = null,
        RecordingRaises? alarms = null,
        bool canRedrive = false) =>
        new(reporting ?? new FakeReporting(),
            packs ?? new FakePacks(null),
            orders ?? new FakeOrders(null),
            printOrders ?? new FakePrintOrders(),
            blobs ?? new FakeBlobs([]),
            generationService: null!,
            orderService: new FakeOrderService(canRedrive),
            regeneration ?? new FakeRegeneration(
                new BekiRegenerationResult(BekiRegenerationStatus.Queued, "ok")),
            packageExport: null!,
            releaseGates: null!,
            reconciliation: null!,
            releasePolicy: null!,
            alarms ?? new RecordingRaises(),
            new OperatorContext(),
            NullLogger<AdminOrdersController>.Instance)
        {
            // The PDF and image routes write a response header, which needs a context to write to.
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    /// <summary>A controller wired for the download route only: one order, one book, one store.</summary>
    private static (AdminOrdersController Controller, FakeBlobs Blobs) PdfController(
        OrderPackage package, bool reading, bool print)
    {
        var pack = Pack(GenerationPipelines.Beki, AdventurePackStatus.Completed, DateTime.UtcNow);
        pack.PdfUrl = reading ? "packs/reading.pdf" : null;
        pack.PrintPdfUrl = print ? "packs/press.pdf" : null;

        var blobs = new FakeBlobs([])
        {
            Bytes =
            {
                ["packs/reading.pdf"] = Encoding.UTF8.GetBytes("reading-bytes"),
                ["packs/press.pdf"] = Encoding.UTF8.GetBytes("print-bytes"),
            },
        };

        return (Controller(new FakeReporting { Package = package }, blobs: blobs, packs: new FakePacks(pack)), blobs);
    }

    // -- doubles ---------------------------------------------------------------------------

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private sealed class OperatorContext : IUserContextService
    {
        public Guid GetUserId() => Owner;

        public string GetEmail() => "operator@beki.ge";
    }

    private sealed class FakeReporting : IAdminReportingRepository
    {
        public OrderPackage Package { get; init; } = OrderPackage.Digital;

        /// <summary>Overrides the default one-order-one-book shape when a test cares about it.</summary>
        public AdminOrderDetailResponse? Detail { get; init; }

        public int Calls { get; private set; }

        public string? LastFlag { get; private set; }

        public string? LastStatus { get; private set; }

        public Task<AdminOrderListResponse> GetOrdersAsync(
            string? status, string? search, string? flag, int page, int pageSize, CancellationToken ct)
        {
            Calls++;
            LastFlag = flag;
            LastStatus = status;
            return Task.FromResult(new AdminOrderListResponse());
        }

        public Task<AdminOrderDetailResponse?> GetOrderDetailAsync(Guid orderId, CancellationToken ct) =>
            Task.FromResult<AdminOrderDetailResponse?>(Detail ?? new AdminOrderDetailResponse
            {
                Order = new AdminOrderRow { Id = orderId, Package = Package.ToString() },
                Book = new AdminOrderBook { Id = PackId },
            });

        public Task<AdminCustomerListResponse> GetCustomersAsync(
            string? search, int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>One row with a real compare-and-set, so a lost race is a lost race.</summary>
    private sealed class FakePacks(AdventurePack? seed) : IAdventurePackRepository
    {
        public AdventurePack Pack { get; } = seed ?? new AdventurePack();

        private readonly bool _present = seed is not null;

        public AdventurePackStatus Status => Pack.Status;

        /// <summary>Simulates another writer moving the row between the read and the write.</summary>
        public Action? OnBeforeTransition { get; set; }

        public void Force(AdventurePackStatus status) => Pack.Status = status;

        public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(_present && id == Pack.Id ? Pack : null);

        public Task<bool> TryUpdateStatusAsync(
            Guid id, AdventurePackStatus expectedStatus, AdventurePackStatus status,
            string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken ct)
        {
            OnBeforeTransition?.Invoke();

            if (Pack.Status != expectedStatus)
            {
                return Task.FromResult(false);
            }

            Pack.Status = status;
            Pack.GeneratedJson = generatedJson;
            Pack.PdfUrl = pdfUrl;
            Pack.ErrorMessage = errorMessage;
            return Task.FromResult(true);
        }

        public Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken ct)
        {
            Pack.PrintPdfUrl = printPdfUrl;
            return Task.CompletedTask;
        }

        public Task UpdateProgressAsync(Guid id, string? progressMessage, int? progressPercent, CancellationToken ct)
        {
            Pack.ProgressMessage = progressMessage;
            Pack.ProgressPercent = progressPercent;
            return Task.CompletedTask;
        }

        // Everything else is refused, so a change that starts calling one fails here rather than
        // passing quietly.
        public Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken ct) => throw new NotSupportedException();
        public Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(Guid characterId, Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> SetAccessLevelAsync(Guid id, BookAccessLevel accessLevel, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateBookPresentationAsync(Guid id, string? title, string? coverImageUrl, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(DateTime cutoffUtc, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryFailStaleGenerationAsync(Guid id, AdventurePackStatus expectedStatus, DateTime cutoffUtc, string errorMessage, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryFailAsync(Guid id, AdventurePackStatus expectedStatus, string errorMessage, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken ct) => throw new NotSupportedException();
        public Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdatePreviewIllustrationAsync(Guid id, PreviewIllustrationStatus status, string? illustrationUrl, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryClaimPreviewIllustrationGenerationAsync(Guid id, int staleAfterMinutes, CancellationToken ct) => throw new NotSupportedException();
        public Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> UpdateGeneratedJsonAsync(Guid id, string generatedJson, CancellationToken ct) => throw new NotSupportedException();
        public Task SetGenerationPipelineAsync(Guid id, string pipeline, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> ListWithheldBekiPacksAsync(int limit, BekiWithheldCursor? after, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeBlobs(IEnumerable<string> stored) : IBlobStorageService
    {
        private readonly HashSet<string> _stored = new(stored, StringComparer.Ordinal);

        public Dictionary<string, byte[]> Bytes { get; } = new(StringComparer.Ordinal);

        public List<string> Deleted { get; } = [];

        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken ct)
        {
            if (!_stored.Remove(storedUrl))
            {
                return Task.FromResult(false);
            }

            Deleted.Add(storedUrl);
            return Task.FromResult(true);
        }

        public Task<bool> ExistsAsync(string blobName, CancellationToken ct) =>
            Task.FromResult(_stored.Contains(blobName) || Bytes.ContainsKey(blobName));

        public Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken ct) =>
            Bytes.TryGetValue(storedUrl, out var bytes)
                ? Task.FromResult(bytes)
                : throw new FileNotFoundException(storedUrl);

        public Task<string> UploadAsync(string blobName, byte[] bytes, string contentType, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Stream> DownloadAsync(string blobName, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeOrders(Order? order) : IOrderRepository
    {
        public int Writes { get; private set; }

        public bool Written { get; init; }

        public OrderStatus? LastStatus { get; private set; }

        public string? LastReason { get; private set; }

        public IReadOnlyCollection<OrderStatus>? LastAllowedFrom { get; private set; }

        public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(order);

        public Task<IReadOnlyList<Order>> GetPaidForBookAsync(Guid bookId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Order>>(order is null ? [] : [order]);

        public Task<bool> TrySetAdminStatusAsync(
            Guid id, OrderStatus status, IReadOnlyCollection<OrderStatus> allowedFrom,
            string? failureReason, CancellationToken ct)
        {
            Writes++;
            LastStatus = status;
            LastReason = failureReason;
            LastAllowedFrom = allowedFrom;
            return Task.FromResult(Written);
        }

        public Task<Guid> CreateAsync(Order created, CancellationToken ct) => throw new NotSupportedException();
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

    private sealed class FakeRuns(MasterStoryRun? run) : IMasterStoryRunRepository
    {
        public Task SaveAppearanceDescriptionAsync(Guid id, string appearanceDescription, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(run is not null && run.Id == id ? run : null);

        public Task CreateAsync(MasterStoryRun created, CancellationToken ct) => throw new NotSupportedException();
        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task SetProgressAsync(Guid id, string status, string? progressMessage, CancellationToken ct) => throw new NotSupportedException();
        public Task SavePromptsAsync(Guid id, string model, string promptVersion, string systemPrompt, string userPrompt, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveStoryAsync(Guid id, string storyJson, string contentJson, int promptTokens, int completionTokens, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid id, string contentJson, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkFailedAsync(Guid id, string error, CancellationToken ct) => throw new NotSupportedException();
        public Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakePrintOrders : IPrintOrderService
    {
        public bool Cancelled { get; init; }

        public Guid? CancelledFor { get; private set; }

        public Task<bool> TryCancelForOrderAsync(Guid orderId, CancellationToken ct)
        {
            CancelledFor = orderId;
            return Task.FromResult(Cancelled);
        }

        public Task<PrintOrder?> CreateForPaidOrderAsync(Order order, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PrintOrderResponse>> ListForUserAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrintOrderResponse?> GetForUserAsync(Guid userId, Guid printOrderId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrintOrderResponse> UpdateAddressAsync(Guid userId, Guid printOrderId, ShippingAddressRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AddressResponse>> ListAddressesAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<AddressResponse> SaveAddressAsync(Guid userId, SaveAddressRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> DeleteAddressAsync(Guid userId, Guid addressId, CancellationToken ct) => throw new NotSupportedException();
        public Task<AdminPrintQueueResponse> GetAdminQueueAsync(string? status, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<AdminPrintOrderResponse?> UpdateStatusAsync(Guid printOrderId, UpdatePrintOrderStatusRequest request, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeRegeneration(BekiRegenerationResult result) : IBekiRegeneration
    {
        public BekiRegenerationRequest? Last { get; private set; }

        public Task<BekiRegenerationResult> RequestAsync(
            BekiRegenerationRequest request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(result);
        }

        public bool CanRegenerate(AdventurePack pack) => true;
    }

    private sealed class RecordingRaises : IBekiAlarmService
    {
        public Task<IReadOnlyList<BekiAlarm>> ListRecentAsync(int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<BekiAlarm?> GetAsync(Guid alarmId, CancellationToken ct) => throw new NotSupportedException();
        public List<BekiAlarmRaise> Raised { get; } = [];

        /// <summary>Deliberately unsorted: ordering is the controller's job, so it is asserted.</summary>
        public List<BekiAlarm> ForPack { get; } = [];

        public Task RaiseAsync(BekiAlarmRaise raise, CancellationToken ct)
        {
            Raised.Add(raise);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>([]);

        public Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>(ForPack);

        public Task<bool> ReviewAsync(Guid alarmId, string reviewedBy, string resolution, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<int> CountOpenAsync(CancellationToken ct) => Task.FromResult(0);
    }

    /// <summary>
    /// Records what was queued rather than queuing it. The method and its arguments are the
    /// assertion: a redraw that enqueued a different job, or the same job against another pack,
    /// would be a book drawn from somebody else's plan.
    /// </summary>
    private sealed class RecordingJobs : IBackgroundJobClient
    {
        public int Enqueued { get; private set; }

        public string? LastMethod { get; private set; }

        public IReadOnlyList<object?> LastArguments { get; private set; } = [];

        public string Create(Job job, IState state)
        {
            Enqueued++;
            LastMethod = job.Method.Name;

            // The cancellation token the expression carries is not an argument anybody chose.
            LastArguments = job.Args.Where(argument => argument is not CancellationToken).ToList();

            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }
}
