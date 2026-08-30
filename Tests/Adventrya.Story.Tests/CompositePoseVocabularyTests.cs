using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The keyword table, the vocabulary it steers the planner towards, and the deterministic checks
/// built on both.
///
/// Everything here is a pure function over shipped config, which is the point: the pose a book gets
/// is decided by a table and a priority order, and the whole argument for doing it that way — the
/// same sentence always gives the same pose, months later, on any machine — is only true if the
/// table is pinned by tests rather than by good intentions.
/// </summary>
public class CompositePoseVocabularyTests
{
    private static BekiPoseRegistry Registry() => BekiPoseRegistry.Load();

    // ---------------------------------------------------------------------------------------
    // R13a — the v1.1 keyword amendment
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Every keyword the v1.1 amendment added, with the pose the changelog says it selects.
    ///
    /// One row per keyword rather than one per pose, because the failure this whole amendment exists
    /// to fix was a table that looked complete and did not contain the words the model writes. A row
    /// here is a promise that this particular word reaches this particular pose, through the real
    /// priority order, in a sentence shaped like a real one.
    /// </summary>
    public static TheoryData<string, string> AddedKeywords => new()
    {
        // 02 welcome / invitation
        { "waves warmly", "pose_02_welcome_invitation" },
        { "waves hello", "pose_02_welcome_invitation" },
        { "waves back", "pose_02_welcome_invitation" },
        { "waves goodbye", "pose_02_welcome_invitation" },
        { "waves at the", "pose_02_welcome_invitation" },
        { "waves to the", "pose_02_welcome_invitation" },
        { "waving hello", "pose_02_welcome_invitation" },
        { "waving back", "pose_02_welcome_invitation" },
        { "welcoming", "pose_02_welcome_invitation" },
        { "invitation", "pose_02_welcome_invitation" },
        { "greeting", "pose_02_welcome_invitation" },
        { "calls the child over", "pose_02_welcome_invitation" },

        // 03 guide / point
        { "gestures encouragingly", "pose_03_guide_point" },
        { "gestures toward", "pose_03_guide_point" },
        { "gestures along", "pose_03_guide_point" },
        { "gestures ahead", "pose_03_guide_point" },
        { "gestures onward", "pose_03_guide_point" },
        { "motions toward", "pose_03_guide_point" },
        { "motions ahead", "pose_03_guide_point" },
        { "signals toward", "pose_03_guide_point" },
        { "shows the child the way", "pose_03_guide_point" },
        { "leads the way", "pose_03_guide_point" },
        { "traces the path", "pose_03_guide_point" },
        { "marks the path", "pose_03_guide_point" },
        { "lights the path", "pose_03_guide_point" },
        { "guiding", "pose_03_guide_point" },

        // 04 listen
        { "listen", "pose_04_listen" },
        { "hearing", "pose_04_listen" },
        { "attentive", "pose_04_listen" },
        { "tilts their head", "pose_04_listen" },
        { "tilts her head", "pose_04_listen" },
        { "tilts his head", "pose_04_listen" },
        { "tilts the head", "pose_04_listen" },
        { "head tilted", "pose_04_listen" },
        { "cocks their head", "pose_04_listen" },
        { "leans an ear", "pose_04_listen" },
        { "perks up", "pose_04_listen" },

        // 05 excited / celebrate
        { "claps", "pose_05_excited_celebrate" },
        { "clapping", "pose_05_excited_celebrate" },
        { "clap", "pose_05_excited_celebrate" },
        { "applauding", "pose_05_excited_celebrate" },
        { "celebrate", "pose_05_excited_celebrate" },
        { "happily", "pose_05_excited_celebrate" },
        { "excitedly", "pose_05_excited_celebrate" },
        { "excited", "pose_05_excited_celebrate" },
        { "cheerful", "pose_05_excited_celebrate" },
        { "in awe", "pose_05_excited_celebrate" },
        { "with awe", "pose_05_excited_celebrate" },
        { "awe and joy", "pose_05_excited_celebrate" },
        { "grins", "pose_05_excited_celebrate" },

        // 06 brave / protective
        { "protect", "pose_06_brave_protective" },
        { "shield", "pose_06_brave_protective" },
        { "guarding", "pose_06_brave_protective" },
        { "stands guard", "pose_06_brave_protective" },
        { "steps in front", "pose_06_brave_protective" },
        { "stands firm", "pose_06_brave_protective" },
        { "shelters", "pose_06_brave_protective" },
        { "keeps the child safe", "pose_06_brave_protective" },
        { "braces", "pose_06_brave_protective" },

        // 07 curious / lean
        { "wonder", "pose_07_curious_lean" },
        { "marvels", "pose_07_curious_lean" },
        { "marveling", "pose_07_curious_lean" },
        { "marvelling", "pose_07_curious_lean" },
        { "leans in", "pose_07_curious_lean" },
        { "leans forward", "pose_07_curious_lean" },
        { "leans down", "pose_07_curious_lean" },
        { "peers", "pose_07_curious_lean" },
        { "peering", "pose_07_curious_lean" },
        { "peeks", "pose_07_curious_lean" },
        { "studies", "pose_07_curious_lean" },
        { "looks closely", "pose_07_curious_lean" },
        { "looks carefully", "pose_07_curious_lean" },
        { "inspect", "pose_07_curious_lean" },
        { "examine", "pose_07_curious_lean" },
        { "investigate", "pose_07_curious_lean" },
        { "intrigued", "pose_07_curious_lean" },
        { "puzzled", "pose_07_curious_lean" },

        // 08 gentle / reassure
        { "reassure", "pose_08_gentle_reassure" },
        { "comfort", "pose_08_gentle_reassure" },
        { "encourages", "pose_08_gentle_reassure" },
        { "encouraging", "pose_08_gentle_reassure" },
        { "encouragement", "pose_08_gentle_reassure" },
        { "stands beside", "pose_08_gentle_reassure" },
        { "stands close", "pose_08_gentle_reassure" },
        { "stands nearby", "pose_08_gentle_reassure" },
        { "stands near", "pose_08_gentle_reassure" },
        { "stays beside", "pose_08_gentle_reassure" },
        { "sits beside", "pose_08_gentle_reassure" },
        { "kneels beside", "pose_08_gentle_reassure" },
        { "rests beside", "pose_08_gentle_reassure" },
        { "rests peacefully", "pose_08_gentle_reassure" },
        { "rests quietly", "pose_08_gentle_reassure" },
        { "rests cozily", "pose_08_gentle_reassure" },
        { "rests calmly", "pose_08_gentle_reassure" },
        { "waits beside", "pose_08_gentle_reassure" },
        { "nods", "pose_08_gentle_reassure" },
        { "smiles warmly", "pose_08_gentle_reassure" },
        { "smiles gently", "pose_08_gentle_reassure" },
        { "smiles softly", "pose_08_gentle_reassure" },
        { "watches warmly", "pose_08_gentle_reassure" },
        { "watches warm-heartedly", "pose_08_gentle_reassure" },
        { "watches thoughtfully", "pose_08_gentle_reassure" },
        { "watches gently", "pose_08_gentle_reassure" },
        { "watches quietly", "pose_08_gentle_reassure" },
        { "watches over", "pose_08_gentle_reassure" },
        { "watching over", "pose_08_gentle_reassure" },
        { "warm and caring", "pose_08_gentle_reassure" },
        { "caring expression", "pose_08_gentle_reassure" },
        { "caring smile", "pose_08_gentle_reassure" },
        { "gentle", "pose_08_gentle_reassure" },
        { "gently", "pose_08_gentle_reassure" },
        { "tenderly", "pose_08_gentle_reassure" },

        // 09 forward / adventure glide
        { "walks beside", "pose_09_forward_adventure_glide" },
        { "walks alongside", "pose_09_forward_adventure_glide" },
        { "walks with the child", "pose_09_forward_adventure_glide" },
        { "steps alongside", "pose_09_forward_adventure_glide" },
        { "steps beside", "pose_09_forward_adventure_glide" },
        { "moves alongside", "pose_09_forward_adventure_glide" },
        { "follows close behind", "pose_09_forward_adventure_glide" },
        { "follows behind", "pose_09_forward_adventure_glide" },
        { "follows the child", "pose_09_forward_adventure_glide" },
        { "glides ahead", "pose_09_forward_adventure_glide" },
        { "glides alongside", "pose_09_forward_adventure_glide" },
        { "flies ahead", "pose_09_forward_adventure_glide" },
        { "heads onward", "pose_09_forward_adventure_glide" },
        { "sets out", "pose_09_forward_adventure_glide" },
        { "travels onward", "pose_09_forward_adventure_glide" },
        { "journeys onward", "pose_09_forward_adventure_glide" },
        { "looks out", "pose_09_forward_adventure_glide" },
        { "looking out", "pose_09_forward_adventure_glide" },
        { "gazes out", "pose_09_forward_adventure_glide" },
        { "gazing out", "pose_09_forward_adventure_glide" },
        { "onward", "pose_09_forward_adventure_glide" },
    };

