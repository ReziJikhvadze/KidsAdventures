namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>
/// Tells whoever is on duty that something happened worth looking at.
///
/// Three events, chosen because each one has an action behind it: money arrived and a book now
/// owes a customer, a book failed and someone has paid for nothing, a parcel needs printing.
/// "Book finished" is deliberately absent — it happens on every sale and an alert nobody acts
/// on is an alert everybody learns to ignore.
///
/// Every method here swallows its own failures. These are notifications about work, never part
/// of it: a mail server that is down must not fail a payment.
/// </summary>
public interface IAdminNotifier
{
    Task OrderPaidAsync(Order order, CancellationToken cancellationToken);

    Task BookFailedAsync(Guid packId, string reason, CancellationToken cancellationToken);

    Task PrintOrderPlacedAsync(PrintOrder printOrder, string? bookTitle, CancellationToken cancellationToken);
}
