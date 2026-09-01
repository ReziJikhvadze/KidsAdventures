using System.Text.Json;
using AdventurePacks.Api.Services.Story.Composite;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The cover wrap's construction bands — audit-2 P0-03, plan amendment A2.
///
/// The finding these exist for is a shipped hardcover whose artwork drew its own bookbinding. The
/// cover prompt names the centre construction as percentage regions ("from 47% to 53% of the
/// canvas width"), the model painted the regions it was told about, and the delivered wrap carried
/// "strong vertical tonal jumps at approximately x=1236 and x=1291 px" — on a 2528-wide, 512 mm
/// cover, 250.5 mm and 261.5 mm: the exact spine boundaries.
///
/// Nothing measured it. The centre-field reading existed and was pointed at 50% of a story spread;
/// the wrap path skipped it deliberately, on the argument that a spine is not a fold. That
/// argument is answered here rather than repeated: the reading is taken at the four dieline lines
/// with strips small enough to fit inside an 8 mm hinge, so what it judges is whether a boundary
/// was painted rather than whether two cover boards differ.
///
/// One of the classes CompositePipelineTestBase serves; see it for the fixtures these use.
/// </summary>
public class CompositeCoverBandTests : CompositePipelineTestBase
{
    /// <summary>
    /// The cropped wrap's shape, as the pipeline actually produces it: the provider's 3:2 frame
    /// centre-cropped to 512:245, which trims height and leaves every column where it was.
    /// </summary>
    private const int WrapWidth = ProviderWidth;

    private const int WrapHeight = 735;

    /// <summary>
    /// Where one dieline boundary lands on a wrap of this width, in columns.
    ///
    /// Computed from the millimetres rather than written down, because a hard-coded column would
    /// pass a test against a measurement that was reading the wrong place.
    ///
    /// Internal because <see cref="CompositePipelinePolicyTests"/> paints the same boundary to ask
    /// what a flagged policy does with it, and two copies of this arithmetic would be two places for
    /// the dieline to drift out of.
    /// </summary>
    internal static int ColumnFor(double millimetres, int width) =>
        (int)Math.Round(width * (millimetres / BekiCoverDieline.CanvasWidthMm));

    // ---------------------------------------------------------------------------------------
    // The four lines
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The boundaries are the dieline's own, in the dieline's own millimetres: A2 voids D6's
    /// "50.5%" and names 242.5, 250.5, 261.5 and 269.5 mm.
    ///
    /// Derived from <see cref="BekiCoverDieline"/> rather than transcribed, which is the assertion
    /// worth having: the geometry a cover is built to and the geometry it is measured at cannot
    /// become two numbers that drift apart.
    /// </summary>
    [Fact]
    public void The_four_boundaries_are_the_dielines_own_millimetres()
    {
        var boundaries = CompositeSeamRepair.CoverConstructionBoundaries;

        Assert.Equal(
            new[] { 242.5, 250.5, 261.5, 269.5 },
            boundaries.Select(boundary => boundary.MillimetresFromLeft).ToList());

        // The same lines as the percentages the amendment quotes.
        Assert.Equal(
            new[] { 0.4736, 0.4893, 0.5107, 0.5264 },
            boundaries.Select(boundary => Math.Round(boundary.WidthFraction, 4)).ToList());

        // And the last one is where the front board begins, which is the whole reason it is a line
        // at all — not a number that happens to be near the middle.
        Assert.Equal(
            BekiCoverDieline.FrontBoardLeftMm, boundaries[3].MillimetresFromLeft, 3);
    }

    /// <summary>
    /// A continuous panorama passes all four. The gate's whole risk is the other kind of error:
    /// refusing a cover that was fine costs a paid image call on the one picture a parent judges
    /// the book by.
    /// </summary>
    [Fact]
    public void A_continuous_wrap_passes_every_boundary()
    {
        var measured = CompositeSeamRepair.MeasureConstructionBands(Gradient(WrapWidth, WrapHeight));

        Assert.False(measured.Exceeded);
        Assert.Empty(measured.Offending);
        Assert.Equal(4, measured.Bands.Count);
    }

