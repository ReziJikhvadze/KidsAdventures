using AdventurePacks.Api.Configuration.Options;

namespace AdventurePacks.Api.Domain;

public static class BookPackPlans
{
    public const string Books3 = "Books3";
    public const string Books5 = "Books5";
    public const string Books15 = "Books15";

    public static bool IsSupported(string planType) =>
        planType is Books3 or Books5 or Books15;

    public static int GetCredits(string planType) => planType switch
    {
        Books3 => 3,
        Books5 => 5,
        Books15 => 15,
        _ => throw new InvalidOperationException($"Unknown book pack plan: {planType}")
    };

    public static string GetPriceId(string planType, StripeOptions stripe) => planType switch
    {
        Books3 => stripe.Books3PriceId,
        Books5 => stripe.Books5PriceId,
        Books15 => stripe.Books15PriceId,
        _ => throw new InvalidOperationException($"Unknown book pack plan: {planType}")
    };

    public static string GetDodoProductId(string planType, DodoPaymentsOptions dodo) => planType switch
    {
        Books3 => dodo.Books3ProductId,
        Books5 => dodo.Books5ProductId,
        Books15 => dodo.Books15ProductId,
        _ => throw new InvalidOperationException($"Unknown book pack plan: {planType}")
    };
}
