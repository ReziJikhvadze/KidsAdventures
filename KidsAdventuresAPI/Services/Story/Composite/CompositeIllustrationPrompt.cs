using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Composite.Poses;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// The deterministic page rhythm as the supplier's config states it, not as this codebase
/// paraphrases it (handoff §6 Step 3).
///
/// <see cref="Prompts.BekiSpreadRhythm"/> already holds an eight-entry table and the composite path
/// deliberately does not read it. The two agree on the text sides — CompositePipelineTests asserts
/// that equivalence rather than trusting it — and they disagree on the shots, because the shot
/// wording is what reaches the image model and the supplier owns the wording it approved a book
/// against. Reading the config is also what makes a reworded shot a data change: the config is
/// diffable, deployed with the assets, and cannot drift from the book the partners signed off.
/// </summary>
public static class CompositeSpreadRhythm
{
    private static readonly Lazy<IReadOnlyDictionary<int, RhythmEntry>> Entries = new(Read);

    /// <summary><c>LEFT</c> or <c>RIGHT</c>, in the config's own spelling.</summary>
    public static string TextSideFor(int page) => Entry(page).TextSide;

    /// <summary>The approved shot sentence, verbatim.</summary>
    public static string ShotFor(int page) => Entry(page).ShotInstruction;

    /// <summary>Every configured page, for the tests that compare the whole table at once.</summary>
    public static IReadOnlyList<int> Pages => Entries.Value.Keys.OrderBy(page => page).ToList();

    private static RhythmEntry Entry(int page) =>
        Entries.Value.TryGetValue(page, out var entry)
            ? entry
            // Not clamped to the nearest page. A book asking for a spread the rhythm has no entry
            // for is a book being drawn to a format nobody configured, and borrowing spread 8's
            // camera for it would print that mistake rather than report it.
            : throw new InvalidOperationException(
                $"pipeline_config_v1.json has no spread_rhythm entry for page {page}.");

    private sealed record RhythmEntry(string TextSide, string ShotInstruction);

    private static IReadOnlyDictionary<int, RhythmEntry> Read()
    {
        using var config = CompositeAssets.Read(CompositeAssets.PipelineConfigPath);

        return config.RootElement
            .GetProperty("spread_rhythm")
            .EnumerateArray()
            .ToDictionary(
                entry => entry.GetProperty("page").GetInt32(),
                entry => new RhythmEntry(
                    entry.GetProperty("text_side").GetString()
                        ?? throw new InvalidOperationException("A spread_rhythm entry has no text_side."),
                    entry.GetProperty("shot_instruction").GetString()
                        ?? throw new InvalidOperationException("A spread_rhythm entry has no shot_instruction.")));
    }
}

/// <summary>
/// One approved theme reference: the picture the image model is shown, and the words that describe
/// the world it stands for.
/// </summary>
/// <param name="Id">The canonical theme id — clouds | space | forest | ocean | magic | dinosaurs.</param>
/// <param name="OfficialName">
/// The supplier's own working title for the world. It reaches the Visual Scenario input and the
/// image prompt, so it is read from the registry rather than translated here.
/// </param>
/// <param name="VisualDirection">The registry's one-sentence description of the world's look.</param>
/// <param name="FileName">The reference PNG's filename, for the log and the prompt record.</param>
/// <param name="Bytes">The reference PNG itself, already verified against the registry's hash.</param>
public sealed record CompositeThemeReference(
    string Id,
    string OfficialName,
    string VisualDirection,
    string FileName,
    byte[] Bytes);

/// <summary>
/// Resolves the one approved world reference a book is drawn against, and refuses to hand back a
/// file the registry does not vouch for.
///
/// The hash check is the point of the class. The theme reference is the second of the two images
/// every spread is generated from, so a re-encoded or replaced PNG would change the look of every
/// picture in every book of that world — quietly, and only visibly once a proof came back wrong.
/// The registry ships the hash beside the filename precisely so that cannot happen unnoticed.
/// </summary>
public static class CompositeThemeReferences
{
    private const string ReferenceDirectory = "theme_references";

    private static readonly Lazy<IReadOnlyDictionary<string, RegistryEntry>> Registry = new(Read);

    /// <summary>
    /// The reference for one canonical theme id, read once and kept — a book asks for it nine
    /// times and the file cannot change while the process is running.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The theme is not in the registry, its file is missing from the published output, or the
    /// file's SHA-256 is not the one the registry names.
    /// </exception>
    public static CompositeThemeReference For(string themeId, string? baseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);

        if (!Registry.Value.TryGetValue(themeId, out var entry))
        {
            throw new InvalidOperationException(
                $"theme_reference_registry_v1.json has no entry for theme '{themeId}'.");
        }

        var path = Path.Combine(
            baseDirectory ?? AppContext.BaseDirectory,
            "Assets", "BekiComposite", ReferenceDirectory, entry.FileName);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The approved theme reference for '{themeId}' is missing from the published "
                + $"output at '{path}'. No child/world image may be drawn without it.");
        }

        var bytes = File.ReadAllBytes(path);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The approved theme reference for '{themeId}' ({entry.FileName}) hashes to "
                + $"{actual}, and the registry says {entry.Sha256}. The installed file is not the "
                + "approved one.");
        }

        return new CompositeThemeReference(
            themeId, entry.OfficialName, entry.VisualDirection, entry.FileName, bytes);
    }

    /// <summary>
    /// The approved hash of one theme's reference, without reading the file.
    ///
    /// For the resume contract, which has to name the artwork a book's pages were drawn against
    /// and is built once per job before anything else happens. Re-arting a world is a new hash in
    /// the registry, and a resumed run must not adopt pages drawn from the old picture while
    /// drawing the rest from the new one — two visual worlds inside one book, every page
    /// individually fine.
    ///
    /// The registry only, deliberately: <see cref="For"/> reads and verifies the PNG and is what
    /// the image stage calls, and there is no reason to pull megabytes off disk to write one line
    /// of a manifest.
    /// </summary>
    public static string RegisteredSha256(string themeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);

        return Registry.Value.TryGetValue(themeId, out var entry)
            ? entry.Sha256
            : throw new InvalidOperationException(
                $"theme_reference_registry_v1.json has no entry for theme '{themeId}'.");
    }

    private sealed record RegistryEntry(
        string FileName, string Sha256, string OfficialName, string VisualDirection);

    private static IReadOnlyDictionary<string, RegistryEntry> Read()
    {
        using var registry = CompositeAssets.Read(CompositeAssets.ThemeRegistryPath);

        return registry.RootElement
            .GetProperty("themes")
            .EnumerateArray()
            .ToDictionary(
                theme => theme.GetProperty("id").GetString()!,
                theme => new RegistryEntry(
                    theme.GetProperty("reference_filename").GetString()!,
                    theme.GetProperty("sha256").GetString()!,
                    theme.GetProperty("working_title_en").GetString()!,
                    theme.GetProperty("visual_direction").GetString()!),
                StringComparer.Ordinal);
    }
}

/// <summary>
/// One page's recurring elements, resolved three ways for three readers: the raw descriptions
/// (continuity references and the QA reviewer key on them), the same descriptions annotated with
/// their prop state (the image prompt's RECURRING block), and the prohibition sentences for
/// elements whose state bans them from the page (the hard constraints).
/// </summary>
public sealed record CompositeSpreadElements(
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Annotated,
    IReadOnlyList<string> Forbidden);

/// <summary>
/// Everything one spread's image prompt is resolved from. All of it is either code's decision or
/// the Visual Scenario's; none of it is the image model's.
/// </summary>
public sealed record CompositeSpreadPromptInput
{
    /// <summary>1-based, and the only thing the text side and the shot are derived from.</summary>
    public required int Page { get; init; }

    /// <summary>The number the parent gave. The prompt says it out loud so proportions are drawn to it.</summary>
    public required int ChildAge { get; init; }

