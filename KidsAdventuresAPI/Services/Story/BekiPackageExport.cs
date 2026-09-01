using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// One book's handback package, as a single zip — audit §9's deliverable list and P1-10's complaint
/// about the one that shipped.
///
/// The rejected package was a fixed list of eleven names with no checksums, no provenance, no ICC
/// profile, no contracts, no fonts, no poses, no normalized story, and an `excluded_by_design` block
/// that excluded the normalized story on the grounds that it lived somewhere else in our schema —
/// which is an answer about our database rather than about their package. Section 9 asks for the
/// story back; the child's photograph and identity file stay out, and now travel as a secure
/// reference and a SHA-256 in the manifest instead (amendment A7), so the dependency is identified
/// without being handed over.
///
/// Assembled on demand rather than at fulfilment time, because the package is an operator's download
/// and not a book artifact: what belongs in it has changed with every audit round, and a zip written
/// at generation time would fossilise whichever round was current.
///
/// **Nothing here decides whether a book may ship.** The zip always builds — a diagnostic package for
/// a refused book is exactly when somebody needs one — and carries `RELEASE_STATUS.json` at its root
/// saying what the gates found. Files that are not releasable are placed under `diagnostic/`
/// (amendment A5), so a reader can tell "this is the deliverable" from "this is what we have".
/// </summary>
public sealed class BekiPackageExport(IBlobStorageService blobStorage, IOptions<BekiOptions> bekiOptions)
{
    private static readonly JsonSerializerOptions Readable = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The audit's own file names for the three deliverables, at the root of the zip.
    ///
    /// Section 6 names them and the rejected package did not use them: `interior.pdf` under a
    /// `press/` folder is a file an operator has to translate before they can talk to the printer
    /// about it. The <c>v001</c> is the revision of the deliverable, which is what the supplier
    /// increments when a book is re-cut.
    /// </summary>
    public static string PressCoverFileName(Guid packId) => $"BEKI_{packId}_PRESS_COVER_v001.pdf";

    public static string PressInteriorFileName(Guid packId) => $"BEKI_{packId}_PRESS_INTERIOR_v001.pdf";

    public static string DigitalReadingFileName(Guid packId) => $"BEKI_{packId}_DIGITAL_READING_v001.pdf";

    /// <summary>The zip an operator downloads, named for the book it is about.</summary>
    public static string PackageFileName(Guid packId) => $"BEKI_{packId}_HANDBACK_v002.zip";

    /// <summary>Builds the zip, including whatever exists and listing whatever does not.</summary>
    public async Task<byte[]> BuildAsync(
        Guid userId, Guid packId, string? title, CancellationToken cancellationToken)
    {
        var release = BekiReleaseGateReport.TryParse(
            await TryReadTextAsync(BekiPackBlobs.ReleaseGatesName(userId, packId), cancellationToken));

        var entries = BlobEntries(userId, packId, release);

        var included = new List<PackageEntry>();
        var missing = new List<string>();

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (blobName, zipPath, status, optional) in entries)
            {
                if (!await blobStorage.ExistsAsync(blobName, cancellationToken))
                {
                    /*
                      An OPTIONAL entry that is absent is not a gap.

                      The listing exists so a recipient never has to guess whether an absent file was
                      withheld, never produced, or lost — and that only works while everything on it
                      is something the package was supposed to contain. The waiver evidence below is
                      a set of forty-five names of which a healthy book has none, and putting those
                      forty-five in `missing` would bury the two entries that actually mean something.
                    */
                    if (!optional)
                    {
                        missing.Add(zipPath);
                    }

                    continue;
                }

                await using var source = await blobStorage.DownloadAsync(blobName, cancellationToken);
                using var bytes = new MemoryStream();
                await source.CopyToAsync(bytes, cancellationToken);

                included.Add(await WriteAsync(archive, zipPath, bytes.ToArray(), status, cancellationToken));
            }

