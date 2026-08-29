using AdventurePacks.Api.Configuration.Options;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// The wall clock a generation job is given, and the one question that has to be answered when it
/// is cancelled: was that us, or was that the host?
///
/// Both long jobs — the fulfilment job that draws a purchased book and the preview job that writes
/// one — used to run under <c>CancellationToken.None</c> and catch every exception into a terminal
/// Failed. That is wrong in both directions at once. Nothing stopped a job that had hung inside a
/// twelve-minute call, and a job stopped because the host was shutting down was recorded as a book
/// that could not be made — when it was in fact a book Hangfire was about to hand to another
/// worker, with a resume path waiting for it.
///
/// So the two causes are separated, and the separation is a fact about which token fired:
///
/// <list type="bullet">
/// <item><description>the deadline fired and the host's token did not — the book ran out of time,
/// which is terminal, because retrying a job that has already spent half an hour is spending
/// another half hour to reach the same place;</description></item>
/// <item><description>the host's token fired — a deploy, a restart, a scale-in. Nothing is wrong
/// with the book. The exception is rethrown so Hangfire requeues the job, and the resume machinery
/// picks it up from the spreads that are already stored.</description></item>
/// </list>
///
/// The deadline is a <see cref="TimeProvider"/> timer rather than a bare <c>CancelAfter</c> so
/// that both outcomes can be tested without a test that waits half an hour — or, worse, a test
/// that waits a real minute and is quietly deleted the first time CI is slow.
/// </summary>
public static class GenerationBudget
{
    /// <summary>
    /// What a book gets when the setting is missing or nonsensical. A zero or negative value in
    /// configuration would otherwise mean "cancel immediately", which turns one bad App Service
    /// setting into every book failing the moment it starts.
    /// </summary>
    public static readonly TimeSpan Default = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long the sweep waits past the budget before it will call a quiet row dead.
    ///
    /// The grace exists because the two clocks are not the same clock: the job's deadline starts
    /// when the job starts, and the sweep only sees the last time a row was written. A job that is
    /// about to fail itself at minute thirty should be allowed to, and to say why — the sweep's
    /// verdict is deliberately the coarser one, and arrives only when nothing else did.
    /// </summary>
    public static readonly TimeSpan SweepGrace = TimeSpan.FromMinutes(10);

    public static TimeSpan For(BekiOptions options) => For(options.GenerationBudgetMinutes);

    public static TimeSpan For(int configuredMinutes) =>
        configuredMinutes <= 0 ? Default : TimeSpan.FromMinutes(configuredMinutes);

    /// <summary>How long a row may be silent before the sweep treats it as abandoned.</summary>
    public static TimeSpan SweepSilenceLimit(BekiOptions options) => For(options) + SweepGrace;

    /// <summary>
    /// True when this cancellation is the budget's doing and the host is not also shutting down.
    ///
    /// The order of the two clauses is the rule, not an optimisation: during a shutdown both
    /// tokens can end up cancelled — the deadline's own registration fires as the linked source is
    /// disposed, or the timer simply lands in the same millisecond — and treating that as a budget
    /// failure would mark a perfectly resumable book as dead every time the site is deployed. When
    /// the host is stopping, the host is the reason, whatever else is also true.
    /// </summary>
    public static bool Expired(CancellationToken deadline, CancellationToken host) =>
        deadline.IsCancellationRequested && !host.IsCancellationRequested;

    /// <summary>
    /// The code an operator matches on, and the sentence stored on the row.
    ///
    /// A word in front, in the shape the composite pipeline's own failures use, because the stored
    /// message is the only thing anyone reading the database will see: an unadorned
    /// "The operation was canceled." is what a cancelled job leaves behind, and it says neither
    /// what was cancelled nor by whom.
    /// </summary>
    public const string ExceededCode = "GENERATION_BUDGET_EXCEEDED";

    /// <summary>The sweep's code, kept beside the job's so the two read as one vocabulary.</summary>
    public const string StalledCode = "GENERATION_STALLED";

    public static string ExceededReason(TimeSpan budget, string stage) =>
        $"{ExceededCode}: the book did not finish within {budget.TotalMinutes:0} minutes "
        + $"(stopped while {stage}).";

    public static string StalledReason(TimeSpan silence) =>
        $"{StalledCode}: nothing has been written to this book for {silence.TotalMinutes:0} "
        + "minutes, so the job that was making it is gone. No retry was started.";

    /// <summary>
    /// The deadline, and the token the work should actually run under.
    ///
    /// Two sources rather than one: the caller has to be able to ask afterwards which of them
    /// fired, and a single linked source that cancels itself cannot answer that question.
    /// </summary>
    public static GenerationDeadline Start(
        CancellationToken hostToken,
        TimeSpan budget,
        TimeProvider timeProvider) => new(hostToken, budget, timeProvider);
}

/// <summary>
/// A running budget: a deadline that cancels on its own, linked to the host's token so that either
/// can stop the work. Dispose stops the timer.
/// </summary>
public sealed class GenerationDeadline : IDisposable
{
    private readonly CancellationTokenSource _deadline;
    private readonly CancellationTokenSource _linked;
    private readonly CancellationToken _hostToken;

    // Both tokens are held rather than read back off their sources, so that asking why the work
    // stopped is safe after the sources have been disposed — which is precisely when a finally or
    // a late log line would ask.
    private readonly CancellationToken _deadlineToken;

    internal GenerationDeadline(CancellationToken hostToken, TimeSpan budget, TimeProvider timeProvider)
    {
        _hostToken = hostToken;
        _deadline = new CancellationTokenSource(budget, timeProvider);
        _deadlineToken = _deadline.Token;
        _linked = CancellationTokenSource.CreateLinkedTokenSource(hostToken, _deadlineToken);
        Budget = budget;
    }

    public TimeSpan Budget { get; }

    /// <summary>The token every awaited call inside the job should be handed.</summary>
    public CancellationToken Token => _linked.Token;

    /// <summary>
    /// True when the work stopped because it ran out of time rather than because the host is going
    /// away. See <see cref="GenerationBudget.Expired"/> for why the host wins a tie.
    /// </summary>
    public bool Expired => GenerationBudget.Expired(_deadlineToken, _hostToken);

    /// <summary>Why the job stopped, in one word, for the log line.</summary>
    public string Cause => Expired ? "budget" : "host";

    public void Dispose()
    {
        _linked.Dispose();
        _deadline.Dispose();
    }
}
