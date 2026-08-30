using System.Text.Json.Serialization;
using AdventurePacks.Api.Services.Story.Prompts;

namespace AdventurePacks.Api.Services.Story;

/// <summary>One spread a previous attempt at this pack already drew, reviewed and stored.</summary>
public sealed record BekiFulfillmentManifestEntry(int SpreadNumber, string StoredUrl);

/// <summary>
/// The cover as it was shipped: where it is stored, which prompt drew it, and the review verdict.
/// </summary>
/// <param name="PromptVersion">
/// <see cref="Composite.CompositeIllustrationPrompt.CoverRedrawVersion"/> for a cover this job drew
/// against the book's own first spread, and <see cref="AdoptedPreviewCover"/> for the previewed one
/// kept because the redraw was refused or could not run.
/// </param>
/// <param name="Verdict">
/// The minimal QA's one-line verdict for a redrawn cover. Null for an adopted one, which is
/// honest rather than tidy: nobody reviewed it, and an empty verdict must not read as a pass.
/// </param>
public sealed record BekiCoverRecord(string StoredUrl, string PromptVersion, string? Verdict)
{
    /// <summary>What the prompt version says when the cover is the one the parent previewed.</summary>
    public const string AdoptedPreviewCover = "adopted-preview-cover";

    /// <summary>
    /// Whether this cover was drawn against the book's own first spread and reviewed.
    ///
    /// Two things turn on it and both are about agreement. The reader's cover is re-pointed at the
    /// pack's stored blob only for a redraw — an adopted cover already IS the preview run's cover,
    /// so pointing at a copy would change nothing. And a resumed run that drew no cover of its own
    /// keeps a stored redraw rather than overwriting it with the previewed picture.
    /// </summary>
    public bool IsRedraw => PromptVersion.StartsWith(
        Composite.CompositeIllustrationPrompt.CoverRedrawVersionPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Whether that redraw was made by the cover prompt this deployment sends.
    ///
    /// The narrower of the two questions, and the one the skip guard asks: a cover drawn before the
    /// entered-age steer landed is a redraw — the reader should keep pointing at it — but it is not
    /// today's cover, so a resumed book is allowed to buy the better one once.
    /// </summary>
    public bool IsCurrentRedraw => string.Equals(
        PromptVersion, Composite.CompositeIllustrationPrompt.CoverRedrawVersion, StringComparison.Ordinal);
}

/// <summary>
/// One page's composition receipt, as a resumed job needs to find it again.
///
/// The pose and the output hash are duplicated out of the stored manifest on purpose. A resumed
/// run adopting a spread has to be able to say what it adopted without fetching and parsing a
/// second document per page, and an operator reading the fulfilment manifest should be able to see
/// that eight different poses were composited without opening eight files.
/// </summary>
/// <param name="StoredUrl">
/// Whatever storage returned for the composition manifest JSON, verbatim. Never a key assembled by
/// hand: the two storage implementations shape their keys differently, and a hand-built key reads
/// in one environment and 404s in the other.
/// </param>
/// <param name="BaseImageUrl">
/// Where this page's pre-composite child/world image is stored.
///
/// Kept because a resumed run needs it and cannot reconstruct it. It is the continuity reference a
/// later spread reusing the same creature is shown; the composited page is not a substitute for it,
/// because the composite has Beki pasted onto it and the one image this pipeline never sends to an
/// image model is a picture of Beki. Null on an entry written before base images were stored, which
/// a resumed run reports as a continuity gap rather than papering over.
/// </param>
public sealed record BekiCompositionManifestEntry(
    int SpreadNumber, string StoredUrl, string PoseId, string OutputSha256, string? BaseImageUrl);

/// <summary>
/// What a resumed fulfilment job needs to pick up where a dead one left off: which spreads were
/// already accepted and stored, and the terms they were drawn under.
///
/// Those terms are code, not data. <see cref="BekiSpreadRhythm"/> decides which side of a spread
/// carries the text and how the scene is shot; <see cref="BekiIdentity"/> decides who Beki is. All
/// three live in the binary, and a deploy landing between two attempts at the same pack can change
/// any of them. A manifest written before such a deploy hands a resumed run pictures drawn under
/// rules the rest of the book will not be drawn under: a spread whose text-safe third is now on
/// the other leaf, a close-up where the rhythm now calls for a wide establishing shot, or — worst,
/// because it is the one a parent notices — a Beki from the retired lamb design sharing a book
/// with the leaf spirit.
///
/// So the snapshot is the whole illustration contract per spread rather than the text side alone,
/// and any mismatch on resume is handled the way it always was: the manifest is ignored outright
/// and every spread is redrawn against the rules in force now. Reconciling image by image would be
/// cheaper and would produce exactly the mixed book this exists to prevent.
/// </summary>
public sealed record BekiFulfillmentManifest
{
    /// <summary>
    /// One line per spread, in spread order. Opaque on purpose — nothing reads the parts back out,
    /// it is only ever compared whole against <see cref="CurrentContract"/>, and a format nobody
    /// parses is a format that can gain a term without anyone having to update a reader.
    ///
    /// A manifest written before this property existed simply fails to deserialize — the property
    /// is required — which lands on the same behaviour as a mismatch: no manifest, redraw
    /// everything. That is the correct answer for those manifests anyway, since they were written
    /// under rules that had not yet been pinned down.
    /// </summary>
    public required IReadOnlyList<string> IllustrationContract { get; init; }

