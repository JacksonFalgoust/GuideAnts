using System.Net;
using System.Text;
using System.Text.Json;

namespace Parity.Tests.Support;

public sealed class FakeHttpHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request, body));
        return respond(Requests.Count);
    }

    /// <summary>A /embeddings response for <paramref name="count"/> inputs. Vector i has value (offset + i) in dimension 0.</summary>
    public static HttpResponseMessage Embeddings(int count, int offset = 0, int dimensions = 1536, bool reverse = false)
    {
        var data = Enumerable.Range(0, count).Select(i =>
        {
            var vector = new float[dimensions];
            vector[0] = offset + i;
            return new { @object = "embedding", index = i, embedding = vector };
        });
        if (reverse)
        {
            data = data.Reverse();
        }

        var json = JsonSerializer.Serialize(new { @object = "list", data, model = "text-embedding-3-small" });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    public static HttpResponseMessage Status(HttpStatusCode code, string body = "{\"error\":\"x\"}", TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(code) { Content = new StringContent(body) };
        if (retryAfter is { } wait)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
        }

        return response;
    }
}
