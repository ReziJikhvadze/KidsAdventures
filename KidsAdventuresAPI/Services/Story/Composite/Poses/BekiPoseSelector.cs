using System.Text;

namespace AdventurePacks.Api.Services.Story.Composite.Poses;

/// <summary>
/// Chooses which approved pose a spread gets, from the scenario's <c>beki_action</c> sentence and
/// nothing else.
///
/// This is a pure function over a table, and that is the whole point. The obvious alternative —
/// asking a model "which of these nine poses fits this sentence?" — is one more call that can be
/// slow, can be wrong, can be differently wrong on a retry, and costs money per spread to answer a
/// question a keyword list already answers. The handoff says it in as many words: never make
/// another model call only to choose a pose. So the same sentence always yields the same pose, a
/// book can be re-composited months later and come out identical, and an operator can predict the
/// choice by reading the registry.
///
/// The price is that it misses. A sentence with no listed keyword falls to the neutral hover, and
/// the caller is told so via <see cref="BekiPoseSelection.Fallback"/> so it lands in the log rather
/// than passing as a real match — a book quietly composited from eight neutral hovers is a
/// scenario-prompt problem, and it is only visible if the fallbacks are counted.
/// </summary>
public static class BekiPoseSelector
{
    /// <summary>
    /// Picks a pose for a story spread or the cover from its action sentence.
    ///
    /// Poses are considered in the registry's <c>priority_order</c>, not in pose order, and within a
    /// pose its keywords are considered in list order. Both orders are load-bearing: an action that
    /// says Beki bravely shields the child while welcoming them must resolve to the protective pose
    /// and not the invitation, because "protecting" is what the picture has to show.
    /// </summary>
    public static BekiPoseSelection Select(BekiPoseRegistry registry, string? bekiAction)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var normalized = Normalize(bekiAction);

        if (normalized.Length > 0)
        {
            foreach (var poseId in registry.PriorityOrder)
            {
                foreach (var keyword in registry.Pose(poseId).Keywords)
                {
                    // Ordinal rather than culture-aware: the comparison has to give the same answer
                    // on an App Service in any locale, and a Turkish-locale 'i' deciding whether a
                    // book gets the listening pose is exactly the sort of bug that never reproduces.
                    if (normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return new BekiPoseSelection(poseId, keyword, Fallback: false);
                    }
                }
            }
        }

        return new BekiPoseSelection(registry.FallbackPoseId, MatchedKeyword: null, Fallback: true);
    }

    /// <summary>
    /// The intro spread's pose, which is fixed by the registry's <c>forced_usage</c> and never
    /// derived from text — the intro has no scenario action to read, and the partners approved one
    /// specific lean for that page's proof.
    ///
    /// Not a fallback: nothing failed to match, so the flag stays false and the log stays honest.
    /// </summary>
    public static BekiPoseSelection SelectForIntro(BekiPoseRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return new BekiPoseSelection(registry.IntroPoseId, MatchedKeyword: null, Fallback: false);
    }

    /// <summary>
    /// The registry's own normalization rule — "lowercase Unicode text; trim; collapse whitespace" —
    /// and only that rule.
    ///
    /// It is exposed because the tests assert against it directly and because a caller logging why a
    /// pose was chosen should log the string that was actually matched, not the raw sentence. No
    /// punctuation stripping and no diacritic folding: the registry does not ask for them, and
    /// adding either would silently change which books get which pose.
    /// </summary>
    public static string Normalize(string? bekiAction)
    {
        if (string.IsNullOrWhiteSpace(bekiAction))
        {
            return string.Empty;
        }

        // Invariant lowering rather than the current culture's, for the same reason the match is
        // ordinal: the answer must not depend on where the process happens to be running.
        var lowered = bekiAction.ToLowerInvariant();

        var builder = new StringBuilder(lowered.Length);
        var pendingSpace = false;
        foreach (var c in lowered)
        {
            if (char.IsWhiteSpace(c))
            {
                // Collapse and trim fall out of one pass: a run of whitespace becomes at most one
                // space, and a space is only ever emitted once a non-space follows it, so leading
                // and trailing runs never reach the output at all.
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}

/// <summary>
/// What the selector decided, and enough of why for the log the handoff's observability contract
/// asks for.
/// </summary>
/// <param name="PoseId">The approved pose to composite.</param>
/// <param name="MatchedKeyword">
/// The registry keyword that hit, or null when nothing did (a fallback) or nothing was consulted
/// (a forced context such as the intro).
/// </param>
/// <param name="Fallback">
/// True only when the action was read and matched nothing — the <c>pose_selection_fallback=true</c>
/// the handoff wants logged. A forced pose is not a fallback.
/// </param>
public sealed record BekiPoseSelection(string PoseId, string? MatchedKeyword, bool Fallback);
