using System.Text.Json;
using System.Text.RegularExpressions;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// One known-bad pattern found in a book's Georgian copy, with enough context for a person to
/// decide what to do about it.
/// </summary>
/// <param name="RuleId">The checklist entry that fired.</param>
/// <param name="Location">Where it was found — <c>title</c> or <c>spread 4</c>.</param>
/// <param name="Found">The exact text that matched, as it appears in the book.</param>
/// <param name="Expected">The checklist's note on what was probably meant.</param>
/// <param name="Excerpt">
/// A short window of the sentence around the match. Short deliberately: this travels into logs, and
/// a log line is the artifact most likely to be pasted into a chat window — the book's prose is the
/// customer's, and a rule that quoted a paragraph would put it there.
/// </param>
public sealed record GeorgianTextFlag(
    string RuleId, string Location, string Found, string Expected, string Excerpt)
{
    public override string ToString() =>
        $"{RuleId} in {Location}: \"{Found}\" (expected {Expected}) — …{Excerpt}…";
}

/// <summary>
/// A small deterministic reading of the Georgian a book is about to print, against a list of
/// mistakes that have actually shipped.
///
/// It flags and never fixes, and that is the whole design rather than caution. Two reasons.
///
/// The correction belongs to the polish pass. That call reads the book with its meaning in front of
/// it and can tell ეწყოს from იყოს in context; a substring rule cannot, and a substring rule that
/// rewrote text would be an editor with no idea what the sentence says. What this is for is the case
/// the polish missed — which is not hypothetical, it is the reason the file exists: a shipped book
/// carried ფუნღუროში and თემო-ს past every stage that could have caught them.
///
/// And a silent repair hides the miss. The value of a flag is that somebody reads it and then goes
/// and fixes the polish prompt, or the name templating, or the checklist. A book quietly corrected
/// on its way out teaches nobody anything, and the same defect ships in the next one.
///
/// The rules live in <c>Assets/BekiComposite/georgian_text_checklist_v1.json</c> — a config file
/// beside the other supplied assets, so adding a pattern after the next proof is a data change
/// somebody can review in a diff, not a deployment of new code.
///
/// **It can never fail a book, and that includes failing on its own configuration.** A rule with an
/// invalid regular expression, a missing field, or a pattern that runs away is skipped, recorded by
/// id and reason, and the remaining rules still run; a checklist file that is missing or unreadable
/// disables the check and says so on the book's record. The alternative was the shape of the defect:
/// an advisory check whose broken config threw out of <c>Inspect</c> and failed every composite book
/// in the deployment until somebody fixed an asset that was only ever there to add a note.
/// </summary>
public static class CompositeGeorgianCheck
{
    /// <summary>The asset, beside the pose registry and the theme registry.</summary>
    public const string ChecklistFileName = "georgian_text_checklist_v1.json";

    /// <summary>What <see cref="ChecklistVersion"/> reads when nothing could be loaded.</summary>
    public const string UnreadableVersion = "unreadable";

    private static readonly Lazy<GeorgianChecklist> Default = new(() => GeorgianChecklist.Load());

    /// <summary>The checklist's own version string, recorded beside the flags it produced.</summary>
    public static string ChecklistVersion => Default.Value.Version;

    /// <summary>
    /// Why one or more rules are not running, by id — an empty list when the whole file loaded.
    ///
    /// Surfaced rather than swallowed, and surfaced as data rather than as a log line, because a
    /// book whose check-list was half broken is a book that was half checked. The pipeline logs
    /// these once per book and writes them onto the book's review, so "no flags" cannot quietly mean
    /// "no rules ran".
    /// </summary>
    public static IReadOnlyList<string> RuleProblems => Default.Value.Problems;

    /// <summary>
    /// Reads a plan's Georgian title and spread text against the checklist.
    ///
    /// The plan and not the scenario: the scenario is English, written for an image model, and the
    /// Georgian a child will actually read lives on <see cref="MasterStory"/>. <c>textEn</c> is not
    /// read at all — the composite path has none, and an English-language rule in a Georgian
    /// checklist would be a category error.
    /// </summary>
    public static IReadOnlyList<GeorgianTextFlag> Inspect(MasterStory? plan) =>
        Default.Value.Inspect(plan);

