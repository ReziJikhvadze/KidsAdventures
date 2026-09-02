using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.DTOs.Print;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IPrintOrderService
{
    /// <summary>
    /// Creates the parcel for a paid print order from the address frozen on it. Idempotent,
    /// because a replayed webhook must not ship a second book. Returns null when the order
    /// carries no address, which means the order was digital-only.
    /// </summary>
    Task<PrintOrder?> CreateForPaidOrderAsync(Order order, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrintOrderResponse>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<PrintOrderResponse?> GetForUserAsync(Guid userId, Guid printOrderId, CancellationToken cancellationToken);

    /// <summary>Corrects the delivery address, allowed only until the parcel ships.</summary>
    Task<PrintOrderResponse> UpdateAddressAsync(
        Guid userId,
        Guid printOrderId,
        ShippingAddressRequest request,
        CancellationToken cancellationToken);

    // -- saved addresses ----------------------------------------------------

    Task<IReadOnlyList<AddressResponse>> ListAddressesAsync(Guid userId, CancellationToken cancellationToken);

    Task<AddressResponse> SaveAddressAsync(
        Guid userId,
        SaveAddressRequest request,
        CancellationToken cancellationToken);

    Task<bool> DeleteAddressAsync(Guid userId, Guid addressId, CancellationToken cancellationToken);

    // -- operations console -------------------------------------------------

    Task<AdminPrintQueueResponse> GetAdminQueueAsync(
        string? status,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a parcel through the fulfilment states and, unless suppressed, emails the
    /// parent. Returns null when the parcel does not exist.
    /// </summary>
    Task<AdminPrintOrderResponse?> UpdateStatusAsync(
        Guid printOrderId,
        UpdatePrintOrderStatusRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Cancels the parcel behind a cancelled order, unless it has already shipped. False when
    /// there is no parcel or it is past the point where cancelling it would be true.
    ///
    /// Called by the console when an operator cancels the order. Without it, a cancelled order
    /// leaves its parcel in the print queue and a book is printed and posted to somebody who is
    /// not being charged for it.
    ///
    /// A default of "nothing to cancel" rather than an abstract member: the fulfilment tests double
    /// this interface for the one method they exercise, and none of them has a parcel.
    /// </summary>
    Task<bool> TryCancelForOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
