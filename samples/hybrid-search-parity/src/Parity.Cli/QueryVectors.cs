using Parity.Core;
using Parity.Core.Data;
using Parity.Core.Embeddings;

namespace Parity.Cli;

public static class QueryVectors
{
    /// <summary>A saved query's vector when the text equals one after trimming; otherwise one live OpenAI call.</summary>
    public static async Task<(float[] Vector, string Source)> ResolveAsync(string text, DataSet data, IEmbeddingClient? embedder, CancellationToken ct)
    {
        if (data.FindQueryByText(text) is { } saved)
        {
            return (saved.Vector, $"saved {saved.Id}");
        }

        if (embedder is null)
        {
            throw new ParityConfigurationException(
                $"\"{text.Trim()}\" isn't a saved query, so it needs a live embedding. Set OPENAI_API_KEY or OPENROUTER_API_KEY, or use a query from inputs/queries.json.");
        }

        var vectors = await embedder.EmbedAsync([text.Trim()], ct);
        return (vectors[0], "live embedding");
    }
}
