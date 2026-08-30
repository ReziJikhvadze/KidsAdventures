using System.Text;
using AdventurePacks.Api.Services.Story.Composite.Poses;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// One page's pose decision, taken from the scenario alone and before any picture exists.
/// </summary>
/// <param name="Page">The spread number, or 0 for the cover.</param>
/// <param name="PoseId">The pose the registry selected.</param>
/// <param name="MatchedKeyword">The keyword that hit, or null on a fallback.</param>
/// <param name="Fallback">True when nothing matched and the neutral hover was selected.</param>
/// <param name="Action">The scenario's own sentence, kept so a log line explains itself.</param>
public sealed record CompositePoseChoice(
    int Page, string PoseId, string? MatchedKeyword, bool Fallback, string Action);

/// <summary>
/// What the registry made of a whole scenario's Beki sentences, before a single image is paid for.
/// </summary>
/// <param name="Choices">One row per story spread, in page order. The cover is not in it.</param>
/// <param name="CoverChoice">The cover's own row, advisory: no cover is composited on this path yet.</param>
public sealed record CompositePoseAudit(
    IReadOnlyList<CompositePoseChoice> Choices, CompositePoseChoice? CoverChoice)
{
    /// <summary>How many story spreads got the neutral hover because nothing matched.</summary>
    public int FallbackCount => Choices.Count(choice => choice.Fallback);

    /// <summary>How many distinct poses this book would actually show across its eight spreads.</summary>
    public int DistinctPoses =>
        Choices.Select(choice => choice.PoseId).Distinct(StringComparer.Ordinal).Count();

    /// <summary>The fallback pages, for the log line and the retry's error list.</summary>
    public IReadOnlyList<int> FallbackPages =>
        Choices.Where(choice => choice.Fallback).Select(choice => choice.Page).ToList();

    /// <summary>
    /// Whether this scenario would compose a book of mostly the same drawing.
    ///
    /// Two is the line the plan draws, and it is drawn where it is because a fallback is not an
    /// error — a sentence the table genuinely has no verb for is allowed to exist, and one or two
    /// neutral hovers in eight pages is a book. Six is the completed book that started this.
    /// </summary>
    public bool ExceedsFallbackBudget => FallbackCount > CompositePoseVocabulary.MaxFallbacksPerBook;
}

/// <summary>
/// The registry's verb vocabulary, read as a whole rather than one sentence at a time.
///
/// Two jobs, and they are the same fact seen from either end. Before the scenario is written, the
/// planner is told which verb families the pose table can actually see (<see cref="PromptBlock"/>) —
/// vocabulary steering, not a new task, and certainly not a model call to choose a pose. After the
/// scenario comes back, the same table is replayed over all eight sentences
/// (<see cref="Audit"/>), and a book whose sentences the table cannot read is caught while it still
/// costs one text call rather than nine images and a printed proof.
///
/// The families are stated here rather than derived from the JSON because a keyword list is not a
/// sentence a planner can be given: the registry holds "gestures encouragingly" and "traces the
/// path" beside "points", and reading them out would be a wall of text that steers nothing. What is
/// derived — and asserted by test — is that every family names a pose the registry prioritises and
/// that every exemplar verb printed into the prompt is genuinely one of that pose's keywords. So the
/// two cannot drift: a pose renamed or a keyword removed fails the build, not a book.
/// </summary>
public static class CompositePoseVocabulary
{
    /// <summary>
    /// How many neutral-hover fallbacks a book may contain before the scenario is treated as a
    /// semantic-validation miss and the one permitted retry is spent on it.
    /// </summary>
    public const int MaxFallbacksPerBook = 2;

    /// <summary>The cover's row uses this page number, which is not a spread.</summary>
    public const int CoverPage = 0;

    /// <summary>
    /// The nine families, in the registry's own <c>priority_order</c> and then the fallback, each
    /// with the two or three exemplar verbs the planner is shown.
    ///
    /// Exemplars, not the list: the point is to put the family's verb in the planner's head, and a
    /// planner that writes "Beki claps" instead of "Beki reacts positively" has done the whole job.
    /// </summary>
    public static readonly IReadOnlyList<(string PoseId, string Family, string[] Verbs)> Families =
    [
        ("pose_06_brave_protective", "protect", ["protects", "shields", "guards"]),
        ("pose_04_listen", "listen", ["listens", "hears", "attentive"]),
        ("pose_07_curious_lean", "wonder", ["curious", "wonder", "leans in", "peers"]),
        ("pose_03_guide_point", "point", ["points", "guides", "shows the way"]),
        ("pose_08_gentle_reassure", "reassure", ["reassures", "comforts", "stands beside", "nods"]),
        ("pose_05_excited_celebrate", "celebrate", ["celebrates", "claps", "cheers"]),
        ("pose_09_forward_adventure_glide", "travel onward", ["glides forward", "walks beside", "leads onward"]),
        ("pose_02_welcome_invitation", "welcome", ["welcomes", "invites", "beckons"]),
        ("pose_01_neutral_hover", "hover (no verb matched)", []),
    ];

