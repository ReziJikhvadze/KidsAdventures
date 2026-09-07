using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// How occupied each part of a generated picture is: its own contours and colour, spread over the
/// distance a drawn figure is wide, and re-zeroed on the picture's own quietest ground.
///
/// **Why it is its own type.** It was a private nested class inside
/// <see cref="Poses.BekiPlacementChooser"/>, which is where it was written and measured: eight real
/// bases of book <c>54cba4b3</c>, calibrated against where a person would have stood Beki. The cover
/// title has the same question to answer — "which part of this picture is calm enough to put
/// something on" — and the owner's rule 5 of 2026-09-01 forbids buying a vision call to answer it
/// either. Two readings of the same kind would be two things to calibrate, two things to drift, and
/// a contract document that had to explain why the cover disagrees with the spreads about where the
/// child is. So the reading moved here whole and both callers share it.
///
/// Nothing about it changed in the move. The constants are the ones
/// <c>BEKI_Beki_Placement_Selection_v1.md</c> §4 publishes, the arithmetic is the arithmetic those
/// eight bases were measured with, and <c>BekiPlacementChooserTests</c> is the assertion that it
/// still is — a shared reading that quietly re-tuned itself would move a placement in a book that
/// has already been printed.
///
/// **What it is not.** It is not subject detection. It cannot say "the child is here"; it says "this
/// is busier than that", which is a weaker claim and the only one contour and colour can honestly
/// support. See §9.4 of the contract for what that costs.
/// </summary>
internal sealed class BekiActivityMap
{
    /// <summary>
    /// The width the activity map is measured at, whatever the picture arrived as.
    ///
    /// Fixed rather than proportional so that the same picture scores the same on a 1536-wide
    /// proof and a 5315-wide press raster: the Sobel kernel is three pixels across, and three
    /// pixels is a different physical distance on each. Downscaling first also does the noise
    /// suppression a blur would otherwise have to, for free.
    /// </summary>
    public const int MapWidth = 512;

    /// <summary>
    /// Which quantile of a reading is treated as its full scale. See
    /// <see cref="NormaliseByPercentile"/>.
    /// </summary>
    public const float EdgePercentile = 0.95f;

    /// <summary>
    /// Which part of the map counts as "as quiet as this picture gets" — the ground every score is
    /// measured from. See <see cref="AboveTheQuietest"/>.
    /// </summary>
    public const float QuietFloorPercentile = 0.05f;

    /// <summary>
    /// How the two readings of the picture are mixed: how detailed a place is, and how far its
    /// colour is from the page's own wash. Half each — see <see cref="ColourDistinctness"/> for what
    /// each half catches alone.
    /// </summary>
    public const double ColourDistinctnessWeight = 0.5;

    /// <summary>
    /// How much the combined reading favours the loud over the merely present, before it is spread.
    ///
    /// Raising a value in [0, 1] to this power leaves 1 where it is and pushes everything below it
    /// down — a strong contour keeps its weight, a soft cloud shoulder loses most of hers. It is
    /// what stops a pastel sky, which is nothing but gentle texture, from out-scoring the child
    /// standing in front of it once both have been averaged over a box. One and a half rather than
    /// two: measured on the eight real bases, a square emphasis leaves the map so sparse that the
    /// interior of a figure reads as empty again.
    /// </summary>
    public const double SubjectEmphasis = 1.5;

    /// <summary>
    /// How far a subject's cost spreads, as a fraction of the canvas width.
    ///
    /// This is the single number that makes the difference between a reading that avoids the child
    /// and one that walks straight onto her. A drawn character is an OUTLINE around a FLAT
    /// interior: dark hair against pale sky, a rim-lit shoulder, eyes — and then a knitted sweater
    /// that a gradient operator finds calmer than a cloud. Scored on raw gradient alone, the
    /// quietest rectangle in every one of the eight real bases of book 54cba4b3 is the child's own
    /// chest, which is the exact defect this reading was written to remove.
    ///
    /// So the reading is spread before it is read. Each pixel's cost becomes the mean over a box
    /// this wide around it, which fills a subject in from its own contour and makes standing just
    /// beside somebody nearly as expensive as standing on them. Six per cent of the width is about
    /// a third of Beki's own height on a story spread — wide enough to close a child, narrow enough
    /// that an open sky two figures away is still open sky.
    /// </summary>
    public const double SubjectSpreadFraction = 0.06;

    private readonly float[] _activity;

