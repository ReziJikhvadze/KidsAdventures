using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Controllers;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Story;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// Print re-preparation: the admin action that gives a finished book its press files without
/// drawing anything again.
///
/// The situation it answers is the one the deployment was actually in. A book is Completed, the
/// family has downloaded it, the printer's files were withheld by a gate — and the only two buttons
/// in the console were "recover", which refuses a book that is not Failed, and "retry", which
/// re-drives the paid order and draws nine pictures. So the fix to the normalization that caused the
/// hold could not be applied to the book that the hold was found on.
///
/// Every test here starts from a real run of the fulfilment job through the same harness the
/// fulfilment suite uses, so the storage the re-preparation reads is storage a run actually wrote.
/// Nothing calls a model: the artwork is stub bytes and the composed documents are offline
/// fixtures, and the assertions include the fact that the drawing side was never touched.
/// </summary>
public class BekiRepreparePrintTests
{
    /// <summary>
    /// The whole point, end to end. A completed book whose printing is held is re-prepared from
    /// stored artwork; the press files are published, the previous ones are kept, and the blocker
    /// that was raised about the hold is closed because the hold is over.
    /// </summary>
    [Fact]
    public async Task A_held_book_is_re_prepared_and_its_hold_alarm_is_closed()
    {
        var world = await HeldBookAsync();
        var previousPdf = world.Blobs.Uploaded[BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId)];
        var wrapCallsBefore = world.Generator.WrapCalls;

        // The fix that was made between the two runs: the rasters now normalize to the locked size.
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();

        await world.Job().RepreparePrintAsync(world.PackId, CancellationToken.None);

        // The book itself is untouched — this is not a rebuild of the family's copy.
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
        Assert.Equal($"https://blob.test/{BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId)}",
            world.Packs.PdfUrl);
        Assert.Null(world.Packs.FailureReason);

        // The press stage measured a clean document this time.
        using var status = JsonDocument.Parse(Encoding.UTF8.GetString(
            world.Blobs.Uploaded[BekiPackBlobs.PressStatusName(world.UserId, world.PackId)]));
        Assert.Empty(status.RootElement.GetProperty("failed_gates").EnumerateArray());
        Assert.Equal("prepared", status.RootElement.GetProperty("interior").GetString());
        Assert.Equal("deterministic_lanczos", status.RootElement.GetProperty("print_prep_mode").GetString());

        /*
          The print slot follows the verdict, and the verdict was written down again.

          Asserted against the verdict rather than against a URL, because this offline harness
          cannot satisfy every one of the sixteen gates — VISUAL_QA reads a machine QA record
          computed from stubbed layout receipts, and HANDBACK_COMPLETENESS wants a supplier package
          a stubbed run never assembled — and neither of those is what this test is about. What IS
          asserted absolutely is that no PRESS gate failed and that the slot was rewritten by this
          run to exactly what the evaluation permits.
        */
        var release = BekiReleaseGateReport.TryParse(Encoding.UTF8.GetString(
            world.Blobs.Uploaded[BekiPackBlobs.ReleaseGatesName(world.UserId, world.PackId)]))!;
        Assert.DoesNotContain(release.FailingGates, gate => gate.StartsWith("PRESS_", StringComparison.Ordinal));
        Assert.True(world.Packs.PrintPdfUrlWritten);
        Assert.Equal(
            release.PrintReady && release.CustomerPdfMayPublish
                ? $"https://blob.test/{BekiPackBlobs.InteriorPdfName(world.UserId, world.PackId)}"
                : null,
            world.Packs.PrintPdfUrl);

