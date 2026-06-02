using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class FamilyMemberRepository(ISqlConnectionFactory connectionFactory) : IFamilyMemberRepository
{
    public async Task<IReadOnlyList<FamilyMember>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT fm.Id, fm.ChildId, fm.Name, fm.Relationship, fm.PhotoUrl, fm.CreatedAt
                           FROM FamilyMembers fm
                           INNER JOIN Children c ON c.Id = fm.ChildId
                           WHERE c.UserId = @UserId
                           ORDER BY fm.CreatedAt DESC;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<FamilyMember>(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<FamilyMember>> GetByChildIdAsync(Guid childId, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT fm.Id, fm.ChildId, fm.Name, fm.Relationship, fm.PhotoUrl, fm.CreatedAt
                           FROM FamilyMembers fm
                           INNER JOIN Children c ON c.Id = fm.ChildId
                           WHERE fm.ChildId = @ChildId AND c.UserId = @UserId
                           ORDER BY fm.CreatedAt DESC;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<FamilyMember>(new CommandDefinition(sql, new { ChildId = childId, UserId = userId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<FamilyMember?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP 1 fm.Id, fm.ChildId, fm.Name, fm.Relationship, fm.PhotoUrl, fm.CreatedAt
                           FROM FamilyMembers fm
                           INNER JOIN Children c ON c.Id = fm.ChildId
                           WHERE fm.Id = @Id AND c.UserId = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<FamilyMember>(new CommandDefinition(sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<int> CountByChildIdAsync(Guid childId, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT COUNT(1)
                           FROM FamilyMembers fm
                           INNER JOIN Children c ON c.Id = fm.ChildId
                           WHERE fm.ChildId = @ChildId AND c.UserId = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { ChildId = childId, UserId = userId }, cancellationToken: cancellationToken));
    }

    public async Task<Guid> CreateAsync(FamilyMember member, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO FamilyMembers (Id, ChildId, Name, Relationship, PhotoUrl, CreatedAt)
                           VALUES (@Id, @ChildId, @Name, @Relationship, @PhotoUrl, @CreatedAt);
                           """;
        member.Id = member.Id == Guid.Empty ? Guid.NewGuid() : member.Id;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, member, cancellationToken: cancellationToken));
        return member.Id;
    }

    public async Task<bool> UpdateAsync(FamilyMember member, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE fm
                           SET fm.Name = @Name,
                               fm.Relationship = @Relationship,
                               fm.PhotoUrl = @PhotoUrl
                           FROM FamilyMembers fm
                           INNER JOIN Children c ON c.Id = fm.ChildId
                           WHERE fm.Id = @Id AND c.UserId = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            member.Id,
            member.Name,
            member.Relationship,
            member.PhotoUrl,
            UserId = userId
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           DELETE fm
                           FROM FamilyMembers fm
                           INNER JOIN Children c ON c.Id = fm.ChildId
                           WHERE fm.Id = @Id AND c.UserId = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
        return affected > 0;
    }
}
