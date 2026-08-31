using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Ai;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The centre-column seam gate: what counts as a painted seam, what is repaired, and what is
/// deliberately left alone.
///
/// One of the classes CompositePipelineTestBase serves; see it for the fixtures these use.
/// </summary>
public class CompositePipelineSeamTests : CompositePipelineTestBase
{
    // ---------------------------------------------------------------------------------------
    // The centre-column seam gate
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A picture with a painted centre line is measured, repaired and measured again; a picture
    /// without one is left exactly as it was.
    ///
    /// The second half is the important half. The de-folding removed the cause and this catches the
    /// residue, so it runs on every base of every book — and a gate that smeared four columns of a
    /// picture whose centre happens to hold a tree would be a defect this code introduced.
    /// </summary>
    [Fact]
    public void A_painted_centre_seam_is_measured_and_interpolated_away()
    {
        var clean = Gradient(1536, 717);
        var seamed = WithSeam(clean, columns: 2, darken: 90);

        var before = CompositeSeamRepair.Measure(seamed);
        Assert.True(before.Exceeded, $"the synthetic seam measured only {before.Ratio:F1}x.");
        Assert.InRange(before.ColumnCount, 1, CompositeSeamRepair.MaxRepairColumns);

        var (repaired, measuredBefore, after) = CompositeSeamRepair.Gate(seamed);

        Assert.True(measuredBefore.Exceeded);
        Assert.False(after.Exceeded, $"the seam still measures {after.Ratio:F1}x after the repair.");
        Assert.True(after.Ratio < before.Ratio / 2);
        Assert.NotEqual(seamed, repaired);

        // The repair is local: everything outside the repaired columns is untouched.
        using var original = Image.Load<Rgba32>(seamed);
        using var fixedUp = Image.Load<Rgba32>(repaired);

        Assert.Equal(original.Width, fixedUp.Width);
        Assert.Equal(original.Height, fixedUp.Height);

        var changed = 0;
        for (var x = 0; x < original.Width; x++)
        {
            if (original[x, 10] != fixedUp[x, 10])
            {
                changed++;
                Assert.InRange(x, measuredBefore.FirstColumn, measuredBefore.LastColumn);
            }
        }

        Assert.InRange(changed, 1, CompositeSeamRepair.MaxRepairColumns);
    }

    /// <summary>
    /// A seam that is not at the exact centre is still a seam.
    ///
    /// The refused image carried a visible vertical band at about 52.5% of the width — some forty
    /// columns out on a spread — and the gate, which scanned three columns either side of centre,
    /// reported nothing wrong with it. The band is now four per cent of the width, and the offset
    /// is recorded so the next one can be read off a log rather than measured by hand.
    /// </summary>
    [Fact]
    public void A_seam_at_fifty_two_and_a_half_per_cent_is_found_and_repaired()
    {
        const int width = 1536;
        var offCentreColumn = (int)Math.Round(width * 0.525);

        var seamed = WithSeam(Gradient(width, 717), columns: 3, darken: 90, atColumn: offCentreColumn);

        var before = CompositeSeamRepair.Measure(seamed);

        Assert.True(before.Exceeded, $"the 52.5% seam measured only {before.Ratio:F1}x.");
        Assert.InRange(before.ColumnCount, 1, CompositeSeamRepair.MaxRepairColumns);

        // The offset is recorded, and it is where the seam actually is — a couple of per cent
        // right of centre, well outside the three-column window the gate used to scan.
        Assert.InRange(before.OffsetFraction, 0.020, 0.030);
        Assert.InRange(before.FirstColumn, offCentreColumn - 4, offCentreColumn + 4);

        var (repaired, _, after) = CompositeSeamRepair.Gate(seamed);

        Assert.False(after.Exceeded, $"the seam still measures {after.Ratio:F1}x after the repair.");
        Assert.NotEqual(seamed, repaired);
    }