    /// <summary>
    /// The audit's own defect, painted: a full-height tonal jump at the hinge-to-spine line. It is
    /// caught, and it is named — the report says which boundary, in millimetres, because "the
    /// spine is painted" and "the front hinge is painted" are different conversations to have with
    /// the prompt.
    /// </summary>
    [Theory]
    [InlineData(250.5, "back-hinge-to-spine")]
    [InlineData(261.5, "spine-to-front-hinge")]
    [InlineData(242.5, "back-board-edge")]
    [InlineData(269.5, "front-board-edge")]
    public void A_band_painted_on_a_dieline_boundary_is_found_and_named(
        double millimetres, string expected)
    {
        var banded = WithSeam(
            Gradient(WrapWidth, WrapHeight), columns: 3, darken: 90,
            atColumn: ColumnFor(millimetres, WrapWidth));

        var measured = CompositeSeamRepair.MeasureConstructionBands(banded);

        Assert.True(measured.Exceeded, measured.ToString());

        var offending = Assert.Single(measured.Offending);
        Assert.Equal(expected, offending.Boundary.Name);
        Assert.Equal(millimetres, offending.Boundary.MillimetresFromLeft, 3);
    }

    /// <summary>
    /// A vertical feature somewhere else in the picture is a tree, a doorway or the edge of a
    /// building, and the gate must not touch it. The four bands are half a per cent wide for
    /// exactly this reason — a spread's four-per-cent band would swallow all four lines and half
    /// the spine as well.
    /// </summary>
    [Fact]
    public void A_strong_vertical_feature_away_from_the_dieline_is_not_a_band()
    {
        var feature = WithSeam(
            Gradient(WrapWidth, WrapHeight), columns: 4, darken: 120,
            atColumn: (int)Math.Round(WrapWidth * 0.30));

        Assert.False(CompositeSeamRepair.MeasureConstructionBands(feature).Exceeded);

        // And one just outside a boundary's own tolerance, which is what keeps the four readings
        // four readings rather than one: two per cent of the width away from the front board's
        // edge is the child's zone, not the hinge.
        var beside = WithSeam(
            Gradient(WrapWidth, WrapHeight), columns: 3, darken: 90,
            atColumn: ColumnFor(269.5, WrapWidth) + (int)Math.Round(WrapWidth * 0.02));

        Assert.False(CompositeSeamRepair.MeasureConstructionBands(beside).Exceeded);
    }

    /// <summary>
    /// A frame too small to hold the strips is a fixture or a thumbnail, not a wrap: measured as
    /// nothing rather than refused, because a gate that fails what it cannot read fails everything
    /// it cannot read.
    /// </summary>
    [Fact]
    public void A_frame_too_small_to_measure_is_not_refused()
    {
        Assert.False(
            CompositeSeamRepair.MeasureConstructionBands(SyntheticImages.SolidPng(24, 12)).Exceeded);
    }

    /// <summary>
    /// The generalized reading is the same reading: pointed back at 50% with the spread's own
    /// geometry it reproduces <c>MeasureCentreField</c> exactly.
    ///
    /// Worth pinning because the refactor that made the x-position a parameter is the refactor
    /// that could quietly have changed what the fold reading means — and every threshold in this
    /// system was calibrated against the old one.
    /// </summary>
    [Fact]
    public void The_generalized_reading_reproduces_the_fold_reading_at_fifty_per_cent()
    {
        foreach (var picture in new[]
                 {
                     Gradient(1536, 717),
                     WithSeam(Gradient(1536, 717), columns: 2, darken: 90),
                 })
        {
            Assert.Equal(
                CompositeSeamRepair.MeasureCentreField(picture),
                CompositeSeamRepair.MeasureFieldAt(picture, 0.5, FieldReadingGeometry.Spread));
        }
    }

