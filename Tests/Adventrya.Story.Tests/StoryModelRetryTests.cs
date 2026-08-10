using System.Net;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// The story call had no retry at all. A single 520 from the provider's edge threw away a run
/// that takes minutes and costs real money, and the parent watching it was told the story could
/// not be written. That is not hypothetical — it is what a production run failed with.
/// </summary>
public class StoryModelRetryTests
{
    private sealed record Answer(string Value);

    /// <summary>
    /// Replays a queued sequence of responses and counts the attempts. Each entry is a factory,
    /// not an instance: the client disposes every response, so a repeated one must be built fresh.
    /// </summary>
    private sealed class QueuedHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _index;
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(responses[Math.Min(_index++, responses.Length - 1)]());
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        // The client is disposed per attempt, so the handler must outlive it.
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static Func<HttpResponseMessage> Fail(HttpStatusCode status) =>
        () => new HttpResponseMessage(status)
        {
            Content = new StringContent("upstream said no", Encoding.UTF8, "text/plain")
        };

    private static Func<HttpResponseMessage> Ok(string value) =>
        () =>
        {
            var payload = JsonSerializer.Serialize(new
            {
                output_text = JsonSerializer.Serialize(new { value }),
                usage = new { input_tokens = 11, output_tokens = 22 }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        };

    private static (StoryModelClient Client, QueuedHandler Handler) Build(params Func<HttpResponseMessage>[] responses)
    {
        var handler = new QueuedHandler(responses);
        var options = Options.Create(new OpenAiOptions
        {
            ApiKey = "test",
            BaseUrl = "https://example.invalid/v1/",
            StoryRetryAttempts = 3,
            StoryRetryBackoffSeconds = 0,
        });
        return (
            new StoryModelClient(new SingleClientFactory(handler), options, NullLogger<StoryModelClient>.Instance),
            handler);
    }

    private static Task<ModelResult<Answer>> Complete(StoryModelClient client) =>
        client.CompleteAsync<Answer>(
            "test-model",
            "system",
            "user",
            "answer",
            JsonDocument.Parse("{\"type\":\"object\"}").RootElement,
            CancellationToken.None);

    [Fact]
    public async Task A_520_from_the_edge_is_retried_and_the_book_survives()
    {
        var (client, handler) = Build(Fail((HttpStatusCode)520), Ok("written"));

        var result = await Complete(client);

        Assert.Equal("written", result.Value.Value);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task A_rate_limit_is_retried()
    {
        var (client, handler) = Build(Fail(HttpStatusCode.TooManyRequests), Ok("written"));

        var result = await Complete(client);

        Assert.Equal("written", result.Value.Value);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task A_bad_request_is_not_retried()
    {
        // Our request is wrong and will be exactly as wrong the second time. Retrying it would
        // only spend the parent's wait three times over.
        var (client, handler) = Build(Fail(HttpStatusCode.BadRequest));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Complete(client));

        Assert.Contains("400", ex.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Attempts_are_bounded_and_the_original_message_survives()
    {
        var (client, handler) = Build(Fail((HttpStatusCode)520));

        // ThrowsAny, not Throws: the transient type derives from InvalidOperationException so
        // that exhausting the attempts leaves callers exactly as they were before retries.
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Complete(client));

        Assert.Contains("520", ex.Message);
        Assert.Equal(3, handler.Calls);
    }
}