    public required CompositeThemeReference Theme { get; init; }

    /// <summary>The Visual Scenario's own sentence, sent verbatim and never edited.</summary>
    public required string ChildWorldScene { get; init; }

    /// <summary>The book-level outfit lock, identical on every image of this book.</summary>
    public required string ChildOutfit { get; init; }

    /// <summary>Only the recurring elements this page needs — see <see cref="CompositeIllustrationPrompt.ElementsFor"/>.</summary>
    public IReadOnlyList<string> RecurringElements { get; init; } = [];

    /// <summary>
    /// Full prohibition sentences for elements whose prop state bans them from this page — the
    /// object before its discovery, the object after it was left behind. Appended to the hard
    /// constraints, because the audit's lantern appeared exactly where nothing forbade it.
    /// </summary>
    public IReadOnlyList<string> ForbiddenElements { get; init; } = [];

    /// <summary>
    /// The recurring elements a continuity reference is attached for. Empty means that image is
    /// not sent and the prompt never mentions it.
    /// </summary>
    public IReadOnlyList<string> ContinuityElementNames { get; init; } = [];

    /// <summary>
    /// The book's four identity attributes, rendered into the CHILD IDENTITY LOCK block.
    ///
    /// Required, and required on purpose: a book without a spec does not reach the image stage at
    /// all (it stops with <see cref="CompositeFailureCodes.IdentitySpecFailed"/>), so an optional
    /// field here would only describe a state the pipeline no longer has.
    /// </summary>
    public required ChildIdentitySpec IdentitySpec { get; init; }

    /// <summary>
    /// Whether the child appearance anchor is attached to this call — true on every spread after
    /// the first, false on the one that produces it.
    ///
    /// It decides two things at once, which is why it is one flag rather than two: whether the
    /// anchor's own instruction is written, and what number the continuity reference is given.
    /// The numbering has to match the order the references are actually attached in, or the model
    /// is told to take the child's stylization from a picture of a dinosaur.
    /// </summary>
    public bool AnchorAttached { get; init; }
}

/// <summary>
/// Everything the cover template needs from the printer-approved cover composer, and nothing this
/// code is entitled to invent.
/// </summary>
/// <param name="PanelInstructions">
/// The resolved natural-language panel and safe-zone block: back panel, spine and hinge, front
/// panel, front title-safe, front child/action, front Beki integration, and the wrap or bleed.
/// </param>
/// <param name="FrontBekiAnchor">
/// Where the approved Beki PNG is composited on the front panel afterwards. It comes from cover
/// configuration, never from the interior story defaults — the front panel is half the width of a
/// spread and Beki placed by a spread's anchor would land in the wrong panel entirely.
/// </param>
public sealed record CompositeCoverGeometry(string PanelInstructions, BekiCompositeAnchor FrontBekiAnchor);

/// <summary>
/// Where the cover's geometry would come from, and why there is none yet.
///
/// The cover base prompt is resolvable only against the active printer-approved dieline: the
/// contract lists seven regions it must be handed, and states in as many words that if the active
/// cover geometry is unavailable the job stops with <c>LAYOUT_FAILED</c> and never substitutes the
/// interior 5 mm bleed. That is not a caution about precision. The interior geometry describes one
/// 440 × 200 mm sheet; a cover is a wrap with a spine in the middle of it, and a cover generated
/// to interior rectangles would put the child across the spine and the title over her face.
///
/// <see cref="BekiPrintLayoutOptions"/> is the only layout configuration this application has, and
/// it is entirely interior: spread size, bleed, safe margin, gutter, the text column, fonts, the
/// QR leaf. It carries no back panel, no spine, no hinge, no title-safe rectangle and no front
/// Beki rectangle, so there is nothing here to resolve from. Hence null, every time, until the
/// cover composer campaign lands the dieline — and a null that the pipeline turns into an explicit
/// LAYOUT_FAILED rather than into a skipped cover.
/// </summary>
public static class CompositeCoverGeometryResolver
{
    public static CompositeCoverGeometry? TryResolve(BekiPrintLayoutOptions layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return null;
    }
}

/// <summary>
/// The child-and-world prompt: the picture an image model is actually asked for on this pipeline.
///
/// Beside <see cref="IllustrationPrompt"/> rather than inside it, and the reason is the one thing
/// the two disagree about most. Every prompt that function builds describes Beki and attaches her
/// canonical picture; every prompt this one builds forbids her outright, because on this pipeline
/// she is not drawn at all — she is pasted afterwards from an approved transparent PNG, and the
/// approved manual test only produced a usable page once the image model had stopped being asked
/// to draw her. A flag inside the existing builder would put "draw Beki" and "never draw Beki" one
/// boolean apart in the string every book in production is generated from.
///
/// The wording is the supplier's, transcribed from
/// <c>contracts/BEKI_Image_Generation_Prompt_Template_v1.md</c>. Transcribed rather than read from
/// the file because the API csproj ships that folder's JSON and PNGs and not its Markdown; the MD
/// stays the source of truth and this is its copy, so a revision there is a deliberate edit here
/// rather than a silent change in what nine paid image calls are told.
/// </summary>
public static class CompositeIllustrationPrompt
{
    /// <summary>
    /// The template's own version string, recorded against every image call.
    ///
    /// v1.1 is the amendment this campaign made to the supplier's v1: the centre of the canvas is
    /// no longer named as a fold anywhere (the models were painting the fold they were told about),
    /// and the child's identity is carried by an attribute lock plus an appearance anchor rather
    /// than by nine independent readings of one photograph. Both are recorded in the contract's
    /// own v1.1 changelog, against the defects that produced them.
    ///
    /// v1.3 moves one line and changes no other word. The deterministic shot instruction — the only
    /// thing that makes spread 3 a wide establishing view and spread 6 a close one — used to sit
    /// second in the COMPOSITION block, behind "one continuous very wide panoramic two-page spread",
    /// and the books came back as eight similar medium shots. A model reading a block that opens
    /// with "very wide panoramic" has already chosen its camera by the time the shot is mentioned.
    /// So the shot is now the block's first line and the panorama sentence follows it: the same two
    /// sentences, in the order that makes the variable one the instruction rather than the caveat.
    /// Recorded in the contract's v1.3 changelog against the supplier's shot-rhythm finding.
    ///
    /// v1.5 answers the supplier's production rejection (2026-08-31): every shipped spread carried
    /// a milky veil over the full text-side half, ending in a razor edge at the fold. The veil was
    /// model-painted and prompt-invited — this template asked for a "two-page spread", told the
    /// model the text third should be "gently lightening toward the outer edge", used the central
    /// low-information zone as a spatial landmark twice, and then banned only the DARK version of
    /// the band it had invited. v1.5 removes every one of those invitations: the canvas is one
    /// painting, the text third is calm at full colour with lightening forbidden by name, the
    /// centre is ordinary painting rather than a named zone, and the negatives now ban pale,
    /// milky and half-toned treatments as loudly as dark ones. Recorded in the contract's v1.5
    /// changelog against the audit's P0-A/P0-B findings.
    ///
    /// v1.6, one live book later: page 4 came back with a literal translucent rectangle painted
    /// at exactly 40.6% of the width — the model had materialised the "Beki integration zone" it
    /// was told to "leave" as an object, precisely where the sentence put it. The zone phrasing
    /// is gone the way the fold's was in v1.1: the placement ask is now a keep-this-area-calm
    /// rule that says outright it is never a shape to draw, and the negatives ban translucent
    /// panels of any size. The matching QA amendment (v1.6) makes a painted panel a
    /// GENERATED_TEXT failure, which is the category whose job is "furniture that is not scene".
    /// </summary>
    public const string Version = "child-world-image-v1.6";

