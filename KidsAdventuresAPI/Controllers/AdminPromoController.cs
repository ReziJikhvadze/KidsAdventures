using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

/// <summary>
/// Discount codes, which until now could only be made in SQL.
///
/// That is the entire reason this exists. A campaign code was a hand-written INSERT against
/// production, which means it was made by whoever had the connection string and was checked by
/// nobody — and switching one off in a hurry was the same operation again, at the worst possible
/// moment. Three routes replace that: see them, make one, switch one off.
///
/// Nothing here deletes, and nothing here edits a discount. A code that has been redeemed is part
/// of the price of an order that already happened; the way to change a price is a new code, and
/// the way to stop an old one is <c>isActive</c>.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin")]
public sealed class AdminPromoController(
    IPromoCodeRepository promoCodes,
    IUserContextService userContext,
    ILogger<AdminPromoController> logger) : ControllerBase
{
    /// <summary>What the table's Code column holds.</summary>
    private const int MaxCodeLength = 64;

    [HttpGet("promo-codes")]
    public async Task<ActionResult<IReadOnlyList<AdminPromoCodeRow>>> PromoCodes(
        CancellationToken cancellationToken)
    {
        var rows = await promoCodes.ListAllAsync(cancellationToken);
        return Ok(rows.Select(ToRow).ToList());
    }

    /// <summary>
    /// Creates a code.
    ///
    /// The two refusals are both the table's own rules, checked here so they arrive as something an
    /// operator can act on. A percentage outside 1–100, or a percentage alongside "free", would
    /// break <c>CK_PromoCodes_Discount</c> and surface as a 500 on a form submission — which tells
    /// somebody the console is broken when what happened is that they asked for a discount that
    /// cannot exist. A name that already exists is a 409, not an overwrite: the existing code may
    /// have been handed to a thousand people.
    /// </summary>
    [HttpPost("promo-codes")]
    public async Task<ActionResult<AdminPromoCodeRow>> CreatePromoCode(
        [FromBody] AdminCreatePromoCodeRequest request,
        CancellationToken cancellationToken)
    {
        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();

        if (code.Length == 0)
        {
            return BadRequest(new { message = "კოდი მითითებული არ არის." });
        }

        if (code.Length > MaxCodeLength)
        {
            return BadRequest(new { message = $"კოდი {MaxCodeLength} სიმბოლოზე გრძელი ვერ იქნება." });
        }

        // A code is typed by a parent into a checkout box on a phone. A space or a Georgian letter
        // in it is a code that will be mistyped every time, so it is refused at the moment it is
        // invented rather than discovered by the first customer who cannot use it.
        if (!code.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            return BadRequest(new
            {
                message = "კოდში დაშვებულია მხოლოდ ლათინური ასოები, ციფრები, დეფისი და ქვედა ტირე."
            });
        }

        if (request.IsFullDiscount && request.DiscountPercent is not null)
        {
            return BadRequest(new
            {
                message = "კოდი ან პროცენტულია, ან სრულიად უფასო — ორივე ერთად არ შეიძლება."
            });
        }

        if (!request.IsFullDiscount && request.DiscountPercent is not (>= 1 and <= 100))
        {
            return BadRequest(new { message = "ფასდაკლება უნდა იყოს 1-დან 100 პროცენტამდე." });
        }

        if (request.MaxRedemptions is <= 0)
        {
            return BadRequest(new { message = "მაქსიმალური გამოყენება ერთზე ნაკლები ვერ იქნება." });
        }

        if (request.ValidFromUtc is { } from && request.ValidUntilUtc is { } until && until <= from)
        {
            return BadRequest(new { message = "ვადის დასასრული დაწყებაზე ადრე ვერ იქნება." });
        }

        // Checked before the insert so the ordinary case gets the readable refusal; the insert
        // catches the unique index too, for two operators typing the same name at once.
        if (await promoCodes.GetByCodeAsync(code, cancellationToken) is not null)
        {
            return Conflict(new { message = $"კოდი {code} უკვე არსებობს." });
        }

        var promo = new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = code,
            Description = null,
            PercentOff = request.IsFullDiscount ? null : request.DiscountPercent,
            IsFullDiscount = request.IsFullDiscount,
            MaxRedemptions = request.MaxRedemptions,
            RedemptionCount = 0,
            OncePerUser = request.OncePerUser,
            StartsAt = Utc(request.ValidFromUtc),
            ExpiresAt = Utc(request.ValidUntilUtc),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        if (!await promoCodes.CreateAsync(promo, cancellationToken))
        {
            return Conflict(new { message = $"კოდი {code} უკვე არსებობს." });
        }

        logger.LogInformation(
            "Admin {Operator} created promo code {Code} ({Discount}).",
            OperatorName(),
            code,
            promo.IsFullDiscount ? "free" : $"{promo.PercentOff}%");

        var row = ToRow(promo);
        return Created($"/api/admin/promo-codes/{promo.Id}", row);
    }

    /// <summary>
    /// Switches a code off (or back on), and adjusts its cap and its expiry.
    ///
    /// A patch, not a replacement: a body that mentions only <c>isActive</c> leaves the window
    /// alone, and a body that says <c>"validUntilUtc": null</c> makes the code open-ended. The
    /// difference is the request DTO's business; what matters here is that switching a code off in
    /// a hurry cannot accidentally clear its expiry as a side effect.
    /// </summary>
    [HttpPut("promo-codes/{id:guid}")]
    public async Task<ActionResult<AdminPromoCodeRow>> UpdatePromoCode(
        Guid id,
        [FromBody] AdminUpdatePromoCodeRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await promoCodes.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound(new { message = "ასეთი კოდი არ არსებობს." });
        }

        var isActive = request.IsActiveSpecified ? request.IsActive ?? existing.IsActive : existing.IsActive;
        var maxRedemptions = request.MaxRedemptionsSpecified ? request.MaxRedemptions : existing.MaxRedemptions;
        var expiresAt = request.ValidUntilUtcSpecified ? Utc(request.ValidUntilUtc) : existing.ExpiresAt;

        if (maxRedemptions is <= 0)
        {
            return BadRequest(new { message = "მაქსიმალური გამოყენება ერთზე ნაკლები ვერ იქნება." });
        }

        if (existing.StartsAt is { } from && expiresAt is { } until && until <= from)
        {
            return BadRequest(new { message = "ვადის დასასრული დაწყებაზე ადრე ვერ იქნება." });
        }

        if (!await promoCodes.UpdateAdminFieldsAsync(
                id, isActive, maxRedemptions, expiresAt, cancellationToken))
        {
            return NotFound(new { message = "ასეთი კოდი არ არსებობს." });
        }

        logger.LogInformation(
            "Admin {Operator} updated promo code {Code}: active={Active}, cap={Cap}, until={Until}.",
            OperatorName(), existing.Code, isActive, maxRedemptions, expiresAt);

        existing.IsActive = isActive;
        existing.MaxRedemptions = maxRedemptions;
        existing.ExpiresAt = expiresAt;

        return Ok(ToRow(existing));
    }

    private string OperatorName() =>
        userContext.GetEmail() is { Length: > 0 } email ? email : userContext.GetUserId().ToString();

    /// <summary>
    /// A stored DATETIME2 as an instant. The column has no offset in it, and every writer in this
    /// system writes UTC; saying so explicitly is what stops the browser shifting the date by its
    /// own offset when it renders "valid until".
    /// </summary>
    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTime? Utc(DateTimeOffset? value) => value?.UtcDateTime;

    private static AdminPromoCodeRow ToRow(PromoCode promo) => new(
        promo.Id,
        promo.Code,
        promo.PercentOff,
        promo.IsFullDiscount,
        promo.MaxRedemptions,
        promo.RedemptionCount,
        promo.OncePerUser,
        promo.StartsAt is { } starts ? Utc(starts) : null,
        promo.ExpiresAt is { } expires ? Utc(expires) : null,
        promo.IsActive,
        Utc(promo.CreatedAt));
}
