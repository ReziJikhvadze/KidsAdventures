using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using AdventurePacks.Api.Services.Story.Prompts;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The cover is drawn once, at preview time, and the purchased book reuses it — owner, 2026-09-06:
/// "create the cover first — the REAL cover — and when we proceed to the book, reuse it."
///
/// The two ends of that are tested where they live: the preview's own behaviour in
/// <see cref="CompositePipelinePreviewTests"/>, the fulfilment job's adoption in
/// <see cref="CompositePipelineFulfillmentTests"/>. What is here is the contract BETWEEN them —
/// the six blob names, and the rules about which stored covers may be adopted at all — plus the
/// harness both files build on, because a fake that disagreed with itself across the seam would
/// make both ends pass while the product stayed broken.
/// </summary>
public class BekiPreviewCoverReuseTests : CompositePipelineTestBase
{
    // ===========================================================================================
    // The contract: six names, one prefix, one run
    // ===========================================================================================

    /// <summary>
    /// Every artifact the two jobs exchange lives under the run's own prefix — the same one the
    /// portrait and the cover image are under.
    ///
    /// It is the whole privacy story of this feature. The identity spec describes a real child's
    /// hair, eyes and skin, and the wrap is a picture of that child; both are deleted when the guest
    /// run expires, because the sweep deletes by this prefix. A name that escaped it would be a
    /// photograph of somebody's child outliving the run they were told it belonged to.
    /// </summary>
    [Fact]
    public void Every_run_cover_artifact_lives_under_the_runs_own_prefix()
    {
        var runId = Guid.NewGuid();
        var prefix = $"master-runs/{runId:N}/";

        Assert.Equal(prefix, BekiRunBlobs.Prefix(runId));

        Assert.All(
            BekiRunBlobs.CoverArtifacts(runId).Append(BekiRunBlobs.CoverName(runId)),
            name => Assert.StartsWith(prefix, name, StringComparison.Ordinal));

        // The names themselves, stated once, because they are a storage contract between two jobs
        // that never run in the same process — a rename is a book whose cover is silently redrawn.
        Assert.Equal(
            [
                prefix + "cover-wrap-base.png",
                prefix + "cover-wrap-composite.png",
                prefix + "cover-composition.json",
                prefix + "cover-wrap-generation.json",
                prefix + "visual-scenario.json",
                prefix + "child-identity.json",
            ],
            BekiRunBlobs.CoverArtifacts(runId));

        // And the picture the parent sees keeps the name the legacy cover always had, so the
        // journey's preview stage and the guest-preview cover endpoint need to know nothing.
        Assert.Equal(prefix + "cover", BekiRunBlobs.CoverName(runId));
    }

    /// <summary>
    /// The two jobs name the same six files. Stated as an assertion rather than as a comment,
    /// because the copy at purchase time is a file-for-file copy and a pair that drifted would leave
    /// a book with a cover master under a name nothing reads.
    /// </summary>
    [Fact]
    public void The_run_and_the_pack_name_the_same_cover_artifacts()
    {
        var runId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var packId = Guid.NewGuid();

        static string Leaf(string name) => name[(name.LastIndexOfAny(['/', '-']) + 1)..];

        Assert.Equal(
            Leaf(BekiPackBlobs.CoverWrapBaseName(userId, packId)),
            Leaf(BekiRunBlobs.CoverWrapBaseName(runId)));
        Assert.Equal(
            Leaf(BekiPackBlobs.CoverWrapCompositeName(userId, packId)),
            Leaf(BekiRunBlobs.CoverWrapCompositeName(runId)));
        Assert.Equal(
            Leaf(BekiPackBlobs.ScenarioName(userId, packId)),
            Leaf(BekiRunBlobs.ScenarioName(runId)));
        Assert.Equal(
            Leaf(BekiPackBlobs.IdentitySpecName(userId, packId)),
            Leaf(BekiRunBlobs.IdentitySpecName(runId)));
    }

    // ===========================================================================================
    // The boundary the preview builds its context from
    // ===========================================================================================

