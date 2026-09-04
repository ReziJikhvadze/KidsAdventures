using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AdventurePacks.Api.Services.Story.Composite.Poses;

/// <summary>
/// Pastes one approved Beki PNG onto one generated child/world image, and writes the receipt.
///
/// A port of the partner pack's <c>scripts/composite_exact_beki.py</c>, kept deliberately close to
/// it: same alpha bounding box, same rounding, same anchor arithmetic, same bounds checks, same
/// manifest. The reference is the definition of a correct page, so where the two could differ the
/// Python wins — including the places its arithmetic is surprising. Its <c>round()</c> is
/// round-half-to-even, which is why <see cref="Math.Round(double, MidpointRounding)"/> is spelled
/// out with <see cref="MidpointRounding.ToEven"/> below rather than left to a default: the Nina
/// spread-01 proof lands on exactly 250.5 for its Y placement, and half-up would put Beki one pixel
/// lower than the approved book.
///
/// What this class cannot do is as important as what it does. There is no mirror, no rotation, no
/// warp, no recolor and no redraw — not disabled, not behind a flag, not present. The pipeline's
/// entire claim is that Beki is never generated, and a flag that could flip her is a flag that one
/// day will. When a placement looks wrong the fix is the anchor, which is data.
/// </summary>
public sealed class BekiCompositeEngine
{
    /// <summary>
    /// The locked resampler, named so it can be logged (handoff §6 step 6.4).
    ///
    /// Lanczos3 is ImageSharp's equivalent of the reference's Pillow LANCZOS — a 3-lobe windowed
    /// sinc in both libraries. Locked rather than chosen per call: two books composited with
    /// different resamplers are two different books, and the handoff asks for exactly one.
    /// </summary>
    public const string ResamplerName = "Lanczos3";

    private readonly BekiPoseRegistry _registry;
    private readonly BekiCompositeConfig _config;

    public BekiCompositeEngine(BekiPoseRegistry registry, BekiCompositeConfig config)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(config);

        // The config names the pose registry it was written against. If the two ever drift — a new
        // pose pack dropped in beside an old pipeline config, or the reverse — the anchors and the
        // hashes are no longer describing the same artwork, and that is worth refusing at
        // construction rather than discovering in a printed proof.
        if (!string.Equals(config.PoseRegistryVersion, registry.RegistryVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Pipeline config expects pose registry '{config.PoseRegistryVersion}' but the "
                + $"installed registry is '{registry.RegistryVersion}'.");
        }

