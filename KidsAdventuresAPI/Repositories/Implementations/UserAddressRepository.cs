using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class UserAddressRepository(ISqlConnectionFactory connectionFactory) : IUserAddressRepository
{
    private const string Columns = """
        Id, UserId, RecipientName, RecipientPhone, City, Region,
        AddressLine1, AddressLine2, PostalCode, IsDefault, CreatedAt
        """;

    public async Task<IReadOnlyList<UserAddress>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT {Columns}
                   FROM dbo.UserAddresses
                   WHERE UserId = @UserId
                   ORDER BY IsDefault DESC, CreatedAt DESC;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<UserAddress>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<UserAddress?> GetDefaultAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Falls back to the most recent address when nothing is flagged default, so
        // "use saved address" still has something to offer.
        var sql = $"""
                   SELECT TOP 1 {Columns}
                   FROM dbo.UserAddresses
                   WHERE UserId = @UserId
                   ORDER BY IsDefault DESC, CreatedAt DESC;
                   """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<UserAddress>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<UserAddress> UpsertAsync(UserAddress address, CancellationToken cancellationToken)
    {
        address.Id = address.Id == Guid.Empty ? Guid.NewGuid() : address.Id;

        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (address.IsDefault)
        {
            // Clearing the old default inside the same transaction is what keeps
            // "exactly one default" true even when two tabs save at once.
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.UserAddresses SET IsDefault = 0 WHERE UserId = @UserId AND Id <> @Id;",
                new { address.UserId, address.Id },
                transaction,
                cancellationToken: cancellationToken));
        }

        const string sql = """
                           MERGE dbo.UserAddresses AS target
                           USING (SELECT @Id AS Id) AS source ON target.Id = source.Id
                           WHEN MATCHED AND target.UserId = @UserId THEN
                               UPDATE SET RecipientName = @RecipientName,
                                          RecipientPhone = @RecipientPhone,
                                          City = @City,
                                          Region = @Region,
                                          AddressLine1 = @AddressLine1,
                                          AddressLine2 = @AddressLine2,
                                          PostalCode = @PostalCode,
                                          IsDefault = @IsDefault
                           WHEN NOT MATCHED THEN
                               INSERT (Id, UserId, RecipientName, RecipientPhone, City, Region,
                                       AddressLine1, AddressLine2, PostalCode, IsDefault, CreatedAt)
                               VALUES (@Id, @UserId, @RecipientName, @RecipientPhone, @City, @Region,
                                       @AddressLine1, @AddressLine2, @PostalCode, @IsDefault, @CreatedAt);
                           """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql, address, transaction, cancellationToken: cancellationToken));

        transaction.Commit();
        return address;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM dbo.UserAddresses WHERE Id = @Id AND UserId = @UserId;";
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
        return affected > 0;
    }
}