    /// <summary>
    /// Each added keyword, in a sentence, resolves to the pose the changelog promises — through the
    /// real registry and the real priority order, not a table this test brought with it.
    ///
    /// The sentence is mixed case and padded because the registry's normalization rule is part of
    /// what is being checked: a keyword that only works on a pre-lowercased string is a keyword that
    /// does not work.
    /// </summary>
    [Theory]
    [MemberData(nameof(AddedKeywords))]
    public void Every_added_keyword_selects_the_pose_the_changelog_promises(string keyword, string poseId)
    {
        var selection = BekiPoseSelector.Select(
            Registry(), "  Beki " + keyword.ToUpperInvariant() + " here.  ");

        Assert.Equal(poseId, selection.PoseId);
        Assert.False(selection.Fallback);
    }

    /// <summary>
    /// Every added keyword is actually in the file, under the pose the row claims.
    ///
    /// The theory above proves the selection; this proves it for the stated reason. Without it a row
    /// could pass because some *other* keyword in the same pose happened to match the sentence — and
    /// a keyword quietly missing from the registry would look tested.
    /// </summary>
    [Theory]
    [MemberData(nameof(AddedKeywords))]
    public void Every_added_keyword_is_listed_under_that_pose(string keyword, string poseId)
        => Assert.Contains(keyword, Registry().Pose(poseId).Keywords);

