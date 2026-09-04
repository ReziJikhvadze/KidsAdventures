namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// The physical shape of a Beki-format book.
///
/// Separate from <see cref="PrintLayoutOptions"/> rather than a second set of values inside it,
/// because both formats are staying: A5 books keep being printed from the numbers they were
/// always printed from, and nothing here can move them.
///
/// The spread is the unit, not the page. One illustration runs across both leaves and the story
/// text is set over it, so the geometry starts from the spread and the page is half of it — the
/// opposite of the A5 book, where a page is a page and a spread is two of them side by side.
///
/// **The numbers are the handoff's, and they are exact.** 440 × 200 mm trims out of a 450 × 210 mm
/// sheet with 5 mm of bleed on every outer edge; at 300 PPI that sheet is 5315 × 2480 px, and its
/// ratio is exactly 15:7 — which is the ratio the illustration stage normalizes to, so a picture
/// that arrived normalized fits the sheet with nothing to crop. The book shipped at 446 × 206 with
/// 3 mm for a while, which is a different physical object; the supplier's audit of a printed PDF is
/// what found it.
/// </summary>
public sealed class BekiPrintLayoutOptions
{
    public const string SectionName = "BekiPrintLayout";

    /// <summary>
    /// The finished spread, both leaves together, in millimetres — the trim, not the sheet. The
    /// handoff's page is 220 × 200; the spread is two of them side by side.
    /// </summary>
    public float SpreadWidthMm { get; set; } = 440f;

    /// <summary>The handoff's page height. The spread and the single leaf share it.</summary>
    public float SpreadHeightMm { get; set; } = 200f;

    /// <summary>Half the spread. A single leaf, portrait, the way a picture book opens.</summary>
    public float PageWidthMm => SpreadWidthMm / 2f;

    /// <summary>
    /// How far the illustration runs past the trim on every outer edge, in millimetres.
    ///
    /// Five, per the handoff's interior rule — which puts the MediaBox and BleedBox at 450 × 210 and
    /// the TrimBox at 440 × 200 centred inside them. This does not apply to the cover: a cover is a
    /// wrap with a spine and its geometry comes from the printer's dieline (handoff §5).
    /// </summary>
    public float BleedMm { get; set; } = 5f;

    /// <summary>
    /// How far the story text stays clear of the trim — the spec's outer safe area. Larger than
    /// the A5 book's margin because this text sits over artwork rather than on paper, and a line
    /// that runs close to the edge of a picture reads as part of the picture.
    /// </summary>
    public float SafeMarginMm { get; set; } = 12f;

    /// <summary>
    /// The width of the low-information band straddling the fold, in millimetres — half of it
    /// falls on each page. A print gutter swallows a sliver of the sheet into the binding, and
    /// even before a printer's own imposition is known, nothing planned this close to the fold
    /// should be trusted to survive it. Used to hold the story text column's inner edge back from
    /// the fold on every spread, so a widened <see cref="TextColumnShare"/> can never quietly creep
    /// into it.
    /// </summary>
    public float GutterZoneMm { get; set; } = 30f;

    /// <summary>
    /// How far the story wash must stay clear of the centre fold, in millimetres.
    ///
    /// Separate from <see cref="GutterZoneMm"/>, which is about where a text COLUMN may begin. This
    /// is about where the cream may end. Audit P1-04's correction is explicit — "keep it within the
    /// selected page, outside the fold safety area and trim safety margins" — and §10.3 repeats it,
    /// because the rejected book had a wash crossing the fold on Story Spread 4. Ten millimetres,
    /// five on each leaf, is the smallest gap at which a soft-edged shape reads as belonging to one
    /// page rather than to the binding.
    /// </summary>
    public float FoldSafetyMm { get; set; } = 10f;

