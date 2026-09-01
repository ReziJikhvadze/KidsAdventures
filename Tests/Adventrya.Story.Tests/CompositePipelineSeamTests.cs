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
/// The centre-column seam gate: what counts as a painted seam, what is repaired, what is
/// deliberately left alone — and, since audit-2 P0-05, what stops a book.
///
/// Two instruments, and they answer different questions. The interpolating repair paints out a
/// one-to-eight column band and refuses to touch anything wider, so a milky veil over half a page
/// walks past it by construction. The centre-field reading measures whether the two sides of the
/// fold agree at all; it repairs nothing, and it is now the gate that buys a redraw and then stops
/// the run. The fixtures the two tiers were calibrated on are kept whole below, because the
/// thresholds have been re-judged once and will be again.
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
    /// The advisory tier still classifies the way it was calibrated to — and it is now the tier
    /// that blocks.
    ///
    /// Both halves matter. The fixture is the calibration: a faint one-way lift measures past the
    /// advisory limits and well short of severe, which is the distinction the two tiers were cut
    /// on and the log lines are still written in. What changed is the consequence. Audit-2 P0-05
    /// demanded an automated centerline test, so the ADVISORY pair is what the pipeline acts on;
    /// severe no longer selects a different road, because there is only one road left.
    /// </summary>
    [Fact]
    public void A_borderline_reading_is_advisory_by_tier_and_blocking_by_ruling()
    {
        // A faint one-way lift: enough to push the field reading past its advisory limit (a 0.15
        // lift moves the wide strips ~21 luma against the 15-luma row step), far too gentle for
        // the razor edge that makes a reading severe — the 24-column shoulder spreads the step to
        // ~7 luma per adjacent strip, under the edge reading's 12.
        var faint = WithVeil(Gradient(1536, 717), leftSide: true, lift: 0.15, shoulderColumns: 24);

        var measured = CompositeSeamRepair.MeasureCentreField(faint);
        Assert.True(measured.Exceeded, $"the faint lift only measured {measured.FieldCoverage:P0}.");
        Assert.False(measured.Severe);

        // The tier classifier is unchanged, which is what keeps the two thresholds comparable to
        // every number already in the logs.
        Assert.Empty(CompositeDeterministicChecks.CentreFieldProblems(faint));
        Assert.NotNull(CompositeDeterministicChecks.CentreFieldWarning(faint));
    }

    /// <summary>
    /// And the same borderline page, put through the pipeline, now costs a picture: the advisory
    /// pair is the blocking pair, so a base that crosses it spends the page's one regeneration
    /// before anything is reviewed.
    /// </summary>
    [Fact]
    public async Task A_borderline_base_spends_the_pages_one_regeneration()
    {
        var faint = WithVeil(
            Gradient(ProviderWidth, ProviderHeight), leftSide: true, lift: 0.15,
            shoulderColumns: 24);

        Assert.False(CompositeSeamRepair.MeasureCentreField(faint).Severe);

        var images = new StubImageService();
        images.QueuedImages.Enqueue(faint);

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(2, result.Spreads[0].BaseAttempts);
        Assert.False(CompositeSeamRepair.MeasureCentreField(result.Spreads[0].BasePng).Exceeded);
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

    // ---------------------------------------------------------------------------------------
    // The centre-fold gate blocks again — audit-2 P0-05
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A veiled base buys the page's one regeneration, and the clean second picture is the one
    /// that goes to the reviewer and into the book.
    ///
    /// This test replaces the one that pinned the opposite. From 2026-08-31 the measurement was
    /// telemetry-only — its first live outing had refused a clean page twice and stopped a paid
    /// order — and the supplier then rejected a shipped book for "an abnormal pixel jump exactly
    /// at x=1264/1265, the 50% fold coordinate" on five of eight spreads, naming an automated
    /// centerline test as the correction. The reversal is affordable because it is no longer a
    /// refusal on sight: a false positive costs one image call, where the old single-tier gate's
    /// cost an order.
    /// </summary>
    [Fact]
    public async Task A_veiled_base_buys_one_redraw_and_the_clean_second_ships()
    {
        var veiled = WithVeil(
            Gradient(ProviderWidth, ProviderHeight), leftSide: true, lift: 0.5, shoulderColumns: 24);

        var images = new StubImageService();
        images.QueuedImages.Enqueue(veiled);

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        // Nine pictures for eight spreads: the veiled first spread cost two, the rest one each.
        Assert.Equal(BookFormat.SpreadCount + 1, images.ImageCalls);
        Assert.Equal(2, result.Spreads[0].BaseAttempts);

        // The veil is not in the book, and it never reached the reviewer either — the gate runs
        // before the composite is built, so the refused picture was never judged or pasted onto.
        Assert.False(CompositeSeamRepair.MeasureCentreField(result.Spreads[0].BasePng).Exceeded);
        Assert.All(
            images.ReviewImages,
            judged => Assert.False(CompositeSeamRepair.MeasureCentreField(judged).Exceeded));
    }

    /// <summary>
    /// Two generations, both veiled, and the book stops — with the picture and the numbers, in the
    /// same two blobs a refused QA verdict leaves behind.
    ///
    /// The pair is the argument: one measurement past the limit is a picture, and two independent
    /// generations past it at the same place is a fold being painted. The evidence is what makes
    /// the gate answerable — the thresholds were re-judged once already, and the next time they
    /// are, the documents this writes are what they will be judged from.
    /// </summary>
    [Fact]
    public async Task A_base_veiled_twice_stops_the_book_with_the_picture_and_the_numbers()
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
        Assert.Equal(1, failure.Page);

        // Two pictures for the anchor spread and then nothing: the book stopped before it drew
        // the seven pages that would have been thrown away.
        Assert.Equal(2, images.ImageCalls);

        // The reviewer was never asked. A page that cannot pass this gate is not worth a verdict.
        Assert.Equal(0, images.ReviewCalls);

        var evidence = Assert.IsType<CompositeFailureEvidence>(failure.Evidence);
        Assert.Equal(1, evidence.Page);

        // The picture is the refused BASE — Beki was never composited onto it — and it still
        // measures as two halves, which is the point of storing it.
        Assert.True(CompositeSeamRepair.MeasureCentreField(evidence.CompositePng).Exceeded);

        using var document = JsonDocument.Parse(evidence.QaJson);
        var root = document.RootElement;

        Assert.Equal("centre_fold", root.GetProperty("gate").GetString());
        Assert.Equal("P0-05", root.GetProperty("audit_item").GetString());
        Assert.Equal(2, root.GetProperty("base_attempts").GetInt32());

        // Both readings, in order, both over the line — and the limits they were judged against,
        // so the document stands on its own when the thresholds move.
        var readings = root.GetProperty("readings").EnumerateArray().ToList();
        Assert.Equal(2, readings.Count);
        Assert.All(readings, reading => Assert.True(reading.GetProperty("exceeded").GetBoolean()));
        Assert.Equal(
            CompositeSeamRepair.FieldCoverageLimit,
            root.GetProperty("limits").GetProperty("field_coverage").GetDouble(),
            3);
    }

    /// <summary>
    /// A base bought by the REVIEWER is measured at the fold too — the way past the gate that a
    /// review found, and the only way through it that existed.
    ///
    /// The gate ran on the first base and on the redraw the gate itself bought, and stopped there.
    /// The two QA rungs spend the same one regeneration for a different reason — the reviewer wanted
    /// a different world, or moving Beki on the old one did not help — and their replacement went
    /// into the book without ever being measured. So a page whose first picture was clean at the
    /// fold and whose second was painted in half shipped, and P0-05's "automated centerline test"
    /// was, on that path, not run at all. Being asked for a redraw was a way around the gate.
    ///
    /// The terminal behaviour is the one the gate already had: the regeneration is spent, there is
    /// no third picture, and the book stops with the refused base and both readings beside it.
    /// </summary>
    [Fact]
    public async Task A_reviewer_requested_redraw_is_measured_at_the_fold_like_any_other_base()
    {
        var clean = Gradient(ProviderWidth, ProviderHeight);
        var veiled = WithVeil(
            Gradient(ProviderWidth, ProviderHeight), leftSide: true, lift: 0.5,
            shoulderColumns: 24);

        var images = new StubImageService();
        images.QueuedImages.Enqueue(clean);
        images.QueuedImages.Enqueue(veiled);

        // The first base passes the fold gate on sight; what buys the second picture is the
        // reviewer asking for a new world, which is the path that used to skip the measurement.
        images.Verdicts.Enqueue(Fail("MAIN_SCENE_BEAT", CompositeQaVerdict.ActionRegenerateBase));

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
                .RunAsync(Request(), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageGenerationFailed, failure.FailureCode);
        Assert.Equal(1, failure.Page);

        // Two pictures and one review: the clean first base was judged, the replacement never
        // reached the reviewer because it never got past the fold.
        Assert.Equal(2, images.ImageCalls);
        Assert.Equal(1, images.ReviewCalls);

        var evidence = Assert.IsType<CompositeFailureEvidence>(failure.Evidence);
        Assert.Equal(1, evidence.Page);
        Assert.True(CompositeSeamRepair.MeasureCentreField(evidence.CompositePng).Exceeded);

        using var document = JsonDocument.Parse(evidence.QaJson);
        var root = document.RootElement;

        // The same document the gate's own refusal writes — same gate name, same shape, so the
        // fulfilment job's evidence path needs to know nothing about which rung bought the picture.
        Assert.Equal("centre_fold", root.GetProperty("gate").GetString());
        Assert.Equal("P0-05", root.GetProperty("audit_item").GetString());
        Assert.Equal(2, root.GetProperty("base_attempts").GetInt32());

        // And the pair reads honestly: the base this page started from was clean, the one bought to
        // replace it was not. A document claiming two failed readings would be describing a
        // different failure.
        var readings = root.GetProperty("readings").EnumerateArray().ToList();
        Assert.Equal(2, readings.Count);
        Assert.False(readings[0].GetProperty("exceeded").GetBoolean());
        Assert.True(readings[1].GetProperty("exceeded").GetBoolean());
    }

    /// <summary>
    /// And the measurement does not turn a healthy redraw into a refusal: a reviewer-requested base
    /// that is clean at the fold goes on to be reviewed and shipped, at the cost of one picture.
    /// </summary>
    [Fact]
    public async Task A_clean_reviewer_requested_redraw_still_ships()
    {
        var images = new StubImageService();
        images.QueuedImages.Enqueue(Gradient(ProviderWidth, ProviderHeight));
        images.QueuedImages.Enqueue(Gradient(ProviderWidth, ProviderHeight));

        images.Verdicts.Enqueue(Fail("MAIN_SCENE_BEAT", CompositeQaVerdict.ActionRegenerateBase));

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);
        Assert.Equal(2, result.Spreads[0].BaseAttempts);
        Assert.False(CompositeSeamRepair.MeasureCentreField(result.Spreads[0].BasePng).Exceeded);
    }

    /// <summary>
    /// The rejected book's veil, applied to a synthetic canvas the way the audit measured it:
    /// lighter on the text side, alternating with the page rhythm. Built here so the constants'
    /// calibration against the stored real bases has a checked-in stand-in.
    ///
    /// Internal rather than private because <see cref="CompositePipelinePolicyTests"/> asks the same
    /// gate a different question — what a flagged policy does with a base that fails it twice — and
    /// a second copy of this arithmetic would be a second calibration to keep in step.
    /// </summary>
    internal static byte[] WithVeil(byte[] png, bool leftSide, double lift, int shoulderColumns)
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
