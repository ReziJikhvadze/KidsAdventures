using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.DTOs.Orders;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// What a parent is told when the book they paid for could not be made.
///
/// Pack 7fc8faf4 is the case these tests are written from. It failed on its first spread with
/// <c>IMAGE_QA_FAILED (spread 1): …</c> and the family never learned anything: the order still read
/// Fulfilled, because an order is marked fulfilled when generation is <em>enqueued</em>; the
/// progress bar stayed at 18%, because the terminal write touched status and message only; and the
/// one surface that did carry the failure carried it verbatim — an English code, on a shelf, in a
/// Georgian product.
///
/// So the rule these tests pin is a single one, applied at every boundary: the code is for the
/// operator and never leaves the building, and every parent-facing surface takes its words from
/// <see cref="ParentFacingFailure"/>.
/// </summary>
public class ParentFacingFailureTests
{
    /// <summary>
    /// Every code the pipeline can stop with, including the two the budget and the sweep write.
    /// Sourced from <see cref="CompositeFailureCodes.All"/> rather than typed out, so a code the
    /// supplier adds to the config arrives here without anybody remembering to add it.
    /// </summary>
    public static TheoryData<string> EveryCode()
    {
        var codes = new TheoryData<string>();
        foreach (var code in CompositeFailureCodes.All)
        {
            codes.Add(code);
        }

        codes.Add(GenerationBudget.ExceededCode);
        codes.Add(GenerationBudget.StalledCode);
        codes.Add("GENERATION_TIMED_OUT");

        // The asset lock's own code, which is written by a stage that runs BEFORE any model call and
        // was the one code with no mapping at all — it fell through to the generic arm.
        codes.Add(BekiAssetLock.FailureCode);
        return codes;
    }

    /// <summary>
    /// <c>ASSET_LOCK_FAILED</c> is a production fault in OUR materials, and the parent is told so —
    /// not that their child's book failed a quality check it never reached.
    ///
    /// The lock runs before a single image is paid for: nothing about this book was ever drawn, let
    /// alone refused. Reading the generic arm's "the drawing did not pass our own check" would tell a
    /// family something untrue about their own book, which is the whole class of fault this mapping
    /// exists to prevent.
    /// </summary>
    [Fact]
    public void The_asset_lock_is_a_production_fault_rather_than_a_refused_drawing()
    {
        var locked = ParentFacingFailure.ToParentMessage(
            $"{BekiAssetLock.FailureCode}: the licensed Noto face does not match its locked hash.");

        Assert.Equal(
            ParentFacingFailure.ToParentMessage(CompositeFailureCodes.LayoutFailed), locked);

        Assert.NotEqual(
            ParentFacingFailure.ToParentMessage(CompositeFailureCodes.ImageQaFailed), locked);

        // And it no longer falls through to the arm that catches codes nobody has mapped.
        Assert.NotEqual(ParentFacingFailure.ToParentMessage("SOME_UNMAPPED_CODE"), locked);
    }

    [Theory]
    [MemberData(nameof(EveryCode))]
    public void Every_code_becomes_a_Georgian_sentence_with_no_code_in_it(string code)
    {
        var message = ParentFacingFailure.ToParentMessage($"{code}: the operator's version.");

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain(code, message, StringComparison.OrdinalIgnoreCase);

        // No Latin at all, which is the check that actually holds the line: a message with any
        // Latin letter in it has either leaked a code, a stage name or an exception's own words.
        Assert.DoesNotContain(message, char.IsAsciiLetter);
        Assert.Contains(message, IsGeorgian);
    }

    [Theory]
    [MemberData(nameof(EveryCode))]
    public void The_bare_code_maps_the_same_as_the_code_with_a_sentence_after_it(string code)
    {
        // The stored message is not one shape. The composite pipeline writes "CODE (spread 4): …",
        // the budget writes "CODE: …", and a row written by hand may hold the code alone.
        Assert.Equal(
            ParentFacingFailure.ToParentMessage(code),
            ParentFacingFailure.ToParentMessage($"{code}: something the parent must not read."));

        Assert.Equal(
            ParentFacingFailure.ToParentMessage(code),
            ParentFacingFailure.ToParentMessage($"{code} (spread 4): something else."));
    }

