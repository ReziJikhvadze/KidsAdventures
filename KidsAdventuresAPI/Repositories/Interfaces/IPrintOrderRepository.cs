using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.DTOs.Print;

namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IPrintOrderRepository
{
    /// <summary>
    /// Creates the parcel unless this order already has one, returning the existing row
    /// if so. A replayed webhook must not ship two books.
    /// </summary>
    Task<PrintOrder> CreateIfAbsentAsync(PrintOrder printOrder, CancellationToken cancellationToken);

    Task<PrintOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PrintOrder?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    Task<PrintOrder?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrintOrder>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrintOrder>> GetByBookIdsAsync(
        IReadOnlyCollection<Guid> bookIds,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The operations queue, with the book, the hero, the buyer and the order's total already
    /// joined on. <paramref name="status"/> null means every parcel, newest first.
    ///
    /// One statement, deliberately, and it replaces a bare parcel listing. The console's queue
    /// used to be assembled by loading each parcel and then asking for its book, its user and its
    /// order in three more round trips — so a fifty-row page was two hundred queries, and it grew
    /// with the business. Nothing here is unavailable to the parcel row; it is all one join away.
    /// </summary>
    Task<IReadOnlyList<AdminPrintQueueRow>> GetAdminQueueAsync(
        PrintOrderStatus? status,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// One parcel in the queue's own shape, so the row a status change hands back is built from
    /// the same projection as the row it replaces. Null when the parcel is gone.
    /// </summary>
    Task<AdminPrintQueueRow?> GetAdminQueueRowAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<PrintOrderStatus, int>> GetAdminCountsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Moves the parcel on. Returns false when the row is gone, so the caller can tell
    /// "not found" from "updated".
    /// </summary>
    Task<bool> UpdateStatusAsync(
        Guid id,
        PrintOrderStatus status,
        string? trackingCode,
        CancellationToken cancellationToken);

    Task<bool> UpdateAddressAsync(PrintOrder printOrder, CancellationToken cancellationToken);
}

public interface IUserAddressRepository
{
    Task<IReadOnlyList<UserAddress>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserAddress?> GetDefaultAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the address for reuse. Marking one default clears the flag on the others in
    /// the same transaction, so a user can never end up with two defaults.
    /// </summary>
    Task<UserAddress> UpsertAsync(UserAddress address, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken);
}
