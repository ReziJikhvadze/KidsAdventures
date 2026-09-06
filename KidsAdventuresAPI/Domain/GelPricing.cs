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

    /*
      TEMPORARY — 1 GEL, to put a real card through the live gateway.

      BOG's test cards only work in their sandbox, which is a separate environment with its own
      credentials. The credentials this site has are live ones capped at 100 GEL, so the only way
      to see a payment reach the callback and a book come out the other side is to actually pay —
      and this is the smallest amount that proves it.

      Put it back to 1400 the moment that test passes. It is the live price of the digital book,
      and `wwwroot/src/lib/pricing.ts` carries the same number for display: change one and the
      site quotes a figure the server does not charge.
    */
    public const int DigitalMinor = 100;

    /*
      TEMPORARY — 1 GEL, and for the same reason as the digital price above: the printed book is
      the other thing a parent can buy, and it goes through the gateway on its own path, so
      proving the digital one proves only half of it.

      **The live price is 7900** — 79 GEL, printed hardback plus digital — and this goes back to
      it the moment the test passes. Both numbers are temporary now, so put both back together.

      Note that the refunds policy at `wwwroot/src/content/legal/refunds.ts` still quotes the real
      79 and 14, deliberately: it describes the product, not this week's test, and rewriting a
      published policy to say 1 GEL would be worse than the mismatch it avoids.
    */
    public const int PrintMinor = 100;

    /// <summary>Adding print to a book already bought digitally: 65 GEL.</summary>
    public const int PrintUpgradeMinor = 6500;

    /// <summary>
    /// Gift wrapping, 5 GEL, and only on something that is posted.
    ///
    /// There is nothing to wrap on a digital book, so the flag is dropped rather than
    /// charged for when the package is Digital — a client that sends it anyway is asking
    /// for something that does not exist, and the safe answer to that is the ordinary
    /// price rather than five lari the parent did not agree to.
    /// </summary>
    public const int GiftWrapMinor = 500;

    /// <summary>Whether wrapping can be added to this order at all — a posted parcel, not a file.</summary>
    public static bool SupportsGiftWrap(OrderPackage package) => package == OrderPackage.Print;

    public static int GiftWrapFor(OrderPackage package, bool giftWrap) =>
        giftWrap && SupportsGiftWrap(package) ? GiftWrapMinor : 0;

    /// <summary>
    /// The most printed copies of one book we will take in a single order.
    ///
    /// Five is a family and its grandparents, which is what the copies are for. Past that it is
    /// a wholesale order and wants a conversation, not a checkbox — and an unbounded number in
    /// a request body is an unbounded charge.
    /// </summary>
    public const int MaxPrintQuantity = 5;

    /// <summary>
    /// How many copies this order is actually for.
    ///
    /// Only a printed book has copies: a digital one is a file, and sending "3" with it would
    /// charge three times for the same download. Anything outside 1..<see cref="MaxPrintQuantity"/>
    /// is brought back inside it rather than rejected, so a stale client cannot fail a checkout.
    /// </summary>
    public static int QuantityFor(OrderType type, OrderPackage package, int quantity) =>
        type == OrderType.NewBook && package == OrderPackage.Print
            ? Math.Clamp(quantity, 1, MaxPrintQuantity)
            : 1;

    public static int SubtotalFor(
        OrderType type,
        OrderPackage package,
        bool giftWrap = false,
        int quantity = 1)
    {
        /*
          Copies multiply the book; wrapping does not multiply with them.

          Three copies is three books in one parcel to one address, and the parcel is wrapped
          once. Charging the five lari per copy would be charging for wrapping that nobody does.
        */
        var copies = QuantityFor(type, package, quantity);

        return type switch
        {
            OrderType.PrintUpgrade => PrintUpgradeMinor + GiftWrapFor(OrderPackage.Print, giftWrap),
            OrderType.NewBook => package switch
            {
                OrderPackage.Digital => DigitalMinor,
                OrderPackage.Print => (PrintMinor * copies) + GiftWrapFor(package, giftWrap),
                _ => throw new InvalidOperationException("პაკეტი არასწორია.")
            },
            _ => throw new InvalidOperationException("შეკვეთის ტიპი არასწორია.")
        };
    }

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
