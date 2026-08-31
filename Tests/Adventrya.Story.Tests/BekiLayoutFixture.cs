using System.Collections.Concurrent;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Options;
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
    ///
    /// <c>MaxPrintUpscale = 0</c> turns off the D5b upscale guard for this fixture and this fixture
    /// only. The guard is real and the default is 1.05× — audit P1-01: a 143-PPI render Lanczos-
    /// stretched to a 300-PPI target reports resolution it does not have, and the book stops. But
    /// this fixture's spreads are one-pixel and 1500-pixel stand-ins composed against a 96-PPI
    /// sheet, which is an enlargement by construction and has nothing to do with the questions any
    /// of these suites ask. The law itself is asserted directly, on one image, by
    /// <c>BekiSpecV2PrintTests.NormalizeForPrint_refuses_to_invent_detail_by_enlarging</c>.
    /// </summary>
    public static BekiPrintLayoutOptions ScreenProofLayout() =>
        new() { PrintTargetPpi = 96, MaxPrintUpscale = 0f };

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

    private static byte[] Solid(int width, int height, (byte R, byte G, byte B) colour) =>
        SyntheticImages.SolidPng(width, height, colour);

    /// <summary>
    /// The fixture book rendered to pages at screen proof resolution — green spreads, a red cover
    /// leaf, the default personalization — which is the book three separate layout suites were each
    /// rendering for themselves, page for page identical.
    ///
    /// §R15 asks for one frozen fixture book; this is it, rendered once for the whole assembly
    /// rather than once per test. The pages are handed out as copies, so a caller still owns what it
    /// receives and no suite can be perturbed by another.
    /// </summary>
    public static IReadOnlyList<byte[]> ScreenProofPages() =>
        StandardPages.Value.Select(page => page.ToArray()).ToList();

    private static readonly Lazy<IReadOnlyList<byte[]>> StandardPages = new(() =>
    {
        var plan = EightSpreadPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, SheetPng((0, 200, 120))))
            .ToList();

        return new BekiPdfComposer(Options.Create(ScreenProofLayout()))
            .RenderPages(plan, LeafPng((200, 60, 60)), spreads, Personalization());
    });

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
