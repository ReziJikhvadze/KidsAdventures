using System.Text.Json.Serialization;
using AdventurePacks.Api.Services.Story.Prompts;

namespace AdventurePacks.Api.Services.Story;

/// <summary>One spread a previous attempt at this pack already drew, reviewed and stored.</summary>
public sealed record BekiFulfillmentManifestEntry(int SpreadNumber, string StoredUrl);

/// <summary>
/// A dependency that must be identified and must not travel: the child's photograph and the four
/// identity attributes read from it (amendment A7).
/// </summary>
/// <param name="Reference">The immutable blob reference, inside the pack's private prefix.</param>
/// <param name="Sha256">The hash of the bytes at that reference, so a claim can be checked.</param>
/// <param name="Bytes">The size, which says a file was actually read rather than assumed.</param>
public sealed record BekiPrivateArtifactReference(string Reference, string Sha256, long Bytes);

/// <summary>
/// The three fields the audit-2 correction added to the manifest, carried together so that the
/// writer keeps one optional parameter rather than four.
/// </summary>
public sealed record BekiManifestPrivateRefs(
    string? StoryUrl = null,
    BekiPrivateArtifactReference? ChildPhotograph = null,
    BekiPrivateArtifactReference? ChildIdentity = null);

/// <summary>
/// The cover as it was shipped: where the master is stored, what produced it, and its verdict.
///
/// Rewritten by audit-2 P0-01. This record used to say which of two AI redraw prompts had drawn the
/// customer's front page — a question that only makes sense in a world with two cover producers,
/// which is the world the supplier rejected. A composite book now has exactly one cover master, the
/// composited 512 × 245 wrap, and every cover a human ever sees is a crop or a typeset derivation of
/// it. So the record names that master: the approved pose composited onto it, the hash of the exact
/// bytes every derivation was cut from, and the anchor the pose was placed at.
/// </summary>
/// <param name="PromptVersion">
/// <see cref="WrapMaster"/> for a composite book's canonical wrap — the only value a book drawn
/// since the audit-2 correction writes. <see cref="AdoptedPreviewCover"/> and the redraw versions
/// appear only on manifests written before it, and are read so those books can still be resumed and
/// explained.
/// </param>
/// <param name="Verdict">
/// The one-line verdict the master shipped under. Null when nobody reviewed it, which is honest
/// rather than tidy: an empty verdict must not read as a pass.
/// </param>
public sealed record BekiCoverRecord(string StoredUrl, string PromptVersion, string? Verdict)
{
    /// <summary>What the prompt version says when the cover is the one the parent previewed.</summary>
    public const string AdoptedPreviewCover = "adopted-preview-cover";

    /// <summary>
    /// What it says for the single cover master: the composited wrap, from which the press cover,
    /// the customer's front and back pages and the reader's image are all derived.
    /// </summary>
    public const string WrapMaster = "cover-wrap-master-v1";

    /// <summary>
    /// Whether this book has one cover master — the gate <c>SINGLE_COVER_MASTER</c> asks exactly
    /// this, and a record still naming a redraw version is a book with two cover designs in it.
    /// </summary>
    public bool IsWrapMaster => string.Equals(PromptVersion, WrapMaster, StringComparison.Ordinal);

    /// <summary>The approved pose composited onto the front board. Null on a pre-correction record.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PoseId { get; init; }

    /// <summary>
    /// The SHA-256 of the composited wrap, recomputed from the stored bytes and compared with the
    /// composition receipt before a single derivation is cut (audit P0-10).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompositeSha256 { get; init; }

    /// <summary>Where on the front board the pose was placed, as the receipt states it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Anchor { get; init; }

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
    /// <summary>A deliberately shortened sample, never a complete printable book.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TestingFlow { get; init; }

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
    /// Where the normalized Story JSON this book was drawn from is stored.
    ///
    /// On the manifest because audit §9 moved it out of the handback's excluded list. The supplier
    /// asked for the normalized story and got "it lives on the master-story run record" — an answer
    /// about our schema, not about their package. The words the pictures were planned from are a
    /// book artifact, so the book carries them.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StoryUrl { get; init; }

    /// <summary>
    /// The child's photograph, as a reference and a hash and never as bytes — amendment A7.
    ///
    /// The handback has to be able to say which photograph a book was drawn from: a reprint that
    /// cannot name its input cannot be shown to be the same book. What it must never do is carry the
    /// photograph. So the manifest states the immutable blob reference and the SHA-256 of the bytes
    /// at that reference, and anybody with the authority to open the pack's private prefix can check
    /// one against the other.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BekiPrivateArtifactReference? ChildPhotograph { get; init; }

