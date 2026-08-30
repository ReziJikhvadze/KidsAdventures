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
    /// The stroke drawn around every glyph of the cover title and the Continue Adventure line, in
    /// points. Those two set light type straight onto artwork, where the picture can win against
    /// the words; the story text does not come through here any more — it is dark type on a cream
    /// wash, which needs no rim. Zero turns it off.
    /// </summary>
    public float TextOutlineWidth { get; set; } = 0.6f;

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

    /// <summary>
    /// Where spread 8's Continue Adventure QR sends the reader. Used to be the closing page's own
    /// code as well, back when one URL did both jobs; <see cref="ReviewQrUrl"/> is the one that
    /// took over the closing page, so each code can be repointed without disturbing the other.
    /// </summary>
    public string EndingQrUrl { get; set; } = "https://beki.ge";

    /// <summary>
    /// Where the closing page's rate-us QR sends the reader — see <see cref="EndingQrUrl"/> for
    /// the sibling that stayed behind on spread 8.
    /// </summary>
    public string ReviewQrUrl { get; set; } = "https://beki.ge";

    /// <summary>The credits page's sign-off line. Reusable across every order.</summary>
    public string EndingLine { get; set; } = "ამბავი აქ მთავრდება — თავგადასავალი კი გრძელდება.";

    /// <summary>Printed under the QR code, saying what scanning it is for.</summary>
    public string EndingQrCaption { get; set; } = "შეაფასე ბეკის წიგნი";

    /// <summary>
    /// The short line beside spread 8's Continue Adventure QR, so the code reads as an invitation
    /// rather than a bare square the reader has to guess the purpose of.
    /// </summary>
    public string ContinueCtaText { get; set; } = "განაგრძე თავგადასავალი ბეკისთან";

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

    /// <summary>The credits page's own line, under the review QR.</summary>
    public string CreditsLine { get; set; } = "Beki • beki.ge";

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

    /// <summary>The story type size a book starts its step-down ladder at, for this reader's age.</summary>
    internal static float StoryFontSizeFor(int? age, BekiPrintLayoutOptions layout) =>
        age == null ? layout.StoryFontSize : (age <= 4 ? layout.StoryFontSizeAges2To4 : layout.StoryFontSizeAges5To8);
}