            // The files that live on the deployment rather than in the pack's storage: the locked
            // ICC profile the press files were converted through, the supplier's own contract
            // documents, the approved pose PNGs, and the font hash list. Audit §9 asks for the
            // inputs a delivery was judged against, and "they are in our repository" is not an
            // answer a supplier can check anything with.
            foreach (var (path, zipPath) in DeploymentFiles())
            {
                if (!File.Exists(path))
                {
                    missing.Add(zipPath);
                    continue;
                }

                included.Add(await WriteAsync(
                    archive, zipPath, await File.ReadAllBytesAsync(path, cancellationToken),
                    PackageStatus.Canonical, cancellationToken));
            }

            included.Add(await WriteAsync(
                archive, "assets/fonts/font-hashes.json", FontHashes(),
                PackageStatus.Canonical, cancellationToken));

            /*
              The release status, at the root, where somebody opening the zip sees it first.

              Amendment A5: the package always builds, so it has to say what it is. A zip that
              contains two press PDFs and no verdict is exactly what the supplier received and
              exactly what they could not act on — they had to derive the rejection themselves.
            */
            included.Add(await WriteAsync(
                archive, "RELEASE_STATUS.json", ReleaseStatus(packId, title, release),
                PackageStatus.Canonical, cancellationToken));

            included.Add(await WriteAsync(
                archive, "provenance.json",
                await ProvenanceAsync(userId, packId, cancellationToken),
                PackageStatus.Canonical, cancellationToken));

