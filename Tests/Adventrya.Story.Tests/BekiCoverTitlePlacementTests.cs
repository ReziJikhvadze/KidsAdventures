using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The cover title is not printed over the child.
///
/// **The observed defect, 2026-09-08 (owner).** "On a production book the cover TITLE was printed
/// over the child's face." The title's rectangle was four constants on
/// <see cref="BekiCoverDieline"/> — x 285.5, y 34, 136 × 46 mm — and every book ever composed put
/// the title there. The cover prompt asks the model to keep the head out of that band in millimetre
/// coordinates it cannot measure; on the book the owner opened, it had not.
///
/// <see cref="BekiCoverTitlePlacement"/> reads the finished wrap and picks a rectangle from the
/// front board, and this file is what that promise means in practice: a cover whose top band is calm
/// keeps the approved box, a cover with something painted in it moves the title down to a calmer
/// band, no chosen box ever lands on the logo or outside the board's margins, the printed cover and
/// the customer's download get the SAME box, the page's receipt says which box that was, and the
/// same bytes always give the same answer.
///
/// Every fixture here is arithmetic — a painted sky and a painted head, no model and no network.
/// </summary>
public class BekiCoverTitlePlacementTests
{
    /// <summary>The approved rectangle, named once so a moved box is legible against it.</summary>
    private static readonly (float Left, float Top) Approved =
        (BekiCoverDieline.TitleSafeLeftMm, BekiCoverDieline.TitleSafeTopMm);

    // ==============================================================================================
    // Keeping the approved box
    // ==============================================================================================

    [Fact]
    public void A_face_in_the_upper_board_keeps_the_title_in_the_lower_band()
    {
        var choice = BekiCoverTitlePlacement.Choose(Wrap(headTopMm: 30f));
        Assert.True(choice.TopMm >= 162, choice.Reason);
        Assert.True(choice.TopMm + choice.HeightMm <= BekiCoverDieline.BoardBottomMm - 16);
        Assert.True(BekiCoverTitleChoice.Approved("fallback").TopMm >= 162);
    }

    /// <summary>
    /// A cover with no picture in it at all — one flat colour — keeps the approved box too.
    ///
    /// The reading normalises by its own percentiles, so a sheet with no variation comes back as
    /// zeros everywhere. Zero is not "calmer than the approved box" by
    /// <see cref="BekiCoverTitlePlacement.CalmerMargin"/>, and the honest answer to "where is the
    /// calm part of a picture that is entirely calm" is "where it was".
    /// </summary>
    [Fact]
    public void A_cover_with_nothing_in_it_keeps_the_approved_box()
    {
        var choice = BekiCoverTitlePlacement.Choose(Flat(1536, 735, new Rgba32(200, 190, 170)));

        Assert.False(choice.Moved);
        Assert.Equal(Approved.Top, choice.TopMm, 3);
    }

    // ==============================================================================================
    // Moving off it
    // ==============================================================================================

    [Theory]
    [InlineData(30f)]
    [InlineData(90f)]
    [InlineData(150f)]
    public void Artwork_never_moves_the_title_back_to_the_upper_board(float headTopMm)
    {
        var choice = BekiCoverTitlePlacement.Choose(Wrap(headTopMm));
        Assert.InRange(choice.TopMm, 162, 163);
    }

    /// <summary>
    /// The box is never resized on its way out of trouble: the title's measure is what the fit
    /// ladder was measured against, and a 120 mm box would break the title's lines somewhere the
    /// ladder never looked.
    /// </summary>
    [Fact]
    public void A_moved_box_is_the_same_size_as_the_approved_one()
    {
        var choice = BekiCoverTitlePlacement.Choose(Wrap(headTopMm: 30f));

        Assert.Equal(BekiCoverDieline.TitleSafeWidthMm, choice.WidthMm, 3);
        Assert.Equal(BekiCoverDieline.TitleSafeHeightMm, choice.HeightMm, 3);
    }

    // ==============================================================================================
    // The window, whatever the picture
    // ==============================================================================================

