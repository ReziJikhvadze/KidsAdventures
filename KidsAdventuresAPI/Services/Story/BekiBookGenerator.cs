using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story.Prompts;

namespace AdventurePacks.Api.Services.Story;

/// <summary>What one Beki illustration came out as, and what it cost to get there.</summary>
public sealed record BekiImageResult
{
    /// <summary>null for the cover; 1-based for a spread.</summary>
    public int? SpreadNumber { get; init; }

    public required byte[] Image { get; init; }
    public required bool Accepted { get; init; }

    /// <summary>The model's own words, unparsed. Kept because it is the point of running this.</summary>
    public required string Verdict { get; init; }

    /// <summary>1 means it passed first time.</summary>
    public required int Attempts { get; init; }

    public required string Prompt { get; init; }

    /// <summary>Which characters this image was drawn from an anchor for, rather than a description.</summary>
    public IReadOnlyList<string> AnchoredCharacters { get; init; } = [];
}

public sealed record BekiBookResult
{
    public required MasterStory Plan { get; init; }
    public required string AppearanceDescription { get; init; }
    public required BekiImageResult Cover { get; init; }
    public required IReadOnlyList<BekiImageResult> Spreads { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IBekiBookGenerator
{
    Task<BekiBookResult> GenerateAsync(
        MasterStoryInput input,
        byte[] childPhoto,
        string childPhotoContentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Draws a plan that already exists — the fulfilment path, where the story was written at
    /// preview time and the parent has already read it. Writing another plan here would replace
    /// the story they chose to buy, which is the exact fault preview adoption exists to prevent.
    /// When <paramref name="existingCover"/> is given it is adopted instead of drawn, for the
    /// same reason: the cover the parent decided on is the cover they get.
    /// </summary>
    /// <param name="onImage">
    /// Called once per finished illustration, in drawing order, before the next one starts.
    /// The book takes minutes and a parent is watching a spinner; this is how the pictures
    /// reach them while the rest are still being drawn. Null when nobody is watching. Not called
    /// for a spread adopted from <paramref name="existingSpreads"/> — it was already delivered
    /// the run that drew it.
    /// </param>
    /// <param name="existingSpreads">
    /// Accepted artwork a previous, interrupted attempt at this same book already produced,
    /// keyed by spread number. Resuming a fulfilment job redraws only the spreads missing from
    /// here — the rest are adopted outright, with no second review and no second bill. Null (the
    /// default) means nothing survives from an earlier attempt, which is every caller but the
    /// resumable fulfilment job.
    /// </param>
    Task<BekiBookResult> IllustrateAsync(
        MasterStory plan,
        byte[] childPhoto,
        string childPhotoContentType,
        byte[]? existingCover,
        Func<BekiImageResult, Task>? onImage,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, byte[]>? existingSpreads = null);

    /// <summary>
    /// The Beki cover alone: the child and the Beki master reference, QA-reviewed exactly as
    /// fulfilment draws it. Exposed so a preview can carry the same cover the parent will get if
    /// they buy the book, instead of the legacy single-reference cover the A5 flow has always
    /// drawn. There is no warnings list here for a single image to report into — a missing Beki
    /// reference or a refused review surface through the result's own
    /// <see cref="BekiImageResult.Verdict"/> and through this generator's logger instead.
    /// </summary>
    Task<BekiImageResult> DrawCoverAsync(
        MasterStory plan, byte[] childPhoto, string childPhotoContentType, CancellationToken cancellationToken);
}

/// <summary>
/// One Beki-format book, start to finish: plan, cover, eight spreads, each reviewed.
///
/// The handoff's whole architecture is three model tasks and a rule about what each image is
/// allowed to know, and this class is mostly that rule. An image call receives the child's
/// photograph, the child's fixed appearance, this spread's scene, the continuity it needs and
/// nothing else — not the story, not the other spreads, not the extra wish, not a dimension or a
/// typography rule. Everything a picture must not be told is simply never assembled here.
///
/// Spreads are drawn in order rather than in parallel, which is slower and deliberate: a
/// character's anchor is the first accepted image it appeared in, so spread 5 cannot be started
/// until it is known whether spread 2 gave char_01 a face. Parallelism would mean drawing every
/// early appearance from description and losing the continuity the anchors exist for.
///
/// It generates and reviews. It does not lay out, store, bill or persist anything — the layout
/// and the printed format are a separate decision that has not been taken yet, and a generator
/// that quietly wrote files would be harder to run twice while that decision is pending.
/// </summary>
public sealed class BekiBookGenerator(
    IStoryModelClient storyClient,
    IOpenAiService openAi,
    IOptions<BekiPrintLayoutOptions> printLayoutOptions,
    ILogger<BekiBookGenerator> logger) : IBekiBookGenerator
{
    /// <summary>
    /// Landscape, until the 2.2:1 spread is decided. gpt-image offers three shapes and none of
    /// them is 440×200, so this is the closest that is not a distortion; the final framing is a
    /// layout question rather than a generation one.
    /// </summary>
    public const string SpreadImageSize = "1536x1024";

    /// <summary>
    /// The handoff allows the original plus two retries. One, for now: the first measured run
    /// showed a correction adding a second copy of a character that the reviewer then failed to
    /// notice, so a second automatic retry buys another chance to make it worse.
    /// </summary>
    public const int MaxRegenerations = 1;

    private const string BekiReferencePath = "Assets/Beki/beki-canonical-v1.png";

    private readonly BekiPrintLayoutOptions _layout = printLayoutOptions.Value;

    /// <summary>
    /// Read once per generator instance and kept: the file never changes mid-run, and a book
    /// draws it up to nine times — the cover and every spread that carries Beki.
    /// </summary>
    private byte[]? _cachedBekiReference;
    private bool _bekiReferenceLoadAttempted;

    /// <summary>
    /// QA must judge what the reader will see, not pixels the print crop discards.
    /// <see cref="BekiPdfComposer"/> centre-crops every render to its sheet at layout time, so
    /// the reviewer is shown that same crop rather than the full 3:2 provider frame — a face the
    /// print will trim away should not be able to fail a review, and a face the print keeps
    /// should not be able to hide from one behind a background band that never ships.
    /// </summary>
    private float SpreadCropRatio =>
        (_layout.SpreadWidthMm + (_layout.BleedMm * 2)) / (_layout.SpreadHeightMm + (_layout.BleedMm * 2));

    /// <summary>The cover prints as a single leaf, half the spread — see <see cref="SpreadCropRatio"/>.</summary>
    private float CoverCropRatio =>
        (_layout.PageWidthMm + (_layout.BleedMm * 2)) / (_layout.SpreadHeightMm + (_layout.BleedMm * 2));

    public async Task<BekiBookResult> GenerateAsync(
        MasterStoryInput input,
        byte[] childPhoto,
        string childPhotoContentType,
        CancellationToken cancellationToken)
    {
        // The child's appearance is read from the photograph, exactly as the A5 flow reads it.
        // The planner needs it for characterLock; it never sees the photograph itself.
        var appearance = await openAi.DescribeCharacterFromPhotoAsync(
            childPhoto, childPhotoContentType, MasterStoryPrompt.PhotoDescribe, cancellationToken);

        var plan = await PlanAsync(input with { AppearanceDescription = appearance }, cancellationToken);

        logger.LogInformation(
            "Beki plan \"{Title}\": {Spreads} spreads, {Cast} recurring character(s).",
            plan.Concept.Title, plan.Spreads.Count, plan.Cast?.Count ?? 0);

        var book = await IllustrateAsync(plan, childPhoto, childPhotoContentType, null, null, cancellationToken);
        return book with { AppearanceDescription = appearance };
    }

    public async Task<BekiBookResult> IllustrateAsync(
        MasterStory plan,
        byte[] childPhoto,
        string childPhotoContentType,
        byte[]? existingCover,
        Func<BekiImageResult, Task>? onImage,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, byte[]>? existingSpreads = null)
    {
        var warnings = new List<string>();
        var adopted = existingSpreads ?? new Dictionary<int, byte[]>();

        var castById = (plan.Cast ?? []).ToDictionary(member => member.Id, StringComparer.OrdinalIgnoreCase);
        var anchors = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /*
          Adopted spreads anchor first, before a single new picture is drawn.

          A resumed run redraws only the holes a previous attempt left; every adopted spread is
          already part of the book the parent will read, so a redrawn hole has to be drawn against
          it, not against nothing. Anchoring here — ahead of the loop below — means a redrawn
          spread 3 sees char_01's face from an adopted spread 2 exactly as it would if spread 2
          had just been drawn in this same run.
        */
        foreach (var spread in plan.Spreads.OrderBy(s => s.Number))
        {
            if (!adopted.TryGetValue(spread.Number, out var adoptedImage))
            {
                continue;
            }

            foreach (var id in spread.Characters ?? [])
            {
                if (castById.ContainsKey(id) && !anchors.ContainsKey(id))
                {
                    anchors[id] = adoptedImage;
                }
            }
        }

        var cover = existingCover is null
            ? await DrawCoverAsync(plan, childPhoto, childPhotoContentType, warnings, cancellationToken)
            : new BekiImageResult
            {
                Image = existingCover,
                Accepted = true,
                Verdict = "Adopted from the preview the parent chose; not drawn here.",
                Attempts = 0,
                Prompt = string.Empty,
            };

        var spreads = new List<BekiImageResult>(plan.Spreads.Count);
        foreach (var spread in plan.Spreads.OrderBy(s => s.Number))
        {
            BekiImageResult result;

            if (adopted.TryGetValue(spread.Number, out var adoptedImage))
            {
                // Already accepted, already stored, already anchored above. Redrawing it would
                // cost a generation for a picture the parent has effectively already seen.
                result = new BekiImageResult
                {
                    SpreadNumber = spread.Number,
                    Image = adoptedImage,
                    Accepted = true,
                    Verdict = "Adopted from a previous run's accepted artwork.",
                    Attempts = 0,
                    Prompt = string.Empty,
                };

                spreads.Add(result);
                continue;
            }

            result = await DrawSpreadAsync(
                plan, spread, castById, anchors, childPhoto, childPhotoContentType, warnings, cancellationToken);

            spreads.Add(result);

            if (onImage is not null)
            {
                await onImage(result);
            }

            /*
              The continuity rule, and the reason spreads are sequential.

              An accepted image becomes the anchor for every character in it that did not already
              have one. Unaccepted images are not promoted: an anchor is what every later
              appearance is matched against, so a bad one does not stay one picture's problem.
            */
            if (result.Accepted)
            {
                foreach (var id in spread.Characters ?? [])
                {
                    if (castById.ContainsKey(id) && !anchors.ContainsKey(id))
                    {
                        anchors[id] = result.Image;
                        logger.LogInformation(
                            "Beki: spread {Spread} is now the anchor for {Character}.", spread.Number, id);
                    }
                }
            }
            else
            {
                warnings.Add($"Spread {spread.Number} shipped as NEEDS_REVIEW after {result.Attempts} attempt(s).");
            }
        }

        var unanchored = castById.Keys.Where(id => !anchors.ContainsKey(id)).ToList();
        if (unanchored.Count > 0)
        {
            warnings.Add(
                $"No accepted image ever established an anchor for: {string.Join(", ", unanchored)}. "
                + "Those characters were drawn from their description every time.");
        }

        return new BekiBookResult
        {
            Plan = plan,
            // Only planning reads the photograph's description; an illustrate-only run never
            // produced one. GenerateAsync stamps its own on before returning.
            AppearanceDescription = string.Empty,
            Cover = cover,
            Spreads = spreads,
            Warnings = warnings,
        };
    }

    private async Task<MasterStory> PlanAsync(MasterStoryInput input, CancellationToken cancellationToken)
    {
        var result = await storyClient.CompleteAsync<MasterStory>(
            "gpt-5.6-sol",
            MasterStoryPromptV5.System(input),
            MasterStoryPromptV5.User(input),
            BekiBookPlanSchema.Name,
            BekiBookPlanSchema.Build(input.SpreadCount),
            cancellationToken);

        var plan = result.Value;
        if (plan.Spreads.Count != input.SpreadCount)
        {
            throw new InvalidOperationException(
                $"The Beki planner returned {plan.Spreads.Count} spreads, expected {input.SpreadCount}.");
        }

        return plan;
    }

    /// <summary>
    /// <inheritdoc cref="IBekiBookGenerator.DrawCoverAsync" />
    /// </summary>
    public async Task<BekiImageResult> DrawCoverAsync(
        MasterStory plan, byte[] childPhoto, string childPhotoContentType, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var result = await DrawCoverAsync(plan, childPhoto, childPhotoContentType, warnings, cancellationToken);

        // The public entry point has nobody to hand a book-wide warnings list to — a single cover
        // call is not part of a book being assembled — so whatever IllustrateAsync would have
        // folded into BekiBookResult.Warnings is logged here instead.
        foreach (var warning in warnings)
        {
            logger.LogWarning("Beki cover: {Warning}", warning);
        }

        return result;
    }

    /// <summary>
    /// The cover, which is the one image with two references: the child and Beki.
    ///
    /// Its subject is the relationship rather than a scene from the story — the handoff asks for
    /// the child as the hero with Beki beside them, warm and lovable — so the shot instruction is
    /// written here rather than taken from the spread rhythm, and no text side is reserved: a
    /// title is typeset over the cover later and is never drawn.
    /// </summary>
    private async Task<BekiImageResult> DrawCoverAsync(
        MasterStory plan,
        byte[] childPhoto,
        string childPhotoContentType,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var beki = LoadBekiReference(warnings, "the cover");

        var prompt = IllustrationPrompt.ComposeBeki(
            plan.CharacterLock,
            plan.Cover.Scene,
            beki is null
                ? string.Empty
                : "Include Beki, the story's guide, exactly as shown in the provided Beki reference; "
                  + "Beki stands with the child as a warm, lovable companion and never in front of them.",
            // The cover has no story text over it, so no side is reserved; "either" reads as a
            // free composition to the model rather than as a constraint it must satisfy.
            "either",
            "A warm hero portrait of the child in this world, inviting the reader in.",
            plan.Cover.Avoid);

        var references = new List<(byte[] Bytes, string ContentType, string Label)>
        {
            (childPhoto, childPhotoContentType, "Child reference photograph"),
        };

        if (beki is not null)
        {
            references.Add((beki, "image/png", "Beki master reference — the sole authority for Beki's design"));
        }

        return await DrawReviewedAsync(
            null, plan.Cover.Scene, "either", prompt, references, [], cancellationToken);
    }

    private async Task<BekiImageResult> DrawSpreadAsync(
        MasterStory plan,
        StorySpread spread,
        IReadOnlyDictionary<string, StoryCastMember> castById,
        IReadOnlyDictionary<string, byte[]> anchors,
        byte[] childPhoto,
        string childPhotoContentType,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var textSide = BekiSpreadRhythm.TextSideFor(spread.Number);
        var shot = BekiSpreadRhythm.ShotFor(spread.Number);

        var present = (spread.Characters ?? [])
            .Where(castById.ContainsKey)
            .ToList();

        var references = new List<(byte[] Bytes, string ContentType, string Label)>
        {
            (childPhoto, childPhotoContentType, "Child reference photograph"),
        };

        /*
          Each recurring character is described the first time and shown every time after.

          Both halves matter. A character introduced by description and then never anchored drifts
          between spreads; a character anchored but not named in the prompt leaves the model to
          guess which of the attached pictures it is looking at.
        */
        var continuity = new List<string>();
        var anchored = new List<string>();

        foreach (var id in present)
        {
            var member = castById[id];
            if (anchors.TryGetValue(id, out var anchorBytes))
            {
                /*
                  An anchored character keeps its description in the prompt.

                  Dropping it was how a spread carrying two anchors came back with the same
                  creature drawn twice. The two references went in as files whose names both began
                  "Continuity reference for" and were cut to the same 24 characters, and the prompt
                  said only "keep ფაფუ identical to its reference" and "keep ლურჯფრთა identical to
                  its reference" — nothing in the request told the two apart, so the model picked
                  one design and used it for both.

                  The label is now the character's name alone, so the filename carries it, and the
                  description stays in the sentence so the model can tell which attached picture is
                  being talked about even if the labels never reach it.
                */
                references.Add((anchorBytes, "image/png", member.Name));
                continuity.Add(
                    $"{member.Name} — {member.VisualDescription} — appears again here: keep "
                    + "it identical to its own continuity reference, and do not give any other "
                    + "character its design.");
                anchored.Add(id);
            }
            else
            {
                continuity.Add($"Include {member.Name}: {member.VisualDescription}");
            }
        }

        /*
          Beki is not a cast member — there is no entry for it in castById, and no
          visualDescription to fall back on, because the canonical design lives in the reference
          image alone. So it gets its own branch rather than joining the loop above: every spread
          that carries Beki attaches the same master reference, described the same way, every
          time — there is no "first appearance" for a character who is in almost every spread.
        */
        if ((spread.Characters ?? []).Any(id => id.Equals(BekiPlanValidator.BekiId, StringComparison.OrdinalIgnoreCase)))
        {
            var beki = LoadBekiReference(warnings, $"spread {spread.Number}");
            if (beki is not null)
            {
                references.Add((beki, "image/png", "Beki master reference — the sole authority for Beki's design"));
                continuity.Add(
                    "Beki appears in this scene: include Beki exactly as shown in the Beki master "
                    + "reference, as the child's warm companion — never leading the action, and "
                    + "never duplicated.");
            }
        }

        var prompt = IllustrationPrompt.ComposeBeki(
            plan.CharacterLock,
            spread.Illustration.Scene,
            string.Join("\n", continuity),
            textSide,
            shot,
            spread.Illustration.Avoid);

        return await DrawReviewedAsync(
            spread.Number, spread.Illustration.Scene, textSide, prompt, references, anchored, cancellationToken);
    }

    /// <summary>
    /// Draw, review, and on a refusal redraw once with the reviewer's own words appended.
    ///
    /// The original prompt is kept whole and the correction added to it, as the handoff requires:
    /// a rewritten prompt is a different picture, and the point of a retry is the same picture
    /// without the fault. An image that never passes is returned anyway, marked — a book with a
    /// flawed spread can be looked at and judged; a book with a hole cannot.
    /// </summary>
    private async Task<BekiImageResult> DrawReviewedAsync(
        int? spreadNumber,
        string scene,
        string textSide,
        string prompt,
        IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
        IReadOnlyList<string> anchored,
        CancellationToken cancellationToken)
    {
        var label = spreadNumber is null ? "cover" : $"spread {spreadNumber}";
        var reference = BekiImageReferences.ToStoryImageReference(references);

        // QA must judge what the reader will see, not pixels the print crop discards — a copy
        // only, cropped to the sheet this image will actually be printed on. The stored/returned
        // image below stays the full provider frame; BekiPdfComposer does its own crop at layout
        // time.
        var reviewRatio = spreadNumber is null ? CoverCropRatio : SpreadCropRatio;

        var image = await openAi.GenerateStoryImageAsync(
            prompt, reference, cancellationToken, SpreadImageSize);

        var verdict = await ReviewAsync(
            SpreadArtCrop.CropToRatio(image, reviewRatio), scene, textSide, references, cancellationToken);
        var attempts = 1;

        while (!IsPass(verdict) && attempts <= MaxRegenerations)
        {
            logger.LogInformation("Beki {Label} refused by QA; redrawing. {Verdict}", label, verdict);

            var corrected = $"{prompt}\n\n{Corrections(verdict)}";
            image = await openAi.GenerateStoryImageAsync(
                corrected, reference, cancellationToken, SpreadImageSize);

            verdict = await ReviewAsync(
                SpreadArtCrop.CropToRatio(image, reviewRatio), scene, textSide, references, cancellationToken);
            attempts++;
        }

        return new BekiImageResult
        {
            SpreadNumber = spreadNumber,
            Image = image,
            Accepted = IsPass(verdict),
            Verdict = verdict,
            Attempts = attempts,
            Prompt = prompt,
            AnchoredCharacters = anchored,
        };
    }

    private Task<string> ReviewAsync(
        byte[] image,
        string scene,
        string textSide,
        IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
        CancellationToken cancellationToken) =>
        openAi.ReviewIllustrationAsync(
            image, BekiImageQaPrompt.For(scene, textSide), references, cancellationToken);

    /// <summary>
    /// Forgiving about the wrapper, strict about the answer.
    ///
    /// A verdict inside a code fence is still a verdict — <see cref="ModelJsonSanitizer"/> pulls
    /// the object out the same way <see cref="Corrections"/> does — but once it parses, only an
    /// exact <c>"status":"PASS"</c> counts. A plain substring search used to accept a verdict that
    /// merely mentioned PASS anywhere, including inside an issue explaining why something did NOT
    /// pass; parsing the field is the difference between reading the verdict and reading around
    /// it. The substring check survives only as the fallback for a verdict whose JSON will not
    /// parse at all — and even then, anything unrecognisable stays a failure.
    /// </summary>
    private static bool IsPass(string verdict)
    {
        var json = ModelJsonSanitizer.ExtractJsonObject(verdict);
        if (string.IsNullOrWhiteSpace(json))
        {
            return verdict.Contains("\"PASS\"", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && string.Equals(status.GetString(), "PASS", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return verdict.Contains("\"PASS\"", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The faults out of the verdict, and nothing else.
    ///
    /// The whole JSON blob used to be pasted onto the prompt, and the reviewer does not fill
    /// `issues` with faults alone. A real refusal read: "no unwanted text, lettering, logo, frame
    /// or QR code is present", "no Beki character is present, which is correct", and then, buried
    /// among them, the one thing actually wrong. The redraw was being asked to correct seven
    /// observations that were already right — five retries across two runs fixed nothing, and one
    /// added a duplicate character.
    ///
    /// So the array is parsed out and the items that report an absence of a problem are dropped,
    /// leaving a short list of instructions. When the verdict will not parse the raw text is used
    /// rather than nothing: a noisy correction still beats redrawing with no correction at all.
    /// </summary>
    internal static string Corrections(string verdict)
    {
        var json = ModelJsonSanitizer.ExtractJsonObject(verdict);
        if (string.IsNullOrWhiteSpace(json))
        {
            return $"Correct these problems from the previous attempt: {verdict.Trim()}";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("issues", out var issues)
                || issues.ValueKind != JsonValueKind.Array)
            {
                return $"Correct these problems from the previous attempt: {verdict.Trim()}";
            }

            var faults = issues
                .EnumerateArray()
                .Select(issue => issue.GetString())
                .Where(issue => !string.IsNullOrWhiteSpace(issue))
                .Select(issue => issue!.Trim())
                .Where(IsFault)
                .ToList();

            if (faults.Count == 0)
            {
                return "The previous attempt was refused. Follow the composition rules above exactly.";
            }

            var numbered = string.Join("\n", faults.Select((fault, index) => $"{index + 1}. {fault}"));
            return "The previous attempt was refused for these reasons. Draw it again, the same "
                + $"scene, with each of them fixed:\n{numbered}";
        }
        catch (JsonException)
        {
            return $"Correct these problems from the previous attempt: {verdict.Trim()}";
        }
    }

    /// <summary>
    /// True for an item that names something wrong. The reviewer writes its clean findings in the
    /// same list as its faults — "there is no unwanted text", "no Beki is present, which is
    /// correct" — and those are the ones a redraw must not be handed.
    /// </summary>
    private static bool IsFault(string issue)
    {
        var text = issue.ToLowerInvariant();

        // "no X is present/visible", "there is no unwanted …" — an absence being reported as fine.
        if (text.StartsWith("no ", StringComparison.Ordinal)
            || text.StartsWith("there is no ", StringComparison.Ordinal)
            || text.StartsWith("there are no ", StringComparison.Ordinal))
        {
            // Unless it goes on to say the absence is itself the problem.
            return text.Contains("but ", StringComparison.Ordinal)
                || text.Contains("as requested", StringComparison.Ordinal)
                || text.Contains("required", StringComparison.Ordinal);
        }

        /*
          Approvals the reviewer files under issues anyway: "…is clear and correct", "…which is
          correct", "not applicable". A fault can also contain the word correct — "the colour is
          not correct" — so a bare search for it would throw away real findings, and the negations
          are checked first.

          This is a heuristic over prose and it will not be exactly right. It errs towards keeping
          an item: a redraw handed one observation too many is a worse prompt, while a redraw
          handed one fault too few is a spread that stays broken.
        */
        var negated = text.Contains("not correct", StringComparison.Ordinal)
            || text.Contains("incorrect", StringComparison.Ordinal)
            || text.Contains("does not", StringComparison.Ordinal)
            || text.Contains("but ", StringComparison.Ordinal);

        if (negated) return true;

        return !text.Contains("is correct", StringComparison.Ordinal)
            && !text.Contains("and correct", StringComparison.Ordinal)
            && !text.Contains("are correct", StringComparison.Ordinal)
            && !text.Contains("matches the request", StringComparison.Ordinal)
            && !text.Contains("not applicable", StringComparison.Ordinal)
            && !text.Contains("no issue", StringComparison.Ordinal);
    }

    /// <summary>
    /// Beki's canonical picture. A missing file is a warning rather than a failure — an
    /// illustration drawn from description alone is worse than one drawn from the reference, and
    /// better than no book.
    ///
    /// The file read happens at most once per generator instance — a book can ask for it up to
    /// nine times, for the cover and every spread that carries Beki, and it never changes
    /// mid-run — but a warning is still added on every call that needed it and did not get it,
    /// because <paramref name="warnings"/> is a different list for a single preview cover than
    /// it is for a whole book, and each caller needs to know its own picture went without.
    /// </summary>
    private byte[]? LoadBekiReference(List<string> warnings, string context)
    {
        if (!_bekiReferenceLoadAttempted)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, BekiReferencePath);
                if (File.Exists(path))
                {
                    _cachedBekiReference = File.ReadAllBytes(path);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read the Beki master reference.");
            }

            _bekiReferenceLoadAttempted = true;
        }

        if (_cachedBekiReference is null)
        {
            warnings.Add($"Beki master reference not found; {context} was drawn without it.");
        }

        return _cachedBekiReference;
    }
}
