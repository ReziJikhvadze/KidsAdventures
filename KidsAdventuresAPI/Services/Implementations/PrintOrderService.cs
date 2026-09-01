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
    IOrderRepository orderRepository,
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

    public async Task<AdminPrintQueueResponse> GetAdminQueueAsync(
        string? status,
        int limit,
        CancellationToken cancellationToken)
    {
        var filter = ParseStatusFilter(status);
        var printOrders = await printOrderRepository.GetForAdminAsync(
            filter, Math.Clamp(limit, 1, MaxAdminPageSize), cancellationToken);

        var counts = await printOrderRepository.GetAdminCountsAsync(cancellationToken);

        var response = new AdminPrintQueueResponse
        {
            Counts = counts.ToDictionary(entry => entry.Key.ToString(), entry => entry.Value)
        };

        foreach (var printOrder in printOrders)
        {
            response.Orders.Add(await ToAdminResponseAsync(printOrder, cancellationToken));
        }

        return response;
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

        return await ToAdminResponseAsync(refreshed, cancellationToken);
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

    private async Task<AdminPrintOrderResponse> ToAdminResponseAsync(
        PrintOrder printOrder,
        CancellationToken cancellationToken)
    {
        var book = await packRepository.GetByIdAsync(printOrder.BookId, printOrder.UserId, cancellationToken);
        var user = await userRepository.GetByIdAsync(printOrder.UserId, cancellationToken);
        var order = await orderRepository.GetByIdAsync(printOrder.OrderId, cancellationToken);

        /*
          The queue is about to hand somebody the wrong file, and now it says so.

          A Beki book is rendered twice on purpose and its press interior is a deliverable the
          release gates can withhold. When that file is absent from a Beki book, this is not the
          old "made before the split" case — it is a press file that was withheld or never written,
          and the queue was quietly substituting the reading copy for it. That is a page count that
          does not divide by four arriving at a binder, and until now nothing recorded that it had
          happened. It is an alarm, at blocker severity, because the money at stake is a printer's.

          Deduplicated on the book, so an operator refreshing the queue does not mint a row per
          refresh — the alarm's own key sees the same incident and moves its last-seen stamp.
        */
        var readingCopyFallback =
            string.IsNullOrWhiteSpace(book?.PrintPdfUrl) && !string.IsNullOrWhiteSpace(book?.PdfUrl);

        if (readingCopyFallback && book is not null && book.IsBekiPipeline)
        {
            await alarms.RaiseAsync(
                new BekiAlarmRaise(
                    book.Id,
                    printOrder.OrderId,
                    printOrder.UserId,
                    "press_file_missing",
                    BekiReleaseSeverity.Blocker,
                    "A print order is in the queue and this book has no press interior, so the "
                    + "reading copy is being offered as the printable file. Its page count does "
                    + "not divide by four and its spreads are laid out for a screen.",
                    null,
                    BekiAlarmEvidence.ForAttempt("press_file_missing", book.Id)),
                cancellationToken);
        }

        return new AdminPrintOrderResponse
        {
            Id = printOrder.Id,
            OrderId = printOrder.OrderId,
            BookId = printOrder.BookId,
            BookTitle = book?.Title,
            CustomerEmail = user?.Email,
            CustomerPhone = user?.PhoneNumber,
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
            /*
              The printable file, not the reading copy.

              Books are rendered twice and stored side by side — the reading copy for the parent
              and a print copy with the blank leaves saddle-stitch needs. This is the only place
              that wants the second one, and sending the first would mean a page count that does
              not divide by four arriving at the binder.

              Books made before the split have no print file and no url recorded for one, so they
              fall back to what exists and the printer pads them as it always did. That fallback
              is the reason this reads a stored url rather than deriving one from the reading
              copy's name: a derived url would point at a blob that was never written.
            */
            PdfUrl = book?.PrintPdfUrl ?? book?.PdfUrl,
            PdfIsReadingCopyFallback = readingCopyFallback,
            TotalMinor = order?.TotalMinor ?? 0,
            TotalFormatted = GelPricing.Format(order?.TotalMinor ?? 0),
            CreatedAt = printOrder.CreatedAt,
            ShippedAt = printOrder.ShippedAt,
            DeliveredAt = printOrder.DeliveredAt
        };
    }

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