    /// <summary>
    /// Every rectangle the window can produce stays 16 mm inside the front board and clear of the
    /// logo's own clear space — asserted over the whole grid rather than over the boxes one fixture
    /// happened to choose, because a window that is only right on the pictures in this file is a
    /// window nobody can trust on the next book.
    ///
    /// The top edge is checked against the approved 34 mm rather than the board's head: above it is
    /// the turn-in, and the window deliberately never goes there.
    /// </summary>
    [Fact]
    public void No_rectangle_the_window_offers_leaves_the_board_or_touches_the_logo()
    {
        var offered = 0;

        foreach (var leftMm in BekiCoverTitlePlacement.LeftsMm)
        {
            for (var topMm = BekiCoverDieline.TitleSafeTopMm;
                 topMm <= BekiCoverDieline.BoardBottomMm - BekiCoverTitlePlacement.BoardMarginMm
                          - BekiCoverDieline.TitleSafeHeightMm;
                 topMm += BekiCoverTitlePlacement.VerticalStepMm)
            {
                if (BekiCoverTitlePlacement.TouchesLogo(
                        leftMm, topMm,
                        BekiCoverDieline.TitleSafeWidthMm, BekiCoverDieline.TitleSafeHeightMm))
                {
                    continue;
                }

                offered++;

                Assert.True(
                    leftMm >= BekiCoverDieline.FrontBoardLeftMm + BekiCoverTitlePlacement.BoardMarginMm,
                    $"x {leftMm} is inside the board's left margin.");
                Assert.True(
                    leftMm + BekiCoverDieline.TitleSafeWidthMm
                        <= BekiCoverDieline.FrontBoardRightMm - BekiCoverTitlePlacement.BoardMarginMm,
                    $"x {leftMm} runs into the board's right margin.");
                Assert.True(topMm >= BekiCoverDieline.TitleSafeTopMm, $"y {topMm} is above the approved top.");
                Assert.True(
                    topMm + BekiCoverDieline.TitleSafeHeightMm
                        <= BekiCoverDieline.BoardBottomMm - BekiCoverTitlePlacement.BoardMarginMm,
                    $"y {topMm} runs into the board's foot.");
            }
        }

        Assert.Equal(4, offered);
    }

    /// <summary>
    /// The logo's rectangle plus its clear space is refused, and the approved box — which sits
    /// clear of it — is not. A window that excluded nothing would pass the test above by accident.
    /// </summary>
    [Fact]
    public void The_logo_and_its_clear_space_are_excluded()
    {
        Assert.False(BekiCoverTitlePlacement.TouchesLogo(
            Approved.Left, Approved.Top,
            BekiCoverDieline.TitleSafeWidthMm, BekiCoverDieline.TitleSafeHeightMm));

        // A box in the same row, pushed right until it reaches under the mark.
        Assert.True(BekiCoverTitlePlacement.TouchesLogo(
            BekiCoverDieline.LogoLeftMm, BekiCoverDieline.LogoTopMm,
            BekiCoverDieline.TitleSafeWidthMm, BekiCoverDieline.TitleSafeHeightMm));
    }

    /// <summary>
    /// However busy the top band is, the chosen box is a rectangle the board actually holds.
    ///
    /// The same three bounds as the window test, asked of the answer rather than of the offer — the
    /// two are only the same claim while <see cref="BekiCoverTitlePlacement.Choose"/> chooses from
    /// the window, which is exactly what could stop being true.
    /// </summary>
    [Theory]
    [InlineData(30f)]
    [InlineData(45f)]
    [InlineData(70f)]
    public void The_chosen_box_is_always_inside_the_board_and_clear_of_the_logo(float headTopMm)
    {
        var choice = BekiCoverTitlePlacement.Choose(Wrap(headTopMm));

        Assert.True(
            choice.LeftMm >= BekiCoverDieline.FrontBoardLeftMm + BekiCoverTitlePlacement.BoardMarginMm);
        Assert.True(
            choice.LeftMm + choice.WidthMm
                <= BekiCoverDieline.FrontBoardRightMm - BekiCoverTitlePlacement.BoardMarginMm);
        Assert.True(choice.TopMm >= BekiCoverDieline.TitleSafeTopMm);
        Assert.True(
            choice.TopMm + choice.HeightMm
                <= BekiCoverDieline.BoardBottomMm - BekiCoverTitlePlacement.BoardMarginMm);
        Assert.False(BekiCoverTitlePlacement.TouchesLogo(
            choice.LeftMm, choice.TopMm, choice.WidthMm, choice.HeightMm));
    }

