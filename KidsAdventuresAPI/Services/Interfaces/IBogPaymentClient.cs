namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>What we need from BOG, and nothing more: start a payment, ask how it went,
/// and prove that a callback really came from the bank.</summary>
public interface IBogPaymentClient
{
    /// <summary>Creates an order on the gateway and returns where to send the parent.</summary>
    Task<BogCheckout> CreateOrderAsync(BogOrderRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The receipt for a BOG order. Null when it cannot be read — a confirmation poll must
    /// not fail the status endpoint, because the callback is the authoritative path.
    /// </summary>
    Task<BogPaymentDetails?> GetPaymentDetailsAsync(string bogOrderId, CancellationToken cancellationToken);

    /// <summary>RSA-SHA256 over the exact bytes BOG posted, against BOG's published public key.</summary>
    bool VerifyCallbackSignature(byte[] rawBody, string? signatureHeader);
}

/// <summary>Everything BOG needs to put up a payment page for one of our orders.</summary>
public sealed record BogOrderRequest(
    Guid OrderId,
    int TotalMinor,
    string Currency,
    string Description,
    string SuccessUrl,
    string FailUrl,
    string? BuyerEmail);

public sealed record BogCheckout(string BogOrderId, string RedirectUrl);

/// <summary>
/// The slice of a BOG receipt that decides what happens to an order. The full payload is
/// far larger; anything not read here is deliberately ignored rather than modelled.
/// </summary>
public sealed record BogPaymentDetails(
    string BogOrderId,
    Guid? OrderId,
    string StatusKey,
    string? TransactionId)
{
    /// <summary>The money is in. Only this status fulfils a book.</summary>
    public bool IsPaid => string.Equals(StatusKey, "completed", StringComparison.OrdinalIgnoreCase);

    /// <summary>Terminal failure: the parent can start again, but this order is done.</summary>
    public bool IsFailed =>
        string.Equals(StatusKey, "rejected", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(StatusKey, "blocked", StringComparison.OrdinalIgnoreCase);
}
