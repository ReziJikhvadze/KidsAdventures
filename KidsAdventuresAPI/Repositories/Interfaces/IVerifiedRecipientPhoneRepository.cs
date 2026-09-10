namespace AdventurePacks.Api.Repositories.Interfaces;

/// <summary>
/// The numbers a parent has already proved they can reach, so checkout asks once and not again.
/// </summary>
public interface IVerifiedRecipientPhoneRepository
{
    /// <summary>
    /// Whether <paramref name="phoneNumber"/> has been proved by this parent before.
    ///
    /// The number arrives normalised: the caller runs it through
    /// <see cref="Infrastructure.GeorgianPhoneNumber"/> first, so a number typed with spaces one day and
    /// without them the next reads as the same row rather than as a second one to verify.
    /// </summary>
    Task<bool> IsVerifiedAsync(Guid userId, string phoneNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Records the number as proved. Verifying the same number twice is not an error - a parent
    /// who reloads checkout mid-flow is doing nothing wrong - so this is a no-op the second time.
    /// </summary>
    Task MarkVerifiedAsync(Guid userId, string phoneNumber, CancellationToken cancellationToken);
}
