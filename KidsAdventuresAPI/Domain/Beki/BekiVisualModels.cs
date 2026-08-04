namespace AdventurePacks.Api.Domain.Beki;

/// <summary>
/// What the analyzer extracted from the child's photo, mirroring the Character Identity
/// Spec. Only visible, illustrator-useful traits: the prompt forbids inferring personality
/// or ethnicity, and uncertainty is recorded rather than guessed away.
/// </summary>
public sealed class BekiChildIdentitySpec
{
    /// <summary>good | usable_with_limits | insufficient</summary>
    public string ReferenceQuality { get; set; } = string.Empty;

    public BekiIdentityTraits Identity { get; set; } = new();
    public List<string> DistinctiveFeatures { get; set; } = [];
    public List<string> UncertainOrOccluded { get; set; } = [];
    public List<string> DoNotInfer { get; set; } = [];
    public BekiParentOverrides ParentOverrides { get; set; } = new();

    /// <summary>One dense paragraph written for a stylized 3D character designer.</summary>
    public string IdentityDesignerParagraph { get; set; } = string.Empty;

    /// <summary>An unusable photo must stop the pipeline, not produce a generic child.</summary>
    public bool IsUsable => !string.Equals(ReferenceQuality, "insufficient", StringComparison.OrdinalIgnoreCase);
}

public sealed class BekiIdentityTraits
{
    public string ApparentAgeRange { get; set; } = string.Empty;
    public string FaceShape { get; set; } = string.Empty;
    public string SkinTone { get; set; } = string.Empty;
    public string EyeShape { get; set; } = string.Empty;
    public string EyeColorVisibleInPhoto { get; set; } = string.Empty;
    public string HairColor { get; set; } = string.Empty;
    public string HairLength { get; set; } = string.Empty;
    public string HairTexture { get; set; } = string.Empty;
    public string HairPartingOrFraming { get; set; } = string.Empty;
    public string Eyebrows { get; set; } = string.Empty;
    public string Nose { get; set; } = string.Empty;
    public string Mouth { get; set; } = string.Empty;
    public string JawlineOrChin { get; set; } = string.Empty;
    public string Ears { get; set; } = string.Empty;
    public string Glasses { get; set; } = string.Empty;
    public string FrecklesOrMarks { get; set; } = string.Empty;
}

/// <summary>Parent-supplied facts beat visual guesswork for age and eye colour.</summary>
public sealed class BekiParentOverrides
{
    public string ChildName { get; set; } = string.Empty;
    public int Age { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public string EyeColor { get; set; } = string.Empty;
}

/// <summary>
/// The book's visual contract, mirroring <c>visual-bible-v1.schema.json</c>. Built once and
/// then restated in every page prompt — it is what stops twelve independently generated
/// images from looking like twelve different books.
/// </summary>
public sealed class BekiVisualBible
{
    public string VisualStyleName { get; set; } = "Beki Premium 3D Storybook Style";
    public string HeroIdentitySummary { get; set; } = string.Empty;
    public BekiHeroOutfit HeroStoryOutfit { get; set; } = new();
    public BekiCanonicalLock BekiCanonicalLock { get; set; } = new();
    public List<BekiSupportingLock> SupportingCharacterLocks { get; set; } = [];
    public BekiWorldStyle WorldStyle { get; set; } = new();
    public BekiCompositionDefaults CompositionDefaults { get; set; } = new();
    public BekiRenderRules RenderRules { get; set; } = new();
}

public sealed class BekiHeroOutfit
{
    public string OutfitId { get; set; } = string.Empty;
    public string Top { get; set; } = string.Empty;
    public string Bottom { get; set; } = string.Empty;
    public string OuterLayer { get; set; } = string.Empty;
    public string Footwear { get; set; } = string.Empty;
    public List<string> Accessories { get; set; } = [];
    public List<string> Palette { get; set; } = [];
    public bool MustRemainConsistent { get; set; } = true;
}

public sealed class BekiCanonicalLock
{
    public List<string> Preserve { get; set; } = [];
    public List<string> Never { get; set; } = [];
    public string VisualPriority { get; set; } = "secondary";
    public string ScaleRelativeToChild { get; set; } = "smaller";
}

public sealed class BekiSupportingLock
{
    public string CharacterId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> Preserve { get; set; } = [];
    public List<string> Never { get; set; } = [];
    public string VisualPriority { get; set; } = "secondary";
}

public sealed class BekiWorldStyle
{
    public List<string> Palette { get; set; } = [];
    public List<string> Materials { get; set; } = [];
    public string LightingLanguage { get; set; } = string.Empty;
    public List<string> EnvironmentMotifs { get; set; } = [];
    public string Mood { get; set; } = string.Empty;
    public List<string> AgeSafetyNotes { get; set; } = [];
}

public sealed class BekiCompositionDefaults
{
    public string InteriorAspectRatio { get; set; } = "2:3";
    public string CoverAspectRatio { get; set; } = "2:3";
    public string TextSafeAreaGuideline { get; set; } = string.Empty;
    public bool GutterNeeded { get; set; }
    public bool NoGeneratedText { get; set; } = true;
}

public sealed class BekiRenderRules
{
    public string StyleStatement { get; set; } = string.Empty;
    public bool ChildIsVisualHero { get; set; } = true;
    public bool NoPhotorealism { get; set; } = true;
    public bool NoText { get; set; } = true;
    public bool NoLogos { get; set; } = true;
    public bool NoWatermarks { get; set; } = true;
    public bool NoFakeQr { get; set; } = true;
}

/// <summary>
/// One illustration's brief, mirroring <c>page-scene-v1.schema.json</c>. Derived from the
/// approved story's structured metadata rather than from its prose — the handoff is
/// explicit that raw story text must not be truncated into an image prompt.
/// </summary>
public sealed class BekiPageSceneSpec
{
    public string SceneId { get; set; } = string.Empty;

