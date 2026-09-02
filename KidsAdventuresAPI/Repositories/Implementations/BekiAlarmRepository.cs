using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Repositories.Implementations;

/// <summary>
/// The <c>BekiAlarms</c> table (migration 035).
///
/// One statement does the whole of the raise, and it is written as UPDATE-then-INSERT rather than as
/// a MERGE. Two reasons, and the first is the ordinary one: MERGE against a unique index under
/// concurrency is a well-known source of duplicate-key races, and this table is written from
/// fulfilment jobs that can run the same evaluation twice. The second is that the update has to be
/// able to say what it did — touched an open alarm or reopened a reviewed one — and reading
/// <c>Resolution</c> back through an OUTPUT clause is how that is done in one round trip.
/// </summary>
public sealed class BekiAlarmRepository(ISqlConnectionFactory connectionFactory) : IBekiAlarmRepository
{
    private const string AlarmColumns = """
        Id, PackId, OrderId, UserId, CheckId, Severity, Detail, EvidenceBlob,
        CreatedAtUtc, LastSeenUtc, ReviewedBy, ReviewedAtUtc, Resolution
        """;

    public async Task<BekiAlarmRaiseOutcome> RaiseAsync(
        BekiAlarmRaise raise, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(raise);

        /*
          The OUTPUT clause reads the row as it was BEFORE the update — `deleted` — which is the only
          way to know whether this raising reopened something. After the write the row is open either
          way, so asking afterwards would always answer "it was open".

          Detail and severity are refreshed rather than kept: the newest sighting is the one worth
          reading, and a two-month-old wording of the same fault helps nobody.

          OrderId is COALESCEd the other way round — a known id fills an empty column and nothing
          ever clears one. Alarms raised before the fulfilment job looked the order up carry a null
          there, and the console's order link and evidence button both key off it; a later sighting
          of the same incident is the cheapest chance this system gets to repair those rows, and
          overwriting a good id with a null would be the same fault arriving from the other side.
          (Review finding 4.)
        */
        const string sql = """
                           DECLARE @previous TABLE (ReviewedAtUtc DATETIME2(3) NULL);

                           UPDATE dbo.BekiAlarms
                           SET LastSeenUtc = SYSUTCDATETIME(),
                               Severity = @Severity,
                               Detail = @Detail,
                               EvidenceBlob = COALESCE(@EvidenceBlob, EvidenceBlob),
                               OrderId = COALESCE(OrderId, @OrderId),
                               ReviewedAtUtc = NULL,
                               Resolution = NULL
                           OUTPUT deleted.ReviewedAtUtc INTO @previous
                           WHERE PackId = @PackId
                             AND CheckId = @CheckId
                             AND EvidenceKey = @EvidenceKey;

                           IF @@ROWCOUNT = 0
                           BEGIN
                               INSERT INTO dbo.BekiAlarms
                                   (Id, PackId, OrderId, UserId, CheckId, Severity, Detail,
                                    EvidenceBlob, EvidenceKey, CreatedAtUtc, LastSeenUtc)
                               VALUES
                                   (@Id, @PackId, @OrderId, @UserId, @CheckId, @Severity, @Detail,
                                    @EvidenceBlob, @EvidenceKey, SYSUTCDATETIME(), SYSUTCDATETIME());

                               SELECT 0;
                           END
                           ELSE
                           BEGIN
                               SELECT CASE
                                          WHEN EXISTS (SELECT 1 FROM @previous WHERE ReviewedAtUtc IS NOT NULL)
                                          THEN 2
                                          ELSE 1
                                      END;
                           END;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var outcome = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new
            {
                Id = Guid.NewGuid(),
                raise.PackId,
                raise.OrderId,
                raise.UserId,
                raise.CheckId,
                raise.Severity,
                Detail = raise.Detail,
                raise.EvidenceBlob,
                raise.EvidenceKey,
            },
            cancellationToken: cancellationToken));

        return outcome switch
        {
            2 => BekiAlarmRaiseOutcome.Reopened,
            1 => BekiAlarmRaiseOutcome.Touched,
            _ => BekiAlarmRaiseOutcome.Inserted,
        };
    }

    public async Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(
        int limit, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP (@Limit) {AlarmColumns}
                   FROM dbo.BekiAlarms
                   WHERE ReviewedAtUtc IS NULL
                   ORDER BY LastSeenUtc DESC;
                   """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AlarmRow>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken));

        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// The same page, without the <c>ReviewedAtUtc IS NULL</c> predicate.
    ///
    /// Ordered by LastSeenUtc rather than by CreatedAtUtc, exactly as the open list is, so that a
    /// closed incident which has just recurred sorts where an operator expects to find it: at the
    /// top, next to the open alarms it happened alongside.
    /// </summary>
    public async Task<IReadOnlyList<BekiAlarm>> ListRecentAsync(
        int limit, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP (@Limit) {AlarmColumns}
                   FROM dbo.BekiAlarms
                   ORDER BY LastSeenUtc DESC;
                   """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AlarmRow>(
            new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken));

        return rows.Select(Map).ToList();
    }

    public async Task<BekiAlarm?> GetAsync(Guid alarmId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP 1 {AlarmColumns}
                   FROM dbo.BekiAlarms
                   WHERE Id = @Id;
                   """;

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<AlarmRow>(
            new CommandDefinition(sql, new { Id = alarmId }, cancellationToken: cancellationToken));

        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(
        Guid packId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT {AlarmColumns}
                   FROM dbo.BekiAlarms
                   WHERE PackId = @PackId
                   ORDER BY LastSeenUtc DESC;
                   """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AlarmRow>(
            new CommandDefinition(sql, new { PackId = packId }, cancellationToken: cancellationToken));

        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// Reviews an alarm that is open. An alarm somebody already closed is left as it was — the first
    /// resolution is the one with a person's reasoning behind it, and a second click should not
    /// overwrite it with a default.
    /// </summary>
    public async Task<bool> ReviewAsync(
        Guid alarmId, string reviewedBy, string resolution, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.BekiAlarms
                           SET ReviewedBy = @ReviewedBy,
                               ReviewedAtUtc = SYSUTCDATETIME(),
                               Resolution = @Resolution
                           WHERE Id = @Id AND ReviewedAtUtc IS NULL;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = alarmId, ReviewedBy = reviewedBy, Resolution = resolution },
            cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<int> CountOpenAsync(CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT COUNT(1) FROM dbo.BekiAlarms WHERE ReviewedAtUtc IS NULL;
                           """;

        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    private static BekiAlarm Map(AlarmRow row) => new(
        row.Id,
        row.PackId,
        row.OrderId,
        row.UserId,
        row.CheckId,
        row.Severity,
        row.Detail,
        row.EvidenceBlob,
        Utc(row.CreatedAtUtc),
        Utc(row.LastSeenUtc),
        row.ReviewedBy,
        row.ReviewedAtUtc is { } reviewed ? Utc(reviewed) : null,
        row.Resolution);

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class AlarmRow
    {
        public Guid Id { get; set; }
        public Guid PackId { get; set; }
        public Guid? OrderId { get; set; }
        public Guid UserId { get; set; }
        public string CheckId { get; set; } = string.Empty;
        public string Severity { get; set; } = BekiReleaseSeverity.Flag;
        public string Detail { get; set; } = string.Empty;
        public string? EvidenceBlob { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
        public string? Resolution { get; set; }
    }
}