    /// <summary>
    /// A wider band — up to the eight columns the gate now allows — is repaired; one wider than
    /// that is a structure and is left alone rather than trimmed to fit.
    /// </summary>
    [Fact]
    public void A_band_up_to_eight_columns_is_repaired_and_a_wider_one_is_not()
    {
        var eight = WithSeam(Gradient(1536, 717), columns: 8, darken: 90);
        var measured = CompositeSeamRepair.Measure(eight);

        Assert.True(measured.Exceeded, $"an eight-column seam measured only {measured.Ratio:F1}x.");
        Assert.Equal(8, measured.ColumnCount);

        var (_, _, after) = CompositeSeamRepair.Gate(eight);
        Assert.False(after.Exceeded);

        // Twelve columns is not a seam. Left alone: repairing part of a real feature and leaving
        // the rest would be a defect this gate introduced.
        Assert.False(CompositeSeamRepair.Measure(
            WithSeam(Gradient(1536, 717), columns: 12, darken: 90)).Exceeded);
    }

    /// <summary>
    /// The reserved text third's own boundary, at exactly 33% of the width, is never touched — even
    /// when it is a hard edge measuring far above the baseline.
    ///
    /// That edge is precisely the defect the prompt amendment addresses, and it is the one thing
    /// this gate must not "fix": it sits in the middle of the picture's content, a repair would
    /// smear eight columns of somebody's artwork, and the fix for it is wording. Outside the band
    /// by a wide margin, and the margin is the point.
    /// </summary>
    [Fact]
    public void The_text_zone_boundary_at_a_third_of_the_width_is_never_repaired()
    {
        const int width = 1536;
        var boundary = (int)Math.Round(width / 3.0);

        // A hard-edged flat panel exactly like the refused image's: everything left of the third
        // is a pale flat field, and the edge where it ends is severe.
        var panelled = WithSeam(Gradient(width, 717), columns: 3, darken: 120, atColumn: boundary);

        var measured = CompositeSeamRepair.Measure(panelled);

        Assert.False(
            measured.Exceeded,
            $"the 33% content edge was treated as a seam ({measured.Ratio:F1}x, columns "
            + $"{measured.FirstColumn}-{measured.LastColumn}).");

        var (unchanged, _, _) = CompositeSeamRepair.Gate(panelled);
        Assert.Same(panelled, unchanged);

        // It is outside the scanned band by a wide margin, which is why: the band reaches 4% either
        // side of centre and this edge is 17% away.
        Assert.True(
            Math.Abs((boundary / (double)width) - 0.5) > CompositeSeamRepair.CentreBandFraction * 3,
            "the text-zone boundary is closer to the scanned band than this test assumes.");
    }

    [Fact]
    public void A_picture_with_no_seam_is_returned_untouched()
    {
        var clean = Gradient(1536, 717);

        var (unchanged, before, after) = CompositeSeamRepair.Gate(clean);

        Assert.False(before.Exceeded);
        Assert.Same(clean, unchanged);
        Assert.Equal(before, after);
    }

    /// <summary>
    /// A strong vertical feature that is not at the centre does not trigger the gate, and neither
    /// does one wider than a seam. Both are pictures, and the gate's whole risk is treating a
    /// picture as a defect.
    /// </summary>
    [Fact]
    public void The_gate_leaves_real_vertical_features_alone()
    {
        var offCentre = WithSeam(Gradient(1536, 717), columns: 2, darken: 90, atColumn: 300);
        Assert.False(CompositeSeamRepair.Measure(offCentre).Exceeded);

        var wideBand = WithSeam(Gradient(1536, 717), columns: 40, darken: 90);
        var measured = CompositeSeamRepair.Measure(wideBand);
        Assert.True(
            !measured.Exceeded || measured.ColumnCount <= CompositeSeamRepair.MaxRepairColumns,
            "a wide band was treated as a repairable seam.");
    }

    /// <summary>
    /// And the gate runs inside the pipeline, on the base the reviewer judges and the compositor
    /// pastes onto — not afterwards, when the picture that was approved would no longer be the
    /// picture that ships.
    /// </summary>
    [Fact]
    public async Task The_pipeline_repairs_a_seam_before_the_reviewer_sees_the_page()
    {
        var images = new StubImageService
        {
            NextImage = WithSeam(Gradient(ProviderWidth, ProviderHeight), columns: 2, darken: 90),
        };

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        // What the provider returned had a seam; what the book kept does not.
        Assert.True(CompositeSeamRepair.Measure(images.Returned[0]).Exceeded);
        Assert.False(CompositeSeamRepair.Measure(result.Spreads[0].BasePng).Exceeded);

        // And the reviewer judged the repaired page, not the one that came back from the provider.
        Assert.False(CompositeSeamRepair.Measure(images.ReviewImages[0]).Exceeded);
    }