    // ==============================================================================================
    // Determinism
    // ==============================================================================================

    /// <summary>
    /// The same bytes give the same rectangle, and the same picture at two sizes gives the same
    /// rectangle.
    ///
    /// Both halves are load bearing and they fail differently. The first is what makes a receipt a
    /// receipt: a reprint that placed the title somewhere else would make the stored decision a
    /// record of a cover that no longer exists. The second is what makes the two covers agree at
    /// all — the press page reads the 6047-wide press wrap and the customer's page reads the same
    /// artwork, and the reading downscales both to <see cref="BekiActivityMap.MapWidth"/> columns
    /// precisely so that the pixel count cannot change the answer.
    /// </summary>
    [Fact]
    public void The_same_cover_always_gives_the_same_rectangle()
    {
        var wrap = Wrap(headTopMm: 30f);

        Assert.Equal(BekiCoverTitlePlacement.Choose(wrap), BekiCoverTitlePlacement.Choose(wrap));

        var small = BekiCoverTitlePlacement.Choose(Wrap(30f, 1024, 490));
        var large = BekiCoverTitlePlacement.Choose(Wrap(30f, 2048, 980));

        Assert.Equal(small.LeftMm, large.LeftMm, 3);
        Assert.Equal(small.TopMm, large.TopMm, 3);
    }

    // ==============================================================================================
    // The two covers, and the receipt
    // ==============================================================================================

