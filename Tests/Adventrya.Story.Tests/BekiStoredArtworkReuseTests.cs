using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The narrower contract question the stored-art stages ask: may these pictures be laid out again
/// as they are?
///
/// It exists because the resume contract answers a different question and was being asked this one.
/// Resuming a half-drawn book draws its remaining spreads, so every term that decides how a picture
/// is made is decisive there. Print re-preparation and stored-art recovery draw nothing — they
/// re-composite hash-verified bases with the pose the receipt names and export the PDF again — so a
/// deployment that bumped its pipeline config between the book being finished and the printer's
/// files being wanted was locking a finished book out of print over a version string that cannot
/// reach a single pixel of it. That was the state of book 54cba4b3 in the deployment:
/// <c>beki-pipeline-v2.0</c> stored, <c>beki-pipeline-v2.1</c> current, and every other term in the
/// nine-line contract identical.
///
/// What still refuses is what identifies the artwork rather than the recipe: the pose registry, and
/// the world the bases are pictures of.
/// </summary>
public class BekiStoredArtworkReuseTests
{
    /// <summary>
    /// A book drawn under exactly this deployment's terms. Reusable, and there is nothing to say
    /// about it — which is what a null drift means, as distinct from an empty string.
    /// </summary>
    [Fact]
    public void An_identical_contract_is_reusable_with_no_drift()
    {
        var contract = Contract(Terms());

        Assert.True(BekiFulfillmentManifest.StoredArtworkIsReusable(contract, Contract(Terms()), out var drift));
        Assert.Null(drift);
    }

