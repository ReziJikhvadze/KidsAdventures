using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class VerifiedRecipientPhoneRepository(ISqlConnectionFactory connectionFactory)
    : IVerifiedRecipientPhoneRepository
{
    public async Task<bool> IsVerifiedAsync(Guid userId, string phoneNumber, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP 1 1
                           FROM dbo.VerifiedRecipientPhones
                           WHERE UserId = @UserId AND PhoneNumber = @PhoneNumber;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var found = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            sql, new { UserId = userId, PhoneNumber = phoneNumber }, cancellationToken: cancellationToken));

        return found is not null;
    }

    public async Task MarkVerifiedAsync(Guid userId, string phoneNumber, CancellationToken cancellationToken)
    {
        // Guarded rather than MERGEd: the pair is the primary key, so two requests racing on the
        // same number would otherwise turn a parent double-tapping "verify" into a 2627.
        const string sql = """
                           IF NOT EXISTS (SELECT 1 FROM dbo.VerifiedRecipientPhones
                                          WHERE UserId = @UserId AND PhoneNumber = @PhoneNumber)
                           BEGIN
                               INSERT INTO dbo.VerifiedRecipientPhones (UserId, PhoneNumber, VerifiedAt)
                               VALUES (@UserId, @PhoneNumber, @VerifiedAt);
                           END;
                           """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            UserId = userId,
            PhoneNumber = phoneNumber,
            VerifiedAt = DateTime.UtcNow
        }, cancellationToken: cancellationToken));
    }
}