        _registry = registry;
        _config = config;
    }

    /// <summary>Loads the registry and the pipeline config from the published asset tree.</summary>
    public static BekiCompositeEngine Create(string? baseDirectory = null)
        => new(BekiPoseRegistry.Load(baseDirectory), BekiCompositeConfig.Load(baseDirectory));

    public BekiPoseRegistry Registry => _registry;

    public BekiCompositeConfig Config => _config;

    /// <summary>
    /// Composites a story spread, using the deterministic anchor for the side the text sits on.
    ///
    /// The anchor moves with the text because it has to: Beki stands in the half of the spread the
    /// Georgian does not occupy, so text-left puts her right of centre and text-right puts her left
    /// of it. <paramref name="anchorOverride"/> exists for the case §14 anticipates — a scene where
    /// the proven default lands her badly. The response to that is a different anchor, never a
    /// redrawn character.
    /// </summary>
    public BekiCompositeResult CompositeStorySpread(
        byte[] basePng,
        string baseFileName,
        string poseId,
        BekiTextSide textSide,
        string outputFileName,
        BekiCompositeAnchor? anchorOverride = null)
        => Composite(
            basePng,
            baseFileName,
            poseId,
            anchorOverride ?? _config.StoryDefaultFor(textSide),
            outputFileName);

    /// <summary>
    /// Composites the intro spread: the registry's forced pose at the config's intro anchor, which
    /// is a much larger and further-right Beki than any story spread — the intro is her page.
    /// </summary>
    public BekiCompositeResult CompositeIntro(
        byte[] basePng,
        string baseFileName,
        string outputFileName,
        BekiCompositeAnchor? anchorOverride = null)
        => Composite(
            basePng,
            baseFileName,
            _config.IntroPoseId,
            anchorOverride ?? _config.IntroAnchor,
            outputFileName);

    /// <summary>
    /// The algorithm itself, step for step against the reference implementation.
    /// </summary>
    public BekiCompositeResult Composite(
        byte[] basePng,
        string baseFileName,
        string poseId,
        BekiCompositeAnchor anchor,
        string outputFileName)
    {
        ArgumentNullException.ThrowIfNull(basePng);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFileName);
        ArgumentNullException.ThrowIfNull(anchor);
        anchor.Validate();

        var pose = _registry.Pose(poseId);

        // Step 0 of the reference: the pose bytes are the approved bytes or there is no composite.
        // The registry hashes on first use, so this call is the verification.
        var poseBytes = _registry.ApprovedPoseBytes(poseId);

        // The reference refuses a pose that is not RGBA, and the reason is not pedantry: an opaque
        // RGB export of the same artwork has no alpha to bound, so it would find a bounding box of
        // the whole 2048×2048 sheet and paste Beki as a rectangle of background.
        var poseInfo = Image.Identify(poseBytes);
        if (!poseInfo.Metadata.TryGetPngMetadata(out var poseMetadata)
            || poseMetadata.ColorType != PngColorType.RgbWithAlpha)
        {
            throw new InvalidOperationException(
                $"Approved Beki pose '{pose.Id}' ({pose.FileName}) must be an RGBA PNG; its colour "
                + $"type is {poseMetadata?.ColorType?.ToString() ?? "unknown"}.");
        }

        using var canvas = Image.Load<Rgba32>(basePng);
        var canvasWidth = canvas.Width;
        var canvasHeight = canvas.Height;

        using var poseImage = Image.Load<Rgba32>(poseBytes);

        var alphaBox = VisibleAlphaBounds(poseImage)
            ?? throw new InvalidOperationException(
                $"Approved Beki pose '{pose.Id}' has no visible alpha content.");

        // Proportional, from the height: the anchor states a visible height as a fraction of the
        // canvas, and the width follows from the source aspect. Beki is never stretched, so the
        // width is derived and not configurable.
        var renderedHeight = RoundHalfToEven(canvasHeight * anchor.VisibleHeight);
        var renderedWidth = RoundHalfToEven((double)alphaBox.Width * renderedHeight / alphaBox.Height);

        using var rendered = poseImage.Clone(ctx => ctx
            .Crop(alphaBox)
            .Resize(new ResizeOptions
            {
                Size = new Size(renderedWidth, renderedHeight),
                // Stretch because the target size is already the proportional one computed above —
                // any other mode would let ImageSharp recompute it and quietly disagree with the
                // number written into the manifest.
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Lanczos3,
                // Companding off, matching the reference: Pillow resamples in the stored sRGB
                // values, and turning on linearization here would change every edge pixel while
                // claiming to be the same algorithm.
                Compand = false,
                // Premultiplied, which is ImageSharp's default and the one place this port
                // knowingly does not do what Pillow does — Pillow resamples RGBA channels
                // independently. It is stated rather than inherited because it was measured: against
                // the approved Nina proof, premultiplied lands at a mean channel delta of 0.044 with
                // a worst pixel of 30, non-premultiplied at 0.059 and 37. The library default is
                // both the better-looking option (no dark fringe pulled in from transparent pixels)
                // and the closer one to the page a human actually signed off.
                PremultiplyAlpha = true,
            }));

        // The anchor addresses the CENTRE of the visible box, not its corner, so that a pose whose
        // artwork sits differently inside its 2048×2048 sheet still lands in the same place on the
        // page. Two roundings, in the reference's order: the centre first, the corner from it.
        var centerX = RoundHalfToEven(canvasWidth * anchor.VisibleCenterX);
        var centerY = RoundHalfToEven(canvasHeight * anchor.VisibleCenterY);
        var placementX = RoundHalfToEven(centerX - (renderedWidth / 2.0));
        var placementY = RoundHalfToEven(centerY - (renderedHeight / 2.0));

        if (placementX < 0 || placementY < 0)
        {
            throw new InvalidOperationException(
                $"Beki placement leaves the top or left canvas boundary (pose '{pose.Id}' at "
                + $"{placementX},{placementY} on {canvasWidth}×{canvasHeight}).");
        }

        if (placementX + renderedWidth > canvasWidth || placementY + renderedHeight > canvasHeight)
        {
            throw new InvalidOperationException(
                $"Beki placement leaves the bottom or right canvas boundary (pose '{pose.Id}' at "
                + $"{placementX},{placementY} sized {renderedWidth}×{renderedHeight} on "
                + $"{canvasWidth}×{canvasHeight}).");
        }

        // Plain source-over at full opacity — Pillow's alpha_composite. Named explicitly rather
        // than left to the overload's defaults, because "whatever DrawImage does by default" is not
        // something a print contract should rest on.
        canvas.Mutate(ctx => ctx.DrawImage(
            rendered,
            new Point(placementX, placementY),
            PixelColorBlendingMode.Normal,
            PixelAlphaCompositionMode.SrcOver,
            1f));

        // The reference converts to RGB before saving and carries the base image's ICC profile and
        // DPI across. Both matter downstream: the layout stage expects an opaque sheet, and print
        // prep expects the colour intent the base arrived with. Loading the base into this same
        // image is what preserves that metadata — it is never re-created here.
        using var buffer = new MemoryStream();
        canvas.Save(buffer, new PngEncoder { ColorType = PngColorType.Rgb });
        var outputPng = buffer.ToArray();

        var manifest = new BekiCompositionManifest
        {
            Canvas = new BekiCompositionSize { WidthPx = canvasWidth, HeightPx = canvasHeight },
            BaseImage = new BekiCompositionFile { File = baseFileName, Sha256 = Sha256Hex(basePng) },
            BekiLayer = new BekiCompositionLayer
            {
                PoseId = pose.Id,
                File = pose.FileName,
                Sha256 = pose.Sha256,
                SourceAlphaBbox = new BekiCompositionRect
                {
                    XPx = alphaBox.X,
                    YPx = alphaBox.Y,
                    WidthPx = alphaBox.Width,
                    HeightPx = alphaBox.Height,
                },
                RenderedSizePx = new BekiCompositionSize
                {
                    WidthPx = renderedWidth,
                    HeightPx = renderedHeight,
                },
                PlacementPx = new BekiCompositionPoint { XPx = placementX, YPx = placementY },
                NormalizedAnchor = new BekiCompositionAnchor
                {
                    VisibleCenterX = anchor.VisibleCenterX,
                    VisibleCenterY = anchor.VisibleCenterY,
                    VisibleHeight = anchor.VisibleHeight,
                },
                Opacity = _config.Opacity,
            },
            Output = new BekiCompositionFile { File = outputFileName, Sha256 = Sha256Hex(outputPng) },
            Resampler = ResamplerName,
        };

        return new BekiCompositeResult(outputPng, manifest);
    }

    /// <summary>
    /// The tight box around every pixel with any alpha at all — Pillow's
    /// <c>image.getchannel("A").getbbox()</c>.
    ///
    /// The rule is <c>alpha &gt; 0</c>, not "alpha above some threshold". A one-step-from-invisible
    /// edge pixel still counts, which is what keeps the box identical to the reference's on the same
    /// file; a threshold would crop a hair tighter and shift every downstream number. Returns null
    /// for a fully transparent sheet, which the caller turns into the reference's error.
    /// </summary>
    private static Rectangle? VisibleAlphaBounds(Image<Rgba32> image)
    {
        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var rowLeft = -1;
                var rowRight = -1;

                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A == 0)
                    {
                        continue;
                    }

                    if (rowLeft < 0)
                    {
                        rowLeft = x;
                    }

                    rowRight = x;
                }

                if (rowLeft < 0)
                {
                    continue;
                }

                if (y < top) top = y;
                if (y > bottom) bottom = y;
                if (rowLeft < left) left = rowLeft;
                if (rowRight > right) right = rowRight;
            }
        });

        if (right < left)
        {
            return null;
        }

        // Pillow's bbox is half-open (right and lower are exclusive); a Rectangle is width/height,
        // so the +1 here is that conversion and not a fencepost slip.
        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// Python's <c>round()</c>: half-to-even on the exact double value.
    ///
    /// <see cref="Math.Round(double)"/> already does this, but it is spelled out because the whole
    /// point is that it must never become half-away-from-zero — see the class remarks for the
    /// spread that lands on a tie.
    /// </summary>
    private static int RoundHalfToEven(double value)
        => (int)Math.Round(value, MidpointRounding.ToEven);

    internal static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