    /// <summary>
    /// The press cover and the customer's download put the title in the same place, and both say so.
    ///
    /// Audit P0-01's finding was two covers that were not the same design, and a per-book title box
    /// is a fresh opportunity to make that true again — the press page pads to wrap millimetres and
    /// the customer's page to fractions of a board crop, and two readings of one wrap could disagree
    /// where two constants could not. They cannot here: both pages take the decision from
    /// <c>BekiPdfComposer.CoverTitleBox</c>, keyed by the wrap's own hash, and both record it in the
    /// wrap's own millimetres so that the two receipts are directly comparable.
    /// </summary>
    [Fact]
    public void The_press_cover_and_the_reading_cover_carry_the_same_box()
    {
        var wrap = Wrap(headTopMm: 30f);
        var plan = BekiLayoutFixture.EightSpreadPlan();
        var composer = new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout()));

        var press = composer.ComposeCoverPressWithReceipts(plan.Concept.Title, wrap);
        var reading = composer.ComposeReading(
            plan,
            wrap,
            plan.Spreads.Select(spread => new BekiSpreadArtwork(spread.Number, Flat(150, 70, Grey))).ToList(),
            BekiLayoutFixture.Personalization());

        var pressBox = press.Receipts.Pages.Single(page => page.Role == "cover-wrap").TitleBox;
        var readingBox = reading.Receipts.Pages.Single(page => page.Role == "cover-front").TitleBox;

        Assert.NotNull(pressBox);
        Assert.Equal(pressBox, readingBox);
        Assert.True(pressBox.TopMm >= 162, "both exports must keep the title below the face.");

        // And the box the customer's page derives from it is a real rectangle on that page: the
        // fractions are what ComposeReadingFrontCover pads by, and one outside [0, 1] would set the
        // title off the leaf.
        var (left, top, width, height) = BekiCoverDieline.InsideFrontBoardCrop(
            (float)pressBox.LeftMm, (float)pressBox.TopMm,
            (float)pressBox.WidthMm, (float)pressBox.HeightMm);

        Assert.InRange(left, 0d, 1d);
        Assert.InRange(top, 0d, 1d);
        Assert.InRange(left + width, 0d, 1d);
        Assert.InRange(top + height, 0d, 1d);
    }

    /// <summary>
    /// The receipt names the algorithm, the rectangle and why — and only on the pages that set a
    /// title over the cover wrap.
    ///
    /// The "only" is the part worth asserting. <c>title_box</c> is a trailing optional member of
    /// <see cref="BekiLayoutPageReceipt"/> so that adding it moved no other page in the book; a
    /// receipt that had quietly acquired one on the credits page would mean the record no longer
    /// says what it claims to.
    /// </summary>
    [Fact]
    public void Only_the_cover_pages_record_a_title_box()
    {
        var plan = BekiLayoutFixture.EightSpreadPlan();
        var book = new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout()))
            .ComposeCanonicalWithReceipts(
                plan,
                Wrap(headTopMm: 30f),
                plan.Spreads.Select(spread => new BekiSpreadArtwork(spread.Number, Flat(150, 70, Grey))).ToList(),
                BekiLayoutFixture.Personalization());

        var cover = book.Receipts.Pages.Single(page => page.Role == "cover-wrap");

        Assert.NotNull(cover.TitleBox);
        Assert.Equal(BekiCoverTitlePlacement.Version, cover.TitleBox.Version);
        Assert.Equal(BekiCoverDieline.TitleSafeWidthMm, cover.TitleBox.WidthMm, 3);
        Assert.NotEmpty(cover.TitleBox.Reason);
        Assert.Contains("\"title_box\"", cover.ToJson(), StringComparison.Ordinal);

        Assert.All(
            book.Receipts.Pages.Where(page => page.Role != "cover-wrap"),
            page => Assert.Null(page.TitleBox));
    }

    // ==============================================================================================
    // The gate that reads the same rectangle
    // ==============================================================================================

    /// <summary>
    /// A reviewer's "the head is here" is judged against the box this book's title was actually set
    /// in, not against the approved one.
    ///
    /// Both directions, because only the pair says anything. A head under the APPROVED box on a
    /// cover whose title moved away from it is no longer a conflict; a head under the MOVED box is
    /// one, even though the approved rectangle is nowhere near it.
    /// </summary>
    [Fact]
    public void The_layout_gate_checks_the_box_the_title_actually_uses()
    {
        var moved = new BekiCoverTitleChoice(
            BekiCoverDieline.TitleSafeLeftMm, 130d,
            BekiCoverDieline.TitleSafeWidthMm, BekiCoverDieline.TitleSafeHeightMm,
            Moved: true, ApprovedScore: 0.4, ChosenScore: 0.1, CandidatesEvaluated: 56, "moved");

        var inTheOldBox = new BekiCoverProtectedArea("head", "head in the default lower band", 320, 200, 40, 10);
        var inTheNewBox = new BekiCoverProtectedArea("head", "head in the chosen band", 320, 140, 40, 40);

        Assert.Empty(BekiCoverLayoutSafety.Conflicts([inTheOldBox], moved));
        Assert.Contains(
            "TITLE overlaps head",
            Assert.Single(BekiCoverLayoutSafety.Conflicts([inTheNewBox], moved)),
            StringComparison.Ordinal);

        // Null uses the current lower default.
        Assert.Single(BekiCoverLayoutSafety.Conflicts([inTheOldBox]));
    }

    // ==============================================================================================
    // Fixtures
    // ==============================================================================================

    private static readonly Rgba32 Grey = new(128, 128, 128);

    /// <summary>How tall the painted head is, in wrap millimetres.</summary>
    private const float HeadHeightMm = 44f;

    private const float HeadWidthMm = 52f;

    /// <summary>
    /// A cover-shaped picture: a pastel sky with soft texture from edge to edge, and one dark head
    /// painted onto the front board with its top at <paramref name="headTopMm"/>.
    ///
    /// The texture is not decoration. The reading normalises each of its two halves by that
    /// picture's own 95th percentile and then subtracts its own 5th, so a sheet that is one flat
    /// colour with one blob on it collapses to zeros everywhere and measures nothing — which is the
    /// honest answer for such a sheet and a useless fixture for this question. A sky with a gradient
    /// and a ripple in it is what a generated cover actually looks like to the map.
    /// </summary>
    private static byte[] Wrap(float headTopMm, int width = 1536, int height = 735)
    {
        using var image = new Image<Rgba32>(width, height);

        var centreX = 350f / BekiCoverDieline.CanvasWidthMm * width;
        var centreY = (headTopMm + (HeadHeightMm / 2f)) / BekiCoverDieline.CanvasHeightMm * height;
        var radiusX = HeadWidthMm / 2f / BekiCoverDieline.CanvasWidthMm * width;
        var radiusY = HeadHeightMm / 2f / BekiCoverDieline.CanvasHeightMm * height;

        // Stated against the fixture's own 1536 columns so that the same picture is painted at every
        // size: a ripple whose wavelength is a pixel count would be a different picture on each.
        var scale = 1536f / width;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var wash = 0.5f
                           + (0.35f * MathF.Sin((x * scale * 0.011f) + (y * scale * 0.004f)))
                           + (0.15f * MathF.Sin((x * scale * 0.047f) - (y * scale * 0.031f)));

                image[x, y] = new Rgba32(
                    (byte)Math.Clamp(210 + (wash * 30), 0, 255),
                    (byte)Math.Clamp(200 + (wash * 34), 0, 255),
                    (byte)Math.Clamp(178 + (wash * 44), 0, 255));

                var dx = (x - centreX) / radiusX;
                var dy = (y - centreY) / radiusY;

                if ((dx * dx) + (dy * dy) <= 1)
                {
                    // Dark, saturated and full of its own contour, so both halves of the reading
                    // find it: a head that only one half could see would prove half the design.
                    var curl = (byte)(30 + (40 * (MathF.Sin(x * scale * 0.35f)
                                                  + MathF.Sin(y * scale * 0.4f) + 2) / 4));
                    image[x, y] = new Rgba32(curl, (byte)(curl * 0.6f), (byte)(curl * 0.5f));
                }
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static byte[] Flat(int width, int height, Rgba32 colour)
    {
        using var image = new Image<Rgba32>(width, height, colour);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// The press stage records the chosen box in <c>cover-layout-safety.json</c> as
    /// <c>title_box</c>, and the admin cover-layout endpoint reads it back rather than measuring
    /// the wrap again (sol review, 2026-09-08: a fresh reading can disagree with the published
    /// cover). The record must survive the round trip as the same rectangle and the same verdict.
    /// </summary>
    [Fact]
    public void The_recorded_title_box_round_trips_through_the_layout_safety_record()
    {
        var chosen = new BekiCoverTitleChoice(
            285.5, 162, 136, 46, Moved: true, ApprovedScore: 0.2763, ChosenScore: 0.2137,
            CandidatesEvaluated: 56, "moved the title off the head");

        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            gate = BekiCoverLayoutSafety.Gate,
            verdict = "NOT_REVIEWED",
            title_box = chosen,
        });

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var box = document.RootElement.GetProperty("title_box");
        var read = System.Text.Json.JsonSerializer.Deserialize<BekiCoverTitleChoice>(box.GetRawText());

        Assert.NotNull(read);
        Assert.Equal(chosen.LeftMm, read!.LeftMm);
        Assert.Equal(chosen.TopMm, read.TopMm);
        Assert.Equal(chosen.WidthMm, read.WidthMm);
        Assert.Equal(chosen.HeightMm, read.HeightMm);
        Assert.True(read.Moved);
        Assert.Equal(chosen.Reason, read.Reason);
        Assert.Equal(BekiCoverTitlePlacement.Version, read.Version);

        // A head recorded inside the approved box is no longer a conflict once the title has moved
        // below it — and the same verdict must come out of the record as out of the live choice.
        var head = new BekiCoverProtectedArea("head", "the child's head", 350, 70, 60, 50);
        Assert.Empty(BekiCoverLayoutSafety.Conflicts([head], read));
        Assert.Empty(BekiCoverLayoutSafety.Conflicts([head]));
    }
}