    /// <summary>
    /// The steering block appended to the Visual Scenario system instruction.
    ///
    /// It asks for the family's verb and nothing else. Every other line in that instruction still
    /// holds — one concise sentence, Beki named, no pose id, no body, no page position — and this
    /// one only says which words the deterministic table downstream is able to read. The planner is
    /// explicitly told the scene stays natural, because the alternative failure is eight sentences
    /// that all say "Beki points" and a book of one pose for the opposite reason.
    /// </summary>
    public static string PromptBlock()
    {
        var builder = new StringBuilder();

        builder.AppendLine("BEKI ACTION VOCABULARY");
        builder.AppendLine();
        builder.AppendLine(
            "Code matches each beki_action against a fixed table of nine approved poses, by verb. A "
            + "sentence whose verb is not in the table gets a neutral hovering pose, so a book "
            + "written in words the table cannot read is a book in which Beki does the same thing on "
            + "every page.");
        builder.AppendLine();
        builder.AppendLine("Phrase each beki_action around one of these nine verb families:");
        builder.AppendLine();

        foreach (var (_, family, verbs) in Families.Where(entry => entry.Verbs.Length > 0))
        {
            builder.AppendLine($"- {family}: {string.Join(", ", verbs)}");
        }

        builder.AppendLine();
        builder.AppendLine(
            "Use the family that the story page actually calls for, in a natural sentence — do not "
            + "force a beat the page does not contain, and do not reuse one family for the whole "
            + "book. Prefer the plain verb (\"Beki claps\", \"Beki gazes in wonder\", \"Beki stands "
            + "beside the child\") over an abstract paraphrase. This is wording guidance only: never "
            + "name a pose, a pose id, or a page position.");

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Replays the registry over a validated scenario, deterministically and with no model call.
    ///
    /// The same <see cref="BekiPoseSelector"/> the pipeline uses per page, run over all of them at
    /// once — not a second implementation of the matching rule, because a check that disagreed with
    /// the thing it is checking would be worse than no check.
    /// </summary>
    public static CompositePoseAudit Audit(BekiPoseRegistry registry, VisualScenarioV2 scenario)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(scenario);

        var choices = (scenario.Spreads ?? [])
            .Select(spread => Choose(registry, spread.Page, spread.BekiAction))
            .ToList();

        var cover = scenario.Cover?.BekiAction is { Length: > 0 } coverAction
            ? Choose(registry, CoverPage, coverAction)
            : null;

        return new CompositePoseAudit(choices, cover);
    }

    private static CompositePoseChoice Choose(BekiPoseRegistry registry, int page, string? action)
    {
        var selection = BekiPoseSelector.Select(registry, action);

        return new CompositePoseChoice(
            page, selection.PoseId, selection.MatchedKeyword, selection.Fallback,
            action?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// The retry's error list entry: which pages the table could not read, quoted, and what to do
    /// about them.
    ///
    /// Written as one problem rather than one per page because it is one fault — the scenario is
    /// phrased in vocabulary the pose table does not carry — and because the retry message is read
    /// by a model that answers better to a single instruction with evidence than to five copies of
    /// it.
    /// </summary>
    public static VisualScenarioProblem Problem(CompositePoseAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);

        var quoted = string.Join(
            " ",
            audit.Choices
                .Where(choice => choice.Fallback)
                .Select(choice => $"Spread {choice.Page}: \"{choice.Action}\"."));

        var families = string.Join(
            ", ",
            Families.Where(entry => entry.Verbs.Length > 0).Select(entry => entry.Family));

        return new VisualScenarioProblem(
            VisualScenarioProblemCodes.PoseVocabularyMiss,
            $"{audit.FallbackCount} of {audit.Choices.Count} beki_action sentences use a verb the "
            + $"approved pose table cannot read, so Beki would be drawn in the same neutral hovering "
            + $"pose on {audit.FallbackCount} spreads. {quoted} Rewrite those sentences — and only "
            + $"those — around one of the nine verb families ({families}), keeping each page's own "
            + "story beat, the same one concise sentence, and Beki named. Do not change any "
            + "child_world_scene, the visual_lock, or the cover.");
    }
}
