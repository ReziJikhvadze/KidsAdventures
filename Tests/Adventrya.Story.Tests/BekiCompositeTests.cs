using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The exact-composite path: the approved poses are the approved poses, the same sentence always
/// picks the same one, and the Nina spread comes out at the numbers the partners approved.
///
/// These run against the real installed assets and the real historic fixture rather than synthetic
/// stand-ins, because every claim being made here is a claim about those specific files. A test
/// that hashed a PNG it had just written would prove only that SHA-256 works.
/// </summary>
public class BekiCompositeTests
{
    private static string FixtureDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "nina_dinosaurs");

    private static string FixturePath(string name) => Path.Combine(FixtureDirectory, name);

    private static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // ---------------------------------------------------------------------------------------
    // Registry
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Every pose the registry names is installed and hashes to what the registry says. This is the
    /// build-time version of the check the pipeline makes per book: if a pose PNG were ever
    /// re-encoded on its way into the repo — by an image optimiser, by a Git filter, by a well-meant
    /// "compress assets" commit — every hash in the partner pack would be wrong and no book could
    /// ship. Better to learn that here.
    /// </summary>
    [Fact]
    public void Registry_VerifiesEveryInstalledPoseAgainstItsHash()
    {
        var registry = BekiPoseRegistry.Load();

        Assert.Equal("beki-pose-registry-v1", registry.RegistryVersion);
        Assert.Equal(9, registry.Poses.Count);
        Assert.Equal("pose_01_neutral_hover", registry.FallbackPoseId);
        Assert.Equal("pose_07_curious_lean", registry.IntroPoseId);

        foreach (var pose in registry.Poses)
        {
            var bytes = registry.ApprovedPoseBytes(pose.Id);
            Assert.NotEmpty(bytes);
            Assert.Equal(pose.Sha256, Sha256Hex(bytes));
        }

        // The fallback carries no keywords and is therefore unreachable by matching — it must not
        // be in the priority order, or a book could land on the neutral hover while reporting a
        // keyword match.
        Assert.DoesNotContain(registry.FallbackPoseId, registry.PriorityOrder);
        Assert.Equal(8, registry.PriorityOrder.Count);
    }

    /// <summary>
    /// One flipped byte in one pose file stops the composite, and the error names the pose and both
    /// hashes — the two things an operator needs to compare the installed file against the partner
    /// pack without reproducing the failure first.
    /// </summary>
    [Fact]
    public void Registry_RefusesATamperedPose_NamingItAndBothHashes()
    {
        var temp = Directory.CreateTempSubdirectory("beki-pose-tamper");
        try
        {
            var installed = Path.Combine(AppContext.BaseDirectory, "Assets", "BekiComposite");
            var staged = Path.Combine(temp.FullName, "Assets", "BekiComposite");
            Directory.CreateDirectory(Path.Combine(staged, "poses"));
            File.Copy(
                Path.Combine(installed, "beki_pose_registry_v1.json"),
                Path.Combine(staged, "beki_pose_registry_v1.json"));

            var registry = BekiPoseRegistry.Load(temp.FullName);
            var pose = registry.Pose("pose_05_excited_celebrate");

            var bytes = File.ReadAllBytes(Path.Combine(installed, "poses", pose.FileName));
            bytes[bytes.Length / 2] ^= 0xFF;
            var tamperedHash = Sha256Hex(bytes);
            File.WriteAllBytes(Path.Combine(staged, "poses", pose.FileName), bytes);

            var error = Assert.Throws<InvalidOperationException>(() => registry.ApprovedPoseBytes(pose.Id));

            Assert.Contains(pose.Id, error.Message, StringComparison.Ordinal);
            Assert.Contains(pose.Sha256, error.Message, StringComparison.Ordinal);
            Assert.Contains(tamperedHash, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Selector
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// One representative keyword per prioritised pose, wrapped in the kind of noise a real
    /// scenario sentence carries: mixed case, leading and trailing space, and — for the multi-word
    /// keywords — extra spaces inside the phrase, which only match once the registry's "collapse
    /// whitespace" rule has been applied.
    /// </summary>
    public static TheoryData<string, string> PoseKeywords => new()
    {
        { "pose_06_brave_protective", "protects" },
        { "pose_04_listen", "listens" },
        { "pose_07_curious_lean", "curious" },
        { "pose_03_guide_point", "points" },
        { "pose_08_gentle_reassure", "reassures" },
        { "pose_05_excited_celebrate", "celebrates" },
        { "pose_09_forward_adventure_glide", "glides forward" },
        // "greet" and not "welcomes": within a pose the keywords are ordered too, and "welcomes"
        // contains the earlier "welcome", so the recorded keyword would be that one. The ordering
        // is the registry's business, not this row's — this row is here to prove the pose.
        { "pose_02_welcome_invitation", "greet" },
    };

    [Theory]
    [MemberData(nameof(PoseKeywords))]
    public void Selector_MatchesEachPoseByItsOwnKeyword(string poseId, string keyword)
    {
        var registry = BekiPoseRegistry.Load();
        var noisy = "   Beki  " + keyword.ToUpperInvariant().Replace(" ", "    ") + "   here.  ";

        var selection = BekiPoseSelector.Select(registry, noisy);

        Assert.Equal(poseId, selection.PoseId);
        Assert.Equal(keyword, selection.MatchedKeyword);
        Assert.False(selection.Fallback);
    }

    /// <summary>
    /// Guards the theory above against a registry revision: a tenth pose added to the priority
    /// order with no test row would otherwise be selected in production without a single test ever
    /// having exercised it.
    /// </summary>
    [Fact]
    public void Selector_TheoryCoversEveryPrioritisedPose()
    {
        var registry = BekiPoseRegistry.Load();
        var covered = PoseKeywords.Select(row => (string)row[0]!).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(registry.PriorityOrder.OrderBy(id => id, StringComparer.Ordinal),
            covered.OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>
    /// An action that hits both the protective pose and the invitation resolves to the protective
    /// one, because the registry orders it first — the picture has to show the thing that matters
    /// in the beat, and a Beki waving hello while the child is in danger is the wrong page.
    ///
    /// The recorded keyword is "shields" and not "bravely": within a pose, keywords are tried in
    /// list order too.
    /// </summary>
    [Fact]
    public void Selector_HonoursPriorityOrderWhenTwoPosesMatch()
    {
        var registry = BekiPoseRegistry.Load();

        var selection = BekiPoseSelector.Select(
            registry, "Beki welcomes the child and bravely shields her from the falling rocks.");

        Assert.Equal("pose_06_brave_protective", selection.PoseId);
        Assert.Equal("shields", selection.MatchedKeyword);
        Assert.False(selection.Fallback);
    }

    [Theory]
    [InlineData("Beki hovers quietly above the meadow.")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Selector_FallsBackToTheNeutralHoverAndSaysSo(string? action)
    {
        var registry = BekiPoseRegistry.Load();

        var selection = BekiPoseSelector.Select(registry, action);

        Assert.Equal("pose_01_neutral_hover", selection.PoseId);
        Assert.Null(selection.MatchedKeyword);
        Assert.True(selection.Fallback);
    }

    /// <summary>
    /// The intro's pose is forced, so it is not a fallback however loudly its action text would
    /// have argued for another pose — and the flag stays false so intro spreads never inflate the
    /// fallback count the pipeline logs.
    /// </summary>
    [Fact]
    public void Selector_ForcesTheCuriousLeanForTheIntro()
    {
        var registry = BekiPoseRegistry.Load();

        var selection = BekiPoseSelector.SelectForIntro(registry);

        Assert.Equal("pose_07_curious_lean", selection.PoseId);
        Assert.Null(selection.MatchedKeyword);
        Assert.False(selection.Fallback);
    }

    [Theory]
    [InlineData("  Beki   LISTENS \n attentively.  ", "beki listens attentively.")]
    [InlineData("BEKI", "beki")]
    [InlineData("\t\t", "")]
    public void Selector_NormalizesExactlyAsTheRegistryDescribes(string raw, string expected)
        => Assert.Equal(expected, BekiPoseSelector.Normalize(raw));

    // ---------------------------------------------------------------------------------------
    // Config
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The anchors the engine will use are the handoff's §6 table, read from the shipped config
    /// rather than restated in C#. If the config is ever edited, this is where the change surfaces.
    /// </summary>
    [Fact]
    public void Config_CarriesTheHandoffAnchorTable()
    {
        var config = BekiCompositeConfig.Load();

        var left = config.StoryDefaultFor(BekiTextSide.Left);
        Assert.Equal(0.594, left.VisibleCenterX);
        Assert.Equal(0.458, left.VisibleCenterY);
        Assert.Equal(0.333, left.VisibleHeight);

        var right = config.StoryDefaultFor(BekiTextSide.Right);
        Assert.Equal(0.406, right.VisibleCenterX);
        Assert.Equal(0.458, right.VisibleCenterY);
        Assert.Equal(0.333, right.VisibleHeight);

        Assert.Equal("pose_07_curious_lean", config.IntroPoseId);
        Assert.Equal(0.78095, config.IntroAnchor.VisibleHeight);
        Assert.Equal(1.0, config.Opacity);
        Assert.True(config.VerifySha256);

        Assert.Equal(BekiTextSide.Left, BekiCompositeConfig.ParseTextSide("LEFT"));
        Assert.Equal(BekiTextSide.Left, BekiCompositeConfig.ParseTextSide("left"));
        Assert.Equal(BekiTextSide.Right, BekiCompositeConfig.ParseTextSide("RIGHT"));
    }

    // ---------------------------------------------------------------------------------------
    // Golden composite
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The one page in existence that a human approved, re-derived.
    ///
    /// Nina's spread 01 was composited by the reference Python, printed, and signed off. Its
    /// manifest recorded the alpha box, the rendered size, the anchor and the placement — so the
    /// port is correct exactly when it computes those same numbers from the same two PNGs. Every
    /// step of the algorithm shows up in one of them: the bounding box proves the alpha rule, the
    /// rendered size proves the proportional resize and its rounding, and the placement proves the
    /// centre anchor and the round-half-to-even tie at Y (392.506 → 393, then 250.5 → 250; half-up
    /// would give 251 and a Beki one pixel low).
    ///
    /// The output bytes are deliberately not asserted. Handoff §14: the historic proof never
    /// recorded which resampling engine made it, so a byte-for-byte match is not something the port
    /// can owe. The hash is computed and recorded instead — see the sibling test for how close the
    /// pixels actually land.
    /// </summary>
    [Fact]
    public void Composite_ReproducesTheApprovedNinaSpreadGeometry()
    {
        var expected = JsonDocument.Parse(File.ReadAllBytes(FixturePath("spread_01_composition_manifest.json")))
            .RootElement;
        var expectedLayer = expected.GetProperty("beki_layer");

        var basePng = File.ReadAllBytes(FixturePath("spread_01_child_world_base.png"));
        var posePng = File.ReadAllBytes(FixturePath("spread_01_beki_pose.png"));

        var engine = BekiCompositeEngine.Create();
        var registry = engine.Registry;

        // The fixture pose is pose 04 — asserted by hash, not by filename, because the filename is
        // the fixture's and only the hash ties it to the registry.
        var poseId = expectedLayer.GetProperty("pose_id").GetString()!;
        Assert.Equal(registry.Pose(poseId).Sha256, Sha256Hex(posePng));
        Assert.Equal("pose_04_listen", poseId);

        // And it is the pose the selector picks unaided from the fixture's own scenario: the
        // geometry below is not an isolated maths exercise, it is the page the pipeline would build.
        var scenario = JsonDocument.Parse(File.ReadAllBytes(FixturePath("visual_scenario_output_v2.json")));
        var spreadOneAction = scenario.RootElement.GetProperty("spreads")[0].GetProperty("beki_action").GetString();
        var selection = BekiPoseSelector.Select(registry, spreadOneAction);
        Assert.Equal(poseId, selection.PoseId);
        Assert.Equal("listens", selection.MatchedKeyword);

        // The manifest records the anchor, not the text side, so the side is recovered from it: the
        // fixture's anchor is the LEFT story default, which is also what the page-1 rhythm asks for.
        var expectedAnchor = expectedLayer.GetProperty("normalized_anchor");
        var textSide = BekiTextSide.Left;
        var configured = engine.Config.StoryDefaultFor(textSide);
        Assert.Equal(expectedAnchor.GetProperty("visible_center_x").GetDouble(), configured.VisibleCenterX);
        Assert.Equal(expectedAnchor.GetProperty("visible_center_y").GetDouble(), configured.VisibleCenterY);
        Assert.Equal(expectedAnchor.GetProperty("visible_height").GetDouble(), configured.VisibleHeight);

        var result = engine.CompositeStorySpread(
            basePng,
            "spread_01_child_world_base.png",
            poseId,
            textSide,
            "spread_01_exact_beki_composite.png");

        var layer = result.Manifest.BekiLayer;

        Assert.Equal(expected.GetProperty("canvas").GetProperty("width_px").GetInt32(), result.Manifest.Canvas.WidthPx);
        Assert.Equal(expected.GetProperty("canvas").GetProperty("height_px").GetInt32(), result.Manifest.Canvas.HeightPx);

        var expectedBox = expectedLayer.GetProperty("source_alpha_bbox");
        Assert.Equal(expectedBox.GetProperty("x_px").GetInt32(), layer.SourceAlphaBbox.XPx);
        Assert.Equal(expectedBox.GetProperty("y_px").GetInt32(), layer.SourceAlphaBbox.YPx);
        Assert.Equal(expectedBox.GetProperty("width_px").GetInt32(), layer.SourceAlphaBbox.WidthPx);
        Assert.Equal(expectedBox.GetProperty("height_px").GetInt32(), layer.SourceAlphaBbox.HeightPx);

        var expectedSize = expectedLayer.GetProperty("rendered_size_px");
        Assert.Equal(expectedSize.GetProperty("width_px").GetInt32(), layer.RenderedSizePx.WidthPx);
        Assert.Equal(expectedSize.GetProperty("height_px").GetInt32(), layer.RenderedSizePx.HeightPx);

        var expectedPlacement = expectedLayer.GetProperty("placement_px");
        Assert.Equal(expectedPlacement.GetProperty("x_px").GetInt32(), layer.PlacementPx.XPx);
        Assert.Equal(expectedPlacement.GetProperty("y_px").GetInt32(), layer.PlacementPx.YPx);

        Assert.Equal(expectedAnchor.GetProperty("visible_center_x").GetDouble(), layer.NormalizedAnchor.VisibleCenterX);
        Assert.Equal(expectedAnchor.GetProperty("visible_center_y").GetDouble(), layer.NormalizedAnchor.VisibleCenterY);
        Assert.Equal(expectedAnchor.GetProperty("visible_height").GetDouble(), layer.NormalizedAnchor.VisibleHeight);

        Assert.Equal(expectedLayer.GetProperty("sha256").GetString(), layer.Sha256);
        Assert.Equal(expected.GetProperty("base_image").GetProperty("sha256").GetString(), result.Manifest.BaseImage.Sha256);

        Assert.Equal(1.0, layer.Opacity);
        Assert.False(layer.Mirrored);
        Assert.False(layer.Rotated);
        Assert.False(layer.Warped);
        Assert.False(layer.Redrawn);
        Assert.Equal("Lanczos3", result.Manifest.Resampler);

        // Recorded, not asserted (§14). It is still worth computing: the pipeline logs it, and a
        // hash that changed between two runs of the same inputs would mean the port is not
        // deterministic, which is a different and much worse failure than differing from Pillow.
        Assert.Equal(64, result.Manifest.Output.Sha256.Length);
        var again = engine.CompositeStorySpread(
            basePng, "spread_01_child_world_base.png", poseId, textSide, "spread_01_exact_beki_composite.png");
        Assert.Equal(result.Manifest.Output.Sha256, again.Manifest.Output.Sha256);

        // The output is an opaque sheet at the base's dimensions, as the layout stage expects.
        using var output = Image.Load<Rgba32>(result.Png);
        Assert.Equal(result.Manifest.Canvas.WidthPx, output.Width);
        Assert.Equal(result.Manifest.Canvas.HeightPx, output.Height);
    }

    /// <summary>
    /// Not an assertion of byte identity — §14 forbids owing that — but of the thing byte identity
    /// was standing in for: that the port puts the same character in the same place with the same
    /// colours as the approved proof. Geometry alone could be right while the picture was wrong (a
    /// premultiplication mistake, a colour-space slip, the wrong crop of the right box), and this
    /// is the only fixture in existence that can catch that.
    ///
    /// The tolerance is loose because two Lanczos implementations legitimately differ by a few
    /// levels on the resampled edge pixels of a 182×285 layer; it is nowhere near loose enough for
    /// a different pose, a mirrored one, or one placed a pixel out.
    /// </summary>
    [Fact]
    public void Composite_LandsOnTheApprovedProofPixels()
    {
        var basePng = File.ReadAllBytes(FixturePath("spread_01_child_world_base.png"));
        var engine = BekiCompositeEngine.Create();

        var result = engine.CompositeStorySpread(
            basePng,
            "spread_01_child_world_base.png",
            "pose_04_listen",
            BekiTextSide.Left,
            "spread_01_exact_beki_composite.png");

        using var produced = Image.Load<Rgba32>(result.Png);
        using var approved = Image.Load<Rgba32>(File.ReadAllBytes(FixturePath("spread_01_exact_beki_composite.png")));

        Assert.Equal(approved.Width, produced.Width);
        Assert.Equal(approved.Height, produced.Height);

        var layer = new Rectangle(
            result.Manifest.BekiLayer.PlacementPx.XPx,
            result.Manifest.BekiLayer.PlacementPx.YPx,
            result.Manifest.BekiLayer.RenderedSizePx.WidthPx,
            result.Manifest.BekiLayer.RenderedSizePx.HeightPx);

        long layerTotal = 0;
        long outsideDiffering = 0;
        var worst = 0;

        for (var y = 0; y < approved.Height; y++)
        {
            for (var x = 0; x < approved.Width; x++)
            {
                var a = approved[x, y];
                var b = produced[x, y];
                var delta = Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

                if (layer.Contains(x, y))
                {
                    layerTotal += delta;
                    if (delta > worst) worst = delta;
                }
                else if (delta != 0)
                {
                    outsideDiffering++;
                }
            }
        }

        var layerMean = (double)layerTotal / ((long)layer.Width * layer.Height);
        var detail = $"layer mean channel delta {layerMean:F4}, worst {worst}, "
            + $"differing pixels outside the layer {outsideDiffering}";

        // Nothing outside the pasted 182×285 box may differ by a single level. The base is copied,
        // not re-rendered, so any difference out here would mean the page had been recompressed,
        // colour-converted or resized on its way through — the sort of thing that silently degrades
        // a print master.
        Assert.True(outsideDiffering == 0, detail);

        // Inside the box, two Lanczos implementations legitimately disagree by a few levels on
        // resampled edges. Measured at 1.33 mean / 30 worst against this proof; the bounds leave
        // room for a library upgrade and none at all for a wrong pose, a mirrored one, or one
        // placed a pixel out — those move whole limbs and run the mean into the tens.
        Assert.True(layerMean < 4.0, detail);
        Assert.True(worst < 64, detail);
    }

    // ---------------------------------------------------------------------------------------
    // Manifest contract
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The serialized manifest has exactly the fields the partners' schema declares — the schema
    /// sets additionalProperties:false, so an extra field is as much a break as a missing one. The
    /// approved fixture manifest is used as the reference shape rather than a list of names copied
    /// into C#, so the two can never drift apart quietly.
    /// </summary>
    [Fact]
    public void Manifest_SerializesToTheApprovedShape()
    {
        var basePng = File.ReadAllBytes(FixturePath("spread_01_child_world_base.png"));
        var engine = BekiCompositeEngine.Create();

        var result = engine.CompositeStorySpread(
            basePng,
            "spread_01_child_world_base.png",
            "pose_04_listen",
            BekiTextSide.Left,
            "spread_01_exact_beki_composite.png");

        var json = result.Manifest.ToJson();
        Assert.EndsWith("\n", json, StringComparison.Ordinal);

        using var produced = JsonDocument.Parse(json);
        using var approved = JsonDocument.Parse(File.ReadAllBytes(FixturePath("spread_01_composition_manifest.json")));

        AssertSameShape(approved.RootElement, produced.RootElement, "$");

        Assert.Equal("beki-exact-composite-v1", produced.RootElement.GetProperty("composition_version").GetString());

        // 1.0, not 1 — the reference implementation's spelling, so a hand diff against a partner
        // manifest shows only the fields that really differ.
        Assert.Contains("\"opacity\": 1.0", json, StringComparison.Ordinal);

        // The locked resampler rides on the record for the log and stays out of the closed schema.
        Assert.DoesNotContain("Lanczos3", json, StringComparison.Ordinal);
        Assert.Equal("Lanczos3", result.Manifest.Resampler);
    }

    private static void AssertSameShape(JsonElement expected, JsonElement actual, string path)
    {
        Assert.True(expected.ValueKind == actual.ValueKind, $"{path}: {expected.ValueKind} vs {actual.ValueKind}");

        if (expected.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var expectedNames = expected.EnumerateObject().Select(p => p.Name).ToList();
        var actualNames = actual.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(expectedNames, actualNames);

        foreach (var property in expected.EnumerateObject())
        {
            AssertSameShape(property.Value, actual.GetProperty(property.Name), $"{path}.{property.Name}");
        }
    }
}
