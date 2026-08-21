using AdventurePacks.Api.Services.Story.Prompts;

namespace AdventurePacks.Api.Services.Story;

/// <summary>One spread a previous attempt at this pack already drew, reviewed and stored.</summary>
public sealed record BekiFulfillmentManifestEntry(int SpreadNumber, string StoredUrl);

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

    /// <summary>The terms this pack's spreads would be drawn under, right now.</summary>
    public static IReadOnlyList<string> CurrentContract(int spreadCount) =>
        Enumerable.Range(1, spreadCount).Select(ContractFor).ToArray();

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
