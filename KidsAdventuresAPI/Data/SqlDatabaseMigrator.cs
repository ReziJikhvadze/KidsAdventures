using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace AdventurePacks.Api.Data;

public interface ISqlDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies the numbered SQL scripts, and remembers which it has already applied.
///
/// Every script used to run on every start. They are written to be idempotent, so it worked —
/// but App Service restarts on each deploy, each scale event and each idle recycle, so the whole
/// schema was being re-executed several times a day, and the cost of a start grew with every
/// migration ever written. It also meant a script that turned out not to be quite idempotent
/// would do its damage repeatedly rather than once.
///
/// A script is identified by its name and the hash of its contents. An edited script runs again:
/// these are meant to be idempotent, so re-running the intended change is safer than skipping it
/// and leaving the database a version behind the code that expects it.
/// </summary>
public sealed class SqlDatabaseMigrator(IConfiguration configuration, ILogger<SqlDatabaseMigrator> logger) : ISqlDatabaseMigrator
{
    /// <summary>
    /// Generous, and per batch. Adding an index to a table that has grown takes longer than the
    /// thirty-second default, and a migration killed halfway is how a schema ends up in a state
    /// nobody wrote down.
    /// </summary>
    private const int BatchTimeoutSeconds = 300;

    private const string LedgerDdl = """
        IF OBJECT_ID(N'dbo.__SchemaMigrations', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.__SchemaMigrations
            (
                ScriptName NVARCHAR(255) NOT NULL CONSTRAINT PK___SchemaMigrations PRIMARY KEY,
                Checksum   CHAR(64)      NOT NULL,
                AppliedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF___SchemaMigrations_AppliedAt DEFAULT SYSUTCDATETIME()
            );
        END;
        """;

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

        var scriptsDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Scripts");
        if (!Directory.Exists(scriptsDirectory))
        {
            logger.LogWarning("SQL scripts directory not found at {Path}", scriptsDirectory);
            return;
        }

        var scriptFiles = Directory.GetFiles(scriptsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (scriptFiles.Length == 0)
        {
            logger.LogWarning("No SQL migration scripts found.");
            return;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Users, Orders and Characters carry filtered unique indexes, and SQL Server
        // refuses any DML against such a table unless both options are ON. SqlClient
        // already defaults to ON; setting it here makes the requirement explicit so
        // the chain does not depend on a driver default.
        await ExecuteAsync(connection, "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", cancellationToken);
        await ExecuteAsync(connection, LedgerDdl, cancellationToken);

        var applied = await LoadLedgerAsync(connection, cancellationToken);
        var ran = 0;

        foreach (var scriptFile in scriptFiles)
        {
            var scriptName = Path.GetFileName(scriptFile);
            var sql = await File.ReadAllTextAsync(scriptFile, cancellationToken);
            var checksum = Checksum(sql);

            if (applied.TryGetValue(scriptName, out var previous))
            {
                if (previous == checksum)
                {
                    continue;
                }

                logger.LogWarning(
                    "SQL script {ScriptName} has changed since it was applied; running it again.",
                    scriptName);
            }

            var batches = sql.Split(["\r\nGO\r\n", "\nGO\n", "\r\nGO\n", "\nGO\r\n"],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            logger.LogInformation("Applying SQL script {ScriptName}", scriptName);

            foreach (var batch in batches)
            {
                if (string.IsNullOrWhiteSpace(batch))
                {
                    continue;
                }

                await ExecuteAsync(connection, batch, cancellationToken);
            }

            // Recorded only once every batch has succeeded, so a script that failed halfway is
            // retried on the next start rather than being remembered as done.
            await RecordAsync(connection, scriptName, checksum, cancellationToken);
            ran++;
        }

        logger.LogInformation(
            "Schema up to date: {Ran} script(s) applied, {Skipped} already present.",
            ran,
            scriptFiles.Length - ran);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = BatchTimeoutSeconds };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, string>> LoadLedgerAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        var applied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using var command = new SqlCommand(
            "SELECT ScriptName, Checksum FROM dbo.__SchemaMigrations;", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            applied[reader.GetString(0)] = reader.GetString(1);
        }

        return applied;
    }

    private static async Task RecordAsync(
        SqlConnection connection,
        string scriptName,
        string checksum,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           MERGE dbo.__SchemaMigrations AS target
                           USING (SELECT @ScriptName AS ScriptName) AS source
                               ON target.ScriptName = source.ScriptName
                           WHEN MATCHED THEN
                               UPDATE SET Checksum = @Checksum, AppliedAt = SYSUTCDATETIME()
                           WHEN NOT MATCHED THEN
                               INSERT (ScriptName, Checksum) VALUES (@ScriptName, @Checksum);
                           """;

        await using var command = new SqlCommand(sql, connection) { CommandTimeout = BatchTimeoutSeconds };
        command.Parameters.AddWithValue("@ScriptName", scriptName);
        command.Parameters.AddWithValue("@Checksum", checksum);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Hashes the script with line endings normalised, so a checkout that converts CRLF to LF
    /// does not read as an edit and re-run the whole schema.
    /// </summary>
    private static string Checksum(string sql)
    {
        var normalised = sql.Replace("\r\n", "\n").Replace('\r', '\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }
}