    /// <summary>
    /// The amendment is additive: every v1.0 keyword is still there, in its original order, at the
    /// front of its pose's list.
    ///
    /// Order at the front is what keeps the *recorded* keyword unchanged for every sentence that
    /// already matched — the selector reports the first hit, and an operator reading an old log line
    /// beside a new one should see the same word for the same sentence.
    /// </summary>
    [Theory]
    [InlineData("pose_02_welcome_invitation", "welcome", "welcomes", "invite", "invites", "inviting", "greet", "greets", "beckon", "beckons")]
    [InlineData("pose_03_guide_point", "points", "pointing", "gestures forward", "shows the way", "guides", "indicates", "directs", "reveals the path", "lights the way", "illuminates")]
    [InlineData("pose_04_listen", "listens", "listening", "attentively", "hears", "sound", "turns toward the call")]
    [InlineData("pose_05_excited_celebrate", "celebrates", "celebrating", "joyful", "delighted", "cheers", "cheering", "applauds", "proud", "relieved")]
    [InlineData("pose_06_brave_protective", "protects", "protective", "shields", "guards", "bravely", "stands between", "blocks danger")]
    [InlineData("pose_07_curious_lean", "curious", "curiously", "leans closer", "inspects", "investigates", "wonders", "examines", "surprised")]
    [InlineData("pose_08_gentle_reassure", "reassures", "reassuring", "comforts", "comforting", "soothes", "calms", "concern", "stays close")]
    [InlineData("pose_09_forward_adventure_glide", "glides forward", "flies forward", "moves ahead", "leads onward", "sets off", "continues onward", "adventure")]
    public void The_original_keywords_are_untouched_and_still_first(string poseId, params string[] original)
        => Assert.Equal(original, Registry().Pose(poseId).Keywords.Take(original.Length));

    /// <summary>
    /// Every keyword in the file is reachable: put it in a sentence on its own and it selects its
    /// own pose.
    ///
    /// The failure this catches is a keyword that can never win because an earlier pose in the
    /// priority order contains a substring of it — "walks joyfully" under pose 09, where pose 05
    /// already lists "joyful". Such an entry is worse than a missing one: it reads as coverage in a
    /// diff, and nobody notices until books keep falling back.
    /// </summary>
    [Fact]
    public void No_keyword_in_the_registry_is_unreachable()
    {
        var registry = Registry();

        foreach (var poseId in registry.PriorityOrder)
        {
            foreach (var keyword in registry.Pose(poseId).Keywords)
            {
                var selection = BekiPoseSelector.Select(registry, $"Beki {keyword} here.");

                Assert.True(
                    selection.PoseId == poseId,
                    $"'{keyword}' is listed under {poseId} but selects {selection.PoseId} "
                    + $"(on '{selection.MatchedKeyword}'), so it can never win.");
            }
        }
    }

    /// <summary>The neutral hover stays keyword-less, which is what makes a fallback a fallback.</summary>
    [Fact]
    public void The_fallback_pose_carries_no_keywords()
    {
        var registry = Registry();

        Assert.Empty(registry.Pose(registry.FallbackPoseId).Keywords);
        Assert.DoesNotContain(registry.FallbackPoseId, registry.PriorityOrder);
        Assert.Equal("v1.1", registry.KeywordRevision);

        // The pack revision is deliberately NOT bumped: no pixel, hash, order or forced pose moved,
        // and pipeline_config_v1.json pins this string.
        Assert.Equal("beki-pose-registry-v1", registry.RegistryVersion);
    }

    // ---------------------------------------------------------------------------------------
    // Priority: a phrase that matches two poses resolves by the documented order
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The collisions the real books actually produce, each with the pose the changelog's table says
    /// wins and the pose it beat.
    ///
    /// Substring matching means a sentence can hit several poses; <c>priority_order</c> is the whole
    /// answer, and these are the cases where getting it wrong would be visible in a printed book.
    /// </summary>
    [Theory]
    // "stands beside" (08) beats "looking out" (09): the pose is standing beside the child.
    [InlineData("Beki stands beside the child, looking out into the night sky.",
        "pose_08_gentle_reassure", "pose_09_forward_adventure_glide")]
    // "points" (03) beats "happily" (05): the page shows a direction being given.
    [InlineData("Beki points happily toward the sunlit dinosaur valley.",
        "pose_03_guide_point", "pose_05_excited_celebrate")]
    // "celebrates" (05) beats "looking out" (09): the beat is the celebration.
    [InlineData("Beki celebrates joyfully next to the child, looking out at the glowing city streets.",
        "pose_05_excited_celebrate", "pose_09_forward_adventure_glide")]
    // "wonder" (07) beats "steps alongside" (09): the moment is the looking, not the walking.
    [InlineData("Beki steps alongside the child, looking up at the glowing street lamps with wonder.",
        "pose_07_curious_lean", "pose_09_forward_adventure_glide")]
    // "points" (03) beats "stands beside" (08): a page that shows a direction shows the pointing.
    [InlineData("Beki stands beside the child and points toward the hidden path.",
        "pose_03_guide_point", "pose_08_gentle_reassure")]
    // "listens" (04) beats "claps" (05): the beat is Beki hearing it, whoever is clapping.
    [InlineData("Beki listens attentively while the child claps.",
        "pose_04_listen", "pose_05_excited_celebrate")]
    // v1.0's own rule, unchanged: protection outranks invitation.
    [InlineData("Beki welcomes the child and bravely shields her from the falling rocks.",
        "pose_06_brave_protective", "pose_02_welcome_invitation")]
    public void A_phrase_matching_two_poses_resolves_by_the_documented_order(
        string action, string winner, string loser)
    {
        var registry = Registry();
        var selection = BekiPoseSelector.Select(registry, action);

        Assert.Equal(winner, selection.PoseId);
        Assert.False(selection.Fallback);

        // And the loser is genuinely a loser rather than a pose that never matched: it is earlier or
        // later in the priority order, and the order is what decided.
        Assert.Contains(loser, registry.PriorityOrder);
        Assert.True(
            registry.PriorityOrder.ToList().IndexOf(winner)
            < registry.PriorityOrder.ToList().IndexOf(loser),
            $"'{winner}' must outrank '{loser}' in priority_order for this sentence to resolve as "
            + "the changelog says.");
    }

