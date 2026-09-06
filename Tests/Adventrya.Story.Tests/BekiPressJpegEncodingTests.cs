using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The press file is encoded exactly once, at quality 95, with all of its chroma.
///
/// The 2026-09-06 decision record (<c>BEKI_Print_Prep_Deterministic_Normalization_v1.md</c>) moved
/// the judgement about enlargement out of the composer and left it one job it must do exactly right:
/// take a raster that already measures the locked sheet and put it on the page without touching a
/// pixel of it, as one JPEG. Three things could quietly break that, and each of them has a test here:
///
/// * ImageSharp chooses 4:2:0 chroma subsampling below quality 91, so the previous quality-90 setting
///   was throwing away three quarters of the colour resolution of a file whose whole purpose is to be
///   printed at 300 PPI. The sampling factors are read out of the JPEG's own SOF marker rather than
///   from a metadata property, because the marker is what a printer's RIP reads.
/// * The sheet's ratio does not divide evenly — 2480 × (450 ÷ 210) rounds to 5314, not 5315 — so the
///   crop used to take one column off an exact-size raster and the normalizer resized it back up.
/// * Skipping the crop must not skip the encode. A press page that placed a PNG straight through
///   would be a raster nobody had stamped with 300 PPI or a colour profile, so the composed book is
///   reopened and the actual embedded object is compared, byte for byte, with what the composer's
///   encoder produces from the untouched artwork.
///
/// The reading copy is checked too, from the other side: it must NOT have followed the press file up
/// to 95, because audit P2-1 rejected that download for its size.
/// </summary>
public class BekiPressJpegEncodingTests
{
    /// <summary>The locked interior raster: 450 × 210 mm at 300 PPI.</summary>
    private const int InteriorWidthPx = 5315;

    private const int InteriorHeightPx = 2480;

    /// <summary>The locked cover wrap: the dieline's whole 512 × 245 mm canvas at 300 PPI.</summary>
    private const int CoverWidthPx = 6047;

    private const int CoverHeightPx = 2894;

    private const int PressQuality = 95;

    private const int ScreenQuality = 90;

    /// <summary>
    /// What a JPEG 95 4:4:4 round trip is allowed to cost, in levels of mean absolute difference.
    ///
    /// A fidelity claim, not a resampling detector: the measured cost of this encode on the fixture
    /// artwork is about 0.44 levels of 255, and a one-pixel crop-and-resize on top of it costs 0.90
    /// — closer than any tolerance could tell apart. What proves nothing was resampled is the byte
    /// equality asserted against the composer's own encoder output.
    /// </summary>
    private const double EncodeTolerance = 1.5d;

    private static BekiPrintLayoutOptions PressLayout() => new();

    private static BekiPdfComposer.PrintRasterTarget InteriorTarget =>
        new(InteriorWidthPx, InteriorHeightPx, 300, PressQuality);

    private static BekiPdfComposer.PrintRasterTarget CoverTarget =>
        new(CoverWidthPx, CoverHeightPx, 300, PressQuality);

    /// <summary>
    /// <see cref="BekiPdfComposer.NormalizeForPrint"/> on the fixture artwork, encoded once.
    ///
    /// The encoder is a pure function of its two arguments, and two tests here ask it for the SAME
    /// press raster — a thirteen-megapixel decode and a quality-95 4:4:4 encode, done twice for one
    /// answer. Remembering it changes nothing about what is asserted: the byte equality below still
    /// holds the composed page against what the composer's own encoder produces from the untouched
    /// artwork, and this IS that output.
    /// </summary>
    private static byte[] PressEncoded(int width, int height, BekiPdfComposer.PrintRasterTarget target) =>
        EncodedCache.GetOrAdd((width, height, target), key =>
            BekiPdfComposer.NormalizeForPrint(Structured(key.Width, key.Height), key.Target));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        (int Width, int Height, BekiPdfComposer.PrintRasterTarget Target), byte[]> EncodedCache = new();

    // ==============================================================================================
    // The encode itself
    // ==============================================================================================

