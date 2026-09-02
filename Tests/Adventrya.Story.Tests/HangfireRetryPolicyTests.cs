using AdventurePacks.Api.Extensions;
using Hangfire;
using Hangfire.States;
using Hangfire.Storage;

namespace Adventrya.Story.Tests;

/// <summary>
/// What Hangfire does with a job that threw.
///
/// Its default is ten retries. Every long job here already writes its own terminal verdict and
/// swallows the exception, so a throw that reaches Hangfire is the host going away — which one
/// requeue answers — or a job that lost the race for its own lock, which no number of retries
/// answers: another worker holds the lock because it is doing the work, and each retry only held
/// a worker for the full lock wait before failing the same way.
/// </summary>
public class HangfireRetryPolicyTests
{
    [Fact]
    public void A_job_that_lost_its_lock_is_deleted_rather_than_retried()
    {
        var candidate = new FailedState(new DistributedLockTimeoutException("beki-pack:7fc8faf4"));

        var replacement = LockTimeoutIsNotRetriedFilter.Replacement(candidate);

        var deleted = Assert.IsType<DeletedState>(replacement);
        Assert.Contains("lock", deleted.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beki-pack:7fc8faf4", deleted.Reason);
    }

    [Fact]
    public void Any_other_failure_is_left_for_the_retry_filter()
    {
        Assert.Null(LockTimeoutIsNotRetriedFilter.Replacement(
            new FailedState(new InvalidOperationException("the model refused"))));
        Assert.Null(LockTimeoutIsNotRetriedFilter.Replacement(new EnqueuedState()));
        Assert.Null(LockTimeoutIsNotRetriedFilter.Replacement(new SucceededState(null, 0, 0)));
    }

    [Fact]
    public void The_global_policy_is_two_attempts_and_exactly_one_retry_filter()
    {
        // Twice, because the API's container is built more than once in a process that runs the
        // suite, and two retry filters electing states for the same failure is the fault the
        // replacement exists to avoid.
        ServiceCollectionExtensions.ConfigureHangfireRetryPolicy();
        ServiceCollectionExtensions.ConfigureHangfireRetryPolicy();

        var retry = Assert.Single(GlobalJobFilters.Filters, f => f.Instance is AutomaticRetryAttribute);
        Assert.Equal(ServiceCollectionExtensions.HangfireRetryAttempts, ((AutomaticRetryAttribute)retry.Instance).Attempts);
        Assert.Equal(2, ((AutomaticRetryAttribute)retry.Instance).Attempts);

        var lockFilter = Assert.Single(GlobalJobFilters.Filters, f => f.Instance is LockTimeoutIsNotRetriedFilter);

        // Elected first: it has to have replaced the candidate state before the retry filter
        // decides whether to reschedule it.
        Assert.True(lockFilter.Order < retry.Order, "the lock filter must run before the retry filter");
    }
}
