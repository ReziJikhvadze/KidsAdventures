using AdventurePacks.Api.DTOs.Auth;
using AdventurePacks.Api.DTOs.Orders;

namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>
/// Proves the handset a parcel is being posted to, before the parcel is paid for.
///
/// A printed book is posted to a phone number as much as to a street: the courier rings before
/// they climb the stairs, and a digit typed wrong is a book that comes back. So checkout sends
/// four digits to the recipient and will not take money until they come back.
///
/// Three things this deliberately is not:
///
/// * It is not a sign-in. The number belongs to whoever is receiving the book - a grandmother, a
///   godparent, a child's other house - and redeeming the code says nothing about who owns the
///   account. Nobody is created, nothing is logged in, and the account's own
///   <c>PhoneNumber</c> is left exactly as it was.
/// * It is not asked twice. A number this parent has already proved is remembered, so the second
///   book to the same grandmother goes straight to payment. Their own confirmed number counts
///   too: a parent who signed in by SMS five minutes ago has proved it already.
/// * It is not asked of a digital order. There is no parcel, no courier and no recipient - only a
///   file - so there is nothing for a phone number to be true of.
/// </summary>
public interface IRecipientPhoneVerificationService
{
    /// <summary>What checkout asks before it decides whether to show the panel at all.</summary>
    Task<RecipientPhoneStatusResponse> GetStatusAsync(
        Guid userId, string phoneNumber, CancellationToken cancellationToken);

    /// <summary>Sends the code. Throttled exactly as a sign-in code is.</summary>
    Task<AuthChallengeResponse> RequestCodeAsync(
        Guid userId, string phoneNumber, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Redeems the code and remembers the number. Throws
    /// <see cref="UnauthorizedAccessException"/> with the sentence a parent should read when the
    /// code is wrong, spent or expired.
    /// </summary>
    Task<RecipientPhoneStatusResponse> VerifyAsync(
        Guid userId, string phoneNumber, string code, CancellationToken cancellationToken);

    /// <summary>
    /// The gate itself, called on the way into a print order.
    ///
    /// The panel in front of the pay button is the part a parent sees; this is the part that
    /// makes it true. A client that skips the panel, or a session that was left open long enough
    /// for the answer to change, arrives here and is turned back before an order row exists.
    /// </summary>
    Task EnsureVerifiedAsync(Guid userId, string? phoneNumber, CancellationToken cancellationToken);
}