    /// <summary>
    /// The cover base template's version. A different document, a different version.
    ///
    /// v1.1 is this campaign's amendment against the supplier's P0-03 finding: the shipped wrap
    /// carried vertical tonal jumps at x=1236 and x=1291 px — 250.5 mm and 261.5 mm on a 512 mm
    /// cover, which are the spine boundaries to the tenth of a millimetre — and an abrupt
    /// warm-green-to-purple transition across them. The prompt had named those boundaries as
    /// percent-bounded regions and the model painted what it was given. It is the third time this
    /// pipeline has measured the same law: name a region and it gets drawn (the v1.1 fold band,
    /// the v1.6 spread-4 translucent panel, and now the spine bands). So the cover prompt stops
    /// naming regions altogether: the panel block is painter's language about sides and the middle
    /// of one picture, the title area is "the upper right stays naturally calm and open", the
    /// spine, the hinge and the Beki rectangle are not mentioned at all, and the negatives ban a
    /// vertical tonal step as loudly as they ban a drawn line.
    /// </summary>
    public const string CoverVersion = "cover-child-world-v1.1";

    /// <summary>
    /// <remarks>
    /// The prefix is load-bearing: <see cref="BekiCoverRecord.IsRedraw"/> matches on it rather than
    /// on the whole string, so a cover redrawn by an earlier version still counts as a redraw for
    /// the reader's pointer. What the exact version decides is narrower — whether a resumed book
    /// gets today's cover prompt, which is how a book in flight when the age steer landed picks it
    /// up instead of keeping a cover drawn without it.
    /// </remarks>
    /// The version of the cover this pipeline actually ships: the legacy upright-cover composition,
    /// redrawn after spread one with the identity lock written into it and the accepted first
    /// spread attached as the appearance anchor.
    ///
    /// Not <see cref="CoverVersion"/>, which names the composite cover template that stops at
    /// LAYOUT_FAILED for want of a printer dieline and draws nothing. This one is what a parent
    /// sees, so it gets a version of its own on the fulfilment manifest — a cover drawn before this
    /// campaign and a cover drawn after it are different pictures made different ways, and the
    /// manifest is where that is recorded.
    /// </summary>
    public const string CoverRedrawVersion = "cover-identity-redraw-v1.4";

    /// <summary>Every version of the cover redraw shares this prefix. See the remark above.</summary>
    public const string CoverRedrawVersionPrefix = "cover-identity-redraw-";

    /// <summary>
    /// One spread's prompt.
    /// </summary>
    public static string ForSpread(CompositeSpreadPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var textSide = CompositeSpreadRhythm.TextSideFor(input.Page);
        var shot = CompositeSpreadRhythm.ShotFor(input.Page);

        return $"""
            Use case: illustration-story
            Asset type: BEKI personalized children's book child/world base image for later exact Beki PNG compositing

            INPUT IMAGES
            {InputImageBlock(input)}

            SCENE
            {input.ChildWorldScene.Trim()}
            Show this as one clear visible moment only.

            CHILD LOCK
            Dress the child in {input.ChildOutfit.Trim()}{OutfitAnchorClause(input.AnchorAttached)}
            Keep the outfit consistent with the cover and all other story spreads. Do not hide the child's face.

            {CompositeChildIdentity.LockBlock(
                input.IdentitySpec, input.ChildAge, input.AnchorAttached ? 2 : 1)}

            RECURRING ELEMENTS REQUIRED ON THIS IMAGE
            {RecurringBlock(input.RecurringElements)}

            COMPOSITION
            {shot}
            Obey that camera distance and framing exactly; do not default to a medium shot, and keep the page's main story subject fully inside the frame.
            Create one continuous very wide panoramic painting designed for a final 15:7 crop.
            {CompositionBlockFor(textSide)}
            {CentralZoneRule}
            Keep all important content in the central horizontal band so modest top-and-bottom crop normalization is safe.

            STYLE AND MOOD
            Premium warm stylized 3D children's-book illustration; expressive but natural; soft tactile materials; cinematic depth; welcoming, age-appropriate emotional tone. Match the supplied approved theme reference while creating a new scene.

            HARD CONSTRAINTS
            {SpreadConstraints}{ForbiddenElementLines(input.ForbiddenElements)}
            """;
    }

    private static string ForbiddenElementLines(IReadOnlyList<string> lines) =>
        lines.Count == 0
            ? string.Empty
            : "\n" + string.Join("\n", lines);

    /// <summary>
    /// The continuous cover base, resolved against the printer's own geometry.
    ///
    /// Takes the geometry as a parameter rather than resolving it, so that the one caller that
    /// cannot supply it fails with <see cref="CompositeFailureCodes.LayoutFailed"/> at the point
    /// where the fact is known, instead of this builder inventing a rectangle to fill a hole.
    ///
    /// What it does with that geometry changed in v1.1. The dieline's millimetres decide where the
    /// wrap is cut, where the pose is composited and where the title is typeset — they no longer
    /// reach the model as coordinates. Everything below the interpolated block is painter's
    /// language about one picture: a middle that is ordinary scene, edges the world runs off, and
    /// no rectangle, zone, panel, or percentage anywhere. The composition the printer needs is
    /// obtained by asking for a composition, not by describing a dieline.
    /// </summary>
    public static string ForCover(
        CompositeCoverGeometry geometry,
        int childAge,
        CompositeThemeReference theme,
        string frontChildWorldScene,
        string backEnvironment,
        string childOutfit,
        IReadOnlyList<string> recurringElements)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(theme);