    /// <summary>
    /// The deployment's actual case. The config version moved; the artwork did not, because the
    /// artwork was drawn months ago and a version string is not a paintbrush.
    /// </summary>
    [Fact]
    public void A_pipeline_config_bump_alone_is_reusable_and_names_both_versions()
    {
        var stored = Contract(Terms() with { PipelineConfigVersion = "beki-pipeline-v2.0" });
        var current = Contract(Terms() with { PipelineConfigVersion = "beki-pipeline-v2.1" });

        Assert.True(BekiFulfillmentManifest.StoredArtworkIsReusable(stored, current, out var drift));

        // Both versions, so a reader of press-status.json can tell which way it moved without
        // going to look up what this deployment happens to be running today.
        Assert.NotNull(drift);
        Assert.Contains("pipeline config", drift, StringComparison.Ordinal);
        Assert.Contains("beki-pipeline-v2.0", drift, StringComparison.Ordinal);
        Assert.Contains("beki-pipeline-v2.1", drift, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every term that is about the recipe rather than the artwork, moved at once, and still
    /// reusable — with all of them written down.
    ///
    /// Asserted together because the individual permissions are not the point: the point is that
    /// the tolerated set is exactly "things that decide how a picture would be drawn", and a term
    /// quietly added to the refusing side later would show up here.
    /// </summary>
    [Fact]
    public void The_prompt_versions_and_the_keyword_revision_are_all_tolerated_and_all_reported()
    {
        var stored = Contract(Terms());
        var current = Contract(Terms() with
        {
            PipelineConfigVersion = "config-2",
            StoryPromptVersion = "story-2",
            ImagePromptVersion = "image-2",
            IdentityPromptVersion = "identity-2",
            PoseKeywordRevision = "keywords-2",
        });

        Assert.True(BekiFulfillmentManifest.StoredArtworkIsReusable(stored, current, out var drift));
        Assert.NotNull(drift);

        foreach (var term in new[] { "pipeline config", "story prompt", "image prompt", "identity prompt", "pose keywords" })
            Assert.Contains(term, drift, StringComparison.Ordinal);
    }

    /// <summary>
    /// A revised pose registry names different approved PNGs with different hashes, so the
    /// character on the stored page is not the character this deployment would composite. Refused,
    /// and the refusal says which registries.
    /// </summary>
    [Fact]
    public void A_different_pose_registry_is_not_reusable()
    {
        var stored = Contract(Terms() with { PoseRegistryVersion = "poses-1" });
        var current = Contract(Terms() with { PoseRegistryVersion = "poses-2" });

        Assert.False(BekiFulfillmentManifest.StoredArtworkIsReusable(stored, current, out var drift));
        Assert.NotNull(drift);
        Assert.Contains("pose registry", drift, StringComparison.Ordinal);
        Assert.Contains("poses-1", drift, StringComparison.Ordinal);
        Assert.Contains("poses-2", drift, StringComparison.Ordinal);
    }

    /// <summary>The world's id: a book of dinosaurs is not a book of the ocean.</summary>
    [Fact]
    public void A_different_world_is_not_reusable()
    {
        var stored = Contract(Terms() with { ThemeId = "dinosaurs" });
        var current = Contract(Terms() with { ThemeId = "ocean" });

        Assert.False(BekiFulfillmentManifest.StoredArtworkIsReusable(stored, current, out var drift));
        Assert.Contains("world", drift!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The world's approved reference, re-art-directed under the same id and the same registry
    /// version — the one difference no version string catches, and the reason the hash is a term.
    /// </summary>
    [Fact]
    public void A_re_art_directed_world_reference_is_not_reusable()
    {
        var stored = Contract(Terms() with { ThemeReferenceSha256 = new string('a', 64) });
        var current = Contract(Terms() with { ThemeReferenceSha256 = new string('b', 64) });

        Assert.False(BekiFulfillmentManifest.StoredArtworkIsReusable(stored, current, out var drift));
        Assert.Contains("world reference", drift!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A shot the rhythm table now words differently is a spread that would be DRAWN differently,
    /// and nothing here is drawn. Tolerated, and counted.
    /// </summary>
    [Fact]
    public void A_reworded_spread_line_is_tolerated_and_counted()
    {
        var stored = Contract(Terms());
        var current = Contract(Terms()).ToArray();
        current[3] = "left|a shot nobody wrote|beki-9";

        Assert.True(BekiFulfillmentManifest.StoredArtworkIsReusable(stored, current, out var drift));
        Assert.Contains("1 spread line differ", drift!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A contract of a different length is a book of a different shape — or a manifest from before
    /// the composite line existed at all, which is a legacy book and not this stage's business.
    /// </summary>
    [Fact]
    public void A_contract_of_a_different_length_is_not_reusable()
    {
        var stored = Contract(Terms()).Take(6).ToArray();

        Assert.False(BekiFulfillmentManifest.StoredArtworkIsReusable(stored, Contract(Terms()), out var drift));
        Assert.Contains("lines", drift!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A first line this deployment cannot read is refused rather than guessed at. Both shapes are
    /// asked: a line that is not a composite line at all, and a composite line carrying a field
    /// count nothing here writes — which is what a term added to the contract would look like to an
    /// older binary.
    /// </summary>
    [Theory]
    [InlineData("left|wide establishing shot|beki-9")]
    [InlineData("composite|only|three|fields")]
    [InlineData("")]
    public void A_first_line_that_is_not_composite_terms_is_not_reusable(string firstLine)
    {
        var stored = Contract(Terms()).ToArray();
        stored[0] = firstLine;

        Assert.False(BekiFulfillmentManifest.StoredArtworkIsReusable(stored, Contract(Terms()), out var drift));
        Assert.Contains("composite pipeline terms", drift!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The round trip the whole helper rests on: the reader reads back exactly what the writer
    /// wrote, in the writer's own field order. If this drifts, every comparison above is silently
    /// comparing the wrong two fields.
    /// </summary>
    [Fact]
    public void The_reader_recovers_every_field_the_writer_wrote()
    {
        var terms = Terms();

        Assert.True(BekiCompositeContractTerms.TryParse(terms.ToString(), out var parsed));
        Assert.Equal(terms, parsed);
    }

    /// <summary>
    /// Terms with a distinct value in every field, so a comparison that reads the wrong one cannot
    /// pass by coincidence.
    /// </summary>
    private static BekiCompositeContractTerms Terms() => new(
        PoseRegistryVersion: "poses-1",
        PipelineConfigVersion: "config-1",
        StoryPromptVersion: "story-1",
        ImagePromptVersion: "image-1",
        IdentityPromptVersion: "identity-1",
        ThemeId: "dinosaurs",
        ThemeReferenceSha256: new string('a', 64),
        PoseKeywordRevision: "keywords-1");

    /// <summary>
    /// A whole contract as the manifest carries it: the composite line, then the book's spreads,
    /// built by the same code the manifest writer uses.
    /// </summary>
    private static IReadOnlyList<string> Contract(BekiCompositeContractTerms terms) =>
        BekiFulfillmentManifest.CurrentContract(BookFormat.SpreadCount, terms);
}
