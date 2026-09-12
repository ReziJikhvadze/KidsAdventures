using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Net.Http.Headers;

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
    IMasterBookService masterBookService,
    IBekiDownloadStatusService downloadStatus,
    IOptions<ClientIpOptions> clientIpOptions,
    ICharacterRepository characterRepository,
    IMasterStoryRunRepository masterStoryRunRepository,
    IBekiPackFulfillment bekiFulfillment,
    IAdventurePdfService adventurePdfService,
    ILogger<AdventurePacksController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Pages a parent may read before paying: the cover and page one.</summary>
    private const int PreviewReadablePages = 1;

    /// <summary>
    /// How many unbought previews the parent's own space lists. Generous: they expire in a day,
    /// so a parent with more than a couple has been busy this afternoon, not for months.
    /// </summary>
    private const int PreviewListLimit = 12;

    /// <summary>
    /// Starts a whole book and hands back an id to watch.
    ///
    /// This replaces generating inside the request. A sixteen-page book takes minutes to write and
    /// Azure closes an inbound request at 230 seconds, so the old shape could only ever have
    /// worked for the short books it was built for.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("guest-preview/start")]
    [RequestSizeLimit(24_000_000)]
    public async Task<ActionResult<MasterStoryRunStartedDto>> StartGuestPreview(
        [FromForm] string name,
        [FromForm] int age,
        [FromForm] string theme,
        [FromForm] string? gender,
        [FromForm] string? eyeColor,
        [FromForm] string? birthDate,
        [FromForm] string? storyLanguage,
        [FromForm] string? optionalStoryNotes,
        [FromForm] string? characterId,
        IFormFile? photo,
        CancellationToken cancellationToken, [FromForm] Guid? reusePreviewId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Child's name is required." });
        }

        /*
          A second book for a child who already exists.

          The route is anonymous because the first book is written before anyone signs in, but a
          signed-in parent starting another one for a saved hero used to be asked for the same
          photograph again — the form had the child's name, date and eyes from the account and no
          bytes to send for the face. When the caller's token names the character's owner, the
          stored portrait is the photo, and the description that portrait was given for the
          previous book is reused rather than bought again. A photo uploaded alongside still wins:
          a parent who changed the picture meant to.
        */
        Character? knownHero = null;
        var signedIn = TryGetSignedInUserId(out var ownerId);
        if (signedIn && Guid.TryParse(characterId, out var heroId))
        {
            knownHero = await characterRepository.GetByIdAsync(heroId, ownerId, cancellationToken);
        }

        // The age the browser worked out is a conclusion; the date is the evidence. A book once
        // came back written for a one-year-old from a birth date the parent was sure said 2023,
        // and with only the number stored there was no way to tell which end was wrong. When a
        // date arrives it decides, and it is kept.
        var parsedBirthDate = ParseBirthDate(birthDate);
        var resolvedAge = parsedBirthDate is { } born ? AgeOn(born, DateOnly.FromDateTime(DateTime.UtcNow)) : age;

        if (resolvedAge < 1 || resolvedAge > 18)
        {
            return BadRequest(new { message = "Please enter a valid age." });
        }

        if (parsedBirthDate is not null && resolvedAge != age)
        {
            logger.LogWarning(
                "Age sent as {Sent} but the birth date {BirthDate} gives {Resolved}; using {Resolved}.",
                age, parsedBirthDate, resolvedAge, resolvedAge);
        }

        if (!Enum.TryParse<ThemeType>(theme, ignoreCase: true, out var themeType))
        {
            return BadRequest(new { message = "Please choose a valid theme." });
        }

        // The ceiling is for anonymous scripts. A signed-in parent is a known account, and one
        // behind an office or carrier-grade NAT used to be told to "sign in" by a limit keyed on
        // an address forty strangers share.
        if (!signedIn && !guestRateLimiter.TryAcquire(GetClientKey()))
        {
            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                new { message = "You've reached the free preview limit. Please sign in to keep creating stories." });
        }

        var photoBytes = await ReadPhotoAsync(photo, cancellationToken);
        if (photoBytes.TooLarge)
        {
            return BadRequest(new { message = "That photo is too large. Please choose a smaller one." });
        }

        MasterStoryRun? reuseRun = null;
        if (reusePreviewId is { } reuseId)
        {
            reuseRun = await masterBookService.GetAsync(reuseId, cancellationToken);
            if (!FastPreviewPlan.IsFast(reuseRun) || reuseRun!.Status != MasterStoryRunStatus.Ready
                || reuseRun.UserId is { } reuseOwner && (!signedIn || reuseOwner != ownerId)
                || reuseRun.ExpiresAt is { } expires && expires <= DateTime.UtcNow)
                return BadRequest(new { message = "Previous preview is unavailable." });
            if (photoBytes.Bytes is null)
                photoBytes = (await blobStorageService.DownloadBytesFromStoredUrlAsync(reuseRun.PhotoBlobUrl!, cancellationToken), "image/png", false);
        }

        string? cachedAppearance = null;
        if (knownHero is not null && photoBytes.Bytes is null && !string.IsNullOrWhiteSpace(knownHero.PhotoUrl))
        {
            try
            {
                var stored = await blobStorageService.DownloadBytesFromStoredUrlAsync(knownHero.PhotoUrl, cancellationToken);
                photoBytes = (stored, PhotoContentTypeFor(knownHero.PhotoUrl), false);

                // The cache is only the truth for the portrait it was written from.
                if (!string.IsNullOrWhiteSpace(knownHero.AppearanceDescription)
                    && string.Equals(knownHero.AppearancePhotoUrl, knownHero.PhotoUrl, StringComparison.Ordinal))
                {
                    cachedAppearance = knownHero.AppearanceDescription;
                }
            }
            catch (Exception ex)
            {
                // Not fatal: the run is written from the description the model gives, as it was
                // before portraits were parked at all. The parent is not asked for the file again.
                logger.LogWarning(ex, "Stored portrait for character {CharacterId} could not be read.", knownHero.Id);
            }
        }

        try
        {
            var runId = await masterBookService.StartAsync(
                new GuestPreviewInput
                {
                    ChildName = name.Trim(),
                    Age = resolvedAge,
                    BirthDate = parsedBirthDate,
                    Theme = themeType,
                    Gender = NormalizeGender(gender),
                    EyeColor = string.IsNullOrWhiteSpace(eyeColor) ? null : eyeColor.Trim(),
                    StoryLanguage = storyLanguage,
                    OptionalStoryNotes = string.IsNullOrWhiteSpace(optionalStoryNotes) ? null : optionalStoryNotes.Trim(),
                    PhotoBytes = photoBytes.Bytes,
                    PhotoContentType = photoBytes.ContentType,
                    AppearanceDescription = cachedAppearance,
                    /*
                      Whose preview this is, said at the start rather than at the till.

                      The run row has always had an owner column and only fulfilment ever wrote
                      it, so until a book was paid for the preview belonged to nobody — and the
                      parent's own space, which can only ask "what is mine", had nothing to show
                      for a story being written that minute. A guest is still a guest: no token,
                      no owner, and the expiry below is untouched either way.

                      The hero is the one this account owns — `knownHero` is read back through
                      the character repository with the caller's own id — so this cannot file a
                      preview under somebody else's child.
                    */
                    UserId = signedIn ? ownerId : null,
                    CharacterId = knownHero?.Id,
                    ReusePreviewId = reusePreviewId
                },
                cancellationToken);

            // What this run learned about the face is kept on the hero, so the next book for the
            // same child starts without the vision call at all.
            if (knownHero is not null && cachedAppearance is null && photoBytes.Bytes is not null
                && !string.IsNullOrWhiteSpace(knownHero.PhotoUrl))
            {
                try
                {
                    var run = await masterBookService.GetAsync(runId, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(run?.AppearanceDescription))
                    {
                        await characterRepository.UpdateAppearanceCacheAsync(
                            knownHero.Id, knownHero.UserId, run.AppearanceDescription, knownHero.PhotoUrl, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Appearance cache for character {CharacterId} was not written.", knownHero.Id);
                }
            }

            return Accepted(new MasterStoryRunStartedDto { RunId = runId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not start a guest book");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { message = "We couldn't start your story right now. Please try again in a moment." });
        }
    }

    /// <summary>What the waiting browser polls.</summary>
    [AllowAnonymous]
    [HttpGet("guest-preview/{runId:guid}")]
    public async Task<ActionResult<MasterStoryRunStatusDto>> GetGuestPreview(Guid runId, CancellationToken cancellationToken)
    {
        // Status first, and only status. This is asked every few seconds for the minutes a book
        // takes to write; reading the whole row each time pulled the entire finished story out of
        // SQL to look at one short string.
        var progress = await masterBookService.GetProgressAsync(runId, cancellationToken);
        if (progress is null)
        {
            return NotFound(new { message = "That story has expired. Please create a new one." });
        }

        var dto = new MasterStoryRunStatusDto
        {
            RunId = progress.Id,
            Status = progress.Status,
            ProgressMessage = progress.ProgressMessage,
            // Same rule as the pack DTOs: a browser gets the parent-facing Georgian line,
            // never the raw failure code the run row stores for the operator.
            ErrorMessage = progress.ErrorMessage is null
                ? null
                : ParentFacingFailure.ToParentMessage(progress.ErrorMessage),
            // Ready now means the story is written. The cover is painted after it, so this stays
            // null for a little longer and the reader opens on the world's own artwork until it
            // arrives.
            CoverImageUrl = progress.CoverImageUrl is null
                ? null
                : $"/api/adventure-packs/guest-preview/{progress.Id}/cover"
        };

        if (progress.Status != MasterStoryRunStatus.Ready)
        {
            return Ok(dto);
        }

        // Only once, when there is a book to describe.
        var run = await masterBookService.GetAsync(runId, cancellationToken);
        if (run?.ContentJson is null)
        {
            return Ok(dto);
        }

        if (FastPreviewPlan.IsFast(run))
        {
            // The paid story may replace ContentJson later; the preview is immutable in blob storage.
            FastPreviewRevision? revision;
            if (string.IsNullOrWhiteSpace(run.StoryJson))
                revision = JsonSerializer.Deserialize<FastPreviewRevision>(run.ContentJson);
            else
            {
                await using var saved = await blobStorageService.DownloadAsync(FastPreviewPlan.ReceiptName(run.Id), cancellationToken);
                revision = await JsonSerializer.DeserializeAsync<FastPreviewRevision>(saved, cancellationToken: cancellationToken);
            }
            if (revision is null) return StatusCode(503);
            dto.PreviewVersion = FastPreviewPlan.Version;
            dto.CoverRevisionId = revision.CoverRevisionId;
            dto.IntroImageUrl = $"/api/adventure-packs/guest-preview/{run.Id}/intro";
            dto.Title = revision.Title;
            dto.ChildName = run.ChildName;
            dto.WorldId = AdventurePacks.Api.Domain.WorldThemes.WorldIdFor(Enum.Parse<ThemeType>(run.Theme));
            dto.BirthDate = run.BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            dto.Gender = run.Gender;
            dto.HasPortrait = true;
            dto.PageCount = BookFormat.PageCount;
            return Ok(dto);
        }

        var content = JsonSerializer.Deserialize<AdventureContentDto>(run.ContentJson, JsonOptions);
        if (content is null)
        {
            return Ok(dto);
        }

        // The first page and the cover are the taste. The rest of the book is not sent: it used
        // to travel back so the client could hand it in at checkout, and fulfilment reads it from
        // our own row now — which also stops nine illustration prompts being published to anyone
        // who asks for a preview.
        dto.Title = content.Title;
        dto.WorldId = AdventurePacks.Api.Domain.WorldThemes.TryGetTheme(run.Theme, out var previewTheme)
            ? AdventurePacks.Api.Domain.WorldThemes.WorldIdFor(previewTheme)
            : null;
        dto.ChildName = content.ChildName;
        dto.FirstPageTitle = content.StoryPages.FirstOrDefault()?.Title;
        dto.FirstPageText = content.StoryPages.Skip(1).FirstOrDefault()?.Content;
        dto.PageCount = content.StoryPages.Count;

        // What the parent typed, so a journey resumed in another tab does not ask again. The
        // name comes from the story (the run's own, unchanged); the rest from the row.
        dto.BirthDate = run.BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        dto.Gender = string.IsNullOrWhiteSpace(run.Gender) ? null : run.Gender;
        dto.EyeColor = run.EyeColor;
        dto.HasPortrait = !string.IsNullOrWhiteSpace(run.PhotoBlobUrl);

        return Ok(dto);
    }

    /// <summary>
    /// The teaser cover, served rather than linked.
    ///
    /// Anonymous because the parent has not signed up yet, and safe because the run id is a GUID
    /// nobody can guess and the row it names expires. Only the cover is reachable this way — the
    /// eight illustrations of a bought book are not.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("guest-preview/{runId:guid}/intro")]
    public async Task<IActionResult> GetGuestPreviewIntro(Guid runId, CancellationToken cancellationToken)
    {
        var run = await masterBookService.GetAsync(runId, cancellationToken);
        if (!FastPreviewPlan.IsFast(run) || run!.CoverImageUrl is null) return NotFound();
        var stream = await blobStorageService.DownloadAsync(FastPreviewPlan.IntroName(runId), cancellationToken);
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(stream, "image/webp");
    }

    [AllowAnonymous]
    [HttpGet("guest-preview/{runId:guid}/cover")]
    public async Task<IActionResult> GetGuestPreviewCover(Guid runId, CancellationToken cancellationToken)
    {
        var run = await masterBookService.GetAsync(runId, cancellationToken);
        if (run?.CoverImageUrl is null)
        {
            return NotFound();
        }

        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(run.CoverImageUrl, cancellationToken);
            Response.Headers.CacheControl = "private, max-age=86400";
            return File(bytes, "image/webp");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cover blob missing for run {RunId}", runId);
            return NotFound();
        }
    }

    /// <summary>
    /// The parent's previews that never became books.
    ///
    /// The shelf answers "what have I bought"; this answers "what did I start". They were the
    /// same question for as long as a preview belonged to nobody until it was paid for, and the
    /// only record of an unbought one lived in the browser that made it — so a parent who read a
    /// preview on their phone and opened their space on a laptop saw nothing at all.
    ///
    /// Bounded and short-lived by nature: only previews that have not become books and have not
    /// expired are listed, so this is a handful of rows at most.
    /// </summary>
    [HttpGet("previews")]
    public async Task<ActionResult<IReadOnlyList<GuestPreviewSummaryDto>>> ListPreviews(
        CancellationToken cancellationToken)
    {
        var runs = await masterStoryRunRepository.ListUnboughtForUserAsync(
            userContext.GetUserId(), PreviewListLimit, cancellationToken);

        var previews = runs.Select(run => new GuestPreviewSummaryDto
        {
            RunId = run.Id,
            CharacterId = run.CharacterId,
            Status = run.Status,
            ProgressMessage = run.ProgressMessage,
            // The parent-facing line, as everywhere else a run's failure reaches a browser.
            ErrorMessage = run.ErrorMessage is null
                ? null
                : ParentFacingFailure.ToParentMessage(run.ErrorMessage),
            Title = string.IsNullOrWhiteSpace(run.Title) ? null : run.Title,
            ChildName = run.ChildName,
            WorldId = AdventurePacks.Api.Domain.WorldThemes.TryGetTheme(run.Theme, out var theme)
                ? AdventurePacks.Api.Domain.WorldThemes.WorldIdFor(theme)
                : null,
            // The served route, never the blob: the same rule the status endpoint follows, and
            // the reason a parent's cover is not a URL anyone can pass around.
            CoverImageUrl = run.CoverImageUrl is null
                ? null
                : $"/api/adventure-packs/guest-preview/{run.Id}/cover",
            CreatedAt = run.CreatedAt,
            ExpiresAt = run.ExpiresAt
        }).ToList();

        return Ok(previews);
    }

    /// <summary>
    /// Puts a guest's preview in the name of the account that has just signed up for it.
    ///
    /// The sign-in screen tells the parent their preview is saved. It was true only of the tab
    /// they were standing in: the run was written before there was an account to own it, and
    /// nothing attached it to one until a payment went through. This is the missing half, called
    /// by the journey the moment the parent is signed in.
    ///
    /// It does not make the preview a book and it does not extend its life — the expiry is what
    /// keeps the promise that an unbought child's photograph is not kept — so a run that belongs
    /// to somebody else is answered with the same 204 as one that was attached. There is nothing
    /// to tell the caller: the id came from their own browser, and saying "that is not yours"
    /// would be the only way to learn anything from it.
    /// </summary>
    [HttpPost("guest-preview/{runId:guid}/claim")]
    public async Task<IActionResult> ClaimGuestPreview(Guid runId, CancellationToken cancellationToken)
    {
        var attached = await masterStoryRunRepository.AttachToUserAsync(
            runId, userContext.GetUserId(), cancellationToken);

        if (attached == 0)
        {
            logger.LogDebug("Preview {RunId} was not attached: it is missing or owned elsewhere.", runId);
        }

        return NoContent();
    }

    /// <summary>
    /// Which spreads of a book being generated already exist, plus the job's own progress. The
    /// generating screen polls this to show the parent real pictures instead of a spinner. A
    /// legacy-pipeline book simply has no spreads under these names and returns an empty list.
    /// </summary>
    [HttpGet("{id:guid}/making-of")]
    public async Task<ActionResult<object>> GetMakingOf(Guid id, CancellationToken cancellationToken)
    {
        var pack = await adventurePackRepository.GetByIdAsync(id, userContext.GetUserId(), cancellationToken);
        if (pack is null)
        {
            return NotFound();
        }

        var ready = new List<int>();
        for (var number = 1; number <= BookFormat.SpreadCount; number++)
        {
            if (await blobStorageService.ExistsAsync(
                    BekiPackBlobs.SpreadName(pack.UserId, pack.Id, number), cancellationToken))
            {
                ready.Add(number);
            }
        }

        return Ok(new
        {
            // The one fact this endpoint left out. The generating screen polls it every few
            // seconds and had no way to learn that the job behind the spreads had stopped: it read
            // a progress message frozen mid-sentence and kept waiting. A status the client can see
            // is what lets it stop waiting and say so.
            status = pack.Status.ToString(),
            progressMessage = pack.ProgressMessage,
            progressPercent = pack.ProgressPercent,
            spreads = ready
        });
    }

    [HttpGet("{id:guid}/making-of/{spread:int}")]
    public async Task<IActionResult> GetMakingOfImage(Guid id, int spread, CancellationToken cancellationToken)
    {
        if (spread is < 1 or > BookFormat.SpreadCount)
        {
            return NotFound();
        }

        var pack = await adventurePackRepository.GetByIdAsync(id, userContext.GetUserId(), cancellationToken);
        if (pack is null)
        {
            return NotFound();
        }

        try
        {
            var stream = await blobStorageService.DownloadAsync(
                BekiPackBlobs.SpreadName(pack.UserId, pack.Id, spread), cancellationToken);
            // Immutable once drawn, so the browser can keep it for the session.
            Response.Headers.CacheControl = "private, max-age=86400";
            return File(stream, "image/png");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Making-of spread {Spread} not available for pack {PackId}.", spread, id);
            return NotFound();
        }
    }

    /// <summary>Accepts what an &lt;input type="date"&gt; sends, and nothing more inventive.</summary>
    private static DateOnly? ParseBirthDate(string? value) =>
        DateOnly.TryParseExact(
            value?.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;

    private static int AgeOn(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;
        return birthDate > today.AddYears(-age) ? age - 1 : age;
    }

    private async Task<(byte[]? Bytes, string ContentType, bool TooLarge)> ReadPhotoAsync(
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        if (photo is not { Length: > 0 })
        {
            return (null, "image/jpeg", false);
        }

        if (photo.Length > 12_000_000)
        {
            // Reached through the action, so the response carries CORS headers and the parent sees
            // a real message instead of a request the browser can only describe as a CORS error.
            return (null, "image/jpeg", true);
        }

        using var ms = new MemoryStream();
        await photo.CopyToAsync(ms, cancellationToken);
        var contentType = string.IsNullOrWhiteSpace(photo.ContentType) ? "image/jpeg" : photo.ContentType;
        return (ms.ToArray(), contentType, false);
    }

    /// <summary>Free, no-login single-page teaser. Generates inline and returns the image as a data URL.</summary>
    [AllowAnonymous]
    [HttpPost("guest-preview")]
    // Generous on purpose. The browser downscales portraits before upload, so a request
    // anywhere near this is a client that did not — an older cached bundle, or a script. Being
    // rejected by the server before the action runs produces a response with no CORS headers,
    // which a browser can only report as a CORS error, hiding the real cause completely.
    [RequestSizeLimit(24_000_000)]
    public async Task<ActionResult<GuestPreviewResult>> GuestPreview(
        [FromForm] string name,
        [FromForm] int age,
        [FromForm] string theme,
        [FromForm] string? gender,
        [FromForm] string? storyLanguage,
        [FromForm] string? optionalStoryNotes,
        IFormFile? photo,
        CancellationToken cancellationToken, [FromForm] Guid? reusePreviewId = null)
    {
        await Task.CompletedTask;
        return StatusCode(StatusCodes.Status410Gone, new { message = "Use guest-preview/start to create the cover preview." });
    }

    /// <summary>Only the two values the prompt understands; anything else is "unspecified".</summary>
    private static string? NormalizeGender(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "girl" => "girl",
            "boy" => "boy",
            _ => null,
        };

    /// <summary>
    /// The guest preview's limiter key. Same rule as every other anonymous endpoint: trust the
    /// entry the nearest proxy wrote, never the one the caller sent.
    /// </summary>
    private string GetClientKey() =>
        ClientIpAddress.Resolve(HttpContext, clientIpOptions.Value.TrustedProxyHops);

    /// <summary>
    /// The signed-in parent on an anonymous route, when there is one. The token is still read
    /// under <c>[AllowAnonymous]</c>; what changes is that its absence is not an error here.
    /// </summary>
    private bool TryGetSignedInUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static string PhotoContentTypeFor(string url) => url switch
    {
        _ when url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) => "image/png",
        _ when url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) => "image/webp",
        _ => "image/jpeg"
    };

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

    /// <summary>
    /// The parent opened this book in the reader. Recorded on the book rather than in the
    /// browser so a book read on a laptop is not offered as unread on a phone.
    /// </summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var marked = await adventurePackRepository.MarkReadAsync(
            id, userContext.GetUserId(), cancellationToken);
        return marked ? NoContent() : NotFound();
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

        var responses = new List<AdventurePackResponse>(rows.Count);
        foreach (var row in rows)
        {
            var response = Map(row);
            response.DownloadHeld = await DownloadHeldAsync(row, cancellationToken);
            responses.Add(response);
        }

        return Ok(responses);
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

        /*
          The legacy auto-illustration trigger, and the guard amendment B5 exists for.

          Opening a book's detail page starts per-page illustration for a StoryReady pack. On the
          legacy pipeline that is the whole point — the story is written, the pictures are drawn on
          demand, and a parent who opens the book kicks it off. On the Beki pipeline StoryReady is
          a stage inside a job that is still running and is going to draw eight spreads itself: a
          second, different illustrator started here would spend money drawing per-page art nobody
          will ever see, into columns the composite book does not read, while the real job works.

          So the trigger is now the legacy branch of a decision rather than a rule about a status.
        */
        if (row.Status == AdventurePackStatus.StoryReady && !row.IsBekiPipeline)
        {
            await generationService.EnsurePreviewIllustrationQueuedAsync(id, cancellationToken);
            row = await adventurePackRepository.GetByIdAsync(id, userId, cancellationToken) ?? row;
        }

        return Ok(await MapDetailAsync(row, userId, cancellationToken));
    }

    [HttpGet("{id:guid}/cover")]
    public async Task<IActionResult> GetCover(Guid id, CancellationToken cancellationToken)
    {
        var pack = await adventurePackRepository.GetByIdAsync(id, userContext.GetUserId(), cancellationToken);
        if (pack is null || string.IsNullOrWhiteSpace(pack.CoverImageUrl))
        {
            return NotFound();
        }

        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(pack.CoverImageUrl, cancellationToken);
            var etag = new EntityTagHeaderValue('"' + Convert.ToHexString(SHA256.HashData(bytes)) + '"');
            if (Request.Headers.IfNoneMatch.Any(value => value == etag.Tag.ToString()))
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }

            Response.Headers.CacheControl = "private, max-age=604800";
            return File(bytes, "image/webp", lastModified: null, entityTag: etag);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cover blob missing for pack {PackId}", id);
            return NotFound();
        }
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

            // An illustration never changes once it has been drawn, and there are nine of them in
            // a book. Without these headers the browser re-downloaded every picture on every page
            // turn and again on every reopen, which is the whole reason a finished book felt slow
            // to read.
            //
            // private, because this is an authorised per-book resource and must not sit in a
            // shared proxy where another parent could be handed it.
            var etag = new EntityTagHeaderValue('"' + Convert.ToHexString(SHA256.HashData(bytes)) + '"');
            if (Request.Headers.IfNoneMatch.Any(value => value == etag.Tag.ToString()))
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }

            Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return File(bytes, "image/webp", lastModified: null, entityTag: etag);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Illustration blob missing for pack {PackId} page {PageIndex}", id, pageIndex);
            return NotFound();
        }
    }

    /// <summary>
    /// This book's reading copy, made now.
    ///
    /// The fallback is for books that predate this: composition reads a fulfilment manifest and
    /// refuses artwork drawn to a contract it no longer understands, and a book finished before
    /// that contract moved would otherwise lose a download it has always had. It is used only
    /// when a file is genuinely still in storage, and goes with the last of those files.
    /// </summary>
    private async Task<byte[]> ReadingPdfAsync(
        Domain.Entities.AdventurePack row,
        CancellationToken cancellationToken)
    {
        try
        {
            return row.IsBekiPipeline
                ? await bekiFulfillment.ComposeCustomerPdfAsync(row.Id, row.UserId, cancellationToken)
                : await LegacyReadingPdfAsync(row, cancellationToken);
        }
        catch (Exception ex) when (!string.IsNullOrWhiteSpace(row.PdfUrl))
        {
            logger.LogWarning(
                ex,
                "Pack {PackId} could not be composed on demand; serving the stored copy.",
                row.Id);
            return await blobStorageService.DownloadBytesFromStoredUrlAsync(
                row.PdfUrl, cancellationToken);
        }
    }

    /// <summary>
    /// A pre-composite book, set again from what it has: the story it was written as and the
    /// cover that was painted for it. The same call the old pipeline made before it uploaded the
    /// result; the upload is what is gone.
    /// </summary>
    private async Task<byte[]> LegacyReadingPdfAsync(
        Domain.Entities.AdventurePack row,
        CancellationToken cancellationToken)
    {
        var content = string.IsNullOrWhiteSpace(row.GeneratedJson)
            ? null
            : JsonSerializer.Deserialize<AdventureContentDto>(row.GeneratedJson, JsonOptions);
        if (content is null)
        {
            throw new InvalidOperationException("The book has no stored story to set.");
        }

        byte[]? cover = null;
        if (!string.IsNullOrWhiteSpace(row.CoverImageUrl))
        {
            try
            {
                cover = await blobStorageService.DownloadBytesFromStoredUrlAsync(
                    row.CoverImageUrl, cancellationToken);
            }
            catch (Exception ex)
            {
                /* A cover that will not load is a typeset cover, never a refused download. */
                logger.LogWarning(ex, "Cover art unavailable for pack {PackId}; typesetting one.", row.Id);
            }
        }

        return adventurePdfService.GeneratePdf(new PdfBookRequest
        {
            Content = content,
            ThemeName = row.Theme.ToString(),
            CoverImage = cover,
            Language = row.StoryLanguage ?? "ka",
            ContinueUrl = BekiOptions.WebsiteQrDestination,
            ForPrint = false,
        });
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
            /*
              The download lie, answered.

              This returned the English string "Pack is not ready." as a bare 400 body, and the
              reader rendered whatever came back — so a parent whose finished book was being held
              for a review read an untranslated sentence with no subject. There are two different
              things to say here and the book's own state decides which: a book still being made is
              a wait with pictures behind it, and a finished book whose file is held is a wait with
              a person behind it.
            */
            var held = row.Status == AdventurePackStatus.Completed
                ? await downloadStatus.DownloadHeldReasonAsync(row.UserId, id, cancellationToken)
                : null;

            return BadRequest(new
            {
                message = held switch
                {
                    BekiDownloadHeld.Review or BekiDownloadHeld.Gates =>
                        "წიგნი გადის ბოლო შემოწმებას - ჩამოტვირთვა მალე გაიხსნება.",
                    _ => "წიგნი ჯერ მზადდება - ცოტა ხანში სცადე ხელახლა.",
                },
                downloadHeld = held,
            });
        }

        try
        {
            /*
              Built for this request, and kept by nobody.

              This used to hand back a file made once and left in storage. Two things were wrong
              with that. A Beki book's stored PDF is the press artifact - the one the print
              stage normalizes and may upscale - so a parent downloading their child's book was
              being given the printer's copy; and the file itself sat in our storage for the
              life of the book, for something every one of its inputs can rebuild in a moment.

              Both ends are the same fix: compose the reading copy when it is asked for. Nothing
              is drawn and nothing is billed - the pictures were paid for when the book was made
              and are read back as they are - and no PDF of this book is written down anywhere.
            */
            var bytes = await ReadingPdfAsync(row, cancellationToken);

            // The book arrives under its own name. What stood here handed the browser the row's
            // primary key, so a parent saving a second book got a second beki-<guid>-book.pdf and
            // no way to tell which child's story was in which. The old spelling is kept as the
            // fallback for a book that somehow has no title.
            var fallbackName = $"beki-{row.Id}-book.pdf";
            Response.Headers.ContentDisposition = PdfFileNames.Attachment(
                PdfFileNames.ForBook(row.Title, suffix: null, fallbackName),
                PdfFileNames.AsciiForBook(row.Title, suffix: null, fallbackName)).ToString();

            // No download name on the result: ASP.NET would rebuild Content-Disposition from it
            // and blank every Georgian letter out of the ASCII parameter.
            return File(bytes, "application/pdf");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PDF blob missing for pack {PackId}. Stored url: {PdfUrl}", id, row.PdfUrl);

            // Georgian, and vague on purpose: the row says a file exists and storage disagrees,
            // which is our problem and not something to describe to a parent in English.
            return NotFound(new { message = "PDF ვერ მოიძებნა - ცოტა ხანში სცადე ხელახლა." });
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
        target.ProgressPercent = x.ProgressPercent;
        target.IsFailed = x.Status == AdventurePackStatus.Failed;

        // Mapped, never copied. The stored message is an English failure code written for whoever
        // is on duty; this response goes to the family's shelf. Mapped whenever there is one at
        // all rather than only on a Failed book, because a PDF export that fails puts the book
        // back to StoryReady and records why — the client waits on that message, and it is just
        // as English as the terminal ones.
        target.ErrorMessage = string.IsNullOrWhiteSpace(x.ErrorMessage)
            ? null
            : ParentFacingFailure.ToParentMessage(x.ErrorMessage);

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
        target.LastReadAt = x.LastReadAt;
        // Served, not linked. CoverImageUrl holds a storage path, and the container is private,
        // so handing it to a browser produces a 404 — Azure hides existence rather than refusing
        // — and the cover silently fails to appear. Exactly the fault already fixed on the teaser
        // cover; this is the same one on the book itself.
        target.CoverImageUrl = string.IsNullOrWhiteSpace(x.CoverImageUrl)
            ? null
            : $"/api/adventure-packs/{x.Id}/cover";
        target.Title = x.Title;

        // Amendment B5. A Beki book is finished at Completed and at no earlier status; a legacy one
        // is readable at StoryReady. Answering that here is what stops the shelf, the reader and the
        // generating screen each having their own opinion about it.
        target.GenerationPipeline = x.GenerationPipeline;
        target.GenerationPending = x.IsBekiPipeline && x.Status != AdventurePackStatus.Completed;
    }

    private static AdventurePackResponse Map(AdventurePack x)
    {
        var response = new AdventurePackResponse();
        MapBookFields(x, response);
        return response;
    }

    /// <summary>
    /// Why this book's download is not there, asked only of the books it could be true of.
    ///
    /// A Completed pack with no reading PDF is the withheld case and nothing else is, so the
    /// question — which reads a stored verdict out of blob storage — is asked for those rows and
    /// skipped for every other one. On a shelf of finished books that is zero extra reads; on the
    /// one card that cannot download, it is the only way to say why.
    /// </summary>
    private async Task<string?> DownloadHeldAsync(AdventurePack pack, CancellationToken cancellationToken)
    {
        if (pack.Status != AdventurePackStatus.Completed || !string.IsNullOrWhiteSpace(pack.PdfUrl))
        {
            return null;
        }

        try
        {
            return await downloadStatus.DownloadHeldReasonAsync(pack.UserId, pack.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A shelf that could not be drawn because a verdict could not be read would be a worse
            // fault than the missing explanation. The card falls back to its ordinary copy.
            logger.LogWarning(ex, "Download-held reason unavailable for pack {PackId}.", pack.Id);
            return null;
        }
    }

    private async Task<AdventurePackDetailResponse> MapDetailAsync(
        AdventurePack pack,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var detail = new AdventurePackDetailResponse();
        MapBookFields(pack, detail);
        detail.DownloadHeld = await DownloadHeldAsync(pack, cancellationToken);

        if (pack.Status is not (AdventurePackStatus.StoryReady or AdventurePackStatus.GeneratingPdf
            or AdventurePackStatus.Completed) || string.IsNullOrWhiteSpace(pack.GeneratedJson))
        {
            /*
              A book with nothing in it yet, described rather than left blank.

              This returned a detail with an empty page list, and the reader drew exactly that: a
              cover, a back cover, and nothing between them — which reads as a book that failed
              rather than one that has not happened yet. The pipeline's own progress line is
              already Georgian and already says where it is; what was missing was anything at all
              when the job has not written one, which is the whole first minute after an order.
            */
            detail.ProgressMessage = string.IsNullOrWhiteSpace(pack.ProgressMessage)
                ? "წიგნი ჯერ იხატება - მალე აქვე გამოჩნდება."
                : pack.ProgressMessage;

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

            // A spread book keeps its cover apart from its pages, so the page-one fallback below
            // does not apply to it.
            var isSpreadBook = content.StoryPages.Any(p => p.IsTextOnlyPage);

            detail.StoryPages = content.StoryPages
                .Select((page, index) =>
                {
                    var isLocked = index >= unlockedPages;
                    var isIllustrated = !isLocked && IsPageIllustrated(pack, page, index, isSpreadBook);
                    return new StoryPageContentDto
                    {
                        Title = page.Title,
                        Caption = page.Caption,
                        Content = page.Content,
                        IsLocked = isLocked,
                        IsIllustrated = isIllustrated,
                        IsTextOnlyPage = page.IsTextOnlyPage,
                        IllustrationUrl = isIllustrated
                            ? $"/api/adventure-packs/{pack.Id}/illustrations/{index}"
                            : null
                    };
                })
                .ToList();

            detail.LockedPageCount = content.StoryPages.Count - unlockedPages;
            detail.IsSpreadBook = isSpreadBook;
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

    private static bool IsPageIllustrated(AdventurePack pack, StoryPageDto page, int pageIndex, bool isSpreadBook)
    {
        if (!string.IsNullOrWhiteSpace(page.IllustrationUrl))
        {
            return true;
        }

        // In a spread book PreviewIllustrationUrl holds the cover, which is not any page's
        // picture. Treating it as page one's would show the cover twice and mark a page
        // illustrated before it had been drawn.
        if (isSpreadBook)
        {
            return false;
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
