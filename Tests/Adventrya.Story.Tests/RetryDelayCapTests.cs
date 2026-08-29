using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// How long the Gemini client is willing to wait when a provider asks it to wait.
///
/// <c>Retry-After</c> was obeyed without limit. That is the polite thing to do to a server and the
/// wrong thing to do to a parent: three attempts, each sleeping whatever the header said, inside a
/// job nothing was watching, is a large part of how a paid book ended up stalled rather than failed
/// — and the sleep was happening under a job that had no deadline to interrupt it.
///
/// The rule is now one line and, deliberately, a pure function: a test that proved a sixty-second
/// cap by waiting sixty seconds would be deleted the first time CI was busy.
/// </summary>
public class RetryDelayCapTests
{
    [Fact]
    public void A_server_asking_for_longer_than_a_minute_is_capped_at_a_minute()
    {
        var delay = GeminiInteractionsClient.RetryDelay(TimeSpan.FromMinutes(5), attempt: 1, backoffSeconds: 5);

        Assert.Equal(TimeSpan.FromSeconds(60), delay);
    }

    [Fact]
    public void A_server_asking_for_less_than_a_minute_is_obeyed_exactly()
    {
        // The advice is capped, not ignored. A provider that knows its window reopens in seven
        // seconds is worth listening to — the cap is only there to stop an unbounded one.
        var delay = GeminiInteractionsClient.RetryDelay(TimeSpan.FromSeconds(7), attempt: 3, backoffSeconds: 5);

        Assert.Equal(TimeSpan.FromSeconds(7), delay);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 15)]
    public void Without_advice_it_backs_off_by_attempt(int attempt, int expectedSeconds)
    {
        var delay = GeminiInteractionsClient.RetryDelay(null, attempt, backoffSeconds: 5);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Fact]
    public void The_cap_applies_to_the_configured_backoff_too()
    {
        // A backoff big enough to walk past the cap on its own is the same problem as an
        // unbounded Retry-After, and gets the same answer.
        var delay = GeminiInteractionsClient.RetryDelay(null, attempt: 3, backoffSeconds: 45);

        Assert.Equal(TimeSpan.FromSeconds(60), delay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_nonsense_backoff_setting_still_waits_a_second(int backoffSeconds)
    {
        // Zero would be a busy loop against a provider that has just asked for a pause, and a
        // negative one is a value somebody typed into a settings box.
        var delay = GeminiInteractionsClient.RetryDelay(null, attempt: 1, backoffSeconds);

        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    [Fact]
    public void Advice_that_has_already_elapsed_falls_back_to_the_backoff()
    {
        // A Retry-After date in the past reaches here as a non-positive span. Sleeping for it
        // would mean not sleeping at all, and hammering a rate limiter is what the header was
        // asking us not to do.
        var delay = GeminiInteractionsClient.RetryDelay(TimeSpan.Zero, attempt: 2, backoffSeconds: 5);

        Assert.Equal(TimeSpan.FromSeconds(10), delay);
    }

    /// <summary>
    /// The other half of the fix, and the half a pure function cannot show: the wait is
    /// interruptible.
    ///
    /// A capped sleep is still a minute of a job that may already be over its budget, so the sleep
    /// has to observe the token the job hands down. The handler cancels while answering the first
    /// attempt — which is what a deadline passing mid-call looks like from in here — and the client
    /// must come straight back out rather than serving the sixty seconds first.
    /// </summary>
    [Fact]
    public async Task The_wait_ends_the_moment_the_job_is_cancelled()
    {
        using var cancellation = new CancellationTokenSource();

        var handler = new RateLimitedHandler(
            retryAfter: TimeSpan.FromHours(1),
            onRequest: cancellation.Cancel);

        var client = new GeminiInteractionsClient(
            new SingleClientFactory(handler),
            Options.Create(new GeminiOptions { ApiKey = "test-key", RetryAttempts = 3 }),
            NullLogger<GeminiInteractionsClient>.Instance);

        var started = DateTimeOffset.UtcNow;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.CompleteTextAsync("model", [GeminiInputItem.Text("hi")], null, cancellation.Token));

        // One attempt, then out. Without the token the client would have slept a full minute
        // before its second.
        Assert.Equal(1, handler.Calls);
        Assert.True(
            DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5),
            "the cancelled retry wait should return immediately, not serve out its delay");
    }

    /// <summary>Answers every call with a 429 that asks for a very long wait.</summary>
    private sealed class RateLimitedHandler(TimeSpan retryAfter, Action onRequest) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            onRequest();

            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("slow down", Encoding.UTF8, "text/plain")
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
            return Task.FromResult(response);
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        // The client is disposed per attempt, so the handler has to outlive it.
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
