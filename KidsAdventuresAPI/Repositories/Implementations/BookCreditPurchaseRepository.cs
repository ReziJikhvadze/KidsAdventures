using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class BookCreditPurchaseRepository(ISqlConnectionFactory connectionFactory) : IBookCreditPurchaseRepository
{
    public async Task<bool> ExistsForUserAsync(
        Guid userId,
        string fulfillmentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP (1) 1
                           FROM BookCreditPurchases
                           WHERE UserId = @UserId
                             AND StripeSessionId = @FulfillmentId;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var exists = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(sql, new { UserId = userId, FulfillmentId = fulfillmentId }, cancellationToken: cancellationToken));
        return exists == 1;
    }

    public async Task<bool> TryRecordPurchaseAsync(
        Guid userId,
        string stripeSessionId,
        int creditsAdded,
        string planType,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO BookCreditPurchases (Id, UserId, StripeSessionId, CreditsAdded, PlanType, CreatedAt)
                           VALUES (@Id, @UserId, @StripeSessionId, @CreditsAdded, @PlanType, @CreatedAt);
                           """;

        try
        {
            using var connection = connectionFactory.CreateConnection();
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                StripeSessionId = stripeSessionId,
                CreditsAdded = creditsAdded,
                PlanType = planType,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken: cancellationToken));
            return true;
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            return false;
        }
    }

    private static bool IsDuplicateKey(Exception ex)
    {
        var message = ex.ToString();
        return message.Contains("UX_BookCreditPurchases_StripeSessionId", StringComparison.OrdinalIgnoreCase)
               || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
               || message.Contains("2601", StringComparison.Ordinal);
    }
}