    [Fact]
    public void The_real_message_from_pack_7fc8faf4_is_not_what_the_parent_sees()
    {
        const string stored = "IMAGE_QA_FAILED (spread 1): the child's hair does not match the "
                              + "identity spec on the left-hand page.";

        var message = ParentFacingFailure.ToParentMessage(stored);

        Assert.DoesNotContain("IMAGE_QA_FAILED", message);
        Assert.DoesNotContain("spread", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(message, char.IsAsciiLetter);
    }

    [Fact]
    public void The_sweeps_own_reason_is_mapped_too()
    {
        // The one a stalled book actually carries, built by the sweep rather than the pipeline.
        var message = ParentFacingFailure.ToParentMessage(
            GenerationBudget.StalledReason(TimeSpan.FromMinutes(45)));

        Assert.DoesNotContain(GenerationBudget.StalledCode, message);
        Assert.DoesNotContain(message, char.IsAsciiLetter);

        // And it does not quote the forty-five minutes either. That number is for the operator
        // deciding whether this is "just over the line" or "dead since yesterday".
        Assert.DoesNotContain("45", message);
    }

    [Fact]
    public void A_book_that_ran_out_of_time_is_not_told_the_same_thing_as_one_that_failed_review()
    {
        // The two a parent can distinguish: "it took too long" is not the same news as "the
        // pictures did not pass our check", and one of them is nobody's fault.
        Assert.NotEqual(
            ParentFacingFailure.ToParentMessage(CompositeFailureCodes.ImageQaFailed),
            ParentFacingFailure.ToParentMessage(GenerationBudget.StalledCode));

        // Failures of the same kind share their wording. Nine codes do not need nine apologies,
        // and a parent cannot act on the difference between a refused picture and an unusable
        // identity read.
        Assert.Equal(
            ParentFacingFailure.ToParentMessage(CompositeFailureCodes.ImageQaFailed),
            ParentFacingFailure.ToParentMessage(CompositeFailureCodes.IdentitySpecFailed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("The operation was canceled.")]
    [InlineData("SOME_CODE_MINTED_NEXT_QUARTER: a stage that does not exist yet.")]
    public void Anything_unrecognised_still_answers_in_Georgian(string? stored)
    {
        // Including null, which is what a row failed by a writer that recorded no reason holds.
        // A caller reaching for this has already decided the parent is being told something;
        // returning nothing would leave the screen blank at exactly the wrong moment.
        var message = ParentFacingFailure.ToParentMessage(stored);

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain(message, char.IsAsciiLetter);
        Assert.Contains(message, IsGeorgian);
    }

    [Fact]
    public void An_ordinary_sentence_is_never_mistaken_for_a_code()
    {
        // "The operation was canceled." starts with a capital T. Splitting on the first colon or
        // space would read that as a one-letter code; the parser wants at least two.
        Assert.Equal(
            ParentFacingFailure.ToParentMessage("A model refused the request."),
            ParentFacingFailure.ToParentMessage(null));
    }

    [Fact]
    public void The_progress_line_is_the_one_the_legacy_pipeline_has_always_written()
    {
        // Both pipelines write this exact sentence over the frozen mid-generation line. It is a
        // constant rather than two literals precisely so they cannot drift into two apologies.
        Assert.Equal("რაღაც შეფერხდა. სცადე ხელახლა ან აირჩიე სხვა თემა.", ParentFacingFailure.ProgressLine);
        Assert.DoesNotContain(ParentFacingFailure.ProgressLine, char.IsAsciiLetter);
    }

    private static bool IsGeorgian(char c) => c is >= 'Ⴀ' and <= 'ჿ';
}

/// <summary>
/// The other pipeline's failures.
///
/// A purchase falls back to the legacy per-page flow whenever the Beki format is off or the
/// preview run no longer holds what that format needs, so a book drawn here is exactly as paid for
/// as a Beki one. It has always written the parent-safe progress line over the frozen one — that
/// sentence is where <see cref="ParentFacingFailure.ProgressLine"/> came from — and then told
/// nobody, which left the failure reaching the family as a shelf card that stopped moving.
/// </summary>
public class LegacyGenerationFailureTests
{
    [Fact]
    public async Task A_book_this_pipeline_could_not_write_reaches_its_parent()
    {
        var world = new LegacyWorld("STORY_FAILED: the model returned nothing usable.");

        await world.Service().ProcessStoryGenerationAsync(world.PackId, CancellationToken.None);

        Assert.Equal(AdventurePackStatus.Failed, world.Packs.Status);
        Assert.Equal(1, world.Notifier.Notifications);

        var sent = Assert.Single(world.Email.Failures);
        Assert.Equal(SingleUserRepository.Address, sent.To);
        Assert.Equal("ბექა და ცისარტყელას ხიდი", sent.BookTitle);
        Assert.DoesNotContain("STORY_FAILED", sent.ParentMessage);
        Assert.DoesNotContain(sent.ParentMessage, char.IsAsciiLetter);

        // The row keeps the operator's copy, and the bar stops rather than staying frozen where
        // the dead job left it.
        Assert.Equal("STORY_FAILED: the model returned nothing usable.", world.Packs.ErrorMessage);
        Assert.Equal(ParentFacingFailure.ProgressLine, world.Packs.ProgressMessage);
    }

    [Fact]
    public async Task The_sweep_having_got_there_first_means_no_second_letter()
    {
        /*
          The double-apology this guard exists for.

          The sweep buries a silent book, records its own reason and writes to the parent. This
          job then comes back to life, loses the compare-and-set, and — before the guard — sent a
          second letter explaining the same failure differently. An operator can absorb a duplicate
          page; a parent reading two different explanations of one failure cannot.
        */
        var world = new LegacyWorld("STORY_FAILED: too late.");
        world.Packs.OnBeforeTransition = () => world.Packs.Force(AdventurePackStatus.Failed, "swept");

        await world.Service().ProcessStoryGenerationAsync(world.PackId, CancellationToken.None);

        Assert.Empty(world.Email.Failures);

        // The operator is still paged, and the sweep's verdict still stands on the row.
        Assert.Equal(1, world.Notifier.Notifications);
        Assert.Equal("swept", world.Packs.ErrorMessage);
    }

    [Fact]
    public async Task A_mail_server_that_is_down_does_not_undo_the_verdict()
    {
        var world = new LegacyWorld("STORY_FAILED: the model refused.");
        world.Email.Throw = true;

        await world.Service().ProcessStoryGenerationAsync(world.PackId, CancellationToken.None);

        Assert.Equal(AdventurePackStatus.Failed, world.Packs.Status);
        Assert.Equal(1, world.Notifier.Notifications);
    }

    [Fact]
    public async Task An_owner_with_no_address_on_file_is_not_written_to()
    {
        var world = new LegacyWorld("STORY_FAILED: the model refused.")
        {
            Users = { HasEmail = false }
        };

        await world.Service().ProcessStoryGenerationAsync(world.PackId, CancellationToken.None);

        Assert.Empty(world.Email.Failures);
        Assert.Equal(AdventurePackStatus.Failed, world.Packs.Status);
    }

    /// <summary>
    /// The story job, wired to fail inside the first call it makes after claiming the pack. What
    /// happens before that point is another test's subject; this one is about the handler.
    /// </summary>
    private sealed class LegacyWorld(string failure)
    {
        public Guid PackId { get; } = Guid.NewGuid();
        public LegacyPacks Packs { get; } = new(Guid.NewGuid());
        public RecordingEmailService Email { get; } = new();
        public SingleUserRepository Users { get; } = new();
        public CountingNotifier Notifier { get; } = new();

        public AdventureGenerationService Service()
        {
            Packs.Seed(PackId);
            return new AdventureGenerationService(
                new FakeJobs(),
                Packs,
                Users,
                new FailingCastResolver(failure),
                new ThrowingOpenAi(),
                new ThrowingNormalizer(),
                new ThrowingPdf(),
                new ThrowingBlobs(),
                Email,
                Notifier,
                new ThrowingSeriesMemory(),
                new ThrowingStoryRules(),
                Options.Create(new EmailOptions { BaseUrl = "https://example.ge" }),
                Options.Create(new OpenAiOptions()),
                NullLogger<AdventureGenerationService>.Instance);
        }
    }

    /// <summary>Fails the book the way a model that will not answer does.</summary>
    private sealed class FailingCastResolver(string message) : IBookCastResolver
    {
        public Task<BookCast> ResolveAsync(AdventurePack book, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);

        public Task CacheAppearanceAsync(Guid userId, BookCastMember member, string appearanceDescription, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>One row, with the claim, the progress writes and a real compare-and-set.</summary>
    private sealed class LegacyPacks(Guid ownerId) : IAdventurePackRepository
    {
        private AdventurePack _pack = new();

        public AdventurePackStatus Status => _pack.Status;
        public string? ErrorMessage => _pack.ErrorMessage;
        public string? ProgressMessage => _pack.ProgressMessage;

        /// <summary>Simulates the sweep landing its verdict between this job's read and its write.</summary>
        public Action? OnBeforeTransition { get; set; }

        public void Seed(Guid packId) => _pack = new AdventurePack
        {
            Id = packId,
            UserId = ownerId,
            Status = AdventurePackStatus.Pending,
            Title = "ბექა და ცისარტყელას ხიდი",
            ProgressMessage = "იწერება შენი უნიკალური ისტორია…",
            ProgressPercent = 18
        };

        public void Force(AdventurePackStatus status, string? errorMessage)
        {
            _pack.Status = status;
            _pack.ErrorMessage = errorMessage;
        }

        public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<AdventurePack?>(_pack);

        public Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken)
        {
            _pack.Status = status;
            return Task.FromResult(true);
        }

        public Task<bool> TryUpdateStatusAsync(Guid id, AdventurePackStatus expectedStatus, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken)
        {
            OnBeforeTransition?.Invoke();

            if (_pack.Status != expectedStatus)
            {
                return Task.FromResult(false);
            }

            _pack.Status = status;
            _pack.ErrorMessage = errorMessage;
            return Task.FromResult(true);
        }

        public Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken cancellationToken)
        {
            _pack.ProgressMessage = progressMessage;
            return Task.CompletedTask;
        }

        public Task UpdateProgressAsync(Guid id, string? progressMessage, int? progressPercent, CancellationToken cancellationToken)
        {
            _pack.ProgressMessage = progressMessage;
            _pack.ProgressPercent = progressPercent;
            return Task.CompletedTask;
        }

        public Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(Guid characterId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetAccessLevelAsync(Guid id, BookAccessLevel accessLevel, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateBookPresentationAsync(Guid id, string? title, string? coverImageUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(DateTime cutoffUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryFailStaleGenerationAsync(Guid id, AdventurePackStatus expectedStatus, DateTime cutoffUtc, string errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryFailAsync(Guid id, AdventurePackStatus expectedStatus, string errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdatePreviewIllustrationAsync(Guid id, PreviewIllustrationStatus status, string? illustrationUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryClaimPreviewIllustrationGenerationAsync(Guid id, int staleAfterMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateGeneratedJsonAsync(Guid id, string generatedJson, CancellationToken cancellationToken) => throw new NotSupportedException();

        // B5's discriminator and B7's withheld sweep. Neither is this double's subject: the pipeline
        // stamp is recorded so a test can read it back, and no test here asks for withheld books.
        public string? StampedPipeline { get; private set; }

        public Task SetGenerationPipelineAsync(Guid id, string pipeline, CancellationToken cancellationToken)
        {
            StampedPipeline = pipeline;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AdventurePack>> ListWithheldBekiPacksAsync(int limit, BekiWithheldCursor? after, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdventurePack>>([]);
    }

    private sealed class CountingNotifier : IAdminNotifier
    {
        public int Notifications { get; private set; }

        public Task BookFailedAsync(Guid packId, string reason, CancellationToken cancellationToken)
        {
            Notifications++;
            return Task.CompletedTask;
        }

        public Task OrderPaidAsync(Order order, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PrintOrderPlacedAsync(PrintOrder printOrder, string? bookTitle, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeJobs : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => Guid.NewGuid().ToString();
        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }

    private sealed class ThrowingOpenAi : IOpenAiService
    {
        public Task<AdventureContentDto> GenerateAdventureContentAsync(AdventureGenerationInput input, Guid adventureId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> GenerateStoryImageAsync(string imagePrompt, StoryImageReference? reference, CancellationToken cancellationToken, string? imageSize = null, bool requireReferences = false) => throw new NotSupportedException();
        public Task<string> ReviewIllustrationAsync(byte[] imageBytes, string reviewPrompt, IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> DescribeCharacterFromPhotoAsync(byte[] imageBytes, string contentType, string promptText, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CompleteTextAsync(string promptText, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingNormalizer : IReferenceImageNormalizer
    {
        public NormalizedReferenceImage NormalizeForOpenAi(byte[] bytes, string? hintContentType = null) => throw new NotSupportedException();
        public NormalizedReferenceImage NormalizeForStorageWebp(byte[] bytes, string? hintContentType = null) => throw new NotSupportedException();
    }

    private sealed class ThrowingPdf : IAdventurePdfService
    {
        public byte[] GeneratePdf(PdfBookRequest request) => throw new NotSupportedException();
    }

    private sealed class ThrowingBlobs : IBlobStorageService
    {
        public Task<string> UploadAsync(string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingSeriesMemory : ISeriesMemoryService
    {
        public Task<string?> GetPromptMemoryAsync(Guid seriesId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RecordBookAsync(AdventurePack book, string storyJson, string heroName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingStoryRules : IStoryRuleRepository
    {
        public Task<IReadOnlyList<StoryRule>> GetAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoryRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoryRule?> ResolveAsync(string ageBand, string theme, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(StoryRule rule, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

/// <summary>
/// The order status the generating screen polls, and the retry an operator reaches for when it
/// says the book is gone.
/// </summary>
public class OrderFailureVisibilityTests
{
    [Fact]
    public async Task A_failed_book_is_reported_as_failed_rather_than_as_still_working()
    {
        var world = new OrderWorld(AdventurePackStatus.Failed, OrderStatus.Fulfilled, fulfilled: true);
        world.Book.ErrorMessage = "IMAGE_QA_FAILED (spread 1): the child's hair does not match.";
        world.Book.ProgressMessage = "იხატება მე-2 გვერდი…";
        world.Book.ProgressPercent = 18;

        var status = await world.Service().GetStatusAsync(world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.True(status.BookFailed);
        Assert.False(status.BookReady);
        Assert.False(string.IsNullOrWhiteSpace(status.ParentMessage));

        // The whole point: neither field carries the operator's string.
        Assert.DoesNotContain("IMAGE_QA_FAILED", status.ParentMessage);
        Assert.DoesNotContain("IMAGE_QA_FAILED", status.ProgressMessage);

        // Nor does the frozen mid-generation line survive on a screen that has stopped moving.
        Assert.DoesNotContain("იხატება", status.ProgressMessage);
    }

    [Fact]
    public async Task A_generation_failure_is_never_reported_as_a_payment_failure()
    {
        // Two different conversations with a parent: a declined card is something they can act on,
        // a failed book is not. FailureReason stays the payment's own, and is null here because
        // the payment went through perfectly.
        var world = new OrderWorld(AdventurePackStatus.Failed, OrderStatus.Fulfilled, fulfilled: true);
        world.Book.ErrorMessage = "STORY_FAILED: the story call failed.";

        var status = await world.Service().GetStatusAsync(world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.Null(status.FailureReason);
        Assert.True(status.BookFailed);
    }

    [Fact]
    public async Task A_declined_payment_still_reports_its_own_reason_and_no_book_failure()
    {
        var world = new OrderWorld(AdventurePackStatus.Pending, OrderStatus.Failed, fulfilled: false);
        world.Order.FailureReason = "გადახდა არ დასრულდა ან ვადა გაუვიდა.";

        var status = await world.Service().GetStatusAsync(world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.Equal("გადახდა არ დასრულდა ან ვადა გაუვიდა.", status.FailureReason);
        Assert.False(status.BookFailed);
        Assert.Null(status.ParentMessage);
    }

    [Fact]
    public async Task A_book_that_is_still_being_drawn_reports_its_progress_untouched()
    {
        var world = new OrderWorld(AdventurePackStatus.GeneratingStory, OrderStatus.Fulfilled, fulfilled: true);
        world.Book.ProgressMessage = "იხატება მე-2 გვერდი…";

        var status = await world.Service().GetStatusAsync(world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.False(status.BookFailed);
        Assert.Null(status.ParentMessage);
        Assert.Equal("იხატება მე-2 გვერდი…", status.ProgressMessage);
    }

    [Fact]
    public async Task A_finished_book_is_still_reported_ready()
    {
        var world = new OrderWorld(AdventurePackStatus.Completed, OrderStatus.Fulfilled, fulfilled: true);

        var status = await world.Service().GetStatusAsync(world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.True(status.BookReady);
        Assert.False(status.BookFailed);
    }

    // -- two pipelines, two finishing lines (amendment B5) ------------------

    /// <summary>
    /// A legacy book stops at StoryReady, and always has: on that path the words and the pictures
    /// arrive together, so a book with a story is a book to read.
    /// </summary>
    [Fact]
    public async Task A_legacy_book_is_ready_at_story_ready_exactly_as_it_always_was()
    {
        var world = new OrderWorld(AdventurePackStatus.StoryReady, OrderStatus.Fulfilled, fulfilled: true);
        world.Book.GenerationPipeline = GenerationPipelines.Legacy;

        var status = await world.Service().GetStatusAsync(
            world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.True(status.BookReady);
    }

    /// <summary>
    /// A Beki book at StoryReady has the words and none of the pictures — the eight spreads are what
    /// the twenty-minute job is for.
    ///
    /// Reporting it ready is what sent a paying parent to a reader full of empty pages seconds after
    /// they paid, and then to a dashboard that showed the book as finished and stopped polling it.
    /// The correction needed a fact nobody had, which is what the pipeline column is.
    /// </summary>
    [Fact]
    public async Task A_beki_book_is_not_ready_until_it_is_completed()
    {
        var world = new OrderWorld(AdventurePackStatus.StoryReady, OrderStatus.Fulfilled, fulfilled: true);
        world.Book.GenerationPipeline = GenerationPipelines.Beki;
        world.Book.ProgressMessage = "იხატება მე-2 გვერდი…";

        var status = await world.Service().GetStatusAsync(
            world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.False(status.BookReady);
        Assert.False(status.BookFailed);

        // And the screen keeps its real line, so the wait is a wait rather than a blank.
        Assert.Equal("იხატება მე-2 გვერდი…", status.ProgressMessage);
    }

    /// <summary>The same book, finished, is ready — the finishing line moved, it did not vanish.</summary>
    [Fact]
    public async Task A_completed_beki_book_is_ready()
    {
        var world = new OrderWorld(AdventurePackStatus.Completed, OrderStatus.Fulfilled, fulfilled: true);
        world.Book.GenerationPipeline = GenerationPipelines.Beki;

        var status = await world.Service().GetStatusAsync(
            world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.True(status.BookReady);
    }

    /// <summary>
    /// A Completed Beki book whose download is withheld is still READY.
    ///
    /// The reader is the spreads and they are stored; what waits is the file the family can keep.
    /// Reporting the book as unready because a gate is holding a PDF would put the parent back on a
    /// spinner for a book they can already read — the same mistake in the other direction.
    /// </summary>
    [Fact]
    public async Task A_completed_beki_book_with_a_withheld_download_is_still_ready()
    {
        var world = new OrderWorld(AdventurePackStatus.Completed, OrderStatus.Fulfilled, fulfilled: true);
        world.Book.GenerationPipeline = GenerationPipelines.Beki;
        world.Book.PdfUrl = null;

        var status = await world.Service().GetStatusAsync(
            world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.True(status.BookReady);
    }

    // -- the retry --------------------------------------------------------

    [Fact]
    public async Task A_fulfilled_order_whose_book_failed_can_be_re_driven()
    {
        /*
          The hole this closes.

          Orders are marked fulfilled when generation is enqueued, so *every* generation failure
          happens to an order that already says Fulfilled — and the console's retry refused exactly
          those. The one failure that always needs an operator was the one the operator had no
          working button for.
        */
        var world = new OrderWorld(AdventurePackStatus.Failed, OrderStatus.Fulfilled, fulfilled: true);

        Assert.True(await world.Service().RequeueFulfilmentAsync(world.Order.Id, CancellationToken.None));
        Assert.Equal(1, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task The_job_the_retry_queues_actually_re_drives_the_order()
    {
        // A requeue that enqueued a job which then declined to do anything would be the same bug
        // one layer down: the operator is told it was queued and nothing happens. The predicate is
        // shared, so the job accepts precisely what the button accepted.
        var world = new OrderWorld(AdventurePackStatus.Failed, OrderStatus.Fulfilled, fulfilled: true);

        await world.Service().FulfilOrderAsync(world.Order.Id);

        Assert.Equal(1, world.Fulfilment.Calls);
    }

    [Fact]
    public async Task A_fulfilled_order_whose_book_is_fine_is_still_refused()
    {
        // Redrawing a finished book is how a family ends up with a different one from the one they
        // read, so the allowance is exactly as wide as the failure that justified it.
        var world = new OrderWorld(AdventurePackStatus.Completed, OrderStatus.Fulfilled, fulfilled: true);

        Assert.False(await world.Service().RequeueFulfilmentAsync(world.Order.Id, CancellationToken.None));
        Assert.Equal(0, world.Jobs.Enqueued);

        await world.Service().FulfilOrderAsync(world.Order.Id);
        Assert.Equal(0, world.Fulfilment.Calls);
    }

    [Theory]
    [InlineData(AdventurePackStatus.GeneratingStory)]
    [InlineData(AdventurePackStatus.GeneratingPdf)]
    [InlineData(AdventurePackStatus.StoryReady)]
    public async Task A_fulfilled_order_whose_book_is_still_in_flight_is_refused(AdventurePackStatus status)
    {
        var world = new OrderWorld(status, OrderStatus.Fulfilled, fulfilled: true);

        Assert.False(await world.Service().RequeueFulfilmentAsync(world.Order.Id, CancellationToken.None));
        Assert.Equal(0, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task A_fulfilled_print_upgrade_is_refused_even_when_its_book_failed()
    {
        /*
          The allowance is about generation, and a print upgrade does not generate anything.

          FulfillAsync dispatches on the order type: a PrintUpgrade goes to the branch that grants
          the print entitlement and returns, never reaching the revival branch that moves a pack
          out of Failed. Letting one through would answer the console with "queued", re-grant an
          entitlement the order already granted, and leave the book exactly as Failed as it was —
          with the operator told the retry had worked.
        */
        var world = new OrderWorld(AdventurePackStatus.Failed, OrderStatus.Fulfilled, fulfilled: true);
        world.Order.Type = OrderType.PrintUpgrade;

        Assert.False(await world.Service().RequeueFulfilmentAsync(world.Order.Id, CancellationToken.None));
        Assert.Equal(0, world.Jobs.Enqueued);

        // And the job says the same thing, so a hand-queued one cannot get in either.
        await world.Service().FulfilOrderAsync(world.Order.Id);
        Assert.Equal(0, world.Fulfilment.Calls);
    }

    [Fact]
    public async Task An_unfulfilled_print_upgrade_is_re_driven_exactly_as_before()
    {
        // The narrowing applies only to the fulfilled-order allowance. A print upgrade whose first
        // attempt died before it finished is an ordinary retry and always was.
        var world = new OrderWorld(AdventurePackStatus.Completed, OrderStatus.Paid, fulfilled: false);
        world.Order.Type = OrderType.PrintUpgrade;

        Assert.True(await world.Service().RequeueFulfilmentAsync(world.Order.Id, CancellationToken.None));
        Assert.Equal(1, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task A_fulfilled_order_with_no_book_at_all_is_refused()
    {
        // Nothing to look at, so nothing to conclude. The old refusal stands.
        var world = new OrderWorld(AdventurePackStatus.Failed, OrderStatus.Fulfilled, fulfilled: true);
        world.Order.BookId = null;

        Assert.False(await world.Service().RequeueFulfilmentAsync(world.Order.Id, CancellationToken.None));
        Assert.Equal(0, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task A_paid_order_that_was_never_fulfilled_is_re_driven_exactly_as_before()
    {
        var world = new OrderWorld(AdventurePackStatus.Pending, OrderStatus.Paid, fulfilled: false);

        Assert.True(await world.Service().RequeueFulfilmentAsync(world.Order.Id, CancellationToken.None));
        Assert.Equal(1, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task An_unpaid_order_is_refused_however_its_book_looks()
    {
        // No money, nothing to deliver — and a Failed book on an unpaid order is a preview that
        // did not work out, not a purchase that went wrong.
        var world = new OrderWorld(AdventurePackStatus.Failed, OrderStatus.Pending, fulfilled: false);

        Assert.False(await world.Service().RequeueFulfilmentAsync(world.Order.Id, CancellationToken.None));
        Assert.Equal(0, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task An_order_that_does_not_exist_is_refused_rather_than_thrown_over()
    {
        var world = new OrderWorld(AdventurePackStatus.Failed, OrderStatus.Fulfilled, fulfilled: true);

        Assert.False(await world.Service().RequeueFulfilmentAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // -- the retry, for a book no job ever claimed ------------------------

    [Fact]
    public async Task A_fulfilled_beki_order_whose_book_no_job_ever_claimed_can_be_re_driven()
    {
        /*
          The second hole, beside the Failed one.

          Fulfilment adopts the story into StoryReady, then enqueues; the order is stamped
          Fulfilled the moment the enqueue returns. A job that was never posted, or that died
          before its claim, leaves a paid Beki book resting at StoryReady: not Failed, so the
          button refused it; not a working status, so the sweep never buried it. The parent
          polled forever. The console, the button and the job now all accept it — once it has
          waited longer than a fulfilment is allowed to before being called stalled.
        */
        var world = new OrderWorld(AdventurePackStatus.StoryReady, OrderStatus.Fulfilled, fulfilled: true);
        world.Book.GenerationPipeline = GenerationPipelines.Beki;
        world.Book.GenerationHeartbeatUtc = DateTime.UtcNow.AddMinutes(-20);

        Assert.True(await world.Service().CanRedriveAsync(world.Order.Id, CancellationToken.None));
        Assert.True(await world.Service().RequeueFulfilmentAsync(world.Order.Id, CancellationToken.None));
        Assert.Equal(1, world.Jobs.Enqueued);

        await world.Service().FulfilOrderAsync(world.Order.Id);
        Assert.Equal(1, world.Fulfilment.Calls);
    }

    [Theory]
    [InlineData(GenerationPipelines.Beki)]
    [InlineData(GenerationPipelines.Legacy)]
    public async Task A_fulfilled_order_whose_book_is_still_pending_can_be_re_driven_once_it_has_waited(string pipeline)
    {
        // Pending is the status fulfilment creates a pack in, on either pipeline, and nothing
        // rests there: a book still Pending after the allowance has no job behind it.
        var world = new OrderWorld(AdventurePackStatus.Pending, OrderStatus.Fulfilled, fulfilled: true);
        world.Book.GenerationPipeline = pipeline;
        world.Book.GenerationHeartbeatUtc = null;
        world.Book.CreatedAt = DateTime.UtcNow.AddMinutes(-20);

        Assert.True(await world.Service().CanRedriveAsync(world.Order.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_book_that_may_only_be_queued_is_not_offered_for_retry_yet()
    {
        // A queued job and a lost one look the same until the claim, so the rule waits. Offering
        // the button at once would have an operator re-queue books that are merely behind others.
        var world = new OrderWorld(AdventurePackStatus.StoryReady, OrderStatus.Fulfilled, fulfilled: true);
        world.Book.GenerationPipeline = GenerationPipelines.Beki;
        world.Book.GenerationHeartbeatUtc = DateTime.UtcNow.AddMinutes(-1);

        Assert.False(await world.Service().CanRedriveAsync(world.Order.Id, CancellationToken.None));
        Assert.False(await world.Service().RequeueFulfilmentAsync(world.Order.Id, CancellationToken.None));
        Assert.Equal(0, world.Jobs.Enqueued);
    }

    [Fact]
    public async Task A_legacy_book_at_story_ready_is_finished_and_is_never_offered_for_retry()
    {
        // However long it has rested there: on the legacy pipeline StoryReady is the book the
        // parent already reads, and re-driving it would redraw what they have read.
        var world = new OrderWorld(AdventurePackStatus.StoryReady, OrderStatus.Fulfilled, fulfilled: true);
        world.Book.GenerationPipeline = GenerationPipelines.Legacy;
        world.Book.GenerationHeartbeatUtc = DateTime.UtcNow.AddHours(-3);

        Assert.False(await world.Service().CanRedriveAsync(world.Order.Id, CancellationToken.None));
    }

    [Fact]
    public async Task The_console_asks_the_same_rule_the_button_applies()
    {
        // One predicate behind both, so the console cannot show a button the button will refuse.
        var failed = new OrderWorld(AdventurePackStatus.Failed, OrderStatus.Fulfilled, fulfilled: true);
        Assert.True(await failed.Service().CanRedriveAsync(failed.Order.Id, CancellationToken.None));

        var fine = new OrderWorld(AdventurePackStatus.Completed, OrderStatus.Fulfilled, fulfilled: true);
        Assert.False(await fine.Service().CanRedriveAsync(fine.Order.Id, CancellationToken.None));

        Assert.False(await fine.Service().CanRedriveAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // -- the wait, described --------------------------------------------

    [Fact]
    public async Task The_wait_is_described_rather_than_only_asserted()
    {
        // Readiness alone told the generating screen to keep spinning. A reload from the bank has
        // nothing but an order id, and showed a generic book for "შენი გმირი"; each of these is the
        // pack's own column, copied as stored.
        var heartbeat = new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc);
        var world = new OrderWorld(AdventurePackStatus.GeneratingStory, OrderStatus.Fulfilled, fulfilled: true);
        world.Book.GenerationPipeline = GenerationPipelines.Beki;
        world.Book.ProgressMessage = "იხატება მე-2 გვერდი…";
        world.Book.ProgressPercent = 18;
        world.Book.GenerationHeartbeatUtc = heartbeat;
        world.Book.WorldId = "dinosaurs";
        world.Book.CoverImageUrl = "covers/abc.webp";

        var status = await world.Service().GetStatusAsync(world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.Equal(18, status.ProgressPercent);
        Assert.Equal("GeneratingStory", status.PackStatus);
        Assert.Equal(heartbeat, status.HeartbeatUtc);
        Assert.Equal("ბექა და ცისარტყელას ხიდი", status.Title);
        Assert.Equal("dinosaurs", status.WorldId);
        Assert.Equal("covers/abc.webp", status.CoverImageUrl);

        // No hero on this book, so no name — and no exception either.
        Assert.Null(status.ChildName);

        // The old fields are exactly what they were.
        Assert.False(status.BookReady);
        Assert.False(status.BookFailed);
        Assert.Equal("იხატება მე-2 გვერდი…", status.ProgressMessage);
    }

    [Fact]
    public async Task The_hero_is_named_from_the_character_the_book_is_about()
    {
        var hero = Guid.NewGuid();
        var world = new OrderWorld(
            AdventurePackStatus.GeneratingStory, OrderStatus.Fulfilled, fulfilled: true,
            characters: new NamedCharacters(hero, " ბექა "));
        world.Book.PrimaryCharacterId = hero;

        var status = await world.Service().GetStatusAsync(world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.Equal("ბექა", status.ChildName);
    }

    [Fact]
    public async Task A_name_lookup_that_fails_does_not_fail_the_poll()
    {
        // The default double refuses every character read. A poll that answers "is my book
        // ready" must not fall over because a name could not be fetched.
        var world = new OrderWorld(AdventurePackStatus.GeneratingStory, OrderStatus.Fulfilled, fulfilled: true);
        world.Book.PrimaryCharacterId = Guid.NewGuid();

        var status = await world.Service().GetStatusAsync(world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.Null(status.ChildName);
        Assert.Equal("GeneratingStory", status.PackStatus);
    }

    [Fact]
    public async Task A_paid_order_whose_book_row_does_not_exist_yet_still_describes_the_book()
    {
        /*
          The window between the payment landing and the pack being written — seconds inline, or
          a sweep's interval after a fulfilment that died. The screen used to get nothing at all
          here. It gets the line the pack will open with, and what the frozen draft already knows.
        */
        var hero = Guid.NewGuid();
        var world = new OrderWorld(
            AdventurePackStatus.Pending, OrderStatus.Paid, fulfilled: false,
            characters: new NamedCharacters(hero, "ბექა"));
        world.Order.BookId = null;
        world.Order.DraftJson = System.Text.Json.JsonSerializer.Serialize(
            new BookDraftRequest { PrimaryCharacterId = hero, WorldId = "Dinosaurs" },
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        var status = await world.Service().GetStatusAsync(world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.False(status.BookReady);
        Assert.False(status.BookFailed);
        Assert.Null(status.BookId);
        Assert.False(string.IsNullOrWhiteSpace(status.ProgressMessage));
        Assert.Equal("dinosaurs", status.WorldId);
        Assert.Equal("ბექა", status.ChildName);

        // Nothing is invented about a row that does not exist.
        Assert.Null(status.PackStatus);
        Assert.Null(status.ProgressPercent);
        Assert.Null(status.HeartbeatUtc);
        Assert.Null(status.Title);
    }

    [Fact]
    public async Task An_unpaid_order_with_no_book_carries_no_progress_line()
    {
        // Nothing is being made, so nothing says it is. A draft that will not parse is simply
        // not read.
        var world = new OrderWorld(AdventurePackStatus.Pending, OrderStatus.Pending, fulfilled: false);
        world.Order.BookId = null;
        world.Order.DraftJson = "{not json";

        var status = await world.Service().GetStatusAsync(world.Order.UserId, world.Order.Id, CancellationToken.None);

        Assert.Null(status.ProgressMessage);
        Assert.Null(status.WorldId);
        Assert.Null(status.ChildName);
    }

    // -- harness ---------------------------------------------------------

    /// <summary>
    /// One paid order and the book it bought. Only the four collaborators these paths touch do
    /// anything; every other call on every other interface throws, so a change that starts
    /// reaching for one fails here rather than passing quietly.
    /// </summary>
    private sealed class OrderWorld
    {
        public Order Order { get; }
        public AdventurePack Book { get; }
        public FakeJobs Jobs { get; } = new();
        public FakeFulfilment Fulfilment { get; } = new();

        private readonly ICharacterRepository _characters;

        public OrderWorld(
            AdventurePackStatus bookStatus,
            OrderStatus orderStatus,
            bool fulfilled,
            ICharacterRepository? characters = null)
        {
            _characters = characters ?? new ThrowingCharacters();

            var userId = Guid.NewGuid();
            var bookId = Guid.NewGuid();

            Book = new AdventurePack
            {
                Id = bookId,
                UserId = userId,
                Status = bookStatus,
                AccessLevel = BookAccessLevel.Full,
                Title = "ბექა და ცისარტყელას ხიდი"
            };

            Order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BookId = bookId,
                Type = OrderType.NewBook,
                Package = OrderPackage.Digital,
                Status = orderStatus,
                PaidAt = orderStatus is OrderStatus.Paid or OrderStatus.Fulfilled ? DateTime.UtcNow : null,
                FulfilledAt = fulfilled ? DateTime.UtcNow : null
            };
        }

        public OrderService Service() =>
            new(new FakeOrders(Order),
                new ThrowingPromoCodes(),
                Fulfilment,
                new FakePacks(Book),
                _characters,
                new ThrowingWorldProgress(),
                new ThrowingPromoCodeRepository(),
                new ThrowingUsers(),
                new ThrowingAdminNotifier(),
                Jobs,
                new ThrowingBog(),
                Options.Create(new StripeOptions()),
                Options.Create(new BogOptions()),
                NullLogger<OrderService>.Instance);
    }

    private sealed class FakeJobs : IBackgroundJobClient
    {
        public int Enqueued { get; private set; }

        public string Create(Job job, IState state)
        {
            Enqueued++;
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }

    private sealed class FakeFulfilment : IBookFulfillmentService
    {
        public int Calls { get; private set; }

        public Task<Guid> FulfillAsync(Order order, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(order.BookId ?? Guid.NewGuid());
        }
    }

    private sealed class FakeOrders(Order seed) : IOrderRepository
    {
        public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == seed.Id ? seed : null);

        public Task<Order?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(id == seed.Id && userId == seed.UserId ? seed : null);

        public Task<bool> TryMarkFulfilledAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<Guid> CreateAsync(Order order, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Order?> GetByProviderSessionIdAsync(string providerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetPaidForBookAsync(Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AttachProviderSessionAsync(Guid id, string providerSessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetBookIdAsync(Guid id, Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryMarkPaidAsync(Guid id, string? providerPaymentIntentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkFailedAsync(Guid id, string reason, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryCancelAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Order>> GetStalledPaidAsync(DateTime paidBeforeUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakePacks(AdventurePack seed) : IAdventurePackRepository
    {
        public Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(id == seed.Id && userId == seed.UserId ? seed : null);

        public Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(Guid characterId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetAccessLevelAsync(Guid id, BookAccessLevel accessLevel, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateBookPresentationAsync(Guid id, string? title, string? coverImageUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryUpdateStatusAsync(Guid id, AdventurePackStatus expectedStatus, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(DateTime cutoffUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryFailStaleGenerationAsync(Guid id, AdventurePackStatus expectedStatus, DateTime cutoffUtc, string errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryFailAsync(Guid id, AdventurePackStatus expectedStatus, string errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressAsync(Guid id, string? progressMessage, int? progressPercent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdatePreviewIllustrationAsync(Guid id, PreviewIllustrationStatus status, string? illustrationUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryClaimPreviewIllustrationGenerationAsync(Guid id, int staleAfterMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateGeneratedJsonAsync(Guid id, string generatedJson, CancellationToken cancellationToken) => throw new NotSupportedException();

        // B5's discriminator and B7's withheld sweep. Neither is this double's subject: the pipeline
        // stamp is recorded so a test can read it back, and no test here asks for withheld books.
        public string? StampedPipeline { get; private set; }

        public Task SetGenerationPipelineAsync(Guid id, string pipeline, CancellationToken cancellationToken)
        {
            StampedPipeline = pipeline;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AdventurePack>> ListWithheldBekiPacksAsync(int limit, BekiWithheldCursor? after, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdventurePack>>([]);
    }

    private sealed class ThrowingPromoCodes : IPromoCodeService
    {
        public Task<PricedOrder> PriceAsync(Guid userId, OrderType type, OrderPackage package, string? promoCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<QuoteResponse> QuoteAsync(Guid userId, OrderType type, OrderPackage package, string? promoCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryRedeemAsync(Order order, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingPromoCodeRepository : IPromoCodeRepository
    {
        public Task<PromoCode?> GetByCodeAsync(string code, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PromoCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasUserRedeemedAsync(Guid promoCodeId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryRedeemAsync(PromoRedemption redemption, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingUsers : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<User?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<User?> GetByConfirmationTokenAsync(string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> CreateAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PurgeDemoAccountsAsync(string emailSuffix, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateSubscriptionTypeAsync(Guid userId, SubscriptionType subscriptionType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ConfirmEmailAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetAdminAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> AttachPhoneNumberAsync(Guid userId, string phoneNumber, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> AttachEmailAsync(Guid userId, string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? preferredLanguage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddBookCreditsAsync(Guid userId, int credits, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryConsumeBookCreditAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RefundBookCreditAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryConsumeWelcomeStoryAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RefundWelcomeStoryAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingAdminNotifier : IAdminNotifier
    {
        public Task OrderPaidAsync(Order order, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task BookFailedAsync(Guid packId, string reason, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PrintOrderPlacedAsync(PrintOrder printOrder, string? bookTitle, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingBog : IBogPaymentClient
    {
        public Task<BogCheckout> CreateOrderAsync(BogOrderRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BogPaymentDetails?> GetPaymentDetailsAsync(string bogOrderId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public bool VerifyCallbackSignature(byte[] payload, string? signature) => throw new NotSupportedException();
    }

    private sealed class ThrowingCharacters : ICharacterRepository
    {
        public Task<IReadOnlyList<Character>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetHeroesAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetHeroPortraitUrlsAsync(Guid userId, IReadOnlyCollection<Guid> characterIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Character?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> CreateAsync(Character character, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(Character character, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateAppearanceCacheAsync(Guid id, Guid userId, string? appearanceDescription, string? appearancePhotoUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsCastInAnyBookAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<Guid>> GetCastCharacterIdsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetByBookIdAsync(Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetBookCastAsync(Guid bookId, IReadOnlyList<Guid> characterIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>One hero with a name; every other read is refused, as the default double's are.</summary>
    private sealed class NamedCharacters(Guid heroId, string name) : ICharacterRepository
    {
        public Task<Character?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<Character?>(id == heroId ? new Character { Id = heroId, UserId = userId, Name = name } : null);

        public Task<IReadOnlyList<Character>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetHeroesAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetHeroPortraitUrlsAsync(Guid userId, IReadOnlyCollection<Guid> characterIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> CreateAsync(Character character, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(Character character, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateAppearanceCacheAsync(Guid id, Guid userId, string? appearanceDescription, string? appearancePhotoUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsCastInAnyBookAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<Guid>> GetCastCharacterIdsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Character>> GetByBookIdAsync(Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetBookCastAsync(Guid bookId, IReadOnlyList<Guid> characterIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingWorldProgress : IWorldProgressService
    {
        public Task<IReadOnlyList<AdventurePacks.Api.DTOs.Worlds.WorldResponse>> GetCatalogueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventurePacks.Api.DTOs.Worlds.AdventureMapResponse> GetMapAsync(Guid userId, Guid characterId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePacks.Api.DTOs.Worlds.AdventureMapResponse>> GetMapsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnsureCanStartAsync(Guid userId, Guid characterId, string worldId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkStartedAsync(Guid userId, Guid characterId, string worldId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkCompletedAsync(Guid userId, Guid characterId, string worldId, Guid bookId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
