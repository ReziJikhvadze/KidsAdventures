using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Repositories.Implementations;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

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
