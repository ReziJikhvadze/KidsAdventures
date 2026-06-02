namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IFamilyMemberRepository
{
    Task<IReadOnlyList<FamilyMember>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FamilyMember>> GetByChildIdAsync(Guid childId, Guid userId, CancellationToken cancellationToken);
    Task<FamilyMember?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<int> CountByChildIdAsync(Guid childId, Guid userId, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(FamilyMember member, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(FamilyMember member, Guid userId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken);
}
