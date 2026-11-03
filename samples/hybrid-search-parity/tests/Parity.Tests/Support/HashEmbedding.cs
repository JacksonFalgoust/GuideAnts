using System.Security.Cryptography;
using System.Text;
using Parity.Core.Data;
using Parity.Core.Embeddings;

namespace Parity.Tests.Support;

/// <summary>
/// Deterministic fake embeddings. Every component is positive, so every cosine similarity is above
/// zero and production's "distance &lt; 1.0" filter keeps every row.
/// </summary>
public static class HashEmbedding
{
    public static float[] Create(string text)
    {
        var rng = new Random(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0));
        var vector = new float[VectorFile.Dimensions];
        double norm = 0;
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(rng.NextDouble() + 0.01);
            norm += vector[i] * vector[i];
        }

        var scale = (float)(1 / Math.Sqrt(norm));
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] *= scale;
        }

        return vector;
    }
}

public sealed class FakeEmbeddingClient(Action<int>? afterCall = null) : IEmbeddingClient
{
    public int Calls { get; private set; }

    public string Model => "fake-hash";

    public Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
        var vectors = inputs.Select(HashEmbedding.Create).ToArray();
        afterCall?.Invoke(Calls);
        return Task.FromResult(vectors);
    }
}