    // ---------------------------------------------------------------------------------------
    // The centre-field gate: the veil and the step the interpolating repair must decline
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The supplier's rejected book, in synthetic form: a milky veil over the whole text-side
    /// half, ending in a soft shoulder near the centre. Too wide for the interpolating repair by
    /// design — and exactly what the field reading exists to refuse.
    /// </summary>
    [Fact]
    public void A_half_canvas_veil_is_refused_by_the_field_reading()
    {
        var veiled = WithVeil(Gradient(1536, 717), leftSide: true, lift: 0.5, shoulderColumns: 24);

        var measured = CompositeSeamRepair.MeasureCentreField(veiled);

        Assert.True(
            measured.FieldCoverage >= CompositeSeamRepair.FieldCoverageLimit,
            $"the veil's field reading is only {measured.FieldCoverage:P0}.");
        Assert.True(measured.Exceeded);

        // The mirrored veil — text on the other side — is the same defect.
        var mirrored = WithVeil(Gradient(1536, 717), leftSide: false, lift: 0.5, shoulderColumns: 24);
        Assert.True(CompositeSeamRepair.MeasureCentreField(mirrored).Exceeded);
    }

    /// <summary>
    /// A hard half-to-half tone step has ONE elevated boundary, so the interpolating repair
    /// rightly declines it — there is no band between two edges to paint across. It used to
    /// decline in silence; now the measurement says so and the edge reading refuses the picture.
    /// </summary>
    [Fact]
    public void A_hard_step_at_the_centre_is_declined_by_the_repair_and_refused_by_the_edge_reading()
    {
        var stepped = WithVeil(Gradient(1536, 717), leftSide: true, lift: 0.25, shoulderColumns: 0);

        var seam = CompositeSeamRepair.Measure(stepped);
        Assert.True(seam.DeclinedRepair, $"the step measured {seam.Ratio:F1}x but was not declined.");
        Assert.False(seam.Exceeded);

        var field = CompositeSeamRepair.MeasureCentreField(stepped);
        Assert.True(
            field.EdgeCoverage >= CompositeSeamRepair.EdgeCoverageLimit,
            $"the step's edge reading is only {field.EdgeCoverage:P0}.");
        Assert.True(field.Exceeded);
    }

    /// <summary>
    /// The advisory tier: a reading past the ordinary limits but short of severe warns and
    /// changes nothing. The first live v1.5 book is the reason the tier exists — a clean page
    /// measured within a few points of the old single-tier limit, and its sibling was refused
    /// twice over what the evidence says was composition, killing a paid order.
    /// </summary>
    [Fact]
    public void A_borderline_reading_warns_and_is_not_refused()
    {
        // A faint one-way lift: enough to push the field reading past its advisory limit (a 0.15
        // lift moves the wide strips ~21 luma against the 15-luma row step), far too gentle for
        // the razor edge that makes a reading severe — the 24-column shoulder spreads the step to
        // ~7 luma per adjacent strip, under the edge reading's 12.
        var faint = WithVeil(Gradient(1536, 717), leftSide: true, lift: 0.15, shoulderColumns: 24);

        var measured = CompositeSeamRepair.MeasureCentreField(faint);
        Assert.True(measured.Exceeded, $"the faint lift only measured {measured.FieldCoverage:P0}.");
        Assert.False(measured.Severe);

        Assert.Empty(CompositeDeterministicChecks.CentreFieldProblems(faint));
        Assert.NotNull(CompositeDeterministicChecks.CentreFieldWarning(faint));
    }