    /// <summary>
    /// The words the changelog says were deliberately left out, each in the sentence that would have
    /// been mis-mapped by adding them.
    ///
    /// These are the regressions a well-meaning future amendment is most likely to introduce, which
    /// is exactly why they are pinned as tests rather than only as prose in a changelog.
    /// </summary>
    [Theory]
    // "enjoying" contains "joy": a quiet evening walk is not a celebration.
    [InlineData("Beki walks beside the child, enjoying the quiet evening atmosphere of the forest.",
        "pose_09_forward_adventure_glide")]
    // "pointed out by the child" is the child pointing, not Beki.
    [InlineData("Beki looks with wonder at the glowing blue trail pointed out by the child.",
        "pose_07_curious_lean")]
    // "warm-heartedly" contains "hear", and listen outranks reassure.
    [InlineData("Beki watches warm-heartedly as the child greets the little dinosaur.",
        "pose_08_gentle_reassure")]
    // "lights up" is not "lights the way" or "lights the path".
    [InlineData("Beki smiles happily as the lantern lights up the night forest.",
        "pose_05_excited_celebrate")]
    public void The_words_left_out_of_the_table_are_the_ones_that_would_have_mis_mapped(
        string action, string poseId)
        => Assert.Equal(poseId, BekiPoseSelector.Select(Registry(), action).PoseId);

    /// <summary>
    /// A sentence with genuinely no listed verb still falls back and still says so.
    ///
    /// The amendment widened the table; it did not make it match everything. A book whose planner
    /// wrote something the table cannot read must still be counted, because that count is the only
    /// signal R13c acts on.
    /// </summary>
    [Theory]
    [InlineData("Beki hovers quietly above the meadow.")]
    [InlineData("Beki is present in the picture.")]
    [InlineData("")]
    [InlineData(null)]
    public void A_sentence_with_no_listed_verb_still_falls_back(string? action)
    {
        var selection = BekiPoseSelector.Select(Registry(), action);

        Assert.Equal("pose_01_neutral_hover", selection.PoseId);
        Assert.Null(selection.MatchedKeyword);
        Assert.True(selection.Fallback);
    }

    // ---------------------------------------------------------------------------------------
    // The two books the amendment was derived from
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The Beki sentences of the two completed books the supplier's finding was about, exactly as
    /// they were stored, with the pose each one now selects.
    ///
    /// Book <c>c4fc5fe7</c> is the one that composited the fallback on six of eight spreads; every
    /// one of those six now resolves. This is the regression that matters: not that the table is
    /// bigger, but that these particular sentences — written by the model that will write the next
    /// ones — now reach a pose.
    /// </summary>
    public static TheoryData<string, string> RealBookActions => new()
    {
        // c4fc5fe7 — six of these eight were fallbacks before the amendment.
        { "Beki stands beside the child, looking up at the starry night sky.", "pose_08_gentle_reassure" },
        { "Beki watches the small sleeping star with a gentle, curious expression.", "pose_07_curious_lean" },
        { "Beki smiles cheerfully as the little star wakes up.", "pose_05_excited_celebrate" },
        { "Beki offers an encouraging gesture to reassure the little star.", "pose_08_gentle_reassure" },
        { "Beki watches happily as the staircase lights up.", "pose_05_excited_celebrate" },
        { "Beki points joyfully toward the glittering star arch.", "pose_03_guide_point" },
        { "Beki waves warmly back up at the little star.", "pose_02_welcome_invitation" },
        { "Beki rests cozily on the cloud beside the child.", "pose_08_gentle_reassure" },

        // 09d57d46 — "claps happily" and "gazes in wonder" are the two the plan quotes by name.
        { "Beki tilts their head attentively, listening to the mysterious sound.", "pose_04_listen" },
        { "Beki watches warm-heartedly as the child greets the little dinosaur.", "pose_08_gentle_reassure" },
        { "Beki claps happily at the discovery of the glowing stone.", "pose_05_excited_celebrate" },
        { "Beki stands beside them, offering comforting encouragement.", "pose_08_gentle_reassure" },
        { "Beki points encouragingly along the illuminated path of footprints.", "pose_03_guide_point" },
        { "Beki gazes in wonder at the grand sight of the dinosaur family.", "pose_07_curious_lean" },
        { "Beki smiles warmly and nods approvingly at the child.", "pose_08_gentle_reassure" },
        { "Beki stands beside the child, looking out into the night sky.", "pose_08_gentle_reassure" },
    };

    [Theory]
    [MemberData(nameof(RealBookActions))]
    public void Every_beki_action_from_the_two_shipped_books_now_reaches_a_pose(
        string action, string poseId)
    {
        var selection = BekiPoseSelector.Select(Registry(), action);

        Assert.Equal(poseId, selection.PoseId);
        Assert.False(selection.Fallback);
        Assert.NotNull(selection.MatchedKeyword);
    }

    /// <summary>
    /// And as whole books: each of the two is now within the fallback budget, where one of them used
    /// to be six over it.
    /// </summary>
    [Fact]
    public void Both_shipped_books_are_now_inside_the_fallback_budget()
    {
        var registry = Registry();

        var counts = RealBookActions
            .Select(row => (string)row[0]!)
            .Chunk(8)
            .Select(book => book.Count(action => BekiPoseSelector.Select(registry, action).Fallback))
            .ToList();

        Assert.Equal(2, counts.Count);
        Assert.All(counts, count => Assert.Equal(0, count));
        Assert.All(counts, count =>
            Assert.True(count <= CompositePoseVocabulary.MaxFallbacksPerBook));
    }

