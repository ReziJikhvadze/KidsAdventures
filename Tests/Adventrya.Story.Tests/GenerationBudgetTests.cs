using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Prompts;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// The wall clock a generation job runs under, and the one distinction that makes it safe.
///
/// Both long jobs ran under <c>CancellationToken.None</c> and swallowed every exception into a
/// terminal Failed. That is wrong twice over: nothing stopped a job hung inside a twelve-minute
/// call, and a job stopped because the site was being deployed was written down as a book that
/// could not be made — when it was a book Hangfire was about to hand to another worker, with a
/// resume path waiting for it.
///
/// So a cancellation now has to say which clock it came from. A deadline that fires is terminal; a
/// host that is shutting down is rethrown. Every test here drives that decision through a timer it
/// controls, because the alternative is a suite that takes thirty minutes to tell you anything.
/// </summary>
public class GenerationBudgetTests
{
    // -- the rule itself -------------------------------------------------

    [Fact]
    public void The_budget_fired_only_when_the_host_did_not()
    {
        using var deadline = new CancellationTokenSource();
        using var host = new CancellationTokenSource();

        Assert.False(GenerationBudget.Expired(deadline.Token, host.Token));

        deadline.Cancel();
        Assert.True(GenerationBudget.Expired(deadline.Token, host.Token));
    }

    [Fact]
    public void When_both_fire_the_host_is_the_reason()
    {
        /*
          Not a tie-break for tidiness. During a shutdown both tokens genuinely end up cancelled —
          the linked source cancels, the deadline's registration runs, or the timer simply lands in
          the same millisecond — and calling that a budget failure would mark a perfectly resumable
          paid book as dead on every deployment.
        */
        using var deadline = new CancellationTokenSource();
        using var host = new CancellationTokenSource();

        deadline.Cancel();
        host.Cancel();

        Assert.False(GenerationBudget.Expired(deadline.Token, host.Token));
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(45, 45)]
    [InlineData(0, 30)]
    [InlineData(-1, 30)]
    public void A_missing_or_nonsense_budget_falls_back_to_the_default(int configured, int expected)
    {
        // Zero would mean "cancel immediately", which turns one bad settings box into every book
        // failing the moment it starts.
        Assert.Equal(TimeSpan.FromMinutes(expected), GenerationBudget.For(configured));
    }

    [Fact]
    public void The_reason_names_the_code_the_budget_and_the_stage()
    {
        var reason = GenerationBudget.ExceededReason(TimeSpan.FromMinutes(30), "drawing the spreads");

        Assert.StartsWith(GenerationBudget.ExceededCode, reason);
        Assert.Contains("30 minutes", reason);
        Assert.Contains("drawing the spreads", reason);
    }

    // -- the paid book ---------------------------------------------------

    [Fact]
    public async Task A_pack_whose_budget_runs_out_is_failed_terminally()
    {
        var world = new PackWorld();
        var job = world.Job();

        await job.ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.Equal(AdventurePackStatus.Failed, world.Packs.StatusOf(world.PackId));
        Assert.StartsWith(GenerationBudget.ExceededCode, world.Packs.ErrorOf(world.PackId)!);

        // The stage the job was in when the clock ran out, so the log and the row both say where
        // the half hour went.
        Assert.Contains("drawing the spreads", world.Packs.ErrorOf(world.PackId)!);

        // A paid book that failed always needs a person: the sweep tells nobody, so the job must.
        Assert.Equal(1, world.Notifier.Notifications);
    }

    [Fact]
    public async Task A_pack_stopped_by_the_host_is_left_for_the_requeue()
    {
        /*
          The regression this whole split exists for.

          A deploy used to reach the generator's await, raise a cancellation, land in the catch-all
          and mark the pack Failed — a paid book declared unmakeable by a restart, one requeue away
          from finishing off its stored manifest. Now the exception goes back to Hangfire and the
          pack stays exactly where the next attempt can pick it up.
        */
        using var host = new CancellationTokenSource();
        var world = new PackWorld { CancelHostInstead = host };
        var job = world.Job();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => job.ProcessAsync(world.PackId, world.RunId, host.Token));

