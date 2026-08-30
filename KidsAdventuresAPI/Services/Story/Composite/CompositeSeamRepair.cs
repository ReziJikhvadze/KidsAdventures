using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// What the centre of a picture measures, and whether that is a seam.
/// </summary>
/// <param name="Baseline">
/// The typical column-to-column change everywhere else in the image — the median, not the mean, so
/// that a few strong vertical edges elsewhere in a scene do not raise the bar the centre is judged
/// against.
/// </param>
/// <param name="Centre">The strongest column-to-column change within the narrow centre band.</param>
/// <param name="Ratio">
/// <paramref name="Centre"/> over <paramref name="Baseline"/>: how many times more abruptly the
/// picture changes at its centre than it does anywhere else. A picture with no seam sits near 1.
/// </param>
/// <param name="FirstColumn">The first column of the run that will be repaired, or -1 for none.</param>
/// <param name="LastColumn">The last column of that run, or -1.</param>
/// <param name="OffsetFraction">
/// Where the strongest change sits, as a signed fraction of the width away from the exact centre —
/// -0.02 is two per cent left of centre. Recorded because the seam that prompted the band being
/// widened was not at the centre at all: it sat at 52.5% of the width, two and a half per cent out,
/// and a gate that only ever looked at the exact middle reported nothing wrong with it.
/// </param>
public sealed record SeamMeasurement(
    double Baseline, double Centre, double Ratio, int FirstColumn, int LastColumn,
    double OffsetFraction = 0)
{
    /// <summary>Whether this picture has a seam worth repairing, and a run of columns to repair.</summary>
    public bool Exceeded => Ratio > CompositeSeamRepair.Threshold && FirstColumn >= 0;

    public int ColumnCount => FirstColumn < 0 ? 0 : LastColumn - FirstColumn + 1;
}

/// <summary>
/// The last line of defence against the painted seam: measure the centre of every generated
/// picture, and paint out the band when one is there.
///
/// The prompts were the first line and they did most of the work. v1.1 removed every mention of a
/// fold from the image template — the models had been painting the fold they were told about, at
/// 35 to 68 times the baseline column change — and the books that came back afterwards were far
/// better. Not clean, though: a faint line still appears at the exact centre of some spreads, which
/// is what this repairs. A prompt is a request; a picture that ships is a fact.
///
/// Deterministic and cheap by design. No model call, no second generation, no judgement: it is
/// arithmetic over columns of pixels, it runs on every base before the reviewer or the compositor
/// sees one, and it either changes nothing or changes at most four columns out of fifteen hundred.
/// The one thing it must never do is repair a picture that has no seam — a legitimate vertical
/// feature at the exact centre of a scene is a tree, a doorway or a horizon post, and smearing it
/// would be a defect this code introduced. Hence the narrow band, the median baseline and a
/// threshold five times above normal, when the defect measured seven times that.
/// </summary>
public static class CompositeSeamRepair
{
    /// <summary>
    /// How many times the baseline column change the centre may reach before it is a seam.
    ///
    /// Five, between a picture's ordinary variation (about one) and the measured defect (35 to 68).
    /// Wide of both, which is what a gate wants to be: the cost of a missed seam is a faint line on
    /// a printed page, and the cost of a false positive is four smeared columns of somebody's
    /// artwork.
    /// </summary>
    public const double Threshold = 5.0;

    /// <summary>
    /// How far either side of the exact centre a seam may sit, as a fraction of the width.
    ///
    /// Three columns of slack was the first guess and it was too tight to catch the defect it was
    /// written for: the band on the refused image sat at 52.5% of the width — about forty columns
    /// out on a 1536-wide render — and the gate looked straight past it. Four per cent either side
    /// covers where a model actually paints these, and still stops a long way short of the reserved
    /// text third's boundary at 33%, which is a real content edge and prompt territory.
    /// </summary>
    public const double CentreBandFraction = 0.04;

    /// <summary>
    /// The widest run this repairs. Past eight columns it is not a seam, it is a feature — and a
    /// feature is left alone rather than trimmed to fit, because trimming would repair part of
    /// somebody's artwork and leave the rest.
    /// </summary>
    public const int MaxRepairColumns = 8;

    /// <summary>
    /// Measures the centre of one PNG.
    ///
    /// The statistic is the mean absolute difference between each adjacent pair of columns, over
    /// every row and the three colour channels — the same measurement the scout used on the books
    /// that came back with the band, so the numbers in the logs are comparable to the numbers in
    /// the defect report.
    /// </summary>
    public static SeamMeasurement Measure(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);