    public required IReadOnlyList<BekiFulfillmentManifestEntry> Entries { get; init; }

    /*
      Everything below belongs to the composite pipeline and is written only by it.

      Nullable, and omitted from the JSON when null, which is not tidiness — it is what keeps a
      legacy manifest byte-identical to the ones written before these fields existed. The legacy
      path is meant to be untouched by this campaign, and a manifest that gained two properties
      would be a change to an artifact it produces.

      Not `required`, for the reason the two above are: a required property added here would make
      every manifest already sitting in storage fail to deserialize, and a book half drawn under
      the old shape would be redrawn from nothing rather than resumed.
    */

    /// <summary>
    /// Where the validated Visual Scenario for this book is stored.
    ///
    /// It is on the manifest because it is the one document a resumed run cannot regenerate: the
    /// scenario is a paid model call, its output decides the outfit and the recurring elements
    /// every remaining page must match, and planning a second one halfway through a book would
    /// dress the child differently on spreads five to eight.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScenarioUrl { get; init; }

    /// <summary>
    /// One composition receipt per page this run composited, in spread order.
    ///
    /// The receipts are what let a reprint prove the character on the page was the approved PNG —
    /// pose id, hash, box, size, anchor, output hash — so losing them to a job that died and
    /// resumed would lose the evidence for pages that were never redrawn.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BekiCompositionManifestEntry>? Compositions { get; init; }

    /// <summary>
    /// Where this book's derived child identity spec is stored.
    ///
    /// It is on the manifest for the reason the scenario is: a resumed run must draw its remaining
    /// spreads to the same description of the child as the ones it adopts. The four attributes go
    /// into every image prompt, so a second derivation — same photograph, same model, "wavy" where
    /// the first said "curly" — would give the redrawn half of a book a different child from the
    /// adopted half, with every page passing its own review on the way.
    ///
    /// The URL, never the attributes. The spec describes a real child's body and belongs in the
    /// pack's own private storage beside the photograph it was read from; this manifest is an
    /// operational document that gets read, logged and pasted into support threads.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IdentitySpecUrl { get; init; }

    /// <summary>
    /// The cover this pack actually shipped: where it is, what drew it, and what review made of it.
    ///
    /// On the record because the cover stopped being an inherited artifact. It used to be whatever
    /// the preview drew, adopted without being looked at again; it is now redrawn against the
    /// book's own first spread and reviewed against the child's identity spec — or, when that
    /// redraw is refused, deliberately still the previewed one. Those are three different
    /// provenances for the same file, and an operator holding a cover a parent is unhappy with
    /// needs to know which of them they are looking at.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BekiCoverRecord? Cover { get; init; }

