using System.Text.Json;

using Hangfire;

using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.DTOs.Orders;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class BookFulfillmentService(
    IOrderRepository orderRepository,
    IAdventurePackRepository packRepository,
    ICharacterRepository characterRepository,
    IWorldProgressService worldProgressService,
    IPrintOrderService printOrderService,
    IBlobStorageService blobStorageService,
    IMasterStoryRunRepository masterStoryRunRepository,
    IBackgroundJobClient backgroundJobClient,
    IOptions<BekiOptions> bekiOptions,
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
            StoryPageCount = AdventureStoryConstants.MaxPageCount,
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

        // The parent already read a story and chose to buy it. Keep that one rather than
        // writing a new one, and reuse its cover as page one so only the pages they have
        // not seen cost a generation.
        await AdoptPreviewAsync(book, draft, cancellationToken);

        var cast = BuildCast(hero.Id, draft.SupportingCharacterIds);
        await characterRepository.SetBookCastAsync(book.Id, cast, cancellationToken);

        await worldProgressService.MarkStartedAsync(order.UserId, hero.Id, book.WorldId!, cancellationToken);

        // Payment is what earns the map pin, not a successful render: generation is retried
        // until it succeeds, and a child should not lose a world to a transient API failure.
        await worldProgressService.MarkCompletedAsync(
            order.UserId, hero.Id, book.WorldId!, book.Id, cancellationToken);

        EnqueueGeneration(book.Id, await BekiRunForAsync(draft, cancellationToken));

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
            // Same decision the first attempt made. A retry that fell back to the legacy
            // pipeline would give the parent a different kind of book than the one that failed.
            Guid? bekiRunId = null;
            try
            {
                bekiRunId = await BekiRunForAsync(DeserializeDraft(order), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Retry for book {BookId} could not read its draft; using the legacy pipeline.", bookId);
            }

            EnqueueGeneration(bookId, bekiRunId);
        }
    }

    /// <summary>
    /// Which pipeline draws this book. Beki when the switch is on and the preview run still
    /// holds what that format needs — a printing-format plan and the portrait; the legacy per-page flow
    /// otherwise. Deciding here rather than failing later means a run that expired between
    /// preview and purchase costs the parent nothing but the old format.
    ///
    /// The prompt-version check matters on its own, separately from the switch: a preview written
    /// before <see cref="BekiOptions.BookFormatEnabled"/> was ever turned on carries a v1–v4 plan
    /// with no cast list and no Beki placement, which the Beki illustrator and PDF composer are
    /// not built to read. Routing it through the Beki pipeline anyway would not fail loudly — it
    /// would draw a book that quietly ignores half of what the plan says. That book stays on the
    /// legacy path it was always going to take.
    /// </summary>
    private async Task<Guid?> BekiRunForAsync(BookDraftRequest draft, CancellationToken cancellationToken)
    {
        if (!bekiOptions.Value.BookFormatEnabled || draft.PreviewBookId is not { } runId)
        {
            return null;
        }

        var run = await masterStoryRunRepository.GetByIdAsync(runId, cancellationToken);

        // The gate is the printing book format, not a version equality: what the Beki pipeline
        // needs is a plan written with a cast list and per-spread placement, whichever version of
        // the printing flow wrote it.
        return run is not null
               && !string.IsNullOrWhiteSpace(run.StoryJson)
               && !string.IsNullOrWhiteSpace(run.PhotoBlobUrl)
               && BookFormat.IsPrintPlan(run.PromptVersion)
            ? runId
            : null;
    }

    private void EnqueueGeneration(Guid bookId, Guid? bekiRunId)
    {
        if (bekiRunId is { } runId)
        {
            backgroundJobClient.Enqueue<IBekiPackFulfillment>(service =>
                service.ProcessAsync(bookId, runId, CancellationToken.None));
            return;
        }

        backgroundJobClient.Enqueue<IAdventureGenerationService>(service =>
            service.ProcessStoryGenerationAsync(bookId, CancellationToken.None));
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

    /// <summary>
    /// Carries the previewed story, and its cover, onto the book that was just bought.
    ///
    /// Until this existed the paid book was written from scratch: a parent read one story,
    /// paid for it, and received a different one. The teaser was only ever shown, never
    /// kept. Saving it here means the book they bought is the book they read.
    ///
    /// The cover is stored as page one's illustration, and the illustrator skips any page
    /// that already has one — so the picture the parent chose is the picture they get, and
    /// only the unseen pages cost a generation.
    ///
    /// Best effort on purpose: a book that cannot adopt its preview is still a book, and
    /// falling back to generating a fresh story is far better than failing a paid order.
    /// </summary>
    private async Task AdoptPreviewAsync(
        AdventurePack book,
        BookDraftRequest draft,
        CancellationToken cancellationToken)
    {
        // Prefer the run we wrote ourselves over the copy the client sends back.
        //
        // The client's copy is a round trip through a browser, so it is a claim rather than a
        // fact: it can be edited, and its cover is a URL we would have to trust. The row holds
        // the same story and the storage path we put the cover at, which is why the story was
        // stored in the first place. The client's copy stays as the fallback for a run that has
        // already expired.
        var storedRun = draft.PreviewBookId is { } runId
            ? await masterStoryRunRepository.GetByIdAsync(runId, cancellationToken)
            : null;

        var storyJson = storedRun?.ContentJson ?? draft.PreviewStoryJson;
        var coverSource = storedRun?.CoverImageUrl ?? draft.PreviewCoverImage;

        if (string.IsNullOrWhiteSpace(storyJson))
        {
            return;
        }

        try
        {
            var content = JsonSerializer.Deserialize<AdventureContentDto>(storyJson, JsonOptions);
            if (content is null || content.StoryPages.Count == 0)
            {
                logger.LogWarning("Preview story for book {BookId} was empty; writing a fresh one.", book.Id);
                return;
            }

            var coverUrl = await StorePreviewCoverAsync(book, coverSource, cancellationToken);
            if (coverUrl is not null)
            {
                // A spread book's cover is not its first page. Page one has its own illustration,
                // its own prompt and its own moment in the story; putting the cover there would
                // both hide that page's picture and stop it ever being drawn, since the
                // illustration pass skips pages that already have a URL.
                //
                // Legacy books, whose cover genuinely was page one's picture, keep that behaviour.
                var isSpreadBook = content.StoryPages.Any(page => page.IsTextOnlyPage);
                if (isSpreadBook)
                {
                    // CoverImageUrl, not PreviewIllustrationStatus.
                    //
                    // Marking the illustration status Ready to park the cover stopped the book
                    // ever being illustrated at all: the illustration job claims a book only when
                    // that status is None, Failed or a stale Generating, so Ready read as "already
                    // done" and every paid spread book sat there with no pictures.
                    await packRepository.UpdateBookPresentationAsync(
                        book.Id,
                        title: null,
                        coverImageUrl: coverUrl,
                        cancellationToken);
                }
                else
                {
                    content.StoryPages[0].IllustrationUrl = coverUrl;
                }
            }

            await packRepository.UpdateStatusAsync(
                book.Id,
                AdventurePackStatus.StoryReady,
                JsonSerializer.Serialize(content, JsonOptions),
                null,
                null,
                cancellationToken);

            if (storedRun is not null)
            {
                await masterStoryRunRepository.ClaimAsync(storedRun.Id, book.UserId, book.Id, cancellationToken);
            }

            logger.LogInformation(
                "Book {BookId} adopted its preview story ({PageCount} pages){CoverNote}.",
                book.Id,
                content.StoryPages.Count,
                coverUrl is null ? " without a cover" : " and its cover");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not adopt the preview for book {BookId}; writing a fresh story.", book.Id);
        }
    }

    /// <summary>Uploads the preview cover data URL, returning null when there is nothing usable.</summary>
    private async Task<string?> StorePreviewCoverAsync(
        AdventurePack book,
        string? dataUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            return null;
        }

        // Books written by the master call keep their cover in blob storage and send its URL
        // rather than inlining megabytes of base64 into the order. Copy it under this book's own
        // name by way of our own storage: a URL we cannot read is a URL that is not ours, so this
        // both validates it and stops a paid order from pointing its cover at somewhere else.
        if (!dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var storedBytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(dataUrl, cancellationToken);
                if (storedBytes is not { Length: > 0 })
                {
                    return null;
                }

                return await blobStorageService.UploadAsync(
                    PreviewCoverBlobName(book),
                    storedBytes,
                    "image/webp",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not copy the preview cover for book {BookId}.", book.Id);
                return null;
            }
        }

        var comma = dataUrl.IndexOf(',');
        if (comma < 0)
        {
            return null;
        }

        var header = dataUrl[..comma];
        var contentType = header.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? header[5..].Split(';')[0]
            : "image/webp";

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }

        // A cover this size is not one of ours. Dropping it costs one redrawn page;
        // accepting anything the client sends would let a paid order carry arbitrary bytes
        // into blob storage.
        const int maxCoverBytes = 6 * 1024 * 1024;
        if (bytes.Length == 0 || bytes.Length > maxCoverBytes)
        {
            return null;
        }

        return await blobStorageService.UploadAsync(
            $"{book.UserId}/{book.Id}/page-0{ExtensionFor(contentType)}",
            bytes,
            contentType,
            cancellationToken);
    }

    /// <summary>
    /// The cover has its own name. It used to share "page-0" with the first page, which was
    /// harmless while the cover *was* the first page's picture and destroys one of them now that
    /// a book has eight illustrated pages and a cover of its own.
    /// </summary>
    private static string PreviewCoverBlobName(AdventurePack book) => $"{book.UserId}/{book.Id}/cover.webp";

    private static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        _ => ".webp",
    };

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