/// <summary>Which half of the spread the Georgian text occupies; Beki takes the other one.</summary>
public enum BekiTextSide
{
    Left,
    Right,
}

/// <summary>
/// Where Beki goes, as fractions of the canvas rather than pixels — the same anchor is correct for
/// a 1836-pixel proof and a 5315-pixel print raster, which is the only reason the Nina spread's
/// numbers are reusable at all.
/// </summary>
public sealed record BekiCompositeAnchor(
    double VisibleCenterX,
    double VisibleCenterY,
    double VisibleHeight)
{
    /// <summary>
    /// The reference's own bound: strictly between 0 and 1, exclusive at both ends. Stricter than
    /// the manifest schema, which permits 0 and 1 for the centres — a centre exactly on the canvas
    /// edge would put half of Beki outside it and fail the bounds check a few lines later anyway,
    /// with a much less obvious message.
    /// </summary>
    public void Validate()
    {
        Check(VisibleCenterX, nameof(VisibleCenterX));
        Check(VisibleCenterY, nameof(VisibleCenterY));
        Check(VisibleHeight, nameof(VisibleHeight));

        static void Check(double value, string name)
        {
            if (!(value > 0 && value < 1))
            {
                throw new ArgumentOutOfRangeException(
                    name, value, $"{name} must be greater than 0 and less than 1.");
            }
        }
    }

    /// <summary>How far the one permitted placement retry moves Beki, as a fraction of the width.</summary>
    public const double RecompositeStep = 0.06;

    /// <summary>And how much smaller it draws her, which moves her inner edge by half as much again.</summary>
    public const double RecompositeHeightScale = 0.9;

    /// <summary>
    /// The same Beki, further from the middle of the page: the one adjustment a refused placement
    /// is allowed to make.
    ///
    /// It exists because the retry that was there before did nothing. A spread refused for
    /// FOLD_SAFETY was re-composited from the same bytes, at the same configured anchor, by the
    /// same deterministic arithmetic — so the second image was the first image, and the reviewer
    /// refused it again for the same reason. One book failed at spread seven that way, having paid
    /// for two reviews of one picture. §14 says what to do instead in as many words: "a failed
    /// placement should first adjust deterministic anchors, not redraw Beki."
    ///
    /// Two changes, both in the same direction. The centre moves <see cref="RecompositeStep"/> of
    /// the canvas width away from the middle, towards the half of the spread Beki already occupies;
    /// and she is drawn <see cref="RecompositeHeightScale"/> of the height, which pulls her inner
    /// edge in again by half the width she loses. Both are what FOLD_SAFETY is asking for — the
    /// character is too close to the centre of the sheet — and neither is a new pose, a mirrored
    /// one, a rotated one or a recoloured one. Those remain impossible: this returns an anchor, and
    /// an anchor is three numbers.
    ///
    /// The result is clamped to the rectangle the deterministic checks already enforce — fully
    /// inside the canvas, and clear of the third the Georgian text is printed over — with a
    /// one-pixel margin for the engine's own half-to-even rounding. A clamp that has nowhere to put
    /// her is null rather than a nudge of zero: the caller's whole reason for asking is to get a
    /// different picture, and it needs to know when it cannot have one.
    /// </summary>
    /// <param name="textSide">
    /// Which third carries the text, and therefore which way "away from the centre" points: Beki
    /// stands in the half the text does not occupy, so a left-text spread moves her further right.
    /// </param>
    /// <param name="canvasWidthPx">The canvas she was composited onto, from its own manifest.</param>
    /// <param name="renderedWidthPx">
    /// How wide she came out at the previous attempt, also from that manifest — the honest number,
    /// rather than this record recomputing the engine's resize arithmetic and risking disagreeing
    /// with it. It is measured before the height scale, so using it here bounds her slightly more
    /// tightly than the smaller sprite needs, which errs the safe way.
    /// </param>
    public BekiCompositeAnchor? NudgedAwayFromCentre(
        BekiTextSide textSide, int canvasWidthPx, int renderedWidthPx)
    {
        if (canvasWidthPx <= 0 || renderedWidthPx <= 0 || renderedWidthPx >= canvasWidthPx)
        {
            return null;
        }

        var halfWidth = renderedWidthPx / 2.0 / canvasWidthPx;
        var margin = 1.0 / canvasWidthPx;
        var third = 1.0 / 3.0;

        // The window her centre may sit in, which is the deterministic checks' own rule read
        // forwards: fully on the canvas, and not one pixel inside the reserved third.
        var (lowest, highest) = textSide == BekiTextSide.Left
            ? (third + halfWidth + margin, 1 - halfWidth - margin)
            : (halfWidth + margin, (1 - third) - halfWidth - margin);

        if (lowest >= highest)
        {
            return null;
        }

        var direction = textSide == BekiTextSide.Left ? 1 : -1;
        var moved = Math.Clamp(VisibleCenterX + (direction * RecompositeStep), lowest, highest);

        var nudged = this with
        {
            VisibleCenterX = moved,
            VisibleHeight = VisibleHeight * RecompositeHeightScale,
        };

        // Degenerate only if the height scale did nothing either, which it cannot while the scale
        // is below one — stated anyway, because a future scale of 1.0 with a fully clamped centre
        // would silently reintroduce the no-op this method exists to remove.
        return nudged == this ? null : nudged;
    }
}

