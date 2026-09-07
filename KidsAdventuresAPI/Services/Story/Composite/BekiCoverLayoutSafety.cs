using System.Security.Cryptography;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>Known visual bounds, in mm from the top-left of the full 512×245 wrap.</summary>
public sealed record BekiCoverProtectedArea(string Kind, string Description,
    double X, double Y, double Width, double Height);

/// <summary>A human observation of specific base pixels, not an automatic face-detection claim.</summary>
public sealed record BekiCoverLayoutReview(string BaseSha256, string Reviewer,
    DateTimeOffset ReviewedAtUtc, IReadOnlyList<BekiCoverProtectedArea> Areas);

public static class BekiCoverLayoutSafety
{
    public const string Gate = "COVER_LAYOUT_SAFETY";

    public static void VerifySource(BekiCoverLayoutReview review, byte[] basePng)
    {
        var hash = Convert.ToHexString(SHA256.HashData(basePng));
        if (!hash.Equals(review.BaseSha256, StringComparison.OrdinalIgnoreCase))
            throw Failure("the recorded visual bounds belong to a different cover base; review this cover again.");
        if (string.IsNullOrWhiteSpace(review.Reviewer) || review.ReviewedAtUtc == default)
            throw Failure("the visual bounds have no reviewer/date provenance.");
        ValidateAreas(review.Areas);
    }

    public static void ValidateAreas(IReadOnlyList<BekiCoverProtectedArea>? areas)
    {
        if (areas is null || areas.Count is < 1 or > 20 || !areas.Any(a => a?.Kind == "head"))
            throw Failure("record the child's whole head/hair/face bounds, and any important cover details.");
        foreach (var area in areas)
        {
            if (area is null || area.Kind is not ("head" or "important_detail")
                || string.IsNullOrWhiteSpace(area.Description) || area.Description.Length > 300
                || !double.IsFinite(area.X) || !double.IsFinite(area.Y)
                || !double.IsFinite(area.Width) || !double.IsFinite(area.Height)
                || area.X < 0 || area.Y < 0 || area.Width <= 0 || area.Height <= 0
                || area.X + area.Width > BekiCoverDieline.CanvasWidthMm
                || area.Y + area.Height > BekiCoverDieline.CanvasHeightMm)
                throw Failure("invalid protected bounds; use full-wrap millimetres, not front-crop pixels.");
        }
    }

    /// <param name="titleBox">
    /// The rectangle this book's title is actually set in, from
    /// <see cref="BekiCoverTitlePlacement"/>. Null means the approved box — which is where the title
    /// went on every book printed before 2026-09-08, and is still the answer on any cover the
    /// placement left alone.
    ///
    /// A parameter rather than a second constant because the two questions this gate asks are now
    /// different in kind. The LOGO's clear space is fixed geometry: it is where the mark is stamped,
    /// on every cover, and an observation that lands there is a defect no matter what. The TITLE's
    /// rectangle is a decision made about this cover's own artwork, so a reviewer's "the head is
    /// here" can only be checked against the box this book's title was set in — checking it against
    /// the approved one would fail a cover whose title had already been moved out of the way, and
    /// pass one whose title had been moved onto something else.
    /// </param>
    public static IReadOnlyList<string> Conflicts(
        IReadOnlyList<BekiCoverProtectedArea> areas, BekiCoverTitleChoice? titleBox = null)
    {
        ValidateAreas(areas);
        var conflicts = new List<string>();
        var clearance = BekiCoverDieline.LogoClearSpaceMm;
        var titleLeft = titleBox?.LeftMm ?? BekiCoverDieline.TitleSafeLeftMm;
        var titleTop = titleBox?.TopMm ?? BekiCoverDieline.TitleSafeTopMm;
        var titleWidth = titleBox?.WidthMm ?? BekiCoverDieline.TitleSafeWidthMm;
        var titleHeight = titleBox?.HeightMm ?? BekiCoverDieline.TitleSafeHeightMm;
        foreach (var area in areas)
        {
            if (Intersects(area, titleLeft, titleTop, titleWidth, titleHeight))
                conflicts.Add($"TITLE overlaps {area.Kind}: {area.Description}.");
            if (Intersects(area, BekiCoverDieline.LogoLeftMm - clearance, BekiCoverDieline.LogoTopMm - clearance,
                    BekiCoverDieline.LogoWidthMm + 2 * clearance, BekiCoverDieline.LogoHeightMm + 2 * clearance))
                conflicts.Add($"LOGO clear space overlaps {area.Kind}: {area.Description}.");
        }
        return conflicts;
    }

    public static void EnsureClear(
        IReadOnlyList<BekiCoverProtectedArea>? areas, BekiCoverTitleChoice? titleBox = null)
    {
        // No observation is not a PASS. Production records NOT_REVIEWED separately; the prompt
        // still reserves the zones. Do not invent detector output or buy an extra vision call.
        if (areas is null) return;
        var conflicts = Conflicts(areas, titleBox);
        if (conflicts.Count > 0)
            throw Failure(string.Join(" ", conflicts) + " Correct the cover composition before export.");
    }

    private static bool Intersects(BekiCoverProtectedArea a, double x, double y, double w, double h) =>
        a.X < x + w && a.X + a.Width > x && a.Y < y + h && a.Y + a.Height > y;

    private static BekiLayoutException Failure(string reason) =>
        new(CompositeFailureCodes.PrintPreflightFailed, $"{Gate}: {reason}");
}
