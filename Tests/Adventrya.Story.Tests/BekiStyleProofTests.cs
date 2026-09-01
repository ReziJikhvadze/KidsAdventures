using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;
using Xunit.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// The style proof sheet: one real spread of one real book, set ten ways, for the owner to choose
/// from.
///
/// A maintenance rig rather than a check, in <see cref="BekiPackRebuildTests"/>'s shape — skipped
/// unless somebody sets the environment variable, because what it does is write ten multi-megabyte
/// files into a person's Downloads folder and no suite should do that by accident.
///
/// It exists because of the shape of the question. "I do not like the border" is not a defect with a
/// measurement behind it: the rim's strength was settled by counting pixels
/// (<c>BekiTextRimReadabilityTests</c>), and pixel counting can prove a treatment is READABLE while
/// having nothing at all to say about whether it is the right one. Only the owner can answer that,
/// and only by looking.
///
/// **It calls no model and spends nothing.** The artwork is the stored pack's own PNG, the words are
/// the stored story's own Georgian, and the renderer is the one that sets sold books.
///
/// **Every sample goes through the production text machinery.** Not a drawing of what the type would
/// look like: <see cref="BekiPdfComposer.RenderStyleProofSpread"/> composes the real page through the
/// real <c>ComposeSpread</c>, with the registered Noto faces, the real offset-stack rim and the real
/// column arithmetic. That is the whole point of the rig — the owner picks a sample, and production
/// prints THAT, because the sample was already production.
/// </summary>
/// <remarks>
/// <c>BEKI_STYLE_PROOF</c>: <c>&lt;pack folder&gt;|&lt;spread number&gt;|&lt;output folder&gt;</c>.
/// The pack folder is the one holding <c>story.json</c> and <c>spread-NN.png</c>. A pipe separates
/// the three because a filesystem path may hold a colon and never holds a pipe.
/// </remarks>
public class BekiStyleProofTests(ITestOutputHelper output)
{
    private static string? Request => Environment.GetEnvironmentVariable("BEKI_STYLE_PROOF");

    /// <summary>
    /// The designers' own sheets, as <c>PREFIX=path</c> pairs separated by commas — the second round
    /// of this rig, after the owner turned the first ten down.
    ///
    /// A file per designer rather than a list in this file, because the whole point of the round is
    /// that the taste in it is not the composer's: three people were each asked for fifteen, and what
    /// they wrote is rendered as written. Read as data and never as instruction — a spec names
    /// colours, sizes and a weight, and nothing in the loader below can act on anything else it finds.
    /// </summary>
    private static string? SpecRequest => Environment.GetEnvironmentVariable("BEKI_STYLE_PROOF_SPECS");

    /// <summary>
    /// Whether a sample whose PNG is already in the output folder is read back rather than rendered
    /// again.
    ///
    /// For the round that adds to a batch already on the owner's desk. The combined contact sheet has
    /// to show every variant, old and new, which means every variant has to be a cell — but the ones
    /// already delivered must come back byte-identical and, more to the point, must not be rewritten
    /// at all. Reading them off disk is the only version of that with no "should be the same" in it.
    ///
    /// Off by default, and every reuse is named in the run's output: silently skipping a render is
    /// exactly how a changed spec would come back as its old picture.
    /// </summary>
    private static bool ReuseExisting =>
        Environment.GetEnvironmentVariable("BEKI_STYLE_PROOF_REUSE") is { Length: > 0 };

    /// <summary>
    /// The ten treatments on the sheet, in the order they are numbered.
    ///
    /// Sample 1 is the shipped book, labelled so, because a proof sheet with no baseline on it asks
    /// the owner to compare nine samples against a memory. Every other sample changes ONE thing from
    /// it where it can: the rim's thickness, the type's size, the fill's colour, an opacity. The
    /// leading moves with the size and only with the size — the approved reference is 18 on 27 and
    /// every sample keeps that ratio, so a bigger sample is bigger type and not tighter lines.
    ///
    /// The last one carries no rim at all. It is not a candidate so much as the control: dark ink
    /// straight on a busy photograph is what the rim exists to be better than, and the owner should
    /// be able to see the thing being argued against.
    /// </summary>
    private static IReadOnlyList<(string File, string Caption, BekiTextStyleProof? Style)> Styles()
    {
        // Cream, near-black: the two inks the book is set in today.
        const string Cream = "FFF8EB";
        const string RimInk = "0D071D";

        return
        [
            ("BEKI_STYLE_01_CURRENT_18pt_rim9_cream",
             "01  CURRENT — 18pt / 27pt leading, cream #FFF8EB, rim #0D071D 9% of em, 16 steps",
             new BekiTextStyleProof { FontSizePt = 18f, LeadingPt = 27f }),

            ("BEKI_STYLE_02_18pt_rim5_cream",
             "02  thinner border — 18pt, cream #FFF8EB, rim #0D071D 5% of em",
             new BekiTextStyleProof
             {
                 FontSizePt = 18f, LeadingPt = 27f, RimWidthFactor = 0.05f,
             }),

            ("BEKI_STYLE_03_18pt_rim13_cream",
             "03  thicker border — 18pt, cream #FFF8EB, rim #0D071D 13% of em",
             new BekiTextStyleProof
             {
                 FontSizePt = 18f, LeadingPt = 27f, RimWidthFactor = 0.13f,
             }),

            ("BEKI_STYLE_04_20pt_rim9_cream",
             "04  bigger type — 20pt / 30pt leading, cream #FFF8EB, rim #0D071D 9% of em",
             new BekiTextStyleProof { FontSizePt = 20f, LeadingPt = 30f }),

            ("BEKI_STYLE_05_22pt_rim10_cream",
             "05  biggest type — 22pt / 33pt leading, cream #FFF8EB, rim #0D071D 10% of em",
             new BekiTextStyleProof
             {
                 FontSizePt = 22f, LeadingPt = 33f, RimWidthFactor = 0.10f,
             }),

            ("BEKI_STYLE_06_18pt_rim9_purewhite",
             "06  pure white — 18pt, fill #FFFFFF, rim #0D071D 9% of em",
             new BekiTextStyleProof
             {
                 FontSizePt = 18f, LeadingPt = 27f, FillColorHex = "FFFFFF",
             }),

            ("BEKI_STYLE_07_18pt_rim9_cream70",
             "07  translucent letters — 18pt, cream #FFF8EB at 70% opacity, rim #0D071D 9% of em",
             new BekiTextStyleProof
             {
                 FontSizePt = 18f, LeadingPt = 27f, FillOpacity = 0.70f,
             }),

            ("BEKI_STYLE_08_18pt_rim12_rim60",
             "08  soft halo — 18pt, cream #FFF8EB, rim #0D071D 12% of em at 60% opacity",
             new BekiTextStyleProof
             {
                 FontSizePt = 18f, LeadingPt = 27f, RimWidthFactor = 0.12f, RimOpacity = 0.60f,
             }),

            ("BEKI_STYLE_09_20pt_rim11_white_rimblack",
             "09  white on black rim — 20pt / 30pt, fill #FFFFFF, rim #000000 11% of em",
             new BekiTextStyleProof
             {
                 FontSizePt = 20f, LeadingPt = 30f,
                 FillColorHex = "FFFFFF", RimColorHex = "000000", RimWidthFactor = 0.11f,
             }),

            ("BEKI_STYLE_10_norim_darkink",
             "10  contrast reference — 18pt, dark ink #241A33, NO rim, straight on the artwork",
             new BekiTextStyleProof
             {
                 FontSizePt = 18f, LeadingPt = 27f,
                 FillColorHex = "241A33", RimWidthFactor = 0f,
             }),

            // The eleventh entry is not on the sheet. It is the shipped path with no proof style at
            // all, rendered so that sample 1 can be checked against it byte for byte — which is the
            // only way to know the parameterization did not quietly move production.
            ("BEKI_STYLE_00_shipped_path_control", "control", null),
        ];
    }