    // ---------------------------------------------------------------------------------------
    // R13b — the vocabulary block sent to the planner
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Every exemplar verb the prompt shows the planner is genuinely a keyword of the pose it is
    /// listed under.
    ///
    /// This is the join that makes the steering honest. A prompt that recommended verbs the table
    /// cannot read would be worse than no steering at all: the planner would obey it and every book
    /// would fall back.
    /// </summary>
    [Fact]
    public void Every_verb_the_prompt_recommends_is_a_keyword_of_that_pose()
    {
        var registry = Registry();

        foreach (var (poseId, family, verbs) in CompositePoseVocabulary.Families)
        {
            var keywords = registry.Pose(poseId).Keywords;

            foreach (var verb in verbs)
            {
                Assert.True(
                    keywords.Contains(verb, StringComparer.Ordinal),
                    $"The scenario prompt offers \"{verb}\" for the {family} family, and "
                    + $"{poseId} does not list it.");
            }
        }
    }

    /// <summary>
    /// The families cover every prioritised pose exactly once, and the fallback is the only one
    /// offered no verbs — so a tenth pose added to the registry cannot be silently unsteerable.
    /// </summary>
    [Fact]
    public void The_families_cover_every_prioritised_pose_and_never_the_fallback()
    {
        var registry = Registry();

        var steered = CompositePoseVocabulary.Families
            .Where(entry => entry.Verbs.Length > 0)
            .Select(entry => entry.PoseId)
            .ToList();

        Assert.Equal(
            registry.PriorityOrder.OrderBy(id => id, StringComparer.Ordinal),
            steered.OrderBy(id => id, StringComparer.Ordinal));

        Assert.Contains(
            CompositePoseVocabulary.Families,
            entry => entry.PoseId == registry.FallbackPoseId && entry.Verbs.Length == 0);
    }

    /// <summary>
    /// The block reaches the system instruction the planner is actually sent, and the contract's own
    /// text is still in front of it, unedited.
    /// </summary>
    [Fact]
    public void The_scenario_instruction_is_the_contract_plus_the_vocabulary_block()
    {
        var instruction = CompositeVisualScenarioPrompt.SystemInstruction;

        Assert.StartsWith(CompositeVisualScenarioPrompt.System, instruction, StringComparison.Ordinal);
        Assert.Contains("BEKI ACTION VOCABULARY", instruction);

        foreach (var (_, family, _) in CompositePoseVocabulary.Families.Where(e => e.Verbs.Length > 0))
        {
            Assert.Contains($"- {family}:", instruction);
        }

        // Steering, not a new task: no pose id anywhere, and the planner is told so.
        Assert.DoesNotContain("pose_0", instruction);
        Assert.Contains("never name a pose, a pose id, or a page position", instruction);

        // And it must not undo the contract's own variety rule by inviting one family everywhere.
        Assert.Contains("do not reuse one family for the whole book", instruction);

        Assert.Equal("visual-scenario-v2.1", CompositeVisualScenarioPrompt.Version);
    }

    // ---------------------------------------------------------------------------------------
    // R13c — the deterministic audit
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The audit counts spreads and not the cover, and the budget is two.
    ///
    /// The cover is excluded because nothing composites one on this path — the composite cover stops
    /// at LAYOUT_FAILED for want of a printer dieline — so counting it would let a picture nobody
    /// draws spend a book's one retry.
    /// </summary>
    [Fact]
    public void The_audit_counts_spreads_and_reads_the_cover_only_as_advice()
    {
        var audit = CompositePoseVocabulary.Audit(
            Registry(),
            Scenario(
                Enumerable.Repeat("Beki hovers quietly nearby.", 8).ToArray(),
                cover: "Beki hovers quietly above the valley."));

        Assert.Equal(8, audit.Choices.Count);
        Assert.Equal(8, audit.FallbackCount);
        Assert.True(audit.ExceedsFallbackBudget);
        Assert.Equal(2, CompositePoseVocabulary.MaxFallbacksPerBook);

        // The cover is read, and separately.
        Assert.NotNull(audit.CoverChoice);
        Assert.Equal(CompositePoseVocabulary.CoverPage, audit.CoverChoice!.Page);
        Assert.True(audit.CoverChoice.Fallback);
    }

    /// <summary>Two fallbacks is inside the budget; three is over it. The boundary is the rule.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(6, true)]
    public void The_budget_is_exceeded_only_above_two_fallbacks(int fallbacks, bool exceeded)
    {
        var actions = Enumerable.Range(0, 8)
            .Select(index => index < fallbacks
                ? "Beki hovers quietly nearby."
                : "Beki points toward the path.")
            .ToArray();

        var audit = CompositePoseVocabulary.Audit(Registry(), Scenario(actions));

        Assert.Equal(fallbacks, audit.FallbackCount);
        Assert.Equal(exceeded, audit.ExceedsFallbackBudget);
        Assert.Equal(
            Enumerable.Range(1, fallbacks).ToList(),
            audit.FallbackPages);
    }

    /// <summary>
    /// The retry's problem quotes the sentences that failed and names the families to use — the two
    /// things a corrective retry needs that a code alone cannot carry.
    /// </summary>
    [Fact]
    public void The_retry_problem_quotes_the_sentences_and_names_the_families()
    {
        var actions = Enumerable.Range(1, 8)
            .Select(page => page <= 4 ? $"Beki is present on page {page}." : "Beki claps.")
            .ToArray();

        var problem = CompositePoseVocabulary.Problem(
            CompositePoseVocabulary.Audit(Registry(), Scenario(actions)));

        Assert.Equal(VisualScenarioProblemCodes.PoseVocabularyMiss, problem.Code);
        Assert.Contains("Beki is present on page 1.", problem.Detail);
        Assert.Contains("Spread 4", problem.Detail);
        Assert.Contains("celebrate", problem.Detail);
        Assert.Contains("reassure", problem.Detail);

        // And it protects everything the retry must not change on its way past.
        Assert.Contains("Do not change any child_world_scene", problem.Detail);
    }