        Assert.Equal(AdventurePackStatus.GeneratingStory, world.Packs.StatusOf(world.PackId));
        Assert.Null(world.Packs.ErrorOf(world.PackId));
        Assert.Equal(0, world.Notifier.Notifications);
    }

    [Fact]
    public async Task A_pack_that_fails_for_an_ordinary_reason_still_fails_the_way_it_always_did()
    {
        // The split must not have changed what happens to a book that simply broke.
        var world = new PackWorld { GeneratorFailure = new InvalidOperationException("the model refused") };
        var job = world.Job();

        await job.ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.Equal(AdventurePackStatus.Failed, world.Packs.StatusOf(world.PackId));
        Assert.Equal("the model refused", world.Packs.ErrorOf(world.PackId));
        Assert.Equal(1, world.Notifier.Notifications);
    }

    [Fact]
    public async Task A_pack_the_sweep_already_buried_is_not_quietly_overwritten()
    {
        /*
          The compare-and-set on the job's own terminal write.

          A job that comes back to life after the sweep has recorded why the book was abandoned must
          not overwrite that verdict — the stored reason is the only thing that says the book took
          three quarters of an hour and nobody was watching.
        */
        var world = new PackWorld { GeneratorFailure = new InvalidOperationException("too late") };
        world.Packs.OnBeforeWrite = () => world.Packs.Force(world.PackId, AdventurePackStatus.Failed, "swept");

        var job = world.Job();
        await job.ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.Equal("swept", world.Packs.ErrorOf(world.PackId));

        // Still told. A book that failed is money taken with nothing delivered whichever writer
        // recorded it, and the sweep notifies nobody.
        Assert.Equal(1, world.Notifier.Notifications);
    }

    [Theory]
    [InlineData(AdventurePackStatus.Failed)]
    [InlineData(AdventurePackStatus.Generating)]
    public async Task A_pack_the_sweep_declared_dead_is_not_claimed_back_to_life(AdventurePackStatus status)
    {
        /*
          A requeued attempt used to claim any non-Completed pack straight into GeneratingStory.

          That quietly undid the one verdict written from outside this process: a book the sweep
          declared abandoned at forty minutes would be revived by the next retry and redrawn, and —
          because the redraw starts from the manifest — plausibly succeed, leaving nothing anywhere
          saying it had ever been lost. It also made Failed a status a book could bounce out of,
          which is the opposite of terminal.
        */
        var world = new PackWorld(status);
        var job = world.Job();

        await job.ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        // No claim, no drawing, and nobody paged: this job did not fail anything, it declined to
        // start.
        Assert.Equal(status, world.Packs.StatusOf(world.PackId));
        Assert.Equal(0, world.Notifier.Notifications);
    }

    [Theory]
    [InlineData(AdventurePackStatus.StoryReady)]
    [InlineData(AdventurePackStatus.Pending)]
    [InlineData(AdventurePackStatus.GeneratingStory)]
    public async Task The_statuses_a_real_book_arrives_in_are_still_claimed(AdventurePackStatus status)
    {
        // StoryReady is the ordinary case and Pending is the same book when preview adoption
        // failed; GeneratingStory is a requeue of an attempt that had already claimed the pack.
        // A guard that refused any of these would refuse every book.
        var world = new PackWorld(status) { GeneratorFailure = new InvalidOperationException("drew nothing") };
        var job = world.Job();

        await job.ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        // Reaching the generator at all is the assertion; the failure is how we know it got there.
        Assert.Equal(AdventurePackStatus.Failed, world.Packs.StatusOf(world.PackId));
    }

    [Fact]
    public async Task Nobody_is_paged_when_the_book_turned_out_to_be_completed()
    {
        /*
          The notification used to fire on every failure, including one whose terminal write lost to
          a Completed. That is a page-out about a book that exists — the worst kind, because it
          teaches an operator that the alert means nothing.

          It still fires when the write loses to a Failed, because the only writer that beats it
          there is the sweep, and the sweep tells nobody.
        */
        var world = new PackWorld { GeneratorFailure = new InvalidOperationException("too late") };
        world.Packs.OnBeforeWrite = () => world.Packs.Force(world.PackId, AdventurePackStatus.Completed, null);

        var job = world.Job();
        await job.ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.Equal(AdventurePackStatus.Completed, world.Packs.StatusOf(world.PackId));
        Assert.Equal(0, world.Notifier.Notifications);
    }

    [Fact]
    public async Task A_budget_that_expires_during_the_opening_read_still_ends_terminally()
    {
        /*
          The opening SELECT used to sit above the try.

          A deadline expiring while it was outstanding threw past every handler: no terminal status,
          no classification of the cause, and a Hangfire retry that would do the same again. The
          pack would rest in the status it was enqueued in — StoryReady — which the sweep
          deliberately does not touch, because that is also the status a pack holds while it waits
          in the queue. Nothing anywhere would ever close the case.

          Now the read is guarded, and the handler goes and looks at the row on a fresh token rather
          than guessing what to compare against.
        */
        var world = new PackWorld();
        world.Packs.OnFirstRead = async token =>
        {
            world.Clock.FireAll();
            await Task.Delay(Timeout.Infinite, token);
        };

        var job = world.Job();
        await job.ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.Equal(AdventurePackStatus.Failed, world.Packs.StatusOf(world.PackId));
        Assert.StartsWith(GenerationBudget.ExceededCode, world.Packs.ErrorOf(world.PackId)!);
        Assert.Contains("loading the pack", world.Packs.ErrorOf(world.PackId)!);
        Assert.Equal(1, world.Notifier.Notifications);
    }

    [Fact]
    public async Task A_host_shutdown_during_the_opening_read_is_still_a_requeue()
    {
        // The other half of the same move: guarding the read must not turn a deploy into a failed
        // book, which is the whole point of separating the two causes.
        using var host = new CancellationTokenSource();
        var world = new PackWorld();
        world.Packs.OnFirstRead = async token =>
        {
            await host.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
        };

        var job = world.Job();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => job.ProcessAsync(world.PackId, world.RunId, host.Token));

        Assert.Equal(AdventurePackStatus.StoryReady, world.Packs.StatusOf(world.PackId));
        Assert.Equal(0, world.Notifier.Notifications);
    }

    // -- the free preview ------------------------------------------------

    [Fact]
    public async Task A_preview_run_whose_budget_runs_out_is_failed_terminally()
    {
        var world = new RunWorld();

        await world.Service().WriteBookAsync(world.RunId, CancellationToken.None);

        Assert.Equal(MasterStoryRunStatus.Failed, world.Runs.Status);
        Assert.StartsWith(GenerationBudget.ExceededCode, world.Runs.Error!);
        Assert.Contains("writing the story", world.Runs.Error!);
    }

    [Fact]
    public async Task A_preview_run_stopped_by_the_host_is_left_for_the_requeue()
    {
        // Left in Writing, unfailed: the next attempt reads the run back and resumes at the cover
        // if the story had already been saved — a branch that could never be reached while every
        // cancellation was being swallowed.
        using var host = new CancellationTokenSource();
        var world = new RunWorld { CancelHostInstead = host };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => world.Service().WriteBookAsync(world.RunId, host.Token));

        Assert.Equal(MasterStoryRunStatus.Writing, world.Runs.Status);
        Assert.Null(world.Runs.Error);
    }

    [Fact]
    public async Task A_budget_that_expires_on_the_cover_keeps_the_story_the_parent_is_waiting_for()
    {
        /*
          The cover is the one part of a preview allowed to be missing: the reader opens on the
          world's own artwork and swaps in the real cover when it lands. So a deadline that expires
          while the cover is being drawn must not roll a written, projected, saved story back to
          Failed — that throws away the expensive part over the cheap one.

          It reaches a handler at all only because the cover helpers stopped swallowing
          cancellations, which is what previously made a shutdown here indistinguishable from a
          cover that simply could not be drawn.
        */
        var world = new RunWorld { CancelDuringCover = true };

        await world.Service().WriteBookAsync(world.RunId, CancellationToken.None);

        Assert.Equal(MasterStoryRunStatus.Ready, world.Runs.Status);
        Assert.Null(world.Runs.Error);
        Assert.Null(world.Runs.CoverUrl);

        // It got as far as asking for the picture — otherwise "Ready with no cover" would be
        // proving nothing at all.
        Assert.Equal(1, world.CoverAttempts);
    }

    [Fact]
    public async Task A_shutdown_on_the_cover_is_requeued_rather_than_silently_losing_it()
    {
        /*
          The fault this pair was written for: swallowed inside the cover helper, a shutdown looked
          exactly like a cover that could not be drawn. The job reported success, Hangfire never
          requeued it, and the run kept a missing cover permanently.

          Rethrown, Hangfire tries again and the resume branch draws the cover it never got.
        */
        using var host = new CancellationTokenSource();
        var world = new RunWorld { CancelDuringCover = true, CancelHostInstead = host };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => world.Service().WriteBookAsync(world.RunId, host.Token));

        // The story survived — it is saved and the run is Ready — and the exception is what gets
        // the cover retried rather than abandoned.
        Assert.Equal(MasterStoryRunStatus.Ready, world.Runs.Status);
        Assert.Null(world.Runs.Error);
        Assert.Equal(1, world.CoverAttempts);
    }

    // -- harness ---------------------------------------------------------

    /// <summary>
    /// A timer nobody has to wait for. <see cref="CancellationTokenSource"/> asks its
    /// <see cref="TimeProvider"/> for the timer that will cancel it, so handing the job this one
    /// lets a test decide the exact moment the half hour is up — from inside the call that would
    /// have been running when it happened.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            lock (_timers)
            {
                _timers.Add(timer);
            }

            return timer;
        }

        /// <summary>The deadline passes, now.</summary>
        public void FireAll()
        {
            ManualTimer[] snapshot;
            lock (_timers)
            {
                snapshot = _timers.ToArray();
            }

            foreach (var timer in snapshot)
            {
                timer.Fire();
            }
        }

        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            public void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Everything the fulfilment job touches, wired so the run stops inside the generator — the
    /// place a book actually spends its minutes.
    /// </summary>
    private sealed class PackWorld
    {
        public Guid PackId { get; } = Guid.NewGuid();
        public Guid RunId { get; } = Guid.NewGuid();
        public FakePacks Packs { get; }
        public CountingNotifier Notifier { get; } = new();
        public ManualTimeProvider Clock { get; } = new();

        /// <summary>Set to cancel the host's token instead of letting the deadline pass.</summary>
        public CancellationTokenSource? CancelHostInstead { get; init; }

        /// <summary>Set to fail the book for an ordinary reason rather than by cancellation.</summary>
        public Exception? GeneratorFailure { get; init; }

        public PackWorld(AdventurePackStatus status = AdventurePackStatus.StoryReady) =>
            // StoryReady by default, because that is what a real Beki pack arrives in: order
            // fulfilment adopts the previewed story — which writes StoryReady — and only then
            // enqueues this job.
            Packs = new FakePacks(new AdventurePack
            {
                Id = PackId,
                UserId = Guid.NewGuid(),
                Theme = ThemeType.Dinosaurs,
                Status = status,
                CreatedAt = DateTime.UtcNow
            });

        public BekiPackFulfillment Job() =>
            new(Packs,
                new FakeRuns(RunId),
                new FakeBlobs(),
                new CancellingGenerator(this),
                new ThrowingComposer(),
                Notifier,
                Options.Create(new BekiOptions()),
                NullLogger<BekiPackFulfillment>.Instance,
                Clock);

        /// <summary>
        /// Stops the book the way the test asked for, from inside the call that draws it: either
        /// the deadline passes, or the host does, and then the generator waits on the token it was
        /// given — which is what any real image call is doing at that moment.
        /// </summary>
        private sealed class CancellingGenerator(PackWorld world) : IBekiBookGenerator
        {
            public Task<BekiBookResult> GenerateAsync(
                MasterStoryInput input, byte[] childPhoto, string childPhotoContentType,
                CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<BekiImageResult> DrawCoverAsync(
                MasterStory plan, byte[] childPhoto, string childPhotoContentType,
                CancellationToken cancellationToken, CompositeBookContext? composite = null) =>
                throw new NotSupportedException();

            public async Task<BekiBookResult> IllustrateAsync(
                MasterStory plan,
                byte[] childPhoto,
                string childPhotoContentType,
                byte[]? existingCover,
                Func<BekiImageResult, Task>? onImage,
                CancellationToken cancellationToken,
                IReadOnlyDictionary<int, byte[]>? existingSpreads = null,
                CompositeBookContext? composite = null)
            {
                if (world.GeneratorFailure is { } failure)
                {
                    throw failure;
                }

                if (world.CancelHostInstead is { } host)
                {
                    await host.CancelAsync();
                }
                else
                {
                    world.Clock.FireAll();
                }

                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new UnreachableException();
            }
        }
    }

    /// <summary>The same for the preview job, stopping inside the story call.</summary>
    private sealed class RunWorld
    {
        public Guid RunId { get; } = Guid.NewGuid();
        public FakeRunRepository Runs { get; }
        public ManualTimeProvider Clock { get; } = new();

        public CancellationTokenSource? CancelHostInstead { get; init; }

        /// <summary>
        /// Let the story succeed and stop the cover instead — the case where the expensive half is
        /// already saved and only the picture on the front is lost.
        /// </summary>
        public bool CancelDuringCover { get; init; }

        /// <summary>
        /// How many times the cover was actually asked for. Without it, both cover tests would
        /// still pass if the run never reached the cover at all — a Ready run with no cover is
        /// exactly what they assert.
        /// </summary>
        public int CoverAttempts { get; private set; }

        public RunWorld() => Runs = new FakeRunRepository(RunId);

        public MasterBookService Service() =>
            new(Runs,
                new CancellingStoryService(this),
                new CancellingImageService(this),
                new FakeBlobs(),
                new ThrowingNormalizer(),
                new ThrowingJobClient(),
                new ThrowingBookGenerator(),
                Options.Create(new BekiOptions()),
                NullLogger<MasterBookService>.Instance,
                Clock);

        /// <summary>Cancels the way the test asked, from inside whichever call it named.</summary>
        internal async Task StopNowAsync(CancellationToken cancellationToken)
        {
            if (CancelHostInstead is { } host)
            {
                await host.CancelAsync();
            }
            else
            {
                Clock.FireAll();
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        }

        /// <summary>Only ever reached when the test asked for the cover to be the casualty.</summary>
        private sealed class CancellingImageService(RunWorld world) : IOpenAiService
        {
            public async Task<byte[]> GenerateStoryImageAsync(
                string imagePrompt, StoryImageReference? reference, CancellationToken cancellationToken,
                string? imageSize = null, bool requireReferences = false)
            {
                world.CoverAttempts++;
                await world.StopNowAsync(cancellationToken);
                return [];
            }

            public Task<AdventureContentDto> GenerateAdventureContentAsync(AdventureGenerationInput input, Guid adventureId, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<string> ReviewIllustrationAsync(byte[] imageBytes, string reviewPrompt, IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<string> DescribeCharacterFromPhotoAsync(byte[] imageBytes, string contentType, string promptText, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<string> CompleteTextAsync(string promptText, CancellationToken cancellationToken) => throw new NotSupportedException();
        }

        private sealed class CancellingStoryService(RunWorld world) : IMasterStoryService
        {
            public string ModelName => "test-model";
            public string PromptVersion => "v1";

            public (string System, string User) BuildPrompts(MasterStoryInput input) => ("system", "user");

            public async Task<MasterStoryResult> WriteAsync(
                MasterStoryInput input, CancellationToken cancellationToken)
            {
                if (world.CancelDuringCover)
                {
                    return new MasterStoryResult
                    {
                        Story = Plan(),
                        SystemPrompt = "system",
                        UserPrompt = "user",
                        Model = "test-model",
                        PromptTokens = 1,
                        CompletionTokens = 2,
                    };
                }

                await world.StopNowAsync(cancellationToken);
                throw new UnreachableException();
            }

            public Task<MasterStoryResult> RetryPlanWithCorrectionsAsync(
                MasterStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<MasterStoryResult> WriteCompositePlanAsync(
                CompositeStoryInput input, IReadOnlyList<string> problems, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
        }
    }

    private static MasterStory Plan() => new()
    {
        Concept = new StoryConcept { Title = "ოქროსფერი ფოთოლი", Outline = ["beat"] },
        TitleEn = "The Golden Leaf",
        CharacterLock = "A child with dark hair.",
        WorldLock = "A warm valley.",
        Cover = new IllustrationBrief { Scene = "The child at the valley's edge." },
        Spreads = Enumerable.Range(1, BookFormat.SpreadCount).Select(number => new StorySpread
        {
            Number = number,
            Title = string.Empty,
            Caption = string.Empty,
            Text = $"ქართული ტექსტი {number}",
            TextEn = $"Georgian text {number}",
            Illustration = new IllustrationBrief { Scene = $"Scene {number}" }
        }).ToList()
    };

    private sealed class UnreachableException() : InvalidOperationException("cancellation did not happen");

    // -- fakes -----------------------------------------------------------

    private sealed class FakePacks(AdventurePack seed) : IAdventurePackRepository
    {
        private readonly AdventurePack _pack = seed;

        public Action? OnBeforeWrite { get; set; }

        public AdventurePackStatus StatusOf(Guid id) => _pack.Status;
        public string? ErrorOf(Guid id) => _pack.ErrorMessage;

        public void Force(Guid id, AdventurePackStatus status, string? error)
        {
            _pack.Status = status;
            _pack.ErrorMessage = error;
        }

        private int _reads;

        /// <summary>
        /// Runs on the very first read only, so a test can make the job's opening SELECT be the
        /// thing that is cancelled. Later reads — including the handler's own re-read on a fresh
        /// token — answer normally, which is the behaviour being relied on.
        /// </summary>
        public Func<CancellationToken, Task>? OnFirstRead { get; set; }

        public async Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken)
        {
            if (_reads++ == 0 && OnFirstRead is { } hook)
            {
                await hook(cancellationToken);
            }

            return _pack;
        }

        public Task<bool> UpdateStatusAsync(
            Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl,
            string? errorMessage, CancellationToken cancellationToken)
        {
            _pack.Status = status;
            _pack.ErrorMessage = errorMessage;
            _pack.GenerationHeartbeatUtc = DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task<bool> TryUpdateStatusAsync(
            Guid id, AdventurePackStatus expectedStatus, AdventurePackStatus status,
            string? generatedJson, string? pdfUrl, string? errorMessage,
            CancellationToken cancellationToken)
        {
            OnBeforeWrite?.Invoke();

            if (_pack.Status != expectedStatus)
            {
                return Task.FromResult(false);
            }

            _pack.Status = status;
            _pack.ErrorMessage = errorMessage;
            _pack.GenerationHeartbeatUtc = DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task UpdateProgressAsync(
            Guid id, string? progressMessage, int? progressPercent, CancellationToken cancellationToken)
        {
            _pack.GenerationHeartbeatUtc = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        public Task<bool> TryFailAsync(
            Guid id, AdventurePackStatus expectedStatus, string errorMessage,
            CancellationToken cancellationToken)
        {
            OnBeforeWrite?.Invoke();

            if (_pack.Status != expectedStatus)
            {
                return Task.FromResult(false);
            }

            _pack.Status = AdventurePackStatus.Failed;
            _pack.ErrorMessage = errorMessage;
            _pack.GenerationHeartbeatUtc = DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(DateTime cutoffUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryFailStaleGenerationAsync(Guid id, AdventurePackStatus expectedStatus, DateTime cutoffUtc, string errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
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
        public Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdatePreviewIllustrationAsync(Guid id, PreviewIllustrationStatus status, string? illustrationUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryClaimPreviewIllustrationGenerationAsync(Guid id, int staleAfterMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateGeneratedJsonAsync(Guid id, string generatedJson, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Just enough of a preview run for the fulfilment job to start drawing from it.</summary>
    private sealed class FakeRuns(Guid runId) : IMasterStoryRunRepository
    {
        public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<MasterStoryRun?>(new MasterStoryRun
            {
                Id = runId,
                ChildName = "ნინო",
                Age = 5,
                Gender = "girl",
                Theme = nameof(ThemeType.Dinosaurs),
                StoryJson = JsonSerializer.Serialize(Plan(), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                PhotoBlobUrl = "photo",
                SpreadCount = BookFormat.SpreadCount
            });

        public Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetProgressAsync(Guid id, string status, string? progressMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SavePromptsAsync(Guid id, string model, string promptVersion, string systemPrompt, string userPrompt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveStoryAsync(Guid id, string storyJson, string contentJson, int promptTokens, int completionTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>The preview job's own view of the run table: status, and whether it was failed.</summary>
    private sealed class FakeRunRepository(Guid runId) : IMasterStoryRunRepository
    {
        public string Status { get; private set; } = MasterStoryRunStatus.Pending;
        public string? Error { get; private set; }
        public string? CoverUrl { get; private set; }

        public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<MasterStoryRun?>(new MasterStoryRun
            {
                Id = runId,
                ChildName = "ნინო",
                Age = 5,
                Gender = "girl",
                Theme = nameof(ThemeType.Dinosaurs),
                StoryLanguage = "ka",
                SpreadCount = BookFormat.SpreadCount
            });

        public Task SetProgressAsync(Guid id, string status, string? progressMessage, CancellationToken cancellationToken)
        {
            Status = status;
            return Task.CompletedTask;
        }

        public Task SavePromptsAsync(Guid id, string model, string promptVersion, string systemPrompt, string userPrompt, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken)
        {
            Status = MasterStoryRunStatus.Failed;
            Error = error;
            return Task.CompletedTask;
        }

        public Task SaveStoryAsync(Guid id, string storyJson, string contentJson, int promptTokens, int completionTokens, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken)
        {
            Status = MasterStoryRunStatus.Ready;
            return Task.CompletedTask;
        }

        public Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken)
        {
            CoverUrl = coverImageUrl;
            return Task.CompletedTask;
        }

        public Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeBlobs : IBlobStorageService
    {
        public Task<string> UploadAsync(string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken) =>
            Task.FromResult($"stored/{blobName}");

        public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
            Task.FromResult(new byte[] { 1, 2, 3 });

        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
            Task.FromResult(true);
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

    private sealed class ThrowingComposer : IBekiPdfComposer
    {
        public byte[] Compose(MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads, BekiBookPersonalization? personalization = null) =>
            throw new NotSupportedException();

        public IReadOnlyList<byte[]> RenderPages(MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads, BekiBookPersonalization? personalization = null) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingBookGenerator : IBekiBookGenerator
    {
        public Task<BekiBookResult> GenerateAsync(MasterStoryInput input, byte[] childPhoto, string childPhotoContentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BekiImageResult> DrawCoverAsync(MasterStory plan, byte[] childPhoto, string childPhotoContentType, CancellationToken cancellationToken, CompositeBookContext? composite = null) => throw new NotSupportedException();
        public Task<BekiBookResult> IllustrateAsync(MasterStory plan, byte[] childPhoto, string childPhotoContentType, byte[]? existingCover, Func<BekiImageResult, Task>? onImage, CancellationToken cancellationToken, IReadOnlyDictionary<int, byte[]>? existingSpreads = null, CompositeBookContext? composite = null) => throw new NotSupportedException();
    }

    private sealed class ThrowingImageService : IOpenAiService
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

    private sealed class ThrowingJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => throw new NotSupportedException();
        public bool ChangeState(string jobId, IState state, string? expectedState) => throw new NotSupportedException();
    }
}
