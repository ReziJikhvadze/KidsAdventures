using System.Text.Json;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Prompts;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// Tests for the Beki Spec v2 implementation covering objects, word bands, and dependencies.
/// </summary>
public class BekiSpecV2PlanTests
{
    private static MasterStory ValidPlan() => new()
    {
        Concept = new StoryConcept { Title = "Title", Outline = ["Outline"] },
        CharacterLock = "Lock",
        Cover = new IllustrationBrief { Scene = "Scene" },
        Cast = [ new StoryCastMember { Id = "char1", Name = "Char", VisualDescription = "desc" } ],
        Objects = [ new StoryObjectItem { Id = "obj1", Name = "Obj", VisualDescription = "desc" } ],
        Spreads =
        [
            new StorySpread { Number = 1, Title = "T", Caption = "C", Text = "test", Illustration = new IllustrationBrief { Scene = "Scene" }, Characters = ["beki"] },
            new StorySpread { Number = 2, Title = "T", Caption = "C", Text = "test", Illustration = new IllustrationBrief { Scene = "Scene" }, Characters = ["char1", "beki"] },
            new StorySpread { Number = 3, Title = "T", Caption = "C", Text = "test", Illustration = new IllustrationBrief { Scene = "Scene" }, Characters = ["char1", "beki"], Objects = ["obj1"] },
            new StorySpread { Number = 4, Title = "T", Caption = "C", Text = "test", Illustration = new IllustrationBrief { Scene = "Scene" }, Characters = ["beki"], Objects = ["obj1"] },
            new StorySpread { Number = 5, Title = "T", Caption = "C", Text = "test", Illustration = new IllustrationBrief { Scene = "Scene" } },
            new StorySpread { Number = 6, Title = "T", Caption = "C", Text = "test", Illustration = new IllustrationBrief { Scene = "Scene" } },
            new StorySpread { Number = 7, Title = "T", Caption = "C", Text = "test", Illustration = new IllustrationBrief { Scene = "Scene" } },
            new StorySpread { Number = 8, Title = "T", Caption = "C", Text = "test", Illustration = new IllustrationBrief { Scene = "Scene" }, Characters = ["beki"] }
        ]
    };

    [Fact]
    public void Objects_validation_catches_unknown_and_reserved_and_collisions()
    {
        var plan = ValidPlan();
        var planUnknown = plan with { Spreads = [ plan.Spreads[0] with { Objects = ["unknown_obj"] }, .. plan.Spreads.Skip(1) ] };
        
        var problems = BekiPlanValidator.Validate(planUnknown, 8);
        Assert.Contains(problems, p => p.Contains("unknown_obj"));

        var planClean = plan with { Spreads = [ plan.Spreads[0] with { Objects = [] }, .. plan.Spreads.Skip(1) ] };
        Assert.Empty(BekiPlanValidator.Validate(planClean, 8));

        var planReserved = plan with { Objects = [ new StoryObjectItem { Id = "beki", Name = "O", VisualDescription = "D" } ] };
        var probs2 = BekiPlanValidator.Validate(planReserved, 8);
        Assert.Contains(probs2, p => p.Contains("reserved"));

        var planColliding = plan with { Objects = [ new StoryObjectItem { Id = "char1", Name = "O", VisualDescription = "D" } ] };
        var probs3 = BekiPlanValidator.Validate(planColliding, 8);
        Assert.Contains(probs3, p => p.Contains("already used by a cast member"));
    }