    /// <summary>How many distinct poses a book would actually show — the variety number itself.</summary>
    [Fact]
    public void The_audit_counts_the_distinct_poses_the_book_would_show()
    {
        var sameEveryPage = CompositePoseVocabulary.Audit(
            Registry(), Scenario(Enumerable.Repeat("Beki points toward the path.", 8).ToArray()));

        Assert.Equal(1, sameEveryPage.DistinctPoses);
        Assert.Equal(0, sameEveryPage.FallbackCount);

        var varied = CompositePoseVocabulary.Audit(
            Registry(),
            Scenario(
            [
                "Beki listens attentively.", "Beki leans in curiously.", "Beki points toward the path.",
                "Beki stands beside the child.", "Beki claps.", "Beki walks beside the child.",
                "Beki welcomes the child.", "Beki shields the child.",
            ]));

        Assert.Equal(8, varied.DistinctPoses);
    }

    private static VisualScenarioV2 Scenario(string[] actions, string? cover = null) => new()
    {
        VisualLock = new VisualLock { ChildOutfit = "a mustard tunic", RecurringElements = [] },
        Cover = new VisualScenarioCover
        {
            FrontChildWorldScene = "The child at the edge of the valley.",
            BekiAction = cover ?? "Beki welcomes the child.",
            BackEnvironment = "The valley, empty.",
        },
        Spreads = actions
            .Select((action, index) => new VisualScenarioSpread
            {
                Page = index + 1,
                ChildWorldScene = $"The child on page {index + 1}.",
                BekiAction = action,
            })
            .ToList(),
    };
}

