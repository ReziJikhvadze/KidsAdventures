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
    IBlobStorageService blobStorage,
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
    /// The waivers nobody has looked at yet — and, on request, the ones somebody already did.
    ///
    /// <c>open=false</c> means "the recent ones, reviewed or not", and it is a real second query
    /// rather than the old placeholder that returned the open list and hoped nobody checked. The
    /// closed rows are the point of the toggle: an incident somebody resolved last week is exactly
    /// what makes this week's identical one worth escalating rather than waving through again.
    ///
    /// Both lists are capped, because an alarms table that has been running for a year is not a
    /// page. The count is taken separately from the page so the header badge says how many exist
    /// rather than how many fitted — and it stays the OPEN count in both modes, because the badge
    /// means "how much work is waiting", which showing the closed ones does not change.
    /// </summary>
    [HttpGet("alarms")]
    public async Task<ActionResult<AdminAlarmListResponse>> Alarms(
        [FromQuery] bool open = true,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);

        var openCount = await alarms.CountOpenAsync(cancellationToken);

        var items = open
            ? await alarms.ListOpenAsync(limit, cancellationToken)
            : await alarms.ListRecentAsync(limit, cancellationToken);

        return Ok(new AdminAlarmListResponse(openCount, items.Select(ToRow).ToList()));
    }

    /// <summary>
    /// The file an alarm was raised about, streamed through the API.
    ///
    /// It used to take the whole handback zip to look at one refused spread, which meant that in
    /// practice nobody looked: reviewing an alarm is a ten-second decision and downloading a
    /// hundred megabytes to make it is not. The row already carries the storage key; this hands
    /// over the bytes behind it and nothing else — the key stays a key, never a link, because a
    /// URL that outlives this request is a URL that leaks a child's book.
    ///
    /// Three 404s, all of them honest: no such alarm, an alarm with no artifact behind it (a
    /// timing or bookkeeping incident has nothing to show), and a key whose blob has since gone.
    /// </summary>
    [HttpGet("alarms/{id:guid}/evidence")]
    public async Task<IActionResult> AlarmEvidence(Guid id, CancellationToken cancellationToken)
    {
        var alarm = await alarms.GetAsync(id, cancellationToken);
        if (alarm is null)
        {
            return NotFound(new { message = "ასეთი შეტყობინება არ არსებობს." });
        }

        if (string.IsNullOrWhiteSpace(alarm.EvidenceBlob))
        {
            return NotFound(new { message = "ამ შეტყობინებას მტკიცებულება არ ახლავს." });
        }

        try
        {
            var stream = await blobStorage.DownloadAsync(alarm.EvidenceBlob, cancellationToken);

            // Inline rather than as an attachment: the console shows a PNG in the row, and a
            // download prompt in the middle of a review is the friction this route removes.
            return File(stream, EvidenceContentType(alarm.EvidenceBlob), FileName(alarm.EvidenceBlob));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex, "Alarm {AlarmId} evidence {Blob} could not be read from storage.",
                id, alarm.EvidenceBlob);

            return NotFound(new { message = "მტკიცებულების ფაილი საცავში ვერ მოიძებნა." });
        }
    }

    /// <summary>
    /// What kind of file the evidence is, from its name.
    ///
    /// By extension rather than by a stored content type, because nothing stores one: the evidence
    /// key is a blob name written by whichever stage raised the alarm. The four that actually
    /// occur are the refused artwork, the QA document behind it, and the odd PDF; anything else is
    /// handed over as bytes rather than mislabelled as something a browser will try to render.
    /// </summary>
    private static string EvidenceContentType(string blobName) =>
        Path.GetExtension(blobName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".json" => "application/json",
            ".pdf" => "application/pdf",
            ".txt" or ".log" => "text/plain",
            _ => "application/octet-stream",
        };

    /// <summary>The last segment of a storage key, which is the only part that reads as a file.</summary>
    private static string FileName(string blobName)
    {
        var name = blobName.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrWhiteSpace(name) ? "evidence" : name;
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
