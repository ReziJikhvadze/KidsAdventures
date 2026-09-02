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

        /*
          Which pipeline this book gets, decided BEFORE the row is written — amendment B5.

          The decision has always been made here; what is new is that it is written down. It used to
          live only in which Hangfire job got enqueued at the bottom of this method, so every other
          part of the system that needed to know — the readiness rule, the download refusal, the
          legacy auto-illustration trigger — had to re-derive it from the preview run's prompt
          version, and two of the three simply did not and guessed. Deriving it once, from the code
          that is choosing, and stamping it in the same write that creates the pack is the whole of
          the correction.
        */
        var bekiRunId = await BekiRunForAsync(draft, cancellationToken);

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
            CreatedAt = DateTime.UtcNow,
            // In the same INSERT as everything else: the pack has never existed in a state where
            // its pipeline was unknown, and it never will.
            GenerationPipeline = bekiRunId is null
                ? GenerationPipelines.Legacy
                : GenerationPipelines.Beki
        };

        /*
          One write, not two.

          The pack and the order's pointer to it used to be separate statements, and the pointer is
          fulfilment's idempotency marker: everything that asks "does this order have a book yet"
          reads it. Split, a fulfilment that died between them — a request the browser dropped, a
          process that restarted — left a real pack with no order pointing at it, and the next
          retry made a second book for the same payment. The repository commits both together.
        */
        await packRepository.CreatePendingForOrderAsync(book, order.Id, cancellationToken);

        // The preview run is claimed the moment the paid book exists — before adoption, and
        // whether or not adoption then succeeds. Claiming clears the run's expiry; left to the
        // adoption path it was skipped whenever that path failed, and the guest-run purge then
        // deleted the portrait and the plan a retry needed, quietly re-routing a paid Beki book
        // down the legacy pipeline.
        await ClaimPreviewRunAsync(draft, book, cancellationToken);

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

        EnqueueGeneration(book.Id, bekiRunId);

        logger.LogInformation(
            "Order {OrderId} fulfilled: book {BookId} ({WorldId}, chapter {SequenceNumber}) queued "
            + "for the {Pipeline} pipeline.",
            order.Id, book.Id, book.WorldId, book.SequenceNumber, book.GenerationPipeline);

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

        /*
          A Beki pack at StoryReady is a pack with no job, and it is re-driven as one.

          Adoption writes StoryReady before generation is enqueued, and everything between the two
          — the cast, the map, the enqueue itself — can still throw. The re-drive used to look only
          at Pending and Failed, so a paid Beki book stranded at StoryReady was skipped: nothing
          queued, the order stamped Fulfilled anyway, and a parent polling "შეკვეთა მიღებულია…"
          for good. The Beki job claims StoryReady, so queuing it is exactly the first attempt's
          missing last step.

          Legacy StoryReady is not in the set, and must not be: on that pipeline it is the
          finished book.
        */
        var unclaimedBeki = book.IsBekiPipeline && book.Status == AdventurePackStatus.StoryReady;

        if (book.Status is AdventurePackStatus.Pending or AdventurePackStatus.Failed || unclaimedBeki)
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

            /*
              And the row is re-stamped, because a re-drive is allowed to decide differently.

              It normally will not — the same draft and the same run give the same answer — but a
              retry after the Beki flag was turned off, or after the preview run expired, genuinely
              routes the book down the other pipeline, and a column left saying the old answer would
              be a book judged by one pipeline's rules and drawn by the other's. Amendment B5's whole
              value is that the column and the queued job cannot disagree.
            */
            await packRepository.SetGenerationPipelineAsync(
                bookId,
                bekiRunId is null ? GenerationPipelines.Legacy : GenerationPipelines.Beki,
                cancellationToken);

            /*
              A failed book has to be revived before it can be re-queued, and revived deliberately.

              Both generation jobs now refuse to claim a pack that is Failed. That refusal is the
              point: Failed is written by the stale-generation sweep, from outside the process that
              died, and a job that could claim its way out of it would make the verdict meaningless
              — a book declared abandoned would be silently redrawn by the next requeue with nothing
              anywhere recording that it had ever been lost.

              This path is the exception, and it is the only one: a paid order being re-driven, by
              the console's retry or by the stalled-order sweep. It is a decision rather than an
              accident, so it says so in the row before it enqueues anything. Enqueuing without the
              transition would post a job that loads the pack, refuses it, and returns — leaving a
              paid book Failed forever while every retry looks like it did something.

              StoryReady rather than Pending when the story is already there, because the legacy job
              only short-circuits to illustrations on that exact pair. Revived to Pending it would
              write a second story, and the parent would be handed a different book from the one
              they read and bought — which is the fault preview adoption exists to prevent. The Beki
              job accepts either.
            */
            if (book.Status == AdventurePackStatus.Failed)
            {
                var revivedTo = string.IsNullOrWhiteSpace(book.GeneratedJson)
                    ? AdventurePackStatus.Pending
                    : AdventurePackStatus.StoryReady;

                // Compare-and-set, so a re-drive that races the book's own recovery cannot drag it
                // back out of a status it had legitimately reached.
                var revived = await packRepository.TryUpdateStatusAsync(
                    bookId,
                    AdventurePackStatus.Failed,
                    revivedTo,
                    book.GeneratedJson,
                    book.PdfUrl,
                    // The failure reason goes with the failure. It has already been logged and an
                    // admin has already been paged with it; left on a row that is being redrawn it
                    // is only an error message the parent can see on a book that is being made.
                    null,
                    cancellationToken);

                if (!revived)
                {
                    logger.LogInformation(
                        "Book {BookId} was no longer Failed when the retry tried to revive it; "
                        + "something else has already moved it on, so nothing is queued.",
                        bookId);
                    return;
                }

                logger.LogWarning(
                    "Book {BookId} was Failed and has been revived to {Status} for a deliberate "
                    + "paid-order retry.", bookId, revivedTo);

                try
                {
                    EnqueueGeneration(bookId, bekiRunId);
                }
                catch (Exception ex)
                {
                    /*
                      Put it back, or the rescue is worse than the failure.

                      A legacy book left revived to StoryReady with no job behind it would read as
                      finished to everything that looks at it — outside the set a later re-drive
                      considers, and outside the sweep's. Failed is the status that keeps it
                      rescuable on both pipelines, and it is the verdict this row already carried.
                    */
                    await packRepository.TryUpdateStatusAsync(
                        bookId,
                        revivedTo,
                        AdventurePackStatus.Failed,
                        book.GeneratedJson,
                        book.PdfUrl,
                        $"The retry could not queue generation: {ex.Message}",
                        CancellationToken.None);

                    throw;
                }

                return;
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
    /// Marks the preview run as belonging to this paid book, so the guest-run purge leaves it
    /// alone from here on.
    ///
    /// Best effort and logged, never fatal: the claim is what keeps the run's portrait and plan
    /// on disk for a re-drive, and a paid order must not fail because that bookkeeping did. It
    /// runs before adoption on purpose — the row it protects is exactly the one adoption is about
    /// to read, and a retry after a failed adoption needs it just as much as the first attempt.
    /// </summary>
    private async Task ClaimPreviewRunAsync(BookDraftRequest draft, AdventurePack book, CancellationToken cancellationToken)
    {
        if (draft.PreviewBookId is not { } runId)
        {
            return;
        }

        try
        {
            await masterStoryRunRepository.ClaimAsync(runId, book.UserId, book.Id, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex, "Could not claim preview run {RunId} for book {BookId}; its purge is not deferred.",
                runId, book.Id);
        }
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
