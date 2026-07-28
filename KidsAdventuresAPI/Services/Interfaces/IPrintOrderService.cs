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
}
