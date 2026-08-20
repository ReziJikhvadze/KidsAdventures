using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp.PixelFormats;

namespace AdventurePacks.Api.Services.Story;

/// <summary>One finished spread: the picture, and the words that go over it.</summary>
public sealed record BekiSpreadArtwork(int SpreadNumber, byte[] Image);

public interface IBekiPdfComposer
{
    byte[] Compose(MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads);

    /// <summary>
    /// The same book as one image per page. For looking at: a PDF cannot be inspected by anything
    /// that does not already render PDFs, and a layout nobody can see is a layout nobody can fix.
    /// </summary>
    IReadOnlyList<byte[]> RenderPages(
        MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads);
}

/// <summary>
/// Sets a Beki-format book for print.
///
/// A separate composer from <see cref="Implementations.AdventurePdfService"/>, which keeps
/// printing A5 books exactly as it always has. The two formats do not differ in styling; they
/// differ in what a page *is*. The A5 book gives a picture its own leaf and the words the facing
/// one, so text never crosses artwork. This book has one illustration across the whole spread and
/// the story set over it, in the third the illustrator was told to leave quiet. Threading that
/// through one class would mean a branch at every decision it makes.
///
/// The spread is one PDF page here, not two. Printers impose the fold themselves and a spread
/// split into two files is a spread with a seam down the middle of the picture — which is the one
/// thing a continuous illustration exists to avoid.
/// </summary>
public sealed class BekiPdfComposer(IOptions<BekiPrintLayoutOptions> options) : IBekiPdfComposer
{
    private readonly BekiPrintLayoutOptions _layout = options.Value;

    /// <summary>Cream, and the same one the reader sets its pages on.</summary>
    private static readonly Color TextColor = Color.FromHex("#FFF8EB");

    /// <summary>
    /// The wash under the words. The illustrator was asked to leave the text third quiet, and
    /// mostly does, but "quiet" is sky and mist rather than a flat colour — so the type sits on a
    /// gentle darkening that follows the same edge, which costs nothing where the picture is
    /// already dark and saves the line where it is not.
    /// </summary>
    private const string TextWashInk = "0D071D";

    public byte[] Compose(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads) =>
        Build(plan, coverImage, spreads).GeneratePdf();

