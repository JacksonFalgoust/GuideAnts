using Parity.Core;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Embeddings;
using Parity.Core.Production;

namespace Parity.Cli.Commands;

public sealed class GenerateDataCommand(TextWriter output, string sampleRoot, Func<string, IDocsSource> docsSourceFactory, IEmbeddingClient? embedder)
{
    public async Task<int> RunAsync(CliArgs args, CancellationToken ct)
    {
        if (embedder is null)
        {
            throw new ParityConfigurationException(
                "generate-data needs an OpenAI API key. Set OPENAI_API_KEY, or run `dotnet user-secrets set OpenAI:ApiKey <key>` in src/Parity.Cli.");
        }

        var docs = docsSourceFactory(args.Get("--corpus-commit") ?? ProductionSources.Commit);
        var edgeCases = DataStore.ReadEdgeCases(sampleRoot);
        var queries = DataStore.ReadQueries(sampleRoot);
        var files = docs.ReadDocs();
        var drafts = CorpusBuilder.Build(files, edgeCases);
        output.WriteLine($"Corpus {docs.ResolvedCommit[..8]}: {files.Count} files, {drafts.Count} chunks ({edgeCases.Count} edge cases), {queries.Count} queries.");

        var chunkVectors = await embedder.EmbedAsync(drafts.Select(d => d.Content).ToList(), ct);
        output.WriteLine($"Embedded {chunkVectors.Length} chunks with {embedder.Model}.");
        var queryVectors = await embedder.EmbedAsync(queries.Select(q => q.Text).ToList(), ct);

        var data = new GeneratedData(
            docs.ResolvedCommit,
            DateTimeOffset.UtcNow,
            embedder.Model,
            drafts.Select((d, i) => d.WithEmbedding(chunkVectors[i])).ToList(),
            queries,
            queryVectors,
            GroundTruth.Compute(queries, drafts),
            ProductionSources.Sha256AtCommit);
        DataStore.WriteAtomically(sampleRoot, data, ct);
        output.WriteLine($"Wrote data/ ({drafts.Count} chunk vectors, {queries.Count} query vectors).");
        return ExitCodes.Success;
    }
}
