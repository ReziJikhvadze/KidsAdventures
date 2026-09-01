using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Repositories.Implementations;

/// <summary>
/// The <c>BekiReleaseChecks</c> table (migration 035), read whole and written one row at a time.
///
/// Reading it whole is not laziness: there are two dozen rows and every consumer needs all of them
/// at once — a snapshot is by definition the entire policy, and a per-check query would be a round
/// trip inside a loop that is already holding the answer.
/// </summary>
public sealed class BekiReleasePolicyRepository(ISqlConnectionFactory connectionFactory)
    : IBekiReleasePolicyRepository
{
    public async Task<IReadOnlyList<BekiReleaseCheckSetting>> ListAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT CheckId, DeliverableClass, Severity, UpdatedBy, UpdatedAtUtc
                           FROM dbo.BekiReleaseChecks
                           ORDER BY CheckId, DeliverableClass;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PolicyRow>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        return rows
            .Select(row => new BekiReleaseCheckSetting(
                row.CheckId,
                row.DeliverableClass,
                row.Severity,
                row.UpdatedBy,
                row.UpdatedAtUtc is { } updated
                    ? new DateTimeOffset(DateTime.SpecifyKind(updated, DateTimeKind.Utc))
                    : null))
            .ToList();
    }

    /// <summary>
    /// Update-then-insert rather than MERGE.
    ///
    /// MERGE on a two-column key is the shape that reads best and the shape that has the concurrency
    /// bugs; this is the idiom the rest of this repository layer uses, and under the primary key a
    /// racing insert loses with a duplicate-key error the caller can retry rather than silently
    /// writing a second row.
    /// </summary>
    public async Task SetAsync(
        string checkId,
        string deliverableClass,
        string severity,
        string updatedBy,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.BekiReleaseChecks
                           SET Severity = @Severity,
                               UpdatedBy = @UpdatedBy,
                               UpdatedAtUtc = SYSUTCDATETIME()
                           WHERE CheckId = @CheckId AND DeliverableClass = @DeliverableClass;

                           IF @@ROWCOUNT = 0
                           BEGIN
                               INSERT INTO dbo.BekiReleaseChecks
                                   (CheckId, DeliverableClass, Severity, UpdatedBy, UpdatedAtUtc)
                               VALUES
                                   (@CheckId, @DeliverableClass, @Severity, @UpdatedBy, SYSUTCDATETIME());
                           END;
                           """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                CheckId = checkId,
                DeliverableClass = deliverableClass,
                Severity = severity,
                UpdatedBy = updatedBy,
            },
            cancellationToken: cancellationToken));
    }

    private sealed class PolicyRow
    {
        public string CheckId { get; set; } = string.Empty;
        public string DeliverableClass { get; set; } = BekiReleaseSeverity.AllClasses;
        public string Severity { get; set; } = BekiReleaseSeverity.Flag;
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
    }
}
