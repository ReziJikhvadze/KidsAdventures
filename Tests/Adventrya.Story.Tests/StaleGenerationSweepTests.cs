using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// The sweep that closes a case nothing else can.
///
/// Pack a9f342cc-780f-4b59-ba5b-35f964ec869e stalled after one spread of eight and sat in
/// GeneratingStory permanently, paid for. Every guard in the system was inside the process that had
/// stopped existing: the terminal status is written by the job's own catch, the budget stops a job
/// that is still running, Hangfire requeues a worker that died. None of them applies to a row whose
/// job simply is not there any more, and the stalled-order sweep could not help either — orders are
/// marked fulfilled when generation is enqueued.
///
/// The fakes here answer the way the SQL does, on purpose: the list applies the same predicate
/// (working statuses only, heartbeat falling back to CreatedAt) and the write is a real
/// compare-and-set against in-memory state. A fake that simply returned whatever it was handed
/// would make every one of these tests pass without testing anything.
/// </summary>
public class StaleGenerationSweepTests
{
    private static readonly DateTime Now = new(2026, 8, 29, 12, 00, 00, DateTimeKind.Utc);

    /// <summary>The parent every book in these tests belongs to.</summary>
    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public async Task A_pack_that_has_been_silent_past_the_budget_and_its_grace_is_failed()
    {
        // Thirty-minute budget, ten-minute grace: forty-five minutes of silence is past both.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-45)));

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(packId));
        Assert.Contains(GenerationBudget.StalledCode, packs.ErrorOf(packId));

        // The number in the message is the silence, not the budget: it is what an operator needs
        // to tell "just over the line" from "this has been dead since yesterday".
        Assert.Contains("45", packs.ErrorOf(packId));