        return $"""
            Use case: illustration-cover
            Asset type: BEKI personalized children's book continuous wraparound cover base for later vector title and exact Beki PNG compositing

            INPUT IMAGES
            {ChildReferenceLine(childAge)}
            {ThemeReferenceLine(theme, forCover: true)}

            FRONT-COVER SCENE
            {frontChildWorldScene.Trim()}
            Dress the child in {childOutfit.Trim()}
            Show one inviting action only. Do not reveal the ending.

            BACK-COVER ENVIRONMENT
            {backEnvironment.Trim()}
            Continue the same world, terrain, atmosphere, and lighting naturally across the whole picture. The left side of the picture contains no child, no Beki, and no other main character.

            RECURRING ELEMENTS REQUIRED ON THE FRONT
            {RecurringBlock(recurringElements)}

            PRINTER-SPECIFIC COMPOSITION
            {geometry.PanelInstructions.Trim()}
            The middle of the picture is ordinary scene: continue the environment through it with the same light, the same colour, and the same level of detail as its surroundings, and give it no edge, band, tint, seam, or change of treatment of its own. No face, hand, child, supporting character, or story-critical object may sit at or near the horizontal middle of the picture.
            Let the environment run all the way off every outer edge of the picture, and keep everything important well away from those edges.

            STYLE AND MOOD
            Premium warm stylized 3D children's-book cover; expressive but natural; soft tactile materials; cinematic depth; clear front-cover focal hierarchy; welcoming, age-appropriate adventure. Match the approved theme reference while creating a new scene.

            HARD CONSTRAINTS
            {CoverConstraints}
            """;
    }

    /// <summary>
    /// The template's composition resolver, both halves of it, written out rather than derived.
    ///
    /// The two percentages are the same numbers <c>pipeline_config_v1.json</c> gives the composite
    /// engine as its anchors — 0.594/0.406 across and 0.458 down — and they are stated to the image
    /// model so the zone it leaves empty is the zone Beki is later pasted into. That correspondence
    /// is what makes the whole two-stage approach work, and CompositePipelineTests asserts it
    /// rather than leaving two files to agree by good intentions.
    /// </summary>
    public static string CompositionBlockFor(string textSide) =>
        BekiCompositeConfig.ParseTextSide(textSide) == BekiTextSide.Left
            ? "Keep the full left third quiet enough to set story text over: continue the same "
              + "scene through it as calm open environment — sky, far foliage, open ground — "
              + "painted at exactly the same colour depth, saturation, contrast, exposure, and "
              + "finish as the rest of the picture. It is calm because the scene is calm there, "
              + "not because anything covers it: do not lighten it, and do not fade, veil, haze "
              + "over, wash, whiten, blur, or desaturate it or any other region of the painting. "
              + "It is part of the painting, not a panel: there must be no hard vertical boundary "
              + "where it begins, no flat field of colour, no visible edge between it and the rest "
              + "of the picture, and no change of tone marking where it begins or ends. No "
              + "character, face, hand, foreground object, or key action may enter this area. "
              + "Place the child and the main action in the outer-right area. Keep the area "
              + "around 59.4% of the canvas width and 45.8% of the canvas height naturally lit, "
              + "calm, and free of characters, faces, hands, hard edges, foreground objects, and "
              + "story-critical details — it is ordinary continuous environment exactly like its "
              + "surroundings, never a zone, shape, panel, or region to mark or draw in any way."
            : "Keep the full right third quiet enough to set story text over: continue the same "
              + "scene through it as calm open environment — sky, far foliage, open ground — "
              + "painted at exactly the same colour depth, saturation, contrast, exposure, and "
              + "finish as the rest of the picture. It is calm because the scene is calm there, "
              + "not because anything covers it: do not lighten it, and do not fade, veil, haze "
              + "over, wash, whiten, blur, or desaturate it or any other region of the painting. "
              + "It is part of the painting, not a panel: there must be no hard vertical boundary "
              + "where it begins, no flat field of colour, no visible edge between it and the rest "
              + "of the picture, and no change of tone marking where it begins or ends. No "
              + "character, face, hand, foreground object, or key action may enter this area. "
              + "Place the child and the main action in the outer-left area. Keep the area "
              + "around 40.6% of the canvas width and 45.8% of the canvas height naturally lit, "
              + "calm, and free of characters, faces, hands, hard edges, foreground objects, and "
              + "story-critical details — it is ordinary continuous environment exactly like its "
              + "surroundings, never a zone, shape, panel, or region to mark or draw in any way.";

    /// <summary>
    /// The centre of the canvas, described as ordinary painting with a content rule, not as a place.
    ///
    /// This line has been de-escalated twice, each time against a measured defect. v1.1 removed
    /// every word that named it a fold, because the first real books came back with a full-height
    /// dark band painted down the middle at 35× the baseline column-brightness step — a model
    /// that is told there is a fold draws a fold. That version still named a "central
    /// low-information zone", and the composition block used that zone as a landmark twice; the
    /// shipped book came back with every text-side veil terminating in a razor edge at exactly
    /// that landmark. A zone with a name is a zone with edges. So v1.5 stops naming a place at
    /// all: the constraint is now about content — keep faces and critical detail away from the
    /// middle — and the middle itself is required to be indistinguishable in treatment from the
    /// painting around it.
    /// </summary>
    public const string CentralZoneRule =
        "The middle of the canvas is ordinary painting: continue the environment through it with "
        + "the same light, the same colour, and the same level of detail as its surroundings, and "
        + "give it no edges, boundaries, or change of treatment of its own. No face, hand, child, "
        + "supporting character, or story-critical detail may sit at or near the horizontal "
        + "middle of the picture.";

    /// <summary>
    /// Which of the book's recurring elements this particular scene actually needs.
    ///
    /// The handoff asks for "only relevant recurring-element descriptions", and the reason is
    /// visible in the approved fixture: on spread 1 the small dinosaur is deliberately unseen, and
    /// a prompt carrying his description would have drawn him. The opposite fault is drift — a
    /// creature redesigned on the page after the one that introduced him — so this errs towards
    /// leaving an element out and lets the continuity reference carry the ones that are attached.
    ///
    /// The rule is the one the Visual Scenario contract makes available: a scene "reusing the same
    /// concise descriptions" for the elements it needs. So an element is relevant when the scene
    /// names it — its lead phrase, the text before the first comma with any article dropped — or
    /// when the two share at least two distinctive words. One shared word is not enough: every
    /// scene in a golden valley says "golden", and one coincidence would put a hidden character on
    /// the page.
    /// </summary>
    public static IReadOnlyList<string> RelevantRecurringElements(
        IReadOnlyList<string>? elements, string? scene)
    {
        if (elements is null or { Count: 0 } || string.IsNullOrWhiteSpace(scene))
        {
            return [];
        }

        var sceneTokens = Tokens(scene);

        return elements
            .Where(element => !string.IsNullOrWhiteSpace(element))
            .Where(element =>
                scene.Contains(LeadPhrase(element), StringComparison.OrdinalIgnoreCase)
                || Tokens(element).Count(sceneTokens.Contains) >= 2)
            .ToList();
    }

    /// <summary>
    /// One page's recurring elements resolved under the prop-state contract (v2.2), or — for a
    /// scenario planned before the amendment — by the fuzzy scene-matching above.
    ///
    /// The difference is the difference the supplier's audit measured. Fuzzy matching decided an
    /// element's presence from whether the model's own prose happened to name it, so the lantern
    /// appeared before its discovery and nothing was wrong with any single page. A stated state
    /// makes presence a plan: FOUND, CARRIED and PLACED elements are required with their state
    /// written into the prompt; AMBIENT ones are required as before; NOT_FOUND and
    /// NO_LONGER_CARRIED ones become explicit prohibitions in the hard constraints; ABSENT ones
    /// are simply not asked for.
    /// </summary>
    public static CompositeSpreadElements ElementsFor(
        IReadOnlyList<string>? elements,
        string? scene,
        IReadOnlyList<VisualScenarioProp>? props)
    {
        if (props is null)
        {
            var fuzzy = RelevantRecurringElements(elements, scene);
            return new CompositeSpreadElements(fuzzy, fuzzy, []);
        }

        var required = new List<string>();
        var annotated = new List<string>();
        var forbidden = new List<string>();

        foreach (var element in elements ?? [])
        {
            var state = props.FirstOrDefault(prop =>
                    string.Equals(prop.Element?.Trim(), element, StringComparison.Ordinal))
                ?.State?.Trim();

            if (state is null)
            {
                continue;
            }

            switch (state)
            {
                case VisualScenarioPropStates.Found:
                    required.Add(element);
                    annotated.Add(element + " — the child discovers this in this very moment; it has not been seen before this page.");
                    break;

                case VisualScenarioPropStates.Carried:
                    required.Add(element);
                    annotated.Add(element + " — the child is holding or carrying this.");
                    break;

                case VisualScenarioPropStates.Placed:
                    required.Add(element);
                    annotated.Add(element + " — the child is placing this where the story says it now belongs.");
                    break;

                case VisualScenarioPropStates.Ambient:
                    required.Add(element);
                    annotated.Add(element);
                    break;

                case VisualScenarioPropStates.NotFound:
                    forbidden.Add(
                        $"Do not show {LeadPhrase(element)} anywhere in this picture: the story "
                        + "has not discovered it yet.");
                    break;

                case VisualScenarioPropStates.NoLongerCarried:
                    forbidden.Add(
                        $"Do not show {LeadPhrase(element)} anywhere in this picture: the child "
                        + "left it behind and no longer has it.");
                    break;
            }
        }

        return new CompositeSpreadElements(required, annotated, forbidden);
    }

    /// <summary>
    /// The name an element leads with: "Bafu, a tiny mint-green baby sauropod…" is about Bafu.
    ///
    /// Falls back to the whole description when there is no comma, which is correct rather than a
    /// gap — an element written as one unbroken noun phrase has no shorter name, and the token
    /// rule above is what catches it when a scene refers to it in different words.
    /// </summary>
    private static string LeadPhrase(string element)
    {
        var head = element.Split(',')[0].Trim().TrimEnd('.');

        foreach (var article in (string[])["a ", "an ", "the "])
        {
            if (head.StartsWith(article, StringComparison.OrdinalIgnoreCase))
            {
                return head[article.Length..];
            }
        }

        return head;
    }

    /// <summary>
    /// The words worth comparing: four letters or more, lower-cased, stripped of punctuation and
    /// of a possessive, and none of the words every scene in every book contains.
    /// </summary>
    private static HashSet<string> Tokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var builder = new StringBuilder();

        void Flush()
        {
            if (builder.Length == 0) return;

            var word = builder.ToString();
            builder.Clear();

            if (word.EndsWith("'s", StringComparison.Ordinal))
            {
                word = word[..^2];
            }

            if (word.Length >= 4 && !GenericWords.Contains(word))
            {
                tokens.Add(word);
            }
        }

        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '\'')
            {
                builder.Append(c);
                continue;
            }

            Flush();
        }

        Flush();
        return tokens;
    }

    /// <summary>
    /// Words that carry no visual identity, so sharing one means nothing.
    ///
    /// Deliberately short and deliberately not a general English stop list: it holds the connective
    /// tissue every sentence has, plus the four nouns every scene in this product is about — the
    /// child, the page, the scene, the image. Colour and material words are absent on purpose;
    /// "mint-green" and "spiral-shaped" are exactly what should make an element recognisable.
    /// </summary>
    private static readonly HashSet<string> GenericWords = new(StringComparer.Ordinal)
    {
        "about", "above", "along", "also", "another", "back", "been", "below", "between", "both",
        "child", "children", "each", "from", "have", "here", "illustration", "image", "into",
        "just", "keep", "kept", "like", "made", "make", "more", "most", "near", "next", "onto",
        "only", "other", "over", "page", "same", "scene", "show", "shows", "some", "spread",
        "still", "such", "than", "that", "their", "them", "then", "there", "these", "they",
        "this", "those", "through", "toward", "towards", "under", "very", "when", "where",
        "which", "while", "whose", "will", "with", "would"
    };

    private static string ChildReferenceLine(int childAge) =>
        "Image 1 - " + ChildReferenceBody(childAge, anchored: false);

    /// <summary>
    /// The photograph, which is attached on every page whichever position it holds.
    ///
    /// Its job changes with its position and the wording says so. Alone, it is what the child looks
    /// like and what the picture is built from. Behind the anchor, it is the ground truth the
    /// anchor is answerable to — because the anchor is one stylization, and a stylization that came
    /// out slightly wrong must not become the book's definition of the child. It is never dropped:
    /// the handoff requires it on every call, and a book anchored only to its own first page is a
    /// book that can drift away from the child it is for without any one page being wrong.
    /// </summary>
    private static string ChildReferenceBody(int childAge, bool anchored) =>
        "child identity reference photograph. Preserve the child's recognizable identity: this "
        + "photograph says WHO the child is, and nothing else. Render the child's proportions and "
        + $"face at {childAge.ToString(CultureInfo.InvariantCulture)} years old, which is the age "
        + "this book is for, even if the photograph appears older or younger — it may have been "
        + "taken some time ago. Render the child as a warm, polished stylized 3D animated "
        + "character, not photorealistically. Do not copy clothing, pose, lighting, crop, or "
        + "background from the photo."
        + (anchored
            ? " This photograph is the identity ground truth: Image 1 shows how this child has "
              + "already been drawn, and where the two disagree about who the child is, the "
              + "photograph is right."
            : string.Empty);

    /// <summary>
    /// The world reference, named by the registry's working title and described in the registry's
    /// own words.
    ///
    /// The template's generic sentence — "use its world vocabulary, palette, atmosphere" — is kept
    /// and the registry's <c>visual_direction</c> added after it, because the approved run's
    /// resolved prompt names the world's actual light and materials rather than the categories.
    /// Taking that from the registry rather than writing six of them here means a re-art-directed
    /// world is a data change in the file that also carries its hash.
    /// </summary>
    private static string ThemeReferenceLine(CompositeThemeReference theme, bool forCover = false) =>
        "Image 2 - " + ThemeReferenceBody(theme, forCover);

    private static string ThemeReferenceBody(CompositeThemeReference theme, bool forCover = false) =>
        $"approved {theme.OfficialName} world/style reference. Use its world vocabulary, "
        + "palette, atmosphere, material treatment, and premium stylized 3D rendering language: "
        + $"{theme.VisualDirection.Trim()} "
        + (forCover
            ? "Create a new cover composition."
            : "Create a new composition; do not copy the reference composition.");

    /// <summary>
    /// The child appearance anchor: the accepted spread-1 base, and from v1.2 the FIRST image on
    /// every spread after it.
    ///
    /// Promoted deliberately. In v1.1 the anchor was attached third, behind the photograph and the
    /// world reference, and the book that came back was internally consistent but only because
    /// spreads 2-8 happened to agree — the owner's verdict on what still drifted was "not the cloth
    /// not the face not the hair not the eyebrows not the glasses". An image model weights the
    /// first reference hardest, and the first reference was a photograph of a real child, which
    /// every spread then re-stylized from scratch. The picture that already IS the answer now goes
    /// first, and the instruction asks for reproduction rather than resemblance.
    ///
    /// It still refuses the anchor everything that is not the child: pose, camera, layout and
    /// background come from this page's own scene and shot, and a model shown one picture and told
    /// to match it will otherwise redraw it whole.
    /// </summary>
    public const string AnchorInstruction =
        "child appearance anchor - the accepted first spread of this same book. Reproduce this "
        + "exact rendered child: same face and face shape, same hair colour and style, same "
        + "eyebrows, same glasses or absence of glasses, same eye colour, same skin tone, same "
        + "outfit down to its colours. Give the child a new pose, camera angle and background as "
        + "this page's scene requires. Do not copy the pose, camera, layout, lighting or "
        + "background from this image.";

    /// <summary>
    /// The outfit clause the anchored spreads add.
    ///
    /// The Visual Scenario's outfit sentence describes the clothes in words; the anchor shows them
    /// rendered. Words alone let the same description come out as a different mustard, a different
    /// collar, a different sash — which is what "not the cloth" meant.
    /// </summary>
    private static string OutfitAnchorClause(bool anchorAttached) =>
        anchorAttached ? " Draw the outfit exactly as rendered in Image 1." : string.Empty;

    /// <summary>
    /// The attached images, numbered by the order they are actually attached in.
    ///
    /// Numbering is computed here and nowhere else, because the numbers are positions in the
    /// request and a prompt that disagrees with the request tells the model to take the child's
    /// face from a picture of a dinosaur. Two shapes exist and the template documents both: the
    /// first spread leads with the photograph because there is no anchor yet, and every later
    /// spread leads with the anchor and demotes the photograph to second.
    ///
    /// The photograph is attached on every page either way — the handoff requires it, and it is
    /// what keeps the anchor honest: the anchor is one stylization of a child, and a stylization
    /// that drifted would otherwise become the book's definition of who the child is.
    /// </summary>
    private static string InputImageBlock(CompositeSpreadPromptInput input)
    {
        var lines = new List<string>();
        var number = 1;

        if (input.AnchorAttached)
        {
            lines.Add($"Image {number++} - {AnchorInstruction}");
        }

        lines.Add($"Image {number++} - {ChildReferenceBody(input.ChildAge, input.AnchorAttached)}");
        lines.Add($"Image {number++} - {ThemeReferenceBody(input.Theme)}");

        if (input.ContinuityElementNames.Count > 0)
        {
            lines.Add($"Image {number} - {ContinuityBody(input.ContinuityElementNames)}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// The continuity reference, present only when one is actually attached — the template is
    /// explicit that the placeholder is otherwise replaced by an empty string and no such image is
    /// mentioned. A prompt that names an image the request does not carry is a prompt the model
    /// answers by inventing what it thinks was there.
    /// </summary>
    private static string ContinuityBody(IReadOnlyList<string> elementNames) =>
        "continuity reference. Preserve only the appearance of these named recurring story "
        + $"elements: {string.Join("; ", elementNames)}. Do not copy the child, Beki, pose, camera, "
        + "layout, lighting, or background from this image.";

    private static string RecurringBlock(IReadOnlyList<string> elements) =>
        elements.Count == 0
            ? "None."
            : string.Join("\n", elements.Select(element => element.Trim()));

    /// <summary>
    /// The template's own constraint list, in its order.
    ///
    /// "Do not generate Beki" sits in the middle of it and is the single most important line in the
    /// whole prompt: every promise this pipeline makes about the character — one approved PNG,
    /// pasted, never redrawn — holds only while no image model is ever asked to draw her. The two
    /// lines after it exist because a model told not to draw a named guide draws an unnamed one.
    ///
    /// The unbroken-painting line is v1.1's rewrite of the same rule, extended by v1.5. v1.1
    /// banned what the first books actually painted — a line, a crease, a band, a strip, an edge,
    /// a border, a split — every one of them a DARK thing, and the models obliged by switching to
    /// the light version: a milky veil over the whole text-side half, which no word of the list
    /// forbade. v1.5 bans the defect symmetrically (pale strip, milky band, whitened half) and
    /// adds the rule the audit's acceptance test actually measures: the two halves must match in
    /// brightness, colour, contrast and finish.
    /// </summary>
    private const string SpreadConstraints =
        """
        Exactly one child.
        Do not generate Beki.
        Do not generate any substitute guide, floating mascot, leaf spirit, lamb, sheep, or Beki-like character.
        Do not generate characters or objects not required by the current scene.
        No duplicate child or duplicated supporting character.
        No text, letters, numbers, logos, captions, labels, signs, frames, QR codes, watermarks, or pseudo-text anywhere.
        The picture is one continuous unbroken painting: no visible vertical dividing line, crease, shadow band, dark strip, pale strip, milky or whitened band, page edge, border, or split down the middle. Paint the environment straight through the centre of the canvas as if it were any other part of the scene.
        The left and right halves of the picture must match in brightness, colour, contrast, and finish: neither half may be lighter, paler, hazier, more faded, or more washed-out than the other, and no veil, wash, fog, or overlay may cover any part of the painting.
        No split screen, montage, comic panel, inset frame, before-and-after view, or repeated version of the same character.
        No dark or pale text panel, milky veil, white or cream overlay, artificial blur panel, or blank rectangle. No translucent or semi-transparent rectangle, square, or panel of any size, anywhere in the picture, for any purpose. The text-safe area is ordinary full-colour painting like the rest of the scene.
        """;

    /// <summary>
    /// The cover template's constraint list, amended by v1.1 the way v1.1 and v1.5 amended the
    /// spread's.
    ///
    /// The dieline landed, so this prompt is live, and the first wrap it produced came back with
    /// the defect the supplier's P0-03 measured: vertical tonal jumps at the two spine boundaries
    /// and a warm-green-to-purple change of world across them. Two lines of the old list invited
    /// exactly that. It named the fold — "the fold is where the printed book will be bound" — and
    /// v1.1 of the spread template already proved that a model told about a fold paints one. And
    /// it banned only drawn things (a line, a crease, a seam), so a *tonal* discontinuity satisfied
    /// every word of it.
    ///
    /// So the fold is not mentioned, the centre is described as painting rather than as
    /// architecture, and the ban is stated the way the defect actually appeared: no vertical step
    /// in tone, colour, or light anywhere across the picture, and the two sides matching in
    /// brightness, colour, contrast, and finish — which is the measurement the cover-band gate now
    /// takes at the four dieline lines. "Spine text" also leaves the no-text line: the line already
    /// says "anywhere", and the only thing naming the spine there could add is the idea of a spine.
    /// </summary>
    private const string CoverConstraints =
        """
        Exactly one child, on the right side of the picture only.
        Do not generate Beki.
        Do not generate any substitute guide, floating mascot, leaf spirit, lamb, sheep, or Beki-like character.
        No duplicate child or mirrored second child.
        No text, title, letters, numbers, logo, caption, label, sign, QR code, watermark, or pseudo-text anywhere.
        The picture is one continuous unbroken painting: no visible dividing line, crease, seam, shadow band, dark strip, pale strip, tinted band, page edge, border, or split anywhere across it. Paint the environment straight through the middle of the picture as if it were any other part of the scene.
        No vertical step in tone, colour, temperature, or light anywhere in the picture: the world does not change from one side of the picture to the other, and the left and right of the painting must match in brightness, colour, contrast, and finish.
        No split screen, montage, comic panel, inset frame, or mirrored composition.
        """;
}

/// <summary>
/// The Visual Scenario v2 call, as a prompt and an input document.
///
/// Transcribed from <c>contracts/BEKI_Visual_Scenario_Prompt_v2.md</c> for the same reason the
/// image template is: the contract folder's Markdown is not shipped into the published output, so
/// the alternative to a transcription is reading a file that is not there. The MD remains the
/// source of truth and this is its copy — a supplier revision is a deliberate edit here.
/// </summary>
public static class CompositeVisualScenarioPrompt
{
    /// <summary>
    /// v2.1 is this campaign's amendment: the contract's instruction, unchanged, with one appended
    /// block naming the nine verb families the deterministic pose table can read.
    ///
    /// It is a version bump rather than a silent addition because this string is recorded against
    /// every scenario call — a book planned before the steering and a book planned after it were
    /// asked for different sentences, and the record should say which.
    ///
    /// v2.2 adds the prop-state contract, against the supplier's rejection of the lantern book:
    /// the key object was in the child's hand a page before its discovery and again a page after
    /// being left in the nest, and every page passed its own review because nothing anywhere
    /// stated where the object stood. Every spread now carries one state per recurring element
    /// (NOT_FOUND → FOUND → CARRIED → PLACED → NO_LONGER_CARRIED for a carried object; AMBIENT
    /// for a companion; ABSENT for "not in this picture"), the validator enforces the chain's
    /// order across the book, and the image prompt turns the states into explicit inclusions and
    /// prohibitions.
    ///
    /// v2.3 answers two of the supplier's audit-2 findings at once, and both are about what a
    /// stated plan is worth.
    ///
    /// P1-08: page 7 of the audited book began <c>" sensitivity, the child gently pats…"</c> — a
    /// fragment with a leading space, no subject and no beginning, sent verbatim to a paid image
    /// call, because the only text-quality rule anywhere was "not empty". The supplied schema now
    /// carries sentence guards (capital first letter, four words or more, terminal punctuation),
    /// <see cref="VisualScenarioValidator"/> carries the exact-trim and leading-fragment rules
    /// that JSON Schema cannot express, and <see cref="ResponseSchema"/> asks the model for the
    /// same shape at generation time so the rule is a request rather than only a rejection.
    ///
    /// P1-07: the audited book's pinecone was described as fading in the story and drawn brightly
    /// glowing on the same spread. The prop chain says where an object *is*; it never said what it
    /// was *doing*, so a light-emitting object had no planned state for the one property the story
    /// kept changing. The PROP STATES block now requires the luminosity or intensity of a
    /// story-critical light source to be stated in the page's own scene text, which is what the
    /// existing PROP_STATE reviewer reads — no new enum, no new field, nothing downstream to
    /// migrate.
    /// </summary>
    public const string Version = "visual-scenario-v2.3";

    /// <summary>The schema name recorded against the call. The file itself is the response schema.</summary>
    public const string SchemaName = "visual_scenario_v2";

    /// <summary>
    /// The input document, in the shape <c>visual_scenario_input_v2.schema.json</c> describes and
    /// no other: an age band, a gender, the theme, and exactly eight ordered Georgian pages.
    ///
    /// The child's name is not in it, and neither is the photograph, the appearance description or
    /// the Extra Wish. The planner is writing English scene descriptions about "the child"; a name
    /// would only give it something to write into a picture as lettering, and everything else on
    /// that list is locked out of the MVP entirely.
    /// </summary>
    public static string InputJson(
        NormalizedBookInput input, CompositeThemeReference theme, StoryBoundaryOutput boundary)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(boundary);

        var document = new
        {
            age_group = input.AgeBand,
            child_gender = input.ChildGender,
            theme = new
            {
                id = theme.Id,
                official_name = theme.OfficialName,
                visual_direction = theme.VisualDirection
            },
            story_pages = boundary.StoryPages
        };

        return JsonSerializer.Serialize(document, CompositeJson.Readable);
    }

    /// <summary>The runtime user message, in the contract's own words.</summary>
    public static string User(string inputJson) =>
        $"Create the visual scenario from this input:\n\n{inputJson}";

    /// <summary>
    /// The one permitted retry's message: the original ask, whole, with the validator's short
    /// error list appended.
    ///
    /// Appended rather than substituted, which is the same idiom the rest of this codebase uses for
    /// a corrective retry. A rewritten prompt asks for a different scenario; the point of the
    /// retry is the same scenario without the fault, and the contract is explicit that the retry
    /// goes out "with the same original input and the validator's short error list".
    /// </summary>
    public static string RetryUser(string inputJson, IReadOnlyList<VisualScenarioProblem> problems)
    {
        var numbered = string.Join(
            "\n", problems.Select((problem, index) => $"{index + 1}. {problem}"));

        return User(inputJson)
            + "\n\nThe previous answer was rejected for these reasons. Return a corrected visual "
            + $"scenario for the same story, with each of them fixed:\n{numbered}";
    }

    /// <summary>
    /// What is actually sent: the contract's instruction, then v2.1's vocabulary block.
    ///
    /// Two members rather than one edited string, so the transcription of the supplier's document
    /// stays a transcription — <see cref="System"/> can still be diffed against the MD line for
    /// line, and the amendment this campaign made is visible as an amendment rather than as an
    /// untraceable edit inside 90 lines of somebody else's prose.
    ///
    /// The block is appended last on purpose. Everything above it is the contract's own definition
    /// of a <c>beki_action</c> — one concise sentence, Beki named, no pose id, no body, no page
    /// position — and this only narrows the verb, so it has to be read after the rules it narrows.
    /// </summary>
    public static string SystemInstruction { get; } =
        System + "\n\n" + CompositePoseVocabulary.PromptBlock();

    /// <summary>
    /// The contract's exact system instruction. Every line of it is load-bearing; the two that
    /// decide whether the pipeline works at all are the separation of the child/world scene from
    /// the Beki action, and the rule that a scene may never mention Beki.
    ///
    /// Kept verbatim and no longer sent on its own — see <see cref="SystemInstruction"/>.
    /// </summary>
    public const string System =
        """
        You are the Visual Scenario Planner for BEKI personalized children's books.

        Your only task is to read one approved eight-spread Georgian story and convert it into:
        1. one short book-level visual lock;
        2. one continuous cover plan;
        3. exactly eight concise story-spread plans.

        Read all eight story pages before planning the visual sequence. Preserve the story exactly. Do not rewrite it, continue it, improve it, or invent new plot events.

        The production system generates the child and story world first, then composites Beki later from an exact approved transparent PNG. Therefore every cover/spread plan must separate the child/world image scene from Beki's action.

        GENERAL RULES

        - Write every output description in clear English.
        - Write every output description as whole sentences: a capital letter first, no leading or trailing space, at least four words, and a full stop, question mark or exclamation mark last. Never begin a description mid-phrase, with a stray fragment, or with "and", "but", "so", "because" or a comma. Every scene description is sent to the image model exactly as you write it.
        - Refer to the personalized protagonist as "the child". Do not invent or describe the child's face, eye color, hair, body, or other identity traits. Those are controlled later by the child photo and structured visual inputs.
        - Refer to the guide character only as "Beki". Do not define Beki's species and do not physically describe or redesign Beki.
        - The child is always the active protagonist. Beki may guide, help, point, react, encourage, listen, reassure, or reveal a path, but must never perform the child's main action instead of the child.
        - Use one visually clear moment per image.
        - Do not use montages, split screens, comic panels, inset frames, before-and-after views, or repeated versions of the same character.
        - Do not include readable text, letters, numbers, logos, labels, signs, typography, frames, or QR codes inside a scene.
        - Do not specify text placement, page layout, left/right positioning, margins, fold placement, dimensions, camera specifications, typography, print settings, or image-model parameters. Those are controlled by code.
        - Keep visual complexity appropriate for the supplied age group while preserving one obvious main action.
        - Supporting characters and objects may appear only when the corresponding story page requires them.

        VISUAL LOCK

        - Define one simple, age-appropriate, theme-appropriate base outfit for the child.
        - The outfit must contain no logo or readable text and must not hide the child's face.
        - The same base outfit is used on the cover and all eight spreads.
        - Story-required accessories may be added without replacing the base outfit or hiding the child's face.
        - List only recurring story elements whose appearance must remain consistent across multiple images.
        - Include no more than three recurring elements. Use an empty array when none are necessary.
        - Do not include Beki in recurring_elements.
        - Do not invent a recurring object unsupported by the story.

        CHILD/WORLD SCENES

        - A child_world_scene is sent directly to the image model.
        - It must explicitly mention "the child".
        - It must never mention Beki and must not include any substitute guide, floating mascot, leaf spirit, lamb, sheep, or Beki-like character.
        - It must state the child's concrete action and the page's one visible story beat.
        - It may include only story characters/objects required on that page.
        - It must be understandable as one image without reading the story text.
        - It must not contain pose, camera, text-side, fold, typography, or print instructions.

        BEKI ACTIONS

        - A beki_action is not sent to the image model. It is used by code to choose one approved Beki pose.
        - It must be one concise sentence that explicitly mentions "Beki".
        - State only Beki's supporting action or reaction for that moment.
        - Do not describe Beki's body, materials, colors, costume, species, size, page position, or camera relationship.
        - Do not name a pose ID. Code selects the pose deterministically.

        COVER

        - front_child_world_scene must show the child in one inviting action that represents the central adventure, question, or mystery.
        - It must not reveal the ending or copy one story spread literally.
        - It must not mention or depict Beki; Beki is added later from the separate cover beki_action.
        - cover.beki_action gives Beki one inviting supporting action.
        - back_environment is a natural continuation of the same world, atmosphere, lighting, and terrain.
        - back_environment contains neither the child nor Beki.

        STORY SPREADS

        - Create exactly one plan for each story page from 1 through 8.
        - Show only the event described on that page. Do not borrow events from another page.
        - Preserve recurring characters and objects consistently by reusing the same concise descriptions.
        - Keep child_world_scene concise: normally one to three precise sentences.
        - Keep beki_action to one concise sentence.
        - Vary action and emotion naturally, but never invent activity only to create variety.

        PROP STATES

        - Every spread carries a props array with EXACTLY ONE entry per recurring element, using the element's exact recurring_elements wording.
        - Each entry's state says where that element stands on that page, so no picture can show an object the story has not discovered yet or has already left behind.
        - For an object the child finds, carries, and leaves, use the chain in story order: NOT_FOUND on every page before the discovery, FOUND on the one page where the child discovers it, CARRIED while the child has it, PLACED on the one page where the child sets it where it now belongs, NO_LONGER_CARRIED on every page after that.
        - FOUND appears on exactly one page. PLACED appears on at most one page. Never move backwards along the chain.
        - For a companion character or a scenery element that is simply present, use AMBIENT on the pages where it appears.
        - Use ABSENT for any element that is not visible in that page's picture. Never mix AMBIENT with the chain states for one element.
        - The scene text and the state must agree: a page whose scene shows the child holding the object is a CARRIED page, and a page before the discovery must neither show nor name it.
        - When a recurring element gives off light and the story turns on it, that page's child_world_scene must state how strongly it is shining right then — brightly glowing, softly lit, dimming, nearly out, dark. State it on every page the element appears on, and follow the story: an object the story says is fading is fading in that picture and stays that way until the story lights it again.

        OUTPUT

        Return valid JSON only, with exactly this structure and no additional keys:

        {
          "visual_lock": {
            "child_outfit": "One concise outfit description",
            "recurring_elements": [
              "Zero to three concise recurring-element descriptions"
            ]
          },
          "cover": {
            "front_child_world_scene": "One concise cover scene that mentions the child and does not mention Beki",
            "beki_action": "One concise sentence that explicitly mentions Beki",
            "back_environment": "One concise continuation of the same environment without the child or Beki"
          },
          "spreads": [
            {
              "page": 1,
              "child_world_scene": "One concise scene that mentions the child and does not mention Beki",
              "beki_action": "One concise sentence that explicitly mentions Beki",
              "props": [
                {
                  "element": "The exact recurring_elements wording",
                  "state": "NOT_FOUND | FOUND | CARRIED | PLACED | NO_LONGER_CARRIED | AMBIENT | ABSENT"
                }
              ]
            }
          ]
        }

        The spreads array must contain exactly eight entries, ordered from page 1 to page 8. Each spread's props array holds one entry per recurring element; use an empty array when recurring_elements is empty.
        """;

    /// <summary>
    /// The shape asked of the provider — the supplied contract's structure, in the subset of JSON
    /// Schema a strict structured-output mode actually accepts.
    ///
    /// This is not a second source of truth, and the distinction is worth stating precisely.
    /// <c>visual_scenario_v2.schema.json</c> remains the authority: every response is evaluated
    /// against that file, verbatim, by <see cref="VisualScenarioValidator"/>, and a response that
    /// satisfies the shape below while failing the supplied schema is a validation failure that
    /// spends the one permitted retry. What this method produces is only the request's half of the
    /// conversation.
    ///
    /// It exists because sending the supplied file was, on the default configuration, a book that
    /// could not be written at all. The story provider defaults to OpenAI, whose Responses API in
    /// strict mode rejects <c>prefixItems</c>, a boolean <c>items</c>, <c>minItems</c>,
    /// <c>maxItems</c> and <c>minLength</c> — and the supplied schema uses all five to pin the
    /// eight spreads to pages 1..8. So both attempts failed on the request rather than on the
    /// answer, which is the worst way to fail: nothing is generated, nothing is reviewable, and the
    /// error is about a schema keyword rather than about a book.
    ///
    /// What the shape loses, the descriptions carry — exactly the trade
    /// <see cref="Prompts.BekiBookPlanSchema"/> and <see cref="CompositeStorySchema"/> already
    /// make. "Exactly eight, numbered 1 to 8 in order" and "at most three" are stated in words to
    /// the model and enforced in code afterwards, which is where they were always enforced: the
    /// supplied schema's <c>maxItems</c> never stopped a model returning four, it only described
    /// what would happen next.
    ///
    /// v2.3's sentence guards are carried the same way, and deliberately so. The supplied file
    /// states them as <c>pattern</c> and <c>minLength</c>; strict structured output is the mode
    /// that rejected <c>minLength</c> outright and failed the request rather than the answer, so
    /// putting a regular expression into this document would risk buying the page-7 fragment fix
    /// at the price of a book that cannot be requested at all. The rule reaches the model as the
    /// thing a model actually reads — the field's own description, in the same words as the
    /// system instruction's new GENERAL RULE — and <see cref="VisualScenarioValidator"/> and the
    /// supplied schema still refuse the fragment afterwards. Generation-time steering, validation
    /// enforcement: the same division the eight-spread rule already lives under.
    /// </summary>
    public static JsonElement ResponseSchema()
    {
        var schema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "visual_lock", "cover", "spreads" },
            properties = new Dictionary<string, object>
            {
                ["visual_lock"] = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "child_outfit", "recurring_elements" },
                    properties = new Dictionary<string, object>
                    {
                        ["child_outfit"] = Text(
                            "One concise base outfit for the child, worn on the cover and all eight "
                            + "spreads. No logo and no readable text, and it must not hide the face."),
                        ["recurring_elements"] = new
                        {
                            type = "array",
                            description =
                                "AT MOST THREE entries — a fourth is rejected. Only recurring story "
                                + "elements whose appearance must stay consistent across images. "
                                + "Never Beki. An empty array is a valid answer.",
                            items = new { type = "string" }
                        }
                    }
                },
                ["cover"] = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "front_child_world_scene", "beki_action", "back_environment" },
                    properties = new Dictionary<string, object>
                    {
                        ["front_child_world_scene"] = Text(
                            "One concise cover scene that mentions \"the child\" and never mentions "
                            + "Beki. " + WholeSentence),
                        ["beki_action"] = Text(
                            "One concise sentence that explicitly mentions Beki. " + ShortSentence),
                        ["back_environment"] = Text(
                            "One concise continuation of the same environment containing neither "
                            + "the child nor Beki. " + WholeSentence)
                    }
                },
                ["spreads"] = new
                {
                    type = "array",
                    description =
                        $"EXACTLY {BookFormat.SpreadCount} entries, one per story page, ordered from "
                        + $"page 1 to page {BookFormat.SpreadCount}, each page number used exactly "
                        + "once. Any other count or order is rejected.",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "page", "child_world_scene", "beki_action", "props" },
                        properties = new Dictionary<string, object>
                        {
                            ["page"] = new
                            {
                                type = "integer",
                                description =
                                    $"This spread's page number, 1 to {BookFormat.SpreadCount}, "
                                    + "matching its position in the array."
                            },
                            ["child_world_scene"] = Text(
                                "One to three precise sentences. Sent straight to the image model. "
                                + "Must mention \"the child\" and must never mention Beki or any "
                                + "substitute guide. " + WholeSentence),
                            ["beki_action"] = Text(
                                "One concise sentence that explicitly mentions Beki. Read only by "
                                + "code, to choose an approved pose. " + ShortSentence),
                            ["props"] = new
                            {
                                type = "array",
                                description =
                                    "EXACTLY one entry per recurring element, using the element's "
                                    + "exact recurring_elements wording; an empty array when there "
                                    + "are no recurring elements. States follow the PROP STATES "
                                    + "rules: the carried-object chain in story order, AMBIENT for "
                                    + "a present companion, ABSENT for anything not in this "
                                    + "picture.",
                                items = new
                                {
                                    type = "object",
                                    additionalProperties = false,
                                    required = new[] { "element", "state" },
                                    properties = new Dictionary<string, object>
                                    {
                                        ["element"] = Text(
                                            "The exact recurring_elements wording for this "
                                            + "element."),
                                        ["state"] = new
                                        {
                                            type = "string",
                                            @enum = VisualScenarioPropStates.All,
                                            description =
                                                "Where this element stands on this page."
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        // CompositeJson rather than StoryJson: these property names are the supplier's snake_case
        // contract, and a naming policy that decided to touch them would produce a request whose
        // shape no longer matches the document the answer is validated against.
        return JsonSerializer.SerializeToElement(schema, CompositeJson.Options);
    }

    private static object Text(string description) => new { type = "string", description };

    /// <summary>
    /// v2.3's sentence guard, in the words the request carries — the supplied schema's
    /// <c>pattern</c> said to a model instead of to a validator.
    ///
    /// One string reused by all four narrative fields rather than four paraphrases, because the
    /// supplied schema applies one rule to all four and two wordings would eventually mean two
    /// rules.
    /// </summary>
    private const string WholeSentence =
        "A whole sentence: capital letter first, no leading or trailing space, at least four "
        + "words, and a full stop, question mark or exclamation mark last. Never a fragment and "
        + "never a mid-phrase opening.";

    /// <summary>The same guard at the three-word floor a <c>beki_action</c> is written to.</summary>
    private const string ShortSentence =
        "A whole sentence: capital letter first, no leading or trailing space, at least three "
        + "words, and a full stop, question mark or exclamation mark last. Never a fragment and "
        + "never a mid-phrase opening.";
}