    /// <summary>1..12 for pages; null for the cover.</summary>
    public int? PageNumber { get; set; }

    /// <summary>The exact cast. Nothing may be added, removed, duplicated or merged.</summary>
    public List<string> CharactersPresent { get; set; } = [];

    public string ChildAction { get; set; } = string.Empty;
    public string SceneSummaryEn { get; set; } = string.Empty;
    public string MainEmotionalBeat { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string KeyObject { get; set; } = string.Empty;

    /// <summary>Outfit, carried props, time of day — what must not contradict earlier pages.</summary>
    public Dictionary<string, string> ContinuityState { get; set; } = [];

    public BekiComposition Composition { get; set; } = new();
}

public sealed class BekiComposition
{
    public string ShotType { get; set; } = string.Empty;
    public string CameraAngle { get; set; } = string.Empty;
    public string HeroPlacement { get; set; } = string.Empty;

    /// <summary>Where the Georgian text will be laid over the art, so it stays clear.</summary>
    public string TextSafeArea { get; set; } = string.Empty;

    public string SupportingPlacement { get; set; } = string.Empty;
    public string FocalObjectPlacement { get; set; } = string.Empty;
}

/// <summary>Automated QA verdict, mirroring <c>visual-review-v1.schema.json</c>.</summary>
public sealed class BekiVisualReview
{
    /// <summary>approve | repair | regenerate</summary>
    public string Decision { get; set; } = string.Empty;

    public BekiVisualScores Scores { get; set; } = new();
    public List<string> DetectedIssues { get; set; } = [];
    public List<string> RepairInstructions { get; set; } = [];
    public List<string> RegenerationInstructions { get; set; } = [];
    public bool TextDetected { get; set; }
    public bool LogoOrWatermarkDetected { get; set; }
    public bool FakeQrDetected { get; set; }
    public List<string> CharacterListSeen { get; set; } = [];
    public string Notes { get; set; } = string.Empty;
}

public sealed class BekiVisualScores
{
    public double HeroIdentityMatch { get; set; }
    public double HeroAgeMatch { get; set; }
    public double HeroOutfitMatch { get; set; }
    public double BekiDesignMatch { get; set; }
    public double CharacterCountCorrect { get; set; }
    public double ChildVisualDominance { get; set; }
    public double SceneActionMatch { get; set; }
    public double ContinuityMatch { get; set; }
    public double TextSafeArea { get; set; }
    public double OverallComposition { get; set; }
}