    /// <summary>
    /// The preview and the fulfilment job assemble the SAME four inputs out of a run, because they
    /// now draw pictures of the same book. One builder, one mapping, and the photograph travelling
    /// as a reference rather than as bytes.
    /// </summary>
    [Fact]
    public void The_composite_input_is_built_from_the_run_the_same_way_for_both_jobs()
    {
        var run = new MasterStoryRun
        {
            Id = Guid.NewGuid(),
            ChildName = "ნინა",
            Age = 5,
            Gender = "girl",
            Theme = "Dinosaurs",
            EyeColor = "მწვანე",
            SpreadCount = BookFormat.SpreadCount,
            StoryLanguage = "ka",
            PhotoBlobUrl = "https://blob.test/portrait.png",
            // Declared and deliberately never read past the boundary.
            ExtraWishes = "a dragon please",
        };

        var input = BekiCompositeInputs.For(run, run.Theme);

        Assert.Equal("ნინა", input.ChildName);
        Assert.Equal(5, input.ChildAge);
        Assert.Equal("girl", input.ChildGender);
        Assert.Equal("Dinosaurs", input.ThemeId);
        Assert.Equal("https://blob.test/portrait.png", input.ChildPhotoRef);

        // The parent's own answer reaches the identity spec and nothing else.
        Assert.Equal("მწვანე", input.LegacyEyeColor);

        // And the story boundary still cannot see the photograph, the eye colour or the wish.
        var normalized = InputNormalization.NormalizeForStory(input);
        Assert.True(normalized.IsValid);
        Assert.DoesNotContain(
            "მწვანე",
            JsonSerializer.Serialize(normalized.Story!),
            StringComparison.Ordinal);
    }

    // ===========================================================================================
    // What may be adopted, and what may not
    // ===========================================================================================

    /// <summary>
    /// A stored wrap is verified against its own composition receipt before it may become a book's
    /// cover master — the same check the press stage makes of the pack's own wrap.
    ///
    /// It is the one thing that makes reuse safe rather than merely cheap. The bytes crossed a job
    /// boundary and a storage account since anybody looked at them, and audit P0-10's whole finding
    /// was a cover master nobody could check against its receipt. Bytes that fail this are not a
    /// cover, wherever they came from.
    /// </summary>
    [Fact]
    public void A_wrap_whose_bytes_do_not_match_its_receipt_is_not_a_cover_master()
    {
        byte[] basePng = [0x89, (byte)'P', (byte)'N', (byte)'G', 1];
        byte[] composite = [0x89, (byte)'P', (byte)'N', (byte)'G', 2];

        var honest = JsonSerializer.Deserialize<BekiCompositionManifest>(
            CompositePipelineFulfillmentTests.PressReceipt(basePng, composite))!;

        // The good case first, so the refusals below are about the tampering and not the fixture.
        BekiPressComposite.ValidateSource(basePng, composite, honest);

        Assert.Throws<BekiLayoutException>(() =>
            BekiPressComposite.ValidateSource([.. basePng, 9], composite, honest));

        Assert.Throws<BekiLayoutException>(() =>
            BekiPressComposite.ValidateSource(basePng, [.. composite, 9], honest));

        // And the approved layer's own rules: a Beki that was mirrored is not the approved Beki.
        Assert.Throws<BekiLayoutException>(() => BekiPressComposite.ValidateSource(
            basePng, composite, honest with { BekiLayer = honest.BekiLayer with { Mirrored = true } }));
    }
}

// ===============================================================================================
// The harness both ends share
// ===============================================================================================

