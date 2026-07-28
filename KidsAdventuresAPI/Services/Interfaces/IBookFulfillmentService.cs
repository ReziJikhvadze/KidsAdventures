namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>
/// Turns a paid order into a book.
///
/// This is the seam that inverts the old model: nothing generates a full story until an
/// order says the money arrived. It is called from webhook handling, from the success
/// page, and from the retry sweeper, so every method has to be safe to run twice on the
/// same order.
/// </summary>
public interface IBookFulfillmentService
{
    /// <summary>
    /// Fulfils a paid order: creates or unlocks the book, records the print entitlement,
    /// advances the adventure map, and queues generation. Returns the book's id.
    /// </summary>
    Task<Guid> FulfillAsync(Order order, CancellationToken cancellationToken);
}
