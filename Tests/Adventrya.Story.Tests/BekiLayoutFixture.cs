using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Adventrya.Story.Tests;

/// <summary>
/// One Forest test book, shared by every layout test (§R15's "freeze one fixture book").
///
/// The artwork is generated at the sheet's own proportions rather than at the image model's 3:2,
/// because the composer's print path now refuses a crop deeper than its tolerance: a fixture drawn
/// at the wrong shape would fail every test for a reason none of them are about.
/// </summary>
internal static class BekiLayoutFixture
{
    /// <summary>The theme every fixture book is written for — Forest, per the supplier's order.</summary>
    public const string Theme = "Animals";

    public const string CanonicalThemeId = "forest";

    public const string ChildName = "ნინო";

    public const int ChildAge = 4;

    public const string WorldName = "მოჯადოებული ტყე";

    public static BekiBookPersonalization Personalization(
        string? name = null, int? age = null, string? theme = null) =>
        new(name ?? ChildName,
            age ?? ChildAge,
            new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc),
            theme ?? Theme,
            WorldName);

    /// <summary>
    /// The book's real geometry with its raster target dropped to screen resolution.
    ///
    /// Every rule these tests ask about — the page boxes, the crop tolerance, the exact raster
    /// dimensions, the density and the profile — is stated in millimetres and proportions and holds
    /// identically at any PPI. What 300 costs is time: each of the fourteen pages would be resampled
    /// to thirteen megapixels and JPEG-encoded, and the approved intro background would be carried
    /// at full size through every page render. At 96 the same code paths run on sheets a fortieth of
    /// the area. The 300 PPI contract itself is asserted directly, on one image, by
    /// <c>BekiPrintRasterTests</c>.
    /// </summary>
    public static BekiPrintLayoutOptions ScreenProofLayout() => new() { PrintTargetPpi = 96 };

    /// <summary>A flat spread-shaped sheet: the whole 450×210 bled canvas, so nothing is cropped.</summary>
    public static byte[] SheetPng((byte R, byte G, byte B) colour, int width = 1500)
    {
        var layout = new BekiPrintLayoutOptions();
        var height = (int)MathF.Round(width
            * (layout.SpreadHeightMm + (layout.BleedMm * 2f))
            / (layout.SpreadWidthMm + (layout.BleedMm * 2f)));
        return Solid(width, height, colour);
    }

    /// <summary>A flat leaf-shaped sheet, for the cover and the back cover.</summary>
    public static byte[] LeafPng((byte R, byte G, byte B) colour, int width = 800)
    {
        var layout = new BekiPrintLayoutOptions();
        var height = (int)MathF.Round(width
            * (layout.SpreadHeightMm + (layout.BleedMm * 2f))
            / (layout.PageWidthMm + (layout.BleedMm * 2f)));
        return Solid(width, height, colour);
    }

    private static byte[] Solid(int width, int height, (byte R, byte G, byte B) colour)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(colour.R, colour.G, colour.B, 255));
        using var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return buffer.ToArray();
    }

    public static MasterStory EightSpreadPlan(string? text = null) => new()
    {
        Concept = new StoryConcept { Title = "ნინო და მოჯადოებული ტყე", Outline = ["a", "b"] },
        CharacterLock = "A child.",
        Cover = new IllustrationBrief { Scene = "cover" },
        TitleEn = "Nino and the enchanted forest",
        Spreads = Enumerable.Range(1, BookFormat.SpreadCount).Select(number => new StorySpread
        {
            Number = number,
            Title = string.Empty,
            Caption = string.Empty,
            Text = text ?? $"ნინო ფრთხილად შევიდა ტყეში და გაიხედა ირგვლივ. {number}",
            TextEn = $"English {number}.",
            Illustration = new IllustrationBrief { Scene = $"scene {number}" },
            Characters = ["child"],
        }).ToList(),
    };
}
