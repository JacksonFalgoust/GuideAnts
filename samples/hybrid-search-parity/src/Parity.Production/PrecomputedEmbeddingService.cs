using GuideAntsApi.BackgroundJobs.Services.Embeddings;

namespace Parity.Production;

/// <summary>
/// Stands in for production's embeddings provider: the saved query vectors in <c>data/</c> are what the embeddings API
/// returned for each query, so every input gets <paramref name="vector"/> and no API is called.
/// </summary>
internal sealed class PrecomputedEmbeddingService(float[] vector) : IEmbeddingService
{
    public Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken = default) =>
        Task.FromResult(texts.Select(_ => vector).ToArray());
}