    [Fact]
    public void Backwards_compat_json_without_objects_deserialises_and_validates()
    {
        var json = @"{
            ""concept"": { ""title"": ""T"", ""outline"": [""O""] },
            ""characterLock"": ""L"",
            ""cover"": { ""scene"": ""S"" },
            ""cast"": [ { ""id"": ""char1"", ""name"": ""C"", ""visualDescription"": ""D"" } ],
            ""spreads"": [
                { ""number"": 1, ""title"": ""T"", ""caption"": ""C"", ""text"": ""test"", ""illustration"": { ""scene"": ""S"" }, ""characters"": [""beki""] },
                { ""number"": 2, ""title"": ""T"", ""caption"": ""C"", ""text"": ""test"", ""illustration"": { ""scene"": ""S"" }, ""characters"": [""beki""] },
                { ""number"": 3, ""title"": ""T"", ""caption"": ""C"", ""text"": ""test"", ""illustration"": { ""scene"": ""S"" }, ""characters"": [""beki""] },
                { ""number"": 4, ""title"": ""T"", ""caption"": ""C"", ""text"": ""test"", ""illustration"": { ""scene"": ""S"" }, ""characters"": [""beki""] },
                { ""number"": 5, ""title"": ""T"", ""caption"": ""C"", ""text"": ""test"", ""illustration"": { ""scene"": ""S"" } },
                { ""number"": 6, ""title"": ""T"", ""caption"": ""C"", ""text"": ""test"", ""illustration"": { ""scene"": ""S"" } },
                { ""number"": 7, ""title"": ""T"", ""caption"": ""C"", ""text"": ""test"", ""illustration"": { ""scene"": ""S"" } },
                { ""number"": 8, ""title"": ""T"", ""caption"": ""C"", ""text"": ""test"", ""illustration"": { ""scene"": ""S"" }, ""characters"": [""beki""] }
            ]
        }";
        var plan = JsonSerializer.Deserialize<MasterStory>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(plan);
        Assert.Null(plan.Objects);
        var problems = BekiPlanValidator.Validate(plan, 8);
        Assert.Empty(problems);
    }

    [Fact]
    public void BekiSpreadRhythm_forces_spread_8_right_and_7_left()
    {
        Assert.Equal("right", BekiSpreadRhythm.TextSideFor(8));
        Assert.Equal("left", BekiSpreadRhythm.TextSideFor(7));
    }

    [Fact]
    public void ComposeBeki_applies_ctaSafe_module_clause_only_when_true()
    {
        var promptTrue = IllustrationPrompt.ComposeBeki("lock", "scene", "cont", "right", "shot", "avoid", ctaSafe: true);
        Assert.Contains("continuation module", promptTrue);

        var promptFalse = IllustrationPrompt.ComposeBeki("lock", "scene", "cont", "right", "shot", "avoid", ctaSafe: false);
        Assert.DoesNotContain("continuation module", promptFalse);

        var legacy = IllustrationPrompt.Compose("lock", "scene", "avoid");
        Assert.DoesNotContain("continuation module", legacy);
        Assert.DoesNotContain("printed over it", legacy); // reserved-side language
    }

    [Fact]
    public void BekiImageQaPrompt_applies_ctaSafe_module_clause_only_when_true()
    {
        var promptTrue = BekiImageQaPrompt.For("scene", "right", "lock", ctaSafe: true);
        Assert.Contains("printed module", promptTrue);
        
        var promptFalse = BekiImageQaPrompt.For("scene", "right", "lock", ctaSafe: false);
        Assert.DoesNotContain("printed module", promptFalse);
        
        Assert.Contains("recurring story object", promptFalse);
    }

    [Fact]
    public void SpreadDependencies_resolves_chains_and_ignores_adopted()
    {
        var plan = ValidPlan();
        
        var adopted = new HashSet<int>();
        var deps = BekiBookGenerator.SpreadDependencies(plan, adopted);
        
        Assert.Contains(2, deps[3]);
        Assert.Contains(3, deps[4]);
        
        Assert.DoesNotContain(3, deps[3]);
        Assert.DoesNotContain(4, deps[4]);
        
        Assert.Empty(deps[1]);

        var adopted2 = new HashSet<int> { 2 };
        var deps2 = BekiBookGenerator.SpreadDependencies(plan, adopted2);
        
        Assert.Empty(deps2[2]);
        Assert.DoesNotContain(2, deps2[3]);
        Assert.Contains(3, deps2[4]); 
        
        Assert.Empty(deps2[5]);
    }

    [Fact]
    public void Word_bands_by_age()
    {
        var plan = ValidPlan();
        
        var plan3_46 = plan with { Spreads = [ plan.Spreads[0] with { Text = string.Join(" ", Enumerable.Repeat("word", 46)) }, .. plan.Spreads.Skip(1) ] };
        var probs3_46 = BekiPlanValidator.Validate(plan3_46, 8, age: 3);
        Assert.Contains(probs3_46, p => p.Contains("46 words; maximum for age 3 is 45"));
        
        var plan3_45 = plan with { Spreads = [ plan.Spreads[0] with { Text = string.Join(" ", Enumerable.Repeat("word", 45)) }, .. plan.Spreads.Skip(1) ] };
        var probs3_45 = BekiPlanValidator.Validate(plan3_45, 8, age: 3);
        Assert.Empty(probs3_45);
        
        var plan7_69 = plan with { Spreads = [ plan.Spreads[0] with { Text = string.Join(" ", Enumerable.Repeat("word", 69)) }, .. plan.Spreads.Skip(1) ] };
        var probs7_69 = BekiPlanValidator.Validate(plan7_69, 8, age: 7);
        Assert.Contains(probs7_69, p => p.Contains("69 words; maximum for age 7 is 68"));
        
        var plan7_68 = plan with { Spreads = [ plan.Spreads[0] with { Text = string.Join(" ", Enumerable.Repeat("word", 68)) }, .. plan.Spreads.Skip(1) ] };
        var probs7_68 = BekiPlanValidator.Validate(plan7_68, 8, age: 7);
        Assert.Empty(probs7_68);
        
        var planNoAge = plan with { Spreads = [ plan.Spreads[0] with { Text = string.Join(" ", Enumerable.Repeat("word", 100)) }, .. plan.Spreads.Skip(1) ] };
        var probsNoAge = BekiPlanValidator.Validate(planNoAge, 8, age: null);
        Assert.Empty(probsNoAge);
    }

    [Fact]
    public void MasterStoryPromptV5_System_contains_band_and_continuation_rules()
    {
        var input = new MasterStoryInput 
        { 
            Age = 4,
            SpreadCount = 8,
            Language = "en", 
            ChildName = "test",
            Gender = "boy",
            EyeColor = "blue",
            Theme = AdventurePacks.Api.Domain.Enums.ThemeType.Magic
        };
        var sys = MasterStoryPromptV5.System(input);
        Assert.Contains("15–30", sys);
        Assert.Contains("25–45", sys);
        Assert.Contains("continuation", sys);
    }
}
