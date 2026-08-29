using System.Text.Json.Serialization;
using AdventurePacks.Api.Services.Story.Prompts;

namespace AdventurePacks.Api.Services.Story;

/// <summary>One spread a previous attempt at this pack already drew, reviewed and stored.</summary>
public sealed record BekiFulfillmentManifestEntry(int SpreadNumber, string StoredUrl);

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
/// Five terms, and each one is a way a page can silently stop matching the rest of its book. The
/// pose registry names the nine approved PNGs and their hashes — a revised registry is different
/// artwork. The pipeline config carries the anchors, so a revision moves Beki on the page. The
/// story prompt version decides the words the pictures were planned from. The image template's
/// version decides what every image call was told.
///
/// And the fifth is the theme reference's own SHA-256, which the four versions above would miss.
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
    string ThemeId,
    string ThemeReferenceSha256)
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
            themeId,
            Composite.CompositeThemeReferences.RegisteredSha256(themeId));
    }

    public override string ToString() => string.Join(
        '|',
        "composite",
        PoseRegistryVersion,
        PipelineConfigVersion,
        StoryPromptVersion,
        ImagePromptVersion,
        ThemeId,
        ThemeReferenceSha256);
}
