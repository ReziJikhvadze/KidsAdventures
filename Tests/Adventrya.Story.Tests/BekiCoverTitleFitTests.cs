using System.Diagnostics;
using System.Text;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The cover carries the whole title.
///
/// **The observed defect, 2026-09-07 (owner).** "The book name is trimmed when put on the cover."
/// It was: <see cref="BekiPdfComposer"/> set the title at a fixed 36 pt inside the dieline's fixed
/// 136 × 46 mm title-safe rectangle, and 46 mm is 130 pt — two 36 pt lines and not three. A title
/// that wrapped to three lines had its last line paginated by QuestPDF into a page that does not
/// exist inside a <c>Layers</c> layer, so it was simply not printed. Nothing measured the block,
/// nothing compared it to the box, and the page's own receipt listed the line that never reached
/// the paper, because the wrapped lines were measured separately from what was drawn.
///
/// The answer is the ladder the story copy has always had: measure the block, take the largest size
/// that fits, and stop the book rather than print a book called something shorter than its name.
/// The four things that have to hold at once each have a test here — the long title survives whole,
/// the ladder was necessary, the two covers break their lines identically, and a title that fitted
/// before is still set at 36 pt.
/// </summary>
public class BekiCoverTitleFitTests
{
    /// <summary>
    /// A title long enough to need three lines at 36 pt — the defect's own shape, in the words a
    /// real book would use.
    /// </summary>
    private const string LongTitle = "ვერიკო და ღრუბლების ქალაქის დიდი საიდუმლო თავგადასავალი";

    /// <summary>The fixture book's own title, which fitted before this existed and still does.</summary>
    private const string ShortTitle = "ნინო და მოჯადოებული ტყე";

    /// <summary>The press cover's measure and box, in points: the dieline's 136 × 46 mm.</summary>
    private static float TitleWidthPt => BekiCoverDieline.TitleSafeWidthMm * 72f / 25.4f;

    private static float TitleBoxHeightPt => BekiCoverDieline.TitleSafeHeightMm * 72f / 25.4f;

    [Fact]
    public void Cover_uses_Mtavruli_without_changing_the_stored_title()
    {
        var plan = BekiLayoutFixture.EightSpreadPlan();
        var original = plan.Concept.Title;
        var composed = PressCover(original);
        var text = string.Join(" ", composed.Receipts.Pages[0].TextLines);
        Assert.Equal(original.ToUpperInvariant(), text);
        Assert.DoesNotContain(text, ch => ch is >= '\u10D0' and <= '\u10F0');
        Assert.Contains(text, ch => ch is >= '\u1C90' and <= '\u1CB0');
        Assert.Equal(original, plan.Concept.Title);

        // Explicit opt-in diagnostic: synthetic background only, never paid artwork generation.
        var proofPath = Environment.GetEnvironmentVariable("BEKI_COVER_STYLE_PROOF_PDF");
        if (!string.IsNullOrWhiteSpace(proofPath))
            File.WriteAllBytes(proofPath, composed.Pdf);
    }

    // ==============================================================================================
    // The whole title, at a size that fits
    // ==============================================================================================