    /// <summary>
    /// The same reading, applied to one string. Exposed for the tests and for any caller that holds
    /// a single piece of copy rather than a whole plan.
    /// </summary>
    public static IReadOnlyList<GeorgianTextFlag> InspectText(string? text, string location) =>
        Default.Value.InspectText(text, location);
}

/// <summary>
/// One loaded check-list: the rules that compiled, the reasons the others did not, and the reading
/// they perform.
///
/// An instance rather than a static, for the reason <see cref="Poses.BekiPoseRegistry"/> is one: the
/// tests need to point at a doctored asset tree — a checklist with a deliberately broken rule in it
/// is the only way to prove that a broken rule is survivable — and production simply uses the
/// cached default.
/// </summary>
public sealed class GeorgianChecklist
{
    /// <summary>
    /// How much of the surrounding sentence a flag quotes, either side of the match.
    /// </summary>
    private const int ExcerptRadius = 24;

    /// <summary>
    /// A regex rule gets a hard timeout rather than trust.
    ///
    /// The patterns come from a config file that operators are invited to extend, so one of them
    /// will eventually be written badly, and the failure mode of a catastrophically backtracking
    /// pattern is a generation job that never finishes and a book that never arrives. A timeout
    /// turns that into one skipped rule.
    /// </summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private readonly IReadOnlyList<ChecklistRule> _rules;

    private GeorgianChecklist(string version, IReadOnlyList<ChecklistRule> rules, IReadOnlyList<string> problems)
    {
        Version = version;
        _rules = rules;
        Problems = problems;
    }

    /// <summary>The file's own version string, or <see cref="CompositeGeorgianCheck.UnreadableVersion"/>.</summary>
    public string Version { get; }

    /// <summary>The rules that will actually run.</summary>
    public int RuleCount => _rules.Count;

    /// <summary>Why any rule — or the whole file — is not running. Empty when everything loaded.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>
    /// Loads the checklist, degrading rather than throwing at every step.
    /// </summary>
    /// <param name="baseDirectory">
    /// For tests pointing at a doctored asset tree; production passes nothing and gets the published
    /// output directory.
    /// </param>
    public static GeorgianChecklist Load(string? baseDirectory = null)
    {
        var path = Path.Combine(
            baseDirectory ?? AppContext.BaseDirectory,
            "Assets", "BekiComposite", CompositeGeorgianCheck.ChecklistFileName);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            /*
              A missing or unreadable checklist disables the check and says so.

              Not a thrown deployment fault, which is what the asset registries around it do,
              because those decide what gets printed and this one only decides whether a note is
              written. But not silence either — a book recorded as "no Georgian flags" when in fact
              no rule ever ran is the same lie the supplier complained about in another form, so the
              reason lands on the book's own record.

              The type rather than the message: a file-system message can carry a path.
            */
            return new GeorgianChecklist(
                CompositeGeorgianCheck.UnreadableVersion,
                [],
                [$"the checklist could not be read ({ex.GetType().Name}); no Georgian rule ran."]);
        }

