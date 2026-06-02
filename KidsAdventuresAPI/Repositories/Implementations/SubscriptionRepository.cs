using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class SubscriptionRepository(ISqlConnectionFactory connectionFactory) : ISubscriptionRepository
{
    public async Task<Subscription?> GetCurrentByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP 1 Id, UserId, StripeCustomerId, StripeSubscriptionId, PlanType, ActiveUntil
                           FROM Subscriptions
                           WHERE UserId = @UserId
                           ORDER BY ActiveUntil DESC;
                           """;

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<SubscriptionRow>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task UpsertAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        const string sql = """
                           MERGE Subscriptions AS target
                           USING (SELECT @UserId AS UserId) AS source
                           ON target.UserId = source.UserId
                           WHEN MATCHED THEN
                               UPDATE SET StripeCustomerId = @StripeCustomerId,
                                          StripeSubscriptionId = @StripeSubscriptionId,
                                          PlanType = @PlanType,
                                          ActiveUntil = @ActiveUntil
                           WHEN NOT MATCHED THEN
                               INSERT (Id, UserId, StripeCustomerId, StripeSubscriptionId, PlanType, ActiveUntil)
                               VALUES (@Id, @UserId, @StripeCustomerId, @StripeSubscriptionId, @PlanType, @ActiveUntil);
                           """;

        subscription.Id = subscription.Id == Guid.Empty ? Guid.NewGuid() : subscription.Id;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            subscription.Id,
            subscription.UserId,
            subscription.StripeCustomerId,
            subscription.StripeSubscriptionId,
            PlanType = subscription.PlanType.ToString(),
            subscription.ActiveUntil
        }, cancellationToken: cancellationToken));
    }

    private static Subscription Map(SubscriptionRow row) => new()
    {
        Id = row.Id,
        UserId = row.UserId,
        StripeCustomerId = row.StripeCustomerId,
        StripeSubscriptionId = row.StripeSubscriptionId,
        PlanType = Enum.Parse<SubscriptionType>(row.PlanType),
        ActiveUntil = row.ActiveUntil
    };

    private sealed class SubscriptionRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string StripeCustomerId { get; set; } = string.Empty;
        public string StripeSubscriptionId { get; set; } = string.Empty;
        public string PlanType { get; set; } = string.Empty;
        public DateTime ActiveUntil { get; set; }
    }
}