    /// <summary>
    /// How far the story panel reaches past the wrapped copy on every side, in millimetres.
    ///
    /// Seven, which is the middle of the audit's own range: P1-04 asks for "one local soft cream
    /// wash with approximately 6–8 mm internal padding". It was 6 mm while the wash existed the
    /// first time, and it is configuration now because the range is the supplier's and a printed
    /// proof is what settles where inside it this book sits.
    ///
    /// This number is also the copy column's inset, and it was that even while no panel was drawn:
    /// the measure the step-down ladder fits copy to is the column less twice this, so changing it
    /// re-wraps every book. The panel (<see cref="StoryPanelOpacity"/>) is drawn to the column's
    /// inset edge, which is what makes the panel's reach past the copy and the copy's inset from the
    /// column the same seven millimetres.
    /// </summary>
    public float WashPaddingMm { get; set; } = 7f;

    /// <summary>
    /// The story panel's corner radius, in millimetres, so the shape reads as a soft shade under the
    /// words rather than as a cut rectangle pasted onto the picture. Zero gives square corners.
    /// </summary>
    public float WashCornerRadiusMm { get; set; } = 4f;

    /// <summary>
    /// The ink of the translucent panel behind the story and intro copy, <c>RRGGBB</c> with or
    /// without its hash.
    ///
    /// **Owner ruling 2026-09-01, the fourth on the question and the one that stands:** "we need
    /// good background on the texts to be readable — transparent-like background, but not too
    /// transparent." The three rulings before it took the cream wash out and left the copy as cream
    /// type with a dark rim straight on the artwork, and on pale artwork that copy fails in a way no
    /// rim can fix: cream letters on a cream-ish picture are hollow outlines, the fill is
    /// indistinguishable from the ground, and the word is drawn by its edge alone. A dark translucent
    /// shade under the block gives the cream fill something to be lighter than on every picture,
    /// and lets the picture stay visible through it.
    ///
    /// The page's own plum, <c>#281B3F</c>, so the shade reads as the book's ink and not as a second
    /// colour on the spread. The type on top of it is unchanged — the same cream, the same rim, the
    /// same offset stack — and so is every millimetre of the layout: the panel is drawn inside the
    /// column the copy already had, at the column's own inset, and moves nothing.
    /// </summary>
    public string StoryPanelInkHex { get; set; } = "281B3F";

    /// <summary>
    /// How opaque that panel is, 0–1. Zero draws no panel at all and is the pre-ruling book; one is
    /// an opaque box, which the ruling equally does not want ("transparent-like").
    ///
    /// 0.6 is the ruling's "not too transparent" made a number: at 60% the plum darkens a cream
    /// ground to about a third of its brightness, so the cream fill is plainly the lightest thing
    /// inside the block, while enough of the illustration comes through that the panel reads as a
    /// shade on the picture rather than a card laid over it. Clamped to 0–1 wherever it is read.
    /// </summary>
    public float StoryPanelOpacity { get; set; } = 0.6f;

    /// <summary>
    /// The largest linear resize <see cref="Services.Story.BekiPdfComposer"/> may perform on its way
    /// to the print raster WITHOUT SAYING SO, as a factor of the source's own dimensions.
    ///
    /// **It used to be a refusal and it is now a disclosure.** Audit P1-01 found the interior
    /// rasters at about 143 effective PPI, Lanczos-stretched to a nominal 300 inside the press PDF:
    /// the number on the file said 300 and the picture in it did not. Correction D5b answered that by
    /// stopping the book above this factor — which, with no super-resolver on the deployment, means
    /// the press interior is never built at all, at any size. Owner ruling 2026-09-01, rule 4: "the
    /// sizes we have indicated for printing are correct." The book is delivered at the sizes the
    /// product states, so the composer performs the upscale.
    ///
    /// What the audit was actually right about is kept, and kept where it belongs — in the evidence.
    /// A resize above this factor is delivered AND marked: the page's layout receipt carries the
    /// source pixels, the delivered pixels and <c>interpolated: true</c>, print prep's
    /// <c>PRESS_RESOLUTION</c> gate still fails on it in the preflight report, and the release policy
    /// decides what a failed gate is worth. Nothing here tells a printer 300 PPI of detail arrived
    /// when it did not; it stops pretending that refusing to build the file is the same as fixing it.
    ///
    /// 1.05 — five per cent, a rounding difference rather than a claim, so a source that is already
    /// the sheet to within its own rounding is not flagged. Downscaling is never flagged: reducing an
    /// approved asset loses nothing a printer would have seen.
    ///
    /// Zero or less marks nothing at all, which is what the shared screen-proof fixture asks for: it
    /// composes stand-in artwork at 96 PPI precisely because none of its questions are about
    /// resolution.
    /// </summary>
    public float MaxPrintUpscale { get; set; } = 1.05f;

