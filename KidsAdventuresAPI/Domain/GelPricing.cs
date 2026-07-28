namespace AdventurePacks.Api.Domain;

/// <summary>
/// The Georgian price list, in tetri.
///
/// Minor units throughout: a total is only ever integer arithmetic, so 20 percent off
/// 79 GEL is exactly 6320 tetri rather than a float that rounds differently in C# than
/// it does in SQL or in the browser. The frontend's <c>lib/pricing.ts</c> holds the same
/// numbers for display; this class is the only one the server trusts.
/// </summary>
public static class GelPricing
{
    public const string Currency = "GEL";

    /// <summary>Digital book: 14 GEL.</summary>
    public const int DigitalMinor = 1400;

    /// <summary>Printed hardback plus digital: 79 GEL.</summary>
    public const int PrintMinor = 7900;

    /// <summary>Adding print to a book already bought digitally: 65 GEL.</summary>
    public const int PrintUpgradeMinor = 6500;

    public static int SubtotalFor(OrderType type, OrderPackage package) => type switch
    {
        OrderType.PrintUpgrade => PrintUpgradeMinor,
        OrderType.NewBook => package switch
        {
            OrderPackage.Digital => DigitalMinor,
            OrderPackage.Print => PrintMinor,
            _ => throw new InvalidOperationException("პაკეტი არასწორია.")
        },
        _ => throw new InvalidOperationException("შეკვეთის ტიპი არასწორია.")
    };

    /// <summary>A print upgrade is always the print package; it has no digital variant.</summary>
    public static OrderPackage PackageFor(OrderType type, OrderPackage requested) =>
        type == OrderType.PrintUpgrade ? OrderPackage.Print : requested;

    /// <summary>
    /// Percentage discounts round down, so a promo never charges the parent a tetri
    /// more than the advertised percentage implies.
    /// </summary>
    public static int PercentDiscount(int subtotalMinor, int percentOff) =>
        Math.Clamp(subtotalMinor * percentOff / 100, 0, subtotalMinor);

    /// <summary>Human-readable amount for emails and admin screens, e.g. "79.00 ₾".</summary>
    public static string Format(int minor) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{minor / 100}.{minor % 100:00} ₾");
}
