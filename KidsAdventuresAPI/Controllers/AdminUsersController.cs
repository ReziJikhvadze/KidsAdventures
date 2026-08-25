using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Controllers;

/// <summary>
/// Parent accounts, and which of them hold the operations role.
///
/// Granting is done from the same list as everything else about a customer, because the person
/// being promoted is a customer — there is no separate staff directory to keep in step.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin")]
public sealed class AdminUsersController(
    IAdminReportingRepository reporting,
    IUserRepository userRepository,
    IUserContextService userContext,
    IOptions<AdminOptions> adminOptions,
    ILogger<AdminUsersController> logger) : ControllerBase
{
    private readonly AdminOptions _adminOptions = adminOptions.Value;

    /// <summary>Parent accounts with their spend, book counts and role.</summary>
    [HttpGet("customers")]
    public async Task<ActionResult<AdminCustomerListResponse>> Customers(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(await reporting.GetCustomersAsync(search, page, pageSize, cancellationToken));
    }

    /// <summary>
    /// Grants or removes the operations role.
    ///
    /// Three refusals, each of them a way this console could be locked shut: demoting yourself
    /// (the classic one — you are holding the only session that could undo it), demoting a
    /// configured super-admin (the setting exists precisely so that cannot happen), and removing
    /// the last admin of all. None of these is a permission question, so none of them is a 403;
    /// they are all "that would break something", which is a 409.
    /// </summary>
    [HttpPut("users/{id:guid}/admin")]
    public async Task<IActionResult> SetAdmin(
        Guid id,
        [FromBody] UpdateUserAdminRequest request,
        CancellationToken cancellationToken)
    {
        var target = await userRepository.GetByIdAsync(id, cancellationToken);
        if (target is null)
        {
            return NotFound();
        }

        var isSuperAdmin = _adminOptions.IsSuperAdmin(target.Email);

        if (!request.IsAdmin)
        {
            if (id == userContext.GetUserId())
            {
                return Conflict(new { message = "საკუთარ თავს ადმინის უფლებას ვერ მოხსნი." });
            }

            if (isSuperAdmin)
            {
                return Conflict(new { message = "ეს ანგარიში კონფიგურაციაშია მითითებული და ვერ ჩამოირთმევა." });
            }
        }

        // The last-admin rule lives in the UPDATE itself, so two operators demoting two different
        // admins at once cannot both pass it. False means the write declined.
        if (!await userRepository.SetAdminAsync(id, request.IsAdmin, cancellationToken))
        {
            return Conflict(new { message = "ბოლო ადმინს უფლებას ვერ მოხსნი." });
        }

        // The only record there is of who changed what. A table would be better once more than
        // one person uses this screen; until then a log line is the honest amount of machinery.
        logger.LogInformation(
            "Admin {Actor} {Action} the operations role for {Target} ({Email}).",
            userContext.GetUserId(),
            request.IsAdmin ? "granted" : "removed",
            id,
            target.Email ?? target.PhoneNumber ?? "—");

        return Ok(new
        {
            isAdmin = request.IsAdmin || isSuperAdmin,
            // The role is stamped into the token at issue time, so nothing changes for a session
            // that is already open. Said here rather than only in the UI copy, because any other
            // client would otherwise have to rediscover it.
            note = "ცვლილება ძალაში შედის მას შემდეგ, რაც მომხმარებელი ხელახლა შევა სისტემაში."
        });
    }
}
