using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// Rebuilding a book that has already been sold, from the bytes it was made of.
///
/// A maintenance rig rather than a check, in the shape the Live tests use: skipped unless somebody
/// sets the environment variables, because the thing it does is write a file over a real customer's
/// deliverable and no suite should do that by accident. It exists because two defects shipped
/// together in one campaign — a cream wash the owner had ruled against, and a Ghostscript colour
/// space with no profile in it that made Chrome draw the whole book as flat pages — and the families
/// who bought those books need the corrected file, not an apology.
///
/// **It calls no model.** Everything a book is made of is already in the blob store: the eight
/// generated spreads, the cover wrap composite, the normalized story, and the layout receipts of the
/// run that produced the broken PDF. The rebuild reads those, composes the book again with today's
/// code, and prepares it with today's preflight. Nothing is generated, nothing is paid for, and the
/// artwork the parent already has on screen is byte-for-byte the artwork that goes back into the
/// file.
///
/// **It proves it rebuilt the same book.** The child's name, age and world are not stored as fields
/// anywhere — they were arguments to a run that finished — so they are recovered by reading the
/// intro spread's own lines back out of the stored receipt and inverting the templates that produced
/// them. That would be a guess if it were not checked, so it is checked: the rebuilt book's intro
/// lines and every spread's wrapped lines must equal the stored ones, character for character. A
/// wrong name, a wrong age or a wrong world all show up there immediately.
/// </summary>
/// <remarks>
/// <c>BEKI_REBUILD_PACK</c>: <c>&lt;userId&gt;:&lt;packId&gt;</c>.
/// <c>BEKI_LOCALBLOB_ROOT</c>: the folder holding <c>adventurepacks/</c>.
/// <c>BEKI_REBUILD_OUT</c>: where to write the finished PDF.
/// </remarks>
public class BekiPackRebuildTests(ITestOutputHelper output)
{
    private static string? Pack => Environment.GetEnvironmentVariable("BEKI_REBUILD_PACK");

    private static string? BlobRoot => Environment.GetEnvironmentVariable("BEKI_LOCALBLOB_ROOT");

    private static string? OutputPath => Environment.GetEnvironmentVariable("BEKI_REBUILD_OUT");