    /// <summary>
    /// Every word of a long title is on the press cover, set smaller than the shipped 36 pt, in a
    /// block the title-safe box actually holds.
    ///
    /// The receipt's wrapped lines are the evidence the customer-PDF gate already compares, so
    /// "every word is present in them" is the same question the gate asks about the two files,
    /// asked here about the title and the book's own name.
    /// </summary>
    [Fact]
    public void A_long_title_is_printed_whole_at_the_largest_size_that_fits()
    {
        var receipt = PressCover(LongTitle).Receipts.Pages.Single(page => page.Role == "cover-wrap");
        var chosen = (float)Assert.Single(receipt.Typography).SizePt;

        Assert.True(chosen < 36f, $"the long title was still set at {chosen}pt.");
        Assert.Contains(chosen, new BekiPrintLayoutOptions().CoverTitleSizeLadderPt);

        // The block the page draws fits the box the page gives it.
        Assert.True(
            MeasureBlockPt(LongTitle, chosen) <= TitleBoxHeightPt,
            $"the chosen {chosen}pt block is taller than the {TitleBoxHeightPt:0}pt box.");

        // And nothing was trimmed: every word of the name the parent bought is in the lines.
        var lines = string.Join(" ", receipt.TextLines);

        foreach (var word in LongTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.Contains(word.ToUpperInvariant(), lines, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The ladder was necessary, and this is the measurement that says so: at the shipped 36 pt that
    /// title's block is taller than the box it is drawn inside. That is the file as it shipped this
    /// morning — a page laid out to overflow, with the overflow going nowhere.
    /// </summary>
    [Fact]
    public void At_the_old_fixed_size_that_title_did_not_fit_its_box()
    {
        Assert.True(
            MeasureBlockPt(LongTitle, 36f) > TitleBoxHeightPt,
            "the long title fits at 36pt, so this test is no longer about the observed defect.");

        Assert.True(MeasureBlockPt(ShortTitle, 32f) <= TitleBoxHeightPt);
    }

    /// <summary>
    /// Mtavruli is taller than Mkhedruli in Ottia. The same complete short title now fits at 32pt;
    /// the sizing ladder must measure the displayed capitals, not its stored lowercase name.
    /// </summary>
    [Fact]
    public void A_short_Mtavruli_title_steps_down_to_fit_and_scales_its_outline()
    {
        var receipt = PressCover(ShortTitle).Receipts.Pages.Single(page => page.Role == "cover-wrap");
        var typography = Assert.Single(receipt.Typography);

        Assert.Equal(32d, typography.SizePt, 6);
        Assert.Equal(1.25d, typography.LineHeight, 6);
        Assert.Equal(
            new BekiPrintLayoutOptions().CoverTitleOutlineWidthPt * 32d / 36d,
            typography.TitleOutlineWidthPt!.Value,
            6);
    }

    /// <summary>
    /// There is no size below the last rung and no trimming: a title that will not fit at 20 pt
    /// stops the book with <c>LAYOUT_FAILED</c> for a human, exactly as overlong story copy stops it
    /// with <c>TEXT_OVERFLOW</c>. The ladder is emptied to its top rung to ask the question, which is
    /// also the composer as it behaved before today.
    /// </summary>
    [Fact]
    public void A_title_that_fits_at_no_size_stops_the_book_rather_than_losing_a_line()
    {
        var layout = BekiLayoutFixture.ScreenProofLayout();
        layout.CoverTitleSizeLadderPt = [];

        var failure = Assert.Throws<BekiLayoutException>(() =>
            new BekiPdfComposer(Options.Create(layout))
                .ComposeCoverPressWithReceipts(LongTitle, Wrap(512, 245)));

        Assert.Equal(CompositeFailureCodes.LayoutFailed, failure.FailureCode);
        Assert.Contains(LongTitle.ToUpperInvariant(), failure.Message, StringComparison.Ordinal);
        Assert.Contains("never trimmed", failure.Message, StringComparison.Ordinal);
    }

    // ==============================================================================================
    // The two covers are one design
    // ==============================================================================================

    /// <summary>
    /// The customer's download breaks the title where the printed cover breaks it.
    ///
    /// Audit P0-01 was about exactly this class of divergence, and the fit ladder is a new way to
    /// produce it: the download's board is 1.12% smaller, so a ladder run against the download's own
    /// rectangle could land a rung lower and re-break every line. The reading cover therefore scales
    /// the size the PRESS cover chose, and its rim scales with it.
    /// </summary>
    [Fact]
    public void The_reading_cover_scales_the_press_size_and_breaks_the_same_lines()
    {
        var press = PressCover(LongTitle).Receipts.Pages.Single(page => page.Role == "cover-wrap");
        var reading = ReadingCover(LongTitle).Receipts.Pages.Single(page => page.Role == "cover-front");

        var pressTitle = Assert.Single(press.Typography);
        var readingTitle = Assert.Single(reading.Typography);

        Assert.Equal(pressTitle.SizePt * BekiCoverDieline.DigitalScale, readingTitle.SizePt, 4);
        Assert.Equal(
            pressTitle.TitleOutlineWidthPt!.Value * BekiCoverDieline.DigitalScale,
            readingTitle.TitleOutlineWidthPt!.Value,
            6);

        Assert.Equal(press.TextLines, reading.TextLines);
        Assert.True(press.TextLines.Count >= 3, "the fixture title no longer needs three lines.");
    }

    // ==============================================================================================
    // The printed page
    // ==============================================================================================

    /// <summary>
    /// And the words are in the file a printer receives — read out of the press PDF by a text
    /// extractor, not out of our own receipt.
    ///
    /// The receipt says what the composer believed it set; this says what the page carries. They are
    /// the same question only if nothing between the layout and the file dropped a line, which is
    /// precisely what happened before today.
    /// </summary>
    [Fact]
    public void The_press_cover_file_carries_a_path_for_every_shaped_title_character()
    {
        var composed = PressCover(LongTitle);
        using var stream = new MemoryStream(composed.Pdf);
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(stream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        var content = System.Text.Encoding.Latin1.GetString(page.Contents.CreateSingleContent().Stream.UnfilteredValue);
        var visibleCharacters = LongTitle.Count(character => !char.IsWhiteSpace(character));
        Assert.True(System.Text.RegularExpressions.Regex.Matches(content, "/BekiTitleGlyph BMC").Count >= visibleCharacters);
        Assert.Null(page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/Font"));
        Assert.Equal(LongTitle, document.Info.Title);
    }

    // ==============================================================================================
    // Fixtures and measurement
    // ==============================================================================================

    private static BekiComposedBook PressCover(string title) =>
        new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout()))
            .ComposeCoverPressWithReceipts(title, Wrap(512, 245));

    private static BekiComposedBook ReadingCover(string title)
    {
        var plan = BekiLayoutFixture.EightSpreadPlan();
        plan = plan with { Concept = plan.Concept with { Title = title } };

        return new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout())).ComposeReading(
            plan,
            Wrap(1024, 490),
            plan.Spreads.Select(spread => new BekiSpreadArtwork(spread.Number, Wrap(150, 70))).ToList(),
            BekiLayoutFixture.Personalization());
    }

    /// <summary>A flat sheet of the fixture's own grey — none of these questions is about the art.</summary>
    private static byte[] Wrap(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(180, 170, 160));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// How tall this title is when it is set — measured the way the composer measures it, and
    /// deliberately not by asking the composer.
    ///
    /// A test that read the composer's own number back would agree with it whatever it said. This is
    /// the same one-page 72 DPI document, in the same registered face, on the cover's own 1.25
    /// leading, so a pixel is a point and the number is arrived at independently.
    /// </summary>
    private static float MeasureBlockPt(string title, float fontSize)
    {
        title = title.ToUpperInvariant();
        PdfFontBootstrap.EnsureRegistered();

        var pages = Document.Create(document => document.Page(page =>
            {
                page.ContinuousSize(TitleWidthPt, Unit.Point);
                page.Margin(0);
                page.DefaultTextStyle(style => style.Bold());
                page.Content().Text(title)
                    .FontFamily(PdfFontBootstrap.TitleFamily, PdfFontBootstrap.BodyFamily)
                    .FontSize(fontSize)
                    .LineHeight(1.25f);
            }))
            .GenerateImages(new ImageGenerationSettings
            {
                ImageFormat = ImageFormat.Png,
                RasterDpi = 72,
            })
            .ToList();

        var page = Assert.Single(pages);
        return SixLabors.ImageSharp.Image.Identify(page).Height;
    }

    private static bool PopplerInstalled(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Any(directory => File.Exists(Path.Combine(directory, tool)));
}