/// <summary>
/// A preview wrap, its receipt and its two documents, built offline — exactly the six artifacts a
/// print-format preview stores.
///
/// One fixture rather than two, because both ends of this feature assert against it: the preview
/// test says these bytes were stored, and the fulfilment test says the pack's cover blobs equal the
/// run's. Two fixtures would let those two sentences be true of different books.
/// </summary>
internal static class PreviewWrapFixture
{
    public static byte[] BasePng => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x11, 0x22];

    public static byte[] CompositePng => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x33, 0x44];

    public static string ReceiptJson =>
        CompositePipelineFulfillmentTests.PressReceipt(BasePng, CompositePng);

    public static string GenerationJson =>
        """{"prompt_version":"cover-wrap-v1","model":"stub-image-model","ms":1}""";

    public static string ScenarioJson => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "nina_dinosaurs", "visual_scenario_output_v2.json"));

    public static string IdentityJson =>
        CompositeChildIdentity.ToStoredJson(CompositePipelineTestBase.IdentityFixture);

    /// <summary>The wrap as the pipeline hands one back — receipt and generation record included.</summary>
    public static CompositeCoverWrap Wrap() =>
        new(BasePng, CompositePng, ReceiptJson, "pose_04_listen", "cover wrap prompt")
        {
            GenerationReceiptJson = GenerationJson,
        };
}

/// <summary>
/// The composite pipeline, stubbed at the three seams the preview now uses: the identity
/// derivation, the scenario planner and the cover wrap.
///
/// It counts rather than pretends. The point of the preview tests is which calls are bought and in
/// what order, and the point of the fulfilment tests is that a book with an adopted cover buys none
/// of them — a stub that answered plausibly without counting would let both claims pass while the
/// product paid twice.
/// </summary>
internal sealed class FakeCoverPipeline : ICompositeBookPipeline
{
    public int IdentityCalls { get; private set; }

    public int ScenarioCalls { get; private set; }

    public int WrapCalls { get; private set; }

    /// <summary>The context each stage was handed, so a test can read what the run mapped to.</summary>
    public CompositeBookContext? LastContext { get; private set; }

    /// <summary>The anchor the wrap was asked to draw against — null is the claim, on a preview.</summary>
    public byte[]? WrapAnchor { get; private set; }

    /// <summary>The story the scenario was planned from.</summary>
    public MasterStory? PlannedFrom { get; private set; }

    /// <summary>Thrown by the wrap call, so the preview's fallback can be reached.</summary>
    public Exception? WrapFails { get; init; }

    /// <summary>The wrap comes back with no generation receipt — not an adoptable cover.</summary>
    public bool WrapHasNoGenerationReceipt { get; init; }

    public Task<CompositeBookResult> RunAsync(
        CompositeBookRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("the preview never draws a book.");

    public Task<byte[]> DrawCoverAsync(
        CompositeBookContext context, VisualScenarioV2 scenario, byte[] childPhoto,
        string childPhotoContentType, CancellationToken cancellationToken) =>
        throw new NotSupportedException("the composite path draws no reader-facing cover.");

    public Task<ChildIdentitySpec> DeriveIdentityAsync(
        CompositeBookContext context, byte[] childPhoto, CancellationToken cancellationToken)
    {
        IdentityCalls++;
        LastContext = context;
        return Task.FromResult(CompositePipelineTestBase.IdentityFixture);
    }

    public Task<CompositeScenarioPlan> PlanPreviewCoverAsync(
        CompositeBookContext context, MasterStory story, byte[] childPhoto,
        CancellationToken cancellationToken)
    {
        ScenarioCalls++;
        LastContext = context;
        PlannedFrom = story;

        return Task.FromResult(CompositePreviewCoverPlan.Create(
            VisualScenarioValidator.Validate(PreviewWrapFixture.ScenarioJson).Scenario!));
    }

    public Task<CompositeCoverWrap> DrawCoverWrapAsync(
        CompositeBookContext context, VisualScenarioV2 scenario, byte[] childPhoto,
        string childPhotoContentType, ChildIdentitySpec identity, byte[]? childAnchor,
        CancellationToken cancellationToken)
    {
        WrapCalls++;
        LastContext = context;
        WrapAnchor = childAnchor;

        if (WrapFails is { } failure)
        {
            throw failure;
        }

        var wrap = PreviewWrapFixture.Wrap();

        return Task.FromResult(WrapHasNoGenerationReceipt
            ? wrap with { GenerationReceiptJson = null }
            : wrap);
    }
}

/// <summary>
/// Blob storage that remembers everything, for the half of this feature that IS storage: six names
/// written by one job and read by another.
/// </summary>
internal sealed class RecordingPreviewBlobs : IBlobStorageService
{
    public Dictionary<string, byte[]> Uploaded { get; } = new(StringComparer.Ordinal);

