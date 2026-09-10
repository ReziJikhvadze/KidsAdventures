namespace AdventurePacks.Api.Domain.Enums;

public enum AuthChallengePurpose
{
    /// <summary>One-time link emailed to a parent; the secret is a 256-bit token.</summary>
    MagicLink = 0,

    /// <summary>Four-digit code sent by SMS to a Georgian mobile number, to sign in.</summary>
    PhoneOtp = 1,

    /// <summary>
    /// The same four digits, sent to the number a parcel is being posted to, and proving nothing
    /// about who is signed in.
    ///
    /// A separate purpose from <see cref="PhoneOtp"/> because consuming one must not do what
    /// consuming the other does. A sign-in code resolves or creates the account that owns the
    /// number; this code says only "somebody holding this handset read four digits", which is all
    /// a courier needs and all a parent posting a book to their mother should be asked to prove.
    /// Sharing the purpose would have let a code issued at checkout be replayed at the sign-in
    /// endpoint and taken as the account holder's.
    /// </summary>
    RecipientPhone = 2
}
