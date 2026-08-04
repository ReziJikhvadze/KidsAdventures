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
/// <summary>
/// Reads and exports books. Creating one is not possible here: a book only comes into
/// existence when an order is fulfilled, which is what makes "pay, then generate" a
/// property of the system rather than a convention the client is trusted to follow.
/// </summary>
public sealed class AdventurePacksController(
    IAdventureGenerationService generationService,
    IAdventurePackRepository adventurePackRepository,
    IBookCastResolver bookCastResolver,
    IBlobStorageService blobStorageService,
    IUserContextService userContext,
    IGuestRateLimiter guestRateLimiter,
    ILogger<AdventurePacksController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Pages a parent may read before paying: the cover and page one.</summary>
    private const int PreviewReadablePages = 1;

    /// <summary>Free, no-login single-page teaser. Generates inline and returns the image as a data URL.</summary>
    [AllowAnonymous]
    [HttpPost("guest-preview")]
    [RequestSizeLimit(8_000_000)]
    public async Task<ActionResult<GuestPreviewResult>> GuestPreview(
        [FromForm] string name,
        [FromForm] int age,
        [FromForm] string theme,
        [FromForm] string? gender,
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
                    Gender = NormalizeGender(gender),
                    StoryLanguage = storyLanguage,
                    OptionalStoryNotes = string.IsNullOrWhiteSpace(optionalStoryNotes) ? null : optionalStoryNotes.Trim(),
                    PhotoBytes = photoBytes,
                    PhotoContentType = contentType
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

    /// <summary>Only the two values the prompt understands; anything else is "unspecified".</summary>
    private static string? NormalizeGender(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "girl" => "girl",
            "boy" => "boy",
            _ => null,
        };

    private string GetClientKey()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Restarts illustration for a book whose render failed. Only a paid book gets here;
    /// the service re-checks that rather than trusting the route.
    /// </summary>
    [HttpPost("{id:guid}/illustrate")]
    public async Task<ActionResult<object>> Illustrate(Guid id, CancellationToken cancellationToken)
    {
        await generationService.QueueIllustrationAsync(userContext.GetUserId(), id, cancellationToken);
        return Accepted(new
        {
            id,
            status = AdventurePackStatus.StoryReady.ToString(),
            previewIllustrationStatus = PreviewIllustrationStatus.Generating.ToString()
        });
    }

    [HttpPost("{id:guid}/generate-pdf")]
    public async Task<ActionResult<object>> GeneratePdf(Guid id, CancellationToken cancellationToken)
    {
        var userId = userContext.GetUserId();
        var pack = await adventurePackRepository.GetByIdAsync(id, userId, cancellationToken);
        if (pack is null)
        {
            return NotFound();
        }

        if (!pack.IsFullyUnlocked)
        {
            return BadRequest(new { message = "ეს წიგნი ჯერ არ არის შეძენილი." });
        }

        await generationService.QueuePdfGenerationAsync(userId, id, cancellationToken);
        return Accepted(new
        {
            id,
            status = AdventurePackStatus.GeneratingPdf.ToString(),
            usesSlideshowImages = PackHasAllIllustrations(pack)
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

        // Same gate as the detail response: an unpaid book only ever serves its cover.
        if (!pack.IsFullyUnlocked && pageIndex >= PreviewReadablePages)
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

    private static void MapBookFields(AdventurePack x, AdventurePackResponse target)
    {
        target.Id = x.Id;
        target.UserId = x.UserId;
        target.Theme = x.Theme;
        target.Status = x.Status;
        target.PdfUrl = x.PdfUrl;
        target.ProgressMessage = x.ProgressMessage;
        target.ErrorMessage = x.ErrorMessage;
        target.StoryLanguage = x.StoryLanguage;
        target.PreviewIllustrationStatus = x.PreviewIllustrationStatus;
        target.StoryPageCount = x.StoryPageCount;
        target.IsWelcomeGiftStory = x.IsWelcomeGiftStory;
        target.CreatedAt = x.CreatedAt;
        target.WorldId = x.WorldId;
        target.PrimaryCharacterId = x.PrimaryCharacterId;
        target.SeriesId = x.SeriesId;
        target.SequenceNumber = x.SequenceNumber;
        target.ContinuesFromBookId = x.ContinuesFromBookId;
        target.AccessLevel = x.AccessLevel;
        target.IsUnlocked = x.IsFullyUnlocked;
        target.HasPrintEntitlement = x.HasPrintEntitlement;
        target.CoverImageUrl = x.CoverImageUrl;
        target.Title = x.Title;
    }

    private static AdventurePackResponse Map(AdventurePack x)
    {
        var response = new AdventurePackResponse();
        MapBookFields(x, response);
        return response;
    }

    private async Task<AdventurePackDetailResponse> MapDetailAsync(
        AdventurePack pack,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var detail = new AdventurePackDetailResponse();
        MapBookFields(pack, detail);

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

            detail.Title = string.IsNullOrWhiteSpace(pack.Title) ? content.Title : pack.Title;
            detail.ChildName = content.ChildName;

            // A preview returns the whole story text and withholds only the artwork. The
            // prose is generated in a single pass regardless, and a preview that shows
            // blank pages does not read like a book. The illustrations remain the gated
            // asset — GetIllustration still 404s past the allowance.
            var unlockedPages = pack.IsFullyUnlocked
                ? content.StoryPages.Count
                : Math.Min(PreviewReadablePages, content.StoryPages.Count);

            detail.StoryPages = content.StoryPages
                .Select((page, index) =>
                {
                    var isLocked = index >= unlockedPages;
                    var isIllustrated = !isLocked && IsPageIllustrated(pack, page, index);
                    return new StoryPageContentDto
                    {
                        Title = page.Title,
                        Caption = page.Caption,
                        Content = page.Content,
                        IsLocked = isLocked,
                        IsIllustrated = isIllustrated,
                        IllustrationUrl = isIllustrated
                            ? $"/api/adventure-packs/{pack.Id}/illustrations/{index}"
                            : null
                    };
                })
                .ToList();

            detail.LockedPageCount = content.StoryPages.Count - unlockedPages;
        }
        catch
        {
            /* return partial detail */
        }

        if (string.IsNullOrWhiteSpace(detail.ChildName))
        {
            var cast = await bookCastResolver.ResolveAsync(pack, cancellationToken);
            detail.ChildName = cast.Hero.Name;
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

    private static bool IsPageIllustrated(AdventurePack pack, StoryPageDto page, int pageIndex)
    {
        if (!string.IsNullOrWhiteSpace(page.IllustrationUrl))
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
