namespace AdventurePacks.Api.Domain;

/// <summary>
/// Which delivery a parcel can be sent by, what it costs, and how long it takes.
///
/// Two things decide it, in this order: the address, then the parent. Tbilisi is offered a
/// choice — three working days for 7 GEL, or five for nothing — and everywhere else in Georgia
/// is one flat 8 GEL in five to seven. So the address does not pick the speed, it picks the
/// menu, and the parent picks off it.
///
/// The server owns these numbers because they are money and a promise at the same time: the
/// figure on the checkout, the one the card is charged and the window in the shipping email all
/// come from here. <c>wwwroot/src/lib/pricing.ts</c> mirrors them for what the client renders
/// before a quote comes back, exactly as it mirrors the book prices.
/// </summary>
public static class GeorgianDelivery
{
    /*
      Tbilisi spelled every way a parent might type it, and matched against whatever the form
      collected — the city field when an autocomplete filled one in, and otherwise the address
      line the parent typed themselves, because without a Maps key that line is all there is.

      Anything unrecognised is treated as a region. That is the safe direction to be wrong in:
      a Tbilisi address priced as a region costs the parent more than it should, which they can
      see and query, where the reverse posts a parcel across the country for nothing.
    */
    private static readonly string[] TbilisiNames =
    [
        "თბილისი",
        "tbilisi",
        "tiflis"
    ];

    /// <summary>Three working days inside Tbilisi, 7 GEL.</summary>
    public const int TbilisiExpressMinor = 700;

    /// <summary>Five working days inside Tbilisi, and nothing to pay.</summary>
    public const int TbilisiStandardMinor = 0;

    /// <summary>Anywhere else in Georgia, 8 GEL.</summary>
    public const int RegionalMinor = 800;

    public const int TbilisiExpressDays = 3;
    public const int TbilisiStandardDays = 5;
    public const int RegionsMinDays = 5;
    public const int RegionsMaxDays = 7;

    public static bool IsTbilisi(string? city) =>
        !string.IsNullOrWhiteSpace(city) &&
        TbilisiNames.Any(name => city.Contains(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Tbilisi, judged on everything the form knows.
    ///
    /// The city field is the reliable one and is checked first; the address line is the fallback
    /// for the parent who typed the whole thing into one box, which is what happens whenever the
    /// address autocomplete is not available.
    /// </summary>
    public static bool IsTbilisiAddress(string? city, string? addressLine) =>
        IsTbilisi(city) || IsTbilisi(addressLine);

    /// <summary>The options this address may choose between, in the order they should be shown.</summary>
    public static IReadOnlyList<DeliveryOption> OptionsFor(string? city, string? addressLine = null) =>
        IsTbilisiAddress(city, addressLine)
            ? [DeliveryOption.TbilisiStandard, DeliveryOption.TbilisiExpress]
            : [DeliveryOption.Regional];

    /// <summary>
    /// What this address gets by default: the free one in Tbilisi, the only one elsewhere.
    ///
    /// It is also where an option the address cannot have lands. A client that asks for the
    /// free Tbilisi delivery to an address in Batumi is not refused — the checkout would fail on
    /// a mismatch the parent cannot see or fix — it is priced for the option that address really
    /// has, and the quote says which one that was.
    /// </summary>
    public static DeliveryOption DefaultFor(string? city, string? addressLine = null) =>
        OptionsFor(city, addressLine)[0];

    /// <summary>Resolves what the parent asked for against what the address allows.</summary>
    public static DeliveryChoice Resolve(string? city, string? addressLine, DeliveryOption? requested)
    {
        var allowed = OptionsFor(city, addressLine);
        var option = requested is { } asked && allowed.Contains(asked) ? asked : allowed[0];
        return For(option);
    }

    /// <summary>Same, from the string a request carries. An unknown name is no choice at all.</summary>
    public static DeliveryChoice Resolve(string? city, string? addressLine, string? requested) =>
        Resolve(city, addressLine, Parse(requested));

    public static DeliveryOption? Parse(string? value) =>
        Enum.TryParse<DeliveryOption>(value, ignoreCase: true, out var parsed) ? parsed : null;

    /// <summary>The price and the window one option carries, wherever it came from.</summary>
    public static DeliveryChoice For(DeliveryOption option) => option switch
    {
        DeliveryOption.TbilisiExpress =>
            new DeliveryChoice(option, TbilisiExpressMinor, TbilisiExpressDays, TbilisiExpressDays),
        DeliveryOption.TbilisiStandard =>
            new DeliveryChoice(option, TbilisiStandardMinor, TbilisiStandardDays, TbilisiStandardDays),
        _ => new DeliveryChoice(DeliveryOption.Regional, RegionalMinor, RegionsMinDays, RegionsMaxDays)
    };

    /// <summary>Nothing is being posted, so there is nothing to pay or promise.</summary>
    public static DeliveryChoice None { get; } = new(DeliveryOption.None, 0, 0, 0);

    /// <summary>Georgian text ready to render, e.g. "მიწოდება 5-7 სამუშაო დღეში".</summary>
    public static string Describe(DeliveryChoice choice) =>
        choice.MinDays == choice.MaxDays
            ? $"მიწოდება {choice.MinDays} სამუშაო დღეში"
            : $"მიწოდება {choice.MinDays}-{choice.MaxDays} სამუშაო დღეში";

    /// <summary>
    /// The window for an address whose chosen option is not known — an order placed before this
    /// choice existed, or an admin screen looking at one. It answers with what that address gets
    /// by default, which for Tbilisi is the free five days rather than the paid three.
    /// </summary>
    public static string DescribeFor(string? city) => Describe(For(DefaultFor(city)));

    /// <summary>The window for an order whose option was recorded, falling back to the address.</summary>
    public static string DescribeFor(string? city, string? option) =>
        Parse(option) is { } chosen ? Describe(For(chosen)) : DescribeFor(city);
}

/// <summary>How a parcel is being sent. <see cref="None"/> is a digital order, which is not.</summary>
public enum DeliveryOption
{
    None = 0,
    TbilisiStandard,
    TbilisiExpress,
    Regional
}

/// <summary>One resolved delivery: what it is, what it costs, and how long it takes.</summary>
public readonly record struct DeliveryChoice(DeliveryOption Option, int PriceMinor, int MinDays, int MaxDays)
{
    /// <summary>The name a request and an order row carry for it.</summary>
    public string Name => Option.ToString();
}
