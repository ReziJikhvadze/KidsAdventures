using System.Text.Json;

using Hangfire;

using AdventurePacks.Api.Domain;
using AdventurePacks.Api.DTOs.Orders;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class BookFulfillmentService(
    IOrderRepository orderRepository,
    IAdventurePackRepository packRepository,
    ICharacterRepository characterRepository,
    IWorldProgressService worldProgressService,
    IPrintOrderService printOrderService,
    IBackgroundJobClient backgroundJobClient,
    ILogger<BookFulfillmentService> logger) : IBookFulfillmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> FulfillAsync(Order order, CancellationToken cancellationToken)
    {
        if (!order.IsPaid)
        {
            throw new InvalidOperationException("გადაუხდელი შეკვეთის შესრულება შეუძლებელია.");
        }

        var bookId = order.Type switch
        {
            OrderType.NewBook => await FulfillNewBookAsync(order, cancellationToken),
            OrderType.PrintUpgrade => await FulfillPrintUpgradeAsync(order, cancellationToken),
            _ => throw new InvalidOperationException("შეკვეთის ტიპი არასწორია.")
        };

        if (order.Package == OrderPackage.Print)
        {
            // The parcel is queued once the book exists, so operations never sees a print
            // job with no book behind it. The order carries the BookId only after the
            // branch above has run, hence the re-read.
            var withBook = await orderRepository.GetByIdAsync(order.Id, cancellationToken) ?? order;
            await printOrderService.CreateForPaidOrderAsync(withBook, cancellationToken);
        }

        return bookId;
    }

    private async Task<Guid> FulfillNewBookAsync(Order order, CancellationToken cancellationToken)
    {
        // The order's BookId is the idempotency marker: once a book exists for this order,
        // a replayed webhook or a sweeper retry re-opens that book instead of writing a
        // second one and charging the parent's map twice.
        if (order.BookId is { } existingBookId)
        {
            await EnsureUnlockedAsync(existingBookId, order, cancellationToken);
            return existingBookId;
        }

        var draft = DeserializeDraft(order);
        var hero = await characterRepository.GetByIdAsync(draft.PrimaryCharacterId, order.UserId, cancellationToken)
                   ?? throw new InvalidOperationException("მთავარი გმირი ვერ მოიძებნა.");

        // The series is the hero, so a child's books share one spine no matter which
        // world each of them visits.
        var seriesId = hero.Id;
        var sequenceNumber = await packRepository.GetNextSequenceNumberAsync(seriesId, cancellationToken);

        var book = new AdventurePack
        {
            Id = Guid.NewGuid(),
            UserId = order.UserId,
            ChildId = null,
            Theme = WorldThemes.ThemeFor(draft.WorldId),
            Status = AdventurePackStatus.Pending,
            OptionalStoryNotes = Trimmed(draft.StoryNotes),
            StoryLanguage = NormalizeBookLanguage(draft.BookLanguage),
            StoryPageCount = AdventureStoryConstants.FullPageCount,
            // Paid up front, so the book is fully readable the moment it exists. There is
            // no half-bought state any more: the free sample is the guest teaser.
            AccessLevel = BookAccessLevel.Full,
            HasPrintEntitlement = order.Package == OrderPackage.Print,
            WorldId = draft.WorldId.Trim().ToLowerInvariant(),
            PrimaryCharacterId = hero.Id,
            SeriesId = seriesId,
            SequenceNumber = sequenceNumber,
            ContinuesFromBookId = draft.ContinuesFromBookId,
            ProgressMessage = "შეკვეთა მიღებულია — იწყება წიგნის შექმნა.",
            CreatedAt = DateTime.UtcNow
        };

        await packRepository.CreatePendingAsync(book, cancellationToken);
        await orderRepository.SetBookIdAsync(order.Id, book.Id, cancellationToken);

        var cast = BuildCast(hero.Id, draft.SupportingCharacterIds);
        await characterRepository.SetBookCastAsync(book.Id, cast, cancellationToken);

        await worldProgressService.MarkStartedAsync(order.UserId, hero.Id, book.WorldId!, cancellationToken);

        // Payment is what earns the map pin, not a successful render: generation is retried
        // until it succeeds, and a child should not lose a world to a transient API failure.
        await worldProgressService.MarkCompletedAsync(
            order.UserId, hero.Id, book.WorldId!, book.Id, cancellationToken);

        backgroundJobClient.Enqueue<IAdventureGenerationService>(service =>
            service.ProcessStoryGenerationAsync(book.Id, CancellationToken.None));

        logger.LogInformation(
            "Order {OrderId} fulfilled: book {BookId} ({WorldId}, chapter {SequenceNumber}) queued for generation.",
            order.Id, book.Id, book.WorldId, book.SequenceNumber);

        return book.Id;
    }

    private async Task<Guid> FulfillPrintUpgradeAsync(Order order, CancellationToken cancellationToken)
    {
        var bookId = order.BookId
                     ?? throw new InvalidOperationException("ბეჭდვის შეკვეთას წიგნი არ აქვს მიბმული.");

        var book = await packRepository.GetByIdAsync(bookId, order.UserId, cancellationToken)
                   ?? throw new KeyNotFoundException("წიგნი ვერ მოიძებნა.");

        if (!book.HasPrintEntitlement)
        {
            await packRepository.SetPrintEntitlementAsync(bookId, cancellationToken);
        }

        logger.LogInformation("Order {OrderId} granted the print entitlement for book {BookId}.", order.Id, bookId);
        return bookId;
    }

    /// <summary>
    /// Re-runs the parts of fulfilment that are safe to repeat, for an order whose book
    /// already exists but whose first attempt died partway through.
    /// </summary>
    private async Task EnsureUnlockedAsync(Guid bookId, Order order, CancellationToken cancellationToken)
    {
        var book = await packRepository.GetByIdAsync(bookId, order.UserId, cancellationToken);
        if (book is null)
        {
            return;
        }

        if (!book.IsFullyUnlocked)
        {
            await packRepository.SetAccessLevelAsync(bookId, BookAccessLevel.Full, cancellationToken);
        }

        if (order.Package == OrderPackage.Print && !book.HasPrintEntitlement)
        {
            await packRepository.SetPrintEntitlementAsync(bookId, cancellationToken);
        }

        if (book.PrimaryCharacterId is { } heroId && book.WorldId is { } worldId)
        {
            await worldProgressService.MarkCompletedAsync(
                order.UserId, heroId, worldId, bookId, cancellationToken);
        }

        if (book.Status == AdventurePackStatus.Pending || book.Status == AdventurePackStatus.Failed)
        {
            backgroundJobClient.Enqueue<IAdventureGenerationService>(service =>
                service.ProcessStoryGenerationAsync(bookId, CancellationToken.None));
        }
    }

    private static BookDraftRequest DeserializeDraft(Order order)
    {
        if (string.IsNullOrWhiteSpace(order.DraftJson))
        {
            throw new InvalidOperationException("შეკვეთას წიგნის მონაცემები არ აქვს.");
        }

        return JsonSerializer.Deserialize<BookDraftRequest>(order.DraftJson, JsonOptions)
               ?? throw new InvalidOperationException("შეკვეთის მონაცემები დაზიანებულია.");
    }

    private static List<Guid> BuildCast(Guid heroId, IEnumerable<Guid> supporting)
    {
        // Hero at position 1: the billing order is what the story prompt and the cover
        // read, so it has to be deterministic.
        var cast = new List<Guid> { heroId };
        cast.AddRange(supporting.Where(id => id != heroId).Distinct());
        return cast;
    }

    private static string NormalizeBookLanguage(string? language)
    {
        var normalized = (language ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "ka" or "en" ? normalized : "ka";
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