        Assert.Equal(previousPdf, world.Blobs.Uploaded[BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId)]);
        Assert.NotEqual(previousPdf, world.Blobs.Uploaded[BekiPackBlobs.InteriorPdfName(world.UserId, world.PackId)]);

        // The previous deliverables are kept, byte for byte, under a timestamped folder.
        var snapshot = world.Blobs.Uploaded.Keys
            .Where(name => name.StartsWith($"{world.UserId}/{world.PackId}/previous/", StringComparison.Ordinal))
            .ToList();
        Assert.Contains(snapshot, name => name.EndsWith($"{world.PackId}.pdf", StringComparison.Ordinal));
        Assert.Contains(snapshot, name => name.EndsWith("-press-status.json", StringComparison.Ordinal));
        Assert.Equal(previousPdf,
            world.Blobs.Uploaded[snapshot.Single(name => name.EndsWith($"{world.PackId}.pdf", StringComparison.Ordinal))]);

        // And the blocker raised about the hold is closed, by the stage rather than by a person.
        Assert.Contains(world.Alarms.Resolved, closed =>
            closed.PackId == world.PackId && closed.CheckId == "PRINT_PREPARATION_HELD");
        Assert.Contains(world.Alarms.Resolved, closed =>
            closed.CheckId == BekiPackFulfillment.PressBudgetAlarmCheck);

        // Nothing was drawn: no wrap, no spreads, no provider call of any kind.
        Assert.Equal(wrapCallsBefore, world.Generator.WrapCalls);
        Assert.Equal(1, world.Generator.IllustrateCalls);
    }

    /// <summary>
    /// A candidate that will not render back is refused before anything live is touched.
    ///
    /// This is the whole reason the stage was split in two. The old press stage wrote the reading
    /// PDF over the live blob and then validated it, so a candidate that turned out unreadable had
    /// already replaced a book the family could open.
    /// </summary>
    [Fact]
    public async Task A_candidate_that_fails_render_validation_leaves_the_live_book_alone()
    {
        var world = await HeldBookAsync();
        var name = BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId);
        var livePdf = world.Blobs.Uploaded[name];
        var livePressStatus = world.Blobs.Uploaded[BekiPackBlobs.PressStatusName(world.UserId, world.PackId)];

        // Renders perfectly well and has no continuation QR on Story spread 8, which is what the
        // render-back gate exists to catch.
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBookWithoutQr();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Job().RepreparePrintAsync(world.PackId, CancellationToken.None));

        Assert.Contains("nothing was replaced", error.Message, StringComparison.Ordinal);
        Assert.Equal(livePdf, world.Blobs.Uploaded[name]);
        Assert.Equal(livePressStatus, world.Blobs.Uploaded[BekiPackBlobs.PressStatusName(world.UserId, world.PackId)]);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
        Assert.DoesNotContain(world.Blobs.Uploaded.Keys,
            key => key.StartsWith($"{world.UserId}/{world.PackId}/previous/", StringComparison.Ordinal));
    }

    /// <summary>
    /// A re-preparation the release policy would not let the family have is rolled back to the
    /// exact bytes it replaced.
    ///
    /// The strict policy makes every check a blocker, which is the cheapest way to reach a verdict
    /// that withholds the customer PDF. What matters is not which gate refused but that the refusal
    /// arrives AFTER the publish has begun and the book still ends up as it started.
    /// </summary>
    [Fact]
    public async Task A_verdict_that_withholds_the_customer_pdf_restores_the_previous_files()
    {
        var world = await HeldBookAsync();
        var name = BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId);
        var livePdf = world.Blobs.Uploaded[name];
        var liveGates = world.Blobs.Uploaded[BekiPackBlobs.ReleaseGatesName(world.UserId, world.PackId)];

        // A book that WAS printable, so that "the print slot came back" is a real assertion rather
        // than null equalling null.
        var livePrintUrl = $"https://blob.test/{name}";
        (await world.Packs.GetByIdNoOwnershipAsync(world.PackId, CancellationToken.None))!
            .PrintPdfUrl = livePrintUrl;

        /*
          The evidence the release gates read about THIS book, marked so the assertion can tell a
          restored file from a rewritten one.

          Byte-identical would otherwise prove nothing here: the stubbed composer returns the same
          receipts every time, so a rollback that never touched these names would pass. Marking
          them first makes the question real — after a refused candidate, are these still the
          rejected document's receipts or the book's own?
        */
        var layout = BekiPackBlobs.LayoutReceiptName(
            world.UserId, world.PackId, BekiPackBlobs.CanonicalLayoutMode);
        var pageReceipt = BekiPackBlobs.LayoutPageReceiptName(
            world.UserId, world.PackId, BekiPackBlobs.CanonicalLayoutMode, "page-02-layout.json");
        var introQa = BekiPackBlobs.FixedPageQaName(world.UserId, world.PackId, "intro");
        var creditsQa = BekiPackBlobs.FixedPageQaName(world.UserId, world.PackId, "credits");

        foreach (var evidence in new[] { layout, pageReceipt, introQa })
        {
            Assert.Contains(evidence, world.Blobs.Uploaded.Keys);
            world.Blobs.Seed(evidence, Encoding.UTF8.GetBytes($$"""{"the_book_as_it_stood":"{{evidence}}"}"""));
        }

        // A document this book does NOT have, which the re-prepared candidate will write. A
        // rollback that only puts files back would leave it behind, and VISUAL_QA would afterwards
        // judge the restored book on a QA record computed for a PDF that was thrown away.
        Assert.True(world.Blobs.Uploaded.TryRemove(creditsQa, out _));

        var marked = new[] { layout, pageReceipt, introQa }
            .ToDictionary(key => key, key => world.Blobs.Uploaded[key], StringComparer.Ordinal);

        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Job(strictPolicy: true).RepreparePrintAsync(world.PackId, CancellationToken.None));

        Assert.Contains("previous files were put back", error.Message, StringComparison.Ordinal);
        Assert.Equal(livePdf, world.Blobs.Uploaded[name]);
        Assert.Equal(liveGates, world.Blobs.Uploaded[BekiPackBlobs.ReleaseGatesName(world.UserId, world.PackId)]);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);

        // Everything came back, so the print slot the book had comes back with it.
        Assert.Equal(livePrintUrl, world.Packs.PrintPdfUrl);

        // And nothing was paged: a rollback that worked is not an incident.
        Assert.DoesNotContain(world.Alarms.Raised, raise =>
            raise.Detail.Contains("rollback incomplete", StringComparison.Ordinal));

        // The layout receipts and the fixed-page QA are the book's own again, byte for byte.
        foreach (var (evidence, bytes) in marked)
        {
            Assert.Equal(bytes, world.Blobs.Uploaded[evidence]);
        }

        // And what the rejected candidate invented is gone rather than left for a gate to read.
        Assert.DoesNotContain(creditsQa, world.Blobs.Uploaded.Keys);

        // The copy it restored from is still there, which is what makes the rollback auditable.
        Assert.Contains(world.Blobs.Uploaded.Keys,
            key => key.StartsWith($"{world.UserId}/{world.PackId}/previous/", StringComparison.Ordinal));
    }

    /// <summary>
    /// The rollback's own failure: the candidate replaced the family's PDF, and putting the
    /// original back failed too.
    ///
    /// This is the case the print slot used to be restored through regardless. The URL it put back
    /// was the one the book had — and the blob name is stable, so that URL now addressed the bytes
    /// this whole stage exists to refuse. The printer would have been sent the rejected candidate,
    /// with an operator seeing a book that looked exactly as it had before.
    /// </summary>
    [Fact]
    public async Task A_rollback_that_cannot_put_the_reading_pdf_back_leaves_the_print_slot_empty()
    {
        var world = await HeldBookAsync();
        var readingPdf = BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId);
        var releaseGates = BekiPackBlobs.ReleaseGatesName(world.UserId, world.PackId);

        // Printable before the attempt — the only state in which restoring the slot does damage.
        (await world.Packs.GetByIdNoOwnershipAsync(world.PackId, CancellationToken.None))!
            .PrintPdfUrl = $"https://blob.test/{readingPdf}";

        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();

        /*
          Armed at the last write before the refusal.

          The candidate's release-gates.json is written by the evaluation immediately before the
          verdict is read, so by the time this fires the canonical PDF really has been replaced —
          which is the premise. Storage then refuses the put-back, and only the put-back.
        */
        var replaced = false;
        world.Blobs.FailUpload = blob =>
        {
            replaced |= string.Equals(blob, releaseGates, StringComparison.Ordinal);
            return replaced && string.Equals(blob, readingPdf, StringComparison.Ordinal);
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Job(strictPolicy: true).RepreparePrintAsync(world.PackId, CancellationToken.None));

        // The caller is told the book is damaged rather than merely refused, so the endpoint's 409
        // says so instead of "the previous files were put back".
        Assert.Contains("reading copy could not be put back", error.Message, StringComparison.Ordinal);

        // The slot is empty rather than pointing at the candidate the gates rejected.
        Assert.Null(world.Packs.PrintPdfUrl);

        var alarm = Assert.Single(world.Alarms.Raised.Where(raise =>
            raise.Detail.Contains("rollback incomplete", StringComparison.Ordinal)));

        Assert.Equal(world.PackId, alarm.PackId);
        Assert.Equal("PRINT_PREPARATION_HELD", alarm.CheckId);
        Assert.Equal(BekiReleaseSeverity.Blocker, alarm.Severity);
        Assert.Contains($"{world.PackId}.pdf", alarm.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// One piece of evidence that would not go back is enough to withhold printing, and the alarm
    /// says which piece.
    ///
    /// The reading copy is fine here, so the family notices nothing and the caller hears the
    /// ordinary refusal. What is not fine is the book's evidence: one layout receipt in storage
    /// describes a document that was thrown away, and the release gates read receipts as this
    /// book's own. Sending that to a printer on the strength of a slot restored "because the loop
    /// finished" is the fault.
    /// </summary>
    [Fact]
    public async Task A_rollback_that_loses_one_receipt_withholds_printing_and_names_the_receipt()
    {
        var world = await HeldBookAsync();
        var releaseGates = BekiPackBlobs.ReleaseGatesName(world.UserId, world.PackId);
        var pageReceipt = BekiPackBlobs.LayoutPageReceiptName(
            world.UserId, world.PackId, BekiPackBlobs.CanonicalLayoutMode, "page-02-layout.json");

        (await world.Packs.GetByIdNoOwnershipAsync(world.PackId, CancellationToken.None))!
            .PrintPdfUrl = $"https://blob.test/{BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId)}";

        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();

        var replaced = false;
        world.Blobs.FailUpload = blob =>
        {
            replaced |= string.Equals(blob, releaseGates, StringComparison.Ordinal);
            return replaced && string.Equals(blob, pageReceipt, StringComparison.Ordinal);
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Job(strictPolicy: true).RepreparePrintAsync(world.PackId, CancellationToken.None));

        // The family's file came back, so this is still the ordinary refusal for the caller.
        Assert.Contains("previous files were put back", error.Message, StringComparison.Ordinal);

        // But the printer is not given a book one of whose receipts belongs to a rejected document.
        Assert.Null(world.Packs.PrintPdfUrl);

        var alarm = Assert.Single(world.Alarms.Raised.Where(raise =>
            raise.Detail.Contains("rollback incomplete", StringComparison.Ordinal)));

        Assert.Contains("page-02-layout.json", alarm.Detail, StringComparison.Ordinal);

        // Named specifically, not as "something failed": the reading copy is not on the list.
        Assert.DoesNotContain($"{world.PackId}.pdf", alarm.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing publishing writes under a served name is missing from the rollback list.
    ///
    /// The list and the publish step are two pieces of code that have to agree, and the way they
    /// stop agreeing is somebody adding a document to publishing — as the layout receipts and the
    /// fixed-page QA were added — without remembering that a refused re-preparation has to be able
    /// to put the previous one back. So the assertion is made against what the publish step
    /// actually wrote rather than against a second list somebody maintains by hand.
    ///
    /// The exclusions are the names publishing does not own. The additive <c>print/*</c> composites
    /// and <c>cover-layout-safety.json</c> describe a CANDIDATE and are consumed by no reader,
    /// download, printer or gate; <c>previous/</c> is the snapshot itself; and
    /// <c>asset-lock-manifest.json</c> is written by the asset-lock verification before the
    /// candidate is built at all — it states which approved files this deployment stands behind,
    /// which is the same answer for the restored book and the rejected one, and verification throws
    /// rather than writing when it is not.
    /// </summary>
    [Fact]
    public async Task Every_document_a_publish_writes_under_a_served_name_is_in_the_rollback_list()
    {
        var world = await HeldBookAsync();
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();
        world.Blobs.UploadOrder.Clear();

        await world.Job().RepreparePrintAsync(world.PackId, CancellationToken.None);

        var prefix = $"{world.UserId}/{world.PackId}/";
        var candidateEvidence = new[]
        {
            prefix + "print/",
            prefix + "previous/",
        };

        var covered = BekiPackFulfillment
            .LiveDeliverables(
                world.UserId,
                world.PackId,
                CompositePipelineFulfillmentTests.RecordingComposer.Receipts(
                    BekiPackBlobs.CanonicalLayoutMode))
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = world.Blobs.UploadOrder
            .Distinct(StringComparer.Ordinal)
            .Where(written => !candidateEvidence.Any(
                skip => written.StartsWith(skip, StringComparison.Ordinal)))
            .Where(written => written != BekiPackBlobs.CoverLayoutSafetyName(world.UserId, world.PackId))
            .Where(written => written != BekiPackBlobs.AssetLockName(world.UserId, world.PackId))
            .Where(written => !covered.Contains(written))
            .ToList();

        Assert.True(missing.Count == 0,
            "These were published under names the book is served by, but a rollback would not put "
            + "them back: " + string.Join(", ", missing));

        // And the other direction, so the list cannot pass by being empty: the documents the
        // finding was about really are the ones publishing writes.
        Assert.Contains(
            BekiPackBlobs.LayoutReceiptName(world.UserId, world.PackId, "print"),
            world.Blobs.UploadOrder);
        Assert.Contains(
            BekiPackBlobs.LayoutPageReceiptName(
                world.UserId, world.PackId, "print", "page-01-layout.json"),
            world.Blobs.UploadOrder);
        Assert.DoesNotContain(
            BekiPackBlobs.FixedPageQaName(world.UserId, world.PackId, "intro"),
            world.Blobs.UploadOrder);
    }

    /// <summary>
    /// A cancelled re-preparation changes nothing, because everything it does before the promotion
    /// is additive.
    /// </summary>
    [Fact]
    public async Task A_cancelled_re_preparation_changes_nothing_live()
    {
        var world = await HeldBookAsync();
        var name = BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId);
        var livePdf = world.Blobs.Uploaded[name];

        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();

        // Cancelled at composition, which is inside the build and before the promotion — the
        // window where the old fused stage had already overwritten the family's PDF.
        using var cancelled = new CancellationTokenSource();
        world.Composer.OnCompose = () => cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => world.Job().RepreparePrintAsync(world.PackId, cancelled.Token));

        Assert.Equal(livePdf, world.Blobs.Uploaded[name]);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
        Assert.DoesNotContain(world.Blobs.Uploaded.Keys,
            key => key.StartsWith($"{world.UserId}/{world.PackId}/previous/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Two impatient clicks are two threads over the same blobs. The second is told so at once
    /// rather than queued behind a stage that takes minutes.
    /// </summary>
    /// <remarks>
    /// The refusal names every operation the book's lock covers, not just this button — a message
    /// that said "re-preparation" would be a lie the moment a redraw was what held it.
    /// </remarks>
    [Fact]
    public async Task A_second_re_preparation_of_the_same_book_is_refused_while_the_first_runs()
    {
        var world = await HeldBookAsync();
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();

        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Composer.OnCompose = () =>
        {
            inside.TrySetResult();
            proceed.Task.GetAwaiter().GetResult();
        };

        var first = Task.Run(() => world.Job().RepreparePrintAsync(world.PackId, CancellationToken.None));

        // WhenAny rather than a bare await: a first call that fails before composition would
        // otherwise leave this test waiting on a signal nobody is going to send.
        if (await Task.WhenAny(inside.Task, first) == first) await first;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Job().RepreparePrintAsync(world.PackId, CancellationToken.None));
        Assert.Equal(BekiPackFulfillment.BusyMessage, error.Message);

        proceed.SetResult();
        await first;
    }

    // ==============================================================================================
    // One book, one operation — whichever admin button started it
    // ==============================================================================================

    /// <summary>
    /// A redraw asked for while this book is being re-prepared is refused, and deletes nothing.
    ///
    /// This is the race the per-stage semaphore could not see. Re-preparation leaves the pack
    /// reading <c>Completed</c> for its whole duration — minutes of building a press candidate and
    /// then writing it over the live blobs — so a redraw arriving in that window passes every status
    /// check it has and then deletes the eight spreads the press stage is laying out. Hangfire's
    /// per-pack lock does not apply: both of these are inline calls from the admin controller, and
    /// the attribute is a server filter that only runs when a worker performs a job.
    ///
    /// What makes the refusal safe rather than merely polite is the second assertion. The redraw
    /// deletes before it queues, so a redraw that got as far as storage would already have taken
    /// artwork away from a stage that is still using it.
    /// </summary>
    [Fact]
    public async Task A_redraw_asked_for_during_a_re_preparation_is_refused_before_it_deletes_anything()
    {
        var world = await HeldBookAsync();
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();
        var deletedBefore = world.Blobs.Deleted.Count;

        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Composer.OnCompose = () =>
        {
            inside.TrySetResult();
            proceed.Task.GetAwaiter().GetResult();
        };

        var reprepare = Task.Run(() => world.Job().RepreparePrintAsync(world.PackId, CancellationToken.None));
        if (await Task.WhenAny(inside.Task, reprepare) == reprepare) await reprepare;

        var refused = await world.Redraw().RequestAsync(RedrawRequest(world.PackId), CancellationToken.None);

        Assert.Equal(BekiRegenerationStatus.Refused, refused.Status);
        Assert.Equal(BekiRegeneration.BusyRefusal, refused.Message);
        Assert.Equal(deletedBefore, world.Blobs.Deleted.Count);
        Assert.Equal(0, world.Jobs.Enqueued);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);

        // And when the stage lets go, the same redraw goes through — the lock is a queue of one,
        // not a permanent refusal.
        proceed.SetResult();
        await reprepare;

        var queued = await world.Redraw().RequestAsync(RedrawRequest(world.PackId), CancellationToken.None);

        Assert.Equal(BekiRegenerationStatus.Queued, queued.Status);
        Assert.Equal(1, world.Jobs.Enqueued);
        Assert.NotEmpty(world.Blobs.Deleted);
    }

    /// <summary>
    /// The same race from the other side: a re-preparation asked for while this book is being
    /// claimed for a redraw is told the book is busy.
    ///
    /// Paused inside the redraw's compare-and-set, which is the honest place to stand — the pack
    /// still reads <c>Completed</c> with a published reading copy at that instant, so every check
    /// re-preparation makes about the book's state passes and only the lock stops it.
    /// </summary>
    [Fact]
    public async Task A_re_preparation_asked_for_during_a_redraw_is_refused()
    {
        var world = await HeldBookAsync();
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();

        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Packs.BeforeClaim = () =>
        {
            inside.TrySetResult();
            proceed.Task.GetAwaiter().GetResult();
        };

        var redraw = Task.Run(() => world.Redraw().RequestAsync(RedrawRequest(world.PackId), CancellationToken.None));
        if (await Task.WhenAny(inside.Task, redraw) == redraw) await redraw;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Job().RepreparePrintAsync(world.PackId, CancellationToken.None));

        Assert.Contains("Another operation is already running", error.Message, StringComparison.Ordinal);

        // Refused before it took a copy of anything, which is what "nothing was touched" means here.
        Assert.DoesNotContain(world.Blobs.Uploaded.Keys,
            key => key.StartsWith($"{world.UserId}/{world.PackId}/previous/", StringComparison.Ordinal));

        proceed.SetResult();
        Assert.Equal(BekiRegenerationStatus.Queued, (await redraw).Status);

        // Released on the way out rather than held for the process's life.
        var free = await new InProcessBekiPackLock()
            .TryAcquireAsync(world.PackId, TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(free);
        await free.DisposeAsync();
    }

    /// <summary>
    /// The fallback lock on its own: one holder per book, released when it is disposed.
    ///
    /// Acquired through two instances on purpose. The fulfilment service and the regeneration
    /// service each build their own fallback, so a per-instance dictionary would give them private
    /// gates and restore exactly the race the lock exists to close.
    /// </summary>
    [Fact]
    public async Task The_in_process_lock_admits_one_holder_per_book_and_reopens_on_dispose()
    {
        var packId = Guid.NewGuid();
        var held = await new InProcessBekiPackLock().TryAcquireAsync(packId, TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(held);

        Assert.Null(await new InProcessBekiPackLock()
            .TryAcquireAsync(packId, TimeSpan.Zero, CancellationToken.None));

        // Another book is another lock: one operator's re-preparation must not queue every other
        // book in the system behind it.
        var other = await new InProcessBekiPackLock()
            .TryAcquireAsync(Guid.NewGuid(), TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(other);
        await other.DisposeAsync();

        await held.DisposeAsync();

        var again = await new InProcessBekiPackLock()
            .TryAcquireAsync(packId, TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(again);

        // Disposed twice releases once: a second release would admit two callers at a time, which
        // is worse than the leak it would be covering.
        await again.DisposeAsync();
        await again.DisposeAsync();

        var third = await new InProcessBekiPackLock()
            .TryAcquireAsync(packId, TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(third);
        Assert.Null(await new InProcessBekiPackLock()
            .TryAcquireAsync(packId, TimeSpan.Zero, CancellationToken.None));
        await third.DisposeAsync();
    }

    /// <summary>
    /// The inline lock and the job's lock name one resource, so the two really do contend.
    ///
    /// Hangfire formats the attribute's pattern with the job's arguments —
    /// <c>String.Format("beki-pack:{0}", packId)</c> — and takes that name through the job's
    /// storage connection. <see cref="BekiPackLockResource.For"/> produces the identical string for
    /// a connection of its own. Asserted because the failure mode is silent: a pattern edited to
    /// anything else leaves both locks working perfectly and protecting nothing from each other.
    /// </summary>
    [Fact]
    public void The_job_lock_and_the_inline_lock_name_one_resource()
    {
        var attribute = Assert.Single(
            typeof(IBekiPackFulfillment)
                .GetMethod(nameof(IBekiPackFulfillment.ProcessAsync))!
                .GetCustomAttributes(typeof(DisableConcurrentExecutionAttribute), inherit: false)
                .Cast<DisableConcurrentExecutionAttribute>());

        var packId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        Assert.Equal(BekiPackLockResource.Pattern, attribute.Resource);
        Assert.Equal("beki-pack:11111111-2222-3333-4444-555555555555", BekiPackLockResource.For(packId));
        Assert.Equal(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, attribute.Resource, packId),
            BekiPackLockResource.For(packId));
    }

    private static BekiRegenerationRequest RedrawRequest(Guid packId) =>
        new(packId, BekiRegenerationScopes.Book, null, "the pictures are wrong", "operator@beki.ge");

    /// <summary>
    /// A book that never finished is recovery's case, and the button hands it over rather than
    /// refusing it — with recovery now finishing INTO print rather than nailing the slot to null.
    /// </summary>
    [Fact]
    public async Task A_failed_book_with_a_print_preflight_error_is_handed_to_recovery_and_finishes()
    {
        var world = await HeldBookAsync();
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();
        world.Packs.Force(AdventurePackStatus.Failed,
            "PRINT_PREFLIGHT_FAILED: the canonical PDF did not pass the mandatory production preflight.",
            pdfUrl: null);

        await world.Job().RepreparePrintAsync(world.PackId, CancellationToken.None);

        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
        Assert.Equal($"https://blob.test/{BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId)}",
            world.Packs.PdfUrl);

        using var status = JsonDocument.Parse(Encoding.UTF8.GetString(
            world.Blobs.Uploaded[BekiPackBlobs.PressStatusName(world.UserId, world.PackId)]));
        Assert.Empty(status.RootElement.GetProperty("failed_gates").EnumerateArray());

        var release = BekiReleaseGateReport.TryParse(Encoding.UTF8.GetString(
            world.Blobs.Uploaded[BekiPackBlobs.ReleaseGatesName(world.UserId, world.PackId)]))!;
        Assert.Equal(
            release.PrintReady && release.CustomerPdfMayPublish
                ? $"https://blob.test/{BekiPackBlobs.InteriorPdfName(world.UserId, world.PackId)}"
                : null,
            world.Packs.PrintPdfUrl);

        // No open hold left behind: recovery closes what it fixed, exactly as re-preparation does.
        Assert.Contains(world.Alarms.Resolved, closed => closed.CheckId == "PRINT_PREPARATION_HELD");
    }

    /// <summary>A legacy book has none of the artifacts this reads, and is refused rather than guessed at.</summary>
    [Fact]
    public async Task A_book_from_the_previous_pipeline_is_refused()
    {
        var world = await HeldBookAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Job(compositePipeline: false).RepreparePrintAsync(world.PackId, CancellationToken.None));

        Assert.Contains("canonical", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ==============================================================================================
    // What a changed contract means to a stage that draws nothing
    // ==============================================================================================

    /// <summary>
    /// A book stored under an older pipeline config is re-prepared, and the difference is written
    /// down rather than used as a refusal.
    ///
    /// This is the deployment's own case. The stage used to demand that the stored contract equal
    /// this deployment's exactly — a rule written for RESUME, where a changed term means the
    /// remaining spreads would be drawn differently. Re-preparation draws nothing: it re-composites
    /// bases that are already on disk and hash-verified, with the pose the composition receipt
    /// names, and exports the PDF again. A config version that moved after the artwork was finished
    /// cannot have changed the artwork, so refusing over it locked a finished book out of its own
    /// press files.
    ///
    /// What replaces the refusal is a record: <c>press-status.json</c> says which terms drifted, so
    /// an operator holding a re-prepared book can see the answer without correlating a log line
    /// with a deploy time.
    /// </summary>
    [Fact]
    public async Task A_book_stored_under_an_older_pipeline_config_is_re_prepared_and_the_drift_is_recorded()
    {
        var world = await HeldBookAsync();
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();

        var current = BekiCompositeContractTerms.Current("dinosaurs").PipelineConfigVersion;
        RewriteStoredTerms(world, terms => terms with { PipelineConfigVersion = "beki-pipeline-v1.0" });

        await world.Job().RepreparePrintAsync(world.PackId, CancellationToken.None);

        using var status = JsonDocument.Parse(Encoding.UTF8.GetString(
            world.Blobs.Uploaded[BekiPackBlobs.PressStatusName(world.UserId, world.PackId)]));
        var drift = status.RootElement.GetProperty("artwork_contract_drift").GetString();

        Assert.NotNull(drift);
        Assert.Contains("pipeline config", drift, StringComparison.Ordinal);
        Assert.Contains("beki-pipeline-v1.0", drift, StringComparison.Ordinal);
        Assert.Contains(current, drift, StringComparison.Ordinal);

        // And it really was prepared, not merely attempted: the press stage measured a clean
        // document and the book is still the family's own.
        Assert.Empty(status.RootElement.GetProperty("failed_gates").EnumerateArray());
        Assert.Equal("prepared", status.RootElement.GetProperty("interior").GetString());
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);

        // Nothing was drawn to get there — the single illustrate call is the original run's.
        Assert.Equal(1, world.Generator.IllustrateCalls);
    }

    /// <summary>
    /// A book stored against a different pose registry is still refused, and the refusal says which
    /// registry rather than "incomplete or incompatible".
    ///
    /// The registry is not a recipe: it names the nine approved PNGs and their hashes, so a revised
    /// one is a different character, and the pose this stage would composite onto the cover is not
    /// the pose that is on the stored pages. That is the case the old exact-match rule was really
    /// protecting, and it survives.
    ///
    /// Thrown as an <see cref="InvalidOperationException"/>, which is what the admin endpoint turns
    /// into a 409 carrying the message — and thrown from the loader, before the stage has taken a
    /// snapshot or written a byte.
    /// </summary>
    [Fact]
    public async Task A_book_stored_against_a_different_pose_registry_is_refused_and_writes_nothing()
    {
        var world = await HeldBookAsync();
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();

        var livePdf = world.Blobs.Uploaded[BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId)];
        var livePressStatus = world.Blobs.Uploaded[BekiPackBlobs.PressStatusName(world.UserId, world.PackId)];

        RewriteStoredTerms(world, terms => terms with { PoseRegistryVersion = "beki-pose-registry-v0" });
        world.Blobs.UploadOrder.Clear();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => world.Job().RepreparePrintAsync(world.PackId, CancellationToken.None));

        Assert.Contains("different pose registry or world", error.Message, StringComparison.Ordinal);
        Assert.Contains("beki-pose-registry-v0", error.Message, StringComparison.Ordinal);
        Assert.Contains("without a redraw", error.Message, StringComparison.Ordinal);

        // Refused before it touched anything: no upload of any kind, the book's files as they were,
        // and no snapshot folder from a promotion that never started.
        Assert.Empty(world.Blobs.UploadOrder);
        Assert.Equal(livePdf, world.Blobs.Uploaded[BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId)]);
        Assert.Equal(livePressStatus,
            world.Blobs.Uploaded[BekiPackBlobs.PressStatusName(world.UserId, world.PackId)]);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
        Assert.DoesNotContain(world.Blobs.Uploaded.Keys,
            key => key.StartsWith($"{world.UserId}/{world.PackId}/previous/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Rewrites the composite line of the manifest a real run wrote, leaving the eight spread lines
    /// and every stored picture exactly as they are.
    ///
    /// Through the parsed terms rather than by string surgery, so a test asking for "a different
    /// pose registry" cannot silently be editing the field beside it when the contract's shape
    /// changes.
    /// </summary>
    private static void RewriteStoredTerms(
        CompositePipelineFulfillmentTests.PackWorld world,
        Func<BekiCompositeContractTerms, BekiCompositeContractTerms> change)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var name = BekiPackBlobs.ManifestName(world.UserId, world.PackId);
        var manifest = JsonSerializer.Deserialize<BekiFulfillmentManifest>(
            world.Blobs.Uploaded[name], options)!;

        Assert.True(BekiCompositeContractTerms.TryParse(manifest.IllustrationContract[0], out var terms));

        var lines = manifest.IllustrationContract.ToArray();
        lines[0] = change(terms!).ToString();

        world.Blobs.Seed(name, JsonSerializer.SerializeToUtf8Bytes(
            manifest with { IllustrationContract = lines }, options));
    }

    /// <summary>
    /// The alarm service's side of the closure: the right book, the right check, and one of the four
    /// words the alarms table's CHECK constraint permits.
    ///
    /// Asserted against a repository double rather than a database because the SQL is one UPDATE and
    /// the thing that can actually be wrong here is the arguments — a resolution the constraint
    /// refuses would close nothing and say nothing.
    /// </summary>
    [Fact]
    public async Task Closing_a_hold_names_the_book_the_check_and_a_permitted_resolution()
    {
        var repository = new RecordingAlarmRepository();
        var service = new BekiAlarmService(
            repository, new SilentAdminNotifier(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BekiAlarmService>.Instance);
        var packId = Guid.NewGuid();

        await service.ResolveForPackAsync(packId, "PRINT_PREPARATION_HELD", "press files published", default);

        var (recordedPack, checkId, reviewedBy, resolution) = Assert.Single(repository.Resolved);
        Assert.Equal(packId, recordedPack);
        Assert.Equal("PRINT_PREPARATION_HELD", checkId);
        Assert.Equal(BekiAlarmService.ReprepareReviewer, reviewedBy);
        Assert.Contains(resolution, BekiAlarmResolutions.All);
    }

    /// <summary>A storage failure while closing an alarm is a stale row, never a failed book.</summary>
    [Fact]
    public async Task A_failure_while_closing_an_alarm_is_swallowed()
    {
        var service = new BekiAlarmService(
            new RecordingAlarmRepository { Throw = true }, new SilentAdminNotifier(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BekiAlarmService>.Instance);

        await service.ResolveForPackAsync(Guid.NewGuid(), "PRINT_PREPARATION_HELD", "n/a", default);
    }

    // ==============================================================================================
    // The endpoint
    // ==============================================================================================

    /// <summary>
    /// The console's answer says what actually happened to the printer's files, read back off the
    /// book rather than assumed — because the verdict is taken inside the job and an optimistic
    /// message is how an operator concludes a book is ready when it is still held.
    /// </summary>
    [Fact]
    public async Task The_endpoint_reports_published_press_files()
    {
        var packId = Guid.NewGuid();
        var packs = new CompositePipelineFulfillmentTests.FakePacks(new AdventurePack
        {
            Id = packId,
            UserId = Guid.NewGuid(),
            Status = AdventurePackStatus.Completed,
            PdfUrl = "https://blob.test/book.pdf",
            PrintPdfUrl = "https://blob.test/book.pdf",
        });

        var response = Assert.IsType<OkObjectResult>(
            await Controller(packs, new SucceedingFulfillment()).RepreparePrint(
                packId, new SucceedingFulfillment(), CancellationToken.None));

        Assert.Contains("print files published", Message(response), StringComparison.Ordinal);
        Assert.Contains("No illustrations were regenerated", Message(response), StringComparison.Ordinal);
    }

    /// <summary>The same call on a book whose printing is still held names the gates that hold it.</summary>
    [Fact]
    public async Task The_endpoint_names_the_gates_that_still_hold_printing()
    {
        var packId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var packs = new CompositePipelineFulfillmentTests.FakePacks(new AdventurePack
        {
            Id = packId,
            UserId = userId,
            Status = AdventurePackStatus.Completed,
            PdfUrl = "https://blob.test/book.pdf",
        });
        var blobs = new CompositePipelineFulfillmentTests.FakeBlobs();
        blobs.Seed(BekiPackBlobs.PressStatusName(userId, packId), Encoding.UTF8.GetBytes(
            """{"failed_gates":["PRESS_RESOLUTION"],"reason":"page 3 measures 1536×717 px"}"""));

        var response = Assert.IsType<OkObjectResult>(
            await Controller(packs, new SucceedingFulfillment(), blobs).RepreparePrint(
                packId, new SucceedingFulfillment(), CancellationToken.None));

        Assert.Contains("printing held: PRESS_RESOLUTION", Message(response), StringComparison.Ordinal);
    }

    /// <summary>A refusal is a 409 carrying the reason, not a 500 and not a silent success.</summary>
    [Fact]
    public async Task A_refused_re_preparation_is_a_conflict_with_its_reason()
    {
        var packId = Guid.NewGuid();
        var packs = new CompositePipelineFulfillmentTests.FakePacks(new AdventurePack
        {
            Id = packId, UserId = Guid.NewGuid(), Status = AdventurePackStatus.Completed,
        });
        var refusing = new RefusingFulfillment("Print re-preparation is already running for this book.");

        var response = Assert.IsType<ConflictObjectResult>(
            await Controller(packs, refusing).RepreparePrint(packId, refusing, CancellationToken.None));

        Assert.Equal("Print re-preparation is already running for this book.", Message(response));
    }

    private static AdminOrdersController Controller(
        CompositePipelineFulfillmentTests.FakePacks packs,
        IBekiPackFulfillment fulfillment,
        CompositePipelineFulfillmentTests.FakeBlobs? blobs = null) =>
        // The collaborators this action never touches are null on purpose — the same convention
        // the approval tests on this controller use. It reads the pack, storage and the job.
        new(reporting: null!,
            packs,
            orderRepository: null!,
            printOrders: null!,
            blobs ?? new CompositePipelineFulfillmentTests.FakeBlobs(),
            generationService: null!,
            orderService: null!,
            regeneration: null!,
            packageExport: null!,
            releaseGates: null!,
            reconciliation: null!,
            releasePolicy: null!,
            alarms: null!,
            userContext: null!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AdminOrdersController>.Instance);

    private static string Message(ObjectResult result) =>
        (string)result.Value!.GetType().GetProperty("message")!.GetValue(result.Value)!;

    private sealed class SucceedingFulfillment : IBekiPackFulfillment
    {
        public Task ProcessAsync(Guid packId, Guid runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RepreparePrintAsync(Guid packId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RefusingFulfillment(string reason) : IBekiPackFulfillment
    {
        public Task ProcessAsync(Guid packId, Guid runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RepreparePrintAsync(Guid packId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(reason);
    }

    // ==============================================================================================
    // Harness
    // ==============================================================================================

    /// <summary>
    /// A finished book with printing held: exactly the state the deployment's latest book was in.
    ///
    /// Produced by running the real fulfilment job over the shared harness with the composer
    /// returning the book as it comes out of composition BEFORE normalization — screen-sized
    /// rasters on the locked sheets — so PRESS_RESOLUTION fails from measurement and the blocker is
    /// raised. Nothing about the state is faked into storage.
    /// </summary>
    private static async Task<CompositePipelineFulfillmentTests.PackWorld> HeldBookAsync()
    {
        var world = new CompositePipelineFulfillmentTests.PackWorld();
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalScreenBook();

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
        Assert.Null(world.Packs.PrintPdfUrl);
        Assert.DoesNotContain(world.Alarms.Raised, alarm => alarm.CheckId == "PRINT_PREPARATION_HELD");

        return world;
    }

    private sealed class RecordingAlarmRepository : IBekiAlarmRepository
    {
        public bool Throw { get; init; }

        public List<(Guid PackId, string CheckId, string ReviewedBy, string Resolution)> Resolved { get; } = [];

        public Task<int> ResolveOpenForPackAsync(
            Guid packId, string checkId, string reviewedBy, string resolution, CancellationToken cancellationToken)
        {
            if (Throw) throw new IOException("Test alarms table unavailable");
            Resolved.Add((packId, checkId, reviewedBy, resolution));
            return Task.FromResult(1);
        }

        public Task<BekiAlarmRaiseOutcome> RaiseAsync(BekiAlarmRaise raise, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<BekiAlarm>> ListRecentAsync(int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<BekiAlarm?> GetAsync(Guid alarmId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> ReviewAsync(
            Guid alarmId, string reviewedBy, string resolution, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> CountOpenAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SilentAdminNotifier : AdventurePacks.Api.Services.Interfaces.IAdminNotifier
    {
        public Task BookFailedAsync(Guid packId, string reason, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task OrderPaidAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PrintOrderPlacedAsync(
            PrintOrder printOrder, string? bookTitle, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