    /// <summary>
    /// The readings must pass real pictures: a clean gradient, a repaired narrow seam, and a
    /// legitimately asymmetric composition whose halves differ without a centre-aligned boundary.
    /// The gate's whole risk is treating a picture as a defect — a false positive here spends a
    /// paid image call on artwork that was fine.
    /// </summary>
    [Fact]
    public void Clean_and_repaired_pictures_pass_the_centre_field_gate()
    {
        Assert.False(CompositeSeamRepair.MeasureCentreField(Gradient(1536, 717)).Exceeded);

        // A narrow painted seam is the interpolating repair's job; after it has done that job,
        // the field gate must agree the picture is whole.
        var (repaired, before, _) = CompositeSeamRepair.Gate(
            WithSeam(Gradient(1536, 717), columns: 2, darken: 90));
        Assert.True(before.Exceeded);
        Assert.False(CompositeSeamRepair.MeasureCentreField(repaired).Exceeded);

        // A frame too small for the strips is a fixture, not a spread: measured as nothing.
        Assert.False(CompositeSeamRepair.MeasureCentreField(SyntheticImages.SolidPng(64, 64)).Exceeded);
    }

    /// <summary>
    /// A veiled base spends the spread's one regeneration before any review is bought, and the
    /// book ships on the clean redraw. The budget is shared with the reviewer's ladder — one
    /// extra picture per spread, never two.
    /// </summary>
    [Fact]
    public async Task A_veiled_base_buys_one_redraw_and_the_book_ships_the_clean_one()
    {
        var images = new StubImageService();
        images.QueuedImages.Enqueue(
            WithVeil(Gradient(ProviderWidth, ProviderHeight), leftSide: true, lift: 0.5, shoulderColumns: 24));

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        // The veiled picture went back; the page that shipped is the redraw, and it is clean.
        Assert.Equal(2, result.Spreads[0].BaseAttempts);
        Assert.False(CompositeSeamRepair.MeasureCentreField(result.Spreads[0].BasePng).Exceeded);

        // The reviewer never saw the veiled picture — the gate is cheaper than a review and runs
        // first.
        Assert.All(
            images.ReviewImages,
            reviewed => Assert.False(CompositeSeamRepair.MeasureCentreField(reviewed).Exceeded));
    }

    /// <summary>
    /// A redraw that comes back veiled again stops the spread as IMAGE_GENERATION_FAILED. Two
    /// pictures is the budget; a third would be paying to fail differently.
    /// </summary>
    [Fact]
    public async Task A_veiled_redraw_stops_the_spread_rather_than_shipping()
    {
        var veiled = WithVeil(
            Gradient(ProviderWidth, ProviderHeight), leftSide: true, lift: 0.5, shoulderColumns: 24);

        var images = new StubImageService();
        images.QueuedImages.Enqueue(veiled);
        images.QueuedImages.Enqueue(veiled);

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageGenerationFailed, failure.FailureCode);
        Assert.Contains("centre-field gate", failure.Message);

        // Spread one is drawn first and alone, so the stop cost exactly two pictures and zero
        // reviews.
        Assert.Equal(2, images.ImageCalls);
        Assert.Empty(images.ReviewImages);
    }

    /// <summary>
    /// The rejected book's veil, applied to a synthetic canvas the way the audit measured it:
    /// lighter on the text side, alternating with the page rhythm. Built here so the constants'
    /// calibration against the stored real bases has a checked-in stand-in.
    /// </summary>
    private static byte[] WithVeil(byte[] png, bool leftSide, double lift, int shoulderColumns)
    {
        using var image = Image.Load<Rgba32>(png);
        var half = image.Width / 2;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var distanceIntoVeil = leftSide ? half - x : x - half + 1;

                    var factor = distanceIntoVeil <= 0
                        ? 0
                        : shoulderColumns > 0 && distanceIntoVeil <= shoulderColumns
                            ? lift * distanceIntoVeil / shoulderColumns
                            : lift;

                    if (factor <= 0)
                    {
                        continue;
                    }

                    var pixel = row[x];
                    row[x] = new Rgba32(
                        (byte)Math.Round(pixel.R + ((255 - pixel.R) * factor)),
                        (byte)Math.Round(pixel.G + ((255 - pixel.G) * factor)),
                        (byte)Math.Round(pixel.B + ((255 - pixel.B) * factor)),
                        pixel.A);
                }
            }
        });

        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }
}
