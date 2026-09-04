using System.Text.Json;

using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.DTOs.Print;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Print fulfilment: turning a paid print order into a parcel, letting the parent watch
/// it move, and giving operations the queue it works through.
///
/// The address is copied onto the parcel rather than referenced. A parent who edits
/// their saved address next month must not silently rewrite the label on a parcel that
/// already went out, so <see cref="PrintOrder"/> owns its own copy from the moment it
/// is created.
/// </summary>
public sealed class PrintOrderService(
    IPrintOrderRepository printOrderRepository,
    IUserAddressRepository addressRepository,
    IAdventurePackRepository packRepository,
    IUserRepository userRepository,
    IEmailService emailService,
    IAdminNotifier adminNotifier,
    IBekiAlarmService alarms,
    ILogger<PrintOrderService> logger) : IPrintOrderService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const int MaxAdminPageSize = 200;

    public async Task<PrintOrder?> CreateForPaidOrderAsync(Order order, CancellationToken cancellationToken)
    {
        var address = DeserializeShipping(order);
        if (address is null)
        {
            // Digital-only, or a print order placed before the address was captured. Either
            // way there is nothing to ship yet; the parent is prompted for an address and
            // the parcel is created then.
            return null;
        }

        if (order.BookId is not { } bookId)
        {
            logger.LogWarning(
                "Print order {OrderId} is paid but has no book yet; skipping parcel creation.", order.Id);
            return null;
        }

        var printOrder = await printOrderRepository.CreateIfAbsentAsync(
            new PrintOrder
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                BookId = bookId,
                UserId = order.UserId,
                RecipientName = address.RecipientName.Trim(),
                RecipientPhone = address.RecipientPhone.Trim(),
                City = address.City.Trim(),
                Region = Clean(address.Region),
                AddressLine1 = address.AddressLine1.Trim(),
                AddressLine2 = Clean(address.AddressLine2),
                PostalCode = Clean(address.PostalCode),
                Notes = Clean(address.Notes),
                Status = PrintOrderStatus.AwaitingPrint
            },
            cancellationToken);

        // Only announce a parcel we just created. CreateIfAbsentAsync returning an older
        // row means a webhook replay, and the parent has already had this email.
        var isNew = printOrder.CreatedAt >= DateTime.UtcNow.AddMinutes(-2);
        if (!isNew)
        {
            return printOrder;
        }

        if (address.SaveForLater)
        {
            // Saved here rather than at checkout so an abandoned payment never leaves a
            // home address behind, while a returning parent still gets "use saved address".
            await SaveAddressAsync(order.UserId, ToSaveRequest(address), cancellationToken);
        }

        await NotifyPlacedAsync(order.UserId, printOrder, cancellationToken);
        return printOrder;
    }

    public async Task<IReadOnlyList<PrintOrderResponse>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var printOrders = await printOrderRepository.GetByUserIdAsync(userId, cancellationToken);
        if (printOrders.Count == 0)
        {
            return [];
        }

        var titles = await ResolveTitlesAsync(printOrders, userId, cancellationToken);
        return printOrders.Select(printOrder => ToResponse(printOrder, Lookup(titles, printOrder.BookId))).ToList();
    }

    public async Task<PrintOrderResponse?> GetForUserAsync(
        Guid userId,
        Guid printOrderId,
        CancellationToken cancellationToken)
    {
        var printOrder = await printOrderRepository.GetByIdForUserAsync(printOrderId, userId, cancellationToken);
        if (printOrder is null)
        {
            return null;
        }

        var book = await packRepository.GetByIdAsync(printOrder.BookId, userId, cancellationToken);
        return ToResponse(printOrder, book?.Title);
    }

    public async Task<PrintOrderResponse> UpdateAddressAsync(
        Guid userId,
        Guid printOrderId,
        ShippingAddressRequest request,
        CancellationToken cancellationToken)
    {
        var printOrder = await printOrderRepository.GetByIdForUserAsync(printOrderId, userId, cancellationToken)
                         ?? throw new KeyNotFoundException("ბეჭდური შეკვეთა ვერ მოიძებნა.");

        printOrder.RecipientName = request.RecipientName.Trim();
        printOrder.RecipientPhone = request.RecipientPhone.Trim();
        printOrder.City = request.City.Trim();
        printOrder.Region = Clean(request.Region);
        printOrder.AddressLine1 = request.AddressLine1.Trim();
        printOrder.AddressLine2 = Clean(request.AddressLine2);
        printOrder.PostalCode = Clean(request.PostalCode);
        printOrder.Notes = Clean(request.Notes);

        var updated = await printOrderRepository.UpdateAddressAsync(printOrder, cancellationToken);
        if (!updated)
        {
            throw new InvalidOperationException("მისამართის შეცვლა შეუძლებელია — შეკვეთა უკვე გაიგზავნა.");
        }

        if (request.SaveForLater)
        {
            await SaveAddressAsync(userId, ToSaveRequest(request), cancellationToken);
        }

        var book = await packRepository.GetByIdAsync(printOrder.BookId, userId, cancellationToken);
        return ToResponse(printOrder, book?.Title);
    }

    // -- saved addresses ----------------------------------------------------

    public async Task<IReadOnlyList<AddressResponse>> ListAddressesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var addresses = await addressRepository.GetByUserIdAsync(userId, cancellationToken);
        return addresses.Select(ToResponse).ToList();
    }

    public async Task<AddressResponse> SaveAddressAsync(
        Guid userId,
        SaveAddressRequest request,
        CancellationToken cancellationToken)
    {
        var existing = request.Id is { } id
            ? (await addressRepository.GetByUserIdAsync(userId, cancellationToken))
                .FirstOrDefault(address => address.Id == id)
            : null;

        if (request.Id is not null && existing is null)
        {
            throw new KeyNotFoundException("მისამართი ვერ მოიძებნა.");
        }

        var saved = await addressRepository.UpsertAsync(
            new UserAddress
            {
                Id = existing?.Id ?? Guid.NewGuid(),
                UserId = userId,
                RecipientName = request.RecipientName.Trim(),
                RecipientPhone = request.RecipientPhone.Trim(),
                City = request.City.Trim(),
                Region = Clean(request.Region),
                AddressLine1 = request.AddressLine1.Trim(),
                AddressLine2 = Clean(request.AddressLine2),
                PostalCode = Clean(request.PostalCode),
                IsDefault = request.IsDefault,
                CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow
            },
            cancellationToken);

        return ToResponse(saved);
    }

    public Task<bool> DeleteAddressAsync(Guid userId, Guid addressId, CancellationToken cancellationToken) =>
        addressRepository.DeleteAsync(addressId, userId, cancellationToken);

    // -- operations console -------------------------------------------------

    /// <summary>
    /// The operations queue.
    ///
    /// Two things about this method are deliberate and were both faults.
    ///
    /// It is ONE query. It used to load each parcel and then ask for its book, its buyer and its
    /// order one at a time — a fifty-row page was two hundred round trips, and the screen got
    /// slower every week the business grew.
    ///
    /// And it WRITES NOTHING. Painting the queue used to raise an alarm for every book with no
    /// press file, so an operator refreshing the screen was minting audit rows about books they
    /// were only looking at; the deduplication kept the count down and the last-seen stamps still
    /// moved, which made "when did this start" unanswerable. The alarm belongs where the
    /// substitution is acted on — the status move below, and the fulfilment job that failed to
    /// prepare the file — not where it is displayed.
    /// </summary>
    public async Task<AdminPrintQueueResponse> GetAdminQueueAsync(
        string? status,
        int limit,
        CancellationToken cancellationToken)
    {
        var filter = ParseStatusFilter(status);
        var rows = await printOrderRepository.GetAdminQueueAsync(
            filter, Math.Clamp(limit, 1, MaxAdminPageSize), cancellationToken);

        var counts = await printOrderRepository.GetAdminCountsAsync(cancellationToken);

        return new AdminPrintQueueResponse
        {
            Counts = counts.ToDictionary(entry => entry.Key.ToString(), entry => entry.Value),
            Orders = rows.Select(ToAdminResponse).ToList(),
        };
    }

    /// <summary>
    /// Cancels the parcel behind a cancelled order, unless it has already gone.
    ///
    /// Called when an operator cancels the order itself. Without it, a cancelled order leaves its
    /// parcel sitting in the print queue and a book gets printed and posted to somebody who is not
    /// being charged for it. Once the parcel has shipped the row is a record of where it went
    /// rather than an instruction, and rewriting it would be a lie about a delivery that happened.
    /// </summary>
    public async Task<bool> TryCancelForOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var printOrder = await printOrderRepository.GetByOrderIdAsync(orderId, cancellationToken);

        if (printOrder is null
            || printOrder.Status is PrintOrderStatus.Shipped
                or PrintOrderStatus.Delivered
                or PrintOrderStatus.Cancelled)
        {
            return false;
        }

        await printOrderRepository.UpdateStatusAsync(
            printOrder.Id, PrintOrderStatus.Cancelled, null, cancellationToken);

        // No email. The parent is being told about the ORDER by whoever cancelled it, and a second
        // letter about the parcel would read as two things having gone wrong.
        logger.LogInformation(
            "Print order {PrintOrderId} was cancelled because order {OrderId} was.",
            printOrder.Id, orderId);

        return true;
    }

    public async Task<AdminPrintOrderResponse?> UpdateStatusAsync(
        Guid printOrderId,
        UpdatePrintOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        var status = ParseStatus(request.Status);

        var printOrder = await printOrderRepository.GetByIdAsync(printOrderId, cancellationToken);
        if (printOrder is null)
        {
            return null;
        }

        var trackingCode = Clean(request.TrackingCode);

        if (status is PrintOrderStatus.Printing or PrintOrderStatus.Shipped or PrintOrderStatus.Delivered)
        {
            var pack = await packRepository.GetByIdNoOwnershipAsync(printOrder.BookId, cancellationToken);
            if (pack is null || string.IsNullOrWhiteSpace(pack.PrintPdfUrl))
                throw new InvalidOperationException(
                    "ბეჭდვა შეჩერებულია: ბეჭდვისთვის დამტკიცებული PDF არ არის მზად. შეამოწმეთ ადმინისტრატორის შეცდომები.");
        }

        // A "shipped" email with no way to follow the parcel is worse than no email, so
        // the code is required at that transition rather than optional everywhere.
        if (status == PrintOrderStatus.Shipped &&
            trackingCode is null &&
            string.IsNullOrWhiteSpace(printOrder.TrackingCode))
        {
            throw new InvalidOperationException("გაგზავნისას თვალის მიდევნების კოდი აუცილებელია.");
        }

        var statusChanged = printOrder.Status != status;
        await printOrderRepository.UpdateStatusAsync(printOrderId, status, trackingCode, cancellationToken);

        var refreshed = await printOrderRepository.GetByIdAsync(printOrderId, cancellationToken) ?? printOrder;

        if (statusChanged && request.NotifyCustomer)
        {
            await NotifyStatusAsync(refreshed, cancellationToken);
        }

        var row = await printOrderRepository.GetAdminQueueRowAsync(printOrderId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        await RaiseMissingPressFileAsync(row, status, cancellationToken);

        return ToAdminResponse(row);
    }

    /// <summary>
    /// The queue is about to send somebody the wrong file, and this is where it says so.
    ///
    /// A Beki book is rendered twice on purpose and its press interior is a deliverable the release
    /// gates can withhold. When that file is absent from a Beki book, this is not the old "made
    /// before the split" case — it is a press file that was withheld or never written, and the
    /// reading copy is being offered in its place: a page count that does not divide by four
    /// arriving at a binder.
    ///
    /// Raised when the parcel is MOVED, not when the queue is painted. It used to fire on every
    /// row of every listing, so an operator refreshing the screen minted audit rows about books
    /// they were only looking at, and the last-seen stamps moved with each refresh until "when did
    /// this start" had no answer. A status change is somebody acting on the parcel, which is the
    /// moment the substitution actually matters.
    ///
    /// Deduplicated on the book, so correcting a tracking code twice is one incident.
    /// </summary>
    private async Task RaiseMissingPressFileAsync(
        AdminPrintQueueRow row, PrintOrderStatus status, CancellationToken cancellationToken)
    {
        if (!row.PdfIsReadingCopyFallback
            || status is PrintOrderStatus.Cancelled
            || !GenerationPipelines.Beki.Equals(row.BookPipeline, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await alarms.RaiseAsync(
            new BekiAlarmRaise(
                row.BookId,
                row.OrderId,
                row.UserId,
                "press_file_missing",
                BekiReleaseSeverity.Blocker,
                "A print order is being moved through the queue and this book has no press "
                + "interior, so the reading copy is the only printable file. Its page count does "
                + "not divide by four and its spreads are laid out for a screen.",
                null,
                BekiAlarmEvidence.ForAttempt("press_file_missing", row.BookId)),
            cancellationToken);
    }

    // -- notifications ------------------------------------------------------

    private async Task NotifyPlacedAsync(
        Guid userId,
        PrintOrder printOrder,
        CancellationToken cancellationToken)
    {
        var book = await packRepository.GetByIdAsync(printOrder.BookId, userId, cancellationToken);

        // Ahead of the customer mail, and not behind the same guard: a phone-only parent has no
        // address to write to, but their parcel still has to be printed and someone still has to
        // be told about it.
        await adminNotifier.PrintOrderPlacedAsync(printOrder, book?.Title, cancellationToken);

        var email = await ResolveEmailAsync(userId, cancellationToken);
        if (email is null)
        {
            return;
        }

        await TrySendAsync(
            () => emailService.SendPrintOrderPlacedAsync(
                email,
                BookTitleOrFallback(book?.Title),
                printOrder.City,
                GeorgianDelivery.DescribeFor(printOrder.City),
                cancellationToken),
            printOrder.Id);
    }

    private async Task NotifyStatusAsync(PrintOrder printOrder, CancellationToken cancellationToken)
    {
        var email = await ResolveEmailAsync(printOrder.UserId, cancellationToken);
        if (email is null)
        {
            return;
        }

        var book = await packRepository.GetByIdAsync(printOrder.BookId, printOrder.UserId, cancellationToken);

        await TrySendAsync(
            () => emailService.SendPrintOrderStatusAsync(
                email,
                BookTitleOrFallback(book?.Title),
                printOrder.Status,
                printOrder.TrackingCode,
                GeorgianDelivery.DescribeFor(printOrder.City),
                cancellationToken),
            printOrder.Id);
    }

    /// <summary>
    /// A failed email must never fail the operation behind it: the parcel has moved, and
    /// blocking the console on SMTP would leave operations unable to record reality.
    /// </summary>
    private async Task TrySendAsync(Func<Task> send, Guid printOrderId)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Notifying the customer about print order {PrintOrderId} failed.", printOrderId);
        }
    }

    private async Task<string?> ResolveEmailAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(user?.Email))
        {
            return user.Email;
        }

        // Phone-only accounts are expected: they signed up with an OTP and never gave an
        // address. Operations still has their phone number on the parcel.
        logger.LogInformation("User {UserId} has no email address; skipping print notification.", userId);
        return null;
    }

    // -- mapping ------------------------------------------------------------

    /// <summary>
    /// One joined queue row as the console shows it. No I/O: everything on the row came out of the
    /// one statement that produced it.
    /// </summary>
    private static AdminPrintOrderResponse ToAdminResponse(AdminPrintQueueRow row) => new()
    {
        Id = row.Id,
        OrderId = row.OrderId,
        BookId = row.BookId,
        BookTitle = row.BookTitle,
        HeroName = row.HeroName,
        BookStatus = row.BookStatus,
        CustomerEmail = row.CustomerEmail,
        CustomerPhone = row.CustomerPhone,
        Status = Enum.TryParse<PrintOrderStatus>(row.Status, out var status)
            ? status
            : PrintOrderStatus.AwaitingPrint,
        StatusLabel = Enum.TryParse<PrintOrderStatus>(row.Status, out var labelled)
            ? PrintOrderStatusText.Label(labelled)
            : row.Status,
        RecipientName = row.RecipientName,
        RecipientPhone = row.RecipientPhone,
        City = row.City,
        Region = row.Region,
        AddressLine1 = row.AddressLine1,
        AddressLine2 = row.AddressLine2,
        PostalCode = row.PostalCode,
        Notes = row.Notes,
        TrackingCode = row.TrackingCode,
        HasPrintPdf = row.HasPrintPdf,
        PdfIsReadingCopyFallback = row.PdfIsReadingCopyFallback,
        TotalMinor = row.TotalMinor,
        TotalFormatted = GelPricing.Format(row.TotalMinor),
        CreatedAt = Utc(row.CreatedAt),
        ShippedAt = Utc(row.ShippedAt),
        DeliveredAt = Utc(row.DeliveredAt),
    };

    /// <summary>
    /// A stored UTC timestamp, said with its zone.
    ///
    /// SQL hands back Kind=Unspecified and the serializer writes it without an offset, so the
    /// console rendered every parcel four hours early in Tbilisi. Stamped here, where the value
    /// leaves the database, exactly as the alarms repository does it.
    /// </summary>
    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? value) => value is { } moment ? Utc(moment) : null;

    private static PrintOrderResponse ToResponse(PrintOrder printOrder, string? bookTitle) => new()
    {
        Id = printOrder.Id,
        OrderId = printOrder.OrderId,
        BookId = printOrder.BookId,
        BookTitle = bookTitle,
        Status = printOrder.Status,
        StatusLabel = PrintOrderStatusText.Label(printOrder.Status),
        RecipientName = printOrder.RecipientName,
        RecipientPhone = printOrder.RecipientPhone,
        City = printOrder.City,
        Region = printOrder.Region,
        AddressLine1 = printOrder.AddressLine1,
        AddressLine2 = printOrder.AddressLine2,
        PostalCode = printOrder.PostalCode,
        Notes = printOrder.Notes,
        TrackingCode = printOrder.TrackingCode,
        DeliveryEstimate = GeorgianDelivery.DescribeFor(printOrder.City),
        CanEditAddress = printOrder.Status is PrintOrderStatus.AwaitingPrint or PrintOrderStatus.Printing,
        CreatedAt = printOrder.CreatedAt,
        ShippedAt = printOrder.ShippedAt,
        DeliveredAt = printOrder.DeliveredAt
    };

    private static AddressResponse ToResponse(UserAddress address) => new()
    {
        Id = address.Id,
        RecipientName = address.RecipientName,
        RecipientPhone = address.RecipientPhone,
        City = address.City,
        Region = address.Region,
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        PostalCode = address.PostalCode,
        IsDefault = address.IsDefault,
        DeliveryEstimate = GeorgianDelivery.DescribeFor(address.City)
    };

    private static SaveAddressRequest ToSaveRequest(ShippingAddressRequest request) => new()
    {
        RecipientName = request.RecipientName,
        RecipientPhone = request.RecipientPhone,
        City = request.City,
        Region = request.Region,
        AddressLine1 = request.AddressLine1,
        AddressLine2 = request.AddressLine2,
        PostalCode = request.PostalCode,
        IsDefault = true
    };

    // -- helpers ------------------------------------------------------------

    private async Task<Dictionary<Guid, string?>> ResolveTitlesAsync(
        IReadOnlyList<PrintOrder> printOrders,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var titles = new Dictionary<Guid, string?>();
        foreach (var bookId in printOrders.Select(printOrder => printOrder.BookId).Distinct())
        {
            var book = await packRepository.GetByIdAsync(bookId, userId, cancellationToken);
            titles[bookId] = book?.Title;
        }

        return titles;
    }

    private static ShippingAddressRequest? DeserializeShipping(Order order)
    {
        if (string.IsNullOrWhiteSpace(order.ShippingJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ShippingAddressRequest>(order.ShippingJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PrintOrderStatus? ParseStatusFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : ParseStatus(value);

    private static PrintOrderStatus ParseStatus(string? value) =>
        Enum.TryParse<PrintOrderStatus>((value ?? string.Empty).Trim(), ignoreCase: true, out var status)
            ? status
            : throw new InvalidOperationException("სტატუსი არასწორია.");

    private static string BookTitleOrFallback(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "პერსონალური წიგნი" : title;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Lookup(IReadOnlyDictionary<Guid, string?> map, Guid key) =>
        map.TryGetValue(key, out var value) ? value : null;

}
