namespace AdventurePacks.Api.Domain;

/// <summary>
/// Delivery windows we quote inside Georgia: Tbilisi in 2-3 working days, the regions
/// in 5-7. The server owns these numbers so the estimate on the checkout screen and
/// the one in the shipping email can never disagree — and `wwwroot/src/lib/pricing.ts`
/// and the refunds policy carry the same pair for the pages the client renders alone.
/// </summary>
public static class GeorgianDelivery
{
    public const int TbilisiMinDays = 2;
    public const int TbilisiMaxDays = 3;
    public const int RegionsMinDays = 5;
    public const int RegionsMaxDays = 7;

    /// <summary>
    /// Tbilisi spelled every way a parent might type it. Anything unrecognised falls to
    /// the regional window, which is the safe direction to be wrong in.
    /// </summary>
    private static readonly string[] TbilisiNames =
    [
        "თბილისი",
        "tbilisi",
        "tiflis"
    ];

    public static bool IsTbilisi(string? city) =>
        !string.IsNullOrWhiteSpace(city) &&
        TbilisiNames.Any(name => city.Trim().Contains(name, StringComparison.OrdinalIgnoreCase));

    public static (int MinDays, int MaxDays) WindowFor(string? city) => IsTbilisi(city)
        ? (TbilisiMinDays, TbilisiMaxDays)
        : (RegionsMinDays, RegionsMaxDays);

    /// <summary>Georgian text ready to render, e.g. "მიწოდება 2-3 სამუშაო დღეში".</summary>
    public static string DescribeFor(string? city)
    {
        var (min, max) = WindowFor(city);
        return $"მიწოდება {min}-{max} სამუშაო დღეში";
    }
}
