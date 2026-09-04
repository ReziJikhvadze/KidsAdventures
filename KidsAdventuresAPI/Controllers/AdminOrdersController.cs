using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Repositories.Implementations;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Controllers;

/// <summary>
/// The order list and everything hanging off one order: who bought it, what they got, whether
/// they ever opened it, and the file itself.
///
/// Every query here deliberately crosses the per-user boundary that the customer-facing
/// controllers enforce, which is precisely why it is gated behind the Admin policy and kept in
/// its own controller rather than added as a flag to the parent-facing ones.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin")]
public sealed class AdminOrdersController(
    IAdminReportingRepository reporting,
    IAdventurePackRepository packRepository,
    IOrderRepository orderRepository,
    IPrintOrderService printOrders,
    IBlobStorageService blobStorage,
    IAdventureGenerationService generationService,
    IOrderService orderService,
    IBekiRegeneration regeneration,
    BekiPackageExport packageExport,
    BekiReleaseGates releaseGates,
    IBekiReleaseReconciliation reconciliation,
    IBekiReleasePolicyService releasePolicy,
    IBekiAlarmService alarms,
    IUserContextService userContext,
    ILogger<AdminOrdersController> logger) : ControllerBase
{
    /// <summary>
    /// Order list across all customers. Paged rather than unbounded — an admin list that
    /// selects every row is fine on day one and a timeout by year two.
    /// </summary>
    [HttpGet("orders")]
    public async Task<ActionResult<AdminOrderListResponse>> Orders(
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string? flag = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 100);

        if (!string.IsNullOrWhiteSpace(status) &&
            !Enum.TryParse<OrderStatus>(status, ignoreCase: true, out _))
        {
            return BadRequest(new { message = "Unknown order status." });
        }

        // Refused rather than ignored: a filter that silently does nothing shows a full list to
        // someone who asked for the short one, and they read it as "nothing is wrong". The set of
        // names lives beside the predicates they select, so a filter this accepts is always one
        // the SQL implements.
        if (!string.IsNullOrWhiteSpace(flag) && !AdminReportingRepository.Flags.Contains(flag))
        {
            return BadRequest(new { message = "Unknown filter." });
        }

        return Ok(await reporting.GetOrdersAsync(status, search, flag, page, pageSize, cancellationToken));
    }

    /// <summary>
    /// One order with its customer, its book and its parcel — plus why its book is being held,
    /// when it is.
    ///
    /// The row already says THAT a finished book has no published file; only the stored verdict
    /// says whether that is a person who has not looked yet or a gate that failed, and those two
    /// want different things done about them. Reading it costs one blob fetch, which is why it
    /// happens here — on one order somebody deliberately opened — and not once per row of a list.
    /// A book with no stored evaluation simply reports neither, the same way the gates endpoint
    /// reports a null verdict rather than an error.
    /// </summary>
    [HttpGet("orders/{id:guid}")]
    public async Task<ActionResult<AdminOrderDetailResponse>> OrderDetail(
        Guid id,
        CancellationToken cancellationToken)
    {
        var detail = await reporting.GetOrderDetailAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        // The service's own rule, asked rather than mirrored: the console greys the button out on
        // this answer and RequeueFulfilmentAsync refuses on the same method, so they cannot drift.
        detail.CanRetry = await orderService.CanRedriveAsync(id, cancellationToken);

        if (detail.Book is not null)
        {
            var pack = await packRepository.GetByIdNoOwnershipAsync(detail.Book.Id, cancellationToken);
            if (pack is not null)
            {
                var report = await ReadReleaseGatesAsync(pack.UserId, pack.Id, cancellationToken);
                detail.AwaitingReview = report?.AwaitingHumanReview ?? false;
                detail.FailingGateCount = report?.FailingGates.Count ?? 0;

                detail.CanRegenerate = regeneration.CanRegenerate(pack);
                await DescribeArtworkAsync(detail.Book, pack, cancellationToken);
            }

            // Reviewed ones included, newest first: the question this panel is opened with is
            // "has this happened to this book before", and a list of only the open ones answers
            // "is it happening right now", which is a different question with the same shape.
            detail.Alarms = (await alarms.ListForPackAsync(detail.Book.Id, cancellationToken))
                .OrderByDescending(alarm => alarm.LastSeenUtc)
                .Select(ToRow)
                .ToList();
        }

        return Ok(detail);
    }

    /// <summary>
    /// Which pictures this book actually has in storage.
    ///
    /// Probed rather than inferred, because the interesting case is precisely the one where the
    /// status does not say: a book that stopped on spread five has four pictures an operator can
    /// look at and judge, and until now the console could only report "Failed". Eight existence
    /// checks plus three, issued together, on one order somebody deliberately opened — never per
    /// row of a list, which is why none of this is on <see cref="AdminOrderRow"/>.
    /// </summary>
    private async Task DescribeArtworkAsync(
        AdminOrderBook book, Domain.Entities.AdventurePack pack, CancellationToken cancellationToken)
    {
        var spreadProbes = Enumerable
            .Range(1, Domain.Story.BookFormat.SpreadCount)
            .Select(number => blobStorage.ExistsAsync(
                BekiPackBlobs.SpreadName(pack.UserId, pack.Id, number), cancellationToken))
            .ToArray();

        var coverProbe = blobStorage.ExistsAsync(
            BekiPackBlobs.CoverFrontName(pack.UserId, pack.Id), cancellationToken);

        var sheetProbes = BekiPackBlobs.RenderedArtifacts
            .Select(artifact => blobStorage.ExistsAsync(
                BekiPackBlobs.ContactSheetName(pack.UserId, pack.Id, artifact), cancellationToken))
            .ToArray();

        try
        {
            await Task.WhenAll(spreadProbes.Concat([coverProbe]).Concat(sheetProbes));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Storage being unreachable must not take the whole panel down with it: everything
            // else on this response came from SQL and is still worth showing.
            logger.LogWarning(ex, "Artwork probe failed for book {BookId}.", pack.Id);
            return;
        }

        book.SpreadsAvailable = spreadProbes
            .Select((probe, index) => (Exists: probe.Result, Number: index + 1))
            .Where(entry => entry.Exists)
            .Select(entry => entry.Number)
            .ToList();

        // The cropped front board is the canonical one; a pack that only has its own cover column
        // still has a picture to show, and saying "no cover" about it would be wrong.
        book.HasCoverImage = coverProbe.Result || !string.IsNullOrWhiteSpace(pack.CoverImageUrl);
        book.HasContactSheet = sheetProbes.Any(probe => probe.Result);
    }

    /// <summary>
    /// The book's PDF, streamed through the API.
    ///
    /// Its own route rather than the customer one, which scopes by owner: an operator looking at
    /// a support ticket is not the owner and never will be. The blob URL is not handed out
    /// either — it is storage internals, and a link that outlives this request is a link that
    /// leaks a child's book.
    /// </summary>
    /// <param name="kind">
    /// <c>reading</c> or <c>print</c>. Omitted, it keeps the behaviour the console has always had:
    /// the print file for a print order, the reading copy otherwise. Named, it overrides that —
    /// a digital order's book still has a press interior worth sending to a supplier, and a print
    /// order's operator sometimes wants to see what the PARENT can open.
    /// </param>
    [HttpGet("orders/{id:guid}/pdf")]
    public async Task<IActionResult> OrderPdf(
        Guid id, [FromQuery] string? kind = null, CancellationToken cancellationToken = default)
    {
        var detail = await reporting.GetOrderDetailAsync(id, cancellationToken);
        if (detail?.Book is null)
        {
            return NotFound(new { message = "ამ შეკვეთას წიგნი არ აქვს." });
        }

        var pack = await packRepository.GetByIdNoOwnershipAsync(detail.Book.Id, cancellationToken);

        // `kind` remains accepted for backwards-compatible admin links, but both database columns
        // must point to the same canonical storage object in the final pipeline.
        var url = pack?.PdfUrl ?? pack?.PrintPdfUrl;

        // 409 rather than 404: the book exists and the file does not exist *yet*, which is a
        // different thing to tell an operator — one of them has a button next to it.
        if (string.IsNullOrWhiteSpace(url))
        {
            return Conflict(new { message = "PDF ჯერ არ დაგენერირებულა." });
        }

        try
        {
            var bytes = await blobStorage.DownloadBytesFromStoredUrlAsync(url, cancellationToken);

            /*
              The name says which of the two files this is, in every case rather than only the
              substituted one.

              An operator downloads both from one panel and ends up with them in one folder, and
              two files called beki-<id>.pdf are two files nobody can tell apart an hour later.
              The READING-COPY-not-print spelling is kept exactly as it was for the fallback,
              because that string is what an operator forwarding to a binder is meant to notice.
            */
            var name = $"beki-{detail.Book.Id}-book.pdf";

            return File(bytes, "application/pdf", name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin PDF download failed for book {BookId}.", detail.Book.Id);
            return NotFound(new { message = "PDF ფაილი საცავში ვერ მოიძებნა." });
        }
    }

    private const string ReadingKind = "reading";

    private const string PrintKind = "print";

    /// <summary>
    /// The book's whole handback package as one zip: press interior and cover with their
    /// preflight reports, the reading copy, the Visual Scenario, the review, every spread with
    /// its base and exact-Beki receipt, and a contents listing that says what is missing and
    /// why some things are excluded on purpose. This is the download an operator sends to the
    /// supplier; the parent-facing book is served elsewhere and never bundles any of this.
    /// </summary>
    [HttpGet("orders/{id:guid}/package")]
    public async Task<IActionResult> OrderHandbackPackage(Guid id, CancellationToken cancellationToken)
    {
        var detail = await reporting.GetOrderDetailAsync(id, cancellationToken);
        if (detail?.Book is null)
        {
            return NotFound(new { message = "ამ შეკვეთას წიგნი არ აქვს." });
        }

        var pack = await packRepository.GetByIdNoOwnershipAsync(detail.Book.Id, cancellationToken);
        if (pack is null)
        {
            return NotFound();
        }

        var package = await packageExport.BuildAsync(
            pack.UserId, pack.Id, pack.Title, cancellationToken);

        // The audit's own naming, so an operator forwarding this to the supplier is forwarding a
        // file whose name means something in the supplier's vocabulary rather than in ours.
        return File(package, "application/zip", BekiPackageExport.PackageFileName(pack.Id));
    }

    /// <summary>
    /// What the sixteen hard gates make of this book, and what is therefore being withheld.
    ///
    /// Read from the stored verdict rather than recomputed, deliberately: the console is showing an
    /// operator the same document the handback package carries and the approval endpoint checks
    /// against, and a view that computed its own answer could disagree with both.
    /// </summary>
    [HttpGet("orders/{id:guid}/release-gates")]
    public async Task<ActionResult<AdminReleaseGatesResponse>> ReleaseGates(
        Guid id, CancellationToken cancellationToken)
    {
        var pack = await PackForOrderAsync(id, cancellationToken);
        if (pack is null)
        {
            return NotFound(new { message = "ამ შეკვეთას წიგნი არ აქვს." });
        }

        var report = await ReadReleaseGatesAsync(pack.UserId, pack.Id, cancellationToken);

        // 200 with a null verdict rather than 404: "this book has no gate evaluation" is a fact the
        // console should show, not an error it should hide. Books fulfilled before the gates existed
        // are exactly this case.
        return Ok(ToResponse(report));
    }

    /// <summary>
    /// A reviewer's signature on the rendered contact sheet — the human half of VISUAL_QA
    /// (plan D8, amendments A2 and A5).
    ///
    /// Three things make this more than a flag. It records WHO, from the authenticated admin rather
    /// than from the request body, because an approval nobody can be asked about is not a resolution.
    /// It records WHICH PIXELS, by the contact sheet's SHA-256, and refuses a sheet that is no longer
    /// the current one — a book re-rendered after approval is a book nobody has approved. And it is
    /// ATOMIC in the sense that matters: it writes the approval, re-runs the whole evaluation against
    /// stored artifacts, rewrites the verdict, and publishes whatever the new verdict unlocks, inside
    /// one request, so there is never a window where the approval exists and the book is still held.
    /// </summary>
    [HttpPost("orders/{id:guid}/approve-review")]
    public async Task<ActionResult<AdminReleaseGatesResponse>> ApproveReview(
        Guid id,
        [FromBody] AdminApproveReviewRequest request,
        CancellationToken cancellationToken)
    {
        var pack = await PackForOrderAsync(id, cancellationToken);
        if (pack is null)
        {
            return NotFound(new { message = "ამ შეკვეთას წიგნი არ აქვს." });
        }

        var current = await ReadReleaseGatesAsync(pack.UserId, pack.Id, cancellationToken);
        if (current is null)
        {
            return Conflict(new
            {
                message = "ამ წიგნს ჯერ არ აქვს გამოშვების შემოწმება — დასადასტურებელი არაფერია."
            });
        }

        if (current.ContactSheetSha256 is not { Length: > 0 } sheet)
        {
            return Conflict(new
            {
                message = "რენდერის კონტაქტ-ფურცელი არ არსებობს, ამიტომ დასადასტურებელი სურათი არ არის."
            });
        }

        // Amendment A2: the approval is of a specific rendering. A reviewer who looked at an older
        // contact sheet is refused rather than recorded, because the alternative is an approval that
        // means "somebody once looked at some version of this book".
        if (!string.Equals(request.ContactSheetSha256?.Trim(), sheet, StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new
            {
                message = "დადასტურება ეხება სხვა კონტაქტ-ფურცელს — გვერდი განაახლეთ და ხელახლა ნახეთ.",
                expected = sheet,
            });
        }

        var approval = new BekiHumanApproval(
            userContext.GetEmail() is { Length: > 0 } email ? email : userContext.GetUserId().ToString(),
            DateTimeOffset.UtcNow,
            sheet,
            string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim());

        await blobStorage.UploadAsync(
            BekiPackBlobs.HumanApprovalName(pack.UserId, pack.Id),
            System.Text.Encoding.UTF8.GetBytes(approval.ToJson()),
            "application/json",
            cancellationToken);

        /*
          Under the policy that is in force NOW, read once and stated out loud.

          This call used to pass no policy at all, and the evaluator's optional argument quietly
          substituted the shipped defaults. The result was that an operator's override was ignored at
          exactly the moment it mattered most: a check they had tightened to `blocker` was re-judged
          as a flag and the book published; a check they had loosened to `flag` was re-judged as a
          blocker and the signed-off book stayed withheld. Either way the stored verdict — the
          document that answers "why was this published?" months later — was overwritten with a
          decision nobody had made. (Review finding 1.)
        */
        var policy = await releasePolicy.SnapshotAsync(cancellationToken);

        var revised = await releaseGates.EvaluateAsync(
            pack.UserId, pack.Id, cancellationToken, policy);

        await blobStorage.UploadAsync(
            BekiPackBlobs.ReleaseGatesName(pack.UserId, pack.Id),
            System.Text.Encoding.UTF8.GetBytes(revised.ToJson()),
            "application/json",
            cancellationToken);

        /*
          The publication this approval unlocked — through the shared writer rather than a copy of
          it, because the approval endpoint, the withheld sweep and the fulfilment job's own late
          publication all write the same two columns under the same compare-and-set, and three
          copies of that guard would be three places for it to drift.

          What is measured first is whether anything was SUPPOSED to be published: the file is
          unlocked, the pack is still Completed, and no URL is on it yet. When that was true and
          nothing was published, the approval reached nobody — the sweep buried the pack between
          the read and the write, or the reading copy is not in storage — and until now that was a
          warning in a log with a family at the other end of it waiting for a book somebody had
          already signed off. It is an alarm.
        */
        var publicationExpected = revised.CustomerPdfMayPublish
            && string.IsNullOrWhiteSpace(pack.PdfUrl)
            && pack.Status == AdventurePackStatus.Completed;

        // The PARENT's half of the answer, specifically. A press column written by the same call is
        // not what an approval was about, and counting it would silence the alarm below on exactly
        // the book that needs it: signed off, press file out, family still waiting.
        var published = await reconciliation.PublishUnlockedFilesAsync(pack, revised, cancellationToken);

        if (publicationExpected && !published.CustomerPdf)
        {
            await alarms.RaiseAsync(
                new BekiAlarmRaise(
                    pack.Id,
                    id,
                    pack.UserId,
                    "publish_after_review",
                    BekiReleaseSeverity.Blocker,
                    $"{approval.ApprovedBy} approved this book and the reading copy was still not "
                    + "published. The pack is no longer Completed, or the file is missing from "
                    + "storage. The family is waiting on a book that has been signed off.",
                    BekiPackBlobs.ReadingPdfName(pack.UserId, pack.Id),
                    // Keyed on the sheet, so approving the same rendering twice is one alarm and a
                    // re-render that fails again is a new one.
                    BekiAlarmEvidence.ForAttempt("approve_review", pack.Id, sheet[..12])),
                cancellationToken);
        }

        logger.LogInformation(
            "Beki pack {PackId}: {Approver} signed off contact sheet {Sheet}; the verdict is now "
            + "{Verdict} ({Failing}).",
            pack.Id, approval.ApprovedBy, sheet[..12], revised.Verdict,
            revised.FailingGates.Count == 0 ? "no failing gates" : string.Join(", ", revised.FailingGates));

        return Ok(ToResponse(revised));
    }

    private async Task<Domain.Entities.AdventurePack?> PackForOrderAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        var detail = await reporting.GetOrderDetailAsync(orderId, cancellationToken);

        return detail?.Book is null
            ? null
            : await packRepository.GetByIdNoOwnershipAsync(detail.Book.Id, cancellationToken);
    }

    private async Task<BekiReleaseGateReport?> ReadReleaseGatesAsync(
        Guid userId, Guid packId, CancellationToken cancellationToken)
    {
        var name = BekiPackBlobs.ReleaseGatesName(userId, packId);

        try
        {
            if (!await blobStorage.ExistsAsync(name, cancellationToken))
            {
                return null;
            }

            await using var stream = await blobStorage.DownloadAsync(name, cancellationToken);
            using var reader = new StreamReader(stream);

            return BekiReleaseGateReport.TryParse(await reader.ReadToEndAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Release gates for pack {PackId} could not be read.", packId);
            return null;
        }
    }

    private static AdminReleaseGatesResponse ToResponse(BekiReleaseGateReport? report) => new(
        report?.Verdict,
        report?.EvaluatedAtUtc,
        report?.FailingGates ?? [],
        report?.AwaitingHumanReview ?? false,
        report?.ContactSheetSha256,
        report?.CustomerPdfMayPublish ?? false,
        report?.PressFilesMayPublish ?? false,
        report?.Gates
            .Select(gate => new AdminReleaseGate(gate.Id, gate.Status, gate.Class, gate.Detail))
            .ToList() ?? []);

    /// <summary>
    /// Builds a PDF for a book that has none. Reuses the customer-facing job, which already
    /// claims the pack and refuses to run twice.
    /// </summary>
    [HttpPost("books/{bookId:guid}/generate-pdf")]
    public async Task<IActionResult> GeneratePdf(Guid bookId, CancellationToken cancellationToken)
    {
        var pack = await packRepository.GetByIdNoOwnershipAsync(bookId, cancellationToken);
        if (pack is null)
        {
            return NotFound();
        }

        await generationService.QueuePdfGenerationAsync(pack.UserId, bookId, cancellationToken);
        return Accepted(new { status = AdventurePackStatus.GeneratingPdf.ToString() });
    }

    /// <summary>
    /// Re-runs fulfilment for a paid order whose book never arrived. The most common support
    /// ticket there is, and until now the console could show it and do nothing about it.
    ///
    /// The order is re-driven rather than the book, because the order is what knows which
    /// pipeline this book belongs to — <c>BookFulfillmentService</c> picks Beki or legacy from
    /// the draft, and a retry that went straight at a generation job would guess.
    /// </summary>
    [HttpPost("orders/{id:guid}/retry")]
    public async Task<IActionResult> RetryFulfilment(Guid id, CancellationToken cancellationToken)
    {
        var queued = await orderService.RequeueFulfilmentAsync(id, cancellationToken);

        if (!queued)
        {
            return Conflict(new
            {
                message = "ხელახლა გაშვება მხოლოდ გადახდილ და შეუსრულებელ შეკვეთაზეა შესაძლებელი."
            });
        }

        // Named in the log, because a re-drive redraws a book and the next person asking "why did
        // this run twice" deserves an answer that is not "the sweep, probably".
        logger.LogInformation("{Operator} re-queued fulfilment for order {OrderId}.", OperatorName(), id);

        return Accepted(new { message = "შეკვეთა რიგში ჩადგა." });
    }

    /// <summary>
    /// The two marks an operator puts on an order by hand: the money went back, or this is never
    /// going to ship.
    ///
    /// Deliberately not a general status setter. Every other transition is written by the thing
    /// that knows it happened — the gateway marks Paid, fulfilment marks Fulfilled — and a console
    /// that could set any status is a console that can tell the system a payment arrived.
    ///
    /// The transitions are checked here AND in the SQL, and both are needed. Here, so the operator
    /// gets a sentence explaining the refusal; in the UPDATE, so two admins clicking at once cannot
    /// produce a refund of a cancelled order.
    /// </summary>
    [HttpPost("orders/{id:guid}/status")]
    public async Task<IActionResult> SetOrderStatus(
        Guid id,
        [FromBody] AdminSetOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<OrderStatus>(request.Status?.Trim(), ignoreCase: true, out var target)
            || target is not (OrderStatus.Refunded or OrderStatus.Cancelled))
        {
            return BadRequest(new { message = "ხელით მხოლოდ „Refunded“ ან „Cancelled“ დაიშვება." });
        }

        var order = await orderRepository.GetByIdAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound(new { message = "შეკვეთა ვერ მოიძებნა." });
        }

        /*
          Which order may become which.

          Refunded only from Paid or Fulfilled, because a refund is a statement about money that
          was actually taken — marking an unpaid order refunded would put a lie in the ledger.
          Cancelled only from Pending or Paid: a fulfilled order has a book behind it, and
          cancelling that is a refund with the parent's copy left in their library.
        */
        var allowedFrom = target == OrderStatus.Refunded
            ? new[] { OrderStatus.Paid, OrderStatus.Fulfilled }
            : [OrderStatus.Pending, OrderStatus.Paid];

        if (!allowedFrom.Contains(order.Status))
        {
            return Conflict(new
            {
                message = target == OrderStatus.Refunded
                    ? "დაბრუნება მხოლოდ გადახდილ ან შესრულებულ შეკვეთაზეა შესაძლებელი."
                    : "გაუქმება მხოლოდ დაუმუშავებელ ან გადახდილ შეკვეთაზეა შესაძლებელი.",
            });
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var operatorName = OperatorName();

        // Prefixed, so a line a person typed is never read back as one the pipeline wrote. The
        // failure-reason column is the same one generation failures land in.
        var reason = note is null
            ? $"admin:{operatorName}: {target}"
            : $"admin:{operatorName}: {note}";

        var written = await orderRepository.TrySetAdminStatusAsync(
            id, target, allowedFrom, reason, cancellationToken);

        if (!written)
        {
            return Conflict(new { message = "შეკვეთის სტატუსი შეიცვალა — გვერდი განაახლეთ." });
        }

        /*
          And the parcel goes with it.

          A cancelled order whose print order is still sitting in the queue is a book that gets
          printed and posted to somebody who is not being charged for it. Only while it has not
          shipped: once a parcel is with a courier, cancelling the row would be a record saying it
          was never sent.
        */
        if (target == OrderStatus.Cancelled)
        {
            var parcel = await printOrders.TryCancelForOrderAsync(id, cancellationToken);
            if (parcel)
            {
                logger.LogInformation("Order {OrderId}: its parcel was cancelled with it.", id);
            }
        }

        logger.LogWarning(
            "{Operator} marked order {OrderId} as {Status}. Note: {Note}",
            operatorName, id, target, note ?? "(none)");

        return Ok(new { status = target.ToString() });
    }

    // -- the book's pictures ---------------------------------------------------------------

    /// <summary>
    /// One spread's artwork, streamed.
    ///
    /// Through the API rather than as a storage URL, for the reason the PDF is: a link that
    /// outlives this request is a link that leaks a child's book. Cached privately for five
    /// minutes because the panel re-renders on every poll and these are megabyte PNGs; a spread
    /// that has just been redrawn is behind a fresh page load anyway.
    /// </summary>
    [HttpGet("books/{bookId:guid}/spreads/{spread:int}")]
    public async Task<IActionResult> BookSpread(
        Guid bookId, int spread, CancellationToken cancellationToken)
    {
        if (spread is < 1 or > Domain.Story.BookFormat.SpreadCount)
        {
            return NotFound();
        }

        var pack = await packRepository.GetByIdNoOwnershipAsync(bookId, cancellationToken);

        return pack is null
            ? NotFound()
            : await ImageAsync(BekiPackBlobs.SpreadName(pack.UserId, pack.Id, spread), cancellationToken);
    }

    /// <summary>
    /// The cover: the cropped front board when it exists, and otherwise whatever the pack's own
    /// cover column points at.
    ///
    /// The order matters. The front board is cut from the single cover master, so it is the
    /// picture that agrees with the printed book; the pack's column can still hold an adopted
    /// preview cover on a book made before that correction, and showing it is better than showing
    /// nothing as long as it is second.
    /// </summary>
    [HttpGet("books/{bookId:guid}/cover")]
    public async Task<IActionResult> BookCover(Guid bookId, CancellationToken cancellationToken)
    {
        var pack = await packRepository.GetByIdNoOwnershipAsync(bookId, cancellationToken);
        if (pack is null)
        {
            return NotFound();
        }

        var front = BekiPackBlobs.CoverFrontName(pack.UserId, pack.Id);

        if (await ExistsAsync(front, cancellationToken))
        {
            return await ImageAsync(front, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(pack.CoverImageUrl))
        {
            return NotFound();
        }

        try
        {
            var bytes = await blobStorage.DownloadBytesFromStoredUrlAsync(
                pack.CoverImageUrl, cancellationToken);
            return Png(bytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cover image for book {BookId} could not be read.", bookId);
            return NotFound();
        }
    }

    /// <summary>
    /// The contact sheet a reviewer signs — the rendering of one finished document, all its pages
    /// on one image.
    /// </summary>
    /// <param name="artifact">
    /// <c>canonical</c>, or one of the former UI aliases. Every accepted value resolves to the one
    /// canonical book's contact sheet.
    /// </param>
    [HttpGet("books/{bookId:guid}/contact-sheet")]
    public async Task<IActionResult> BookContactSheet(
        Guid bookId, [FromQuery] string artifact = "digital", CancellationToken cancellationToken = default)
    {
        var name = artifact?.Trim().ToLowerInvariant() switch
        {
            "canonical" or "digital" or "press" or "cover" => BekiPackBlobs.CanonicalRenderArtifact,
            _ => null,
        };

        if (name is null)
        {
            return BadRequest(new { message = "ასეთი კონტაქტ-ფურცელი არ არსებობს." });
        }

        var pack = await packRepository.GetByIdNoOwnershipAsync(bookId, cancellationToken);

        return pack is null
            ? NotFound()
            : await ImageAsync(
                BekiPackBlobs.ContactSheetName(pack.UserId, pack.Id, name), cancellationToken);
    }

    /// <summary>
    /// Draws part or all of a paid book again.
    ///
    /// The one route in this console that spends money: every spread is a paid image call, so the
    /// browser reaches it only from a dialog that says so and asks for a reason, and the reason is
    /// required here too rather than only there. What it actually does — which stored bytes go,
    /// how the pack is claimed, which alarm records the spend — is <see cref="IBekiRegeneration"/>'s;
    /// this action is the boundary and the operator's identity.
    /// </summary>
    [HttpPost("books/{bookId:guid}/regenerate")]
    public async Task<IActionResult> RegenerateBook(
        Guid bookId,
        [FromBody] AdminRegenerateBookRequest request,
        CancellationToken cancellationToken)
    {
        var result = await regeneration.RequestAsync(
            new BekiRegenerationRequest(
                bookId,
                request.Scope,
                request.Spread,
                request.Reason ?? string.Empty,
                OperatorName()),
            cancellationToken);

        return result.Status switch
        {
            BekiRegenerationStatus.Queued => Accepted(new { message = result.Message }),
            BekiRegenerationStatus.NotFound => NotFound(new { message = result.Message }),
            _ => Conflict(new { message = result.Message }),
        };
    }

    // -- helpers ----------------------------------------------------------------------------

    /// <summary>
    /// The signed-in operator, by address where there is one — the same identity the visual
    /// approval and the alarm reviews record, so one person's decisions look like one person's
    /// across every screen.
    /// </summary>
    private string OperatorName() =>
        userContext.GetEmail() is { Length: > 0 } email ? email : userContext.GetUserId().ToString();

    /// <summary>
    /// One stored PNG, or a 404. Never a storage URL, and never an exception on the way out: a
    /// picture that is not there is a 404 an operator can read, not a 500.
    /// </summary>
    private async Task<IActionResult> ImageAsync(string blobName, CancellationToken cancellationToken)
    {
        try
        {
            if (!await blobStorage.ExistsAsync(blobName, cancellationToken))
            {
                return NotFound();
            }

            return Png(await blobStorage.DownloadBytesFromStoredUrlAsync(blobName, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Admin image {Blob} could not be read.", blobName);
            return NotFound();
        }
    }

    private async Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken)
    {
        try
        {
            return await blobStorage.ExistsAsync(blobName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Admin image probe for {Blob} failed.", blobName);
            return false;
        }
    }

    private FileContentResult Png(byte[] bytes)
    {
        // Private, because these are photographs of somebody's child laid into a story. Five
        // minutes is long enough for a panel that re-renders on a poll and short enough that a
        // redraw is visible on the next page load.
        Response.Headers.CacheControl = "private, max-age=300";
        return File(bytes, "image/png");
    }

    /// <summary>
    /// One alarm as the console shows it.
    ///
    /// The same projection <c>AdminReleaseController</c> makes, copied rather than shared: the two
    /// controllers own different routes and neither should have to take a dependency on the other
    /// to answer with the same thirteen fields.
    /// </summary>
    private static AdminAlarmRow ToRow(BekiAlarm alarm) => new(
        alarm.Id,
        alarm.PackId,
        alarm.OrderId,
        alarm.UserId,
        alarm.CheckId,
        alarm.Severity,
        alarm.Detail,
        alarm.EvidenceBlob,
        alarm.CreatedAtUtc,
        alarm.LastSeenUtc,
        alarm.ReviewedBy,
        alarm.ReviewedAtUtc,
        alarm.Resolution);
}
