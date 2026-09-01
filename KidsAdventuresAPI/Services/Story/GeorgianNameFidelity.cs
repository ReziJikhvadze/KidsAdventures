using System.Text;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// One thing wrong with how a book spells the child it was written for.
/// </summary>
/// <param name="Kind">
/// <see cref="NearMiss"/>, <see cref="AbsentFromTitle"/> or <see cref="AbsentFromBook"/> — the three
/// shapes the defect takes, kept apart because the correction each one asks the planner for is
/// different.
/// </param>
/// <param name="Location">Where it was found, in the words the rest of this pipeline uses:
/// <c>title</c>, <c>spread 4</c>, <c>the book</c>.</param>
/// <param name="Spread">
/// The spread number, or 0 for the title and for anything that is true of the whole book. It is the
/// same numbering the pipeline's evidence and alarms use, so a waived problem lands where somebody
/// looking for it will look.
/// </param>
/// <param name="Found">The word the book actually printed. Empty when the fault is an absence.</param>
/// <param name="Expected">The child's name exactly as the parent typed it.</param>
public sealed record NameFidelityProblem(
    string Kind, string Location, int Spread, string Found, string Expected)
{
    /// <summary>A word that is one letter away from the child's name, and is not the name.</summary>
    public const string NearMiss = "near_miss";

    /// <summary>The title mangles the name and never spells it correctly.</summary>
    public const string AbsentFromTitle = "absent_from_title";

    /// <summary>The child is never named anywhere in their own book.</summary>
    public const string AbsentFromBook = "absent_from_book";

    /// <summary>
    /// The problem as the corrective retry is sent it — a sentence, not a rule id.
    ///
    /// It names the exact name, the exact word that was written instead, and the obligation, because
    /// this string travels straight into the numbered correction note the planner receives and a
    /// model cannot fix what it has only been told the category of. The declension examples are there
    /// because the first thing a model does when told "use the exact name" is stop declining it, and
    /// a book that says „ვეკო მიდის“ where Georgian wants „ვეკოს“ is a different defect in the same
    /// place.
    /// </summary>
    public override string ToString() => Kind switch
    {
        NearMiss =>
            $"The child's name is „{Expected}“, exactly — the {Location} wrote „{Found}“. Every "
            + $"mention of the child, in the title and on every spread, must be the exact name "
            + $"„{Expected}“, letter for letter. Georgian case endings may follow it — "
            + $"{Declensions(Expected)} — but the letters of the name itself never change.",

        AbsentFromTitle =>
            $"The title spells the child's name wrongly and never spells it correctly. The child's "
            + $"name is „{Expected}“: a title that names the hero must name „{Expected}“, letter for "
            + "letter (a case ending may follow it), and a title that does not name the hero at all "
            + "is fine.",

        _ =>
            $"The child is never named in their own book. The child's name is „{Expected}“, and it "
            + "must appear — spelled exactly, letter for letter — at least once in the story text.",
    };

    /// <summary>
    /// Three of the endings Georgian actually adds, built from this book's own name so the example
    /// is about this child rather than about ვეკო.
    /// </summary>
    private static string Declensions(string name) =>
        $"„{name}ს“, „{name}მ“, „{name}სთვის“";
}

/// <summary>
/// A deterministic reading of whether a book spells the child's name the way the parent wrote it.
///
/// **The observed defect, 2026-09-01.** A live composite run for a child called ვეკო came back
/// titled „ველო და მოციმციმე ტყე“. One Georgian letter — კ became ლ — in the child's own name, in
/// the title, and nothing in the system looked at it. The title is canonical: it goes onto the
/// cover, into the pack row and into the PDF's metadata, so the first thing that family would have
/// seen of the book they paid to personalise is their child's name misspelled.
///
/// Nothing upstream could have caught it. The Georgian checklist
/// (<see cref="Composite.CompositeGeorgianCheck"/>) matches known-bad substrings from a config file,
/// and the child's name is not knowable in advance — it is an input. The plan validators check the
/// cast, the spread count and Beki's spelling, none of which is the child. And the polish pass is
/// another model reading Georgian prose: it has no reason to think ველო is wrong, because in the
/// only place it could check, the name is spelled the same way in the title and nowhere else.
///
/// So this is a comparison against the input instead of against a dictionary, which is the one form
/// of check that can be right about a name nobody has ever seen.
///
/// **What it looks for.** Georgian declines by suffix — ვეკოს, ვეკომ, ვეკოსთვის are all the same
/// name — so every comparison is made on a token's leading len(name) characters, and a token whose
/// prefix IS the name is the name, however it is suffixed. A token whose prefix is one edit away
/// from the name and is not the name is a near miss: that is the defect, and it is reported with the
/// word and the page.
///
/// **And the near miss is only asked of names long enough to be asked it of.** One edit away from a
/// three-letter name is an ordinary word of the language rather than a misspelling — „Ana“ against
/// "and" is the case that proved it, on a healthy book this check was rejecting. See
/// <see cref="ShortestNearMissName"/>; the exact-name and absence rules run from three letters up,
/// unchanged.
///
/// **What it never does is repair.** The same reasoning as the Georgian checklist's, sharpened by
/// the fact that this one could: replacing ველო with ვეკო everywhere would produce a correct-looking
/// book whose sentences were written around a different word, and would hide from everybody that the
/// planner cannot spell the name it was given. The answer is the corrective retry the plan validation
/// already owns, and then the release policy.
/// </summary>
public static class GeorgianNameFidelity
{
    /// <summary>
    /// Names shorter than this are not checked at all.
    ///
    /// Below three letters even the exact-name rules are noise: a two-letter string appears as the
    /// prefix of so many ordinary words that "the book names the child" would be true of every book
    /// ever written, and the check would be asserting nothing while looking as though it asserted
    /// something. At three the exact-name and absence rules are worth having; the NEAR-MISS rule is
    /// not, and that boundary is <see cref="ShortestNearMissName"/>.
    /// </summary>
    public const int ShortestCheckableName = 3;

