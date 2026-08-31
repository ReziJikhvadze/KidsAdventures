using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The release gates, and the finding that produced them.
///
/// Audit P0-09: `BEKI_Acceptance_Gates_v1.json` declares sixteen hard gates and
/// `release_policy: all_hard_gates_must_pass`, and had zero C# references. The only gate in the
/// pipeline was the composition-receipt check; `TryUpdateStatusAsync(Completed)` followed it
/// unconditionally; `needs_human_reading` was computed, serialized and read by nothing. A book was
/// declared finished because nothing had thrown.
///
/// So these tests are about the two answers that were missing. An absent artifact is not a pass —
/// it is <c>UNKNOWN</c>, and it withholds. And the withholding is graduated by amendment A5's
/// governance classes: a press failure must never hold back the parent's download, and no failure at
/// all holds back the in-app reader.
/// </summary>
public class BekiReleaseGatesTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PackId = Guid.NewGuid();

    /// <summary>
    /// A book with nothing stored releases nothing, and says so gate by gate rather than throwing.
    /// </summary>
    [Fact]
    public async Task A_book_with_no_evidence_passes_nothing()
    {
        var verdict = await new BekiReleaseGates(new FakeBlobs())
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.Equal(BekiReleaseGates.NotReleasable, verdict.Verdict);
        Assert.False(verdict.IsReleasable);
        Assert.False(verdict.CustomerPdfMayPublish);
        Assert.False(verdict.PressFilesMayPublish);

        // Every gate the supplier's document names is answered, and none of them is a pass.
        var expected = BekiReleaseGates.ReadGateIds(AppContext.BaseDirectory);
        Assert.Equal(16, expected.Count);
        Assert.Equal(expected, verdict.Gates.Select(gate => gate.Id).ToList());
        Assert.All(verdict.Gates, gate => Assert.NotEqual(BekiReleaseGates.Pass, gate.Status));

        // "UNKNOWN", not "FAIL": nothing was measured and found wrong — nothing was measured.
        Assert.Contains(verdict.Gates, gate =>
            gate.Id == "ASSET_LOCK" && gate.Status == BekiReleaseGates.Unknown);
    }

    /// <summary>A fully evidenced book releases everything, which is the point of measuring it.</summary>
    [Fact]
    public async Task A_fully_evidenced_book_is_releasable()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.Equal(
            BekiReleaseGates.Releasable,
            verdict.Verdict);
        Assert.Empty(verdict.FailingGates);
        Assert.True(verdict.CustomerPdfMayPublish);
        Assert.True(verdict.PressFilesMayPublish);
        Assert.False(verdict.AwaitingHumanReview);
        Assert.Equal(ContactSheetSha, verdict.ContactSheetSha256);
    }

    /// <summary>
    /// Amendment A5's whole reason for classifying gates: a press file that failed its resolution
    /// gate must not cost a family the book they paid for.
    /// </summary>
    [Fact]
    public async Task A_press_failure_withholds_the_press_files_and_not_the_parents_download()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);

        blobs.Seed(BekiPackBlobs.PressStatusName(UserId, PackId), Json(new
        {
            failed_gates = new[] { "PRESS_RESOLUTION" },
            reason = "the source art carries 143 PPI of detail at placement size",
        }));

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.False(verdict.PressFilesMayPublish);
        Assert.True(verdict.CustomerPdfMayPublish);
        Assert.Contains("PRESS_RESOLUTION", verdict.FailingGates);
        Assert.Equal(BekiReleaseGates.NotReleasable, verdict.Verdict);

        // The three press gates that did not refuse still pass: a refusal names itself.
        Assert.Contains(verdict.Gates, gate =>
            gate.Id == "PRESS_GEOMETRY" && gate.Status == BekiReleaseGates.Pass);
    }

    /// <summary>
    /// The digital preflight is the customer file's own gate, and its absence withholds the download
    /// while the printer's files, which do not depend on it, stay releasable.
    /// </summary>
    [Fact]
    public async Task A_missing_digital_preflight_withholds_the_download_only()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);
        blobs.Remove(BekiPackBlobs.DigitalReportName(UserId, PackId));

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.False(verdict.CustomerPdfMayPublish);
        Assert.True(verdict.PressFilesMayPublish);
        Assert.Contains("DIGITAL_GEOMETRY", verdict.FailingGates);
    }

    /// <summary>
    /// `needs_human_reading` was computed and read by nobody. It is now the one gate a machine
    /// cannot close: a shot or age advisory holds every deliverable file until a person signs the
    /// rendered contact sheet.
    /// </summary>
    [Fact]
    public async Task An_unresolved_human_flag_holds_every_file_until_somebody_signs()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs, needsHumanReading: true);

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.True(verdict.AwaitingHumanReview);
        Assert.False(verdict.CustomerPdfMayPublish);
        Assert.False(verdict.PressFilesMayPublish);
        Assert.Contains(verdict.Gates, gate =>
            gate.Id == "VISUAL_QA" && gate.Status == BekiReleaseGates.NeedsHuman);
    }

    /// <summary>
    /// Amendment A2: the approval is of a specific rendering. An approval that names a different
    /// contact sheet is an approval of a book that no longer exists, and does not resolve anything.
    /// </summary>
    [Fact]
    public async Task A_stale_approval_does_not_resolve_the_human_gate()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs, needsHumanReading: true);

        blobs.Seed(BekiPackBlobs.HumanApprovalName(UserId, PackId), Encoding.UTF8.GetBytes(
            new BekiHumanApproval(
                "misho@example.test", DateTimeOffset.UtcNow, new string('b', 64), "looked fine")
                .ToJson()));

        var stale = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.True(stale.AwaitingHumanReview);
        Assert.Contains(stale.Gates, gate =>
            gate.Id == "VISUAL_QA" && gate.Detail.Contains("different contact sheet"));

        // The same approval, naming the sheet render validation actually produced, resolves it.
        blobs.Seed(BekiPackBlobs.HumanApprovalName(UserId, PackId), Encoding.UTF8.GetBytes(
            new BekiHumanApproval(
                "misho@example.test", DateTimeOffset.UtcNow, ContactSheetSha, "looked fine")
                .ToJson()));

        var signed = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.False(signed.AwaitingHumanReview);
        Assert.True(signed.CustomerPdfMayPublish);
        Assert.Contains(signed.Gates, gate =>
            gate.Id == "VISUAL_QA" && gate.Detail.Contains("misho@example.test"));
    }

    /// <summary>
    /// Audit P0-01. A manifest whose cover record still names an AI redraw is a book with two cover
    /// designs in it, whatever else is stored.
    /// </summary>
    [Fact]
    public async Task A_cover_record_that_is_not_the_wrap_master_fails_the_single_master_gate()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);

        blobs.Seed(BekiPackBlobs.ManifestName(UserId, PackId), Manifest(
            new BekiCoverRecord("https://blob.test/redraw", "cover-identity-redraw-v1.4", "PASS")));

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.Contains(verdict.Gates, gate =>
            gate.Id == "SINGLE_COVER_MASTER" && gate.Status == BekiReleaseGates.Fail);
        Assert.False(verdict.CustomerPdfMayPublish);
        Assert.False(verdict.PressFilesMayPublish);
    }

    /// <summary>
    /// Amendment A4: an adopted spread whose stored QA is gone is a page nothing current has looked
    /// at. The gate refuses rather than counting the pages that happen to have records.
    /// </summary>
    [Fact]
    public async Task A_spread_whose_stored_QA_is_gone_refuses_the_visual_gate()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);
        blobs.Remove(BekiPackBlobs.SpreadQaName(UserId, PackId, 4));

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.Contains(verdict.Gates, gate =>
            gate.Id == "VISUAL_QA"
            && gate.Status == BekiReleaseGates.Unknown
            && gate.Detail.Contains("spread(s) 4"));
    }

    /// <summary>
    /// A QA document written by a superseded reviewer contract answered different questions, so it
    /// is treated as no document — the same rule the resume path applies, for the same reason.
    /// </summary>
    [Fact]
    public async Task A_QA_document_from_an_older_reviewer_contract_does_not_count()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);

        blobs.Seed(BekiPackBlobs.SpreadQaName(UserId, PackId, 2), Json(new
        {
            page = 2,
            qa_prompt_version = "beki-minimal-visual-qa-v1.2",
            status = "PASS",
            recommended_action = "ship",
        }));

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.Contains(verdict.Gates, gate =>
            gate.Id == "VISUAL_QA" && gate.Detail.Contains("spread(s) 2"));
    }

    /// <summary>
    /// Amendment A8: Poppler absent means the render was never fully done, and a validation that did
    /// not run cannot evidence anything. A NOT_RELEASABLE render report withholds the press files.
    /// </summary>
    [Fact]
    public async Task A_refused_render_validation_withholds_the_press_files()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);

        blobs.Seed(
            BekiPackBlobs.RenderReportName(UserId, PackId, BekiPackBlobs.CoverRenderArtifact),
            RenderReport(BekiPackBlobs.CoverRenderArtifact, releasable: false, qrPage: null));

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.Contains("RENDER_VALIDATION", verdict.FailingGates);
        Assert.False(verdict.PressFilesMayPublish);

        // Shared gates are untouched, so the parent's book is still publishable.
        Assert.True(verdict.CustomerPdfMayPublish);
    }

    /// <summary>
    /// A stored final that nothing rendered back does not get carried by the artifacts that did.
    ///
    /// The gate asked whether ANY report was releasable, so a press cover with no render report of
    /// its own — the file a printer receives — passed on the strength of the interior and the
    /// reading copy. Every file in storage owes this gate a report about itself.
    /// </summary>
    [Fact]
    public async Task A_stored_final_without_a_render_report_refuses_the_render_gate()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);
        blobs.Remove(
            BekiPackBlobs.RenderReportName(UserId, PackId, BekiPackBlobs.CoverRenderArtifact));

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        var gate = Assert.Single(verdict.Gates, g => g.Id == "RENDER_VALIDATION");
        Assert.Equal(BekiReleaseGates.Unknown, gate.Status);
        Assert.Contains(BekiPackBlobs.CoverRenderArtifact, gate.Detail);

        // The press cover is a press file, so the press slot withholds and the parent's book does
        // not — the file-level rule, not a book-level one.
        Assert.False(verdict.PressFilesMayPublish);
        Assert.True(verdict.CustomerPdfMayPublish);
        Assert.Equal(BekiReleaseGates.NotReleasable, verdict.Verdict);
    }

    /// <summary>
    /// The customer's PDF is render-validated like the press files, and its failures withhold IT.
    ///
    /// RENDER_VALIDATION and QR are classed press because the supplier wrote them for the printer,
    /// and they aggregate evidence from the reading copy as well — so a download whose fonts did not
    /// resolve failed a press gate and was published anyway. The gate ids are unchanged; the
    /// evidence under them is sliced by the artifact it came off.
    /// </summary>
    [Fact]
    public async Task A_refused_render_on_the_reading_copy_withholds_the_parents_download()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);

        blobs.Seed(
            BekiPackBlobs.RenderReportName(UserId, PackId, BekiPackBlobs.DigitalRenderArtifact),
            RenderReport(BekiPackBlobs.DigitalRenderArtifact, releasable: false, qrPage: 12));

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.False(verdict.CustomerPdfMayPublish);
        Assert.Contains("RENDER_VALIDATION", verdict.FailingGates);

        // And the printer's files, which this says nothing about, are unaffected.
        Assert.True(verdict.PressFilesMayPublish);

        var slice = Assert.Single(
            verdict.ArtifactEvidence,
            artifact => artifact.Artifact == BekiPackBlobs.DigitalRenderArtifact);
        Assert.Equal(BekiReleaseGates.DigitalClass, slice.Class);
        Assert.Equal(BekiReleaseGates.Fail, slice.Render);
    }

    /// <summary>The same split for the QR: a code that will not scan off the parent's own file.</summary>
    [Fact]
    public async Task A_failed_qr_on_the_reading_copy_withholds_the_parents_download()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);

        blobs.Seed(
            BekiPackBlobs.RenderReportName(UserId, PackId, BekiPackBlobs.DigitalRenderArtifact),
            RenderReport(
                BekiPackBlobs.DigitalRenderArtifact, releasable: true, qrPage: 12,
                qrStatus: "failed"));

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.False(verdict.CustomerPdfMayPublish);
        Assert.Contains("QR", verdict.FailingGates);
    }

    /// <summary>
    /// A fixed page's QA record is read, not counted: a document whose own status says FAIL is
    /// evidence that the page was looked at and found wrong.
    /// </summary>
    [Fact]
    public async Task A_fixed_page_QA_record_that_records_a_failure_refuses_the_visual_gate()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);

        blobs.Seed(
            BekiPackBlobs.FixedPageQaName(UserId, PackId, "endpaper-front"),
            FixedPageQa("endpaper-front", BekiFixedPageQa.Fail));

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        var gate = Assert.Single(verdict.Gates, g => g.Id == "VISUAL_QA");
        Assert.Equal(BekiReleaseGates.Fail, gate.Status);
        Assert.Contains("endpaper-front", gate.Detail);

        // VISUAL_QA is shared, so nothing ships.
        Assert.False(verdict.CustomerPdfMayPublish);
        Assert.False(verdict.PressFilesMayPublish);
    }

    /// <summary>
    /// And a record written under a superseded QA contract is no record — the same rule the spread
    /// documents follow, for the same reason.
    /// </summary>
    [Fact]
    public async Task A_fixed_page_QA_record_from_an_older_contract_does_not_count()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);

        blobs.Seed(
            BekiPackBlobs.FixedPageQaName(UserId, PackId, "credits"),
            FixedPageQa("credits", BekiFixedPageQa.Pass, version: "beki-fixed-page-qa-v0"));

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.Contains(verdict.Gates, gate =>
            gate.Id == "VISUAL_QA"
            && gate.Status == BekiReleaseGates.Unknown
            && gate.Detail.Contains("credits"));
    }

    /// <summary>
    /// The retry defect: the digital preparation fails, the unprepared PDF goes to storage under the
    /// same name, and the report the PREVIOUS successful attempt wrote is still under the report's
    /// name. Presence used to be the whole check, so stale evidence published an unvalidated file.
    /// </summary>
    [Fact]
    public async Task A_withheld_digital_report_is_a_refusal_rather_than_a_present_report()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);

        blobs.Seed(
            BekiPackBlobs.DigitalReportName(UserId, PackId),
            BekiWithheldReport.Bytes(
                "DIGITAL_GEOMETRY", "laying out the customer's book",
                "the reading copy carries printer-only structures"));

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        var gate = Assert.Single(verdict.Gates, g => g.Id == "DIGITAL_GEOMETRY");
        Assert.Equal(BekiReleaseGates.Fail, gate.Status);

        Assert.False(verdict.CustomerPdfMayPublish);
        Assert.True(verdict.PressFilesMayPublish);
    }

    /// <summary>The same staleness on the press side, refused the same way.</summary>
    [Fact]
    public async Task A_withheld_press_preflight_is_a_refusal_rather_than_a_present_report()
    {
        var blobs = new FakeBlobs();
        SeedCompleteBook(blobs);

        blobs.Seed(
            BekiPackBlobs.InteriorPreflightName(UserId, PackId),
            BekiWithheldReport.Bytes(
                "PRESS_RESOLUTION", "preparing the press interior",
                "the source art carries 143 PPI of detail at placement size"));

        var verdict = await new BekiReleaseGates(blobs)
            .EvaluateAsync(UserId, PackId, CancellationToken.None);

        Assert.Contains(verdict.Gates, gate =>
            gate.Id == "PRESS_GEOMETRY" && gate.Status == BekiReleaseGates.Fail);
        Assert.False(verdict.PressFilesMayPublish);
        Assert.True(verdict.CustomerPdfMayPublish);
    }

    // ==============================================================================================
    // The fixed-page placement check (audit §10.1, and the false positive it used to produce)
    // ==============================================================================================

    /// <summary>
    /// A reading-mode endpaper is the approved pattern downsampled for a screen, so the raster it
    /// embeds cannot hash to the locked file — and the check compared exactly those two numbers,
    /// which failed every approved page it was pointed at.
    ///
    /// What it compares now is provenance: the source the page derived from, against the lock.
    /// </summary>
    [Fact]
    public void A_downsampled_endpaper_passes_the_placement_check_on_its_source_hash()
    {
        var locked = new string('a', 64);
        var derived = new string('b', 64);

        var document = BekiFixedPageQa.Write(
            "endpaper-front",
            new BekiLayoutReceipts("reading", [Endpaper(derived, locked)]),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { locked });

        Assert.NotNull(document);

        using var parsed = JsonDocument.Parse(document!);
        Assert.Equal(BekiFixedPageQa.Pass, parsed.RootElement.GetProperty("status").GetString());
        Assert.Empty(parsed.RootElement.GetProperty("failed_checks").EnumerateArray());

        // Both hashes travel: what the page carries, and what it came from.
        Assert.Equal(
            derived,
            parsed.RootElement.GetProperty("image_sha256")[0].GetString());
        Assert.Equal(
            locked,
            parsed.RootElement.GetProperty("source_sha256")[0].GetString());
    }

    /// <summary>
    /// And the check the page is there for still bites: a placeholder endpaper derives from bytes
    /// the lock never proved, and that is a failure with the page named.
    /// </summary>
    [Fact]
    public void An_endpaper_derived_from_unlocked_bytes_fails_the_placement_check()
    {
        var document = BekiFixedPageQa.Write(
            "endpaper-front",
            new BekiLayoutReceipts(
                "reading", [Endpaper(new string('b', 64), new string('c', 64))]),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { new('a', 64) });

        Assert.NotNull(document);

        using var parsed = JsonDocument.Parse(document!);
        Assert.Equal(BekiFixedPageQa.Fail, parsed.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            parsed.RootElement.GetProperty("failed_checks").EnumerateArray(),
            check => check.GetString()!.StartsWith("ASSET_PLACEMENT", StringComparison.Ordinal));
    }

    /// <summary>One endpaper page's receipt: an embedded raster, and the source it derives from.</summary>
    private static BekiLayoutPageReceipt Endpaper(string embedded, string source) =>
        new(2, "endpaper-front", 450d, 210d, 0d, [embedded], Wash: null, [], [],
            TextProbe: null, SourceSha256: [source]);

    // ==============================================================================================
    // Harness
    // ==============================================================================================

    private const string ContactSheetSha =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Everything a book that passes all sixteen gates has in storage.</summary>
    private static void SeedCompleteBook(FakeBlobs blobs, bool needsHumanReading = false)
    {
        blobs.Seed(BekiPackBlobs.AssetLockName(UserId, PackId), Json(new
        {
            manifest_version = BekiAssetLock.ManifestVersion,
            generated_at_utc = DateTimeOffset.UtcNow,
            source_registries = new Dictionary<string, string> { ["layout"] = "v1.2" },
            assets = new[]
            {
                new { role = "noto_sans_georgian_regular_licensed", file = "n.ttf", version = "v1", sha256 = new string('1', 64), approval_status = "approved" },
                new { role = "ottia_regular_ttf_licensed", file = "o.ttf", version = "v1", sha256 = new string('2', 64), approval_status = "approved" },
                new { role = "fogra39_output_intent", file = "p.icc", version = "v1", sha256 = new string('3', 64), approval_status = "approved" },
            },
        }));

        blobs.Seed(BekiPackBlobs.ManifestName(UserId, PackId), Manifest(
            new BekiCoverRecord(
                "https://blob.test/wrap", BekiCoverRecord.WrapMaster, "verified")
            {
                PoseId = "pose_01_neutral_hover",
                CompositeSha256 = new string('c', 64),
            }));

        blobs.Seed(BekiPackBlobs.CoverWrapCompositeName(UserId, PackId), [1]);
        blobs.Seed(BekiPackBlobs.CoverWrapBaseName(UserId, PackId), [2]);
        blobs.Seed(BekiPackBlobs.CoverCompositionName(UserId, PackId), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.CoverFrontName(UserId, PackId), [3]);
        blobs.Seed(BekiPackBlobs.StoryName(UserId, PackId), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.ScenarioName(UserId, PackId), "{}"u8.ToArray());

        blobs.Seed(BekiPackBlobs.CompositeReviewName(UserId, PackId), Json(new
        {
            needs_human_reading = needsHumanReading,
        }));

        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            blobs.Seed(BekiPackBlobs.SpreadQaName(UserId, PackId, spread), Json(new
            {
                page = spread,
                qa_prompt_version = CompositeMinimalQa.Version,
                status = "PASS",
                recommended_action = "ship",
            }));
        }

        foreach (var role in BekiFixedPageQa.Roles)
        {
            blobs.Seed(
                BekiPackBlobs.FixedPageQaName(UserId, PackId, role),
                FixedPageQa(role, BekiFixedPageQa.Pass));
        }

        foreach (var mode in BekiPackBlobs.LayoutModes)
        {
            blobs.Seed(BekiPackBlobs.LayoutReceiptName(UserId, PackId, mode), Json(new
            {
                mode,
                pages = new[]
                {
                    new
                    {
                        page = 1,
                        role = "intro",
                        text_lines = new[] { "ეს წიგნი ეკუთვნის ნინოს" },
                        typography = new[] { new { role = "intro", family = "Noto Sans Georgian" } },
                    },
                },
            }));
        }

        blobs.Seed(BekiPackBlobs.InteriorPreflightName(UserId, PackId), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.CoverPreflightName(UserId, PackId), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.DigitalReportName(UserId, PackId), "{}"u8.ToArray());

        // The three finals themselves. RENDER_VALIDATION enumerates what is in storage and demands
        // a report for each, so a book whose evidence is complete has to have the files the
        // evidence is about.
        foreach (var artifact in BekiPackBlobs.RenderedArtifacts)
        {
            blobs.Seed(BekiPackBlobs.FinalPdfName(UserId, PackId, artifact), [9]);
        }

        blobs.Seed(
            BekiPackBlobs.RenderReportName(UserId, PackId, BekiPackBlobs.InteriorRenderArtifact),
            RenderReport(BekiPackBlobs.InteriorRenderArtifact, releasable: true, qrPage: 11));
        blobs.Seed(
            BekiPackBlobs.RenderReportName(UserId, PackId, BekiPackBlobs.CoverRenderArtifact),
            RenderReport(BekiPackBlobs.CoverRenderArtifact, releasable: true, qrPage: null));
        blobs.Seed(
            BekiPackBlobs.RenderReportName(UserId, PackId, BekiPackBlobs.DigitalRenderArtifact),
            RenderReport(BekiPackBlobs.DigitalRenderArtifact, releasable: true, qrPage: 12));
    }

    private static byte[] Manifest(BekiCoverRecord cover) => Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(
            new BekiFulfillmentManifest
            {
                IllustrationContract = ["composite"],
                Entries = Enumerable.Range(1, BookFormat.SpreadCount)
                    .Select(n => new BekiFulfillmentManifestEntry(n, $"https://blob.test/spread-{n}"))
                    .ToList(),
                Compositions = Enumerable.Range(1, BookFormat.SpreadCount)
                    .Select(n => new BekiCompositionManifestEntry(
                        n, $"https://blob.test/receipt-{n}", "pose_01_neutral_hover",
                        new string('a', 64), $"https://blob.test/base-{n}"))
                    .ToList(),
                ScenarioUrl = "https://blob.test/scenario",
                StoryUrl = "https://blob.test/story",
                Cover = cover,
            },
            Web));

    private static byte[] RenderReport(
        string artifact, bool releasable, int? qrPage, string qrStatus = "ok") => Json(new
    {
        stage = "beki-render-validation-v1",
        artifact,
        verdict = releasable ? "RELEASABLE" : "NOT_RELEASABLE",
        failed_gates = releasable ? Array.Empty<string>() : ["RENDER_VALIDATION"],
        qr = new { gate = "QR", status = qrStatus, page = qrPage },
        contact_sheet = new { sha256 = ContactSheetSha, bytes = 1024 },
    });

    /// <summary>One fixed page's machine QA record, in the shape the composer's own writer emits.</summary>
    private static byte[] FixedPageQa(string role, string status, string? version = null) => Json(new
    {
        role,
        page = 1,
        qa_prompt_version = version ?? BekiFixedPageQa.Version,
        status,
        recommended_action = status == BekiFixedPageQa.Pass ? "ship" : "fix_layout",
        machine_generated = true,
    });

    private static byte[] Json(object value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true });

    private sealed class FakeBlobs : IBlobStorageService
    {
        private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

        public void Seed(string blobName, byte[] bytes) => _blobs[blobName] = bytes;

        public void Remove(string blobName) => _blobs.Remove(blobName);

        public Task<string> UploadAsync(
            string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken)
        {
            _blobs[blobName] = bytes;
            return Task.FromResult($"https://blob.test/{blobName}");
        }

        public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(
                _blobs.TryGetValue(blobName, out var bytes) ? bytes : []));

        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult(_blobs.ContainsKey(blobName));

        public Task<byte[]> DownloadBytesFromStoredUrlAsync(
            string storedUrl, CancellationToken cancellationToken) =>
            Task.FromResult(_blobs.TryGetValue(
                storedUrl.Replace("https://blob.test/", string.Empty), out var bytes) ? bytes : []);

        public Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult(_blobs.Remove(blobName));

        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
            Task.FromResult(_blobs.Remove(storedUrl.Replace("https://blob.test/", string.Empty)));
    }
}
