using System.Text.Json;
using AdventurePacks.Api.Domain.Enums;
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
                    Content = p.Content,
                    IsIllustrated = isIllustrated,
                    IllustrationUrl = isIllustrated
                        ? $"/api/adventure-packs/{pack.Id}/illustrations/{index}"
                        : null
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
