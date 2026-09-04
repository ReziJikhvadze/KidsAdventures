using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.DTOs.Print;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Implementations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adventrya.Story.Tests;

public class PrintManufacturingHoldTests
{
    [Theory]
    [InlineData("Printing")]
    [InlineData("Shipped")]
    [InlineData("Delivered")]
    public async Task Customer_pdf_does_not_authorize_printing_or_shipping(string next)
    {
        var book = new AdventurePack
        {
            Id = Guid.NewGuid(), Status = AdventurePackStatus.Completed,
            PdfUrl = "customer-book.pdf", PrintPdfUrl = null
        };
        var parcels = new Parcels(new PrintOrder { Id = Guid.NewGuid(), BookId = book.Id });
        var service = new PrintOrderService(parcels, null!, new ReconcilePacks(book),
            null!, null!, null!, null!, NullLogger<PrintOrderService>.Instance);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateStatusAsync(
            parcels.Row.Id, new UpdatePrintOrderStatusRequest
            { Status = next, TrackingCode = "test-tracking", NotifyCustomer = false }, CancellationToken.None));

        Assert.Contains("ბეჭდვა შეჩერებულია", error.Message);
        Assert.Equal(0, parcels.Updates);
        Assert.Equal(PrintOrderStatus.AwaitingPrint, parcels.Row.Status);
    }

    private sealed class Parcels(PrintOrder row) : IPrintOrderRepository
    {
        public PrintOrder Row => row;
        public int Updates { get; private set; }
        public Task<PrintOrder?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<PrintOrder?>(row);
        public Task<bool> UpdateStatusAsync(Guid id, PrintOrderStatus status, string? code, CancellationToken ct)
        { Updates++; return Task.FromResult(true); }
        public Task<PrintOrder> CreateIfAbsentAsync(PrintOrder parcel, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrintOrder?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrintOrder?> GetByOrderIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PrintOrder>> GetByUserIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PrintOrder>> GetByBookIdsAsync(IReadOnlyCollection<Guid> ids, Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdminPrintQueueRow>> GetAdminQueueAsync(PrintOrderStatus? status, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<AdminPrintQueueRow?> GetAdminQueueRowAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<PrintOrderStatus, int>> GetAdminCountsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> UpdateAddressAsync(PrintOrder parcel, CancellationToken ct) => throw new NotSupportedException();
    }
}