    /// <summary><inheritdoc cref="ChildPhotograph"/></summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BekiPrivateArtifactReference? ChildIdentity { get; init; }

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
    /// Whether artwork stored under <paramref name="storedContract"/> may be re-composited and
    /// re-exported as it stands, without drawing anything again.
    ///
    /// A narrower question than the one the resume contract asks, and it has to be. Resuming a
    /// half-drawn book DRAWS its remaining spreads, so every term that changes how a picture is
    /// made is decisive there: a prompt version, an anchor table, a keyword revision. Print
    /// re-preparation and stored-art recovery draw nothing at all. They re-composite bases that are
    /// already on disk and hash-verified with the same approved pose PNG the composition receipt
    /// names, and lay the result out again. A deployment that revised its image prompt cannot
    /// retroactively change the pixels those bases contain, so refusing the operation over it locks
    /// a finished book out of its own press files for a reason that is not about the book.
    ///
    /// What still decides is what identifies the ARTWORK rather than the recipe: the pose registry,
    /// because it names the nine approved PNGs and their hashes and a revised registry is a
    /// different character; and the world — its canonical id and the SHA-256 of its approved
    /// reference — because the reference is only ever read while drawing, but it is the thing the
    /// stored bases are pictures of, and a book whose world was re-art-directed is a book whose
    /// stored pages belong to a world this deployment no longer has.
    /// </summary>
    /// <param name="drift">
    /// What differs and was tolerated, as one short human-readable line, or null when the two
    /// contracts are identical. On a refusal it carries the difference that caused it instead, so
    /// the operator's message can say which one it was.
    /// </param>
    public static bool StoredArtworkIsReusable(
        IReadOnlyList<string> storedContract, IReadOnlyList<string> currentContract, out string? drift)
    {
        drift = null;

        if (storedContract.Count != currentContract.Count)
        {
            drift = $"the stored contract has {storedContract.Count} lines where this book has "
                + $"{currentContract.Count}";
            return false;
        }

        if (storedContract.Count == 0
            || !BekiCompositeContractTerms.TryParse(storedContract[0], out var stored)
            || !BekiCompositeContractTerms.TryParse(currentContract[0], out var current))
        {
            drift = "the stored contract does not begin with composite pipeline terms";
            return false;
        }

        if (!string.Equals(stored.PoseRegistryVersion, current.PoseRegistryVersion, StringComparison.Ordinal))
        {
            drift = $"pose registry {stored.PoseRegistryVersion} → {current.PoseRegistryVersion}";
            return false;
        }

        if (!string.Equals(stored.ThemeId, current.ThemeId, StringComparison.Ordinal))
        {
            drift = $"world {stored.ThemeId} → {current.ThemeId}";
            return false;
        }

        if (!string.Equals(stored.ThemeReferenceSha256, current.ThemeReferenceSha256, StringComparison.Ordinal))
        {
            drift = $"world reference {ShortHash(stored.ThemeReferenceSha256)} → "
                + ShortHash(current.ThemeReferenceSha256);
            return false;
        }

        var differences = new List<string>();
        Note(differences, "pipeline config", stored.PipelineConfigVersion, current.PipelineConfigVersion);
        Note(differences, "story prompt", stored.StoryPromptVersion, current.StoryPromptVersion);
        Note(differences, "image prompt", stored.ImagePromptVersion, current.ImagePromptVersion);
        Note(differences, "identity prompt", stored.IdentityPromptVersion, current.IdentityPromptVersion);
        Note(differences, "pose keywords", stored.PoseKeywordRevision, current.PoseKeywordRevision);

        var shots = Enumerable.Range(1, storedContract.Count - 1).Count(
            line => !string.Equals(storedContract[line], currentContract[line], StringComparison.Ordinal));
        if (shots > 0)
            differences.Add($"{shots} spread line{(shots == 1 ? string.Empty : "s")} differ");

        drift = differences.Count == 0 ? null : string.Join("; ", differences);
        return true;

        static void Note(List<string> into, string term, string stored, string current)
        {
            if (!string.Equals(stored, current, StringComparison.Ordinal))
                into.Add($"{term} {stored} → {current}");
        }

        static string ShortHash(string sha) => sha.Length <= 12 ? sha : sha[..12];
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
/// Compared whole by the resume contract, which is the only comparison that decides whether
/// anything is DRAWN. The stored-art paths, which draw nothing, read the terms back out through
/// <see cref="TryParse"/> to ask the narrower question in
/// <see cref="BekiFulfillmentManifest.StoredArtworkIsReusable"/> — so a new term added here is
/// still safe by default: an unknown field changes the line, the resume contract stops matching and
/// redraws, and the reader below refuses a line whose shape it does not recognise.
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
    public static BekiCompositeContractTerms Current(string themeId, bool insertBekiInGeneration = false)
    {
        var config = Composite.Poses.BekiCompositeConfig.Load();

        return new BekiCompositeContractTerms(
            config.PoseRegistryVersion,
            config.ConfigVersion,
            Composite.MasterStoryPromptComposite.Version,
            insertBekiInGeneration ? Composite.Poses.BekiGeneratedArtwork.Version : Composite.CompositeIllustrationPrompt.Version,
            Composite.CompositeChildIdentity.Version,
            themeId,
            Composite.CompositeThemeReferences.RegisteredSha256(themeId),
            // Read from the installed registry rather than from the config, because the config
            // pins the pack revision and this is deliberately not part of it. Loading here is one
            // JSON read per job, beside the two this method already does.
            Composite.Poses.BekiPoseRegistry.Load().KeywordRevision);
    }

    /// <summary>
    /// Reads one contract line back into its terms, or refuses it.
    ///
    /// Refuses rather than guesses: a line that is not a composite line, or that carries a
    /// different number of fields from the one <see cref="ToString"/> writes, is a line this
    /// deployment cannot claim to understand — and the callers of this method are deciding whether
    /// a finished book may go to press, which is not a decision to make from a half-read record.
    /// </summary>
    public static bool TryParse(
        string? line, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BekiCompositeContractTerms? terms)
    {
        terms = null;
        if (string.IsNullOrEmpty(line)) return false;

        var fields = line.Split('|');
        if (fields.Length != 9 || !string.Equals(fields[0], Marker, StringComparison.Ordinal)) return false;

        terms = new BekiCompositeContractTerms(
            fields[1], fields[2], fields[3], fields[4], fields[5], fields[6], fields[7], fields[8]);
        return true;
    }

    /// <summary>What says this line is the composite pipeline's, rather than a spread's.</summary>
    private const string Marker = "composite";

    public override string ToString() => string.Join(
        '|',
        Marker,
        PoseRegistryVersion,
        PipelineConfigVersion,
        StoryPromptVersion,
        ImagePromptVersion,
        IdentityPromptVersion,
        ThemeId,
        ThemeReferenceSha256,
        PoseKeywordRevision);
}