    [SkippableFact]
    public void Render_one_real_spread_in_ten_text_treatments_for_the_owner_to_choose_from()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Request),
            "Set BEKI_STYLE_PROOF to \"<pack folder>|<spread number>|<output folder>\" to run this.");

        var parts = Request!.Split('|');
        Skip.If(parts.Length != 3, "BEKI_STYLE_PROOF wants three pipe-separated parts.");

        var folder = parts[0].Trim();
        var spreadNumber = int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
        var outputFolder = parts[2].Trim();

        Assert.True(Directory.Exists(folder), $"No stored pack at '{folder}'.");
        Directory.CreateDirectory(outputFolder);

        // ---- The book this is a spread of ----------------------------------------------------

        var plan = JsonSerializer.Deserialize<MasterStory>(
            File.ReadAllBytes(Path.Combine(folder, "story.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("story.json did not deserialize to a plan.");

        var spread = plan.Spreads.Single(page => page.Number == spreadNumber);
        var artwork = File.ReadAllBytes(Path.Combine(folder, $"spread-{spreadNumber:00}.png"));

        var side = AdventurePacks.Api.Services.Story.Prompts.BekiSpreadRhythm.TextSideFor(spreadNumber);

        output.WriteLine($"proofing “{plan.Concept.Title}” spread {spreadNumber}, text on the {side}");
        output.WriteLine($"artwork {artwork.Length:N0} bytes; copy: {spread.Text.Replace('\n', ' ')}");

        // Production's own layout, exactly as the pack was built with it. A proof rendered on a test
        // layout would be a proof of a book nobody is printing.
        var composer = new BekiPdfComposer(Options.Create(new BekiPrintLayoutOptions()));

        // No personalization: the only thing the composer reads it for on a story spread is the age
        // band the step-down ladder starts at, and a proof states its size rather than fitting one.
        // The shipped-path control therefore starts at BekiPrintLayoutOptions.StoryFontSize — 18 pt,
        // which is what sample 1 declares.
        BekiBookPersonalization? personalization = null;

        // ---- The samples -----------------------------------------------------------------------

        var written = new List<(string File, string Caption, byte[] Png)>();

        foreach (var (file, caption, style) in Styles())
        {
            var png = composer.RenderStyleProofSpread(spread, artwork, personalization, style);

            Assert.True(png.Length > 100_000,
                $"{file} came back {png.Length:N0} bytes, which is not a rendered spread.");

            written.Add((file, caption, png));
        }

        const string Control = "BEKI_STYLE_00_shipped_path_control";

        var control = written.Single(sample => sample.File == Control);
        var current = written.Single(sample => sample.File == "BEKI_STYLE_01_CURRENT_18pt_rim9_cream");

        // The refactor's own proof, on the artefact rather than in an argument: the shipped path and
        // the style that claims to describe it produce the same pixels.
        Assert.Equal(Sha256(control.Png), Sha256(current.Png));

        var samples = written.Where(sample => sample.File != Control).ToList();

        Assert.Equal(10, samples.Count);

        // Ten treatments have to be ten pictures. Two identical hashes would mean a style the real
        // machinery cannot express — which is exactly the thing this rig must not hide.
        var hashes = samples.ToDictionary(sample => sample.File, sample => Sha256(sample.Png));
        Assert.Equal(10, hashes.Values.Distinct(StringComparer.Ordinal).Count());

        foreach (var (file, _, png) in samples)
        {
            var destination = Path.Combine(outputFolder, file + ".png");
            File.WriteAllBytes(destination, png);

            var size = Image.Identify(png);
            output.WriteLine(
                $"{file}.png — {size.Width}×{size.Height}, {png.Length:N0} bytes, "
                + $"sha256 {hashes[file][..12]}");

            Assert.True(File.Exists(destination));
            Assert.True(new FileInfo(destination).Length == png.Length);
        }

        // ---- The sheet -------------------------------------------------------------------------

        var sheet = ContactSheet(samples, columns: 2, thumbnailWidth: 900, captionSize: 14);
        var sheetPath = Path.Combine(outputFolder, "BEKI_STYLE_CONTACT_SHEET.png");
        File.WriteAllBytes(sheetPath, sheet);

        var sheetSize = Image.Identify(sheet);
        output.WriteLine(
            $"BEKI_STYLE_CONTACT_SHEET.png — {sheetSize.Width}×{sheetSize.Height}, "
            + $"{sheet.Length:N0} bytes");

        Assert.True(sheet.Length > 100_000);
    }

    /// <summary>
    /// A batch of treatments written down somewhere else, rendered on the same spread as the first
    /// ten — the shape every round after the first one takes.
    ///
    /// The rounds differ in what is being asked, and the rig deliberately does not: the first round
    /// was ten hand-written candidates, the second was three designers' free taste, and the third is
    /// a systematic matrix over the four variables the owner left open — size, weight, border width
    /// and opacity, and fill transparency, on cream or white only. What stays the same is that the
    /// batch is DATA. This method reads size, leading, weight, two colours, two opacities and the
    /// rim's geometry from a file and honours no other field, so a round can change its mind about
    /// taste without anything here having an opinion about it.
    ///
    /// What it does refuse is a value that would waste a cell. A rim past
    /// <see cref="RimFactorCap"/> of the em stops being a border and starts being a blot that closes
    /// the counters of the Georgian letters, and an opacity outside 0–1 is not a colour at all.
    /// Both are clamped, and every clamp is written into the test output by name, because a sample
    /// the owner is choosing from must not silently be a different sample from the one requested.
    /// </summary>
    [SkippableFact]
    public void Render_a_specified_batch_of_text_treatments_on_the_same_spread()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Request),
            "Set BEKI_STYLE_PROOF to \"<pack folder>|<spread number>|<output folder>\" to run this.");
        Skip.If(
            string.IsNullOrWhiteSpace(SpecRequest),
            "Set BEKI_STYLE_PROOF_SPECS to \"<file stem>=<path>\" to run this.");

        var parts = Request!.Split('|');
        Skip.If(parts.Length != 3, "BEKI_STYLE_PROOF wants three pipe-separated parts.");

        var folder = parts[0].Trim();
        var spreadNumber = int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
        var outputFolder = parts[2].Trim();

        Assert.True(Directory.Exists(folder), $"No stored pack at '{folder}'.");
        Directory.CreateDirectory(outputFolder);

        var plan = JsonSerializer.Deserialize<MasterStory>(
            File.ReadAllBytes(Path.Combine(folder, "story.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("story.json did not deserialize to a plan.");

        var spread = plan.Spreads.Single(page => page.Number == spreadNumber);
        var artwork = File.ReadAllBytes(Path.Combine(folder, $"spread-{spreadNumber:00}.png"));

        output.WriteLine(
            $"batch render on “{plan.Concept.Title}” spread {spreadNumber}, "
            + $"copy: {spread.Text.Replace('\n', ' ')}");

        var composer = new BekiPdfComposer(Options.Create(new BekiPrintLayoutOptions()));

        // ---- The batch, read off disk ------------------------------------------------------------

        var loaded = new List<(string File, string Caption, BekiTextStyleProof Style)>();
        var specsByFile = new Dictionary<string, DesignerStyleSpec>(StringComparer.Ordinal);
        var clamps = new List<string>();
        var stems = new List<string>();

        // Numbering runs across the whole request rather than restarting per file, so a round that
        // appends fifteen variants to a batch of thirty-two numbers them 33 to 47 by naming both
        // files in order — and the numbers on the sheet stay the numbers the owner already has.
        var number = 0;

        foreach (var pair in SpecRequest!.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.Split('=', 2);
            Assert.True(split.Length == 2, $"'{pair}' is not a <file stem>=<path> pair.");

            // The stem IS the filename prefix, so the round that commissioned the batch decides what
            // its files are called without this file having to know the round exists.
            var stem = split[0].Trim();
            var path = split[1].Trim();
            stems.Add(stem);

            Assert.True(File.Exists(path), $"No style spec at '{path}'.");

            var specs = ReadSpecFile(path);

            Assert.NotEmpty(specs);

            foreach (var spec in specs)
            {
                number++;
                var id = $"{stem}-{number:00}";

                var caption = spec.IsHollow
                    ? $"{number:00}  {spec.Name}  —  {spec.FontSizePt:0.#}pt/{spec.Leading:0.#}pt "
                      + $"{spec.Weight}, border {spec.Border:0.##}pt {spec.BorderColor}, interior "
                      + (spec.VeilOpacity <= 0f
                          ? "see-through"
                          : $"{spec.VeilColor} @ {spec.VeilOpacity:P0}")
                    : $"{number:00}  {spec.Name}  {spec.FontSizePt:0.#}pt/{spec.Leading:0.#}pt  "
                      + $"{spec.Weight}  fill {spec.FillColorHex} @{spec.FillOpacity:0.##}  "
                      + $"rim {spec.RimColorHex} @{spec.RimOpacity:0.##} × {spec.RimWidthFactor:0.###} em";

                loaded.Add(($"{stem}_{number:00}_{spec.Name}", caption, spec.ToStyle(id, clamps)));
                specsByFile[$"{stem}_{number:00}_{spec.Name}"] = spec;
            }
        }

        output.WriteLine($"{loaded.Count} variants loaded from {SpecRequest}");

        foreach (var note in clamps)
        {
            output.WriteLine($"CLAMPED: {note}");
        }

        // ---- The renders -----------------------------------------------------------------------

        var written = new List<(string File, string Caption, byte[] Png)>();

        // The two ingredients a hollow sample is made of, remembered across the batch: the artwork
        // page with no ink on it (one, for every variant) and a glyph coverage mask per distinct
        // size/leading/weight. Fifteen hollow variants over four type settings is four mask renders
        // rather than fifteen.
        var grounds = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var masks = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var (file, caption, style) in loaded)
        {
            var destination = Path.Combine(outputFolder, file + ".png");

            // Already delivered: read it back, leave the file alone, and say so. The cell on the
            // combined sheet is then literally the picture the owner already has, not a re-render
            // that ought to match it.
            if (ReuseExisting && File.Exists(destination))
            {
                var existing = File.ReadAllBytes(destination);
                output.WriteLine($"REUSED: {file}.png — {existing.Length:N0} bytes, not rewritten");
                written.Add((file, caption, existing));
                continue;
            }

            var spec = specsByFile[file];

            var png = spec.IsHollow
                ? HollowSample(composer, spread, artwork, spec, style, grounds, masks)
                : composer.RenderStyleProofSpread(spread, artwork, personalization: null, style);

            Assert.True(png.Length > 100_000,
                $"{file} came back {png.Length:N0} bytes, which is not a rendered spread.");

            File.WriteAllBytes(destination, png);

            Assert.True(File.Exists(destination));
            output.WriteLine($"{file}.png — {png.Length:N0} bytes");

            written.Add((file, caption, png));
        }

        // N specs have to be N pictures: a duplicate hash would mean two specs the real machinery
        // cannot tell apart, which the owner has to know about rather than squint at.
        var hashes = written.Select(sample => Sha256(sample.Png)).ToList();
        Assert.Equal(written.Count, hashes.Distinct(StringComparer.Ordinal).Count());

        // ---- One sheet, every cell ---------------------------------------------------------------

        // Wide enough to take the batch in at one glance, and no wider: past about four across the
        // cells are too small to see a rim in, which is the thing the sheet exists to show.
        //
        // Overridable, because a batch can have a shape of its own. A seven-step ladder wants to be a
        // ROW: the whole question the ladder asks is what happens between one end of it and the
        // other, and an eye reading it left to right answers that in a way the same seven cells
        // scattered over two rows do not.
        var columns = Environment.GetEnvironmentVariable("BEKI_STYLE_PROOF_COLUMNS") is { Length: > 0 } forced
            && int.TryParse(forced, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wide)
            && wide is > 0 and <= 12
                ? wide
                : written.Count <= 12 ? 2 : (written.Count <= 40 ? 4 : 5);

        // A batch can also name its rows outright — "7,8,8,4" — when the families in it are different
        // lengths and putting each on its own line is the whole point of the sheet. The widest row
        // then sets the grid.
        var rows = Environment.GetEnvironmentVariable("BEKI_STYLE_PROOF_ROWS") is { Length: > 0 } grouped
            ? grouped.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => int.Parse(part.Trim(), CultureInfo.InvariantCulture))
                .ToList()
            : null;

        if (rows is not null)
        {
            Assert.Equal(written.Count, rows.Sum());
            columns = rows.Max();
        }

        var thumbnailWidth = columns >= 8 ? 560 : (columns >= 7 ? 620 : (columns >= 5 ? 700 : 800));
        var captionSize = columns >= 7 ? 11 : 12;

        var sheet = ContactSheet(written, columns, thumbnailWidth, captionSize, rows);
        var sheetName = $"{stems[0]}_CONTACT_{written.Count:00}.png";
        File.WriteAllBytes(Path.Combine(outputFolder, sheetName), sheet);

        var sheetSize = Image.Identify(sheet);
        output.WriteLine(
            $"{sheetName} — {sheetSize.Width}×{sheetSize.Height}, {sheet.Length:N0} bytes");

        Assert.True(sheet.Length > 100_000);
    }

    // ==============================================================================================
    // A designer's sheet, read as data
    // ==============================================================================================

    /// <summary>
    /// One round's spec file, in either of the two shapes a round has written one in.
    ///
    /// A bare array is the common case. An OBJECT of named arrays is what a round writes when the
    /// batch is really two families being compared — <c>{ "hollow": [...], "rimstack": [...] }</c> —
    /// and the families are concatenated in the order the document declares them, which is the order
    /// the round numbered them in. Reading both is a few lines here and saves rewriting somebody
    /// else's sheet into a shape this file happens to prefer.
    /// </summary>
    private static List<DesignerStyleSpec> ReadSpecFile(string path)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var bytes = File.ReadAllBytes(path);

        using var document = JsonDocument.Parse(bytes);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<DesignerStyleSpec>>(bytes, options)
                ?? throw new InvalidOperationException($"'{path}' did not deserialize to a list.");
        }

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);

        var specs = new List<DesignerStyleSpec>();

        foreach (var family in document.RootElement.EnumerateObject())
        {
            Assert.True(
                family.Value.ValueKind == JsonValueKind.Array,
                $"'{path}' has a '{family.Name}' that is not a list of styles.");

            specs.AddRange(
                family.Value.Deserialize<List<DesignerStyleSpec>>(options) ?? []);
        }

        return specs;
    }

    /// <summary>
    /// The widest rim a variant may ask for, as a fraction of the em.
    ///
    /// The shipped rim is 0.09 and the options file records the measurement behind it: at 0.12 the
    /// counters of the round Georgian letters — the bowl of ო, the eye of ფ — are already visibly
    /// narrowing. Past 0.2 they close, and the sample stops being type at all. A designer may go as
    /// far as that line and no further, and the crossing is reported.
    /// </summary>
    private const float RimFactorCap = 0.2f;

    /// <summary>
    /// One line of a designer's sheet, exactly as they wrote it.
    ///
    /// The defaults matter: two of the forty-five omit fields, and a missing rim is the shipped rim
    /// rather than no rim, which is what a designer who did not mention it plainly meant.
    /// </summary>
    private sealed record DesignerStyleSpec
    {
        public string Name { get; init; } = "unnamed";

        public float FontSizePt { get; init; } = 18f;

        /// <summary>
        /// The leading in points, or absent — in which case the book's own 18-on-27 ratio is applied
        /// to whatever size the variant asked for.
        ///
        /// A round that varies one thing should not have to restate the others, and a ladder written
        /// as "20 pt, seven veils" plainly means the leading 20 pt copy is always set on. Defaulting
        /// to a flat 27 instead would quietly tighten every sample above 18 pt and the ladder would
        /// be measuring two things at once.
        /// </summary>
        public float? LeadingPt { get; init; }

        public float Leading => LeadingPt ?? (FontSizePt * 1.5f);

        public string Weight { get; init; } = "Regular";

        public string FillColorHex { get; init; } = "#FFF8EB";

        public float FillOpacity { get; init; } = 1f;

        public string RimColorHex { get; init; } = "#0D071D";

        public float RimOpacity { get; init; } = 1f;

        /// <summary>
        /// True for a see-through letter: an outline with nothing painted inside it, so the
        /// illustration itself is what fills the glyph.
        ///
        /// A different thing entirely from a translucent fill, which is what the owner rejected — a
        /// low-opacity fill still sits on top of the rim stack, so what shows through the letter is
        /// the dark rim behind it rather than the artwork. Hollow letters have no fill at all.
        /// </summary>
        public bool Hollow { get; init; }

        /// <summary>The hollow outline's thickness in points, drawn inside the glyph's own edge.</summary>
        public float BorderWidthPt { get; init; } = 0.8f;

        /// <summary>
        /// How much cream is laid inside the outline, 0–1. Zero is fully see-through; the opacity
        /// ladder is this field walked from a quarter to most of the way, which is the range the
        /// owner asked to see isolated after the fully see-through batch read as too transparent.
        /// </summary>
        public float VeilOpacity { get; init; }

        // ------------------------------------------------------------------------------------------
        // The same three things under the names a later round wrote them in.
        //
        // A spec file is the round's document, not this file's, and the vocabulary drifted once the
        // veil became the subject: "borderPt" rather than "borderWidthPt", "borderColorHex" and
        // "veilColorHex" rather than the rim/fill pair inherited from the solid batches. Reading both
        // spellings is cheaper and more honest than rewriting somebody else's sheet to match a
        // property name — and a file that names a border at all is describing a hollow letter, which
        // is why Hollow does not have to be restated either.
        // ------------------------------------------------------------------------------------------

        public float? BorderPt { get; init; }

        public string? BorderColorHex { get; init; }

        public string? VeilColorHex { get; init; }

        public bool IsHollow => Hollow || BorderPt is not null;

        public float Border => BorderPt ?? BorderWidthPt;

        public string BorderColor => BorderColorHex ?? RimColorHex;

        public string VeilColor => VeilColorHex ?? FillColorHex;

        public float RimWidthFactor { get; init; } = 0.09f;

        public int RimSteps { get; init; } = 16;

        /// <summary>
        /// The spec as the composer's own style, with the two sanity clamps applied and any clamp
        /// appended to <paramref name="clamps"/> for the run's output to carry.
        /// </summary>
        public BekiTextStyleProof ToStyle(string id, List<string> clamps)
        {
            var rim = RimWidthFactor;
            if (rim > RimFactorCap)
            {
                clamps.Add($"{id} {Name}: rimWidthFactor {rim:0.###} → {RimFactorCap:0.###}");
                rim = RimFactorCap;
            }

            if (rim < 0f)
            {
                clamps.Add($"{id} {Name}: rimWidthFactor {rim:0.###} → 0");
                rim = 0f;
            }

            var fillOpacity = Math.Clamp(FillOpacity, 0f, 1f);
            if (Math.Abs(fillOpacity - FillOpacity) > float.Epsilon)
            {
                clamps.Add($"{id} {Name}: fillOpacity {FillOpacity:0.##} → {fillOpacity:0.##}");
            }

            var rimOpacity = Math.Clamp(RimOpacity, 0f, 1f);
            if (Math.Abs(rimOpacity - RimOpacity) > float.Epsilon)
            {
                clamps.Add($"{id} {Name}: rimOpacity {RimOpacity:0.##} → {rimOpacity:0.##}");
            }

            return new BekiTextStyleProof
            {
                FontSizePt = FontSizePt,
                LeadingPt = Leading,
                Weight = Weight,
                FillColorHex = FillColorHex,
                FillOpacity = fillOpacity,
                RimColorHex = RimColorHex,
                RimOpacity = rimOpacity,
                RimWidthFactor = rim,
                RimSteps = RimSteps,
            };
        }
    }

    // ==============================================================================================
    // The background box audition
    // ==============================================================================================

    /// <summary>
    /// A translucent panel behind the copy, at a range of colours and strengths, for the owner to
    /// look at.
    ///
    /// **This is a proof and it changes nothing.** The owner has ruled three times that a Beki book
    /// has no box behind its words, and <see cref="BekiPdfComposer"/> still has no setting that would
    /// draw one — nothing in this section touches it. The panel is painted into the ARTWORK before
    /// the artwork is handed to the composer, which is why the composer needs no box code and its
    /// claim about itself stays literally true. If a box is ever chosen, that is a new ruling and a
    /// separate piece of work in the composer; until then the only place in this repository that can
    /// draw one is a test file that writes PNGs into somebody's Downloads folder.
    ///
    /// The geometry is the historical wash's: the measured text block, padded, with softened corners.
    /// The block is measured the way the hollow samples measure it — off a coverage mask of the real
    /// typeset copy — so the panel sits behind the actual lines rather than behind an estimate of
    /// where they fall.
    /// </summary>
    [SkippableFact]
    public void Render_a_background_box_audition_on_the_same_spread()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(Request),
            "Set BEKI_STYLE_PROOF to \"<pack folder>|<spread number>|<output folder>\" to run this.");

        var boxRequest = Environment.GetEnvironmentVariable("BEKI_STYLE_PROOF_BOXES");
        Skip.If(
            string.IsNullOrWhiteSpace(boxRequest),
            "Set BEKI_STYLE_PROOF_BOXES to \"<file stem>=<path>\" to run this.");

        var parts = Request!.Split('|');
        Skip.If(parts.Length != 3, "BEKI_STYLE_PROOF wants three pipe-separated parts.");

        var folder = parts[0].Trim();
        var spreadNumber = int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
        var outputFolder = parts[2].Trim();

        var pair = boxRequest!.Split('=', 2);
        Assert.True(pair.Length == 2, $"'{boxRequest}' is not a <file stem>=<path> pair.");

        var stem = pair[0].Trim();
        var specPath = pair[1].Trim();

        Assert.True(Directory.Exists(folder), $"No stored pack at '{folder}'.");
        Assert.True(File.Exists(specPath), $"No box spec at '{specPath}'.");
        Directory.CreateDirectory(outputFolder);

        var plan = JsonSerializer.Deserialize<MasterStory>(
            File.ReadAllBytes(Path.Combine(folder, "story.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("story.json did not deserialize to a plan.");

        var spread = plan.Spreads.Single(page => page.Number == spreadNumber);
        var artwork = File.ReadAllBytes(Path.Combine(folder, $"spread-{spreadNumber:00}.png"));

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var sheet = JsonSerializer.Deserialize<BoxAuditionSpec>(File.ReadAllBytes(specPath), options)
            ?? throw new InvalidOperationException($"'{specPath}' did not deserialize.");

        Assert.NotNull(sheet.Text);
        Assert.NotEmpty(sheet.Boxes);

        output.WriteLine(
            $"box audition on “{plan.Concept.Title}” spread {spreadNumber}, "
            + $"{sheet.Boxes.Count} panels behind the same copy");

        var composer = new BekiPdfComposer(Options.Create(new BekiPrintLayoutOptions()));
        var clamps = new List<string>();
        var masks = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        // The artwork at the page's own pixel count, so the panel's edge is drawn at the density it
        // will be looked at rather than drawn small and enlarged with the picture. Sized off a
        // rendered page rather than arithmetic: the page's height in pixels is the rasteriser's
        // rounding of 210 mm, and being one pixel out is exactly the kind of near-miss that makes a
        // panel sit a hair off the copy it belongs to.
        var scaled = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        var written = new List<(string File, string Caption, byte[] Png)>();

        for (var index = 0; index < sheet.Boxes.Count; index++)
        {
            var box = sheet.Boxes[index];
            var number = index + 1;

            // A variant may replace the text wholesale — the two no-rim combinations do, because the
            // question they ask is whether a panel makes the rim unnecessary.
            var text = (box.TextOverride ?? sheet.Text!).ToStyle($"{stem}-{number:00}", clamps);

            var maskKey = $"{text.FontSizePt}|{text.LeadingPt}|{text.Weight}";

            if (!masks.TryGetValue(maskKey, out var mask))
            {
                mask = composer.RenderStyleProofSpread(
                    spread, FlatSheetLike(artwork, new Rgba32(255, 255, 255)), personalization: null,
                    new BekiTextStyleProof
                    {
                        FontSizePt = text.FontSizePt,
                        LeadingPt = text.LeadingPt,
                        Weight = text.Weight,
                        FillColorHex = "000000",
                        FillOpacity = 1f,
                        RimWidthFactor = 0f,
                    });

                masks[maskKey] = mask;
            }

            var page = Image.Identify(mask);
            var scaleKey = $"{page.Width}x{page.Height}";

            if (!scaled.TryGetValue(scaleKey, out var pageArtwork))
            {
                pageArtwork = ArtworkAtPageScale(artwork, page.Width, page.Height);
                scaled[scaleKey] = pageArtwork;
            }

            var boxed = PanelBehindCopy(
                pageArtwork, mask,
                box.PaddingMm, box.CornerRadiusMm,
                ParseHex(box.BoxColorHex), Math.Clamp(box.BoxOpacity, 0f, 1f));

            var png = composer.RenderStyleProofSpread(spread, boxed, personalization: null, text);

            Assert.True(png.Length > 100_000,
                $"{box.Name} came back {png.Length:N0} bytes, which is not a rendered spread.");

            var file = $"{stem}_{number:00}_{box.Name}";
            File.WriteAllBytes(Path.Combine(outputFolder, file + ".png"), png);
            output.WriteLine($"{file}.png — {png.Length:N0} bytes");

            var rim = text.RimWidthFactor <= 0f
                ? "no rim"
                : $"rim {text.RimColorHex} × {text.RimWidthFactor:0.###} em";

            written.Add((
                file,
                $"{number:00}  {box.Name}  —  box {box.BoxColorHex} @ {box.BoxOpacity:P0}, "
                + $"pad {box.PaddingMm:0.#}mm r{box.CornerRadiusMm:0.#}mm  |  text "
                + $"{text.FontSizePt:0.#}pt {text.Weight} {text.FillColorHex}, {rim}",
                png));
        }

        foreach (var note in clamps)
        {
            output.WriteLine($"CLAMPED: {note}");
        }

        var hashes = written.Select(sample => Sha256(sample.Png)).ToList();
        Assert.Equal(written.Count, hashes.Distinct(StringComparer.Ordinal).Count());

        var rows = Environment.GetEnvironmentVariable("BEKI_STYLE_PROOF_ROWS") is { Length: > 0 } grouped
            ? grouped.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => int.Parse(part.Trim(), CultureInfo.InvariantCulture))
                .ToList()
            : null;

        if (rows is not null) Assert.Equal(written.Count, rows.Sum());

        var columns = rows?.Max() ?? 4;
        var contact = ContactSheet(written, columns, 800, captionSize: 12, rows);
        var contactName = $"{stem}_CONTACT_{written.Count:00}.png";
        File.WriteAllBytes(Path.Combine(outputFolder, contactName), contact);

        var size = Image.Identify(contact);
        output.WriteLine($"{contactName} — {size.Width}×{size.Height}, {contact.Length:N0} bytes");

        Assert.True(contact.Length > 100_000);
    }

    /// <summary>One base text style, and the panels to try behind it.</summary>
    private sealed record BoxAuditionSpec
    {
        public DesignerStyleSpec? Text { get; init; }

        public List<BoxSpec> Boxes { get; init; } = [];
    }

    private sealed record BoxSpec
    {
        public string Name { get; init; } = "unnamed";

        public string BoxColorHex { get; init; } = "#281B3F";

        public float BoxOpacity { get; init; } = 0.3f;

        /// <summary>How far the panel reaches past the copy — the historical wash's 6–8 mm range.</summary>
        public float PaddingMm { get; init; } = 7f;

        /// <summary>So the panel reads as a support under the words and not as a cut rectangle.</summary>
        public float CornerRadiusMm { get; init; } = 4f;

        /// <summary>A text style for this variant only, replacing the sheet's base one.</summary>
        public DesignerStyleSpec? TextOverride { get; init; }
    }

    /// <summary>
    /// The spread's artwork resampled to the proof page's own pixel count.
    ///
    /// The composer would enlarge it to exactly this anyway when it places the raster on a 450 × 210
    /// mm page at the proof density; doing it first means the panel's rounded edge is drawn at the
    /// density it will be judged at instead of being drawn small and enlarged with the picture.
    ///
    /// The size is handed in from a page the composer actually rendered rather than worked out here,
    /// because the rasteriser's own rounding of 210 mm is the authority on it and this arithmetic
    /// disagreed with it by one pixel.
    /// </summary>
    private static byte[] ArtworkAtPageScale(byte[] artwork, int width, int height)
    {
        using var image = Image.Load<Rgba32>(artwork);
        image.Mutate(context => context.Resize(width, height));

        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    /// <summary>
    /// The panel, painted onto the artwork before the composer ever sees it.
    ///
    /// Its rectangle is the copy's own: the ink bounding box of a coverage mask of the real typeset
    /// text, grown by the padding on every side. Measured rather than computed from the column's
    /// millimetres, for the same reason the hollow letters are — the block that is actually set is
    /// the only block worth putting a panel behind.
    /// </summary>
    private static byte[] PanelBehindCopy(
        byte[] pageArtwork, byte[] maskPng,
        float paddingMm, float cornerRadiusMm,
        Rgba32 colour, float opacity)
    {
        using var image = Image.Load<Rgba32>(pageArtwork);
        using var mask = Image.Load<Rgba32>(maskPng);

        Assert.True(
            image.Width == mask.Width && image.Height == mask.Height,
            "The page-scale artwork and the glyph mask are different sizes.");

        var width = image.Width;
        var height = image.Height;

        int minX = width, minY = height, maxX = -1, maxY = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (Luma(mask[x, y]) > 250f) continue;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        Assert.True(maxX >= 0, "The glyph mask has no ink on it, so there is no copy to sit behind.");

        var layout = new BekiPrintLayoutOptions();
        var pxPerMm = width / (layout.SpreadWidthMm + (layout.BleedMm * 2f));

        var pad = paddingMm * pxPerMm;
        var radius = MathF.Max(0f, cornerRadiusMm * pxPerMm);

        var left = MathF.Max(0f, minX - pad);
        var top = MathF.Max(0f, minY - pad);
        var right = MathF.Min(width - 1f, maxX + pad);
        var bottom = MathF.Min(height - 1f, maxY + pad);

        for (var y = (int)MathF.Floor(top); y <= (int)MathF.Ceiling(bottom); y++)
        {
            if (y < 0 || y >= height) continue;

            for (var x = (int)MathF.Floor(left); x <= (int)MathF.Ceiling(right); x++)
            {
                if (x < 0 || x >= width) continue;

                var cover = RoundedRectCoverage(x, y, left, top, right, bottom, radius);
                if (cover <= 0f) continue;

                image[x, y] = Blend(image[x, y], colour, cover * opacity);
            }
        }

        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    /// <summary>
    /// How much of one pixel the rounded rectangle covers, 0–1.
    ///
    /// Square everywhere except within a corner's radius of a corner, where the distance to the
    /// corner's centre decides it — and the half-pixel of slope at the boundary is what keeps the
    /// curve from reading as a staircase.
    /// </summary>
    private static float RoundedRectCoverage(
        int x, int y, float left, float top, float right, float bottom, float radius)
    {
        if (x < left - 1f || x > right + 1f || y < top - 1f || y > bottom + 1f) return 0f;

        var dx = MathF.Max(0f, MathF.Max(left + radius - x, x - (right - radius)));
        var dy = MathF.Max(0f, MathF.Max(top + radius - y, y - (bottom - radius)));

        if (dx <= 0f || dy <= 0f)
        {
            // Not in a corner's quarter: the straight edges decide, with the same half-pixel slope.
            var edge = MathF.Min(
                MathF.Min(x - left, right - x),
                MathF.Min(y - top, bottom - y));

            return Math.Clamp(edge + 0.5f, 0f, 1f);
        }

        return Math.Clamp(radius - MathF.Sqrt((dx * dx) + (dy * dy)) + 0.5f, 0f, 1f);
    }

    // ==============================================================================================
    // Hollow letters
    // ==============================================================================================

    /// <summary>
    /// One hollow sample, out of two renders of the real page.
    ///
    /// The GROUND is the spread composed exactly as any other proof sample, with the copy's fill
    /// opacity set to zero and no rim — so the composer walks its whole text path, applies the column
    /// safety rules and paints nothing. It is the artwork at the page's real geometry, not a
    /// separately loaded picture.
    ///
    /// The MASK is the same page, same type, at the same place, set solid black on a flat white sheet
    /// the artwork's own dimensions — so it crops identically and every inked pixel on it is a pixel
    /// of the real shaped, wrapped, positioned glyphs.
    ///
    /// Both are cached by what actually decides them, because a batch varies border and veil far more
    /// often than it varies the type.
    /// </summary>
    private static byte[] HollowSample(
        BekiPdfComposer composer, StorySpread spread, byte[] artwork,
        DesignerStyleSpec spec, BekiTextStyleProof style,
        Dictionary<string, byte[]> grounds, Dictionary<string, byte[]> masks)
    {
        const string GroundKey = "ground";

        if (!grounds.TryGetValue(GroundKey, out var ground))
        {
            ground = composer.RenderStyleProofSpread(
                spread, artwork, personalization: null,
                new BekiTextStyleProof
                {
                    FontSizePt = style.FontSizePt,
                    LeadingPt = style.LeadingPt,
                    FillOpacity = 0f,
                    RimWidthFactor = 0f,
                });

            grounds[GroundKey] = ground;
        }

        var maskKey = $"{style.FontSizePt}|{style.LeadingPt}|{style.Weight}";

        if (!masks.TryGetValue(maskKey, out var mask))
        {
            mask = composer.RenderStyleProofSpread(
                spread, FlatSheetLike(artwork, new Rgba32(255, 255, 255)), personalization: null,
                new BekiTextStyleProof
                {
                    FontSizePt = style.FontSizePt,
                    LeadingPt = style.LeadingPt,
                    Weight = style.Weight,
                    FillColorHex = "000000",
                    FillOpacity = 1f,
                    RimWidthFactor = 0f,
                });

            masks[maskKey] = mask;
        }

        return HollowLetters(
            ground, mask,
            spec.Border, BekiPdfComposer.StyleProofRasterDpi,
            ParseHex(spec.BorderColor), Math.Clamp(spec.RimOpacity, 0f, 1f),
            ParseHex(spec.VeilColor), Math.Clamp(spec.VeilOpacity, 0f, 1f));
    }

    /// <summary>
    /// A see-through letter: the shipped glyph's own outline, stroked inward, with the interior left
    /// unpainted so the illustration shows through it.
    ///
    /// **Why this is built out of the production renderer rather than out of SkiaSharp.** The obvious
    /// way to stroke type is to ask a font library for the glyph path and stroke it — and the moment
    /// you do that you have taken over line breaking, Georgian shaping and baseline placement from
    /// QuestPDF and HarfBuzz, which is precisely where "the same wrap, the same lines, the same
    /// position as the production text block" stops being true. (SkiaSharp is also not on this
    /// solution: QuestPDF 2025.7.4 carries its own native Skia and exposes no managed bindings.)
    ///
    /// So the glyph shape is taken FROM the production renderer instead of reproduced beside it. The
    /// composer renders the page twice at the same geometry — once with the artwork and no ink, and
    /// once with the copy set solid black on a flat white sheet — and the second is an exact
    /// coverage mask of the real, shaped, wrapped, positioned type. The outline is then that mask
    /// minus an erosion of itself: <c>border = cov − min(cov over a disc of radius r)</c>, which is
    /// the glyph's edge, r points thick, drawn INSIDE its own silhouette. Nothing about the letter's
    /// shape, size or position is this file's opinion.
    ///
    /// Inward rather than centred, deliberately: an inner stroke leaves the outer silhouette exactly
    /// the shipped type's, so a hollow sample and a filled one are the same letters in the same
    /// places and the owner is comparing the treatment and nothing else.
    /// </summary>
    private static byte[] HollowLetters(
        byte[] groundPng, byte[] maskPng,
        float borderWidthPt, int rasterDpi,
        Rgba32 border, float borderOpacity,
        Rgba32 veil, float veilOpacity)
    {
        using var ground = Image.Load<Rgba32>(groundPng);
        using var mask = Image.Load<Rgba32>(maskPng);

        Assert.True(
            ground.Width == mask.Width && ground.Height == mask.Height,
            "The artwork page and the glyph mask are different sizes, so the mask does not describe "
            + "where the type on that page is.");

        var width = ground.Width;
        var height = ground.Height;

        // Black type on a flat white sheet: the ink IS the coverage, antialiasing included.
        var coverage = new float[width * height];
        int minX = width, minY = height, maxX = -1, maxY = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = mask[x, y];
                var ink = 1f - (Luma(pixel) / 255f);

                if (ink <= 0.004f) continue;

                coverage[(y * width) + x] = ink;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        Assert.True(maxX >= 0, "The glyph mask has no ink on it at all.");

        var radius = Math.Max(1, (int)MathF.Round(borderWidthPt * rasterDpi / 72f));

        // Only the copy's own rectangle is walked. The erosion is a disc per pixel, and paying for
        // that over a five-megapixel page to describe a block of type in the corner of it would make
        // a fifteen-variant sheet take minutes for nothing.
        var left = Math.Max(0, minX - radius - 1);
        var top = Math.Max(0, minY - radius - 1);
        var right = Math.Min(width - 1, maxX + radius + 1);
        var bottom = Math.Min(height - 1, maxY + radius + 1);

        // The disc, as row offsets, worked out once.
        var spans = new List<(int Dy, int Dx)>();
        for (var dy = -radius; dy <= radius; dy++)
        {
            var dx = (int)MathF.Floor(MathF.Sqrt((radius * radius) - (dy * dy)));
            spans.Add((dy, dx));
        }

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var here = coverage[(y * width) + x];
                if (here <= 0f) continue;

                // Grayscale erosion: the least coverage anywhere within a border's reach. A pixel
                // deep inside a stem keeps its own value; one within r of the edge takes the edge's.
                var interior = here;

                foreach (var (dy, dx) in spans)
                {
                    var sy = y + dy;
                    if (sy < 0 || sy >= height) { interior = 0f; break; }

                    var rowStart = sy * width;

                    for (var sx = x - dx; sx <= x + dx; sx++)
                    {
                        var value = sx < 0 || sx >= width ? 0f : coverage[rowStart + sx];
                        if (value < interior) interior = value;
                        if (interior <= 0f) break;
                    }

                    if (interior <= 0f) break;
                }

                var edge = MathF.Max(0f, here - interior);

                var pixel = ground[x, y];

                // The outline first, then whatever veil the variant asked for inside it. A veil of
                // zero — which is most of the batch — touches nothing, and the artwork is simply
                // what the letter is made of.
                if (edge > 0f)
                {
                    pixel = Blend(pixel, border, edge * borderOpacity);
                }

                if (veilOpacity > 0f && interior > 0f)
                {
                    pixel = Blend(pixel, veil, interior * veilOpacity);
                }

                ground[x, y] = pixel;
            }
        }

        using var buffer = new MemoryStream();
        ground.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    private static Rgba32 Blend(Rgba32 under, Rgba32 over, float alpha)
    {
        var a = Math.Clamp(alpha, 0f, 1f);
        return new Rgba32(
            (byte)MathF.Round((over.R * a) + (under.R * (1f - a))),
            (byte)MathF.Round((over.G * a) + (under.G * (1f - a))),
            (byte)MathF.Round((over.B * a) + (under.B * (1f - a))),
            255);
    }

    private static float Luma(Rgba32 pixel) =>
        (0.2126f * pixel.R) + (0.7152f * pixel.G) + (0.0722f * pixel.B);

    private static Rgba32 ParseHex(string hex)
    {
        var value = hex.TrimStart('#');
        return new Rgba32(
            Convert.ToByte(value[..2], 16),
            Convert.ToByte(value.Substring(2, 2), 16),
            Convert.ToByte(value.Substring(4, 2), 16),
            255);
    }

    /// <summary>A flat sheet the artwork's own size, so the mask page crops exactly as the real one does.</summary>
    private static byte[] FlatSheetLike(byte[] artwork, Rgba32 colour)
    {
        var size = Image.Identify(artwork);
        using var flat = new Image<Rgba32>(size.Width, size.Height, colour);

        using var buffer = new MemoryStream();
        flat.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    // ==============================================================================================
    // The sheet
    // ==============================================================================================

    private const int Gutter = 18;

    /// <summary>
    /// The caption strip's height in points, which is also its height in pixels — it is rasterized
    /// at 72 DPI. Two lines' worth, so a narrow cell whose caption wraps still shows all of it
    /// rather than half.
    /// </summary>
    private const int CaptionHeight = 56;

    /// <summary>
    /// Every sample on one page, so they are compared rather than remembered.
    ///
    /// ImageSharp for the grid, because a contact sheet is furniture — but the CELLS are the real
    /// renders, scaled and not redrawn. The captions come back through QuestPDF for the plain reason
    /// that ImageSharp's core has no text renderer, and a caption written in a hand-rolled bitmap
    /// alphabet would be unreadable at exactly the size somebody reads a caption at.
    ///
    /// The grid is a parameter because the two rounds are different sheets: ten samples want two big
    /// cells across, forty-five want five smaller ones — and forty-five cells at the first round's
    /// size would be a page nobody can take in at once, which is the only thing a contact sheet is
    /// for.
    /// </summary>
    /// <param name="rowLengths">
    /// How many cells each row holds, when the batch's families are different lengths and each wants
    /// its own line. Null lays the samples out in a plain grid.
    /// </param>
    private static byte[] ContactSheet(
        IReadOnlyList<(string File, string Caption, byte[] Png)> samples,
        int columns, int thumbnailWidth, int captionSize,
        IReadOnlyList<int>? rowLengths = null)
    {
        var thumbnails = new List<Image<Rgb24>>();
        var captions = new List<Image<Rgb24>>();

        try
        {
            foreach (var (_, caption, png) in samples)
            {
                var thumbnail = Image.Load<Rgb24>(png);
                var scaled = Math.Max(
                    1, (int)Math.Round((double)thumbnail.Height * thumbnailWidth / thumbnail.Width));
                thumbnail.Mutate(context => context.Resize(thumbnailWidth, scaled));
                thumbnails.Add(thumbnail);

                captions.Add(Image.Load<Rgb24>(CaptionPng(caption, thumbnailWidth, captionSize)));
            }

            var cellHeight = thumbnails.Max(thumbnail => thumbnail.Height) + CaptionHeight;

            // Where every sample sits, as (row, column). A plain grid fills rows of `columns`; named
            // row lengths give each family in the batch a line of its own.
            var places = new List<(int Row, int Column)>(thumbnails.Count);

            if (rowLengths is null)
            {
                for (var index = 0; index < thumbnails.Count; index++)
                {
                    places.Add((index / columns, index % columns));
                }
            }
            else
            {
                for (var row = 0; row < rowLengths.Count; row++)
                {
                    for (var column = 0; column < rowLengths[row]; column++)
                    {
                        places.Add((row, column));
                    }
                }
            }

            var rows = places.Max(place => place.Row) + 1;

            var width = (columns * thumbnailWidth) + ((columns + 1) * Gutter);
            var height = (rows * cellHeight) + ((rows + 1) * Gutter);

            using var sheet = new Image<Rgb24>(width, height, new Rgb24(28, 26, 34));

            for (var index = 0; index < thumbnails.Count; index++)
            {
                var (row, column) = places[index];
                var x = Gutter + (column * (thumbnailWidth + Gutter));
                var y = Gutter + (row * (cellHeight + Gutter));

                var thumbnail = thumbnails[index];
                var caption = captions[index];

                sheet.Mutate(context => context
                    .DrawImage(thumbnail, new Point(x, y), 1f)
                    .DrawImage(caption, new Point(x, y + thumbnail.Height), 1f));
            }

            using var buffer = new MemoryStream();
            sheet.Save(buffer, new PngEncoder());
            return buffer.ToArray();
        }
        finally
        {
            foreach (var image in thumbnails.Concat(captions))
            {
                image.Dispose();
            }
        }
    }

    /// <summary>
    /// One caption strip, set by the same renderer that set the page above it and rasterized at 72
    /// DPI so that a point is a pixel and the strip comes back the width it was asked for.
    /// </summary>
    private static byte[] CaptionPng(string text, int widthPx, int captionSize)
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        return Document.Create(document => document.Page(page =>
        {
            page.Size(widthPx, CaptionHeight, QuestPDF.Infrastructure.Unit.Point);
            page.Margin(0);
            page.PageColor(QuestPDF.Infrastructure.Color.FromHex("#1C1A22"));

            page.Content()
                .PaddingHorizontal(10)
                .AlignMiddle()
                .Text(text)
                .FontSize(captionSize)
                .FontColor(QuestPDF.Infrastructure.Color.FromHex("#F5EFE2"));
        }))
        .GenerateImages(new QuestPDF.Infrastructure.ImageGenerationSettings
        {
            ImageFormat = QuestPDF.Infrastructure.ImageFormat.Png,
            RasterDpi = 72,
        })
        .Single();
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
