using System.Text.Json;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/adventure-packs")]
public sealed class AdventurePacksController(
    IAdventureGenerationService generationService,
    IAdventurePackRepository adventurePackRepository,
    IChildRepository childRepository,
    IBlobStorageService blobStorageService,
    ISubscriptionService subscriptionService,
    IUserContextService userContext,
    IGuestRateLimiter guestRateLimiter,
    ILogger<AdventurePacksController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost("generate")]
    public async Task<ActionResult<object>> Generate([FromBody] GenerateAdventurePackRequest request, CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var packId = await generationService.QueueGenerationAsync(userId, request, cancellationToken);
        var balance = await subscriptionService.GetAccountBalanceAsync(userId, cancellationToken);
        return Accepted(new
        {
            id = packId,
            status = AdventurePackStatus.Pending.ToString(),
            bookCredits = balance.BookCredits,
            storiesRemainingThisMonth = balance.StoriesRemainingThisMonth,
            welcomeStoryRemaining = balance.WelcomeStoryRemaining
        });
    }

    /// <summary>Free, no-login single-page teaser. Generates inline and returns the image as a data URL.</summary>
    [AllowAnonymous]
    [HttpPost("guest-preview")]
    [RequestSizeLimit(8_000_000)]
    public async Task<ActionResult<GuestPreviewResult>> GuestPreview(
        [FromForm] string name,
        [FromForm] int age,
        [FromForm] string theme,
        [FromForm] string? storyLanguage,
        [FromForm] string? optionalStoryNotes,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Child's name is required." });
        }

        if (age < 1 || age > 18)
        {
            return BadRequest(new { message = "Please enter a valid age." });
        }

        if (!Enum.TryParse<ThemeType>(theme, ignoreCase: true, out var themeType))
        {
            return BadRequest(new { message = "Please choose a valid theme." });
        }

        if (!guestRateLimiter.TryAcquire(GetClientKey()))
        {
            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                new { message = "You've reached the free preview limit. Please sign in to keep creating stories." });
        }

        byte[]? photoBytes = null;
        var contentType = "image/jpeg";
        if (photo is { Length: > 0 })
        {
            if (photo.Length > 6_000_000)
            {
                return BadRequest(new { message = "Photo is too large (max 6 MB)." });
            }

            using var ms = new MemoryStream();
            await photo.CopyToAsync(ms, cancellationToken);
            photoBytes = ms.ToArray();
            if (!string.IsNullOrWhiteSpace(photo.ContentType))
            {
                contentType = photo.ContentType;
            }
        }

        try
        {
            var result = await generationService.GenerateGuestPreviewAsync(
                new GuestPreviewInput
                {
                    ChildName = name.Trim(),
                    Age = age,
                    Theme = themeType,
                    StoryLanguage = storyLanguage,
                    OptionalStoryNotes = string.IsNullOrWhiteSpace(optionalStoryNotes) ? null : optionalStoryNotes.Trim(),
                    PhotoBytes = photoBytes,
                    PhotoContentType = contentType,
                    ClientKey = GetClientKey()
                },
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Guest preview generation failed");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { message = "We couldn't create your preview right now. Please try again in a moment." });
        }
    }

    private string GetClientKey()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>Saves a story created during the no-login teaser to the now signed-in parent's account.</summary>
    [HttpPost("import-guest")]
    public async Task<ActionResult<object>> ImportGuest(
        [FromBody] ImportGuestStoryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = await generationService.ImportGuestStoryAsync(
                userContext.GetUserId(),
                request,
                cancellationToken);
            return Ok(new { id });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/illustrate")]
    public async Task<ActionResult<object>> Illustrate(Guid id, CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        await generationService.QueueIllustrationAsync(userId, id, cancellationToken);
        var balance = await subscriptionService.GetAccountBalanceAsync(userId, cancellationToken);
        return Accepted(new
        {
            id,
            status = AdventurePackStatus.StoryReady.ToString(),
            previewIllustrationStatus = PreviewIllustrationStatus.Generating.ToString(),
            bookCredits = balance.BookCredits
        });
    }

    [HttpPost("{id:guid}/generate-pdf")]
    public async Task<ActionResult<object>> GeneratePdf(Guid id, CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var pack = await adventurePackRepository.GetByIdAsync(id, userId, cancellationToken)
                   ?? throw new InvalidOperationException("Pack not found.");

        await generationService.QueuePdfGenerationAsync(userId, id, cancellationToken);
        var balance = await subscriptionService.GetAccountBalanceAsync(userId, cancellationToken);
        var usesSlideshowImages = PackHasAllIllustrations(pack);
        return Accepted(new
        {
            id,
            status = AdventurePackStatus.GeneratingPdf.ToString(),
            bookCredits = balance.BookCredits,
            usesSlideshowImages
        });
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdventurePackResponse>>> Get(CancellationToken cancellationToken)
    {
        var rows = await adventurePackRepository.GetByUserIdAsync(userContext.GetUserId(), cancellationToken);
        return Ok(rows.Select(Map).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdventurePackDetailResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var row = await adventurePackRepository.GetByIdAsync(id, userId, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        if (row.Status == AdventurePackStatus.StoryReady)
        {
            await generationService.EnsurePreviewIllustrationQueuedAsync(id, cancellationToken);
            row = await adventurePackRepository.GetByIdAsync(id, userId, cancellationToken) ?? row;
        }

        return Ok(await MapDetailAsync(row, userId, cancellationToken));
    }

    [HttpGet("{id:guid}/illustrations/{pageIndex:int}")]
    public async Task<IActionResult> GetIllustration(Guid id, int pageIndex, CancellationToken cancellationToken)
    {
        if (pageIndex < 0)
        {
            return BadRequest("Invalid page index.");
        }

        var pack = await adventurePackRepository.GetByIdAsync(id, userContext.GetUserId(), cancellationToken);
        if (pack is null)
        {
            return NotFound();
        }

        var storedUrl = await ResolveIllustrationBlobUrlAsync(pack, pageIndex, cancellationToken);
        if (string.IsNullOrWhiteSpace(storedUrl))
        {
            return NotFound();
        }

        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(storedUrl, cancellationToken);
            return File(bytes, "image/webp");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Illustration blob missing for pack {PackId} page {PageIndex}", id, pageIndex);
            return NotFound();
        }
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var row = await adventurePackRepository.GetByIdAsync(id, userContext.GetUserId(), cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        if (row.Status != AdventurePackStatus.Completed || string.IsNullOrWhiteSpace(row.PdfUrl))
        {
            return BadRequest("Pack is not ready.");
        }

        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(row.PdfUrl, cancellationToken);
            var fileName = $"adventure-pack-{row.Id}.pdf";
            return File(bytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PDF blob missing for pack {PackId}. Stored url: {PdfUrl}", id, row.PdfUrl);
            return NotFound("PDF file was not found in storage. Please generate a new pack.");
        }
    }

    private static AdventurePackResponse Map(AdventurePack x) => new()
    {
        Id = x.Id,
        UserId = x.UserId,
        ChildId = x.ChildId,
        Theme = x.Theme,
        Status = x.Status,
        PdfUrl = x.PdfUrl,
        ProgressMessage = x.ProgressMessage,
        ErrorMessage = x.ErrorMessage,
        StoryLanguage = x.StoryLanguage,
        PreviewIllustrationStatus = x.PreviewIllustrationStatus,
        StoryPageCount = x.StoryPageCount,
        IsWelcomeGiftStory = x.IsWelcomeGiftStory,
        CreatedAt = x.CreatedAt
    };

    private async Task<AdventurePackDetailResponse> MapDetailAsync(
        AdventurePack pack,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var detail = new AdventurePackDetailResponse
        {
            Id = pack.Id,
            UserId = pack.UserId,
            ChildId = pack.ChildId,
            Theme = pack.Theme,
            Status = pack.Status,
            PdfUrl = pack.PdfUrl,
            ProgressMessage = pack.ProgressMessage,
            ErrorMessage = pack.ErrorMessage,
            StoryLanguage = pack.StoryLanguage,
            CreatedAt = pack.CreatedAt,
            PreviewIllustrationStatus = pack.PreviewIllustrationStatus,
            StoryPageCount = pack.StoryPageCount,
            IsWelcomeGiftStory = pack.IsWelcomeGiftStory
        };

        if (pack.Status is not (AdventurePackStatus.StoryReady or AdventurePackStatus.GeneratingPdf
            or AdventurePackStatus.Completed) || string.IsNullOrWhiteSpace(pack.GeneratedJson))
        {
            return detail;
        }

        try
        {
            var content = JsonSerializer.Deserialize<AdventureContentDto>(pack.GeneratedJson, JsonOptions);
            if (content is null)
            {
                return detail;
            }

            detail.Title = content.Title;
            detail.ChildName = content.ChildName;
            detail.StoryPages = content.StoryPages.Select((p, index) =>
            {
                var isIllustrated = IsPageIllustrated(pack, p, index);
                return new StoryPageContentDto
                {
                    Title = p.Title,
                    Caption = p.Caption,
                    Content = p.Content,
                    IsIllustrated = isIllustrated,
                    IllustrationUrl = isIllustrated
                        ? $"/api/adventure-packs/{pack.Id}/illustrations/{index}"
                        : null,
                    Interactive = SanitizeInteractive(p.Interactive)
                };
            }).ToList();
        }
        catch
        {
            /* return partial detail */
        }

        if (string.IsNullOrWhiteSpace(detail.ChildName))
        {
            var child = await childRepository.GetByIdAsync(pack.ChildId, userId, cancellationToken);
            detail.ChildName = child?.Name;
        }

        return detail;
    }

    private static bool PackHasAllIllustrations(AdventurePack pack)
    {
        if (string.IsNullOrWhiteSpace(pack.GeneratedJson))
        {
            return false;
        }

        try
        {
            var content = JsonSerializer.Deserialize<AdventureContentDto>(pack.GeneratedJson, JsonOptions);
            return content?.StoryPages.Count > 0
                   && content.StoryPages.All(p => !string.IsNullOrWhiteSpace(p.IllustrationUrl));
        }
        catch
        {
            return false;
        }
    }

    private static PageInteractiveDto? SanitizeInteractive(PageInteractiveDto? interactive)
    {
        if (interactive is null)
        {
            return null;
        }

        var hasAvatar = interactive.AvatarTap is not null;
        var hasFindIt = interactive.FindIt is not null
                        && !string.IsNullOrWhiteSpace(interactive.FindIt.Prompt)
                        && interactive.FindIt.Region is not null;
        var hasCounting = interactive.Counting is not null
                          && interactive.Counting.Target > 0
                          && interactive.Counting.Target <= 10
                          && !string.IsNullOrWhiteSpace(interactive.Counting.Prompt);
        var hasRevealItem = interactive.RevealItem is not null
                            && !string.IsNullOrWhiteSpace(interactive.RevealItem.CoverLabel)
                            && !string.IsNullOrWhiteSpace(interactive.RevealItem.RevealLabel)
                            && interactive.RevealItem.Region is not null;

        if (!hasAvatar && !hasFindIt && !hasCounting && !hasRevealItem)
        {
            return null;
        }

        if (interactive.AvatarTap?.Region is not null)
        {
            interactive.AvatarTap.Region = ClampRegion(interactive.AvatarTap.Region);
        }

        if (hasFindIt)
        {
            interactive.FindIt!.Region = ClampRegion(interactive.FindIt.Region);
        }

        if (hasCounting)
        {
            interactive.Counting!.Target = Math.Clamp(interactive.Counting.Target, 1, 10);
        }

        if (hasRevealItem)
        {
            interactive.RevealItem!.Region = ClampRegion(interactive.RevealItem.Region);
        }
        else
        {
            interactive.RevealItem = null;
        }

        return interactive;
    }

    private static HotspotRegionDto ClampRegion(HotspotRegionDto region) => new()
    {
        X = Math.Clamp(region.X, 0, 100),
        Y = Math.Clamp(region.Y, 0, 100),
        W = Math.Clamp(region.W, 5, 60),
        H = Math.Clamp(region.H, 5, 60),
    };

    private static bool IsPageIllustrated(AdventurePack pack, StoryPageDto page, int pageIndex)
    {
        if (!string.IsNullOrWhiteSpace(page.IllustrationUrl))
        {
            return true;
        }

        // Welcome-gift books paint every page for free. Once the illustration job finishes, treat any
        // page that still lacks a URL as illustrated via the pack-level preview (covers racey JSON reads).
        if (pack.IsWelcomeGiftStory
            && pack.PreviewIllustrationStatus == PreviewIllustrationStatus.Ready
            && !string.IsNullOrWhiteSpace(pack.PreviewIllustrationUrl))
        {
            return true;
        }

        return pageIndex == 0
               && pack.PreviewIllustrationStatus == PreviewIllustrationStatus.Ready
               && !string.IsNullOrWhiteSpace(pack.PreviewIllustrationUrl);
    }

    private static async Task<string?> ResolveIllustrationBlobUrlAsync(
        AdventurePack pack,
        int pageIndex,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(pack.GeneratedJson))
        {
            try
            {
                var content = JsonSerializer.Deserialize<AdventureContentDto>(pack.GeneratedJson, JsonOptions);
                if (content is not null
                    && pageIndex >= 0
                    && pageIndex < content.StoryPages.Count
                    && !string.IsNullOrWhiteSpace(content.StoryPages[pageIndex].IllustrationUrl))
                {
                    return content.StoryPages[pageIndex].IllustrationUrl;
                }
            }
            catch
            {
                /* fall through to preview */
            }
        }

        if (pageIndex == 0
            && pack.PreviewIllustrationStatus == PreviewIllustrationStatus.Ready
            && !string.IsNullOrWhiteSpace(pack.PreviewIllustrationUrl))
        {
            return pack.PreviewIllustrationUrl;
        }

        await Task.CompletedTask;
        return null;
    }
}
