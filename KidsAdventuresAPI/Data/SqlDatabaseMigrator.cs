using Microsoft.Data.SqlClient;

namespace AdventurePacks.Api.Data;

public interface ISqlDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}

public sealed class SqlDatabaseMigrator(IConfiguration configuration, ILogger<SqlDatabaseMigrator> logger) : ISqlDatabaseMigrator
{
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
        await using (var sessionOptions = new SqlCommand(
                         "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", connection))
        {
            await sessionOptions.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var scriptFile in scriptFiles)
        {
            var scriptName = Path.GetFileName(scriptFile);
            var sql = await File.ReadAllTextAsync(scriptFile, cancellationToken);
            var batches = sql.Split(["\r\nGO\r\n", "\nGO\n", "\r\nGO\n", "\nGO\r\n"],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            logger.LogInformation("Applying SQL script {ScriptName}", scriptName);

            foreach (var batch in batches)
            {
                if (string.IsNullOrWhiteSpace(batch))
                {
                    continue;
                }

                await using var command = new SqlCommand(batch, connection);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
