using System.Globalization;
using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Embeddings;
using Parity.Core.Model;
using Parity.Core.Settings;

namespace Parity.Cli.Commands;

/// <summary>The "top-level app": it knows only ISearchStore and which profile the configuration names.</summary>
public sealed class SearchCommand(TextWriter output, string sampleRoot, Func<ParitySettings, string, SearchEngine> engineFactory, IEmbeddingClient? embedder)
{
    public async Task<int> RunAsync(CliArgs args, ParitySettings settings, CancellationToken ct)
    {
        var text = string.Join(' ', args.Positionals).Trim();
        if (text.Length == 0)
        {
            throw new ParityConfigurationException("search needs query text, for example: search \"conversation lock\".");
        }

        var profileName = args.Get("--profile") ?? settings.Search.Profile;
        var scope = ParityScopes.Find(args.Get("--scope") ?? "P1");
        var data = DataStore.Load(sampleRoot);
        await using var engine = engineFactory(settings, profileName);
        await engine.Store.PingServerAsync(ct);
        var (vector, source) = await QueryVectors.ResolveAsync(text, data, embedder, ct);

        var h = settings.Hybrid;
        var query = new HybridQuery(text, vector, scope.Scope, h.MaxResults, h.VectorWeight, h.TextWeight, h.CandidateMultiplier, args.Has("--quirk-fixed"));
        var result = await engine.Store.HybridSearchAsync(query, ct);

        output.WriteLine($"Profile {profileName} ({engine.Provider}) · scope {scope.Label} · \"{text}\" ({source}){(query.FilterBeforeTopN ? " · quirk fixed (not production)" : "")}");
        if (!result.IsSuccess)
        {
            output.WriteLine($"Database error [{result.Error.Code}]: {result.Error.Message}");
            output.WriteLine("Production catches this and returns 0 results.");
            return ExitCodes.Success;
        }

        output.WriteLine("  #  Score   Vector  Text    Chunk");
        for (var i = 0; i < result.Value.Count; i++)
        {
            var hit = result.Value[i];
            var chunk = data.FindChunk(hit.ChunkId);
            var label = chunk is null ? hit.ChunkId.ToString() : $"{chunk.SourcePath}{(chunk.Kind == ChunkKind.Doc ? $"#{chunk.ChunkIndex}" : "")}";
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {i + 1}  {hit.Score:F4}  {hit.VectorScore:F4}  {hit.TextScore:F4}  {label}"));
        }

        if (result.Value.Count == 0)
        {
            output.WriteLine("  (no results)");
        }

        return ExitCodes.Success;
    }
}
