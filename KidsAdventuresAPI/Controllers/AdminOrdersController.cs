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
    IBlobStorageService blobStorage,
    IAdventureGenerationService generationService,
    IOrderService orderService,
    BekiPackageExport packageExport,
    BekiReleaseGates releaseGates,
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
        // someone who asked for the short one, and they read it as "nothing is wrong".
        if (!string.IsNullOrWhiteSpace(flag) &&
            !string.Equals(flag, AdminReportingRepository.PaidUnfulfilledFlag, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Unknown filter." });
        }

        return Ok(await reporting.GetOrdersAsync(status, search, flag, page, pageSize, cancellationToken));
    }

    /// <summary>One order with its customer, its book and its parcel.</summary>
    [HttpGet("orders/{id:guid}")]
    public async Task<ActionResult<AdminOrderDetailResponse>> OrderDetail(
        Guid id,
        CancellationToken cancellationToken)
    {
        var detail = await reporting.GetOrderDetailAsync(id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>
    /// The book's PDF, streamed through the API.
    ///
    /// Its own route rather than the customer one, which scopes by owner: an operator looking at
    /// a support ticket is not the owner and never will be. The blob URL is not handed out
    /// either — it is storage internals, and a link that outlives this request is a link that
    /// leaks a child's book.
    /// </summary>
    [HttpGet("orders/{id:guid}/pdf")]
    public async Task<IActionResult> OrderPdf(Guid id, CancellationToken cancellationToken)
    {
        var detail = await reporting.GetOrderDetailAsync(id, cancellationToken);
        if (detail?.Book is null)
        {
            return NotFound(new { message = "ამ შეკვეთას წიგნი არ აქვს." });
        }

        var pack = await packRepository.GetByIdNoOwnershipAsync(detail.Book.Id, cancellationToken);

        // A print order gets the print file. The two are not the same PDF: the print copy carries
        // the blank leaves saddle-stitch needs, and handing the binder the reading copy produces
        // a book with its pages in the wrong places. Whichever is asked for, the other is the
        // fallback — a file an operator can open beats a 409 they cannot act on.
        var isPrint = string.Equals(detail.Order.Package, nameof(OrderPackage.Print), StringComparison.OrdinalIgnoreCase);
        var url = isPrint
            ? pack?.PrintPdfUrl ?? pack?.PdfUrl
            : pack?.PdfUrl ?? pack?.PrintPdfUrl;

        // The fallback stays, but it stops being silent: a reading copy handed to an operator
        // who asked for the print file is how the unprepped hybrid reached a printer's reviewer.
        // The filename now says what the file is, so the substitution travels with the download.
        var printFallback = isPrint && string.IsNullOrWhiteSpace(pack?.PrintPdfUrl);

        // 409 rather than 404: the book exists and the file does not exist *yet*, which is a
        // different thing to tell an operator — one of them has a button next to it.
        if (string.IsNullOrWhiteSpace(url))
        {
            return Conflict(new { message = "PDF ჯერ არ დაგენერირებულა." });
        }

        try
        {
            var bytes = await blobStorage.DownloadBytesFromStoredUrlAsync(url, cancellationToken);
            return File(
                bytes,
                "application/pdf",
                printFallback
                    ? $"beki-{detail.Book.Id}-READING-COPY-not-print.pdf"
                    : $"beki-{detail.Book.Id}.pdf");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin PDF download failed for book {BookId}.", detail.Book.Id);
            return NotFound(new { message = "PDF ფაილი საცავში ვერ მოიძებნა." });
        }
    }

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

        var revised = await releaseGates.EvaluateAsync(pack.UserId, pack.Id, cancellationToken);

        await blobStorage.UploadAsync(
            BekiPackBlobs.ReleaseGatesName(pack.UserId, pack.Id),
            System.Text.Encoding.UTF8.GetBytes(revised.ToJson()),
            "application/json",
            cancellationToken);

        await PublishUnlockedFilesAsync(pack, revised, cancellationToken);

        logger.LogInformation(
            "Beki pack {PackId}: {Approver} signed off contact sheet {Sheet}; the verdict is now "
            + "{Verdict} ({Failing}).",
            pack.Id, approval.ApprovedBy, sheet[..12], revised.Verdict,
            revised.FailingGates.Count == 0 ? "no failing gates" : string.Join(", ", revised.FailingGates));

        return Ok(ToResponse(revised));
    }

    /// <summary>
    /// Writes the URL columns the new verdict unlocks, and only those.
    ///
    /// The compare-and-set idiom the fulfilment job's own terminal write uses, for the same reason:
    /// this runs long after the job finished and must not resurrect a pack the stale-generation
    /// sweep buried, or overwrite a status somebody else wrote. A pack that is not Completed keeps
    /// its columns and the operator is told why.
    /// </summary>
    private async Task PublishUnlockedFilesAsync(
        Domain.Entities.AdventurePack pack,
        BekiReleaseGateReport verdict,
        CancellationToken cancellationToken)
    {
        if (verdict.PressFilesMayPublish
            && await blobStorage.ExistsAsync(
                BekiPackBlobs.InteriorPdfName(pack.UserId, pack.Id), cancellationToken))
        {
            await packRepository.UpdatePrintPdfUrlAsync(
                pack.Id,
                await StoredUrlAsync(BekiPackBlobs.InteriorPdfName(pack.UserId, pack.Id)),
                cancellationToken);
        }

        if (!verdict.CustomerPdfMayPublish
            || !string.IsNullOrWhiteSpace(pack.PdfUrl)
            || pack.Status != AdventurePackStatus.Completed)
        {
            return;
        }

        var published = await packRepository.TryUpdateStatusAsync(
            pack.Id,
            AdventurePackStatus.Completed,
            AdventurePackStatus.Completed,
            pack.GeneratedJson,
            await StoredUrlAsync(BekiPackBlobs.ReadingPdfName(pack.UserId, pack.Id)),
            null,
            cancellationToken);

        if (!published)
        {
            logger.LogWarning(
                "Beki pack {PackId}: the customer PDF was unlocked by this approval but the pack is "
                + "no longer Completed, so nothing was published. Whoever moved it decides next.",
                pack.Id);
        }

        // The blob's own stored URL, which is whatever upload returned for it. Re-uploading the same
        // bytes is the only way this account hands back that string, and it is cheap next to being
        // wrong: a key assembled by hand reads on one storage backend and 404s on the other.
        async Task<string?> StoredUrlAsync(string blobName)
        {
            if (!await blobStorage.ExistsAsync(blobName, cancellationToken))
            {
                return null;
            }

            await using var stream = await blobStorage.DownloadAsync(blobName, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);

            return await blobStorage.UploadAsync(
                blobName, buffer.ToArray(), "application/pdf", cancellationToken);
        }
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

        return queued
            ? Accepted(new { message = "შეკვეთა რიგში ჩადგა." })
            : Conflict(new
            {
                message = "ხელახლა გაშვება მხოლოდ გადახდილ და შეუსრულებელ შეკვეთაზეა შესაძლებელი."
            });
    }
}