        using var image = Image.Load<Rgba32>(png);
        return Measure(image);
    }

    private static SeamMeasurement Measure(Image<Rgba32> image)
    {
        var width = image.Width;
        var height = image.Height;

        if (width < 8 || height < 1)
        {
            return new SeamMeasurement(0, 0, 0, -1, -1);
        }

        // differences[x] is the change between column x and column x+1.
        var differences = new double[width - 1];

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);

                for (var x = 0; x < row.Length - 1; x++)
                {
                    var left = row[x];
                    var right = row[x + 1];

                    differences[x] +=
                        Math.Abs(left.R - right.R)
                        + Math.Abs(left.G - right.G)
                        + Math.Abs(left.B - right.B);
                }
            }
        });

        for (var x = 0; x < differences.Length; x++)
        {
            differences[x] /= height * 3.0;
        }

        var centre = width / 2;
        var slack = Math.Max(3, (int)Math.Round(width * CentreBandFraction));
        var from = Math.Max(0, centre - slack);
        var to = Math.Min(differences.Length - 1, centre + slack);

        // The baseline is everything the centre band is not. A median rather than a mean: a scene
        // with a few hard vertical edges has a handful of large values, and a mean would let the
        // picture's own strongest features hide a seam.
        var outside = new List<double>(differences.Length);
        for (var x = 0; x < differences.Length; x++)
        {
            if (x < from || x > to)
            {
                outside.Add(differences[x]);
            }
        }

        var baseline = Median(outside);

        var peak = 0.0;
        var peakAt = -1;
        for (var x = from; x <= to; x++)
        {
            if (differences[x] > peak)
            {
                peak = differences[x];
                peakAt = x;
            }
        }

        // A flat image — a solid colour, a test fixture — has no baseline to be a multiple of, and
        // nothing to repair either. Reported as a ratio of zero rather than an infinity.
        var floor = Math.Max(baseline, 0.05);
        var ratio = peak / floor;

        if (peakAt < 0 || ratio <= Threshold)
        {
            return new SeamMeasurement(
                baseline, peak, ratio, -1, -1,
                peakAt < 0 ? 0 : (double)(peakAt - centre) / width);
        }

        /*
          Which columns to repair.

          differences[x] is the boundary between columns x and x+1, so a band of dark columns from a
          to b shows up as TWO elevated boundaries — one at a-1 where the picture drops into the
          band, one at b where it climbs back out — with quiet boundaries between them, because the
          middle of a uniform band does not change. The run is therefore the outermost elevated
          boundaries inside the centre band rather than a contiguous walk from the peak: a walk
          stops at the quiet middle and repairs one edge of the seam, leaving the other.

          Which makes the arithmetic: the first dark column is one past the first elevated boundary,
          and the last dark column is the last elevated boundary itself.
        */
        var elevated = Math.Max(floor * 2, 0.1);

        /*
          The run is looked for around the PEAK, not across the whole band.

          The band is wide now — four per cent of the width, some hundred and twenty columns on a
          spread — and the outermost elevated boundaries in a window that size can belong to two
          unrelated things: the seam, and an ordinary edge in the picture that happens to fall
          inside the band. Pairing those would span them both and interpolate away everything
          between. A seam is a narrow band with two edges of its own, so the second edge is looked
          for within a seam's width of the first.
        */
        var windowFrom = Math.Max(from, peakAt - MaxRepairColumns);
        var windowTo = Math.Min(to, peakAt + MaxRepairColumns);

        var firstBoundary = -1;
        var lastBoundary = -1;

        for (var x = windowFrom; x <= windowTo; x++)
        {
            if (differences[x] <= elevated)
            {
                continue;
            }

            if (firstBoundary < 0)
            {
                firstBoundary = x;
            }

            lastBoundary = x;
        }

        var first = firstBoundary + 1;
        var last = lastBoundary;
        var offset = (double)(peakAt - centre) / width;

        // One elevated boundary and no second one is a step, not a band: the picture genuinely
        // changes at that column and there is nothing between two edges to interpolate across.
        //
        // And a run wider than a seam is a structure, left alone rather than trimmed to fit — a
        // trimmed repair would smear part of a real feature and leave the rest of it standing.
        if (firstBoundary < 0 || first > last || last - first + 1 > MaxRepairColumns)
        {
            return new SeamMeasurement(baseline, peak, ratio, -1, -1, offset);
        }

        if (first < 1 || last > width - 2)
        {
            return new SeamMeasurement(baseline, peak, ratio, -1, -1, offset);
        }

        return new SeamMeasurement(baseline, peak, ratio, first, last, offset);
    }

    /// <summary>
    /// Measures, and repairs when there is a seam. Returns the picture to use and both readings.
    ///
    /// The repair is a straight linear interpolation across the offending columns, from the intact
    /// column on one side to the intact column on the other, row by row. It is the smallest edit
    /// that removes a one-to-four-column discontinuity without inventing anything: every replaced
    /// pixel lies on the line between two pixels the model actually painted.
    /// </summary>
    /// <returns>
    /// The repaired PNG when a seam was found and fixed, the original bytes otherwise; the reading
    /// before, and the reading after — which is what a log needs to show the repair worked.
    /// </returns>
    public static (byte[] Png, SeamMeasurement Before, SeamMeasurement After) Gate(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);

        using var image = Image.Load<Rgba32>(png);

        var before = Measure(image);
        if (!before.Exceeded)
        {
            return (png, before, before);
        }

        Interpolate(image, before.FirstColumn, before.LastColumn);

        var after = Measure(image);

        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });

        return (buffer.ToArray(), before, after);
    }

    /// <summary>
    /// Replaces a run of columns with the straight line between their intact neighbours.
    ///
    /// Alpha is left exactly as it was: the spread bases are opaque, and a repair that touched
    /// transparency would be a change nobody asked for on the one channel the compositor cares
    /// about.
    /// </summary>
    private static void Interpolate(Image<Rgba32> image, int first, int last)
    {
        var left = first - 1;
        var right = last + 1;
        var span = right - left;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);

                var a = row[left];
                var b = row[right];

                for (var x = first; x <= last; x++)
                {
                    var t = (double)(x - left) / span;

                    row[x] = new Rgba32(
                        (byte)Math.Round(a.R + ((b.R - a.R) * t)),
                        (byte)Math.Round(a.G + ((b.G - a.G) * t)),
                        (byte)Math.Round(a.B + ((b.B - a.B) * t)),
                        row[x].A);
                }
            }
        });
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();

        var middle = values.Count / 2;

        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2;
    }
}