        using (document)
        {
            var rules = new List<ChecklistRule>();
            var problems = new List<string>();

            var version = document.RootElement.TryGetProperty("checklist_version", out var configured)
                          && configured.ValueKind == JsonValueKind.String
                ? configured.GetString() ?? CompositeGeorgianCheck.ChecklistFileName
                : CompositeGeorgianCheck.ChecklistFileName;

            if (!document.RootElement.TryGetProperty("rules", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                return new GeorgianChecklist(
                    version, [], ["the checklist has no rules array; no Georgian rule ran."]);
            }

            var index = 0;

            foreach (var entry in entries.EnumerateArray())
            {
                // Per rule, so one bad entry costs one rule rather than the whole file. The id is
                // read first and defensively, because it is what makes the problem actionable —
                // "rule 3" sends somebody counting array entries.
                var id = Text(entry, "id") ?? $"rule[{index}]";
                index++;

                var kind = Text(entry, "kind");
                var pattern = Text(entry, "pattern");

                if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(pattern))
                {
                    problems.Add($"{id}: skipped — it has no kind or no pattern.");
                    continue;
                }

                var expected = Text(entry, "expected") ?? string.Empty;

                if (!string.Equals(kind, "regex", StringComparison.Ordinal))
                {
                    rules.Add(new ChecklistRule(id, expected, pattern!, null));
                    continue;
                }

                try
                {
                    rules.Add(new ChecklistRule(
                        id, expected, pattern!,
                        new Regex(pattern!, RegexOptions.CultureInvariant, RegexTimeout)));
                }
                catch (ArgumentException ex)
                {
                    // An invalid pattern is the operator's typo, not this book's problem. The
                    // message is the regex engine's own and names the fault in the pattern.
                    problems.Add($"{id}: skipped — the regular expression is invalid ({ex.Message}).");
                }
            }

            if (rules.Count == 0 && problems.Count == 0)
            {
                problems.Add("the checklist lists no rules; no Georgian rule ran.");
            }

            return new GeorgianChecklist(version, rules, problems);
        }
    }

    /// <inheritdoc cref="CompositeGeorgianCheck.Inspect"/>
    public IReadOnlyList<GeorgianTextFlag> Inspect(MasterStory? plan)
    {
        if (plan is null)
        {
            return [];
        }

        var flags = new List<GeorgianTextFlag>();

        Read(plan.Concept?.Title, "title", flags);

        foreach (var spread in plan.Spreads ?? [])
        {
            Read(spread.Text, $"spread {spread.Number}", flags);
        }

        return flags;
    }

    /// <inheritdoc cref="CompositeGeorgianCheck.InspectText"/>
    public IReadOnlyList<GeorgianTextFlag> InspectText(string? text, string location)
    {
        var flags = new List<GeorgianTextFlag>();
        Read(text, location, flags);
        return flags;
    }

    private void Read(string? text, string location, List<GeorgianTextFlag> flags)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var rule in _rules)
        {
            // One flag per rule per location, not one per occurrence. A name-suffix bug hits every
            // sentence on the page, and eight identical lines about spread 4 would bury the one
            // rule that fired once — the reviewer is being told which page to open, not counted at.
            var match = rule.FirstMatch(text!);

            if (match is null)
            {
                continue;
            }

            flags.Add(new GeorgianTextFlag(
                rule.Id, location, match.Value.Text, rule.Expected,
                Excerpt(text!, match.Value.Index, match.Value.Text.Length)));
        }
    }

    private static string Excerpt(string text, int index, int length)
    {
        var start = Math.Max(0, index - ExcerptRadius);
        var end = Math.Min(text.Length, index + length + ExcerptRadius);

        return text[start..end].Replace('\n', ' ').Trim();
    }

    private static string? Text(JsonElement entry, string property) =>
        entry.ValueKind == JsonValueKind.Object
        && entry.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record ChecklistRule(string Id, string Expected, string Pattern, Regex? Compiled)
    {
        public (string Text, int Index)? FirstMatch(string text)
        {
            if (Compiled is null)
            {
                // Ordinal and case-sensitive: Georgian is caseless, so there is no folding to do,
                // and an invariant comparison would only add ways for a pattern to match something
                // its author did not write.
                var index = text.IndexOf(Pattern, StringComparison.Ordinal);
                return index < 0 ? null : (Pattern, index);
            }

            try
            {
                var match = Compiled.Match(text);
                return match.Success ? (match.Value, match.Index) : null;
            }
            catch (RegexMatchTimeoutException)
            {
                // A rule that cannot finish is a rule that did not fire. Never an exception out of
                // here: this check exists to add information to a book, and it may not be the reason
                // a paid book fails.
                return null;
            }
        }
    }
}