    /// <summary>
    /// Every press raster the composer writes is a JPEG at 95 with three components at 1 × 1.
    ///
    /// 1 × 1 on all three is 4:4:4 — no chroma subsampling — and it is asserted off the frame header
    /// the decoder in a press RIP reads, not off ImageSharp's own opinion of the file it just made.
    /// </summary>
    [Fact]
    public void A_press_raster_is_one_jpeg_at_quality_95_with_no_chroma_subsampling()
    {
        var normalized = PressEncoded(1536, 717, InteriorTarget);

        var frame = ReadJpegFrame(normalized);
        Assert.Equal(InteriorWidthPx, frame.Width);
        Assert.Equal(InteriorHeightPx, frame.Height);
        Assert.Equal(3, frame.Sampling.Count);
        Assert.All(frame.Sampling, factors =>
        {
            Assert.Equal(1, factors.Horizontal);
            Assert.Equal(1, factors.Vertical);
        });

        var identified = Image.Identify(normalized);
        Assert.Equal(PressQuality, identified.Metadata.GetJpegMetadata().Quality);
        Assert.Equal(300, identified.Metadata.HorizontalResolution, 1);
        Assert.NotNull(identified.Metadata.IccProfile);
    }

    /// <summary>
    /// A raster that arrives at the exact press size is encoded, and nothing else happens to it.
    ///
    /// The dimensions come back unchanged, the receipt says nothing was resampled, and the decoded
    /// picture is the one that went in. That last clause is about the encode's fidelity rather than
    /// about resampling — a near-identity resize costs well under a level, so what proves the crop
    /// was skipped is the byte equality asserted on the composed book below.
    /// </summary>
    [Fact]
    public void A_raster_already_at_the_press_size_is_encoded_but_never_resampled()
    {
        var source = Structured(InteriorWidthPx, InteriorHeightPx);

        var normalized = PressEncoded(InteriorWidthPx, InteriorHeightPx, InteriorTarget);

        var identified = Image.Identify(normalized);
        Assert.Equal(InteriorWidthPx, identified.Width);
        Assert.Equal(InteriorHeightPx, identified.Height);
        Assert.True(
            MeanAbsoluteDifference(source, normalized) < EncodeTolerance,
            "An exact-size raster must survive the press encode unresampled.");

        var provenance = BekiPdfComposer.RasterProvenance(
            "spread-01", source, normalized, maxSilentUpscale: 1.05f);

        Assert.Equal("none", provenance.Resampler);
        Assert.False(provenance.Interpolated);
        Assert.Equal(1d, provenance.Factor, 4);
    }

    // ==============================================================================================
    // The one-pixel rule
    // ==============================================================================================

    /// <summary>
    /// The rounding that used to cost an exact raster a resample, stated as the rule it now is.
    ///
    /// 5315 × 2480 and 1536 × 717 are both "the sheet" — the first exactly, the second to within the
    /// rounding of a much smaller picture — and a square or a 3:2 render is not, which is what the
    /// crop tolerance downstream exists to refuse.
    /// </summary>
    [Fact]
    public void One_pixel_of_rounding_is_not_a_crop_and_a_real_mismatch_still_is()
    {
        const float sheet = 450f / 210f;

        Assert.True(BekiPdfComposer.AlreadyAtRatio(InteriorWidthPx, InteriorHeightPx, sheet));
        Assert.True(BekiPdfComposer.AlreadyAtRatio(1536, 717, sheet));
        Assert.True(BekiPdfComposer.AlreadyAtRatio(1701, 794, sheet));

        // A square render loses three tenths of its height to this sheet, and a 3:2 render loses a
        // quarter. Neither may be waved through as "already at ratio".
        Assert.False(BekiPdfComposer.AlreadyAtRatio(1200, 1200, sheet));
        Assert.False(BekiPdfComposer.AlreadyAtRatio(1536, 1024, sheet));
    }