/// <summary>The composited page and its receipt.</summary>
/// <param name="Png">The output image, RGB, ready to store beside the book's other artifacts.</param>
/// <param name="Manifest">
/// The composition manifest. Persist it: it is what lets a reprint prove the page carried the
/// approved character.
/// </param>
public sealed record BekiCompositeResult(byte[] Png, BekiCompositionManifest Manifest);

/// <summary>
/// The <c>beki_composite</c> block of the partner pack's <c>pipeline_config_v1.json</c>: the
/// anchors, the opacity, and the five capability switches.
///
/// The anchors live in the config and not in C# because §14 says out loud that they are proven on
/// one spread and may need adjusting on others — that is a data change, reviewable in a diff of a
/// JSON file, not a deployment.
/// </summary>
public sealed class BekiCompositeConfig
{
    /// <summary>Resolved against <see cref="AppContext.BaseDirectory"/>, beside the pose registry.</summary>
    public const string ConfigAssetPath = "Assets/BekiComposite/pipeline_config_v2.json";

    private readonly IReadOnlyDictionary<BekiTextSide, BekiCompositeAnchor> _storyDefaults;

    private BekiCompositeConfig(
        string configVersion,
        string poseRegistryVersion,
        bool verifySha256,
        double opacity,
        IReadOnlyDictionary<BekiTextSide, BekiCompositeAnchor> storyDefaults,
        string introPoseId,
        BekiCompositeAnchor introAnchor)
    {
        ConfigVersion = configVersion;
        PoseRegistryVersion = poseRegistryVersion;
        VerifySha256 = verifySha256;
        Opacity = opacity;
        _storyDefaults = storyDefaults;
        IntroPoseId = introPoseId;
        IntroAnchor = introAnchor;
    }

