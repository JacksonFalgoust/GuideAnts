using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Parity.Core.Data;

namespace Parity.Core.Embeddings;

public interface IEmbeddingClient
{
    string Model { get; }

    Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct);
}

/// <summary>
/// POST {endpoint}/embeddings, the same REST call production's OpenAiEmbeddingService makes, with
/// batching and retry on 429 and 5xx responses.
/// </summary>
public sealed class OpenAiEmbeddingClient : IEmbeddingClient
{
    public const int BatchSize = 100;
    public const int MaxAttempts = 6;

    private static readonly JsonSerializerOptions ResponseOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public OpenAiEmbeddingClient(
        HttpClient http,
        string? apiKey,
        string endpointBase = "https://api.openai.com/v1",
        string model = "text-embedding-3-small",
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ParityConfigurationException(
                "An OpenAI API key is required. Set OPENAI_API_KEY, or run `dotnet user-secrets set OpenAI:ApiKey <key>` in src/Parity.Cli.");
        }

        _http = http;
        _apiKey = apiKey;
        _endpoint = new Uri(endpointBase.TrimEnd('/') + "/embeddings");
        _delay = delay ?? Task.Delay;
        Model = model;
    }

    public string Model { get; }

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        var results = new float[inputs.Count][];
        for (var start = 0; start < inputs.Count; start += BatchSize)
        {
            var batch = inputs.Skip(start).Take(BatchSize).ToArray();
            var vectors = await EmbedBatchAsync(batch, ct);
            Array.Copy(vectors, 0, results, start, vectors.Length);
        }

        return results;
    }

    private async Task<float[][]> EmbedBatchAsync(string[] batch, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(new { model = Model, input = batch, encoding_format = "float" }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                return Parse(body, batch.Length);
            }

            var status = (int)response.StatusCode;
            var retryable = status == 429 || status >= 500;
            if (!retryable || attempt >= MaxAttempts)
            {
                throw new InvalidOperationException(
                    $"OpenAI embeddings request failed ({status}) after {attempt} attempt(s): {(body.Length > 500 ? body[..500] : body)}");
            }

            var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
            await _delay(wait, ct);
        }
    }

    private static float[][] Parse(string body, int expected)
    {
        var parsed = JsonSerializer.Deserialize<EmbeddingResponse>(body, ResponseOptions)
            ?? throw new InvalidOperationException("OpenAI embeddings response was empty.");
        if (parsed.Data is null || parsed.Data.Count != expected)
        {
            throw new InvalidOperationException($"OpenAI returned {parsed.Data?.Count ?? 0} vectors for {expected} inputs.");
        }

        var ordered = parsed.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            VectorFile.Validate(ordered[i], i);
        }

        return ordered;
    }

    private sealed record EmbeddingResponse(List<EmbeddingData>? Data);

    private sealed record EmbeddingData(int Index, float[] Embedding);
}