    /// <summary>
    /// And the short raster still takes the path it always took: enlarged to the sheet, and declared.
    ///
    /// The one-pixel rule is about not cropping; it changes nothing about what the normalizer then
    /// does with a picture that is genuinely smaller than the sheet. Composed at the fixture's 96 PPI
    /// because the question is about the receipt, and a receipt is the same shape at any density.
    /// </summary>
    [Fact]
    public void A_short_raster_is_still_enlarged_to_the_sheet_and_says_so()
    {
        var layout = new BekiPrintLayoutOptions { PrintTargetPpi = 96, MaxPrintUpscale = 1.05f };
        var plan = BekiLayoutFixture.EightSpreadPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, Structured(1536, 717)))
            .ToList();

        var book = new BekiPdfComposer(Options.Create(layout))
            .ComposeInteriorWithReceipts(plan, spreads, BekiLayoutFixture.Personalization());

        var raster = Assert.Single(
            book.Receipts.Pages.Single(page => page.Role == "spread-01").Rasters!);

        Assert.Equal(1536, raster.SourceWidthPx);
        Assert.Equal(717, raster.SourceHeightPx);
        Assert.Equal(1701, raster.DeliveredWidthPx);   // 450 mm at 96 PPI
        Assert.Equal(794, raster.DeliveredHeightPx);   // 210 mm at 96 PPI
        Assert.Equal("lanczos3", raster.Resampler);
        Assert.True(raster.Interpolated);
        Assert.Equal($"jpeg-q{PressQuality}-444", raster.Encoder);
    }

    // ==============================================================================================
    // The composed book
    // ==============================================================================================

    /// <summary>
    /// The whole canonical book, reopened: the pixels that arrived are the pixels on the page.
    ///
    /// This is the assertion the plan review asked for by name. Skipping the crop must not skip the
    /// encode, so the embedded object is inspected rather than the receipt believed: the cover wrap
    /// and the story spread are both <c>/DCTDecode</c> at exactly the locked dimensions, their
    /// sampling factors are 1 × 1, and the spread's decoded pixels match the artwork that went in.
    ///
    /// Composed once, at the real 300 PPI, because everything this test is about only exists at the
    /// locked sizes — a screen-proof sheet has no 5315 to round to 5314.
    /// </summary>
    [Fact]
    public void The_canonical_book_places_exact_rasters_as_one_jpeg_each()
    {
        var artwork = Structured(InteriorWidthPx, InteriorHeightPx);
        var wrap = Structured(CoverWidthPx, CoverHeightPx);
        var plan = BekiLayoutFixture.EightSpreadPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, artwork))
            .ToList();

        var book = new BekiPdfComposer(Options.Create(PressLayout())).ComposeCanonicalWithReceipts(
            plan, wrap, spreads, BekiLayoutFixture.Personalization());

        using var stream = new MemoryStream(book.Pdf);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        // Page 1 is the wrap; pages 2 and 3 are the endpaper and the intro, so spread 1 is page 4.
        var cover = SoleImage(pdf.Pages[0]);
        Assert.Equal("/DCTDecode", Filters(cover));
        Assert.Equal(CoverWidthPx, cover.Elements.GetInteger("/Width"));
        Assert.Equal(CoverHeightPx, cover.Elements.GetInteger("/Height"));
        Assert.Equal(PressEncoded(CoverWidthPx, CoverHeightPx, CoverTarget), cover.Stream.Value);

        var spreadRole = book.Receipts.Pages[3].Role;
        Assert.Equal("spread-01", spreadRole);

        var placed = SoleImage(pdf.Pages[3]);
        Assert.Equal("/DCTDecode", Filters(placed));
        Assert.Equal(InteriorWidthPx, placed.Elements.GetInteger("/Width"));
        Assert.Equal(InteriorHeightPx, placed.Elements.GetInteger("/Height"));

        // The embedded stream of a DCTDecode object IS the JPEG, so the file a printer opens can be
        // held against the bytes the composer's own encoder produced from the untouched artwork.
        //
        // Byte equality, and it is the whole point of this test. It says three things at once that
        // no looser comparison can: the page carries the composer's single encode (not a second one
        // performed by the PDF writer), that encode was fed the raster exactly as it arrived (a
        // one-pixel crop and a resample back would land on different bytes), and nothing between
        // here and the file touched it. A pixel tolerance would not have caught the crop — the
        // measured difference from a 5314-column round trip is under a level.
        var embedded = placed.Stream.Value;
        Assert.Equal(PressEncoded(InteriorWidthPx, InteriorHeightPx, InteriorTarget), embedded);

        var frame = ReadJpegFrame(embedded);
        Assert.Equal(InteriorWidthPx, frame.Width);
        Assert.All(frame.Sampling, factors =>
        {
            Assert.Equal(1, factors.Horizontal);
            Assert.Equal(1, factors.Vertical);
        });
        Assert.Equal(PressQuality, Image.Identify(embedded).Metadata.GetJpegMetadata().Quality);
        Assert.True(
            MeanAbsoluteDifference(artwork, embedded) < EncodeTolerance,
            "The spread on the printed page must be the raster that arrived, encoded once.");

        // And the receipts say the same thing the file does.
        var spreadRaster = Assert.Single(book.Receipts.Pages[3].Rasters!);
        Assert.Equal("none", spreadRaster.Resampler);
        Assert.Equal(InteriorWidthPx, spreadRaster.DeliveredWidthPx);
        Assert.Equal($"jpeg-q{PressQuality}-444", spreadRaster.Encoder);

        var coverRaster = Assert.Single(book.Receipts.Pages[0].Rasters!);
        Assert.Equal("cover-wrap", coverRaster.Role);
        Assert.Equal("none", coverRaster.Resampler);
        Assert.Equal($"jpeg-q{PressQuality}-444", coverRaster.Encoder);
    }

    /// <summary>
    /// A wrap that is not the locked size is placed exactly as it arrived, and the receipt says so.
    ///
    /// The composer does not quietly resample a cover to the size it wishes it had: a wrap of the
    /// wrong size is either a screen-proof fixture or a real defect, and a defect is for the press
    /// gate to measure in the output. What must never happen is the receipt claiming an encode that
    /// did not take place.
    /// </summary>
    [Fact]
    public void A_cover_wrap_that_is_not_the_locked_size_is_placed_untouched()
    {
        var wrap = Structured(1536, 735);

        var cover = new BekiPdfComposer(Options.Create(PressLayout()))
            .ComposeCoverPressWithReceipts("ნინო და მოჯადოებული ტყე", wrap);

        using var stream = new MemoryStream(cover.Pdf);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        var image = SoleImage(pdf.Pages[0]);
        Assert.Equal("/FlateDecode", Filters(image));
        Assert.Equal(1536, image.Elements.GetInteger("/Width"));
        Assert.Equal(735, image.Elements.GetInteger("/Height"));

        var raster = Assert.Single(cover.Receipts.Pages[0].Rasters!);
        Assert.Equal("cover-wrap", raster.Role);
        Assert.Equal("none", raster.Resampler);
        Assert.Equal("passthrough", raster.Encoder);
    }

    // ==============================================================================================
    // The reading copy
    // ==============================================================================================

    /// <summary>
    /// The download did not follow the press file up to 95.
    ///
    /// Its own setting, asserted separately, because the two numbers answer opposite questions: one
    /// is about paper and the other is about the 34 MB file audit P2-1 rejected.
    /// </summary>
    [Fact]
    public void The_reading_copy_keeps_its_own_jpeg_quality()
    {
        var layout = PressLayout();
        Assert.Equal(ScreenQuality, layout.ScreenAssetJpegQuality);
        Assert.Equal(PressQuality, layout.PrintAssetJpegQuality);

        var composer = new BekiPdfComposer(Options.Create(layout));

        // Above the 150-PPI ceiling on a 440 mm page (2598 px), so the reduction and the encode run.
        var reduced = composer.FitForScreen(Structured(3200, 1493), layout.SpreadWidthMm);

        var identified = Image.Identify(reduced);
        Assert.Equal(2598, identified.Width);
        Assert.Equal(ScreenQuality, identified.Metadata.GetJpegMetadata().Quality);
    }

    // ==============================================================================================
    // Helpers
    // ==============================================================================================

    /// <summary>One component's sampling factors, straight out of the frame header.</summary>
    private readonly record struct JpegComponent(int Horizontal, int Vertical);

    private sealed record JpegFrame(int Width, int Height, IReadOnlyList<JpegComponent> Sampling);

    /// <summary>
    /// The JPEG's own start-of-frame header, walked marker by marker.
    ///
    /// Hand-parsed on purpose. Whether a file is subsampled is a property of the bytes a press RIP
    /// reads, and asking the same library that wrote the file to describe it would prove only that
    /// the library agrees with itself.
    /// </summary>
    private static JpegFrame ReadJpegFrame(byte[] jpeg)
    {
        Assert.True(
            jpeg.Length > 4 && jpeg[0] == 0xFF && jpeg[1] == 0xD8,
            "These bytes do not start with the JPEG start-of-image marker.");

        var index = 2;
        while (index + 3 < jpeg.Length)
        {
            if (jpeg[index] != 0xFF)
            {
                index++;
                continue;
            }

            var marker = jpeg[index + 1];
            index += 2;

            // Fill bytes and the standalone markers carry no length of their own.
            if (marker == 0xFF)
            {
                index--;
                continue;
            }

            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD9)
            {
                continue;
            }

            var length = (jpeg[index] << 8) | jpeg[index + 1];

            // SOF0 baseline, SOF1 extended sequential, SOF2 progressive.
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                var payload = index + 2;
                var height = (jpeg[payload + 1] << 8) | jpeg[payload + 2];
                var width = (jpeg[payload + 3] << 8) | jpeg[payload + 4];
                var components = jpeg[payload + 5];

                var sampling = new List<JpegComponent>(components);
                for (var component = 0; component < components; component++)
                {
                    var factors = jpeg[payload + 6 + (component * 3) + 1];
                    sampling.Add(new JpegComponent(factors >> 4, factors & 0x0F));
                }

                return new JpegFrame(width, height, sampling);
            }

            // Entropy-coded data starts here and no frame header follows it.
            if (marker == 0xDA)
            {
                break;
            }

            index += length;
        }

        throw new Xunit.Sdk.XunitException("The JPEG carries no start-of-frame marker.");
    }

    /// <summary>The one image object on a page, refusing to guess if there is more than one.</summary>
    private static PdfDictionary SoleImage(PdfPage page)
    {
        var xObjects = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");
        Assert.NotNull(xObjects);

        var images = xObjects!.Elements.Keys
            .Select(key => xObjects.Elements.GetObject(key) as PdfDictionary)
            .Where(dictionary => dictionary?.Elements.GetName("/Subtype") == "/Image")
            .ToList();

        return Assert.Single(images)!;
    }

    /// <summary>The image's filter chain, as a name or a comma-joined array of them.</summary>
    private static string Filters(PdfDictionary image) => image.Elements["/Filter"] switch
    {
        PdfName name => name.Value,
        PdfArray array => string.Join(",", array.Elements.Select(item => (item as PdfName)?.Value)),
        _ => string.Empty,
    };

    /// <summary>
    /// Artwork a resampler cannot pass through unnoticed: two ramps and a fine band pattern.
    ///
    /// Flat colour would prove nothing — a stretched flat rectangle is the same flat rectangle — and
    /// pixel noise would fail a lossy encode for the wrong reason. Bands with a 64-pixel period are
    /// smooth enough for JPEG 95 to reproduce almost exactly and short enough that a one-pixel
    /// resample shifts them measurably.
    ///
    /// Cached: the press-size sheet is thirteen megapixels and several tests want the same one.
    /// </summary>
    private static byte[] Structured(int width, int height) =>
        StructuredCache.GetOrAdd((width, height), key =>
        {
            using var image = new Image<Rgba32>(key.Width, key.Height);

            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    var down = (byte)(y * 255L / Math.Max(1, accessor.Height - 1));

                    for (var x = 0; x < row.Length; x++)
                    {
                        var across = (byte)(x * 255L / Math.Max(1, row.Length - 1));
                        var band = (byte)(128 + (double)(60 * Math.Sin(x * Math.PI / 32d)));
                        row[x] = new Rgba32(across, down, band, byte.MaxValue);
                    }
                }
            });

            using var buffer = new MemoryStream();
            // Speed over bytes: PNG is lossless at every level, so the pixels the encoder under
            // test receives are the same either way, and deflating a thirteen-megapixel gradient
            // hard costs seconds for a file that is decoded again immediately.
            image.Save(buffer, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
            return buffer.ToArray();
        }).ToArray();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int Width, int Height), byte[]>
        StructuredCache = new();

    /// <summary>Mean absolute difference per colour channel, in levels of 255.</summary>
    private static double MeanAbsoluteDifference(byte[] expected, byte[] actual)
    {
        using var left = Image.Load<Rgba32>(expected);
        using var right = Image.Load<Rgba32>(actual);

        Assert.Equal(left.Width, right.Width);
        Assert.Equal(left.Height, right.Height);

        var total = 0L;

        // A row at a time. Thirteen megapixels through the two-dimensional indexer re-resolves the
        // row on every one of them; the sum is over the same pixels either way.
        left.ProcessPixelRows(right, (first, second) =>
        {
            for (var y = 0; y < first.Height; y++)
            {
                var top = first.GetRowSpan(y);
                var bottom = second.GetRowSpan(y);

                for (var x = 0; x < top.Length; x++)
                {
                    var a = top[x];
                    var b = bottom[x];
                    total += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                }
            }
        });

        return (double)total / ((long)left.Width * left.Height * 3);
    }
}
