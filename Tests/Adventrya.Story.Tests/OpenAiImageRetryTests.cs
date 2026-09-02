using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// How the OpenAI image client waits, and when it stops.
///
/// The edit route retried on a linear backoff and read the response body for the word "429",
/// which meant a provider that said exactly when its window would reopen was ignored, and one
/// that asked for an hour would have been ignored just the same. Now the status decides — 408,
/// 429 and 5xx are the provider asking to be asked again — the <c>Retry-After</c> is obeyed up
/// to the same minute the Gemini client allows, and the sleep runs on the job's own token.
///
/// The rule is a pure function and the sleep is a seam, for the reason the Gemini tests give: a
/// test that proved a sixty-second cap by waiting sixty seconds would be deleted the first time
/// CI was busy.
/// </summary>
public class OpenAiImageRetryTests
{
    // -- the rule ----------------------------------------------------------

    [Fact]
    public void A_server_asking_for_longer_than_a_minute_is_capped_at_a_minute() =>
        Assert.Equal(
            TimeSpan.FromSeconds(60),
            OpenAiService.RetryDelay(TimeSpan.FromMinutes(5), attempt: 1, backoffSeconds: 3));

    [Fact]
    public void A_server_asking_for_less_than_a_minute_is_obeyed_exactly() =>
        Assert.Equal(
            TimeSpan.FromSeconds(7),
            OpenAiService.RetryDelay(TimeSpan.FromSeconds(7), attempt: 2, backoffSeconds: 3));

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 6)]
    [InlineData(3, 9)]
    public void Without_advice_it_backs_off_by_attempt(int attempt, int expectedSeconds) =>
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            OpenAiService.RetryDelay(null, attempt, backoffSeconds: 3));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_nonsense_backoff_setting_still_waits_a_second(int backoffSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(1), OpenAiService.RetryDelay(null, attempt: 1, backoffSeconds));

    [Fact]
    public void Advice_that_has_already_elapsed_falls_back_to_the_backoff() =>
        Assert.Equal(
            TimeSpan.FromSeconds(6),
            OpenAiService.RetryDelay(TimeSpan.Zero, attempt: 2, backoffSeconds: 3));

    [Fact]
    public void The_image_ceiling_is_shorter_than_the_text_ceiling()
    {
        // Nine or more image calls a book, each retried, inside one thirty-minute budget: the
        // per-call ceiling is what decides how long one stuck slot can hold the whole job.
        var options = new OpenAiOptions();

        Assert.Equal(3, options.ImageTimeoutMinutes);
        Assert.True(options.ImageTimeoutMinutes < options.TimeoutMinutes);
    }

    // -- the wire ----------------------------------------------------------

    [Fact]
    public async Task A_429_is_retried_after_the_wait_the_server_asked_for()
    {
        var handler = new ScriptedHandler(
            () => RateLimited(TimeSpan.FromSeconds(7)),
            () => Picture());
        var (service, waits) = Service(handler);

        var bytes = await service.GenerateStoryImageAsync(
            "draw", HeroPhoto(), CancellationToken.None, requireReferences: true);

        Assert.Equal([9, 9, 9, 9], bytes);
        Assert.Equal(2, handler.Calls);
        Assert.Equal([TimeSpan.FromSeconds(7)], waits);
    }

    [Fact]
    public async Task An_absurd_retry_after_is_capped_at_a_minute()
    {
        var handler = new ScriptedHandler(
            () => RateLimited(TimeSpan.FromHours(1)),
            () => Picture());
        var (service, waits) = Service(handler);

        await service.GenerateStoryImageAsync("draw", HeroPhoto(), CancellationToken.None, requireReferences: true);

        Assert.Equal([TimeSpan.FromSeconds(60)], waits);
    }

    [Fact]
    public async Task A_503_is_retried_on_the_backoff_when_the_server_gave_no_advice()
    {
        var handler = new ScriptedHandler(
            () => Status(HttpStatusCode.ServiceUnavailable, "overloaded"),
            () => Status(HttpStatusCode.ServiceUnavailable, "still overloaded"),
            () => Picture());
        var (service, waits) = Service(handler);

        await service.GenerateStoryImageAsync("draw", HeroPhoto(), CancellationToken.None, requireReferences: true);

        Assert.Equal(3, handler.Calls);
        Assert.Equal([TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6)], waits);
    }

    [Fact]
    public async Task A_400_is_our_request_being_wrong_and_is_not_retried()
    {
        var handler = new ScriptedHandler(
            () => Status(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"invalid_image_file\"}}"),
            () => Picture());
        var (service, waits) = Service(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateStoryImageAsync("draw", HeroPhoto(), CancellationToken.None, requireReferences: true));

        Assert.Contains("invalid_image_file", error.Message);
        Assert.Equal(1, handler.Calls);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task After_the_last_attempt_the_provider_s_answer_is_the_error()
    {
        var handler = new ScriptedHandler(
            () => RateLimited(TimeSpan.FromSeconds(1)),
            () => RateLimited(TimeSpan.FromSeconds(1)),
            () => RateLimited(TimeSpan.FromSeconds(1)),
            () => Picture());
        var (service, waits) = Service(handler);

        // ThrowsAny: the transient exception is an InvalidOperationException by inheritance, which
        // is what keeps every caller that catches the base type working.
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.GenerateStoryImageAsync("draw", HeroPhoto(), CancellationToken.None, requireReferences: true));

        // Three attempts, two sleeps, and the picture that would have come on the fourth is never
        // bought: the attempt count is the budget, not a suggestion.
        Assert.Contains("429", error.Message);
        Assert.Equal(3, handler.Calls);
        Assert.Equal(2, waits.Count);
    }

    [Fact]
    public async Task The_wait_ends_the_moment_the_job_is_cancelled()
    {
        // A capped sleep is still a minute of a job that may already be over its budget, so the
        // sleep has to observe the token the job hands down. The token fires as the wait begins —
        // which is what a deadline passing mid-retry looks like from in here — and the client must
        // come straight back out rather than serve the minute first.
        using var cancellation = new CancellationTokenSource();
        var handler = new ScriptedHandler(
            () => RateLimited(TimeSpan.FromHours(1)),
            () => Picture());
        var (service, _) = Service(handler);

        service.Delay = (delay, token) =>
        {
            cancellation.Cancel();
            return Task.Delay(delay, token);
        };

        var started = DateTimeOffset.UtcNow;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GenerateStoryImageAsync("draw", HeroPhoto(), cancellation.Token, requireReferences: true));

        Assert.Equal(1, handler.Calls);
        Assert.True(
            DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5),
            "the cancelled retry wait should return immediately, not serve out its delay");
    }

    // ---- harness ---------------------------------------------------------

    private static (OpenAiService Service, List<TimeSpan> Waits) Service(ScriptedHandler handler)
    {
        var service = new OpenAiService(
            new SingleClientFactory(handler),
            new PassThroughNormalizer(),
            Options.Create(new OpenAiOptions
            {
                ApiKey = "test-key",
                BaseUrl = "https://openai.test/v1/",
                EnableStoryImages = true,
                ImageModel = "gpt-image-2",
                ImageEditModel = "gpt-image-2",
                ImageRetryAttempts = 3,
                ImageRetryBackoffSeconds = 3,
                LogPrompts = false,
            }),
            NullLogger<OpenAiService>.Instance);

        var waits = new List<TimeSpan>();
        service.Delay = (delay, _) =>
        {
            waits.Add(delay);
            return Task.CompletedTask;
        };

        return (service, waits);
    }

    private static StoryImageReference HeroPhoto() => new()
    {
        CastPhotos =
        [
            new CastPhotoReference
            {
                Name = "ნინი",
                Relationship = "hero",
                IsHero = true,
                Bytes = [0x01, 0x02, 0x03, 0x04],
                ContentType = "image/png",
            },
        ],
    };

    private static HttpResponseMessage Picture() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                data = new[] { new { b64_json = Convert.ToBase64String(new byte[] { 9, 9, 9, 9 }) } },
            }),
            Encoding.UTF8,
            "application/json"),
    };

    private static HttpResponseMessage RateLimited(TimeSpan retryAfter)
    {
        var response = Status(HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":\"rate_limit_exceeded\"}}");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
        return response;
    }

    private static HttpResponseMessage Status(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    /// <summary>Answers each call with the next scripted response, and counts.</summary>
    private sealed class ScriptedHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new(responses);

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("The script ran out of responses.");
            }

            return Task.FromResult(_responses.Dequeue()());
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://openai.test/v1/"),
        };
    }

    private sealed class PassThroughNormalizer : IReferenceImageNormalizer
    {
        public NormalizedReferenceImage NormalizeForOpenAi(byte[] bytes, string? hintContentType = null) =>
            new(bytes, hintContentType ?? "image/png", "reference.png");

        public NormalizedReferenceImage NormalizeForStorageWebp(byte[] bytes, string? hintContentType = null) =>
            new(bytes, "image/webp", "illustration.webp");
    }
}