    /// <summary>
    /// The share of the spread reserved for story text — the same third the illustrator was told
    /// to leave quiet. Written here as well so the two cannot disagree: if this widens and the
    /// prompt does not, text starts landing on faces the model was never asked to move.
    /// </summary>
    public float TextColumnShare { get; set; } = 0.33f;

    /// <summary>
    /// Story text size, in points — the approved spread-1 reference's 18 pt (handoff §6 Step 8).
    ///
    /// A spread is read aloud from arm's length by an adult holding a book open, which is further
    /// away than a page of prose is ever read from. Configurable, and deliberately: the handoff
    /// calls these "configurable v0 defaults, not permanent typography law for every age band".
    /// </summary>
    public float StoryFontSize { get; set; } = 18f;

    /// <summary>
    /// The leading the reference sets <see cref="StoryFontSize"/> on, in points: 18 on 27.
    ///
    /// Stated as a pair of point sizes rather than as a multiplier because that is how the approved
    /// proof states it, and applied as their ratio, so a spread that steps down to 16 pt tightens
    /// its leading with the type instead of keeping 27 pt of air around smaller words.
    /// </summary>
    public float StoryLeadingPt { get; set; } = 27f;

    /// <summary>
    /// The widest measure a line of story text may be set to, in millimetres — the reference's
    /// 170 mm. On today's 450 mm spread a third of the sheet is already narrower than this, so the
    /// cap does not bind; it is written down because <see cref="TextColumnShare"/> is configuration
    /// and a very wide column would otherwise produce a measure no reading age can track across.
    /// </summary>
    public float MaxTextWidthMm { get; set; } = 170f;

    /// <summary>
    /// Whether the English text is printed under the Georgian. Off by default: the handoff asks
    /// for both languages to exist, not for both to be on the same spread, and two languages over
    /// one illustration is twice the text in the space that was reserved for one.
    /// </summary>
    public bool PrintEnglishToo { get; set; }

    /// <summary>
    /// The smallest rim any outlined glyph may carry, in points — and the switch that turns the rim
    /// off altogether.
    ///
    /// A floor rather than the width itself since owner ruling 2026-09-01 (rule 3): "text must have
    /// a STRONGER border so it is readable on all backgrounds." A fixed 0.6 pt was the whole rim on
    /// every block in the book, which meant an 18 pt story line and a 36 pt cover title carried the
    /// same edge — a thirtieth of the em on one and a sixtieth on the other. The rim is now a
    /// proportion of the type it surrounds (<see cref="TextOutlineWidthFactor"/>) and this is the
    /// floor under it, so a very small size still gets an edge a press can hold.
    ///
    /// Zero still means no rim at all, on any block, whatever the factor says — the one setting a
    /// caller who wants plain type reaches for.
    /// </summary>
    public float TextOutlineWidth { get; set; } = 0.6f;