    private BekiActivityMap(int width, int height, float[] activity, double scale)
    {
        Width = width;
        Height = height;
        _activity = activity;
        Scale = scale;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Map pixels per canvas pixel — how a placement is carried into this map.</summary>
    public double Scale { get; }

    /// <summary>
    /// Reads one picture.
    ///
    /// The downscale is the only expensive step: the picture arrives at 1536 or 6047 pixels wide and
    /// is reduced to <see cref="MapWidth"/>, after which everything below is arithmetic over about a
    /// hundred thousand floats.
    /// </summary>
    public static BekiActivityMap Measure(Image<Rgba32> canvas)
    {
        var width = Math.Min(MapWidth, canvas.Width);
        var height = Math.Max(
            1, (int)Math.Round((double)canvas.Height * width / canvas.Width, MidpointRounding.ToEven));

        using var small = canvas.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(width, height),
            Mode = ResizeMode.Stretch,
            // Box, not Lanczos3: this is a measurement rather than a picture, and a windowed
            // sinc rings at every edge — which is precisely the quantity being measured.
            Sampler = KnownResamplers.Box,
            Compand = false,
        }));

        var luminance = new float[width * height];
        var red = new float[width * height];
        var green = new float[width * height];
        var blue = new float[width * height];

        small.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    var index = (y * width) + x;

                    luminance[index] =
                        ((0.2126f * pixel.R) + (0.7152f * pixel.G) + (0.0722f * pixel.B)) / 255f;
                    red[index] = pixel.R / 255f;
                    green[index] = pixel.G / 255f;
                    blue[index] = pixel.B / 255f;
                }
            }
        });

        // Two readings of the same picture, because a drawn subject shows up in both and the
        // background of these books shows up in neither reliably. See ContourStrength and
        // ColourDistinctness for what each catches and what each misses on its own.
        var contours = ContourStrength(luminance, width, height);
        var distinct = ColourDistinctness(red, green, blue);

        var raw = new float[width * height];
        for (var index = 0; index < raw.Length; index++)
        {
            raw[index] = MathF.Pow(
                ((1 - (float)ColourDistinctnessWeight) * contours[index])
                + ((float)ColourDistinctnessWeight * distinct[index]),
                (float)SubjectEmphasis);
        }

        var reach = Math.Max(1, (int)Math.Round(
            SubjectSpreadFraction * width, MidpointRounding.ToEven));

        return new BekiActivityMap(
            width,
            height,
            AboveTheQuietest(Spread(raw, width, height, reach)),
            (double)width / canvas.Width);
    }

    /// <summary>
    /// Where a rectangle of <paramref name="rectangleWidth"/> × <paramref name="rectangleHeight"/>
    /// map pixels, placed at canvas pixel (<paramref name="canvasX"/>, <paramref name="canvasY"/>),
    /// starts in this map — clamped so that it is read from inside the map rather than off its edge.
    /// </summary>
    public (int Left, int Top) Place(
        int canvasX, int canvasY, int rectangleWidth, int rectangleHeight) => (
        Math.Clamp(
            (int)Math.Round(canvasX * Scale, MidpointRounding.ToEven),
            0,
            Math.Max(0, Width - rectangleWidth)),
        Math.Clamp(
            (int)Math.Round(canvasY * Scale, MidpointRounding.ToEven),
            0,
            Math.Max(0, Height - rectangleHeight)));

    /// <summary>
    /// A band width in map pixels, from a fraction of the canvas width — at least one pixel, because
    /// a band nothing falls in measures nothing.
    /// </summary>
    public int Band(double fractionOfWidth) =>
        Math.Max(1, (int)Math.Round(fractionOfWidth * Width, MidpointRounding.ToEven));

    /// <summary>
    /// The activity under a shape, weighted by how much of each pixel that shape actually covers.
    ///
    /// A sprite is not a rectangle, and scoring her bounding box would judge a placement by scenery
    /// her silhouette does not touch — the gap under an outstretched arm.
    /// </summary>
    /// <param name="alpha">The shape's coverage at map scale, row-major, in [0, 1].</param>
    /// <param name="alphaTotal">Its sum, so an empty shape divides by one rather than by zero.</param>
    public double MaskedMean(
        int left, int top, int maskWidth, int maskHeight, float[] alpha, double alphaTotal)
    {
        var covered = 0.0;

        for (var y = 0; y < maskHeight; y++)
        {
            var mapRow = Math.Min(Height - 1, top + y) * Width;
            var maskRow = y * maskWidth;

            for (var x = 0; x < maskWidth; x++)
            {
                var weight = alpha[maskRow + x];
                if (weight <= 0)
                {
                    continue;
                }

                covered += weight * _activity[mapRow + Math.Min(Width - 1, left + x)];
            }
        }

        return covered / (alphaTotal > 0 ? alphaTotal : 1);
    }

    /// <summary>The plain mean activity inside a rectangle of map pixels.</summary>
    public double RectangleMean(int left, int top, int width, int height)
    {
        var right = Math.Min(Width, left + width);
        var bottom = Math.Min(Height, top + height);
        var total = 0.0;
        var counted = 0;

        for (var y = Math.Max(0, top); y < bottom; y++)
        {
            var row = y * Width;
            for (var x = Math.Max(0, left); x < right; x++)
            {
                total += _activity[row + x];
                counted++;
            }
        }

        return counted == 0 ? 0 : total / counted;
    }

    /// <summary>
    /// The mean activity in the ring around a rectangle — that rectangle grown by
    /// <paramref name="band"/> map pixels on every side, less the rectangle itself.
    ///
    /// The shape's own footprint is not enough. A gap exactly its size between two busy objects
    /// scores beautifully and looks wedged; the band is what tells those apart from an open sky.
    /// </summary>
    public double BandMean(int left, int top, int width, int height, int band)
    {
        var outerLeft = Math.Max(0, left - band);
        var outerTop = Math.Max(0, top - band);
        var outerRight = Math.Min(Width, left + width + band);
        var outerBottom = Math.Min(Height, top + height + band);

        var total = 0.0;
        var counted = 0;

        for (var y = outerTop; y < outerBottom; y++)
        {
            var inside = y >= top && y < top + height;
            var row = y * Width;

            for (var x = outerLeft; x < outerRight; x++)
            {
                if (inside && x >= left && x < left + width)
                {
                    continue;
                }

                total += _activity[row + x];
                counted++;
            }
        }

        return counted == 0 ? 0 : total / counted;
    }

    /// <summary>
    /// How far each pixel's colour sits from the average colour of the whole picture, normalised
    /// the same way the contours are.
    ///
    /// The contour reading alone loses the two things it most needs to find. A child's clothing
    /// is a large flat area with no gradient in it at all, and a white dog against a white cloud
    /// has no luminance edge to detect — while a pastel cloudscape is nothing BUT soft texture,
    /// so an edge detector ranks the sky above the subject standing in it. Colour rescues both:
    /// these pages are a warm cream wash with one child painted in blue, cream and dark brown on
    /// top of it, and the child is by some distance the least average-coloured thing in frame.
    ///
    /// It is the average colour rather than a trained model on purpose. There is no per-book
    /// tuning here and nothing to drift: the reading is a property of the picture in hand, and a
    /// picture with no dominant wash — a busy interior — simply returns a flat map and lets the
    /// contour half decide.
    /// </summary>
    private static float[] ColourDistinctness(float[] red, float[] green, float[] blue)
    {
        var meanRed = 0.0;
        var meanGreen = 0.0;
        var meanBlue = 0.0;

        for (var index = 0; index < red.Length; index++)
        {
            meanRed += red[index];
            meanGreen += green[index];
            meanBlue += blue[index];
        }

        meanRed /= red.Length;
        meanGreen /= red.Length;
        meanBlue /= red.Length;

        var distance = new float[red.Length];

        for (var index = 0; index < red.Length; index++)
        {
            var dr = red[index] - meanRed;
            var dg = green[index] - meanGreen;
            var db = blue[index] - meanBlue;

            distance[index] = (float)Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
        }

        return NormaliseByPercentile(distance);
    }

    /// <summary>
    /// Re-zeroes the map on its own quietest ground: every pixel less its
    /// <see cref="QuietFloorPercentile"/>, clamped at zero.
    ///
    /// The question a candidate is asked is not "how busy is this in the abstract" but "how much
    /// busier is this than the calmest place this particular picture has to offer". Without the
    /// shift, a pastel cloudscape whose every square inch carries some soft texture scores 0.5
    /// nearly everywhere, and the ratio test — is the best candidate 15% calmer than the default
    /// — can never fire, because the constant floor is most of both numbers.
    ///
    /// A shift rather than a rescale, deliberately. Rescaling to the picture's own range would
    /// manufacture a decision out of a picture that is uniformly busy: divide a flat map by its
    /// own vanishing spread and the loudest grain in it becomes a reason to move the approved
    /// anchor. Subtracting leaves such a picture near zero everywhere, where the callers' own
    /// materiality margins refuse to act on it — which is the honest answer.
    /// </summary>
    private static float[] AboveTheQuietest(float[] spread)
    {
        var sorted = (float[])spread.Clone();
        Array.Sort(sorted);
        var floor = sorted[(int)(QuietFloorPercentile * (sorted.Length - 1))];

        for (var index = 0; index < spread.Length; index++)
        {
            spread[index] = Math.Max(0f, spread[index] - floor);
        }

        return spread;
    }

    /// <summary>
    /// Each pixel replaced by the mean over a square of side <c>2 × reach + 1</c> around it —
    /// a box blur, computed once from a summed-area table so the radius costs nothing.
    ///
    /// This is the step that turns an edge detector into an occupancy map. See
    /// <see cref="SubjectSpreadFraction"/> for why it has to exist at all; the mechanism is only
    /// that a figure's own outline is what fills the figure in.
    /// </summary>
    private static float[] Spread(float[] source, int width, int height, int reach)
    {
        // One row and column of zeros in front, so every window is four lookups with no
        // special case at the edges.
        var stride = width + 1;
        var sums = new double[stride * (height + 1)];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                sums[((y + 1) * stride) + x + 1] =
                    source[(y * width) + x]
                    + sums[(y * stride) + x + 1]
                    + sums[((y + 1) * stride) + x]
                    - sums[(y * stride) + x];
            }
        }

        var spread = new float[source.Length];

        for (var y = 0; y < height; y++)
        {
            var top = Math.Max(0, y - reach);
            var bottom = Math.Min(height, y + reach + 1);

            for (var x = 0; x < width; x++)
            {
                var left = Math.Max(0, x - reach);
                var right = Math.Min(width, x + reach + 1);

                var total = sums[(bottom * stride) + right]
                            - sums[(top * stride) + right]
                            - sums[(bottom * stride) + left]
                            + sums[(top * stride) + left];

                spread[(y * width) + x] = (float)(total / ((bottom - top) * (right - left)));
            }
        }

        return spread;
    }

    /// <summary>
    /// Sobel gradient magnitude on the luminance, normalised the same way the colour reading is.
    ///
    /// This is the "how detailed is it here" half: foliage, architecture, glowing stars, a
    /// child's face. What it cannot see is a flat interior or a white shape on a white ground,
    /// which is what <see cref="ColourDistinctness"/> is for.
    /// </summary>
    private static float[] ContourStrength(float[] luminance, int width, int height)
    {
        var magnitude = new float[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var left = Math.Max(0, x - 1);
                var right = Math.Min(width - 1, x + 1);
                var above = Math.Max(0, y - 1) * width;
                var below = Math.Min(height - 1, y + 1) * width;
                var middle = y * width;

                var topLeft = luminance[above + left];
                var top = luminance[above + x];
                var topRight = luminance[above + right];
                var midLeft = luminance[middle + left];
                var midRight = luminance[middle + right];
                var bottomLeft = luminance[below + left];
                var bottom = luminance[below + x];
                var bottomRight = luminance[below + right];

                var gx = (topRight + (2 * midRight) + bottomRight)
                         - (topLeft + (2 * midLeft) + bottomLeft);
                var gy = (bottomLeft + (2 * bottom) + bottomRight)
                         - (topLeft + (2 * top) + topRight);

                magnitude[middle + x] = MathF.Sqrt((gx * gx) + (gy * gy));
            }
        }

        return NormaliseByPercentile(magnitude);
    }

    /// <summary>
    /// Divides a reading by its own <see cref="EdgePercentile"/> and clamps it to [0, 1].
    ///
    /// A percentile rather than the maximum, so that one specular highlight or one dark eyelash
    /// cannot rate everything else in a pastel picture at nearly zero; and a clamp afterwards,
    /// so that the busiest five per cent cannot dominate a mean either. A reading with no
    /// variation at all — a solid fill — comes back as zeros, which is the honest answer and the
    /// one that leaves the approved placement alone.
    /// </summary>
    private static float[] NormaliseByPercentile(float[] values)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        var percentile = sorted[(int)(EdgePercentile * (sorted.Length - 1))];

        if (percentile <= 0)
        {
            return new float[values.Length];
        }

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = Math.Clamp(values[index] / percentile, 0f, 1f);
        }

        return values;
    }
}
