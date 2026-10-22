using System.Net;
using System.Text.Json;
using Parity.Core;
using Parity.Core.Embeddings;
using Parity.Tests.Support;

namespace Parity.Tests.Unit;

public class OpenAiEmbeddingClientTests
{
    private readonly List<TimeSpan> _delays = [];

    private OpenAiEmbeddingClient Client(FakeHttpHandler handler) =>
        new(new HttpClient(handler), "sk-test", delay: (wait, _) => { _delays.Add(wait); return Task.CompletedTask; });

    [Fact]
    public async Task SendsModelInputsAndBearerKey()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Embeddings(2));
        await Client(handler).EmbedAsync(["a", "b"], CancellationToken.None);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.openai.com/v1/embeddings", request.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", request.Headers.Authorization!.ToString());
        using var json = JsonDocument.Parse(body);
        Assert.Equal("text-embedding-3-small", json.RootElement.GetProperty("model").GetString());
        Assert.Equal("float", json.RootElement.GetProperty("encoding_format").GetString());
        Assert.Equal(new[] { "a", "b" }, json.RootElement.GetProperty("input").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task BatchesByOneHundredAndKeepsInputOrder()
    {
        var handler = new FakeHttpHandler(call => FakeHttpHandler.Embeddings(call < 3 ? 100 : 50, offset: (call - 1) * 100));
        var vectors = await Client(handler).EmbedAsync(Enumerable.Range(0, 250).Select(i => $"t{i}").ToList(), CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(250, vectors.Length);
        Assert.Equal(Enumerable.Range(0, 250).Select(i => (float)i), vectors.Select(v => v[0]));
    }

    [Fact]
    public async Task SortsResponseVectorsByIndex()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Embeddings(3, reverse: true));
        var vectors = await Client(handler).EmbedAsync(["a", "b", "c"], CancellationToken.None);
        Assert.Equal(new[] { 0f, 1f, 2f }, vectors.Select(v => v[0]));
    }

    [Fact]
    public async Task RetriesRateLimitsHonouringRetryAfter()
    {
        var handler = new FakeHttpHandler(call => call == 1
            ? FakeHttpHandler.Status(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(3))
            : FakeHttpHandler.Embeddings(1));
        await Client(handler).EmbedAsync(["a"], CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(3) }, _delays);
    }

    [Fact]
    public async Task RetriesServerErrorsWithExponentialBackoff()
    {
        var handler = new FakeHttpHandler(call => call <= 2 ? FakeHttpHandler.Status(HttpStatusCode.BadGateway) : FakeHttpHandler.Embeddings(1));
        await Client(handler).EmbedAsync(["a"], CancellationToken.None);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, _delays);
    }

    [Fact]
    public async Task GivesUpAfterSixAttempts()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.TooManyRequests));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).EmbedAsync(["a"], CancellationToken.None));
        Assert.Contains("(429) after 6 attempt(s)", error.Message);
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRetryClientErrorsAndShowsTheBody()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.BadRequest, "{\"error\":\"bad input\"}"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).EmbedAsync(["a"], CancellationToken.None));
        Assert.Contains("bad input", error.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RejectsVectorsThatAreNot1536Dimensions()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Embeddings(1, dimensions: 1024));
        await Assert.ThrowsAsync<DataFileException>(() => Client(handler).EmbedAsync(["a"], CancellationToken.None));
    }

    [Fact]
    public void RequiresAnApiKey()
    {
        var error = Assert.Throws<ParityConfigurationException>(() => new OpenAiEmbeddingClient(new HttpClient(), " "));
        Assert.Contains("OPENAI_API_KEY", error.Message);
        Assert.Contains("OPENROUTER_API_KEY", error.Message);
    }
}