    public List<string> UploadOrder { get; } = [];

    /// <summary>The portrait every download answers with, whatever was asked for.</summary>
    public byte[] Photo { get; init; } = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    public Task<string> UploadAsync(
        string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        Uploaded[blobName] = bytes;
        UploadOrder.Add(blobName);
        ContentTypes[blobName] = contentType;
        return Task.FromResult($"https://blob.test/{blobName}");
    }

    public Dictionary<string, string> ContentTypes { get; } = new(StringComparer.Ordinal);

    public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new MemoryStream(
            Uploaded.TryGetValue(blobName, out var bytes) ? bytes : []));

    public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) =>
        Task.FromResult(Uploaded.ContainsKey(blobName));

    public Task<byte[]> DownloadBytesFromStoredUrlAsync(
        string storedUrl, CancellationToken cancellationToken) =>
        Task.FromResult(Photo);

    public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
        Task.FromResult(Uploaded.Remove(
            storedUrl.Replace("https://blob.test/", string.Empty, StringComparison.Ordinal)));

    public Task<byte[]?> TryDownloadBesideAsync(
        string storedUrl, string suffix, CancellationToken cancellationToken) =>
        Task.FromResult<byte[]?>(null);

    public Task UploadBesideAsync(
        string storedUrl, string suffix, byte[] bytes, string contentType,
        CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The legacy Beki cover generator, answering successfully — the fallback the preview must still
/// reach when the real wrap cannot be drawn.
///
/// The shared spy refuses every call, which is right for the tests it was written for (the cover
/// path must not run at all) and useless here: the claim under test is that a preview whose wrap
/// failed still gets a cover, and a generator that throws would prove only that the deeper
/// child-only fallback exists.
/// </summary>
internal sealed class LegacyCoverGenerator : IBekiBookGenerator
{
    public static readonly byte[] CoverPng = [0x89, (byte)'P', (byte)'N', (byte)'G', 0xAA];

    public int CoverCalls { get; private set; }

    public Task<BekiBookResult> GenerateAsync(
        MasterStoryInput input, byte[] childPhoto, string childPhotoContentType,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<BekiBookResult> IllustrateAsync(
        MasterStory plan, byte[] childPhoto, string childPhotoContentType, byte[]? existingCover,
        Func<BekiImageResult, Task>? onImage, CancellationToken cancellationToken,
        IReadOnlyDictionary<int, byte[]>? existingSpreads = null,
        CompositeBookContext? composite = null) => throw new NotSupportedException();

    public Task<BekiImageResult> DrawCoverAsync(
        MasterStory plan, byte[] childPhoto, string childPhotoContentType,
        CancellationToken cancellationToken, CompositeBookContext? composite = null)
    {
        CoverCalls++;

        return Task.FromResult(new BekiImageResult
        {
            Image = CoverPng,
            Accepted = true,
            Verdict = "PASS (legacy preview cover)",
            Attempts = 1,
            Prompt = "legacy cover prompt",
        });
    }
}

/// <summary>
/// The run repository the preview tests already share, with the one thing it does not remember:
/// which url was saved as the run's cover.
///
/// A decorator rather than a second repository, because everything else about the shared one — the
/// run it hands out, the prompts and story it records, the failure it captures — is exactly what
/// these tests want, and a copy would be one more thing to keep in step.
/// </summary>
internal sealed class CoverRecordingRuns(IMasterStoryRunRepository inner) : IMasterStoryRunRepository
{
    /// <summary>What <c>run.CoverImageUrl</c> would be, once the job has finished.</summary>
    public string? SavedCoverUrl { get; private set; }

    public Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken)
    {
        SavedCoverUrl = coverImageUrl;
        return inner.SaveCoverAsync(id, coverImageUrl, cancellationToken);
    }

    public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        inner.GetByIdAsync(id, cancellationToken);

    public Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken) =>
        inner.CreateAsync(run, cancellationToken);

    public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken) =>
        inner.GetProgressAsync(id, cancellationToken);

    public Task SetProgressAsync(
        Guid id, string status, string? progressMessage, CancellationToken cancellationToken) =>
        inner.SetProgressAsync(id, status, progressMessage, cancellationToken);

    public Task SavePromptsAsync(
        Guid id, string model, string promptVersion, string systemPrompt, string userPrompt,
        CancellationToken cancellationToken) =>
        inner.SavePromptsAsync(id, model, promptVersion, systemPrompt, userPrompt, cancellationToken);

    public Task SaveStoryAsync(
        Guid id, string storyJson, string contentJson, int promptTokens, int completionTokens,
        CancellationToken cancellationToken) =>
        inner.SaveStoryAsync(id, storyJson, contentJson, promptTokens, completionTokens, cancellationToken);

    public Task SaveAppearanceDescriptionAsync(
        Guid id, string appearanceDescription, CancellationToken cancellationToken) =>
        inner.SaveAppearanceDescriptionAsync(id, appearanceDescription, cancellationToken);

    public Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken) =>
        inner.MarkReadyAsync(id, contentJson, cancellationToken);

    public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken) =>
        inner.MarkFailedAsync(id, error, cancellationToken);

    public Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken) =>
        inner.ClaimAsync(id, userId, packId, cancellationToken);

    public Task<int> AttachToUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        inner.AttachToUserAsync(id, userId, cancellationToken);

    public Task<IReadOnlyList<MasterStoryRunSummary>> ListUnboughtForUserAsync(
        Guid userId, int limit, CancellationToken cancellationToken) =>
        inner.ListUnboughtForUserAsync(userId, limit, cancellationToken);

    public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(
        int limit, CancellationToken cancellationToken) =>
        inner.ListExpiredAsync(limit, cancellationToken);

    public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) =>
        inner.DeleteAsync(ids, cancellationToken);
}