    /// <summary>
    /// The rim around every outlined glyph, as a fraction of that block's own type size.
    ///
    /// **0.09 — nine per cent of the em, chosen by measurement and not by eye.** Owner ruling
    /// 2026-09-01, rule 3, is that book copy has to survive ANY background. When the number was
    /// chosen the ruling before it had taken away the cream wash, so every word over artwork was
    /// cream #FFF8EB with a #0D071D rim and nothing else, and the worst case was a background the
    /// exact colour of the fill, where the rim is the only thing drawing the glyph at all.
    ///
    /// <c>BekiTextRimReadabilityTests</c> composed that worst case — a spread whose artwork is solid
    /// #FFF8EB — rendered it at 220 PPI, and measured the rim's dark coverage inside the copy
    /// column's own ink box. The old flat 0.6 pt rim on 20 pt type (0.03 of the em) leaves 5.8% of
    /// that box dark: a hairline, and one that closes up the moment a press dot-gains. At 0.09 the
    /// same measurement is 17.4% — three times the rim, and a glyph you can read as a shape. The
    /// cover title, being twice story size, goes from 14,518 rim pixels to 73,235.
    ///
    /// Deliberately not "as thick as possible". At 0.12 the measurement is 21.3% and the counters of
    /// the round Georgian letters — the bowl of ო, the eye of ფ — are visibly narrowing; past that
    /// the rim starts eating the letterform it exists to describe, and a rim heavy enough to merge
    /// between letters would be a panel made of rim, which is not what a panel is for. 0.09 is the
    /// far side of legible-at-a-glance and the near side of that.
    ///
    /// It applies to every outlined block equally: story copy in both languages, the intro spread's
    /// four lines, the back-cover address and the cover title — which, being twice story size, gets
    /// twice the rim out of the same number.
    ///
    /// This is a RIM, and it is the same rim since the panel came back. The fourth ruling of
    /// 2026-09-01 (<see cref="StoryPanelOpacity"/>) put a translucent shade under the story and
    /// intro blocks because the rim alone could not make cream letters read on pale artwork; it did
    /// not replace the rim, which is still what separates the fill from whatever is behind it, on
    /// the cover title and the back-cover address as much as over the panel.
    /// </summary>
    public float TextOutlineWidthFactor { get; set; } = 0.09f;

    /// <summary>
    /// How many offset copies of a glyph make up its rim — the number of directions on the circle.
    ///
    /// Sixteen, up from the eight this started at, and the reason is arithmetic rather than taste.
    /// QuestPDF has no glyph stroke, so the rim is the text drawn again and again around a circle of
    /// radius r. The result is not a circle: between two neighbouring directions the rim's outer edge
    /// falls short of one by <c>r·(1 − cos(π/steps))</c> — 7.6% of the radius at eight directions,
    /// 1.9% at sixteen — and it falls short exactly on the diagonals, where a round Georgian letter
    /// is at its widest curvature.
    ///
    /// At the old hairline that flat spot was 0.6 pt × 7.6% = 0.05 pt, which no press can resolve. At
    /// this rim it would be 1.8 pt × 7.6% = 0.14 pt, or well over half a pixel at 300 PPI, and a
    /// rim that reads as an octagon on every letter is not the border the ruling asked for. Sixteen
    /// puts it back to 0.03 pt, under a tenth of a press pixel.
    ///
    /// The cost is honest and recorded: every outlined line is that many more vector text runs in
    /// the file, and a text extractor reads it that many times. The supplier's preflight requires
    /// real vector glyphs rather than a raster of them, which is the trade being made.
    /// </summary>
    public int TextOutlineSteps { get; set; } = 16;

    /// <summary>
    /// How much of an illustration a centred crop to the sheet may remove, per axis, before the
    /// book stops.
    ///
    /// The handoff allows "a tiny centered crop … only to normalize to 15:7" and forbids stretching.
    /// The bled sheet is exactly 15:7, so artwork that arrived normalized loses nothing at all and
    /// never comes near this. Four per cent is the line between a rounding difference and a
    /// recomposition: a raw 3:2 render loses three tenths of its height to this crop, which is not a
    /// normalization, and the whole point of the number is that taking it has to be a decision
    /// somebody records here rather than something the composer does quietly.
    /// </summary>
    public float PrintCropTolerance { get; set; } = 0.04f;

    /// <summary>Printed under the continuation QR on Story spread 8.</summary>
    public string ContinuationQrCaption { get; set; } = "ამბავი გრძელდება";

    /// <summary>Legacy values are read only for migration and are never rendered.</summary>
    [Obsolete("QR moved to Story spread 8 in the 2026-09-04 product configuration.")]
    public string ReviewQrUrl { get; set; } = string.Empty;

    [Obsolete("The final spread is credits-left/pattern-right with no closing CTA.")]
    public string EndingLine { get; set; } = string.Empty;

    [Obsolete("The final spread has no QR caption.")]
    public string EndingQrCaption { get; set; } = string.Empty;