    /// <summary>
    /// Names shorter than this get the exact-name and absence rules only — no near-miss detection.
    ///
    /// **Review finding 2.** Distance-1 prefix matching on a three-letter name is a false-positive
    /// generator, and the example that found it is the plainest one imaginable: a book for a child
    /// called „Ana“ whose text says "Ana and Beki" reports „and“ as a near miss of „Ana“ — one
    /// substitution, same first letter, every guard satisfied. name_fidelity is a BLOCKER by default,
    /// so that healthy book is replanned, replanned again, and then refused. The check was not
    /// catching a misspelling; it was catching the English word "and", and it would have done so on
    /// every book for every three-letter name in the catalogue.
    ///
    /// Three letters is simply not enough signal. One edit is a third of the name, and the space of
    /// three-letter strings a language actually uses is dense enough that a neighbour is nearly
    /// always a real word — ანა/ანი/აია, Ana/and/any. At four letters an edit is a quarter of the
    /// name and the neighbourhood thins out sharply, which is where the rule starts being about
    /// spelling rather than about the dictionary.
    ///
    /// What the boundary costs is a genuinely misspelled three-letter name, and that is a real loss
    /// stated plainly rather than argued away: ანა written ანი on a spread is not caught. What it
    /// buys is that the observed defect is still caught — ვეკო is four characters, ველო is one edit
    /// from it, and it is reported exactly as before — while healthy books stop being rejected. A
    /// blocker that fires on correct books is worse than no blocker, because it is turned off.
    ///
    /// The absence rules are untouched at three letters: a book that never names ანა at all is still
    /// a book that was not personalised, and that reading needs no neighbourhood.
    /// </summary>
    public const int ShortestNearMissName = 4;

    /// <summary>
    /// The companion's Georgian name, exempt from being read as a misspelling of anybody.
    ///
    /// „ბეკი“ is on every page of every book by contract, and it is written there by
    /// <see cref="BekiIdentityRules.EnforceBrandSpelling"/> rather than chosen. A child called ბეკა
    /// or ბექი would otherwise collect eight near-miss problems per book against a word this system
    /// itself put there — the check would be reporting our own brand rule as a defect in the story.
    /// </summary>
    private const string CompanionName = "ბეკი";

