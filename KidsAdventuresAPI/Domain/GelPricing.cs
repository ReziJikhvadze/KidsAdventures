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

    /// <summary>
    /// What the courier costs, and only for something a courier carries.
    ///
    /// The prices and the windows live in <see cref="GeorgianDelivery"/>, because they are a
    /// promise about time as much as a charge; this is only the rule that a file is not posted.
    /// A Digital order sent a delivery option is asking for something that does not exist, and
    /// the safe answer to that is nothing to pay.
    /// </summary>
    public static int DeliveryFor(OrderPackage package, DeliveryChoice delivery) =>
        SupportsGiftWrap(package) ? delivery.PriceMinor : 0;

    /// <summary>Whether wrapping can be added to this order at all — a posted parcel, not a file.</summary>
    public static bool SupportsGiftWrap(OrderPackage package) => package == OrderPackage.Print;

    /// <summary>
    /// Per copy: two books bought as presents are two presents, and each is wrapped. It was
    /// charged once per order, on the reasoning that one parcel is wrapped once; the owner's
    /// reading of what a parent is buying wins.
    /// </summary>
    public static int GiftWrapFor(OrderPackage package, bool giftWrap, int copies = 1) =>
        giftWrap && SupportsGiftWrap(package) ? GiftWrapMinor * Math.Max(1, copies) : 0;

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
        int quantity = 1,
        DeliveryChoice delivery = default)
    {
        /*
          Copies multiply the book, and the wrapping with it: each copy given as a present is
          wrapped as one. Delivery does not multiply - one parcel is carried once.
        */
        var copies = QuantityFor(type, package, quantity);

        /*
          Delivery is inside the subtotal for the same reason wrapping is: a percentage code
          takes its cut of it and a full-discount code clears it, and either way the parent can
          add up the lines they were shown and arrive at the total they are charged. One parcel
          is delivered once, so it does not multiply with the copies.
        */
        return type switch
        {
            OrderType.PrintUpgrade =>
                PrintUpgradeMinor
                + GiftWrapFor(OrderPackage.Print, giftWrap)
                + DeliveryFor(OrderPackage.Print, delivery),
            OrderType.NewBook => package switch
            {
                OrderPackage.Digital => DigitalMinor,
                OrderPackage.Print =>
                    (PrintMinor * copies)
                    + GiftWrapFor(package, giftWrap, copies)
                    + DeliveryFor(package, delivery),
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