            // The listing itself, so the recipient never has to guess whether an absent file was
            // withheld, never produced, or lost — now with a checksum per entry, which is P1-10's
            // actual complaint: a package with no hashes cannot be shown to be the package that was
            // reviewed.
            var contents = archive.CreateEntry("PACKAGE_CONTENTS.json", CompressionLevel.Fastest);
            await using var writer = contents.Open();
            await JsonSerializer.SerializeAsync(
                writer,
                new
                {
                    schema = "beki-package-contents-v2",
                    pack_id = packId,
                    title,
                    assembled_at_utc = DateTime.UtcNow,
                    release_verdict = release?.Verdict ?? "UNKNOWN",
                    entries = included
                        .OrderBy(entry => entry.Path, StringComparer.Ordinal)
                        .ToList(),
                    missing = missing.OrderBy(path => path, StringComparer.Ordinal).ToList(),
                    excluded_by_design = new[]
                    {
                        "the child's photograph — a real child's face; the fulfilment manifest "
                        + "carries its secure reference and SHA-256 instead (audit amendment A7)",
                        "child-identity.json — the four appearance attributes read from that "
                        + "photograph; same rule, same reference-and-hash treatment",
                        "the licensed font binaries — Noto and Ottia may not be redistributed; "
                        + "assets/fonts/font-hashes.json carries the role, file name and SHA-256",
                    },
                },
                Readable,
                cancellationToken);
        }

        return buffer.ToArray();
    }

    // ==============================================================================================
    // What goes in
    // ==============================================================================================

    private enum PackageStatus
    {
        Canonical,
        Diagnostic,
    }

    /// <summary>
    /// One line of the contents listing. Named explicitly rather than by a serializer policy: the
    /// document around it is hand-written snake_case, and a block that disagreed with its neighbours
    /// is a block somebody has to read twice.
    /// </summary>
    private sealed record PackageEntry(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("bytes")] long Bytes,
        [property: JsonPropertyName("mime")] string Mime,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("status")] string Status);

    /// <summary>
    /// One blob this package may carry.
    /// </summary>
    /// <param name="Optional">
    /// Whether an absence is worth reporting. False for everything a finished book owes the package;
    /// true for the per-incident evidence a healthy book simply does not have.
    /// </param>
    private sealed record PackageBlob(
        string BlobName, string ZipPath, PackageStatus Status, bool Optional = false);

    /// <summary>
    /// Every blob this package may carry, with the folder it lands in.
    ///
    /// The three deliverables go to the root under the audit's own names, and go there only when the
    /// gates released them: a press PDF that failed PRESS_RESOLUTION is still worth having and is
    /// still not a deliverable, so it travels under <c>diagnostic/</c> where nobody can hand it to a
    /// printer by accident.
    /// </summary>
    private static IReadOnlyList<PackageBlob> BlobEntries(
        Guid userId, Guid packId, BekiReleaseGateReport? release)
    {
        /*
          THE RAW FAMILY, and only the raw family — amendment B1.

          These two booleans decide what a printer may be handed, and the release policy has no
          business in that decision. Under the owner's ruling a book whose RENDER_VALIDATION refused
          the reading copy is still published to the family who bought it; a package that read the
          same policy-filtered flag would then file that PDF at the root of the handback as a
          deliverable, and the supplier would receive a failing file presented as a passing one.

          So the parent's publication and the supplier's release are two questions with two answers,
          and this asks the supplier's. A file the policy published to a family and the gates refused
          travels under diagnostic/, and RELEASE_STATUS.json below says NOT_RELEASABLE, which is what
          the evidence supports.
        */
        var pressReleased = release?.SupplierPressReleasable == true;
        var digitalReleased = release?.SupplierCustomerPdfReleasable == true;

        string Deliverable(string name, bool released) =>
            released ? name : $"diagnostic/{name}";

        var entries = new List<PackageBlob>
        {
            new(BekiPackBlobs.CoverPdfName(userId, packId),
                Deliverable(PressCoverFileName(packId), pressReleased),
                pressReleased ? PackageStatus.Canonical : PackageStatus.Diagnostic),
            new(BekiPackBlobs.InteriorPdfName(userId, packId),
                Deliverable(PressInteriorFileName(packId), pressReleased),
                pressReleased ? PackageStatus.Canonical : PackageStatus.Diagnostic),
            new(BekiPackBlobs.ReadingPdfName(userId, packId),
                Deliverable(DigitalReadingFileName(packId), digitalReleased),
                digitalReleased ? PackageStatus.Canonical : PackageStatus.Diagnostic),

            new(BekiPackBlobs.InteriorPreflightName(userId, packId), "press/interior-preflight.json", PackageStatus.Canonical),
            new(BekiPackBlobs.CoverPreflightName(userId, packId), "press/cover-preflight.json", PackageStatus.Canonical),
            new(BekiPackBlobs.PressStatusName(userId, packId), "press/press-status.json", PackageStatus.Canonical),
            new(BekiPackBlobs.DigitalReportName(userId, packId), "digital/digital-preflight.json", PackageStatus.Canonical),

            // The single cover master and its paperwork (audit P0-01/P0-10).
            new(BekiPackBlobs.CoverWrapCompositeName(userId, packId), "cover/cover-wrap-composite.png", PackageStatus.Canonical),
            new(BekiPackBlobs.CoverWrapBaseName(userId, packId), "cover/cover-wrap-base.png", PackageStatus.Canonical),
            new(BekiPackBlobs.CoverCompositionName(userId, packId), "cover/cover-composition.json", PackageStatus.Canonical),
            new(BekiPackBlobs.CoverFrontName(userId, packId), "cover/cover-front.png", PackageStatus.Canonical),

            // The AI redraw, which is no longer anything's cover. It travels so that the two can be
            // compared, and it travels under diagnostic/ so that it cannot be mistaken for a master.
            new(BekiPackBlobs.CoverName(userId, packId), "diagnostic/cover-redraw.png", PackageStatus.Diagnostic),

            new(BekiPackBlobs.StoryName(userId, packId), "plan/story.json", PackageStatus.Canonical),
            new(BekiPackBlobs.ScenarioName(userId, packId), "plan/visual-scenario.json", PackageStatus.Canonical),
            new(BekiPackBlobs.CompositeReviewName(userId, packId), "plan/composite-review.json", PackageStatus.Canonical),
            new(BekiPackBlobs.ManifestName(userId, packId), "plan/fulfilment-manifest.json", PackageStatus.Canonical),
            new(BekiPackBlobs.TelemetryName(userId, packId), "plan/telemetry.json", PackageStatus.Canonical),

            new(BekiPackBlobs.AssetLockName(userId, packId), $"lock/{BekiAssetLock.ManifestFileName}", PackageStatus.Canonical),
            new(BekiPackBlobs.ReleaseGatesName(userId, packId), "gates/release-gates.json", PackageStatus.Canonical),
            new(BekiPackBlobs.HumanApprovalName(userId, packId), "gates/human-approval.json", PackageStatus.Canonical),
        };

        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            entries.Add(new(BekiPackBlobs.SpreadName(userId, packId, spread),
                $"spreads/spread-{spread:00}.png", PackageStatus.Canonical));
            entries.Add(new(BekiPackBlobs.SpreadBaseName(userId, packId, spread),
                $"bases/spread-{spread:00}-base.png", PackageStatus.Canonical));
            entries.Add(new(BekiPackBlobs.CompositionManifestName(userId, packId, spread),
                $"receipts/spread-{spread:00}-composition.json", PackageStatus.Canonical));
            entries.Add(new(BekiPackBlobs.SpreadQaName(userId, packId, spread),
                $"qa/spread-{spread:00}-qa.json", PackageStatus.Canonical));
            entries.Add(new(BekiPackBlobs.FailedSpreadName(userId, packId, spread),
                $"diagnostic/spread-{spread:00}-failed.png", PackageStatus.Diagnostic));
        }

        /*
          What the release policy waived, and the pictures it waived it on — amendment B4's evidence,
          which the package did not carry.

          Every waiver raises an alarm whose EvidenceBlob names one of these files, and the console's
          evidence button downloads THIS ZIP to satisfy it. So an operator clicking the button on the
          most common alarm in the system got a package with no such entry in it, and the answer to
          "show me the page we shipped anyway" was a 404 against a file that was sitting in storage
          the whole time. (Review finding 6.)

          RAW-only classification is preserved: these are diagnostics and are marked as such. They are
          not deliverables, they never become deliverables, and a supplier reading the package can
          tell at a glance that they are the record of a decision rather than part of the book.

          Enumerated rather than listed, because a blob store answers questions about names and not
          about prefixes. Page zero is the cover wrap; a healthy book has none of these, which is why
          they are optional and their absence is not reported as a gap.
        */
        foreach (var check in BekiReleaseChecks.Pipeline)
        {
            for (var page = 0; page <= BookFormat.SpreadCount; page++)
            {
                var stem = page == 0 ? "cover" : $"spread-{page:00}";

                entries.Add(new(
                    BekiPackBlobs.PolicyWaiverName(userId, packId, check, page),
                    $"diagnostic/waivers/{stem}-{check}.json",
                    PackageStatus.Diagnostic,
                    Optional: true));

                entries.Add(new(
                    BekiPackBlobs.WaivedEvidenceName(userId, packId, check, page),
                    $"diagnostic/waivers/{stem}-{check}.png",
                    PackageStatus.Diagnostic,
                    Optional: true));
            }
        }

        // The fixed pages' machine QA (D7): the cover boards, the endpapers, the intro, the credits.
        foreach (var role in BekiFixedPageQa.Roles)
        {
            entries.Add(new(BekiPackBlobs.FixedPageQaName(userId, packId, role),
                $"qa/fixed-{role}-qa.json", PackageStatus.Canonical));
        }

        // The post-layout receipts, per composed document and per page (amendment A4). These are the
        // only evidence that exists for where the words landed and whether the cream under them
        // cleared the fold.
        foreach (var mode in BekiPackBlobs.LayoutModes)
        {
            entries.Add(new(BekiPackBlobs.LayoutReceiptName(userId, packId, mode),
                $"receipts/{mode}-layout.json", PackageStatus.Canonical));

            // Fourteen is the longest of the three documents; a mode with fewer pages simply reports
            // the rest as missing, which is what the listing is for.
            for (var page = 1; page <= BookFormat.SpreadCount + 6; page++)
            {
                var fileName = $"page-{page:00}-layout.json";
                entries.Add(new(BekiPackBlobs.LayoutPageReceiptName(userId, packId, mode, fileName),
                    $"receipts/{mode}/{fileName}", PackageStatus.Canonical));
            }
        }

        // The render logs and the contact sheets the human approval is about (P2-6, amendment A2).
        foreach (var artifact in BekiPackBlobs.RenderedArtifacts)
        {
            entries.Add(new(BekiPackBlobs.RenderReportName(userId, packId, artifact),
                $"qa/render-{artifact}.json", PackageStatus.Canonical));
            entries.Add(new(BekiPackBlobs.ContactSheetName(userId, packId, artifact),
                $"qa/contact-sheet-{artifact}.png", PackageStatus.Canonical));
        }

        return entries;
    }

    /// <summary>
    /// The deployment's own files: the locked ICC profile, the supplier's contract documents, and
    /// the approved pose PNGs.
    /// </summary>
    private IEnumerable<(string Path, string ZipPath)> DeploymentFiles()
    {
        var root = AppContext.BaseDirectory;

        var icc = Path.IsPathRooted(bekiOptions.Value.PrintPrep.OutputIntentIccPath)
            ? bekiOptions.Value.PrintPrep.OutputIntentIccPath
            : Path.Combine(root, bekiOptions.Value.PrintPrep.OutputIntentIccPath);

        yield return (icc, $"assets/icc/{Path.GetFileName(icc)}");

        var contracts = Path.Combine(root, "Assets", "BekiComposite", "contracts");
        if (Directory.Exists(contracts))
        {
            foreach (var file in Directory
                         .EnumerateFiles(contracts)
                         .Where(file => file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                                        || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(file => file, StringComparer.Ordinal))
            {
                yield return (file, $"contracts/{Path.GetFileName(file)}");
            }
        }

        var poses = Path.Combine(root, "Assets", "BekiComposite", "poses");
        if (Directory.Exists(poses))
        {
            foreach (var file in Directory
                         .EnumerateFiles(poses, "*.png")
                         .OrderBy(file => file, StringComparer.Ordinal))
            {
                yield return (file, $"assets/poses/{Path.GetFileName(file)}");
            }
        }
    }

    /// <summary>
    /// The licensed faces, as roles and hashes.
    ///
    /// The binaries do not travel: Noto and Ottia are licensed to us and redistributing them inside
    /// a handback zip would be a licence breach dressed as diligence. What the supplier actually
    /// needs is the ability to check that the file we embedded is the file we said we embedded, and
    /// a SHA-256 does that.
    /// </summary>
    private static byte[] FontHashes()
    {
        object payload;

        try
        {
            var assets = BekiLayoutAssets.Current;

            payload = new
            {
                schema = "beki-font-hashes-v1",
                note = "Binaries excluded for licensing; roles, file names and SHA-256 only.",
                registry_version = assets.RegistryVersion,
                fonts = assets.Fonts
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new
                    {
                        role = pair.Value.Role ?? pair.Key,
                        id = pair.Key,
                        file = pair.Value.FileName,
                        sha256 = pair.Value.Sha256,
                    })
                    .ToList(),
            };
        }
        catch (Exception ex) when (ex is BekiLayoutException or IOException or InvalidOperationException)
        {
            payload = new
            {
                schema = "beki-font-hashes-v1",
                note = "Binaries excluded for licensing; roles, file names and SHA-256 only.",
                error = $"the layout asset registry could not be read: {ex.Message}",
            };
        }

        return JsonSerializer.SerializeToUtf8Bytes(payload, Readable);
    }

    private static byte[] ReleaseStatus(Guid packId, string? title, BekiReleaseGateReport? release) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                schema = "beki-release-status-v1",
                pack_id = packId,
                title,
                generated_at_utc = DateTime.UtcNow,
                verdict = release?.Verdict ?? "UNKNOWN",
                failing_gates = release?.FailingGates ?? ["release-gates.json is not stored for this book"],
                awaiting_human_review = release?.AwaitingHumanReview ?? true,
                contact_sheet_sha256 = release?.ContactSheetSha256,
                gates = release?.Gates,
                /*
                  What the supplier is being told, said in the supplier's own terms — and, separately,
                  what the family got.

                  Both are here because the honest thing to do with a divergence is to name it. A
                  reader of this document who found a book in the customer's library and a
                  NOT_RELEASABLE verdict in the package would otherwise be looking at what appears to
                  be a contradiction; it is not one, and the waiver list says who decided and about
                  which check.
                */
                supplier_release = new
                {
                    press_files = release?.SupplierPressReleasable ?? false,
                    customer_pdf = release?.SupplierCustomerPdfReleasable ?? false,
                    note = "Policy-blind. These are what this package's canonical/diagnostic "
                           + "classification is computed from.",
                },
                parent_publication = new
                {
                    press_files = release?.PressFilesMayPublish ?? false,
                    customer_pdf = release?.CustomerPdfMayPublish ?? false,
                    waivers = release?.PolicyWaivers ?? [],
                    note = "What this deployment's release policy allowed to reach the family who "
                           + "bought the book. Never a claim about the gates.",
                },
                note = release is null
                    ? "This book was fulfilled before the release gates existed, or its evaluation "
                      + "was never stored. Nothing in this package may be treated as released."
                    : "Files under diagnostic/ did not pass their gates and are not deliverables.",
            },
            Readable);

    /// <summary>
    /// How this book was built — audit §9's build provenance, which the rejected package had none of.
    ///
    /// The commit is read off the assembly's own informational version, which the csproj stamps from
    /// <c>git rev-parse HEAD</c>: a provenance file that recorded a version number nobody could map
    /// back to a tree would answer the question in form only.
    /// </summary>
    private async Task<byte[]> ProvenanceAsync(
        Guid userId, Guid packId, CancellationToken cancellationToken)
    {
        var assembly = typeof(BekiPackageExport).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        // "1.0.0+<sha>" is what the SDK writes once SourceRevisionId is set; a build without it
        // says so rather than inventing a commit.
        var plus = informational.IndexOf('+');
        var commit = plus >= 0 && plus + 1 < informational.Length
            ? informational[(plus + 1)..]
            : "unknown (no SourceRevisionId was stamped into this build)";

        var telemetry = await TryReadTextAsync(
            BekiPackBlobs.TelemetryName(userId, packId), cancellationToken);

        return JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                schema = "beki-provenance-v1",
                pack_id = packId,
                generated_at_utc = DateTime.UtcNow,
                build = new
                {
                    commit,
                    informational_version = informational,
                    runtime = Environment.Version.ToString(),
                    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                },
                models = new
                {
                    story = bekiOptions.Value.StoryGeneratorModel,
                    story_reviewer = bekiOptions.Value.StoryReviewerModel,
                    visual_scenario = bekiOptions.Value.VisualScenarioModel,
                    image = bekiOptions.Value.ImageModel,
                    image_reviewer = bekiOptions.Value.VisualReviewerModel,
                    identity = bekiOptions.Value.IdentityAnalyzerModel,
                },
                contract_versions = new
                {
                    story_prompt = MasterStoryPromptComposite.Version,
                    image_prompt = CompositeIllustrationPrompt.Version,
                    scenario_prompt = CompositeVisualScenarioPrompt.Version,
                    qa_prompt = CompositeMinimalQa.Version,
                    identity_prompt = CompositeChildIdentity.Version,
                    fixed_page_qa = BekiFixedPageQa.Version,
                    composition_manifest = BekiCompositionManifest.Version,
                    asset_lock = BekiAssetLock.ManifestVersion,
                    release_gates = BekiReleaseGates.Schema,
                    pdfx = BekiPrintPrep.PdfxVersion,
                },
                // The exact command lines Ghostscript and Poppler were invoked with, lifted out of
                // the reports they wrote. Audit §9 asks for them by name: a colour conversion that
                // cannot be reproduced is a colour conversion nobody can argue with.
                renderer_commands = await RendererCommandsAsync(userId, packId, cancellationToken),
                retries = RetryCounts(telemetry),
            },
            Readable);
    }

    private async Task<IReadOnlyList<object>> RendererCommandsAsync(
        Guid userId, Guid packId, CancellationToken cancellationToken)
    {
        var commands = new List<object>();

        var reports = BekiPackBlobs.RenderedArtifacts
            .Select(artifact => (artifact, name: BekiPackBlobs.RenderReportName(userId, packId, artifact)))
            .Concat(
            [
                ("press-interior-preflight", BekiPackBlobs.InteriorPreflightName(userId, packId)),
                ("press-cover-preflight", BekiPackBlobs.CoverPreflightName(userId, packId)),
                ("digital-preflight", BekiPackBlobs.DigitalReportName(userId, packId)),
            ]);

        foreach (var (artifact, name) in reports)
        {
            if (await TryReadTextAsync(name, cancellationToken) is not { Length: > 0 } json)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                foreach (var command in Commands(document.RootElement))
                {
                    commands.Add(new { artifact, command });
                }
            }
            catch (JsonException)
            {
                // A report nobody can parse contributes no command line, which is the same as a
                // report that never named one.
            }
        }

        return commands;

        static IEnumerable<string> Commands(JsonElement root)
        {
            if (root.TryGetProperty("renderers", out var renderers))
            {
                if (renderers.ValueKind == JsonValueKind.Array)
                {
                    foreach (var run in renderers.EnumerateArray())
                    {
                        if (run.TryGetProperty("command", out var command)
                            && command.ValueKind == JsonValueKind.String
                            && command.GetString() is { Length: > 0 } text)
                        {
                            yield return text;
                        }
                    }
                }
                else if (renderers.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in renderers.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String
                            && property.Value.GetString() is { Length: > 0 } text)
                        {
                            yield return text;
                        }
                    }
                }
            }

            if (root.TryGetProperty("colour", out var colour)
                && colour.TryGetProperty("conversion", out var conversion)
                && conversion.ValueKind == JsonValueKind.String
                && conversion.GetString() is { Length: > 0 } line)
            {
                yield return line;
            }

            if (root.TryGetProperty("linearization", out var linearization)
                && linearization.TryGetProperty("conversion", out var linear)
                && linear.ValueKind == JsonValueKind.String
                && linear.GetString() is { Length: > 0 } linearLine)
            {
                yield return linearLine;
            }
        }
    }

    /// <summary>How many image attempts this book cost, out of the telemetry it wrote.</summary>
    private static object RetryCounts(string? telemetryJson)
    {
        if (telemetryJson is not { Length: > 0 })
        {
            return new { note = "no telemetry is stored for this book" };
        }

        try
        {
            using var document = JsonDocument.Parse(telemetryJson);
            var root = document.RootElement;

            return new
            {
                total_image_attempts = Number(root, "totalImageAttempts"),
                accepted = Number(root, "acceptedCount"),
                needs_review = Number(root, "needsReviewCount"),
            };
        }
        catch (JsonException)
        {
            return new { note = "the stored telemetry could not be read" };
        }

        static int? Number(JsonElement root, string property) =>
            root.TryGetProperty(property, out var value) && value.TryGetInt32(out var number)
                ? number
                : null;
    }

    private static async Task<PackageEntry> WriteAsync(
        ZipArchive archive,
        string zipPath,
        byte[] bytes,
        PackageStatus status,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(zipPath, CompressionLevel.Fastest);
        await using var target = entry.Open();
        await target.WriteAsync(bytes, cancellationToken);

        return new PackageEntry(
            zipPath,
            bytes.Length,
            MimeFor(zipPath),
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            status == PackageStatus.Canonical ? "canonical" : "diagnostic");
    }

    private static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".json" => "application/json",
        ".md" => "text/markdown",
        ".icc" => "application/vnd.iccprofile",
        _ => "application/octet-stream",
    };

    private async Task<string?> TryReadTextAsync(string blobName, CancellationToken cancellationToken)
    {
        try
        {
            if (!await blobStorage.ExistsAsync(blobName, cancellationToken))
            {
                return null;
            }

            await using var stream = await blobStorage.DownloadAsync(blobName, cancellationToken);
            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
