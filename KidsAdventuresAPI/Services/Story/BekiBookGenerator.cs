using System.Text.Json;
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

    public async Task<BekiBookResult> GenerateAsync(
        MasterStoryInput input,
        byte[] childPhoto,
        string childPhotoContentType,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        // The child's appearance is read from the photograph, exactly as the A5 flow reads it.
        // The planner needs it for characterLock; it never sees the photograph itself.
        var appearance = await openAi.DescribeCharacterFromPhotoAsync(
            childPhoto, childPhotoContentType, MasterStoryPrompt.PhotoDescribe, cancellationToken);

        var plan = await PlanAsync(input with { AppearanceDescription = appearance }, cancellationToken);

        logger.LogInformation(
            "Beki plan \"{Title}\": {Spreads} spreads, {Cast} recurring character(s).",
            plan.Concept.Title, plan.Spreads.Count, plan.Cast?.Count ?? 0);

        var castById = (plan.Cast ?? []).ToDictionary(member => member.Id, StringComparer.OrdinalIgnoreCase);
        var anchors = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        var cover = await DrawCoverAsync(plan, childPhoto, childPhotoContentType, warnings, cancellationToken);

        var spreads = new List<BekiImageResult>(plan.Spreads.Count);
        foreach (var spread in plan.Spreads.OrderBy(s => s.Number))
        {
            var result = await DrawSpreadAsync(
                plan, spread, castById, anchors, childPhoto, childPhotoContentType, cancellationToken);

            spreads.Add(result);

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
            AppearanceDescription = appearance,
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
        var beki = LoadBekiReference(warnings);

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

        var image = await openAi.GenerateStoryImageAsync(
            prompt, reference, cancellationToken, SpreadImageSize);

        var verdict = await ReviewAsync(image, scene, textSide, references, cancellationToken);
        var attempts = 1;

        while (!IsPass(verdict) && attempts <= MaxRegenerations)
        {
            logger.LogInformation("Beki {Label} refused by QA; redrawing. {Verdict}", label, verdict);

            var corrected = $"{prompt}\n\n{Corrections(verdict)}";
            image = await openAi.GenerateStoryImageAsync(
                corrected, reference, cancellationToken, SpreadImageSize);

            verdict = await ReviewAsync(image, scene, textSide, references, cancellationToken);
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
    /// Forgiving about the wrapper, strict about the answer: a verdict inside a code fence is
    /// still a verdict, and anything that is not recognisably a pass is a failure.
    /// </summary>
    private static bool IsPass(string verdict) =>
        verdict.Contains("\"PASS\"", StringComparison.OrdinalIgnoreCase);

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
    /// Beki's canonical picture. A missing file is a warning rather than a failure — a cover
    /// drawn from the description alone is worse than one drawn from the reference, and better
    /// than no book.
    /// </summary>
    private byte[]? LoadBekiReference(List<string> warnings)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, BekiReferencePath);
            if (File.Exists(path)) return File.ReadAllBytes(path);

            warnings.Add("Beki master reference not found; the cover was drawn without it.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the Beki master reference.");
            warnings.Add("Beki master reference could not be read; the cover was drawn without it.");
            return null;
        }
    }
}