/// <summary>
/// What a finished print-format preview must have left under its run, asserted from one place.
///
/// A free function rather than a member of either test class, because both ends of the feature check
/// it — the preview says it wrote these six, the fulfilment job says the pack's cover blobs are
/// these six — and two copies of the list would let those two sentences be about different books.
/// </summary>
internal static class PreviewCoverAssertions
{
    public static void RunHoldsTheCover(RecordingPreviewBlobs blobs, Guid runId)
    {
        foreach (var name in BekiRunBlobs.CoverArtifacts(runId))
        {
            Assert.Contains(name, blobs.Uploaded.Keys);
        }

        Assert.Equal(PreviewWrapFixture.BasePng, blobs.Uploaded[BekiRunBlobs.CoverWrapBaseName(runId)]);
        Assert.Equal(
            PreviewWrapFixture.CompositePng, blobs.Uploaded[BekiRunBlobs.CoverWrapCompositeName(runId)]);
        Assert.Equal(
            CompositePreviewCoverPlan.Create(VisualScenarioValidator.Validate(PreviewWrapFixture.ScenarioJson).Scenario!).Json,
            Encoding.UTF8.GetString(blobs.Uploaded[BekiRunBlobs.ScenarioName(runId)]));
        Assert.Equal(
            PreviewWrapFixture.IdentityJson,
            Encoding.UTF8.GetString(blobs.Uploaded[BekiRunBlobs.IdentitySpecName(runId)]));
        Assert.Equal(
            PreviewWrapFixture.GenerationJson,
            Encoding.UTF8.GetString(blobs.Uploaded[BekiRunBlobs.CoverWrapGenerationName(runId)]));
    }
}