    public string ConfigVersion { get; }

    public string PoseRegistryVersion { get; }

    /// <summary>
    /// Read and asserted true rather than obeyed. The registry hashes unconditionally; this exists
    /// so that a config edit turning verification "off" is caught as the mistake it is instead of
    /// being silently ignored.
    /// </summary>
    public bool VerifySha256 { get; }

    public double Opacity { get; }

    public string IntroPoseId { get; }

    public BekiCompositeAnchor IntroAnchor { get; }

    public BekiCompositeAnchor StoryDefaultFor(BekiTextSide textSide)
        => _storyDefaults.TryGetValue(textSide, out var anchor)
            ? anchor
            : throw new InvalidOperationException(
                $"Pipeline config has no beki_composite story default for text side '{textSide}'.");

    /// <summary>
    /// Accepts the config's <c>LEFT</c>/<c>RIGHT</c> and the existing pipeline's lower-case
    /// <c>left</c>/<c>right</c>, so a caller holding either spelling does not have to translate.
    /// </summary>
    public static BekiTextSide ParseTextSide(string textSide)
    {
        if (string.Equals(textSide, "LEFT", StringComparison.OrdinalIgnoreCase)) return BekiTextSide.Left;
        if (string.Equals(textSide, "RIGHT", StringComparison.OrdinalIgnoreCase)) return BekiTextSide.Right;
        throw new ArgumentOutOfRangeException(nameof(textSide), textSide, "Text side must be LEFT or RIGHT.");
    }

