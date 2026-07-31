using AdventurePacks.Api.DTOs.Admin;

namespace AdventurePacks.Api.Repositories.Interfaces;

/// <summary>
/// Cross-customer reads for the operations console. Every method here deliberately ignores
/// the per-user ownership predicate the customer-facing repositories enforce, so callers
/// must be behind <see cref="AuthorizationPolicies.Admin"/>.
/// </summary>
public interface IAdminReportingRepository
{
    Task<AdminOverviewResponse> GetOverviewAsync(DateTime sinceUtc, CancellationToken cancellationToken);

    Task<AdminOrderListResponse> GetOrdersAsync(
        string? status, string? search, int page, int pageSize, CancellationToken cancellationToken);

    Task<AdminCustomerListResponse> GetCustomersAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken);

    Task<AdminProductionListResponse> GetProductionAsync(
        bool includeCompleted, int page, int pageSize, CancellationToken cancellationToken);
}
