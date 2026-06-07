using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using Hangfire;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class AdventureGenerationService(
    IBackgroundJobClient backgroundJobClient,
    IAdventurePackRepository adventurePackRepository,
    IChildRepository childRepository,
    IFamilyMemberRepository familyMemberRepository,
    IUserRepository userRepository,
    ISubscriptionService subscriptionService,
    IOpenAiService openAiService,
    IAdventurePdfService adventurePdfService,
    IBlobStorageService blobStorageService,
    IEmailService emailService,
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

    public async Task<Guid> QueueGenerationAsync(
        Guid userId,
        GenerateAdventurePackRequest request,
        CancellationToken cancellationToken)
    {
        _ = await childRepository.GetByIdAsync(request.ChildId, userId, cancellationToken)
            ?? throw new InvalidOperationException("Child not found.");

        await subscriptionService.EnsureGenerationAllowedAsync(userId, cancellationToken);

        var isWelcomeGift = await userRepository.TryConsumeWelcomeStoryAsync(userId, cancellationToken);
        var pageCount = isWelcomeGift
            ? AdventureStoryConstants.WelcomeGiftPageCount
            : AdventureStoryConstants.FullPageCount;

        var pack = new AdventurePack
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ChildId = request.ChildId,
            Theme = request.Theme,
            Status = AdventurePackStatus.Pending,
            OptionalStoryNotes = string.IsNullOrWhiteSpace(request.OptionalStoryNotes)
                ? null
                : request.OptionalStoryNotes.Trim(),
            StoryLanguage = NormalizeLanguage(request.StoryLanguage),
            StoryPageCount = pageCount,
            IsWelcomeGiftStory = isWelcomeGift,
            ProgressMessage = isWelcomeGift
                ? "Queued — your free 2-page welcome story will appear in My Books when ready (~2 minutes)."
                : "Queued — your story will appear in My Books when ready (usually 1–2 minutes).",
            CreatedAt = DateTime.UtcNow
        };

        await adventurePackRepository.CreatePendingAsync(pack, cancellationToken);
        backgroundJobClient.Enqueue<IAdventureGenerationService>(service =>
            service.ProcessStoryGenerationAsync(pack.Id, CancellationToken.None));

        return pack.Id;
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

        if (!HasAllSlideshowIllustrations(pack))
        {
            throw new InvalidOperationException(
                "Illustrations are still being created. Wait until your slideshow is fully illustrated in My Books, then export PDF.");
        }

        await subscriptionService.EnsurePdfGenerationAllowedAsync(userId, cancellationToken);
        await subscriptionService.TryChargePdfCreditAsync(userId, packId, cancellationToken);

        await adventurePackRepository.UpdateStatusAsync(
            packId,
            AdventurePackStatus.GeneratingPdf,
            pack.GeneratedJson,
            null,
            null,
            cancellationToken);

        await SetProgressAsync(
            packId,
            "Building your printable PDF from slideshow illustrations… ~30 seconds",
            cancellationToken);

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
            await adventurePackRepository.UpdateStatusAsync(
                packId,
                AdventurePackStatus.GeneratingStory,
                null,
                null,
                null,
                cancellationToken);

            await SetProgressAsync(
                packId,
                "Starting… You can leave this page — we will save your story in My Books.",
                cancellationToken);

            var input = await BuildGenerationInputAsync(pack, cancellationToken);

            await SetProgressAsync(
                packId,
                "Writing your unique story… ~30 seconds",
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

            await SetProgressAsync(
                packId,
                "Story written — painting illustrations (~8–12 min for 6 pages)…",
                cancellationToken);

            await SendStoryReadyEmailAsync(pack, input.ChildName, cancellationToken);

            EnqueuePreviewIllustrationJob(packId);
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

        if (pack.PreviewIllustrationStatus == PreviewIllustrationStatus.Generating
            && !IsPreviewIllustrationStale(pack))
        {
            return;
        }

        EnqueuePreviewIllustrationJob(packId);
        await Task.CompletedTask;
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

            var pageCount = ResolveEffectivePageCount(pack);
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

            await SetProgressAsync(
                packId,
                "Your picture-book slideshow is ready! Read it in My Books.",
                cancellationToken);

            await SendSlideshowReadyEmailAsync(pack, input.ChildName, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Preview illustration failed for pack {PackId}", packId);
            await adventurePackRepository.UpdatePreviewIllustrationAsync(
                packId,
                PreviewIllustrationStatus.Failed,
                pack.PreviewIllustrationUrl,
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

            await SetProgressAsync(packId, "Assembling your storybook PDF… ~90%", cancellationToken);

            var pdfBytes = adventurePdfService.GeneratePdf(content, pack.Theme.ToString());
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
                "Done! Open My Books to download your storybook PDF.",
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
                await subscriptionService.RefundPdfCreditIfChargedAsync(pack.UserId, packId, cancellationToken);
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
                "PDF creation failed. Your story is still saved — try Create illustrated PDF again.",
                cancellationToken);
        }
    }

    /// <summary>Illustration job — no vision API; photos go straight to the image edit endpoint.</summary>
    private async Task<AdventureGenerationInput> BuildIllustrationInputAsync(
        AdventurePack pack,
        CancellationToken cancellationToken)
    {
        var child = await childRepository.GetByIdAsync(pack.ChildId, pack.UserId, cancellationToken)
                    ?? throw new InvalidOperationException("Child not found.");

        var familyMembers = await familyMemberRepository.GetByChildIdAsync(pack.ChildId, pack.UserId, cancellationToken);
        var cast = familyMembers.Select(m => new FamilyMemberCastEntry
        {
            Name = m.Name,
            Relationship = m.Relationship,
            PhotoUrl = m.PhotoUrl,
            AppearanceDescription = null
        }).ToList();

        return new AdventureGenerationInput
        {
            ChildName = child.Name,
            Age = child.Age,
            Theme = pack.Theme,
            ChildAppearanceDescription = child.AppearanceDescription,
            FamilyMembers = cast,
            OptionalStoryNotes = pack.OptionalStoryNotes,
            StoryLanguage = pack.StoryLanguage ?? "en",
            StoryPageCount = ResolveEffectivePageCount(pack)
        };
    }

    private async Task<AdventureGenerationInput> BuildGenerationInputAsync(
        AdventurePack pack,
        CancellationToken cancellationToken)
    {
        var child = await childRepository.GetByIdAsync(pack.ChildId, pack.UserId, cancellationToken)
                    ?? throw new InvalidOperationException("Child not found.");

        var familyMembers = await familyMemberRepository.GetByChildIdAsync(pack.ChildId, pack.UserId, cancellationToken);

        byte[]? heroPhotoBytes = null;
        string heroPhotoContentType = "image/jpeg";
        if (!string.IsNullOrWhiteSpace(child.PhotoUrl))
        {
            try
            {
                heroPhotoBytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(
                    child.PhotoUrl,
                    cancellationToken);
                heroPhotoContentType = InferImageContentType(child.PhotoUrl);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not load child hero photo for {ChildId}", child.Id);
            }
        }

        string? childAppearance = null;
        if (!string.IsNullOrWhiteSpace(child.PhotoUrl)
            && child.PhotoUrl == child.AppearancePhotoUrl
            && !string.IsNullOrWhiteSpace(child.AppearanceDescription))
        {
            childAppearance = child.AppearanceDescription;
        }
        else if (heroPhotoBytes is not null)
        {
            childAppearance = await DescribeChildFromPhotoAsync(
                child,
                heroPhotoBytes,
                heroPhotoContentType,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(childAppearance))
            {
                await childRepository.UpdateAppearanceCacheAsync(
                    child.Id,
                    pack.UserId,
                    childAppearance,
                    child.PhotoUrl,
                    cancellationToken);
            }
        }

        var cast = new List<FamilyMemberCastEntry>();
        foreach (var member in familyMembers)
        {
            var appearance = await ResolveFamilyAppearanceAsync(member, cancellationToken);
            cast.Add(new FamilyMemberCastEntry
            {
                Name = member.Name,
                Relationship = member.Relationship,
                PhotoUrl = member.PhotoUrl,
                AppearanceDescription = appearance
            });
        }

        return new AdventureGenerationInput
        {
            ChildName = child.Name,
            Age = child.Age,
            Theme = pack.Theme,
            ChildAppearanceDescription = childAppearance,
            FamilyMembers = cast,
            OptionalStoryNotes = pack.OptionalStoryNotes,
            StoryLanguage = pack.StoryLanguage ?? "en",
            StoryPageCount = ResolveEffectivePageCount(pack)
        };
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

            var packUrl = $"{_emailOptions.BaseUrl.TrimEnd('/')}/my-packs";
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
            "Something went wrong. Try again or pick a simpler theme.",
            cancellationToken);
    }

    private async Task SetProgressAsync(Guid packId, string message, CancellationToken cancellationToken)
    {
        await adventurePackRepository.UpdateProgressMessageAsync(packId, message, cancellationToken);
    }

    private async Task<string?> DescribeChildFromPhotoAsync(
        Child child,
        byte[] photoBytes,
        string contentType,
        CancellationToken cancellationToken)
    {
        try
        {
            return await openAiService.DescribeCharacterFromPhotoAsync(
                photoBytes,
                contentType,
                $"This photo is the hero child {child.Name}, age {child.Age}, for a Pixar-style adventure book. "
                + "List concrete visual traits an illustrator must copy: exact hair color and style, skin tone, eye color, glasses/freckles, face shape, and 2–3 distinctive details. "
                + "Write for a cartoon designer — be specific, not vague.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not describe child photo for {ChildId}", child.Id);
            return null;
        }
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

    private async Task<string?> ResolveFamilyAppearanceAsync(FamilyMember member, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(member.PhotoUrl))
        {
            return null;
        }

        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(member.PhotoUrl, cancellationToken);
            return await openAiService.DescribeCharacterFromPhotoAsync(
                bytes,
                InferImageContentType(member.PhotoUrl),
                $"This photo is {member.Name} ({member.Relationship}) in a Pixar-style children's adventure book. "
                + "List concrete visual traits an illustrator must copy: exact hair color and style, skin tone, age, glasses, and distinctive details.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not describe family photo for {MemberId}", member.Id);
            return null;
        }
    }

    private static string NormalizeLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "en";
        }

        var c = code.Trim().ToLowerInvariant();
        return c is "ka" or "es" or "en" or "fr" or "de" ? c : "en";
    }

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
        var child = await childRepository.GetByIdAsync(pack.ChildId, pack.UserId, cancellationToken)
                    ?? throw new InvalidOperationException("Child not found.");

        if (string.IsNullOrWhiteSpace(child.PhotoUrl))
        {
            return (null, "image/jpeg");
        }

        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(child.PhotoUrl, cancellationToken);
            return (bytes, InferImageContentType(child.PhotoUrl));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load child hero photo for {ChildId}", child.Id);
            return (null, "image/jpeg");
        }
    }

    private async Task LoadIllustrationsForPdfAsync(
        AdventurePack pack,
        AdventureContentDto content,
        CancellationToken cancellationToken)
    {
        if (!HasAllSlideshowIllustrations(pack))
        {
            throw new InvalidOperationException(
                "PDF export only uses your saved slideshow illustrations. Wait until every page is illustrated.");
        }

        await SetProgressAsync(
            pack.Id,
            "Using your slideshow illustrations… ~40%",
            cancellationToken);

        var pageCount = ResolveEffectivePageCount(pack);
        NormalizeStoryPages(content, pageCount);

        for (var i = 0; i < pageCount && i < content.StoryPages.Count; i++)
        {
            var page = content.StoryPages[i];
            if (string.IsNullOrWhiteSpace(page.IllustrationUrl))
            {
                throw new InvalidOperationException(
                    $"Page {i + 1} is missing an illustration. Open My Books and wait for the slideshow to finish.");
            }

            page.ImageBytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(
                page.IllustrationUrl,
                cancellationToken);

            var pct = 40 + (int)Math.Round(45.0 * (i + 1) / Math.Max(1, pageCount));
            await SetProgressAsync(
                pack.Id,
                $"Preparing page {i + 1} of {pageCount} for PDF… ~{pct}%",
                cancellationToken);
        }
    }

    private static bool HasAllSlideshowIllustrations(AdventurePack pack)
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

            var pageCount = ResolveEffectivePageCount(pack);
            var pages = content.StoryPages.Take(pageCount).ToList();
            return pages.Count == pageCount
                   && pages.All(p => !string.IsNullOrWhiteSpace(p.IllustrationUrl));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Welcome gift = 2 pages; full stories cap at <see cref="AdventureStoryConstants.FullPageCount"/>.</summary>
    private static int ResolveEffectivePageCount(AdventurePack pack) =>
        AdventureStoryConstants.ResolvePageCount(pack.StoryPageCount, pack.IsWelcomeGiftStory);

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
            var imagePrompt = AdventurePromptBuilder.BuildStoryImagePrompt(
                input,
                page,
                pageIndex,
                pack.Id,
                anchor is { Length: > 0 },
                pageCastPhotos);

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
            content.StoryPages[pageIndex].IllustrationUrl = await blobStorageService.UploadAsync(
                PageIllustrationBlobName(pack.UserId, pack.Id, pageIndex),
                imageBytes,
                "image/webp",
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

                await SetProgressAsync(
                    packId,
                    $"Creating illustrations… page {pageIndex + 1} of {pageCount} is ready",
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
