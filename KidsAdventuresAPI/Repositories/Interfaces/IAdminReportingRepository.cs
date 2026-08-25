using AdventurePacks.Api.DTOs.Admin;

namespace AdventurePacks.Api.Repositories.Interfaces;

/// <summary>
/// Cross-customer reads for the operations console. Every method here deliberately ignores
/// the per-user ownership predicate the customer-facing repositories enforce, so callers
/// must be behind <see cref="AuthorizationPolicies.Admin"/>.
/// </summary>
public interface IAdminReportingRepository
{
    Task<AdminOrderListResponse> GetOrdersAsync(
        string? status, string? search, string? flag, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>One order with its customer, its book and its parcel. Null when the id is unknown.</summary>
    Task<AdminOrderDetailResponse?> GetOrderDetailAsync(Guid orderId, CancellationToken cancellationToken);

    Task<AdminCustomerListResponse> GetCustomersAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken);
}