/// <summary>
/// The Georgian check-list (R12c) and the advisory shot note (R14), both of which are deliberate
/// non-events: they add information to a book and can never stop one.
/// </summary>
public class CompositeGeorgianAndShotNoteTests
{
    // ---------------------------------------------------------------------------------------
    // R12c — the deterministic Georgian check-list
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The two defects that actually shipped, found in the text they shipped in.
    ///
    /// Both are seeded from a printed book: ფუნღუროში for ფუღუროში, and the hyphenated case ending
    /// the name templating produced. Neither is a thing a reader of these logs can be expected to
    /// spot, which is the entire reason for a deterministic list.
    /// </summary>
    [Theory]
    [InlineData("პატარა თაგვი ფუნღუროში იმალებოდა.", "funguro_misspelling")]
    [InlineData("თემო-ს გაუხარდა, როცა ბილიკი გამოჩნდა.", "hyphenated_name_suffix")]
    [InlineData("ეზოში ბურთი ეწყოს და ბავშვი ხარობს.", "ewyos_verb_choice")]
    [InlineData("ბექიმ ხელი გაუწოდა.", "beki_with_qari")]
    public void The_checklist_finds_the_patterns_that_shipped(string text, string ruleId)
    {
        var flags = CompositeGeorgianCheck.InspectText(text, "spread 4");

        var flag = Assert.Single(flags, f => f.RuleId == ruleId);
        Assert.Equal("spread 4", flag.Location);
        Assert.NotEmpty(flag.Expected);
        Assert.Contains(flag.Found, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A broken rule costs its own rule and nothing else: the rest of the check-list still runs, the
    /// reason is recorded by id, and nothing throws.
    ///
    /// This is the defect the review found. The rules were compiled eagerly behind a lazy, so an
    /// invalid regular expression or a rule missing a field threw out of <c>Inspect</c> — which runs
    /// on every composite book — and an advisory note-taker would have failed every book in the
    /// deployment until somebody fixed an asset that exists only to add a note. An advisory check
    /// may never be the reason a paid book fails, and least of all over its own configuration.
    /// </summary>
    [Fact]
    public void A_broken_rule_is_skipped_and_the_rest_of_the_checklist_still_runs()
    {
        var checklist = LoadChecklist(
            """
            {
              "checklist_version": "test-checklist",
              "rules": [
                {"id": "broken_regex", "kind": "regex", "pattern": "[unclosed", "expected": "x"},
                {"id": "no_pattern", "kind": "substring", "expected": "x"},
                {"id": "funguro_misspelling", "kind": "substring", "pattern": "ფუნღურ",
                 "expected": "ფუღურ"}
              ]
            }
            """);

        // The good rule still fires, which is the half that matters: the book was still checked.
        var flag = Assert.Single(
            checklist.InspectText("პატარა თაგვი ფუნღუროში იმალებოდა.", "spread 4"));

        Assert.Equal("funguro_misspelling", flag.RuleId);
        Assert.Equal(1, checklist.RuleCount);
        Assert.Equal("test-checklist", checklist.Version);

        // And both broken ones are recorded by id and by reason, so an operator can fix the asset
        // and knows which packs went past while it was broken.
        Assert.Equal(2, checklist.Problems.Count);
        Assert.Contains(checklist.Problems, problem =>
            problem.StartsWith("broken_regex:", StringComparison.Ordinal)
            && problem.Contains("regular expression is invalid", StringComparison.Ordinal));
        Assert.Contains(checklist.Problems, problem =>
            problem.StartsWith("no_pattern:", StringComparison.Ordinal)
            && problem.Contains("no kind or no pattern", StringComparison.Ordinal));

        // A whole plan reads without throwing, which is what "the book proceeds" means here.
        var flags = checklist.Inspect(PlanWith("ფუნღუროს ზღაპარი", ["ყველაფერი კარგადაა."]));
        Assert.Single(flags);
    }

    /// <summary>
    /// A check-list that cannot be read at all disables the check and says so, rather than throwing
    /// or — worse — silently reporting a clean book.
    ///
    /// "No flags" and "no rules ran" are the same output and opposite facts. The second one has to
    /// reach the book's record, or a pack that was never checked reads as a pack that was checked
    /// and found clean.
    /// </summary>
    [Fact]
    public void An_unreadable_checklist_disables_the_check_and_says_so()
    {
        var empty = Directory.CreateTempSubdirectory("beki-georgian-missing");

        try
        {
            var checklist = GeorgianChecklist.Load(empty.FullName);

            Assert.Equal(0, checklist.RuleCount);
            Assert.Equal(CompositeGeorgianCheck.UnreadableVersion, checklist.Version);
            Assert.Contains(checklist.Problems, problem =>
                problem.Contains("could not be read", StringComparison.Ordinal));

            // And it still reads a book without throwing — it simply finds nothing.
            Assert.Empty(checklist.InspectText("პატარა თაგვი ფუნღუროში იმალებოდა.", "spread 4"));
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    /// <summary>The shipped check-list has no broken rules — the amendment is not merely survivable.</summary>
    [Fact]
    public void The_installed_checklist_loads_completely()
    {
        Assert.Empty(CompositeGeorgianCheck.RuleProblems);
        Assert.Equal("georgian-text-checklist-v1", CompositeGeorgianCheck.ChecklistVersion);
    }

    /// <summary>Writes a check-list into a temp asset tree and loads it the way production does.</summary>
    private static GeorgianChecklist LoadChecklist(string json)
    {
        var root = Directory.CreateTempSubdirectory("beki-georgian-checklist");
        var assets = Path.Combine(root.FullName, "Assets", "BekiComposite");

        Directory.CreateDirectory(assets);
        File.WriteAllText(
            Path.Combine(assets, CompositeGeorgianCheck.ChecklistFileName), json);

        try
        {
            return GeorgianChecklist.Load(root.FullName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Correct Georgian is not flagged — including the corrected spellings of the two rules above,
    /// which is what makes the rules stems rather than whole words.
    /// </summary>
    [Theory]
    [InlineData("პატარა თაგვი ფუღუროში იმალებოდა.")]
    [InlineData("თემოს გაუხარდა, როცა ბილიკი გამოჩნდა.")]
    [InlineData("ბეკიმ ხელი გაუწოდა და ბავშვი გაიღიმა.")]
    // A hyphen between two ordinary Georgian words is not a case ending.
    [InlineData("ცისფერ-მწვანე შუქი ანათებდა.")]
    public void Correct_georgian_is_left_alone(string text)
        => Assert.Empty(CompositeGeorgianCheck.InspectText(text, "spread 1"));

    /// <summary>
    /// The check reads the title and every spread, and reports where — because "somewhere in the
    /// book" is not something a person can act on.
    /// </summary>
    [Fact]
    public void The_checklist_reads_the_title_and_every_spread_and_says_which()
    {
        var flags = CompositeGeorgianCheck.Inspect(PlanWith(
            title: "ფუნღუროს ზღაპარი",
            texts: ["ყველაფერი კარგადაა.", "თემო-ს გაეღიმა.", "ისევ კარგადაა."]));

        Assert.Equal(2, flags.Count);
        Assert.Contains(flags, flag => flag.Location == "title" && flag.RuleId == "funguro_misspelling");
        Assert.Contains(flags, flag => flag.Location == "spread 2" && flag.RuleId == "hyphenated_name_suffix");

        // The excerpt is a window, not the paragraph: this travels into logs.
        Assert.All(flags, flag => Assert.True(flag.Excerpt.Length <= 120));
    }

    /// <summary>
    /// One flag per rule per location, however many times the pattern occurs.
    ///
    /// A name-suffix bug hits every sentence on a page; eight identical lines about spread 4 would
    /// bury the rule that fired once. The reviewer is being told which page to open.
    /// </summary>
    [Fact]
    public void A_repeated_fault_is_reported_once_per_page()
    {
        var flags = CompositeGeorgianCheck.InspectText(
            "თემო-ს გაეხარდა. თემო-ს ბილიკი დაინახა. თემო-ს გაეღიმა.", "spread 3");

        Assert.Single(flags);
    }

    /// <summary>
    /// It never rewrites. The flag carries what was found; the text it was found in is unchanged,
    /// because correcting a sentence a substring rule does not understand is how a book gets worse.
    /// </summary>
    [Fact]
    public void The_checklist_flags_and_never_rewrites()
    {
        const string original = "პატარა თაგვი ფუნღუროში იმალებოდა.";
        var plan = PlanWith("სათაური", [original]);

        var flags = CompositeGeorgianCheck.Inspect(plan);

        Assert.NotEmpty(flags);

        // The plan is a record and nothing here mutates it; said out loud because the whole
        // contract of this class is "flag, never repair".
        Assert.Equal(original, plan.Spreads[0].Text);
        Assert.Equal("georgian-text-checklist-v1", CompositeGeorgianCheck.ChecklistVersion);
    }

    [Fact]
    public void A_book_with_nothing_to_flag_produces_no_flags()
        => Assert.Empty(CompositeGeorgianCheck.Inspect(
            PlanWith("ბეკი და ვარსკვლავი", ["ბავშვი ღიმილით გაემართა ბილიკზე."])));

    // ---------------------------------------------------------------------------------------
    // R14 — the advisory shot note
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The shot instruction is the first line of the composition block, which is the whole of the
    /// R14 prompt fix: a model that reads "very wide panoramic" first has chosen its camera before
    /// the shot is mentioned.
    /// </summary>
    [Fact]
    public void The_shot_instruction_is_the_first_line_of_the_composition_block()
    {
        var prompt = CompositeIllustrationPrompt.ForSpread(new CompositeSpreadPromptInput
        {
            Page = 3,
            ChildAge = 5,
            Theme = CompositeThemeReferences.For("dinosaurs"),
            ChildWorldScene = "The child steps into the valley.",
            ChildOutfit = "a mustard tunic",
            IdentitySpec = CompositePipelineTests.IdentityFixture,
        });

        var composition = prompt[(prompt.IndexOf("COMPOSITION\n", StringComparison.Ordinal) + 12)..];
        var firstLine = composition[..composition.IndexOf('\n')];

        Assert.Equal(CompositeSpreadRhythm.ShotFor(3), firstLine);

        // The panorama sentence is still there, and now second. Since v1.5 it asks for a
        // "painting" rather than a "two-page spread": a model told the canvas is two pages
        // treats the middle as a place where one page ends, and the shipped book's veils all
        // stopped exactly there.
        Assert.Contains(
            $"{CompositeSpreadRhythm.ShotFor(3)}\nCreate one continuous very wide panoramic "
            + "painting designed for a final 15:7 crop.", prompt);
        Assert.DoesNotContain("two-page", prompt);

        Assert.Equal("child-world-image-v1.5", CompositeIllustrationPrompt.Version);
    }

    /// <summary>
    /// The reviewer is told which shot was asked for, and told twice that a note about it is
    /// advisory — once in the instruction, once in the page description.
    /// </summary>
    [Fact]
    public void The_reviewer_is_told_the_shot_and_that_a_note_changes_nothing()
    {
        var prompt = CompositeMinimalQa.Prompt(
            "The child steps into the valley.",
            "Beki points toward the path.",
            "a mustard tunic",
            [],
            "LEFT",
            anchorAttached: false,
            identity: CompositePipelineTests.IdentityFixture,
            shotInstruction: CompositeSpreadRhythm.ShotFor(3));

        Assert.Contains($"Shot this page was asked for: {CompositeSpreadRhythm.ShotFor(3)}", prompt);
        Assert.Contains("This is advisory only: it is not a failed check", prompt);
        Assert.Contains("shot_note is optional, advisory, and never a failure", prompt);

        // A caller with no rhythm entry says nothing about a shot rather than inventing one.
        Assert.DoesNotContain(
            "Shot this page was asked for",
            CompositeMinimalQa.Prompt(
                "The child steps into the valley.", "Beki points toward the path.",
                "a mustard tunic", [], "LEFT"));

        Assert.Equal("minimal-visual-qa-v1.4", CompositeMinimalQa.Version);
    }

    /// <summary>
    /// A PASS carrying a shot note is still a PASS, and the note is read off it.
    ///
    /// This is the whole advisory contract in one assertion: the verdict the retry ladder reads is
    /// unchanged, and the note rides alongside.
    /// </summary>
    [Fact]
    public void A_pass_with_a_shot_note_is_still_a_pass()
    {
        var parsed = CompositeMinimalQa.Parse(
            """
            {"status":"PASS","failed_checks":[],"recommended_action":"pass","notes":[],
             "shot_note":"A close-up, where a wide establishing view was asked for."}
            """);

        Assert.True(parsed.IsValid);
        Assert.True(parsed.Verdict!.Passed);
        Assert.Equal(CompositeQaVerdict.ActionPass, parsed.Verdict.RecommendedAction);
        Assert.Empty(parsed.Verdict.FailedChecks);
        Assert.Equal(
            "A close-up, where a wide establishing view was asked for.", parsed.Verdict.ShotNote);

        // The line the ladder and the stored record read does not mention it.
        Assert.DoesNotContain("close-up", parsed.Verdict.ToString());
    }

    /// <summary>
    /// Absent, empty and whitespace are the same answer, and none of them is an observation.
    /// A v1.2-shaped answer with no such key is still valid, which is what "optional" has to mean.
    /// </summary>
    [Theory]
    [InlineData("""{"status":"PASS","failed_checks":[],"recommended_action":"pass","notes":[]}""")]
    [InlineData("""{"status":"PASS","failed_checks":[],"recommended_action":"pass","notes":[],"shot_note":""}""")]
    [InlineData("""{"status":"PASS","failed_checks":[],"recommended_action":"pass","notes":[],"shot_note":"   "}""")]
    public void No_note_absent_or_empty_reads_as_no_observation(string answer)
    {
        var parsed = CompositeMinimalQa.Parse(answer);

        Assert.True(parsed.IsValid);
        Assert.Null(parsed.Verdict!.ShotNote);
    }

    /// <summary>
    /// The advisory did not become a tenth category. A reviewer that tries to fail a page for the
    /// shot is rejected by the supplied schema, exactly as it was before.
    /// </summary>
    [Fact]
    public void Shot_trouble_is_not_a_failed_check()
    {
        var parsed = CompositeMinimalQa.Parse(
            """
            {"status":"FAIL","failed_checks":["SHOT_TYPE"],"recommended_action":"regenerate_base","notes":[]}
            """);

        Assert.False(parsed.IsValid);
    }

    private static MasterStory PlanWith(string title, string[] texts) => new()
    {
        Concept = new StoryConcept { Title = title, Outline = [.. texts] },
        Spreads = texts
            .Select((text, index) => new StorySpread
            {
                Number = index + 1,
                Title = string.Empty,
                Caption = string.Empty,
                Text = text,
                Characters = ["child", "beki"],
                Objects = [],
                Illustration = new IllustrationBrief { Scene = "The child in the valley." },
            })
            .ToList(),
        CharacterLock = string.Empty,
        Cover = new IllustrationBrief { Scene = "The child at the edge of the valley." },
    };
}
