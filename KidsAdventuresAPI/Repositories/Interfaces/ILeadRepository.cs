namespace AdventurePacks.Api.Repositories.Interfaces;

public interface ILeadRepository
{
    /// <summary>
    /// Inserts the lead if the email is new. Returns <c>true</c> only when a brand-new row was created,
    /// so the caller can send the welcome nudge exactly once per email (repeat submissions are ignored).
    /// </summary>
    Task<bool> TryCreateAsync(Lead lead, CancellationToken cancellationToken);

    /// <summary>Stamps <c>EmailedAt</c> after the follow-up email is dispatched.</summary>
    Task MarkEmailedAsync(Guid id, CancellationToken cancellationToken);
}