    // ---------------------------------------------------------------------------------------
    // The wrap path
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A banded wrap buys one regeneration — the budget the wrap never had — and the continuous
    /// second picture is the one Beki is composited onto.
    /// </summary>
    [Fact]
    public async Task A_banded_wrap_base_buys_one_redraw_and_the_clean_second_is_composited()
    {
        var images = new StubImageService();

        // The provider's frame with the spine painted in. The crop to 512:245 trims height only,
        // so the column the band sits in is the column the measurement will read.
        images.QueuedImages.Enqueue(WithSeam(
            Gradient(ProviderWidth, ProviderHeight), columns: 3, darken: 90,
            atColumn: ColumnFor(250.5, ProviderWidth)));

        var pipeline = Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images);
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        var wrap = await pipeline.DrawCoverWrapAsync(
            Context(), scenario, Photo(), "image/png", CancellationToken.None);

        Assert.Equal(2, images.ImageCalls);
        Assert.False(CompositeSeamRepair.MeasureConstructionBands(wrap.BasePng).Exceeded);

        // The second picture is a real wrap, receipt and all — the regeneration is a retry, not a
        // degraded path.
        Assert.NotEmpty(wrap.PoseId);
        Assert.NotEmpty(wrap.ManifestJson);
    }

    /// <summary>
    /// Two bases, both painting the dieline, and the wrap refuses — with the picture and a reading
    /// per boundary, in the same two blobs a refused spread leaves behind.
    ///
    /// The cover has no page number, so the evidence lands under spread zero: a page number no
    /// book has, which is how somebody looking for a refused cover finds it.
    /// </summary>
    [Fact]
    public async Task A_wrap_banded_twice_is_refused_with_a_reading_for_every_boundary()
    {
        var banded = WithSeam(
            Gradient(ProviderWidth, ProviderHeight), columns: 3, darken: 90,
            atColumn: ColumnFor(261.5, ProviderWidth));

        var images = new StubImageService { NextImage = banded };

        var pipeline = Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images);
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            pipeline.DrawCoverWrapAsync(
                Context(), scenario, Photo(), "image/png", CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageGenerationFailed, failure.FailureCode);
        Assert.Equal(0, failure.Page);

        // Exactly two pictures: one regeneration, then a stop. A wrap does not climb a ladder.
        Assert.Equal(2, images.ImageCalls);

        var evidence = Assert.IsType<CompositeFailureEvidence>(failure.Evidence);
        Assert.Equal(0, evidence.Page);
        Assert.True(CompositeSeamRepair.MeasureConstructionBands(evidence.CompositePng).Exceeded);

        using var document = JsonDocument.Parse(evidence.QaJson);
        var root = document.RootElement;

        Assert.Equal("cover_construction_bands", root.GetProperty("gate").GetString());
        Assert.Equal("P0-03", root.GetProperty("audit_item").GetString());
        Assert.Equal(
            BekiCoverDieline.CanvasWidthMm, root.GetProperty("canvas_width_mm").GetSingle(), 3);

        var attempts = root.GetProperty("attempts").EnumerateArray().ToList();
        Assert.Equal(2, attempts.Count);

        foreach (var attempt in attempts)
        {
            // Every boundary, not only the offending one: a wrap painted at one line and a wrap
            // painted at all four are different faults, and only the full list says which.
            var boundaries = attempt.GetProperty("boundaries").EnumerateArray().ToList();
            Assert.Equal(4, boundaries.Count);
            Assert.Contains(
                boundaries,
                boundary => boundary.GetProperty("name").GetString() == "spine-to-front-hinge"
                            && boundary.GetProperty("exceeded").GetBoolean());
        }
    }

    /// <summary>
    /// A continuous wrap costs one image call and nothing else. The gate is not a tax on the
    /// ordinary case.
    /// </summary>
    [Fact]
    public async Task A_continuous_wrap_costs_one_image_call()
    {
        var images = new StubImageService();

        var pipeline = Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images);
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        await pipeline.DrawCoverWrapAsync(
            Context(), scenario, Photo(), "image/png", CancellationToken.None);

        Assert.Equal(1, images.ImageCalls);
    }
}