    /// <summary>
    /// Where this book's composite review is stored: the pose-fallback count, the Georgian
    /// check-list's flags and the reviewer's advisory shot notes.
    ///
    /// On the manifest because it is the one artifact that says what is *wrong* with a book that
    /// nevertheless shipped. Everything else recorded here is provenance — which picture, which
    /// prompt, which pose — and a completed pack looks identical whether its guide hovers neutrally
    /// on six spreads or acts on all eight, whether its Georgian carries a misspelling that reached
    /// print, and whether the reviewer thought half the book was shot wrongly. Those are the
    /// questions the supplier's handback package and the admin actually arrive with, and until now
    /// the only answer was to grep a log.
    ///
    /// The URL, never the content, exactly as <see cref="IdentitySpecUrl"/> is. The review quotes
    /// short windows of the book's Georgian to show what a check-list rule matched, and the child's
    /// name is in that prose — the hyphenated-suffix rule finds the name with a suffix stuck on it.
    /// This manifest is read, logged and pasted into support threads; the words belong in the pack's
    /// private folder and are deleted with it.
    ///
    /// Written once, with the finished book, unlike the two URLs above: the review is a fact about a
    /// whole book — a count across eight spreads — so there is nothing true to write down until all
    /// eight exist. Nullable and omitted when null, so a legacy manifest stays byte-identical and a
    /// manifest written before this field existed still deserializes and still resumes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReviewUrl { get; init; }

    /// <summary>
    /// The terms this pack's spreads would be drawn under, right now.
    /// </summary>
    /// <param name="composite">
    /// The composite pipeline's own identity, when it is the pipeline drawing this book; null when
    /// the previous path is.
    ///
    /// Which pipeline drew a page is the most important term in the whole contract and was missing
    /// from it. The two produce pages that are incompatible in the way that matters most: the
    /// previous path asks an image model to draw Beki, the composite path pastes an approved PNG.
    /// A flag flipped between two attempts at the same pack would therefore have adopted
    /// AI-invented Beki pages into a composite book — eight pages, two different characters, each
    /// page individually fine — and the contract, which knew only about the text side, the shot and
    /// the Beki asset version, would have said they matched.
    ///
    /// Adding the term when the composite pipeline is on and leaving the array untouched when it is
    /// off is deliberate: a manifest written by the previous path still matches the previous path's
    /// contract exactly, so no in-flight legacy book is invalidated by this change, while a flip in
    /// either direction is a mismatch and redraws.
    /// </param>
    public static IReadOnlyList<string> CurrentContract(
        int spreadCount, BekiCompositeContractTerms? composite = null)
    {
        var spreads = Enumerable.Range(1, spreadCount).Select(ContractFor);

        return composite is null
            ? spreads.ToArray()
            : spreads.Prepend(composite.ToString()).ToArray();
    }

