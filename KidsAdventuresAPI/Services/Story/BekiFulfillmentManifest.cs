using AdventurePacks.Api.Services.Story.Prompts;

namespace AdventurePacks.Api.Services.Story;

/// <summary>One spread a previous attempt at this pack already drew, reviewed and stored.</summary>
public sealed record BekiFulfillmentManifestEntry(int SpreadNumber, string StoredUrl);

/// <summary>
/// What a resumed fulfilment job needs to pick up where a dead one left off: which spreads were
/// already accepted and stored, and the rhythm they were drawn against.
///
/// The rhythm snapshot exists because <see cref="BekiSpreadRhythm"/> is code, not data — a
/// deploy landing between two attempts at the same pack could change which side of a spread
/// carries the text, and a manifest written against the old rhythm would hand a resumed run a
/// spread whose text-safe third no longer matches what the composer will typeset it against. A
/// mismatch there is not a fault worth reconciling image by image; the whole manifest is simply
/// ignored and every spread is redrawn against the rhythm in force now.
/// </summary>
public sealed record BekiFulfillmentManifest
{
    public required IReadOnlyList<string> RhythmSnapshot { get; init; }
    public required IReadOnlyList<BekiFulfillmentManifestEntry> Entries { get; init; }

    /// <summary>The rhythm this pack's spreads are drawn against, right now.</summary>
    public static IReadOnlyList<string> CurrentRhythm(int spreadCount) =>
        Enumerable.Range(1, spreadCount).Select(BekiSpreadRhythm.TextSideFor).ToArray();
}
