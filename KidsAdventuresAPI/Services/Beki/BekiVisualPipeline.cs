using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Beki;

namespace AdventurePacks.Api.Services.Beki;

public interface IBekiVisualPipeline
{
    Task<BekiVisualResult> IllustrateAsync(
        BekiStoryOutput story,
        BekiVisualContext context,
        CancellationToken cancellationToken);
}

/// <summary>Everything the visual pipeline needs that does not come from the story itself.</summary>
public sealed class BekiVisualContext
{
    public required Guid StoryId { get; init; }
    public required Guid CharacterId { get; init; }
    public required byte[] ChildPhotoBytes { get; init; }
    public required string ChildPhotoContentType { get; init; }
    public required int Age { get; init; }
    public required string AgeBand { get; init; }
    public string? EyeColor { get; init; }

    /// <summary>licensed | private_test | originalize | exclude — carried through to the bible.</summary>
    public string ExtraWishMode { get; init; } = "originalize";
}

public sealed class BekiVisualResult
{
    public required bool Success { get; init; }
    public required string FailureReason { get; init; }
    public BekiChildIdentitySpec? Identity { get; init; }
    public BekiVisualBible? VisualBible { get; init; }
    public byte[]? HeroAnchor { get; init; }
    public byte[]? Cover { get; init; }
    public IReadOnlyDictionary<int, byte[]> Pages { get; init; } = new Dictionary<int, byte[]>();
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Builds the illustrations for one approved book.
///
/// The ordering is the substance of this class, not an implementation detail. Identity is
/// extracted before anything is drawn; the Visual Bible fixes the outfit and Beki's lock
/// before any page exists; and the hero anchor is generated and approved *first*, because
/// every later page is matched against it. The previous flow used page 1 as the reference,
/// which meant a weak page 1 quietly degraded the whole book.
///
/// Beki's canonical asset is attached only to pages whose cast includes Beki. That is why
/// the story validator insists <c>bekiPresent</c> and <c>charactersPresent</c> agree: this
/// pipeline trusts the cast list completely.
/// </summary>
public sealed class BekiVisualPipeline(
    IBekiOpenAiClient client,
    IBekiPromptProvider prompts,
    BekiSceneSpecBuilder specBuilder,
    IOptions<BekiOptions> options,
    ILogger<BekiVisualPipeline> logger) : IBekiVisualPipeline
{
    private readonly BekiOptions _options = options.Value;
    private byte[]? _bekiReference;

    public async Task<BekiVisualResult> IllustrateAsync(
        BekiStoryOutput story,
        BekiVisualContext context,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        // 1. Identity. Doubles as the photo quality gate: an unusable photo stops here
        //    rather than producing a generic child the parent will not recognise.
        var identity = await AnalyzeIdentityAsync(story, context, cancellationToken);
        if (identity is null)
        {
            return Failure("identity_analysis_failed");
        }

        if (!identity.IsUsable)
        {
            logger.LogInformation(
                "Beki visual {StoryId}: photo rejected ({Quality}) — {Reasons}",
                context.StoryId, identity.ReferenceQuality, string.Join("; ", identity.UncertainOrOccluded));
            return Failure("photo_insufficient", identity);
        }

        // 2. The book's visual contract.
        var bible = await BuildVisualBibleAsync(story, context, identity, cancellationToken);
        if (bible is null)
        {
            return Failure("visual_bible_failed", identity);
        }

        // 3. The canonical hero. Nothing else may be drawn until this is right.
        var anchor = await GenerateHeroAnchorAsync(context, bible, cancellationToken);
        if (anchor is null)
        {
            return Failure("hero_anchor_failed", identity, bible);
        }

        var childPhoto = new BekiImageAttachment(
            "Reference Image A: child photo — identity only",
            context.ChildPhotoBytes,
            context.ChildPhotoContentType);

        var anchorReference = new BekiImageAttachment(
            "Reference Image B: approved hero anchor — stylized design and outfit",
            anchor,
            "image/png");

        // 4. Cover.
        var coverSpec = specBuilder.BuildCoverSpec(story);
        var cover = await GenerateReviewedAssetAsync(
            story, bible, coverSpec, childPhoto, anchorReference,
            isCover: true, warnings, cancellationToken);

        // 5. Pages, in small batches now that the references exist.
        var pageSpecs = specBuilder.BuildPageSpecs(story);
        var pages = new Dictionary<int, byte[]>();
        using var gate = new SemaphoreSlim(Math.Max(1, _options.PageConcurrency));

        var tasks = pageSpecs.Select(async spec =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                // Stagger starts so a burst does not trip provider rate limits.
                if (_options.PageStaggerSeconds > 0 && spec.PageNumber is > 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.PageStaggerSeconds), cancellationToken);
                }

                var bytes = await GenerateReviewedAssetAsync(
                    story, bible, spec, childPhoto, anchorReference,
                    isCover: false, warnings, cancellationToken);

                return (spec.PageNumber!.Value, bytes);
            }
            finally
            {
                gate.Release();
            }
        });

        foreach (var (pageNumber, bytes) in await Task.WhenAll(tasks))
        {
            if (bytes is not null)
            {
                pages[pageNumber] = bytes;
            }
        }

        var missing = BekiStoryConstants.PageCount - pages.Count;
        if (missing > 0)
        {
            warnings.Add($"{missing} page illustration(s) could not be produced.");
        }

        return new BekiVisualResult
        {
            Success = pages.Count == BekiStoryConstants.PageCount && cover is not null,
            FailureReason = missing > 0 ? "incomplete_pages" : string.Empty,
            Identity = identity,
            VisualBible = bible,
            HeroAnchor = anchor,
            Cover = cover,
            Pages = pages,
            Warnings = warnings,
        };
    }

    private Task<BekiChildIdentitySpec?> AnalyzeIdentityAsync(
        BekiStoryOutput story,
        BekiVisualContext context,
        CancellationToken cancellationToken) =>
        client.CompleteJsonWithImagesAsync<BekiChildIdentitySpec>(
            _options.IdentityAnalyzerModel,
            prompts.Get(BekiPromptProvider.CharacterIdentityAnalyzer),
            new
            {
                parentProvided = new
                {
                    childName = story.ChildName,
                    age = context.Age,
                    ageBand = context.AgeBand,
                    eyeColor = context.EyeColor,
                },
            },
            [new BekiImageAttachment("Child photo", context.ChildPhotoBytes, context.ChildPhotoContentType)],
            cancellationToken);

    private Task<BekiVisualBible?> BuildVisualBibleAsync(
        BekiStoryOutput story,
        BekiVisualContext context,
        BekiChildIdentitySpec identity,
        CancellationToken cancellationToken) =>
        client.CompleteJsonAsync<BekiVisualBible>(
            _options.VisualBibleModel,
            prompts.Get(BekiPromptProvider.VisualBibleBuilder),
            new
            {
                approvedStory = story,
                childIdentitySpec = identity,
                childProfile = new
                {
                    childName = story.ChildName,
                    age = context.Age,
                    ageBand = context.AgeBand,
                    theme = story.Theme,
                    extraWishMode = context.ExtraWishMode,
                },
                officialBekiReference = BekiCanonDescription,
                layoutConfig = new
                {
                    interiorAspectRatio = _options.InteriorAspectRatio,
                    coverAspectRatio = _options.CoverAspectRatio,
                    interiorAssetType = "single_page_portrait",
                },
            },
            cancellationToken,
            prompts.GetSchema(BekiPromptProvider.VisualBibleSchema));

    private async Task<byte[]?> GenerateHeroAnchorAsync(
        BekiVisualContext context,
        BekiVisualBible bible,
        CancellationToken cancellationToken)
    {
        try
        {
            var prompt = await client.CompleteTextWithImagesAsync(
                _options.VisualPromptModel,
                prompts.Get(BekiPromptProvider.HeroCharacterAnchor),
                new { visualBible = bible },
                [new BekiImageAttachment("Reference Image A: child photo — identity only",
                    context.ChildPhotoBytes, context.ChildPhotoContentType)],
                cancellationToken);

            return await client.GenerateImageAsync(
                prompt,
                [new BekiImageAttachment("Reference Image A: child photo — identity only",
                    context.ChildPhotoBytes, context.ChildPhotoContentType)],
                _options.InteriorImageSize,
                _options.AnchorImageQuality,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Beki hero anchor generation failed for story {StoryId}", context.StoryId);
            return null;
        }
    }

    /// <summary>
    /// Generates one illustration, reviews it, and repairs or regenerates within the
    /// configured budget. Returns the best image obtained: a page that scores below
    /// threshold but is otherwise sound still beats a hole in the book.
    /// </summary>
    private async Task<byte[]?> GenerateReviewedAssetAsync(
        BekiStoryOutput story,
        BekiVisualBible bible,
        BekiPageSceneSpec spec,
        BekiImageAttachment childPhoto,
        BekiImageAttachment anchor,
        bool isCover,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var label = isCover ? "cover" : $"page {spec.PageNumber}";

        try
        {
            var references = new List<BekiImageAttachment> { childPhoto, anchor };

            // Beki's canonical asset travels only when Beki is actually in the scene.
            var bekiPresent = spec.CharactersPresent.Any(IsBeki);
            if (bekiPresent)
            {
                var bekiBytes = await LoadBekiReferenceAsync(cancellationToken);
                if (bekiBytes is not null)
                {
                    references.Add(new BekiImageAttachment(
                        "Reference Image C: official Beki — the sole authority for Beki's design",
                        bekiBytes, "image/png"));
                }
                else
                {
                    warnings.Add($"{label}: Beki reference asset missing; Beki drawn from description only.");
                }
            }

            var promptName = isCover
                ? BekiPromptProvider.CoverImageGenerator
                : BekiPromptProvider.PageImageGenerator;

            var finalPrompt = await client.CompleteTextWithImagesAsync(
                _options.VisualPromptModel,
                prompts.Get(promptName),
                new
                {
                    visualBible = bible,
                    sceneSpec = spec,
                    bookTitleKa = story.TitleKa,
                    referenceMap = references.Select(r => r.Label).ToArray(),
                },
                [],
                cancellationToken);

            var quality = isCover ? _options.CoverImageQuality : _options.PageImageQuality;
            var size = isCover ? _options.CoverImageSize : _options.InteriorImageSize;

            var image = await client.GenerateImageAsync(finalPrompt, references, size, quality, cancellationToken);

            // Review, then repair or regenerate within budget.
            for (var attempt = 0; attempt <= _options.MaxPageRepairAttempts; attempt++)
            {
                var review = await ReviewAsync(bible, spec, image, references, cancellationToken);
                if (review is null)
                {
                    warnings.Add($"{label}: visual review unavailable; image accepted unreviewed.");
                    return image;
                }

                if (MeetsThresholds(review, bekiPresent))
                {
                    return image;
                }

                logger.LogInformation(
                    "Beki {Label} for story {RequestId}: review says {Decision} — {Issues}",
                    label, story.RequestId, review.Decision, string.Join("; ", review.DetectedIssues.Take(3)));

                if (attempt == _options.MaxPageRepairAttempts)
                {
                    warnings.Add($"{label}: shipped below QA threshold after {attempt + 1} attempt(s) — {review.Decision}.");
                    return image;
                }

                image = review.Decision == "regenerate"
                    ? await client.GenerateImageAsync(
                        finalPrompt + "\n\nADDITIONAL CORRECTIONS:\n" +
                        string.Join("\n", review.RegenerationInstructions),
                        references, size, quality, cancellationToken)
                    : await RepairAsync(bible, spec, image, review, references, size, quality, cancellationToken);
            }

            return image;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Beki {Label} generation failed for story {RequestId}", label, story.RequestId);
            warnings.Add($"{label}: generation failed ({ex.Message}).");
            return null;
        }
    }

    private Task<BekiVisualReview?> ReviewAsync(
        BekiVisualBible bible,
        BekiPageSceneSpec spec,
        byte[] image,
        IReadOnlyList<BekiImageAttachment> references,
        CancellationToken cancellationToken)
    {
        var attachments = new List<BekiImageAttachment>
        {
            new("Generated illustration under review", image, "image/png"),
        };
        attachments.AddRange(references);

        return client.CompleteJsonWithImagesAsync<BekiVisualReview>(
            _options.VisualReviewerModel,
            prompts.Get(BekiPromptProvider.VisualReviewer),
            new { visualBible = bible, sceneSpec = spec },
            attachments,
            cancellationToken,
            prompts.GetSchema(BekiPromptProvider.VisualReviewSchema));
    }

    private async Task<byte[]> RepairAsync(
        BekiVisualBible bible,
        BekiPageSceneSpec spec,
        byte[] image,
        BekiVisualReview review,
        IReadOnlyList<BekiImageAttachment> references,
        string size,
        string quality,
        CancellationToken cancellationToken)
    {
        var attachments = new List<BekiImageAttachment>
        {
            new("Illustration to edit", image, "image/png"),
        };
        attachments.AddRange(references);

        var repairPrompt = await client.CompleteTextWithImagesAsync(
            _options.VisualPromptModel,
            prompts.Get(BekiPromptProvider.VisualRepair),
            new { visualBible = bible, sceneSpec = spec, visualReview = review },
            attachments,
            cancellationToken);

        return await client.GenerateImageAsync(repairPrompt, attachments, size, quality, cancellationToken);
    }

    /// <summary>
    /// Hard fails first: any generated text, logo or fake QR makes an image unusable
    /// regardless of how well it scores, because the Georgian layout is applied on top.
    /// </summary>
    private bool MeetsThresholds(BekiVisualReview review, bool bekiPresent)
    {
        if (review.TextDetected || review.LogoOrWatermarkDetected || review.FakeQrDetected)
        {
            return false;
        }

        var t = _options.ReviewThresholds;
        var s = review.Scores;

        return s.HeroIdentityMatch >= t.HeroIdentityMatch
               && s.HeroAgeMatch >= t.HeroAgeMatch
               && s.HeroOutfitMatch >= t.HeroOutfitMatch
               && (!bekiPresent || s.BekiDesignMatch >= t.BekiDesignMatch)
               && s.CharacterCountCorrect >= t.CharacterCountCorrect
               && s.ChildVisualDominance >= t.ChildVisualDominance
               && s.SceneActionMatch >= t.SceneActionMatch
               && s.TextSafeArea >= t.TextSafeArea;
    }

    private async Task<byte[]?> LoadBekiReferenceAsync(CancellationToken cancellationToken)
    {
        if (_bekiReference is not null)
        {
            return _bekiReference;
        }

        var path = Path.Combine(AppContext.BaseDirectory, _options.BekiReferenceAssetPath);
        if (!File.Exists(path))
        {
            logger.LogError("Beki canonical asset not found at {Path}", path);
            return null;
        }

        _bekiReference = await File.ReadAllBytesAsync(path, cancellationToken);
        return _bekiReference;
    }

    private static bool IsBeki(string name) =>
        name.Equals("Beki", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("ბეკი", StringComparison.Ordinal);

    private static BekiVisualResult Failure(
        string reason,
        BekiChildIdentitySpec? identity = null,
        BekiVisualBible? bible = null) => new()
    {
        Success = false,
        FailureReason = reason,
        Identity = identity,
        VisualBible = bible,
    };

    /// <summary>
    /// Beki's canon in words, for the stages that reason about Beki without seeing the
    /// asset. The image remains the authority; this exists so the Visual Bible can state
    /// the lock explicitly rather than inferring it from a picture.
    /// </summary>
    private const string BekiCanonDescription = """
        Beki is the platform's canonical magical lamb guide: a cream wool body with a soft
        felted tactile texture, a dark purple face and limbs, long floppy purple ears, warm
        golden eyes, and a distinctive cream wool tuft on the head. Round childlike
        proportions, small and secondary to the child.
        Never: realistic sheep anatomy, recoloured wool, short or reshaped ears, horns,
        unapproved clothing, duplication, merging with another character, or any framing
        that makes Beki visually dominant over the child.
        """;
}