        // Never a requeue. A book that has already spent forty-five minutes and real money is a
        // decision for a person.
        Assert.Equal(1, packs.Writes);
    }

    /// <summary>
    /// A burial reaches an operator, which it did not.
    ///
    /// The audit's word for this was admin blindness: the sweep is the only writer of a terminal
    /// status that runs outside the job, so the one book nobody was watching was also the one nobody
    /// was told about. It wrote a log line and stopped there. The alarm is the durable half — a row
    /// in a list somebody works through, naming the pack and the silence — and it is a BLOCKER,
    /// because a paid book with no process behind it is not an acceptable-and-recorded state.
    /// </summary>
    [Fact]
    public async Task A_buried_book_raises_an_alarm_naming_the_pack_and_the_silence()
    {
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-45)));

        var alarms = new SweepAlarms();

        await Sweep(packs, alarms: alarms).SweepAsync();

        var raised = Assert.Single(alarms.Raised);
        Assert.Equal(packId, raised.PackId);
        Assert.Equal(Owner, raised.UserId);
        Assert.Equal(GenerationBudget.StalledCode, raised.CheckId);
        Assert.Equal(BekiReleaseSeverity.Blocker, raised.Severity);
        Assert.Contains("45", raised.Detail);

        // Keyed on the burial rather than on the wording, so a book the sweep reaches twice is one
        // incident with two sightings rather than two rows.
        Assert.Equal(BekiAlarmEvidence.ForAttempt("sweep-burial", packId), raised.EvidenceKey);
    }

    /// <summary>A book the sweep leaves alone raises nothing: the alarm follows the verdict.</summary>
    [Fact]
    public async Task A_healthy_book_raises_no_alarm()
    {
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-5)));

        var alarms = new SweepAlarms();

        await Sweep(packs, alarms: alarms).SweepAsync();

        Assert.Empty(alarms.Raised);
    }

    [Fact]
    public async Task A_pack_still_being_drawn_is_left_alone()
    {
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-12)));

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.GeneratingStory, packs.StatusOf(packId));
        Assert.Equal(0, packs.Writes);
    }

    [Fact]
    public async Task A_pack_that_is_slower_than_the_budget_but_inside_the_grace_is_left_alone()
    {
        // Thirty-five minutes: over its own deadline, under the sweep's. The job is entitled to
        // fail itself first, with a reason of its own — the sweep's verdict is the coarse one and
        // only arrives when nothing else did.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-35)));

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.GeneratingStory, packs.StatusOf(packId));
    }

    [Fact]
    public async Task A_pack_with_no_heartbeat_is_judged_by_when_it_was_created()
    {
        /*
          The whole reason the sweep can reach today's stuck book.

          GenerationHeartbeatUtc arrived after the rows that are already stalled, so every one of
          them carries NULL. Reading that as "no news is good news" would leave exactly the books
          this was written for untouchable, forever.
        */
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(new AdventurePack
        {
            Id = packId,
            Status = AdventurePackStatus.GeneratingStory,
            CreatedAt = Now.AddHours(-3),
            GenerationHeartbeatUtc = null
        });

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(packId));
        Assert.Contains("180", packs.ErrorOf(packId));
    }

    [Fact]
    public async Task A_pack_created_long_ago_but_beating_recently_is_left_alone()
    {
        // The fallback is a fallback. A resumed book can be hours old and perfectly alive, and
        // CreatedAt would condemn it.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(new AdventurePack
        {
            Id = packId,
            Status = AdventurePackStatus.GeneratingStory,
            CreatedAt = Now.AddHours(-3),
            GenerationHeartbeatUtc = Now.AddMinutes(-2)
        });

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.GeneratingStory, packs.StatusOf(packId));
    }

    [Theory]
    [InlineData(AdventurePackStatus.Completed)]
    [InlineData(AdventurePackStatus.Failed)]
    [InlineData(AdventurePackStatus.Pending)]
    [InlineData(AdventurePackStatus.StoryReady)]
    public async Task A_pack_that_is_not_mid_generation_is_never_touched(AdventurePackStatus status)
    {
        // Terminal rows are already answered; Pending has no job yet to be stalled; StoryReady is
        // a book waiting on a person rather than on a process. Failing any of them would be the
        // sweep inventing a problem.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, status, heartbeat: Now.AddDays(-1)));

        await Sweep(packs).SweepAsync();

        Assert.Equal(status, packs.StatusOf(packId));
        Assert.Equal(0, packs.Writes);
    }

    [Fact]
    public async Task A_pack_stuck_halfway_through_its_pdf_is_swept_too()
    {
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingPdf,
            heartbeat: Now.AddMinutes(-90)));

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(packId));
    }

    [Fact]
    public async Task A_job_that_answers_between_the_read_and_the_write_keeps_its_own_verdict()
    {
        /*
          The race the compare-and-set exists for.

          The sweep reads a row, decides it is dead, and in the meantime the job it was about to
          bury finishes and writes Completed. Without the compare-and-set the sweep's Failed lands
          on a finished book — a parent with a PDF in storage, told their book could not be made.
        */
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-45)));

        packs.OnBeforeWrite = () => packs.Force(packId, AdventurePackStatus.Completed);

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.Completed, packs.StatusOf(packId));
        Assert.Null(packs.ErrorOf(packId));
    }

    [Fact]
    public async Task A_job_that_delivers_a_spread_between_the_read_and_the_write_is_not_buried()
    {
        /*
          The race a status-only compare-and-set cannot see.

          The sweep reads a batch and then writes each row in turn. In between, a job that was slow
          rather than dead finishes a spread: that write refreshes the heartbeat and leaves the
          status exactly where it was, so a check on status alone still matches and the sweep fails
          a book that had just proved it was alive — and, with the claim guard on the fulfilment
          job, that book can no longer claim its way back out.

          The write therefore re-tests the silence that justified the verdict.
        */
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-45)));

        // Still GeneratingStory — only the heartbeat moved, which is exactly what delivering a
        // spread does.
        packs.OnBeforeWrite = () => packs.Force(
            packId, AdventurePackStatus.GeneratingStory, heartbeat: Now.AddSeconds(-3));

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.GeneratingStory, packs.StatusOf(packId));
        Assert.Null(packs.ErrorOf(packId));
        Assert.Equal(0, packs.Writes);
    }

    [Fact]
    public async Task A_run_that_writes_something_between_the_read_and_the_write_is_not_buried()
    {
        // The same race on the run table, where every write stamps UpdatedAt: saving the prompts or
        // the story moves the row forward without changing its status.
        var runId = Guid.NewGuid();
        var runs = new FakeRunStore((runId, MasterStoryRunStatus.Writing, Now.AddMinutes(-60)));
        runs.OnBeforeWrite = () => runs.Force(
            runId, MasterStoryRunStatus.Writing, updatedAt: Now.AddSeconds(-3));

        await Sweep(new FakePackStore(), runs).SweepAsync();

        Assert.Equal(MasterStoryRunStatus.Writing, runs.StatusOf(runId));
        Assert.Null(runs.ErrorOf(runId));
    }

    [Fact]
    public async Task The_losing_writer_is_the_job_when_the_sweep_gets_there_first()
    {
        /*
          The same race from the other side, and the reason the fulfilment job's terminal write is
          a compare-and-set as well: once the sweep has recorded why a book was abandoned, a job
          that comes back to life must not quietly overwrite that with Completed and leave nothing
          anywhere saying it took three quarters of an hour.
        */
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-45)));

        await Sweep(packs).SweepAsync();

        var revived = await packs.TryUpdateStatusAsync(
            packId,
            AdventurePackStatus.GeneratingStory,
            AdventurePackStatus.Completed,
            "{}", "pdf", null, CancellationToken.None);

        Assert.False(revived);
        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(packId));
        Assert.Contains(GenerationBudget.StalledCode, packs.ErrorOf(packId));
    }

    [Fact]
    public async Task The_cutoff_follows_the_configured_budget()
    {
        // A deployment that raises the budget raises the sweep's patience with it, in one setting.
        // At sixty minutes, fifty of silence is not yet stale.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-50)));

        await Sweep(packs, budgetMinutes: 60).SweepAsync();
        Assert.Equal(AdventurePackStatus.GeneratingStory, packs.StatusOf(packId));

        // Seventy-one is: sixty plus the ten-minute grace.
        packs.Force(packId, AdventurePackStatus.GeneratingStory, heartbeat: Now.AddMinutes(-71));
        await Sweep(packs, budgetMinutes: 60).SweepAsync();
        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(packId));
    }

    [Fact]
    public async Task Every_stale_pack_in_the_batch_is_closed()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var healthy = Guid.NewGuid();

        var packs = new FakePackStore(
            Pack(first, AdventurePackStatus.GeneratingStory, Now.AddMinutes(-45)),
            Pack(second, AdventurePackStatus.GeneratingPdf, Now.AddMinutes(-200)),
            Pack(healthy, AdventurePackStatus.GeneratingStory, Now.AddMinutes(-1)));

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(first));
        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(second));
        Assert.Equal(AdventurePackStatus.GeneratingStory, packs.StatusOf(healthy));
    }

    [Fact]
    public async Task One_pack_that_will_not_write_does_not_strand_the_rest_of_the_batch()
    {
        // The next book in the list belongs to a different parent.
        var broken = Guid.NewGuid();
        var other = Guid.NewGuid();

        var packs = new FakePackStore(
            Pack(broken, AdventurePackStatus.GeneratingStory, Now.AddMinutes(-45)),
            Pack(other, AdventurePackStatus.GeneratingStory, Now.AddMinutes(-45)))
        {
            ThrowFor = broken
        };

        await Sweep(packs).SweepAsync();

        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(other));
    }

    [Fact]
    public async Task A_preview_run_that_stopped_writing_is_failed_by_its_own_timestamp()
    {
        // Runs need no new column: MasterStoryRuns has carried UpdatedAt since it was created and
        // every write in its repository sets it.
        var runId = Guid.NewGuid();
        var runs = new FakeRunStore(
            (runId, MasterStoryRunStatus.Writing, Now.AddMinutes(-60)));

        await Sweep(new FakePackStore(), runs).SweepAsync();

        Assert.Equal(MasterStoryRunStatus.Failed, runs.StatusOf(runId));
        Assert.Contains(GenerationBudget.StalledCode, runs.ErrorOf(runId));
    }

    [Theory]
    [InlineData(MasterStoryRunStatus.Ready)]
    [InlineData(MasterStoryRunStatus.Failed)]
    [InlineData(MasterStoryRunStatus.Pending)]
    public async Task A_run_that_is_not_mid_flight_is_never_touched(string status)
    {
        var runId = Guid.NewGuid();
        var runs = new FakeRunStore((runId, status, Now.AddDays(-2)));

        await Sweep(new FakePackStore(), runs).SweepAsync();

        Assert.Equal(status, runs.StatusOf(runId));
    }

    [Fact]
    public async Task A_run_whose_job_answers_first_keeps_its_own_status()
    {
        var runId = Guid.NewGuid();
        var runs = new FakeRunStore((runId, MasterStoryRunStatus.Writing, Now.AddMinutes(-60)));
        runs.OnBeforeWrite = () => runs.Force(runId, MasterStoryRunStatus.Ready);

        await Sweep(new FakePackStore(), runs).SweepAsync();

        Assert.Equal(MasterStoryRunStatus.Ready, runs.StatusOf(runId));
    }

    [Fact]
    public void The_silence_limit_is_the_budget_plus_a_grace_period()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(40),
            GenerationBudget.SweepSilenceLimit(new BekiOptions { GenerationBudgetMinutes = 30 }));

        // A budget somebody typed wrong falls back to the default rather than sweeping everything
        // in flight on the next pass.
        Assert.Equal(
            TimeSpan.FromMinutes(40),
            GenerationBudget.SweepSilenceLimit(new BekiOptions { GenerationBudgetMinutes = 0 }));
    }

    // -- who gets told ---------------------------------------------------

    [Fact]
    public async Task The_family_whose_book_was_buried_is_told()
    {
        /*
          The line this class's own doc comment used to end on: "the sweep tells nobody".

          It is the only writer of a terminal status that runs outside the job, so a book it fails
          is a book whose owner would otherwise never learn anything — their screen simply stops
          changing. Every other terminal write pages an operator and writes to the parent; this one
          did neither.
        */
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-45)));
        var email = new RecordingEmailService();

        await Sweep(packs, email: email).SweepAsync();

        var sent = Assert.Single(email.Failures);
        Assert.Equal(SingleUserRepository.Address, sent.To);
        Assert.Equal("ბექა და ცისარტყელას ხიდი", sent.BookTitle);

        // Georgian, and not the sweep's own sentence: that one names the code and the forty-five
        // minutes, and it is written for whoever is on duty.
        Assert.False(string.IsNullOrWhiteSpace(sent.ParentMessage));
        Assert.DoesNotContain(GenerationBudget.StalledCode, sent.ParentMessage);
        Assert.DoesNotContain(sent.ParentMessage, char.IsAsciiLetter);
    }

    [Fact]
    public async Task Nobody_is_written_to_about_a_book_that_was_left_alone()
    {
        // The compare-and-set losing means the job is alive and the book is fine. An email here
        // would be an apology for a book that is still being drawn.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-45)));
        packs.OnBeforeWrite = () => packs.Force(packId, AdventurePackStatus.Completed);
        var email = new RecordingEmailService();

        await Sweep(packs, email: email).SweepAsync();

        Assert.Empty(email.Failures);
    }

    [Fact]
    public async Task A_mail_server_that_is_down_costs_neither_the_verdict_nor_the_rest_of_the_batch()
    {
        // The letter is a courtesy that runs after the write. The next pack in the list belongs to
        // a different parent, and neither of them should lose a recorded verdict to SMTP.
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var packs = new FakePackStore(
            Pack(first, AdventurePackStatus.GeneratingStory, Now.AddMinutes(-45)),
            Pack(second, AdventurePackStatus.GeneratingStory, Now.AddMinutes(-50)));

        await Sweep(packs, email: new RecordingEmailService { Throw = true }).SweepAsync();

        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(first));
        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(second));
    }

    [Fact]
    public async Task An_owner_with_no_address_on_file_is_not_written_to()
    {
        // Accounts created by phone code have no email at all. Sending to an empty string is an
        // exception, not a message.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.GeneratingStory,
            heartbeat: Now.AddMinutes(-45)));
        var email = new RecordingEmailService();

        await Sweep(packs, email: email, users: new SingleUserRepository { HasEmail = false }).SweepAsync();

        Assert.Empty(email.Failures);
        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(packId));
    }

    // -- books no job has claimed -------------------------------------------

    /// <summary>
    /// A Beki book resting at StoryReady past the silence limit has no job, and the sweep says
    /// so — without burying it. StoryReady is where a Beki pack waits for the queue, and the
    /// queue is allowed to be slow; what is not allowed is nobody knowing.
    /// </summary>
    [Fact]
    public async Task A_beki_book_no_job_has_claimed_is_reported_but_not_buried()
    {
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.StoryReady,
            heartbeat: Now.AddMinutes(-45), pipeline: GenerationPipelines.Beki));
        var alarms = new SweepAlarms();

        await Sweep(packs, alarms: alarms).SweepAsync();

        // The row is untouched: not Failed, no message, nothing written.
        Assert.Equal(AdventurePackStatus.StoryReady, packs.StatusOf(packId));
        Assert.Null(packs.ErrorOf(packId));
        Assert.Equal(0, packs.Writes);

        // But an operator has been told, as a flag rather than a blocker.
        var alarm = Assert.Single(alarms.Raised);
        Assert.Equal(packId, alarm.PackId);
        Assert.Equal(Owner, alarm.UserId);
        Assert.Equal(StaleGenerationSweepService.UnclaimedCode, alarm.CheckId);
        Assert.Equal(BekiReleaseSeverity.Flag, alarm.Severity);
        Assert.Contains("45", alarm.Detail);
    }

    [Fact]
    public async Task A_legacy_book_resting_at_story_ready_is_a_finished_book_and_raises_nothing()
    {
        // On the legacy pipeline StoryReady is the book the parent already reads. However long
        // it has rested there, it is not waiting for anything.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.StoryReady,
            heartbeat: Now.AddHours(-3), pipeline: GenerationPipelines.Legacy));
        var alarms = new SweepAlarms();

        await Sweep(packs, alarms: alarms).SweepAsync();

        Assert.Empty(alarms.Raised);
        Assert.Equal(0, packs.Writes);
        Assert.Equal(AdventurePackStatus.StoryReady, packs.StatusOf(packId));
    }

    [Theory]
    [InlineData(GenerationPipelines.Beki)]
    [InlineData(GenerationPipelines.Legacy)]
    public async Task A_pending_book_nothing_has_claimed_is_reported_on_either_pipeline(string pipeline)
    {
        // Pending is where fulfilment creates a pack, on both pipelines, and nothing rests there.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.Pending,
            heartbeat: Now.AddMinutes(-45), pipeline: pipeline));
        var alarms = new SweepAlarms();

        await Sweep(packs, alarms: alarms).SweepAsync();

        Assert.Single(alarms.Raised);
        Assert.Equal(AdventurePackStatus.Pending, packs.StatusOf(packId));
        Assert.Equal(0, packs.Writes);
    }

    [Fact]
    public async Task A_book_that_may_only_be_queued_is_not_reported_yet()
    {
        // Five minutes at StoryReady is a queue, not a loss. The silence limit is the budget plus
        // its grace, the same line the burial pass draws.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.StoryReady,
            heartbeat: Now.AddMinutes(-5), pipeline: GenerationPipelines.Beki));
        var alarms = new SweepAlarms();

        await Sweep(packs, alarms: alarms).SweepAsync();

        Assert.Empty(alarms.Raised);
    }

    [Fact]
    public async Task An_unclaimed_book_is_reported_once_not_every_pass()
    {
        // A reviewed alarm that is raised again reopens, and the sweep runs every five minutes.
        // An operator who looked and chose to wait for the queue is not re-paged for the same
        // book on every pass.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.StoryReady,
            heartbeat: Now.AddMinutes(-45), pipeline: GenerationPipelines.Beki));
        var alarms = new SweepAlarms();

        await Sweep(packs, alarms: alarms).SweepAsync();
        await Sweep(packs, alarms: alarms).SweepAsync();
        await Sweep(packs, alarms: alarms).SweepAsync();

        Assert.Single(alarms.Raised);
    }

    [Fact]
    public async Task Nobody_is_written_to_about_a_book_that_is_only_unclaimed()
    {
        // The letter is for a book that was lost. This one may still be drawn, and telling a
        // family it failed when it is merely behind other books would be its own kind of untrue.
        var packId = Guid.NewGuid();
        var packs = new FakePackStore(Pack(packId, AdventurePackStatus.StoryReady,
            heartbeat: Now.AddMinutes(-45), pipeline: GenerationPipelines.Beki));
        var email = new RecordingEmailService();

        await Sweep(packs, email: email, alarms: new SweepAlarms()).SweepAsync();

        Assert.Empty(email.Failures);
    }

    [Fact]
    public async Task An_unclaimed_report_never_touches_the_burial_pass()
    {
        // Both kinds in one batch: the silent working book is buried, the unclaimed one is only
        // reported, and neither pass changes what the other did.
        var buried = Guid.NewGuid();
        var unclaimed = Guid.NewGuid();
        var packs = new FakePackStore(
            Pack(buried, AdventurePackStatus.GeneratingStory, heartbeat: Now.AddMinutes(-45)),
            Pack(unclaimed, AdventurePackStatus.StoryReady, heartbeat: Now.AddMinutes(-45),
                pipeline: GenerationPipelines.Beki));
        var alarms = new SweepAlarms();

        await Sweep(packs, alarms: alarms).SweepAsync();

        Assert.Equal(AdventurePackStatus.Failed, packs.StatusOf(buried));
        Assert.Equal(AdventurePackStatus.StoryReady, packs.StatusOf(unclaimed));
        Assert.Equal(1, packs.Writes);
        Assert.Equal(2, alarms.Raised.Count);
        Assert.Contains(alarms.Raised, alarm => alarm.PackId == buried && alarm.CheckId == GenerationBudget.StalledCode);
        Assert.Contains(alarms.Raised, alarm => alarm.PackId == unclaimed && alarm.CheckId == StaleGenerationSweepService.UnclaimedCode);
    }

    // -- harness ---------------------------------------------------------

    private static StaleGenerationSweepService Sweep(
        FakePackStore packs,
        FakeRunStore? runs = null,
        int budgetMinutes = 30,
        RecordingEmailService? email = null,
        SingleUserRepository? users = null,
        SweepAlarms? alarms = null) =>
        new(packs,
            runs ?? new FakeRunStore(),
            email ?? new RecordingEmailService(),
            users ?? new SingleUserRepository(),
            Options.Create(new BekiOptions { GenerationBudgetMinutes = budgetMinutes }),
            NullLogger<StaleGenerationSweepService>.Instance,
            new FixedTimeProvider(Now),
            alarms);

    /// <summary>Every alarm one sweep pass raised, in order.</summary>
    private sealed class SweepAlarms : IBekiAlarmService
    {
        public List<BekiAlarmRaise> Raised { get; } = [];

        public Task RaiseAsync(BekiAlarmRaise raise, CancellationToken ct)
        {
            Raised.Add(raise);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>([]);

        public Task<IReadOnlyList<BekiAlarm>> ListRecentAsync(int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>([]);

        /// <summary>What was raised for this pack, as stored alarms — so "once" can be tested.</summary>
        public Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>(Raised
                .Where(raise => raise.PackId == packId)
                .Select(raise => new BekiAlarm(
                    Guid.NewGuid(), raise.PackId, raise.OrderId, raise.UserId, raise.CheckId,
                    raise.Severity, raise.Detail, raise.EvidenceBlob,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null))
                .ToList());

        public Task<BekiAlarm?> GetAsync(Guid alarmId, CancellationToken ct) =>
            Task.FromResult<BekiAlarm?>(null);

        public Task<bool> ReviewAsync(
            Guid alarmId, string reviewedBy, string resolution, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<int> CountOpenAsync(CancellationToken ct) => Task.FromResult(Raised.Count);
    }

    private static AdventurePack Pack(
        Guid id,
        AdventurePackStatus status,
        DateTime heartbeat,
        string pipeline = GenerationPipelines.Legacy) => new()
    {
        Id = id,
        UserId = Owner,
        Title = "ბექა და ცისარტყელას ხიდი",
        Status = status,
        GenerationPipeline = pipeline,
        CreatedAt = heartbeat.AddMinutes(-5),
        GenerationHeartbeatUtc = heartbeat
    };

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    /// <summary>
    /// An in-memory AdventurePacks table that answers the two sweep calls exactly as the SQL does:
    /// the same status predicate, the same NULL-heartbeat fallback, and a real compare-and-set.
    /// Everything else on the interface throws, because the sweep must not be reaching for it.
    /// </summary>
    private sealed class FakePackStore(params AdventurePack[] seed) : IAdventurePackRepository
    {
        private readonly Dictionary<Guid, AdventurePack> _packs = seed.ToDictionary(pack => pack.Id);

        public int Writes { get; private set; }
        public Action? OnBeforeWrite { get; set; }
        public Guid? ThrowFor { get; set; }

        public AdventurePackStatus StatusOf(Guid id) => _packs[id].Status;
        public string? ErrorOf(Guid id) => _packs[id].ErrorMessage;

        public void Force(Guid id, AdventurePackStatus status, DateTime? heartbeat = null)
        {
            _packs[id].Status = status;
            if (heartbeat is { } stamp)
            {
                _packs[id].GenerationHeartbeatUtc = stamp;
            }
        }

        public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(
            DateTime cutoffUtc, int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<StaleGenerationPack> stale = _packs.Values
                .Where(pack => pack.Status is AdventurePackStatus.GeneratingStory
                                          or AdventurePackStatus.GeneratingPdf)
                .Where(pack => (pack.GenerationHeartbeatUtc ?? pack.CreatedAt) < cutoffUtc)
                .OrderBy(pack => pack.GenerationHeartbeatUtc ?? pack.CreatedAt)
                .Take(limit)
                .Select(pack => new StaleGenerationPack(
                    pack.Id, pack.Status, pack.CreatedAt, pack.GenerationHeartbeatUtc))
                .ToList();

            return Task.FromResult(stale);
        }

        /// <summary>
        /// The unclaimed listing, with the SQL's predicate: Pending on either pipeline, StoryReady
        /// only on the Beki one, and the same NULL-heartbeat fallback.
        /// </summary>
        public Task<IReadOnlyList<StaleGenerationPack>> ListUnclaimedGenerationAsync(
            DateTime cutoffUtc, int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<StaleGenerationPack> unclaimed = _packs.Values
                .Where(pack => pack.Status == AdventurePackStatus.Pending
                               || (pack.Status == AdventurePackStatus.StoryReady && pack.IsBekiPipeline))
                .Where(pack => (pack.GenerationHeartbeatUtc ?? pack.CreatedAt) < cutoffUtc)
                .OrderBy(pack => pack.GenerationHeartbeatUtc ?? pack.CreatedAt)
                .Take(limit)
                .Select(pack => new StaleGenerationPack(
                    pack.Id, pack.Status, pack.CreatedAt, pack.GenerationHeartbeatUtc))
                .ToList();

            return Task.FromResult(unclaimed);
        }

        /// <summary>
        /// Both halves of the real predicate: the status the sweep saw, and the silence it judged.
        /// A fake that checked only the status would let the interleaved-heartbeat test pass while
        /// the SQL still had the hole.
        /// </summary>
        public Task<bool> TryFailStaleGenerationAsync(
            Guid id, AdventurePackStatus expectedStatus, DateTime cutoffUtc, string errorMessage,
            CancellationToken cancellationToken)
        {
            if (ThrowFor == id)
            {
                throw new InvalidOperationException("the database said no");
            }

            OnBeforeWrite?.Invoke();

            var pack = _packs[id];
            if (pack.Status != expectedStatus
                || (pack.GenerationHeartbeatUtc ?? pack.CreatedAt) >= cutoffUtc)
            {
                return Task.FromResult(false);
            }

            pack.Status = AdventurePackStatus.Failed;
            pack.ErrorMessage = errorMessage;
            Writes++;
            return Task.FromResult(true);
        }

        public Task<bool> TryFailAsync(
            Guid id, AdventurePackStatus expectedStatus, string errorMessage,
            CancellationToken cancellationToken)
        {
            var pack = _packs[id];
            if (pack.Status != expectedStatus)
            {
                return Task.FromResult(false);
            }

            pack.Status = AdventurePackStatus.Failed;
            pack.ErrorMessage = errorMessage;
            return Task.FromResult(true);
        }

        public Task<bool> TryUpdateStatusAsync(
            Guid id, AdventurePackStatus expectedStatus, AdventurePackStatus status,
            string? generatedJson, string? pdfUrl, string? errorMessage,
            CancellationToken cancellationToken)
        {
            var pack = _packs[id];
            if (pack.Status != expectedStatus)
            {
                return Task.FromResult(false);
            }

            pack.Status = status;
            pack.ErrorMessage = errorMessage;
            return Task.FromResult(true);
        }

        /// <summary>Read once per buried pack, to find out whose book it was.</summary>
        public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_packs.TryGetValue(id, out var pack) ? pack : null);

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
        public Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
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

    /// <summary>The same, for the two questions the sweep asks of MasterStoryRuns.</summary>
    private sealed class FakeRunStore : IMasterStoryRunSweepStore
    {
        private readonly Dictionary<Guid, (string Status, DateTime UpdatedAt, string? Error)> _runs;

        public FakeRunStore(params (Guid Id, string Status, DateTime UpdatedAt)[] seed) =>
            _runs = seed.ToDictionary(run => run.Id, run => (run.Status, run.UpdatedAt, (string?)null));

        public Action? OnBeforeWrite { get; set; }

        public string StatusOf(Guid id) => _runs[id].Status;
        public string? ErrorOf(Guid id) => _runs[id].Error;

        public void Force(Guid id, string status, DateTime? updatedAt = null) =>
            _runs[id] = (status, updatedAt ?? _runs[id].UpdatedAt, _runs[id].Error);

        public Task<IReadOnlyList<StaleMasterStoryRun>> ListStaleAsync(
            DateTime cutoffUtc, int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<StaleMasterStoryRun> stale = _runs
                .Where(entry => entry.Value.Status is MasterStoryRunStatus.Writing
                                                   or MasterStoryRunStatus.Illustrating)
                .Where(entry => entry.Value.UpdatedAt < cutoffUtc)
                .OrderBy(entry => entry.Value.UpdatedAt)
                .Take(limit)
                .Select(entry => new StaleMasterStoryRun(entry.Key, entry.Value.Status, entry.Value.UpdatedAt))
                .ToList();

            return Task.FromResult(stale);
        }

        public Task<bool> TryFailStaleAsync(
            Guid id, string expectedStatus, DateTime cutoffUtc, string errorMessage,
            CancellationToken cancellationToken)
        {
            OnBeforeWrite?.Invoke();

            if (_runs[id].Status != expectedStatus || _runs[id].UpdatedAt >= cutoffUtc)
            {
                return Task.FromResult(false);
            }

            _runs[id] = (MasterStoryRunStatus.Failed, _runs[id].UpdatedAt, errorMessage);
            return Task.FromResult(true);
        }
    }
}
