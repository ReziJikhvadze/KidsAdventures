using AdventurePacks.Api.DTOs.Admin;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Controllers;

/// <summary>
/// The release policy, and everything it has waived.
///
/// Its own controller rather than four more actions on the orders one, because these routes are not
/// about an order. They are about the rule every order is judged by — which check stops a book and
/// which merely leaves a note — and the notes themselves. The orders console answers "what happened
/// to this family's book"; this answers "what are we willing to ship, and what did we ship anyway".
///
/// Behind the same Admin policy as everything else here: a route that can turn off human review is
/// a route that can send an unreviewed book to a child.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("api/admin")]
public sealed class AdminReleaseController(
    IBekiReleasePolicyService policyService,
    IBekiAlarmService alarms,
    IUserContextService userContext,
    ILogger<AdminReleaseController> logger) : ControllerBase
{
    /// <summary>
    /// Every check, with the severity in force — the stored decisions and the shipped defaults in
    /// one list.
    ///
    /// The defaults are the policy's own, not a second copy kept here: a settings screen whose
    /// board disagreed with the service's would show an operator a rule that is not the rule.
    /// Anything stored under a key the defaults do not know about is appended rather than dropped,
    /// so a row somebody inserted by hand is visible in the place people go to change it.
    /// </summary>
    [HttpGet("release-policy")]
    public async Task<ActionResult<AdminReleasePolicyResponse>> ReleasePolicy(
        CancellationToken cancellationToken)
    {
        var stored = await policyService.ListAsync(cancellationToken);
        var snapshot = await policyService.SnapshotAsync(cancellationToken);

        var storedByKey = stored.ToDictionary(
            setting => Key(setting.CheckId, setting.DeliverableClass),
            setting => setting);

        var rows = new List<AdminReleaseCheckSetting>();

        foreach (var shipped in BekiReleasePolicySnapshot.Defaults.Settings)
        {
            var key = Key(shipped.CheckId, shipped.DeliverableClass);
            storedByKey.TryGetValue(key, out var stamped);
            storedByKey.Remove(key);

            rows.Add(new AdminReleaseCheckSetting(
                shipped.CheckId,
                shipped.DeliverableClass,
                stamped?.Severity ?? shipped.Severity,
                stamped is null,
                stamped?.UpdatedBy,
                stamped?.UpdatedAtUtc));
        }

        rows.AddRange(storedByKey.Values.Select(extra => new AdminReleaseCheckSetting(
            extra.CheckId, extra.DeliverableClass, extra.Severity, false,
            extra.UpdatedBy, extra.UpdatedAtUtc)));

        return Ok(new AdminReleasePolicyResponse(snapshot.HumanReviewRequired, rows));
    }

    /// <summary>
    /// Changes one check — and acts on the change, which is amendment B7 and the whole reason this
    /// is a PUT with a body rather than a toggle that saves a row.
    ///
    /// Loosening a check is a promise to the families whose finished books are sitting withheld
    /// under the old rule. The reconciliation that keeps that promise runs inside the setting call
    /// and returns its own count, so the response can say how many books actually came out — an
    /// operator who flips a switch is entitled to know whether it reached anybody. It is bounded and
    /// compare-and-set throughout, so the worst case is a slow console request.
    ///
    /// ONE scan. This action used to await a reconciliation of its own on top of the one the setting
    /// already started in the background, and the two raced over the same books: nothing published
    /// twice, because every write is compare-and-set, but the number reported here was only the
    /// share this copy of the scan happened to win. (Review finding 3.)
    ///
    /// Tightening one never revokes a file that has already been published. That is stated in the
    /// UI copy beside this control, and it is true because publication only ever adds a URL.
    /// </summary>
    [HttpPut("release-policy")]
    public async Task<ActionResult<AdminReleasePolicyUpdateResponse>> SetReleasePolicy(
        [FromBody] AdminSetReleasePolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CheckId))
        {
            return BadRequest(new { message = "შემოწმება მითითებული არ არის." });
        }

        var severity = Severity(request.Severity);
        if (severity is null)
        {
            return BadRequest(new { message = "სიმძიმე უნდა იყოს blocker ან flag." });
        }

        var deliverableClass = string.IsNullOrWhiteSpace(request.DeliverableClass)
            ? BekiReleaseSeverity.AllClasses
            : request.DeliverableClass.Trim().ToLowerInvariant();

        // Checked here rather than left to the store's CHECK constraint. A class the table refuses
        // would surface as a 500 on a settings click, which tells an operator the console is broken
        // when what happened is that they asked for a deliverable that does not exist.
        if (!DeliverableClasses.Contains(deliverableClass))
        {
            return BadRequest(new { message = "ასეთი ფაილის კლასი არ არსებობს." });
        }

        var checkId = request.CheckId.Trim();
        var updatedBy = OperatorName();

        var published = await policyService.SetAsync(
            checkId, deliverableClass, severity, updatedBy, cancellationToken);

        var snapshot = await policyService.SnapshotAsync(cancellationToken);

        logger.LogInformation(
            "Beki release policy: {Operator} set {CheckId} ({Class}) to {Severity}; {Published} "
            + "withheld book(s) were published as a result.",
            updatedBy, checkId, deliverableClass, severity, published);

        return Ok(new AdminReleasePolicyUpdateResponse(
            new AdminReleaseCheckSetting(
                checkId,
                deliverableClass,
                severity,
                false,
                updatedBy,
                DateTimeOffset.UtcNow),
            published,
            snapshot.HumanReviewRequired));
    }

    /// <summary>
    /// The waivers nobody has looked at yet.
    ///
    /// <c>open=false</c> is accepted and means "the recent ones, reviewed or not" — the list is
    /// still capped, because an alarms table that has been running for a year is not a page.
    /// The count is taken separately from the page so the header badge says how many exist rather
    /// than how many fitted.
    /// </summary>
    [HttpGet("alarms")]
    public async Task<ActionResult<AdminAlarmListResponse>> Alarms(
        [FromQuery] bool open = true,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);

        var openCount = await alarms.CountOpenAsync(cancellationToken);

        // Only the open list exists on the service, which is the list this console is for. A
        // request for everything gets the open ones rather than a lie about having searched.
        var items = await alarms.ListOpenAsync(limit, cancellationToken);

        return Ok(new AdminAlarmListResponse(openCount, items.Select(ToRow).ToList()));
    }

    /// <summary>
    /// Every alarm against one book, reviewed ones included — the history behind a chip on the
    /// orders list, for somebody who has opened that order and wants to know whether this has
    /// happened before.
    /// </summary>
    [HttpGet("alarms/pack/{packId:guid}")]
    public async Task<ActionResult<AdminAlarmListResponse>> AlarmsForPack(
        Guid packId, CancellationToken cancellationToken)
    {
        var items = await alarms.ListForPackAsync(packId, cancellationToken);

        return Ok(new AdminAlarmListResponse(
            items.Count(alarm => alarm.ReviewedAtUtc is null),
            items.Select(ToRow).ToList()));
    }

    /// <summary>
    /// Closes one alarm, in the reviewer's name.
    ///
    /// WHO comes from the authenticated admin rather than from the body, for the reason the visual
    /// approval does it: a resolution nobody can be asked about is not a resolution. The word
    /// itself is normalised by the service to one the store accepts, so a console that sends
    /// something unexpected records "somebody looked at it" instead of failing.
    /// </summary>
    [HttpPost("alarms/{id:guid}/review")]
    public async Task<IActionResult> ReviewAlarm(
        Guid id,
        [FromBody] AdminReviewAlarmRequest request,
        CancellationToken cancellationToken)
    {
        var reviewed = await alarms.ReviewAsync(
            id, OperatorName(), request.Resolution ?? string.Empty, cancellationToken);

        return reviewed
            ? Ok(new { id, reviewedBy = OperatorName() })
            : NotFound(new { message = "ასეთი შეტყობინება არ არსებობს." });
    }

    /// <summary>
    /// The signed-in operator, by address where there is one. The same identity the visual approval
    /// records, so one person's decisions look like one person's across both screens.
    /// </summary>
    private string OperatorName() =>
        userContext.GetEmail() is { Length: > 0 } email ? email : userContext.GetUserId().ToString();

    /// <summary>The five the store's CHECK constraint accepts, and no others.</summary>
    private static readonly IReadOnlySet<string> DeliverableClasses =
        new HashSet<string>(StringComparer.Ordinal)
        {
            BekiReleaseSeverity.AllClasses,
            BekiReleaseGates.SharedClass,
            BekiReleaseGates.PressClass,
            BekiReleaseGates.DigitalClass,
            BekiReleaseGates.PackageClass,
        };

    private static string Key(string checkId, string deliverableClass) =>
        $"{checkId.Trim().ToLowerInvariant()}|{deliverableClass.Trim().ToLowerInvariant()}";

    private static string? Severity(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            BekiReleaseSeverity.Blocker => BekiReleaseSeverity.Blocker,
            BekiReleaseSeverity.Flag => BekiReleaseSeverity.Flag,
            _ => null,
        };

    private static AdminAlarmRow ToRow(BekiAlarm alarm) => new(
        alarm.Id,
        alarm.PackId,
        alarm.OrderId,
        alarm.UserId,
        alarm.CheckId,
        alarm.Severity,
        alarm.Detail,
        alarm.EvidenceBlob,
        alarm.CreatedAtUtc,
        alarm.LastSeenUtc,
        alarm.ReviewedBy,
        alarm.ReviewedAtUtc,
        alarm.Resolution);
}