    /// <summary>
    /// Everything wrong with how <paramref name="story"/> spells <paramref name="childName"/>.
    /// Empty means the book names the child, and never nearly names them.
    /// </summary>
    public static IReadOnlyList<NameFidelityProblem> Inspect(MasterStory? story, string? childName)
    {
        if (story is null)
        {
            return [];
        }

        var expected = (childName ?? string.Empty).Trim();
        var name = GivenName(Fold(expected));

        if (name.Length < ShortestCheckableName)
        {
            return [];
        }

        var exempt = Exemptions(story, name);
        var problems = new List<NameFidelityProblem>();

        var exactSomewhere = false;
        var exactInTitle = false;
        var nearMissInTitle = false;

        void Read(string? text, string location, int spread, bool isTitle)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            // One problem per distinct misspelling per location. A name written wrongly is written
            // wrongly in every sentence of the page, and six identical lines about spread 4 bury the
            // one page that is actually different.
            var reported = new HashSet<string>(StringComparer.Ordinal);

            foreach (var token in Tokens(Fold(text!)))
            {
                var prefix = Prefix(token, name.Length);
                var distance = Distance(prefix, name);

                if (distance == 0)
                {
                    exactSomewhere = true;
                    exactInTitle |= isTitle;
                    continue;
                }

                if (distance > 1)
                {
                    continue;
                }

                /*
                  From here down the token is a NEAR MISS candidate, and everything below decides
                  whether it is one. A three-letter name never gets this far — see
                  ShortestNearMissName, and review finding 2's "Ana and Beki".
                */
                if (name.Length < ShortestNearMissName)
                {
                    continue;
                }

                /*
                  A misspelling of a Georgian name is written in Georgian.

                  Stated as its own rule rather than left to the first-character check below, which
                  happens to imply it today: ვეკო and Veko are not one edit apart in any alphabet, so
                  a cross-script near miss is a comparison that has gone wrong somewhere upstream —
                  a half-transliterated token, a name folded into the wrong script by a form field —
                  and reporting it as "the book misspelled the child" would send the planner a
                  correction about a defect that is not in the story. The rule the first-character
                  guard enforces is about typos; this one is about what alphabet the book is in, and
                  the two should not depend on each other.
                */
                if (!string.Equals(ScriptOf(prefix), ScriptOf(name), StringComparison.Ordinal))
                {
                    continue;
                }

                /*
                  The first character has to survive.

                  Without it a short name matches the opening of a large fraction of the language —
                  ნინა against ნიორი — and the check would spend its credibility on books that are
                  fine. What it costs is the first-letter typo, ვეკო written ბეკო,
                  which this will not see. That is the right side of the trade: a check that fires on
                  healthy books stops being read, and a wrong first letter is the one form of this
                  defect a parent notices instantly on the cover.
                */
                if (prefix.Length == 0 || prefix[0] != name[0])
                {
                    continue;
                }

                if (exempt.Any(other => token.StartsWith(other, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (!reported.Add(token))
                {
                    continue;
                }

                nearMissInTitle |= isTitle;

                problems.Add(new NameFidelityProblem(
                    NameFidelityProblem.NearMiss, location, spread, token, expected));
            }
        }

        Read(story.Concept?.Title, "title", 0, isTitle: true);

        // The English half, where a book has one. It is secondary in every sense — the composite
        // format has none at all — but an A5 book prints it, and a name misspelled in the English
        // title is the same defect in the other language.
        Read(story.TitleEn, "English title", 0, isTitle: true);

        foreach (var spread in story.Spreads ?? [])
        {
            Read(spread.Text, $"spread {spread.Number}", spread.Number, isTitle: false);
            Read(spread.TextEn, $"spread {spread.Number} (English)", spread.Number, isTitle: false);
        }

        /*
          And the two absences.

          A title that does not name the hero is a good title — „მოციმციმე ტყე“ names nobody and is
          exactly the kind of title this prompt asks for. What is never acceptable is a title that
          reaches for the name and misses, which is why the title's obligation is conditional on a
          near miss being in it. The book's obligation is not conditional: a personalised book in
          which the child is never named is not personalised.
        */
        if (!exactSomewhere)
        {
            problems.Add(new NameFidelityProblem(
                NameFidelityProblem.AbsentFromBook, "the book", 0, string.Empty, expected));
        }

        if (nearMissInTitle && !exactInTitle)
        {
            problems.Add(new NameFidelityProblem(
                NameFidelityProblem.AbsentFromTitle, "title", 0, string.Empty, expected));
        }

        return problems;
    }

    /// <summary>
    /// The same reading as the sentences the corrective retry is sent — see
    /// <see cref="NameFidelityProblem.ToString"/> for why they are sentences.
    /// </summary>
    public static IReadOnlyList<string> Problems(MasterStory? story, string? childName) =>
        Inspect(story, childName).Select(problem => problem.ToString()).ToList();

    /// <summary>
    /// The words this book has declared to be somebody else's name, plus the companion's.
    ///
    /// A cast member or a story object whose name happens to sit one letter from the child's is NOT
    /// exempt, and that asymmetry is deliberate: a book whose hero is ვეკო and whose fox is ველო is
    /// either the same defect wearing a cast entry or a book no child could follow, and both are
    /// worth a second attempt. Everything further away — ბაფუ in a book about ნინა — is simply a
    /// different word, and reading it as a misspelling would be a bug in this file.
    /// </summary>
    private static HashSet<string> Exemptions(MasterStory story, string name)
    {
        var exempt = new HashSet<string>(StringComparer.Ordinal) { Fold(CompanionName) };

        foreach (var member in story.Cast ?? [])
        {
            Consider(member.Name);
        }

        foreach (var item in story.Objects ?? [])
        {
            Consider(item.Name);
        }

        return exempt;

        void Consider(string? other)
        {
            foreach (var token in Tokens(Fold((other ?? string.Empty).Trim())))
            {
                if (token.Length >= 2 && Distance(Prefix(token, name.Length), name) > 1)
                {
                    exempt.Add(token);
                }
            }
        }
    }

    /// <summary>
    /// The name under test, when the parent typed more than one word.
    ///
    /// The first word: it is the given name in every form this product collects, and matching the
    /// whole string would mean tokenising the book by phrase rather than by word, which no other
    /// check here does. A middle or family name spelled wrongly is not caught, and that is a smaller
    /// wrong than not catching the given name at all.
    /// </summary>
    private static string GivenName(string folded)
    {
        foreach (var token in Tokens(folded))
        {
            return token;
        }

        return string.Empty;
    }

    /// <summary>
    /// Case- and width-folded, which for Georgian is not the no-op it looks like: Mtavruli
    /// (U+1C90–U+1CBA) is a real casing of Mkhedruli that a model can and does emit, and a
    /// compatibility form can arrive from anywhere a name has been through a form field.
    /// </summary>
    private static string Fold(string value) =>
        value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    private static string Prefix(string token, int length) =>
        token.Length <= length ? token : token[..length];

    /// <summary>
    /// Which alphabet a word is written in, decided by its first letter — <c>georgian</c>,
    /// <c>latin</c>, <c>other</c>, or <c>none</c> for a token with no letter in it at all.
    ///
    /// The first letter rather than a survey of the whole token, because a name is one word in one
    /// script and a token that starts Georgian and continues Latin is not a spelling of anything.
    /// All four Georgian scripts count as Georgian: Mkhedruli is what a book is set in, Asomtavruli
    /// and Nuskhuri arrive from copy-paste, and Mtavruli (U+1C90–U+1CBA) is a real casing a model
    /// emits — reading a Mtavruli spelling of the name as a different alphabet would turn correct
    /// books into cross-script mismatches.
    /// </summary>
    private static string ScriptOf(string token)
    {
        foreach (var character in token)
        {
            if (!char.IsLetter(character))
            {
                continue;
            }

            return character switch
            {
                // Georgian and Georgian Supplement (Asomtavruli, Mkhedruli, Nuskhuri) and the
                // Georgian Extended block that carries Mtavruli.
                >= 'Ⴀ' and <= 'ჿ' => "georgian",
                >= 'Ა' and <= 'Ჿ' => "georgian",
                >= '\u2D00' and <= '\u2D2F' => "georgian",
                // Basic Latin through Latin Extended-B: every alphabet this product's names are
                // typed in when they are not Georgian.
                <= 'ɏ' => "latin",
                _ => "other",
            };
        }

        return "none";
    }

    /// <summary>
    /// Words, by the only definition that survives Georgian punctuation: runs of letters and digits.
    /// „ვეკო“, ვეკო-ს and (ვეკო) all yield the same token, which is what the quotation marks and the
    /// hyphens in this book's copy require.
    /// </summary>
    private static IEnumerable<string> Tokens(string text)
    {
        var start = -1;

        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsLetterOrDigit(text[index]))
            {
                if (start < 0)
                {
                    start = index;
                }

                continue;
            }

            if (start >= 0)
            {
                yield return text[start..index];
                start = -1;
            }
        }

        if (start >= 0)
        {
            yield return text[start..];
        }
    }

    /// <summary>
    /// Levenshtein distance, answered only as 0, 1, or "2 or more" — everything this check asks.
    ///
    /// Bounded rather than a matrix because it runs over every word of a whole book and the answer
    /// beyond one edit is never used: a token two edits from the name is a different word, not a
    /// misspelling of this one.
    /// </summary>
    private static int Distance(string token, string name)
    {
        if (Math.Abs(token.Length - name.Length) > 1)
        {
            return 2;
        }

        if (token.Length == name.Length)
        {
            var substitutions = 0;

            for (var index = 0; index < token.Length; index++)
            {
                if (token[index] != name[index] && ++substitutions > 1)
                {
                    return 2;
                }
            }

            return substitutions;
        }

        // One longer than the other: a single insertion or deletion, walked with two cursors.
        var (longer, shorter) = token.Length > name.Length ? (token, name) : (name, token);
        var skipped = false;

        for (int outer = 0, inner = 0; outer < longer.Length; outer++)
        {
            if (inner < shorter.Length && longer[outer] == shorter[inner])
            {
                inner++;
                continue;
            }

            if (skipped)
            {
                return 2;
            }

            skipped = true;
        }

        return 1;
    }
}
