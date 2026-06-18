using AdventurePacks.Api.Configuration.Options;

namespace AdventurePacks.Api.Domain;

public static class BookPackPlans
{
    /// <summary>The single one-time $4.99 purchase that grants 1 book credit.</summary>
    public const string Book1 = "Book1";

    public const string Default = Book1;

    public static bool IsSupported(string planType) =>
        string.Equals(planType, Book1, StringComparison.OrdinalIgnoreCase);

    public static int GetCredits(string planType) =>
        IsSupported(planType)
            ? 1
            : throw new InvalidOperationException($"Unknown book pack plan: {planType}");

    public static string GetPriceId(string planType, StripeOptions stripe) =>
        IsSupported(planType)
            ? stripe.BookPriceId
            : throw new InvalidOperationException($"Unknown book pack plan: {planType}");

    public static string GetDodoProductId(string planType, DodoPaymentsOptions dodo) =>
        IsSupported(planType)
            ? dodo.BookProductId
            : throw new InvalidOperationException($"Unknown book pack plan: {planType}");
}