    [SkippableFact]
    public void Rebuild_a_stored_packs_reading_copy_from_its_own_artwork()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Pack) || !Pack!.Contains(':', StringComparison.Ordinal),
            "Set BEKI_REBUILD_PACK to \"<userId>:<packId>\" to run this.");
        Skip.If(
            string.IsNullOrWhiteSpace(BlobRoot) || !Directory.Exists(BlobRoot),
            "Set BEKI_LOCALBLOB_ROOT to the folder that holds adventurepacks/ to run this.");
        Skip.If(
            string.IsNullOrWhiteSpace(OutputPath),
            "Set BEKI_REBUILD_OUT to the path the rebuilt PDF should be written to.");

        var split = Pack!.Split(':', 2);
        var userId = split[0].Trim();
        var packId = split[1].Trim();

        var container = Path.Combine(BlobRoot!, "adventurepacks", userId);
        var folder = Path.Combine(container, packId);

        Assert.True(Directory.Exists(folder), $"No stored pack at '{folder}'.");

        // ---- What the run left behind ----------------------------------------------------------

        var plan = JsonSerializer.Deserialize<MasterStory>(
            File.ReadAllBytes(Path.Combine(folder, "story.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("story.json did not deserialize to a plan.");

        Assert.Equal(BookFormat.SpreadCount, plan.Spreads.Count);

        var wrap = File.ReadAllBytes(
            Path.Combine(container, $"{packId}-cover-wrap-composite.png"));

        var spreads = plan.Spreads
            .OrderBy(spread => spread.Number)
            .Select(spread => new BekiSpreadArtwork(
                spread.Number,
                File.ReadAllBytes(Path.Combine(folder, $"spread-{spread.Number:00}.png"))))
            .ToList();

        var storedReceipts = StoredReadingReceipts(folder);
        var personalization = RecoverPersonalization(folder, storedReceipts, plan.Concept.Title);

        output.WriteLine(
            $"rebuilding {plan.Concept.Title} — {personalization.ChildName}, {personalization.Age}, "
            + $"{personalization.Theme} ({personalization.WorldName}); "
            + $"{spreads.Count} spreads, wrap {wrap.Length:N0} bytes");

        // ---- The book, composed again ----------------------------------------------------------

        // Production's own layout: the pack was built with the configured defaults, and a rebuild
        // that quietly used a test layout would produce a different book from the one being fixed.
        var composer = new BekiPdfComposer(Options.Create(new BekiPrintLayoutOptions()));
        var reading = composer.ComposeReading(plan, wrap, spreads, personalization);

        // The owner's ruling of 2026-09-01 (the fourth) in the artefact: a translucent panel under
        // the copy of the intro and of every story spread that set copy, and none anywhere else —
        // not on the covers, not on the endpapers, not on the credits page, and not on a spread
        // whose text went missing and printed as artwork alone.
        Assert.All(reading.Receipts.Pages, page => Assert.Equal(
            (page.Role == "intro" || page.Role.StartsWith("spread-", StringComparison.Ordinal))
                && page.TextLines.Count > 0,
            page.Wash is not null));

        // And it is the same book. The stored receipts are the broken run's own record of what it
        // set; if the recovered name, age or world were wrong, these lines would differ.
        foreach (var stored in storedReceipts)
        {
            var rebuilt = reading.Receipts.Pages.SingleOrDefault(page => page.Role == stored.Role);
            Assert.True(rebuilt is not null, $"the rebuilt book has no '{stored.Role}' page.");
            Assert.Equal(stored.TextLines, rebuilt!.TextLines);
        }

        // ---- The deliverable -------------------------------------------------------------------

        var (prepared, reportJson) = BekiDigitalPrep.Prepare(
            reading.Pdf, new BekiPrintPrepOptions());

        using var report = JsonDocument.Parse(reportJson);
        Assert.Equal("PASS", report.RootElement.GetProperty("verdict").GetString());
        Assert.Equal("DIGITAL_GEOMETRY", report.RootElement.GetProperty("gate").GetString());

        // The defect this rebuild exists for, read off the finished bytes three ways: the gate's own
        // check, the literal shape Ghostscript wrote into the shipped file, and Poppler's stderr.
        Assert.Empty(BekiDigitalPrep.IccProfileProblems(prepared));
        Assert.DoesNotContain(
            "/N 3/Length 0", Encoding.Latin1.GetString(prepared), StringComparison.Ordinal);
        Poppler.AssertRendersCleanly(prepared);

        var destination = OutputPath!;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, prepared);
        File.WriteAllText(Path.ChangeExtension(destination, ".report.json"), reportJson);

        var colour = report.RootElement.GetProperty("colour");
        output.WriteLine($"wrote {destination} — {prepared.Length:N0} bytes");
        output.WriteLine($"icc profiles: {colour.GetProperty("icc_profiles")}");
        output.WriteLine($"restamped: {colour.GetProperty("icc_restamped_for_pdfwrite")}");
    }

    // ==============================================================================================
    // Reading a finished run's own evidence
    // ==============================================================================================

    private sealed record StoredPage(string Role, IReadOnlyList<string> TextLines);

    /// <summary>
    /// The reading-mode layout receipts the broken run stored, page by page.
    ///
    /// The per-page files rather than the combined document, because the combined one is what a
    /// gate reads and the per-page ones are what fulfillment writes first; a pack interrupted after
    /// the pages and before the roll-up still has these.
    /// </summary>
    private static List<StoredPage> StoredReadingReceipts(string folder)
    {
        var receipts = Path.Combine(folder, "receipts");
        Assert.True(Directory.Exists(receipts), $"No stored layout receipts at '{receipts}'.");

        var pages = new List<StoredPage>();

        foreach (var path in Directory
            .GetFiles(receipts, "reading-page-*-layout.json")
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;

            var lines = root.TryGetProperty("text_lines", out var text)
                ? text.EnumerateArray().Select(line => line.GetString() ?? string.Empty).ToList()
                : [];

            // Only the pages that actually set copy: a receipt with no lines proves nothing about
            // whether the right child's book was rebuilt.
            if (lines.Count > 0)
            {
                pages.Add(new StoredPage(root.GetProperty("role").GetString()!, lines));
            }
        }

        Assert.NotEmpty(pages);
        return pages;
    }

    /// <summary>
    /// The child, the age and the world, recovered from the book's own intro spread.
    ///
    /// None of the three is stored as a field: they were arguments to a fulfilment run, and what
    /// survives it is the sentences they produced. So the templates are run backwards — the
    /// dedication gives the name in the dative, the quiet line under it gives the age — and the
    /// world comes from the theme the composition manifest names, through the same
    /// <see cref="StoryWorlds"/> table the original run read it from.
    ///
    /// Every step of that is a guess until it is checked, and the caller checks it: the rebuilt
    /// intro's lines have to equal the stored ones exactly.
    /// </summary>
    private static BekiBookPersonalization RecoverPersonalization(
        string folder, IReadOnlyList<StoredPage> receipts, string title)
    {
        var layout = new BekiPrintLayoutOptions();

        var intro = receipts.SingleOrDefault(page => page.Role == "intro")
            ?? throw new InvalidOperationException(
                "the stored receipts carry no intro page, so the child this book was made for "
                + "cannot be recovered from them.");

        var name = Captured(intro.TextLines, layout.IntroBelongsTemplate, "{name_dative}")
            ?? throw new InvalidOperationException(
                "the stored intro receipt does not carry a dedication line in the configured "
                + "template's shape, so the child's name cannot be recovered.");

        var age = Captured(intro.TextLines, layout.IntroAgeTemplate, "{age}")
            ?? throw new InvalidOperationException(
                "the stored intro receipt does not carry an age line in the configured template's "
                + "shape, so the child's age cannot be recovered.");

        var theme = ThemeFromManifest(folder);

        return new BekiBookPersonalization(
            UndoDative(name),
            int.Parse(age, CultureInfo.InvariantCulture),
            // The intro prints no date — a reprint has to be the same book as the one that was
            // bought — so any instant produces the same fourteen pages, and this one says plainly
            // that it is not the purchase date.
            DateTime.UnixEpoch,
            theme.ToString(),
            StoryWorlds.For(theme).Place);

        static string? Captured(
            IReadOnlyList<string> lines, string template, string placeholder)
        {
            var index = template.IndexOf(placeholder, StringComparison.Ordinal);
            if (index < 0) return null;

            var pattern = "^" + Regex.Escape(template[..index]) + "(.+?)"
                + Regex.Escape(template[(index + placeholder.Length)..]) + "$";

            return lines
                .Select(line => Regex.Match(line.Trim(), pattern))
                .FirstOrDefault(match => match.Success)
                ?.Groups[1].Value.Trim();
        }
    }

    /// <summary>
    /// A Georgian name with its dative ending taken back off — the inverse of
    /// <see cref="GeorgianNameSuffix.Dative"/>, and checked against it before it is believed.
    /// </summary>
    private static string UndoDative(string dative)
    {
        foreach (var candidate in new[]
        {
            dative.EndsWith("-" + GeorgianNameSuffix.DativeSuffix, StringComparison.Ordinal)
                ? dative[..^2]
                : dative,
            dative.EndsWith(GeorgianNameSuffix.DativeSuffix, StringComparison.Ordinal)
                ? dative[..^1]
                : dative,
            dative,
        })
        {
            if (GeorgianNameSuffix.Dative(candidate) == dative)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"'{dative}' is not any name this book's dedication template could have produced.");
    }

    /// <summary>
    /// The world this book was drawn for, from the illustration contract the run recorded.
    ///
    /// The contract's first entry is the composite pipeline's own fingerprint, and the theme is one
    /// of its fields — which makes it the only place in the stored pack where the parent's choice
    /// survives as a name rather than as a picture.
    /// </summary>
    private static ThemeType ThemeFromManifest(string folder)
    {
        using var manifest = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(folder, "fulfilment.json")));

        var themes = Enum.GetValues<ThemeType>();

        foreach (var entry in manifest.RootElement.GetProperty("illustrationContract").EnumerateArray())
        {
            foreach (var field in (entry.GetString() ?? string.Empty).Split('|'))
            {
                var match = themes.FirstOrDefault(
                    theme => string.Equals(theme.ToString(), field, StringComparison.OrdinalIgnoreCase));

                if (match != default)
                {
                    return match;
                }
            }
        }

        throw new InvalidOperationException(
            "the stored fulfilment manifest names no theme, so the intro spread's approved "
            + "background cannot be resolved for the rebuild.");
    }
}