    /// <summary>
    /// Text side, shot and Beki's version, joined by a character none of the three contains. The
    /// shot goes in verbatim rather than as an index: the rhythm's wording is what reaches the
    /// image model, so a reworded shot is a differently drawn spread even when its position in the
    /// table did not move.
    /// </summary>
    private static string ContractFor(int spreadNumber) => string.Join(
        '|',
        BekiSpreadRhythm.TextSideFor(spreadNumber),
        BekiSpreadRhythm.ShotFor(spreadNumber),
        BekiIdentity.Version);
}

/// <summary>
/// Everything about the composite pipeline that decides what a page looks like, as one line of the
/// resume contract.
///
/// Six terms, and each one is a way a page can silently stop matching the rest of its book. The
/// pose registry names the nine approved PNGs and their hashes — a revised registry is different
/// artwork. The pipeline config carries the anchors, so a revision moves Beki on the page. The
/// story prompt version decides the words the pictures were planned from. The image template's
/// version decides what every image call was told.
///
/// The identity derivation prompt's version is the newest of them, and it earns its place the same
/// way. The four attributes it produces — hair, eyes, skin — are written into every image prompt
/// and compared by every review, so a revised derivation prompt describes the child differently,
/// and a run that adopted pages drawn to the old description while drawing the rest to the new one
/// would produce two children in one book with every page passing its own review. A version change
/// redraws instead, which is the whole point of this line.
///
/// The pose registry's KEYWORD REVISION is a term of its own beside its version, and it has to be.
/// A keyword amendment deliberately does not move <c>registry_version</c> — no pixel, hash, priority
/// order or forced pose changes, and <c>pipeline_config_v1.json</c> pins that string — so the
/// version alone cannot see it. What a keyword revision does change is which approved pose a
/// sentence selects: under v1.0 "Beki claps happily" selected the neutral hover, and under v1.1 it
/// selects the celebrate pose. A resumed run that adopted pages composited under the old table while
/// compositing the rest under the new one would bind one book from two different readings of the
/// same scenario, every page individually correct, and the review would then count fallbacks against
/// a table half the book was never selected by. The alternative considered — recovering each adopted
/// page's pose from its stored composition manifest and auditing from those — is strictly more code
/// for a worse answer: it would let the mixed book ship and merely describe it accurately. Pinning
/// redraws instead, which is what every other version key here does.
///
/// And the last is the theme reference's own SHA-256, which the five versions above would miss.
/// Every picture in a composite book is generated against one approved world PNG, and that file
/// can be re-art-directed without the registry's version string moving — a lighter palette, a
/// redrawn skyline. A resumed run would then adopt spreads drawn from the old world and draw the
/// rest from the new one: two visual worlds bound into one book, every page individually fine and
/// passing its own review. The hash is the only term that catches that, which is why it is the
/// file's hash and not the registry's version.
///
/// Opaque, like the per-spread lines it sits beside: nothing reads the parts back out, it is only
/// ever compared whole, and a format nobody parses is a format that can gain a term without anyone
/// having to update a reader.
/// </summary>
public sealed record BekiCompositeContractTerms(
    string PoseRegistryVersion,
    string PipelineConfigVersion,
    string StoryPromptVersion,
    string ImagePromptVersion,
    string IdentityPromptVersion,
    string ThemeId,
    string ThemeReferenceSha256,
    string PoseKeywordRevision)
{
    /// <summary>
    /// The terms as they stand in this deployment, for the world this particular book is set in.
    ///
    /// Called only when the composite flag is on, because it reads the pipeline config and the
    /// theme registry — and a deployment running the previous path may not have the composite
    /// assets installed at all.
    /// </summary>
    /// <param name="themeId">
    /// The canonical theme id this book is drawn against. The hash is per world, so the contract
    /// has to be built for the world rather than for the deployment.
    /// </param>
    public static BekiCompositeContractTerms Current(string themeId)
    {
        var config = Composite.Poses.BekiCompositeConfig.Load();

        return new BekiCompositeContractTerms(
            config.PoseRegistryVersion,
            config.ConfigVersion,
            Composite.MasterStoryPromptComposite.Version,
            Composite.CompositeIllustrationPrompt.Version,
            Composite.CompositeChildIdentity.Version,
            themeId,
            Composite.CompositeThemeReferences.RegisteredSha256(themeId),
            // Read from the installed registry rather than from the config, because the config
            // pins the pack revision and this is deliberately not part of it. Loading here is one
            // JSON read per job, beside the two this method already does.
            Composite.Poses.BekiPoseRegistry.Load().KeywordRevision);
    }

    public override string ToString() => string.Join(
        '|',
        "composite",
        PoseRegistryVersion,
        PipelineConfigVersion,
        StoryPromptVersion,
        ImagePromptVersion,
        IdentityPromptVersion,
        ThemeId,
        ThemeReferenceSha256,
        PoseKeywordRevision);
}