    public static BekiCompositeConfig Load(string? baseDirectory = null)
    {
        var path = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, ConfigAssetPath);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Beki pipeline config missing at '{path}'. The composite pipeline cannot place a "
                + "pose without its anchors.", path);
        }

        ConfigDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ConfigDocument>(File.ReadAllBytes(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Beki pipeline config at '{path}' is not valid JSON.", ex);
        }

        var composite = document?.BekiComposite
            ?? throw new InvalidOperationException(
                $"Beki pipeline config at '{path}' has no beki_composite block.");

        // The five switches are all read and all required false. The engine has no code that could
        // honour a true, so a config asking for one is describing a pipeline that does not exist —
        // and the failure mode of ignoring it is a book that silently is not what the config says.
        RequireDisabled(composite.AllowMirror, "allow_mirror", path);
        RequireDisabled(composite.AllowRotation, "allow_rotation", path);
        RequireDisabled(composite.AllowWarp, "allow_warp", path);
        RequireDisabled(composite.AllowRecolor, "allow_recolor", path);
        RequireDisabled(composite.AllowAiRedraw, "allow_ai_redraw", path);

        if (composite.VerifySha256 != true)
        {
            throw new InvalidOperationException(
                $"Beki pipeline config at '{path}' sets verify_sha256 false; approved-pose "
                + "verification is not optional.");
        }

        if (composite.Opacity is not 1.0)
        {
            throw new InvalidOperationException(
                $"Beki pipeline config at '{path}' sets opacity {composite.Opacity}; the approved "
                + "character is composited at 1.0 or not at all.");
        }

        if (string.IsNullOrWhiteSpace(composite.PoseRegistryVersion))
        {
            throw new InvalidOperationException(
                $"Beki pipeline config at '{path}' names no pose_registry_version.");
        }

        var defaults = composite.StoryDefaults
            ?? throw new InvalidOperationException(
                $"Beki pipeline config at '{path}' has no beki_composite.story_defaults.");

        var storyDefaults = new Dictionary<BekiTextSide, BekiCompositeAnchor>();
        foreach (var (side, anchor) in defaults)
        {
            var parsed = ToAnchor(anchor, $"story_defaults.{side}", path);
            storyDefaults[ParseTextSide(side)] = parsed;
        }

        if (!storyDefaults.ContainsKey(BekiTextSide.Left) || !storyDefaults.ContainsKey(BekiTextSide.Right))
        {
            throw new InvalidOperationException(
                $"Beki pipeline config at '{path}' must give a story default for both LEFT and RIGHT.");
        }

        var intro = composite.Intro
            ?? throw new InvalidOperationException(
                $"Beki pipeline config at '{path}' has no beki_composite.intro.");

        if (string.IsNullOrWhiteSpace(intro.PoseId))
        {
            throw new InvalidOperationException(
                $"Beki pipeline config at '{path}' has no beki_composite.intro.pose_id.");
        }

        var introAnchor = ToAnchor(
            new AnchorDocument
            {
                VisibleCenterX = intro.VisibleCenterX,
                VisibleCenterY = intro.VisibleCenterY,
                VisibleHeight = intro.VisibleHeight,
            },
            "intro",
            path);

        return new BekiCompositeConfig(
            document?.ConfigVersion ?? string.Empty,
            composite.PoseRegistryVersion,
            composite.VerifySha256.Value,
            composite.Opacity.Value,
            storyDefaults,
            intro.PoseId,
            introAnchor);
    }

    private static void RequireDisabled(bool? value, string field, string path)
    {
        if (value != false)
        {
            throw new InvalidOperationException(
                $"Beki pipeline config at '{path}' sets {field} to '{value?.ToString() ?? "null"}'. "
                + "The composite engine has no such capability and will not pretend to.");
        }
    }

    private static BekiCompositeAnchor ToAnchor(AnchorDocument? anchor, string where, string path)
    {
        if (anchor?.VisibleCenterX is null || anchor.VisibleCenterY is null || anchor.VisibleHeight is null)
        {
            throw new InvalidOperationException(
                $"Beki pipeline config at '{path}' has an incomplete anchor at beki_composite.{where}.");
        }

        var parsed = new BekiCompositeAnchor(
            anchor.VisibleCenterX.Value,
            anchor.VisibleCenterY.Value,
            anchor.VisibleHeight.Value);

        try
        {
            parsed.Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidOperationException(
                $"Beki pipeline config at '{path}' has an out-of-range anchor at "
                + $"beki_composite.{where}: {ex.Message}", ex);
        }

        return parsed;
    }

    private sealed record ConfigDocument
    {
        [JsonPropertyName("config_version")] public string? ConfigVersion { get; init; }
        [JsonPropertyName("beki_composite")] public CompositeDocument? BekiComposite { get; init; }
    }

    private sealed record CompositeDocument
    {
        [JsonPropertyName("pose_registry_version")] public string? PoseRegistryVersion { get; init; }
        [JsonPropertyName("verify_sha256")] public bool? VerifySha256 { get; init; }
        [JsonPropertyName("opacity")] public double? Opacity { get; init; }
        [JsonPropertyName("allow_mirror")] public bool? AllowMirror { get; init; }
        [JsonPropertyName("allow_rotation")] public bool? AllowRotation { get; init; }
        [JsonPropertyName("allow_warp")] public bool? AllowWarp { get; init; }
        [JsonPropertyName("allow_recolor")] public bool? AllowRecolor { get; init; }
        [JsonPropertyName("allow_ai_redraw")] public bool? AllowAiRedraw { get; init; }
        [JsonPropertyName("story_defaults")] public Dictionary<string, AnchorDocument>? StoryDefaults { get; init; }
        [JsonPropertyName("intro")] public IntroDocument? Intro { get; init; }
    }

    private sealed record AnchorDocument
    {
        [JsonPropertyName("visible_center_x")] public double? VisibleCenterX { get; init; }
        [JsonPropertyName("visible_center_y")] public double? VisibleCenterY { get; init; }
        [JsonPropertyName("visible_height")] public double? VisibleHeight { get; init; }
    }

    private sealed record IntroDocument
    {
        [JsonPropertyName("pose_id")] public string? PoseId { get; init; }
        [JsonPropertyName("visible_center_x")] public double? VisibleCenterX { get; init; }
        [JsonPropertyName("visible_center_y")] public double? VisibleCenterY { get; init; }
        [JsonPropertyName("visible_height")] public double? VisibleHeight { get; init; }
    }
}
