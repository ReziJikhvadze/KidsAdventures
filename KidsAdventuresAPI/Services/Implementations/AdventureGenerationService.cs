using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using Hangfire;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class AdventureGenerationService(
    IBackgroundJobClient backgroundJobClient,
    IAdventurePackRepository adventurePackRepository,
    IUserRepository userRepository,
    IBookCastResolver bookCastResolver,
    IOpenAiService openAiService,
    IReferenceImageNormalizer referenceImageNormalizer,
    IAdventurePdfService adventurePdfService,
    IBlobStorageService blobStorageService,
    IEmailService emailService,
    ISeriesMemoryService seriesMemoryService,
    IStoryRuleRepository storyRuleRepository,
    IOptions<EmailOptions> emailOptions,
    IOptions<OpenAiOptions> openAiOptions,
    ILogger<AdventureGenerationService> logger) : IAdventureGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly EmailOptions _emailOptions = emailOptions.Value;
    private readonly OpenAiOptions _openAiOptions = openAiOptions.Value;

    public async Task<GuestPreviewResult> GenerateGuestPreviewAsync(
        GuestPreviewInput input,
        CancellationToken cancellationToken)
    {
        var language = NormalizeLanguage(input.StoryLanguage);

        // Describe the uploaded photo so the cartoon hero resembles the child (same as the signed-in flow).
        string? appearance = null;
        if (input.PhotoBytes is { Length: > 0 })
        {
            try
            {
                var describePrompt = AdventurePromptBuilder.BuildHeroPhotoDescribePrompt(language, input.ChildName, input.Age);
                appearance = await openAiService.DescribeCharacterFromPhotoAsync(
                    input.PhotoBytes,
                    input.PhotoContentType,
                    describePrompt,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Guest preview photo description failed; continuing without it.");
            }
        }

        var generationInput = new AdventureGenerationInput
        {
            ChildName = input.ChildName,
            Age = input.Age,
            Gender = input.Gender,
            Theme = input.Theme,
            ChildAppearanceDescription = appearance,
            FamilyMembers = [],
            OptionalStoryNotes = input.OptionalStoryNotes,
            StoryLanguage = language,
            // Write the WHOLE story now (text is cheap) so we can save it verbatim after sign-in.
            StoryPageCount = AdventureStoryConstants.LegacyPageCount
        };

        var adventureId = Guid.NewGuid();
        var content = await openAiService.GenerateAdventureContentAsync(generationInput, adventureId, cancellationToken);
        NormalizeStoryPages(content, AdventureStoryConstants.LegacyPageCount);

        var firstPage = content.StoryPages.FirstOrDefault()
                        ?? throw new InvalidOperationException("The story preview could not be generated. Please try again.");

        var castPhotos = new List<CastPhotoReference>();
        if (input.PhotoBytes is { Length: > 0 })
        {
            castPhotos.Add(new CastPhotoReference
            {
                Name = input.ChildName,
                Relationship = "hero child",
                IsHero = true,
                AppearanceDescription = appearance,
                Bytes = input.PhotoBytes,
                ContentType = input.PhotoContentType
            });
        }

        // Only ONE image for the free teaser: the cover (the hero in the first scene).
        var coverPrompt = AdventurePromptBuilder.BuildStoryImagePrompt(
            generationInput,
            firstPage,
            pageIndex: 0,
            adventureId,
            hasCharacterAnchor: false,
            castPhotos);

        var imageBytes = await openAiService.GenerateStoryImageAsync(
            coverPrompt,
            new StoryImageReference { CharacterAnchorBytes = null, CastPhotos = castPhotos },
            cancellationToken);

        var stored = referenceImageNormalizer.NormalizeForStorageWebp(imageBytes);
        var coverDataUrl = $"data:{stored.ContentType};base64,{Convert.ToBase64String(stored.Bytes)}";

        // Ensure the saved childName matches the parent's input (so the account copy is consistent).
        content.ChildName = input.ChildName;

        // A teaser stores nothing. The child's name, age, photo and the parent's notes exist only for
        // the length of this request: the photo is described to text and discarded, the story and cover
        // are returned inline, and no row is written. A visitor who never signs up leaves no trace.
        //
        // The id below is handed to the client for the sign-up round trip only. It deliberately matches
        // no record, so WelcomeGiftService resolves it to null and treats the account as a fresh signup —
        // which grants the standard welcome gift. That was already the outcome for anyone who signed up
        // without replaying a teaser, so nothing is lost by not recording it.
        var guestPreviewId = Guid.NewGuid();

        return new GuestPreviewResult
        {
            Title = string.IsNullOrWhiteSpace(content.Title) ? firstPage.Title : content.Title,
            ChildName = input.ChildName,
            FirstPageTitle = firstPage.Title,
            FirstPageText = firstPage.Content,
            CoverImageDataUrl = coverDataUrl,
            Theme = input.Theme,
            GuestPreviewId = guestPreviewId,
            StoryId = adventureId,
            StoryJson = JsonSerializer.Serialize(content, JsonOptions)
        };
    }

    public async Task QueueIllustrationAsync(Guid userId, Guid packId, CancellationToken cancellationToken)
    {
        var pack = await adventurePackRepository.GetByIdAsync(packId, userId, cancellationToken)
                   ?? throw new InvalidOperationException("Pack not found.");

        if (pack.Status != AdventurePackStatus.StoryReady || string.IsNullOrWhiteSpace(pack.GeneratedJson))
        {
            throw new InvalidOperationException("ილუსტრაციებამდე ისტორია ბოლომდე უნდა დაიწეროს.");
        }

        if (HasAllSlideshowIllustrations(pack))
        {
            return;
        }

        // Illustrations are part of a bought book, not a separate purchase. The order is
        // what authorises them, so the only question left here is whether this book was
        // paid for — there is no credit to consume and nothing to refund.
        if (!pack.IsFullyUnlocked)
        {
            throw new InvalidOperationException("ეს წიგნი ჯერ არ არის შეძენილი.");
        }

        if (pack.PreviewIllustrationStatus == PreviewIllustrationStatus.Generating
            && !IsPreviewIllustrationStale(pack))
        {
            return;
        }

        await SetProgressAsync(packId, "ვხატავთ წიგნის გვერდებს…", cancellationToken);

        EnqueuePreviewIllustrationJob(packId);
    }

    public async Task QueuePdfGenerationAsync(Guid userId, Guid packId, CancellationToken cancellationToken)
    {
        var pack = await adventurePackRepository.GetByIdAsync(packId, userId, cancellationToken)
                   ?? throw new InvalidOperationException("Pack not found.");

        if (pack.Status != AdventurePackStatus.StoryReady)
        {
            throw new InvalidOperationException("Story must be ready before creating a PDF.");
        }

        if (string.IsNullOrWhiteSpace(pack.GeneratedJson))
        {
            throw new InvalidOperationException("Story content is missing.");
        }

        if (!CanExportPdf(pack))
        {
            throw new InvalidOperationException(
                pack.IsWelcomeGiftStory
                    ? "უფასო ილუსტრირებული გვერდი ჯერ იქმნება. სცადე ერთ წუთში."
                    : "ილუსტრაციები ჯერ იქმნება. დაელოდე დასრულებას და შემდეგ ჩამოტვირთე PDF.");
        }

        await adventurePackRepository.UpdateStatusAsync(
            packId,
            AdventurePackStatus.GeneratingPdf,
            pack.GeneratedJson,
            null,
            null,
            cancellationToken);

        await SetProgressAsync(packId, "PDF-ს ვამზადებთ…", 5, cancellationToken);

        backgroundJobClient.Enqueue<IAdventureGenerationService>(service =>
            service.ProcessPdfGenerationAsync(packId, CancellationToken.None));
    }

    public async Task ProcessStoryGenerationAsync(Guid packId, CancellationToken cancellationToken)
    {
        var pack = await adventurePackRepository.GetByIdNoOwnershipAsync(packId, cancellationToken);
        if (pack is null)
        {
            return;
        }

        try
        {
            // The book already carries the story the parent read in the preview, adopted at
            // fulfilment. Writing another one here would replace the story they chose to buy
            // with a different one, which is the whole reason that adoption exists.
            if (pack.Status == AdventurePackStatus.StoryReady
                && !string.IsNullOrWhiteSpace(pack.GeneratedJson))
            {
                logger.LogInformation(
                    "Book {PackId} already has its previewed story; going straight to illustrations.",
                    packId);

                await SetProgressAsync(
                    packId,
                    "ისტორია მზადაა — ვხატავთ წიგნის გვერდებს…",
                    cancellationToken);

                EnqueuePreviewIllustrationJob(packId);
                return;
            }

            await adventurePackRepository.UpdateStatusAsync(
                packId,
                AdventurePackStatus.GeneratingStory,
                null,
                null,
                null,
                cancellationToken);

            await SetProgressAsync(
                packId,
                "ვიწყებთ… შეგიძლია დატოვო ეს გვერდი — წიგნს შენს ბიბლიოთეკაში შევინახავთ.",
                cancellationToken);

            var input = await BuildGenerationInputAsync(pack, cancellationToken);

            await SetProgressAsync(
                packId,
                "იწერება შენი უნიკალური ისტორია… ~30 წამი",
                cancellationToken);

            var content = await openAiService.GenerateAdventureContentAsync(input, pack.Id, cancellationToken);
            var pageCount = ResolveEffectivePageCount(pack);
            NormalizeStoryPages(content, pageCount);
            var generatedJson = JsonSerializer.Serialize(content, JsonOptions);

            await adventurePackRepository.UpdateStatusAsync(
                packId,
                AdventurePackStatus.StoryReady,
                generatedJson,
                null,
                null,
                cancellationToken);

            if (pack.IsWelcomeGiftStory)
            {
                await SetProgressAsync(
                    packId,
                    "ისტორია დაწერილია — ვხატავთ უფასო ნიმუშის ილუსტრაციას (~1 წუთი)…",
                    cancellationToken);

                // Free 1-page illustrated sample (the welcome perk) — no credit is charged.
                backgroundJobClient.Enqueue<IAdventureGenerationService>(service =>
                    service.ProcessFreeSampleIllustrationAsync(packId, CancellationToken.None));
            }
            else if (pack.IsFullyUnlocked)
            {
                // Payment already happened: the book is being made, not unlocked. Story
                // finished, so illustrations start immediately rather than waiting for a
                // second "buy illustrations" click that no longer exists.
                await SetProgressAsync(
                    packId,
                    "ისტორია მზადაა — ვხატავთ წიგნის გვერდებს…",
                    cancellationToken);

                EnqueuePreviewIllustrationJob(packId);
            }
            else
            {
                await SetProgressAsync(
                    packId,
                    "თქვენი უფასო ნიმუში მზადაა. სრული წიგნისთვის გადაიხადეთ შეკვეთის გვერდზე.",
                    cancellationToken);
            }

            await SendStoryReadyEmailAsync(pack, input.ChildName, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Story generation failed for pack {PackId}", packId);
            await FailPackAsync(packId, ex.Message, cancellationToken);
        }
    }

    public async Task EnsurePreviewIllustrationQueuedAsync(Guid packId, CancellationToken cancellationToken)
    {
        var pack = await adventurePackRepository.GetByIdNoOwnershipAsync(packId, cancellationToken);
        if (pack is null)
        {
            return;
        }

        if (pack.Status != AdventurePackStatus.StoryReady)
        {
            return;
        }

        if (HasAllSlideshowIllustrations(pack))
        {
            return;
        }

        if (!pack.IsFullyUnlocked)
        {
            return;
        }

        // A book whose status says Ready but whose pages have no pictures.
        //
        // The illustration job claims a book only when this status is None, Failed or a stale
        // Generating, so a book left saying Ready is a book that can never be picked up again —
        // it would sit unillustrated forever, and the parent has already paid for it. Reaching
        // here means the status is not telling the truth, so it is reset and the work re-queued.
        if (pack.PreviewIllustrationStatus == PreviewIllustrationStatus.Ready)
        {
            logger.LogWarning(
                "Book {PackId} says its illustrations are ready but is missing them; queueing again.",
                packId);

            await adventurePackRepository.UpdatePreviewIllustrationAsync(
                packId,
                PreviewIllustrationStatus.None,
                pack.PreviewIllustrationUrl,
                cancellationToken);

            EnqueuePreviewIllustrationJob(packId);
            return;
        }

        // Resume a stalled illustration job for a paid book. New jobs are kicked off by
        // order fulfilment (via ProcessStoryGenerationAsync) or QueueIllustrationAsync —
        // this path only restarts one that already started and went quiet.
        if (pack.PreviewIllustrationStatus == PreviewIllustrationStatus.Generating
            && IsPreviewIllustrationStale(pack))
        {
            EnqueuePreviewIllustrationJob(packId);
            return;
        }

        // Never started at all: fulfilment queued it and the process died before the job ran.
        if (pack.PreviewIllustrationStatus is PreviewIllustrationStatus.None
            or PreviewIllustrationStatus.Failed)
        {
            EnqueuePreviewIllustrationJob(packId);
        }
    }

    private void EnqueuePreviewIllustrationJob(Guid packId)
    {
        backgroundJobClient.Enqueue<IAdventureGenerationService>(service =>
            service.ProcessPreviewIllustrationAsync(packId, CancellationToken.None));
    }

    public async Task ProcessPreviewIllustrationAsync(Guid packId, CancellationToken cancellationToken)
    {
        var pack = await adventurePackRepository.GetByIdNoOwnershipAsync(packId, cancellationToken);
        if (pack is null)
        {
            return;
        }

        if (HasAllSlideshowIllustrations(pack))
        {
            if (pack.PreviewIllustrationStatus is not PreviewIllustrationStatus.Ready)
            {
                var content = JsonSerializer.Deserialize<AdventureContentDto>(pack.GeneratedJson!, JsonOptions);
                var previewUrl = content?.StoryPages.FirstOrDefault()?.IllustrationUrl;
                await adventurePackRepository.UpdatePreviewIllustrationAsync(
                    packId,
                    PreviewIllustrationStatus.Ready,
                    previewUrl,
                    cancellationToken);
            }

            return;
        }

        if (pack.Status != AdventurePackStatus.StoryReady || string.IsNullOrWhiteSpace(pack.GeneratedJson))
        {
            return;
        }

        if (!await adventurePackRepository.TryClaimPreviewIllustrationGenerationAsync(
                packId,
                AdventureStoryConstants.PreviewIllustrationStaleMinutes,
                cancellationToken))
        {
            logger.LogDebug(
                "Skipping duplicate preview illustration job for pack {PackId} (status {Status})",
                packId,
                pack.PreviewIllustrationStatus);
            return;
        }

        logger.LogInformation("Starting illustration job for pack {PackId}", packId);

        try
        {
            var content = JsonSerializer.Deserialize<AdventureContentDto>(pack.GeneratedJson, JsonOptions)
                          ?? throw new InvalidOperationException("Failed to parse stored story JSON.");

            if (content.StoryPages.Count == 0)
            {
                throw new InvalidOperationException("Story has no pages.");
            }

            // Derived from the book, not from the row.
            //
            // This trimmed the story to StoryPageCount and saved the shortened version back, so a
            // sixteen-page book whose row still said six lost ten pages permanently — the pages
            // were not hidden, they were deleted from the stored story on the way to being
            // illustrated.
            var pageCount = EffectivePageCount(pack, content);
            if (NormalizeStoryPages(content, pageCount))
            {
                var trimmedJson = JsonSerializer.Serialize(content, JsonOptions);
                await adventurePackRepository.UpdateStatusAsync(
                    packId,
                    AdventurePackStatus.StoryReady,
                    trimmedJson,
                    null,
                    null,
                    cancellationToken);
            }

            // Fold this book into the series memory as soon as the TEXT exists, not after the
            // illustrations. The next book needs the companions and moments, not the pictures,
            // and illustration is the part most likely to fail and be retried.
            await seriesMemoryService.RecordBookAsync(
                pack,
                JsonSerializer.Serialize(content, JsonOptions),
                content.ChildName,
                cancellationToken);

            // Reuse photo bytes only — skip duplicate vision API calls (already done during story writing).
            var input = await BuildIllustrationInputAsync(pack, cancellationToken);
            var castPhotos = await LoadCastPhotosAsync(pack, input, cancellationToken);
            await IllustrateStoryPagesAsync(
                packId,
                pack,
                content,
                input,
                castPhotos,
                pageCount,
                cancellationToken);

            var previewUrl = content.StoryPages[0].IllustrationUrl;
            await adventurePackRepository.UpdatePreviewIllustrationAsync(
                packId,
                PreviewIllustrationStatus.Ready,
                previewUrl,
                cancellationToken);

            // The library list only carries these two columns, so without this the shelf
            // shows a generic world title and stock art instead of the book that was made.
            // A spread book already has the cover the parent chose to buy. Page one's
            // illustration is a scene from the story, not the cover, so it only fills in for a
            // book that never had one.
            await adventurePackRepository.UpdateBookPresentationAsync(
                packId,
                string.IsNullOrWhiteSpace(content.Title) ? null : content.Title,
                string.IsNullOrWhiteSpace(pack.CoverImageUrl) ? previewUrl : null,
                cancellationToken);

            await SetProgressAsync(
                packId,
                "წიგნი მზადაა! გახსენი ბიბლიოთეკაში.",
                cancellationToken);

            await SendSlideshowReadyEmailAsync(pack, input.ChildName, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Preview illustration failed for pack {PackId}", packId);

            // Nothing to refund: the book was paid for as a whole, and it stays fully
            // unlocked. The status is set to Failed so the retry path can pick it up.
            await adventurePackRepository.UpdatePreviewIllustrationAsync(
                packId,
                PreviewIllustrationStatus.Failed,
                pack.PreviewIllustrationUrl,
                cancellationToken);

            await SetProgressAsync(
                packId,
                "ილუსტრაციები ვერ დაიხატა — ვცდილობთ ხელახლა. ტექსტი შენახულია.",
                cancellationToken);
        }
    }

    public async Task ProcessFreeSampleIllustrationAsync(Guid packId, CancellationToken cancellationToken)
    {
        var pack = await adventurePackRepository.GetByIdNoOwnershipAsync(packId, cancellationToken);
        if (pack is null
            || pack.Status != AdventurePackStatus.StoryReady
            || string.IsNullOrWhiteSpace(pack.GeneratedJson))
        {
            return;
        }

        // Already fully illustrated (e.g. the user paid before the sample ran) — nothing to do.
        if (HasAllSlideshowIllustrations(pack))
        {
            return;
        }

        try
        {
            var content = JsonSerializer.Deserialize<AdventureContentDto>(pack.GeneratedJson, JsonOptions)
                          ?? throw new InvalidOperationException("Failed to parse stored story JSON.");

            if (content.StoryPages.Count == 0)
            {
                return;
            }

            // Paint ONLY the welcome-gift page(s) for free. previewIllustrationStatus is left untouched (None) so the
            // paid unlock can still claim the job later and illustrate the remaining pages.
            var samplePageCount = Math.Min(AdventureStoryConstants.WelcomeGiftPageCount, content.StoryPages.Count);

            var input = await BuildIllustrationInputAsync(pack, cancellationToken);
            var castPhotos = await LoadCastPhotosAsync(pack, input, cancellationToken);
            await IllustrateStoryPagesAsync(
                packId,
                pack,
                content,
                input,
                castPhotos,
                samplePageCount,
                cancellationToken);

            await SetProgressAsync(
                packId,
                "უფასო ილუსტრირებული გვერდი მზადაა.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            // The sample is a free perk — never fail the pack or charge anything; the story
            // stays readable either way.
            logger.LogWarning(ex, "Free sample illustration failed for pack {PackId}", packId);
            await SetProgressAsync(
                packId,
                "ისტორია წასაკითხად მზადაა.",
                cancellationToken);
        }
    }

    public async Task ProcessPdfGenerationAsync(Guid packId, CancellationToken cancellationToken)
    {
        var pack = await adventurePackRepository.GetByIdNoOwnershipAsync(packId, cancellationToken);
        if (pack is null)
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(pack.GeneratedJson))
            {
                throw new InvalidOperationException("Story content is missing.");
            }

            var content = JsonSerializer.Deserialize<AdventureContentDto>(pack.GeneratedJson, JsonOptions)
                          ?? throw new InvalidOperationException("Failed to parse stored story JSON.");

            await LoadIllustrationsForPdfAsync(pack, content, cancellationToken);

            await SetProgressAsync(packId, "PDF-ს ვაწყობთ…", 85, cancellationToken);

            // The cover is the book's own, fetched here rather than reused from page one:
            // those are different pictures, and printing one as the other both duplicated a
            // page and left the real cover out of the book. Best effort — a book with no
            // cover art still prints, with a typeset cover instead.
            byte[]? coverBytes = null;
            if (!string.IsNullOrWhiteSpace(pack.CoverImageUrl))
            {
                try
                {
                    coverBytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(
                        pack.CoverImageUrl,
                        cancellationToken);
                }
                catch (Exception coverEx)
                {
                    logger.LogWarning(coverEx, "Cover art unavailable for pack {PackId}; typesetting one.", packId);
                }
            }

            var pdfBytes = adventurePdfService.GeneratePdf(new PdfBookRequest
            {
                Content = content,
                ThemeName = pack.Theme.ToString(),
                CoverImage = coverBytes,
                Language = pack.StoryLanguage ?? "ka",
                // The back cover's QR goes where the reader's button goes: start the next book.
                ContinueUrl = $"{_emailOptions.BaseUrl.TrimEnd('/')}/create"
            });
            await SetProgressAsync(packId, "წიგნს ვინახავთ…", 95, cancellationToken);

            var blobName = $"{pack.UserId}/{pack.Id}.pdf";
            var pdfUrl = await blobStorageService.UploadAsync(blobName, pdfBytes, "application/pdf", cancellationToken);

            var generatedJson = JsonSerializer.Serialize(content, JsonOptions);
            await adventurePackRepository.UpdateStatusAsync(
                packId,
                AdventurePackStatus.Completed,
                generatedJson,
                pdfUrl,
                null,
                cancellationToken);

            await SetProgressAsync(
                packId,
                "მზადაა! PDF-ის ჩამოსატვირთად გახსენი ბიბლიოთეკა.",
                100,
                cancellationToken);

            try
            {
                await SendPdfReadyEmailAsync(pack, content.ChildName, cancellationToken);
            }
            catch (Exception emailEx)
            {
                logger.LogWarning(emailEx, "PDF ready email failed for pack {PackId}", packId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PDF generation failed for pack {PackId}", packId);
            var current = await adventurePackRepository.GetByIdNoOwnershipAsync(packId, cancellationToken);
            if (current?.Status != AdventurePackStatus.Completed)
            {
                // PDF export is free and PdfCreditCharged tracks the illustration credit, so nothing is refunded here.
                await adventurePackRepository.UpdateStatusAsync(
                    packId,
                    AdventurePackStatus.StoryReady,
                    pack.GeneratedJson,
                    pack.PdfUrl,
                    ex.Message,
                    cancellationToken);
            }
            await SetProgressAsync(
                packId,
                "PDF ვერ შეიქმნა. ისტორია შენახულია — სცადე ხელახლა.",
                null,
                cancellationToken);
        }
    }

    /// <summary>Illustration job — no vision API; photos go straight to the image edit endpoint.</summary>
    private async Task<AdventureGenerationInput> BuildIllustrationInputAsync(
        AdventurePack pack,
        CancellationToken cancellationToken)
    {
        var cast = await bookCastResolver.ResolveAsync(pack, cancellationToken);

        return new AdventureGenerationInput
        {
            ChildName = cast.Hero.Name,
            Age = cast.HeroAge,
            Gender = cast.Hero.Gender,
            Theme = pack.Theme,
            ChildAppearanceDescription = cast.Hero.AppearanceDescription,
            FamilyMembers = cast.Supporting.Select(member => new FamilyMemberCastEntry
            {
                Name = member.Name,
                Relationship = member.Relationship ?? string.Empty,
                PhotoUrl = member.PhotoUrl,
                AppearanceDescription = member.AppearanceDescription
            }).ToList(),
            OptionalStoryNotes = pack.OptionalStoryNotes,
            StoryLanguage = NormalizeLanguage(pack.StoryLanguage),
            StoryPageCount = ResolveEffectivePageCount(pack)
        };
    }

    private async Task<AdventureGenerationInput> BuildGenerationInputAsync(
        AdventurePack pack,
        CancellationToken cancellationToken)
    {
        var cast = await bookCastResolver.ResolveAsync(pack, cancellationToken);
        var language = NormalizeLanguage(pack.StoryLanguage);

        var heroAppearance = await ResolveHeroAppearanceAsync(pack, cast, language, cancellationToken);

        var supporting = new List<FamilyMemberCastEntry>();
        foreach (var member in cast.Supporting)
        {
            supporting.Add(new FamilyMemberCastEntry
            {
                Name = member.Name,
                Relationship = member.Relationship ?? string.Empty,
                PhotoUrl = member.PhotoUrl,
                AppearanceDescription = await ResolveSupportingAppearanceAsync(
                    pack, member, language, cancellationToken)
            });
        }

        // Only the story pass needs the series memory; the illustration pass rebuilds its own
        // input and would just be paying to carry text the image model never reads.
        var seriesMemory = pack.SeriesId is { } seriesId
            ? await seriesMemoryService.GetPromptMemoryAsync(seriesId, cancellationToken)
            : null;

        // Operator tuning for this age band and world. Absent or untuned means the built-in
        // age guidance stands alone, exactly as before the matrix existed.
        var storyRule = await storyRuleRepository.ResolveAsync(
            StoryAgeBands.ForAge(cast.HeroAge),
            pack.Theme.ToString(),
            cancellationToken);

        return new AdventureGenerationInput
        {
            ChildName = cast.Hero.Name,
            Age = cast.HeroAge,
            Gender = cast.Hero.Gender,
            Theme = pack.Theme,
            ChildAppearanceDescription = heroAppearance,
            FamilyMembers = supporting,
            OptionalStoryNotes = pack.OptionalStoryNotes,
            StoryLanguage = language,
            StoryPageCount = ResolveEffectivePageCount(pack),
            SeriesMemory = seriesMemory,
            ChapterNumber = pack.SequenceNumber > 0 ? pack.SequenceNumber : 1,
            StoryRule = storyRule
        };
    }

    /// <summary>
    /// The hero's face, described once and reused. The cache is only trusted when it was
    /// derived from the photo currently on file — a parent who uploads a new portrait must
    /// get a new description, or every illustration would keep the old face.
    /// </summary>
    private async Task<string?> ResolveHeroAppearanceAsync(
        AdventurePack pack,
        BookCast cast,
        string language,
        CancellationToken cancellationToken)
    {
        var hero = cast.Hero;

        if (IsAppearanceCacheFresh(hero))
        {
            return hero.AppearanceDescription;
        }

        if (string.IsNullOrWhiteSpace(hero.PhotoUrl))
        {
            return null;
        }

        var photo = await TryLoadPhotoAsync(hero.PhotoUrl, cancellationToken);
        if (photo is null)
        {
            return null;
        }

        var described = await TryDescribeAsync(
            photo.Value.Bytes,
            photo.Value.ContentType,
            AdventurePromptBuilder.BuildHeroPhotoDescribePrompt(language, hero.Name, cast.HeroAge),
            hero.Name,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(described))
        {
            await bookCastResolver.CacheAppearanceAsync(pack.UserId, hero, described, cancellationToken);
        }

        return described;
    }

    private async Task<string?> ResolveSupportingAppearanceAsync(
        AdventurePack pack,
        BookCastMember member,
        string language,
        CancellationToken cancellationToken)
    {
        if (IsAppearanceCacheFresh(member))
        {
            return member.AppearanceDescription;
        }

        if (string.IsNullOrWhiteSpace(member.PhotoUrl))
        {
            return null;
        }

        var photo = await TryLoadPhotoAsync(member.PhotoUrl, cancellationToken);
        if (photo is null)
        {
            return null;
        }

        var described = await TryDescribeAsync(
            photo.Value.Bytes,
            photo.Value.ContentType,
            AdventurePromptBuilder.BuildFamilyPhotoDescribePrompt(
                language, member.Name, member.Relationship ?? string.Empty),
            member.Name,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(described))
        {
            await bookCastResolver.CacheAppearanceAsync(pack.UserId, member, described, cancellationToken);
        }

        return described;
    }

    private static bool IsAppearanceCacheFresh(BookCastMember member) =>
        !string.IsNullOrWhiteSpace(member.PhotoUrl)
        && !string.IsNullOrWhiteSpace(member.AppearanceDescription)
        && string.Equals(member.PhotoUrl, member.AppearancePhotoUrl, StringComparison.Ordinal);

    private async Task<(byte[] Bytes, string ContentType)?> TryLoadPhotoAsync(
        string photoUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(photoUrl, cancellationToken);
            return (bytes, InferImageContentType(photoUrl));
        }
        catch (Exception ex)
        {
            // A missing portrait degrades the likeness; it must not stop the book.
            logger.LogWarning(ex, "Could not load cast photo {PhotoUrl}", photoUrl);
            return null;
        }
    }

    private async Task<string?> TryDescribeAsync(
        byte[] bytes,
        string contentType,
        string prompt,
        string memberName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await openAiService.DescribeCharacterFromPhotoAsync(bytes, contentType, prompt, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not describe the photo for cast member {Name}", memberName);
            return null;
        }
    }

    private async Task SendStoryReadyEmailAsync(AdventurePack pack, string childName, CancellationToken cancellationToken)
    {
        try
        {
            var user = await userRepository.GetByIdAsync(pack.UserId, cancellationToken);
            if (user is null)
            {
                return;
            }

            var packUrl = $"{_emailOptions.BaseUrl.TrimEnd('/')}/my-packs";
            await emailService.SendStoryReadyAsync(
                user.Email,
                childName,
                pack.Theme.ToString(),
                packUrl,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send story-ready email for pack {PackId}", pack.Id);
        }
    }

    private async Task SendSlideshowReadyEmailAsync(AdventurePack pack, string childName, CancellationToken cancellationToken)
    {
        try
        {
            var user = await userRepository.GetByIdAsync(pack.UserId, cancellationToken);
            if (user is null)
            {
                return;
            }

            var packUrl = $"{_emailOptions.BaseUrl.TrimEnd('/')}/my-packs";
            await emailService.SendSlideshowReadyAsync(
                user.Email,
                childName,
                pack.Theme.ToString(),
                packUrl,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send slideshow-ready email for pack {PackId}", pack.Id);
        }
    }

    private async Task SendPdfReadyEmailAsync(AdventurePack pack, string childName, CancellationToken cancellationToken)
    {
        try
        {
            var user = await userRepository.GetByIdAsync(pack.UserId, cancellationToken);
            if (user is null)
            {
                return;
            }

            // Straight to the book. /my-packs is a shelf, and the parent then has to find the
            // one book the email was about.
            var packUrl = $"{_emailOptions.BaseUrl.TrimEnd('/')}/reader/{pack.Id}";
            await emailService.SendPdfReadyAsync(
                user.Email,
                childName,
                pack.Theme.ToString(),
                packUrl,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send PDF-ready email for pack {PackId}", pack.Id);
        }
    }

    private async Task FailPackAsync(Guid packId, string message, CancellationToken cancellationToken)
    {
        // Story TEXT generation is free, so a text failure costs the user nothing to refund — except the one-time
        // welcome perk: if this book had claimed the free 2-page sample, give that allowance back on failure.
        var pack = await adventurePackRepository.GetByIdNoOwnershipAsync(packId, cancellationToken);
        if (pack?.IsWelcomeGiftStory == true)
        {
            await userRepository.RefundWelcomeStoryAsync(pack.UserId, cancellationToken);
        }

        await adventurePackRepository.UpdateStatusAsync(
            packId,
            AdventurePackStatus.Failed,
            null,
            null,
            message,
            cancellationToken);
        await SetProgressAsync(
            packId,
            "რაღაც შეფერხდა. სცადე ხელახლა ან აირჩიე სხვა თემა.",
            null,
            cancellationToken);
    }

    private async Task SetProgressAsync(Guid packId, string message, CancellationToken cancellationToken)
    {
        await adventurePackRepository.UpdateProgressMessageAsync(packId, message, cancellationToken);
    }

    /// <summary>
    /// Progress with a number the client can draw. Percentages used to live inside the Georgian
    /// message ("PDF-ს ვაწყობთ… ~90%"), which meant the only way to render a bar was to parse prose.
    /// </summary>
    /// <param name="percent">Null clears the bar, which is what a finished or failed job wants.</param>
    private async Task SetProgressAsync(Guid packId, string message, int? percent, CancellationToken cancellationToken)
    {
        await adventurePackRepository.UpdateProgressAsync(
            packId,
            message,
            percent is { } value ? Math.Clamp(value, 0, 100) : null,
            cancellationToken);
    }

    private static string InferImageContentType(string url)
    {
        if (url.Contains(".png", StringComparison.OrdinalIgnoreCase))
        {
            return "image/png";
        }

        if (url.Contains(".webp", StringComparison.OrdinalIgnoreCase))
        {
            return "image/webp";
        }

        return "image/jpeg";
    }

    private static string NormalizeLanguage(string? code) => AdventurePromptTexts.NormalizeLanguageCode(code);

    private async Task<IReadOnlyList<CastPhotoReference>> LoadCastPhotosAsync(
        AdventurePack pack,
        AdventureGenerationInput input,
        CancellationToken cancellationToken)
    {
        var cast = new List<CastPhotoReference>();
        var (heroBytes, heroContentType) = await LoadHeroPhotoAsync(pack, cancellationToken);
        if (heroBytes is { Length: > 0 })
        {
            cast.Add(new CastPhotoReference
            {
                Name = input.ChildName,
                Relationship = "hero child",
                IsHero = true,
                AppearanceDescription = input.ChildAppearanceDescription,
                Bytes = heroBytes,
                ContentType = heroContentType
            });
        }

        foreach (var member in input.FamilyMembers)
        {
            if (string.IsNullOrWhiteSpace(member.PhotoUrl))
            {
                continue;
            }

            try
            {
                var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(
                    member.PhotoUrl,
                    cancellationToken);
                cast.Add(new CastPhotoReference
                {
                    Name = member.Name,
                    Relationship = member.Relationship,
                    IsHero = false,
                    AppearanceDescription = member.AppearanceDescription,
                    Bytes = bytes,
                    ContentType = InferImageContentType(member.PhotoUrl)
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not load family photo for cast member {Name}", member.Name);
            }
        }

        return cast;
    }

    private async Task<(byte[]? Bytes, string ContentType)> LoadHeroPhotoAsync(
        AdventurePack pack,
        CancellationToken cancellationToken)
    {
        var cast = await bookCastResolver.ResolveAsync(pack, cancellationToken);
        if (string.IsNullOrWhiteSpace(cast.Hero.PhotoUrl))
        {
            return (null, "image/jpeg");
        }

        var photo = await TryLoadPhotoAsync(cast.Hero.PhotoUrl, cancellationToken);
        return photo is null ? (null, "image/jpeg") : (photo.Value.Bytes, photo.Value.ContentType);
    }

    private async Task LoadIllustrationsForPdfAsync(
        AdventurePack pack,
        AdventureContentDto content,
        CancellationToken cancellationToken)
    {
        if (!CanExportPdf(pack))
        {
            throw new InvalidOperationException(
                pack.IsWelcomeGiftStory
                    ? "უფასო ილუსტრირებული გვერდი ჯერ არ არის მზად. სცადე ერთ წუთში."
                    : "დაელოდე, სანამ ყველა გვერდი დაილუსტრირდება, შემდეგ ჩამოტვირთე PDF.");
        }

        await SetProgressAsync(
            pack.Id,
            pack.IsWelcomeGiftStory && !HasAllSlideshowIllustrations(pack)
                ? "უფასო preview PDF-ს ვქმნით…"
                : "ილუსტრაციებს ვამზადებთ…",
            10,
            cancellationToken);

        var pageCount = EffectivePageCount(pack, content);
        NormalizeStoryPages(content, pageCount);

        for (var i = 0; i < pageCount && i < content.StoryPages.Count; i++)
        {
            var page = content.StoryPages[i];
            var illustrationUrl = ResolvePageIllustrationUrl(pack, page, i);
            if (!string.IsNullOrWhiteSpace(illustrationUrl))
            {
                page.ImageBytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(
                    illustrationUrl,
                    cancellationToken);
            }

            // 10 to 80: the pages are the long part, and the assembly that follows is quick.
            var pct = 10 + (int)Math.Round(70.0 * (i + 1) / Math.Max(1, pageCount));
            await SetProgressAsync(
                pack.Id,
                $"PDF-ისთვის ვამზადებთ გვერდს {i + 1} / {pageCount}…",
                pct,
                cancellationToken);
        }
    }

    internal static bool CanExportPdf(AdventurePack pack)
    {
        if (string.IsNullOrWhiteSpace(pack.GeneratedJson))
        {
            return false;
        }

        try
        {
            var content = JsonSerializer.Deserialize<AdventureContentDto>(pack.GeneratedJson, JsonOptions);
            if (content is null || content.StoryPages.Count == 0)
            {
                return false;
            }

            // This used to require art on all 16 pages of a spread book, 8 of which never get
            // any, so a finished book could never be exported.
            var pageCount = EffectivePageCount(pack, content);
            var expected = IllustratablePages(content, pageCount);
            var illustratedCount = CountIllustratedPages(pack, content, pageCount);

            if (pack.IsWelcomeGiftStory)
            {
                return illustratedCount >= AdventureStoryConstants.WelcomeGiftPageCount;
            }

            return expected.Count > 0 && illustratedCount == expected.Count;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The pages a finished book is expected to have art on. Every "is this book fully
    /// illustrated" check goes through here so they cannot drift apart again.
    /// </summary>
    private static List<StoryPageDto> IllustratablePages(AdventureContentDto content, int pageCount) =>
        content.StoryPages.Take(pageCount).Where(p => !p.IsTextOnlyPage).ToList();

    private static int CountIllustratedPages(AdventurePack pack, AdventureContentDto content, int pageCount)
    {
        var count = 0;
        for (var i = 0; i < pageCount && i < content.StoryPages.Count; i++)
        {
            var page = content.StoryPages[i];
            if (page.IsTextOnlyPage)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ResolvePageIllustrationUrl(pack, page, i)))
            {
                count++;
            }
        }

        return count;
    }

    private static string? ResolvePageIllustrationUrl(AdventurePack pack, StoryPageDto page, int pageIndex)
    {
        if (!string.IsNullOrWhiteSpace(page.IllustrationUrl))
        {
            return page.IllustrationUrl;
        }

        return pageIndex == 0 && !string.IsNullOrWhiteSpace(pack.PreviewIllustrationUrl)
            ? pack.PreviewIllustrationUrl
            : null;
    }

    internal static bool HasAllSlideshowIllustrations(AdventurePack pack)
    {
        if (string.IsNullOrWhiteSpace(pack.GeneratedJson))
        {
            return false;
        }

        try
        {
            var content = JsonSerializer.Deserialize<AdventureContentDto>(pack.GeneratedJson, JsonOptions);
            if (content is null || content.StoryPages.Count == 0)
            {
                return false;
            }

            var pageCount = EffectivePageCount(pack, content);
            var pages = content.StoryPages.Take(pageCount).ToList();
            var illustrated = IllustratablePages(content, pageCount);

            return pages.Count == pageCount
                   && illustrated.Count > 0
                   && illustrated.All(p => !string.IsNullOrWhiteSpace(p.IllustrationUrl));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Books cap at <see cref="AdventureStoryConstants.MaxPageCount"/> pages.</summary>
    private static int ResolveEffectivePageCount(AdventurePack pack) =>
        AdventureStoryConstants.ResolvePageCount(pack.StoryPageCount, pack.IsWelcomeGiftStory);

    /// <summary>
    /// How many pages a book actually has, according to the book.
    ///
    /// StoryPageCount is written when the row is created, before the story exists, and books
    /// created under an older constant carry an older number. Bounding the illustration pass by
    /// it means a sixteen-page book whose row still says six gets its first six pages drawn and
    /// is then declared finished — three pictures in a book that should have eight, with nothing
    /// reporting a fault, because every page it looked at did have one.
    ///
    /// The stored count is a guess made in advance. The content is the answer.
    /// </summary>
    private static int EffectivePageCount(AdventurePack pack, AdventureContentDto content) =>
        content.StoryPages.Count > 0
            ? Math.Min(content.StoryPages.Count, AdventureStoryConstants.MaxPageCount)
            : ResolveEffectivePageCount(pack);

    private static bool NormalizeStoryPages(AdventureContentDto content, int pageCount)
    {
        if (pageCount <= 0 || content.StoryPages.Count <= pageCount)
        {
            return false;
        }

        content.StoryPages = content.StoryPages.Take(pageCount).ToList();
        return true;
    }

    private async Task IllustrateStoryPagesAsync(
        Guid packId,
        AdventurePack pack,
        AdventureContentDto content,
        AdventureGenerationInput input,
        IReadOnlyList<CastPhotoReference> castPhotos,
        int pageCount,
        CancellationToken cancellationToken)
    {
        byte[]? characterAnchor = null;
        var pendingPages = new List<int>();

        var pageOneUrl = content.StoryPages.FirstOrDefault()?.IllustrationUrl;
        if (!string.IsNullOrWhiteSpace(pageOneUrl))
        {
            try
            {
                characterAnchor = await blobStorageService.DownloadBytesFromStoredUrlAsync(
                    pageOneUrl,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not load page-1 anchor for pack {PackId}", packId);
            }
        }

        for (var i = 0; i < pageCount && i < content.StoryPages.Count; i++)
        {
            var page = content.StoryPages[i];
            if (!string.IsNullOrWhiteSpace(page.IllustrationUrl))
            {
                continue;
            }

            // Half the pages of a spread book are prose facing a picture. Drawing them would
            // double the images a book costs to produce and put a picture where the words are
            // supposed to have the page to themselves.
            if (page.IsTextOnlyPage)
            {
                continue;
            }

            pendingPages.Add(i);
        }

        if (pendingPages.Count == 0)
        {
            return;
        }

        var persistLock = new SemaphoreSlim(1, 1);
        var maxParallel = Math.Clamp(_openAiOptions.IllustrationMaxParallel, 1, 3);
        var pacingSeconds = Math.Max(0, _openAiOptions.IllustrationPacingSeconds);
        var staggerSeconds = Math.Max(0, _openAiOptions.IllustrationStaggerSeconds);

        async Task<byte[]> RenderPageAsync(int pageIndex, byte[]? anchor)
        {
            await adventurePackRepository.TouchPreviewIllustrationHeartbeatAsync(packId, cancellationToken);
            logger.LogInformation(
                "Rendering illustration page {Page} of {Total} for pack {PackId}",
                pageIndex + 1,
                pageCount,
                packId);

            var page = content.StoryPages[pageIndex];
            var pageCastPhotos = SelectCastPhotosForPage(castPhotos, anchor is not { Length: > 0 });

            // When the story call wrote the prompt, use it as written. It was composed by the pass
            // that also wrote the words, with the character lock quoted inside it, so it describes
            // this scene and this hero. Rebuilding it here from the page text would throw that
            // away and re-introduce the drift the master call exists to prevent. Books written
            // before the master call carry no prompt, and those still get one built for them.
            var imagePrompt = string.IsNullOrWhiteSpace(page.ImagePrompt)
                ? AdventurePromptBuilder.BuildStoryImagePrompt(
                    input,
                    page,
                    pageIndex,
                    pack.Id,
                    anchor is { Length: > 0 },
                    pageCastPhotos)
                : page.ImagePrompt;

            return await openAiService.GenerateStoryImageAsync(
                imagePrompt,
                new StoryImageReference
                {
                    CharacterAnchorBytes = anchor,
                    CastPhotos = pageCastPhotos
                },
                cancellationToken);
        }

        async Task PersistPageAsync(int pageIndex, byte[] imageBytes)
        {
            var storedImage = referenceImageNormalizer.NormalizeForStorageWebp(imageBytes);
            content.StoryPages[pageIndex].IllustrationUrl = await blobStorageService.UploadAsync(
                PageIllustrationBlobName(pack.UserId, pack.Id, pageIndex),
                storedImage.Bytes,
                storedImage.ContentType,
                cancellationToken);

            await persistLock.WaitAsync(cancellationToken);
            try
            {
                var generatedJson = JsonSerializer.Serialize(content, JsonOptions);
                await adventurePackRepository.UpdateStatusAsync(
                    packId,
                    AdventurePackStatus.StoryReady,
                    generatedJson,
                    null,
                    null,
                    cancellationToken);

                // The explicit percent is what the client reads for the progress bar, so it
                // has to survive translation — the prose around it is free to change.
                var illustrationPct = 35 + (int)Math.Round(60.0 * (pageIndex + 1) / Math.Max(1, pageCount));
                await SetProgressAsync(
                    packId,
                    $"ილუსტრაციებს ვხატავთ… გვერდი {pageIndex + 1} / {pageCount} მზადაა · ~{Math.Min(95, illustrationPct)}%",
                    cancellationToken);
            }
            finally
            {
                persistLock.Release();
            }
        }

        if (characterAnchor is null)
        {
            var bootstrapIndex = pendingPages[0];
            var bootstrapBytes = await RenderPageAsync(bootstrapIndex, null);
            characterAnchor = bootstrapBytes;
            await PersistPageAsync(bootstrapIndex, bootstrapBytes);
            pendingPages.RemoveAt(0);

            if (pendingPages.Count > 0 && pacingSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(pacingSeconds), cancellationToken);
            }
        }

        if (pendingPages.Count == 0)
        {
            return;
        }

        if (maxParallel == 1)
        {
            foreach (var pageIndex in pendingPages)
            {
                var imageBytes = await RenderPageAsync(pageIndex, characterAnchor);
                await PersistPageAsync(pageIndex, imageBytes);

                if (pageIndex != pendingPages[^1] && pacingSeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(pacingSeconds), cancellationToken);
                }
            }

            return;
        }

        using var parallelGate = new SemaphoreSlim(maxParallel);
        var parallelTasks = pendingPages.Select(async (pageIndex, order) =>
        {
            if (staggerSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(order * staggerSeconds), cancellationToken);
            }

            await parallelGate.WaitAsync(cancellationToken);
            try
            {
                var imageBytes = await RenderPageAsync(pageIndex, characterAnchor);
                await PersistPageAsync(pageIndex, imageBytes);
            }
            finally
            {
                parallelGate.Release();
            }
        });

        await Task.WhenAll(parallelTasks);
    }

    /// <summary>Page 1 uses the real photo; later pages use only the Pixar anchor so the model does not drift.</summary>
    private static IReadOnlyList<CastPhotoReference> SelectCastPhotosForPage(
        IReadOnlyList<CastPhotoReference> castPhotos,
        bool includeHeroPhoto) =>
        includeHeroPhoto
            ? castPhotos
            : castPhotos.Where(static c => !c.IsHero).ToList();

    private static bool IsPreviewIllustrationStale(AdventurePack pack)
    {
        if (pack.PreviewIllustrationStatus != PreviewIllustrationStatus.Generating)
        {
            return false;
        }

        var lastTouch = pack.PreviewIllustrationUpdatedAt ?? pack.CreatedAt;
        return DateTime.UtcNow - lastTouch > TimeSpan.FromMinutes(AdventureStoryConstants.PreviewIllustrationStaleMinutes);
    }

    private static string PageIllustrationBlobName(Guid userId, Guid packId, int pageIndex) =>
        $"{userId}/{packId}/page-{pageIndex}.webp";
}