    public IReadOnlyList<byte[]> RenderPages(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads) =>
        Build(plan, coverImage, spreads)
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 96 })
            .ToList();

    private Document Build(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        PdfFontBootstrap.EnsureRegistered();

        var bySpread = plan.Spreads.ToDictionary(spread => spread.Number);

        return Document.Create(document =>
        {
            ComposeArtOnly(document, coverImage);

            foreach (var artwork in spreads.OrderBy(spread => spread.SpreadNumber))
            {
                if (!bySpread.TryGetValue(artwork.SpreadNumber, out var spread))
                {
                    // A picture with no words is still a page of the book; dropping it would
                    // silently shorten the story.
                    ComposeArtOnly(document, artwork.Image);
                    continue;
                }

                ComposeSpread(document, artwork.Image, spread);
            }
        });
    }

    /// <summary>The cover, and any spread whose text went missing: artwork to the bleed and nothing else.</summary>
    private void ComposeArtOnly(IDocumentContainer container, byte[] image)
    {
        container.Page(page =>
        {
            ApplyGeometry(page);
            page.Content().Image(image).FitUnproportionally();
        });
    }

    private void ComposeSpread(IDocumentContainer container, byte[] image, StorySpread spread)
    {
        var textSide = Prompts.BekiSpreadRhythm.TextSideFor(spread.Number);
        var textOnLeft = textSide.Equals("left", StringComparison.OrdinalIgnoreCase);

        container.Page(page =>
        {
            ApplyGeometry(page);

            page.Content().Layers(layers =>
            {
                // The picture was generated at the sheet's own proportions, so filling the frame
                // is exact rather than a stretch.
                layers.PrimaryLayer().Image(image).FitUnproportionally();

                // The wash runs wider than the text column so it has somewhere to fade out; a
                // gradient that ends where the words end is a panel with a soft edge, not a wash.
                // One shape across the whole sheet, with the fade written into the gradient's own
                // stops. Splitting it into a row of columns meant relying on the layout engine to
                // stretch an SVG that has no intrinsic height, and it did not: the wash ended
                // partway down the page in a straight horizontal line across the artwork. Nothing
                // here depends on measurement any more.
                layers.Layer().Extend().Image(WashImage(textOnLeft, _layout.TextColumnShare))
                    .FitUnproportionally();

                layers.Layer().Row(row =>
                {
                    if (!textOnLeft) row.RelativeItem(1f - _layout.TextColumnShare);

                    row.RelativeItem(_layout.TextColumnShare)
                        .Padding(_layout.SafeMarginMm, Unit.Millimetre)
                        .AlignMiddle()
                        .Column(column =>
                        {
                            column.Spacing(10);

                            column.Item().Text(spread.Text)
                                .FontFamily(PdfFontBootstrap.BodyFamily)
                                .FontSize(_layout.StoryFontSize)
                                .LineHeight(1.55f)
                                .FontColor(TextColor);

                            if (_layout.PrintEnglishToo && !string.IsNullOrWhiteSpace(spread.TextEn))
                            {
                                column.Item().Text(spread.TextEn!)
                                    .FontFamily(PdfFontBootstrap.BodyFamily)
                                    .FontSize(_layout.StoryFontSize * 0.82f)
                                    .LineHeight(1.5f)
                                    .FontColor(Color.FromHex("#FFF8EBB0"));
                            }
                        });

                    if (textOnLeft) row.RelativeItem(1f - _layout.TextColumnShare);
                });
            });
        });
    }

    /// <summary>
    /// The darkening under the words, as one gradient.
    ///
    /// It was thirty-two adjacent rectangles of increasing alpha, which is how you fake a gradient
    /// when you have only solid fills — and it printed hairlines. Each pair of rectangles meets on
    /// a fractional pixel, the renderer rounds both edges the same way, and the page colour shows
    /// through the seam: thin dark stripes down the artwork, exactly like a border nobody asked
    /// for. An SVG gradient is one shape with no seams to leave.
    ///
    /// The curve is deliberately not linear. A straight ramp is either too weak where the words
    /// are or too heavy where the picture is; easing it keeps the outer edge dark enough to read
    /// on and lets the middle clear early, so the illustration is not dimmed across a third of
    /// its width.
    /// </summary>
    /// <summary>
    /// The wash, drawn as pixels.
    ///
    /// Third attempt, and the first that works. Thirty-two adjacent rectangles left hairline seams
    /// where their edges met — thin dark stripes down the artwork, exactly like a border. An SVG
    /// linear gradient has no seams to leave, but QuestPDF draws SVG through Skia's parser and
    /// that parser ignores <c>linearGradient</c>: the element rendered as nothing at all, which
    /// looked like a wash that was merely too weak until the alpha was doubled and nothing changed.
    ///
    /// A raster image has neither problem. One row of pixels is enough — the sheet stretches it —
    /// and the ramp is computed rather than approximated, so there is nothing to seam and nothing
    /// for a parser to decline.
    /// </summary>
    private static byte[] WashImage(bool textOnLeft, float textColumnShare)
    {
        var end = Math.Min(0.92f, textColumnShare * 1.7f);
        const int width = 1024;

        using var image = new SixLabors.ImageSharp.Image<Rgba32>(width, 2);
        var ink = Convert.ToInt32(TextWashInk, 16);
        var (r, g, b) = ((byte)(ink >> 16), (byte)((ink >> 8) & 0xFF), (byte)(ink & 0xFF));

        for (var x = 0; x < width; x++)
        {
            // Measured from the text edge, whichever side that is.
            var t = (x + 0.5f) / width;
            var distance = textOnLeft ? t : 1f - t;
            var alpha = (byte)MathF.Round(AlphaAt(distance, textColumnShare, end) * 255f);

            image[x, 0] = new Rgba32(r, g, b, alpha);
            image[x, 1] = new Rgba32(r, g, b, alpha);
        }

        using var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return buffer.ToArray();
    }

    /// <summary>
    /// Nearly full strength across the whole text column, then away.
    ///
    /// A ramp that has already begun fading where the last line sits fails on exactly the line a
    /// child is still reading, so the falloff starts where the words stop rather than where they
    /// start. White type over sunlit green is the case that decides this, not a night scene.
    /// </summary>
    private static float AlphaAt(float distance, float textColumnShare, float end)
    {
        const float peak = 0.92f;

        if (distance <= textColumnShare * 0.7f) return peak;

        if (distance <= textColumnShare)
        {
            var t = (distance - (textColumnShare * 0.7f)) / (textColumnShare * 0.3f);
            return peak - (t * (peak - 0.70f));
        }

        if (distance >= end) return 0f;

        // Eased out, so the edge of the wash is never a line the eye can find.
        var fade = (distance - textColumnShare) / (end - textColumnShare);
        return 0.70f * (1f - (fade * fade));
    }


    private void ApplyGeometry(PageDescriptor page)
    {
        page.Size(new PageSize(
            _layout.SpreadWidthMm + (_layout.BleedMm * 2),
            _layout.SpreadHeightMm + (_layout.BleedMm * 2),
            Unit.Millimetre));

        page.Margin(0);
        page.PageColor(Color.FromHex("#281B3F"));
    }
}