    /// <summary>
    /// The intro spread's dedication.
    ///
    /// <c>{name_dative}</c> is the child's name already in the dative — the case „ეკუთვნის“ governs
    /// — built by <see cref="Services.Story.GeorgianNameSuffix"/> rather than by gluing a suffix on
    /// here. The old default was <c>"…ეკუთვნის {name}-ს"</c>, and it printed „თემო-ს“ in a sold book:
    /// the hyphen belongs to a name written in another alphabet, never to a Georgian one.
    /// <c>{name}</c> is still accepted, uninflected, for a template that wants the plain name.
    /// </summary>
    public string IntroBelongsTemplate { get; set; } = "ეს წიგნი ეკუთვნის {name_dative}";

    /// <summary>The quiet line under the dedication. <c>{age}</c> is the child's age in years.</summary>
    public string IntroAgeTemplate { get; set; } = "{age} წლის";

    /// <summary>
    /// The world the book opens into, named the way the parent chose it. <c>{world}</c> is quoted in
    /// the template itself so an arbitrary place name never has to inflect.
    /// </summary>
    public string IntroThemeTemplate { get; set; } = "„{world}“";

    /// <summary>
    /// The invitation. <c>{name}</c> is the plain name — Georgian addresses a person by the
    /// nominative — and this template must never take a case ending.
    /// </summary>
    public string IntroInviteTemplate { get; set; } =
        "{name}, ერთად გავუყვებით ამ ბილიკს. დროა, დაიწყოს ჩვენი თავგადასავალი!";

    /// <summary>The final line in the fixed left-page credits block.</summary>
    public string CreditsLine { get; set; } = "BEKI · beki.ge";

    /// <summary>
    /// The spec's starting type targets by reader age (§20): generous for the youngest readers,
    /// a step down for the readers who get more words. The composer starts the step-down ladder at
    /// whichever of these the child's age selects.
    /// </summary>
    public float StoryFontSizeAges2To4 { get; set; } = 20f;
    public float StoryFontSizeAges5To8 { get; set; } = 18f;

    /// <summary>
    /// Every type size the story copy is allowed to be reduced to, largest first.
    ///
    /// The age band picks where the ladder starts; each rung below it is a permitted reduction, and
    /// below the last rung there is nothing but <c>TEXT_OVERFLOW</c>. A list rather than a step size,
    /// because "which sizes may this book be set at" is a typographic decision an owner should be
    /// able to read off a config file — and because the previous ladder ended by accepting its own
    /// last rung whether the copy fitted or not, which is how a book printed off the bottom of a page.
    /// </summary>
    public float[] StoryFontSizeLadderPt { get; set; } = [20f, 18f, 16f, 14f];

    /// <summary>
    /// The print-normalization target (§6 Step 8): every interior raster layer is delivered at this
    /// density, which on the 450 × 210 mm sheet is exactly 5315 × 2480 px. Zero disables the step
    /// and embeds the native render.
    /// </summary>
    public int PrintTargetPpi { get; set; } = 300;

    /// <summary>
    /// The JPEG quality of normalized print artwork. JPEG rather than PNG is what keeps a
    /// 300-PPI book tens of megabytes instead of hundreds.
    /// </summary>
    public int PrintAssetJpegQuality { get; set; } = 90;

    /// <summary>
    /// The ceiling, in pixels per inch of finished page, on what the customer's download carries.
    ///
    /// A CEILING, never a target: nothing is ever enlarged to reach it. Audit P2-1 measured the
    /// rejected reading copy at 33,985,705 bytes and asked for "a visually approved sRGB export
    /// around 144–180 PPI"; the approved endpaper and intro artwork are 300-PPI press masters, and
    /// embedding them verbatim in a download rebuilds exactly the file the audit rejected. So a
    /// raster already at or under this density is embedded as it arrived — which is every generated
    /// story spread — and one above it is reduced to it. The press masters themselves are untouched;
    /// P2-1 says so in as many words, and the press path never comes through here.
    ///
    /// Zero disables the reduction and embeds every raster at its own resolution.
    /// </summary>
    public int ScreenTargetPpi { get; set; } = 150;

    /// <summary>The story type size a book starts its step-down ladder at, for this reader's age.</summary>
    internal static float StoryFontSizeFor(int? age, BekiPrintLayoutOptions layout) =>
        age == null ? layout.StoryFontSize : (age <= 4 ? layout.StoryFontSizeAges2To4 : layout.StoryFontSizeAges5To8);
}
