using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class LeadRepository(ISqlConnectionFactory connectionFactory) : ILeadRepository
{
    public async Task<bool> TryCreateAsync(Lead lead, CancellationToken cancellationToken)
    {
        // Insert only when the email is not already captured; the unique index makes this race-safe.
        const string sql = """
                           IF NOT EXISTS (SELECT 1 FROM Leads WHERE Email = @Email)
                           BEGIN
                               INSERT INTO Leads (Id, Email, Source, ChildName, Theme, CreatedAt, EmailedAt)
                               VALUES (@Id, @Email, @Source, @ChildName, @Theme, @CreatedAt, @EmailedAt);
                           END;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            lead.Id,
            lead.Email,
            lead.Source,
            lead.ChildName,
            lead.Theme,
            lead.CreatedAt,
            lead.EmailedAt
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task MarkEmailedAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE Leads SET EmailedAt = SYSUTCDATETIME() WHERE Id = @Id;";
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }
}
