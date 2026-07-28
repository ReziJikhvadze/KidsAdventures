using Microsoft.Data.SqlClient;

using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class PromoCodeRepository(ISqlConnectionFactory connectionFactory) : IPromoCodeRepository
{
    private const int UniqueConstraintViolation = 2601;
    private const int UniqueKeyViolation = 2627;

    private const string Columns = """
        Id, Code, Description, PercentOff, IsFullDiscount, MaxRedemptions, RedemptionCount,
        OncePerUser, StartsAt, ExpiresAt, IsActive, CreatedAt
        """;

    public async Task<PromoCode?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var sql = $"SELECT TOP 1 {Columns} FROM dbo.PromoCodes WHERE Code = @Code;";
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<PromoCode>(new CommandDefinition(
            sql,
            new { Code = Normalize(code) },
            cancellationToken: cancellationToken));
    }

    public async Task<PromoCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var sql = $"SELECT TOP 1 {Columns} FROM dbo.PromoCodes WHERE Id = @Id;";
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<PromoCode>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<bool> HasUserRedeemedAsync(
        Guid promoCodeId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT CASE WHEN EXISTS (
                               SELECT 1 FROM dbo.PromoRedemptions
                               WHERE PromoCodeId = @PromoCodeId AND UserId = @UserId
                           ) THEN 1 ELSE 0 END;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql,
            new { PromoCodeId = promoCodeId, UserId = userId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> TryRedeemAsync(PromoRedemption redemption, CancellationToken cancellationToken)
    {
        redemption.Id = redemption.Id == Guid.Empty ? Guid.NewGuid() : redemption.Id;

        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Claim the quota first. The WHERE clause is the gate, so two orders racing
            // for the last redemption of a limited code cannot both win.
            const string claimSql = """
                                    UPDATE dbo.PromoCodes
                                    SET RedemptionCount = RedemptionCount + 1
                                    WHERE Id = @PromoCodeId
                                      AND IsActive = 1
                                      AND (MaxRedemptions IS NULL OR RedemptionCount < MaxRedemptions);
                                    """;
            var claimed = await connection.ExecuteAsync(new CommandDefinition(
                claimSql,
                new { redemption.PromoCodeId },
                transaction,
                cancellationToken: cancellationToken));

            if (claimed == 0)
            {
                transaction.Rollback();
                return false;
            }

            const string insertSql = """
                                     INSERT INTO dbo.PromoRedemptions
                                         (Id, PromoCodeId, UserId, OrderId, DiscountMinor, RedeemedAt)
                                     VALUES
                                         (@Id, @PromoCodeId, @UserId, @OrderId, @DiscountMinor, @RedeemedAt);
                                     """;
            await connection.ExecuteAsync(new CommandDefinition(
                insertSql, redemption, transaction, cancellationToken: cancellationToken));

            transaction.Commit();
            return true;
        }
        catch (SqlException ex) when (ex.Number is UniqueConstraintViolation or UniqueKeyViolation)
        {
            // UX_PromoRedemptions_OrderId fired: this order already redeemed a code, which
            // means a duplicate webhook got here first. Not an error, just nothing to do.
            transaction.Rollback();
            return false;
        }
    }

    private static string Normalize(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();
}
