using System.Data.Common;
using System.Diagnostics;
using GuideAntsApi.BackgroundJobs.Services.Search;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseSpeed.Operations;

/// <summary>
/// One hybrid search through the production <see cref="IHybridSearcher"/> registration (<c>AddBackgroundJobs</c>:
/// <c>HybridSearcher</c> on SQL Server, <c>PostgresHybridSearcher</c> on Postgres), scoped to one project's index, with
/// production's defaults (5 results, weights 0.7/0.3). The query embedding is a saved one from the parity sample's
/// <c>data/query-embeddings.f32</c>.
/// </summary>
public sealed class HybridSearchOperation : IOperation
{
    private const int MaxResults = 5;
    private readonly string _text;
    private readonly float[] _vector;
    private readonly string _indexName;
    private readonly Dictionary<DatabaseProvider, (string Searcher, long Matches, int Results)> _prepared = [];

    /// <param name="textSource">Where the text and vector come from, for the description.</param>
    public HybridSearchOperation(string name, string text, float[] vector, string textSource, SeedData seed)
    {
        Name = name;
        _text = text;
        _vector = vector;
        _indexName = seed.SearchIndexName;
        Description =
            $"`IHybridSearcher.SearchAsync(\"{text}\", <project 0's index>, notebookId: null, maxResults: {MaxResults}, 0.7, 0.3)`, " +
            $"resolved from the production registration, over {seed.Volumes.Chunks:N0} chunks in {Math.Min(seed.CorpusCopies, seed.Volumes.Projects)} project(s) " +
            $"(the scoped index holds {seed.ChunksInSearchIndex:N0}). Both searchers take the full-text candidates table-wide before the index filter, " +
            $"and neither database has a vector index, so the vector half scans the scoped index's rows. {textSource}";
    }

    public string Name { get; }

    public string Description { get; }

    public IReadOnlyList<string> Metrics => [Name];

    public async Task PrepareAsync(ProviderTarget target, CancellationToken ct)
    {
        await using var scope = target.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<QueryVector>().Value = _vector;
        var searcher = scope.ServiceProvider.GetRequiredService<IHybridSearcher>();
        var results = await searcher.SearchAsync(_text, _indexName, null, MaxResults, 0.7, 0.3, ct);
        _prepared[target.Provider] = (searcher.GetType().Name, await CountTableWideMatchesAsync(target, ct), results.Count);
    }

    public async Task RunAsync(ProviderTarget target, Measurement measurement, CancellationToken ct)
    {
        await using var scope = target.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<QueryVector>().Value = _vector;
        var searcher = scope.ServiceProvider.GetRequiredService<IHybridSearcher>();

        var stopwatch = Stopwatch.StartNew();
        var results = await searcher.SearchAsync(_text, _indexName, null, MaxResults, 0.7, 0.3, ct);
        stopwatch.Stop();

        // The searchers return an empty list on any error (and log it, which the run's log check catches); the
        // vector half always matches, so an empty list means the search didn't run.
        if (results.Count == 0)
        {
            throw new InvalidOperationException("The search returned no results.");
        }

        measurement.Add(Name, Measurement.Ms(stopwatch));
    }

    public IReadOnlyList<string> Notes(IReadOnlyDictionary<DatabaseProvider, Measurement> measured) =>
    [
        "Searcher resolved: " + string.Join("; ", _prepared.OrderBy(kv => kv.Key).Select(kv => $"{Report.ProviderName(kv.Key)} `{kv.Value.Searcher}`")),
        "Table-wide full-text matches for the text (every one is ranked before the candidate limit): " +
            string.Join("; ", _prepared.OrderBy(kv => kv.Key).Select(kv => $"{Report.ProviderName(kv.Key)} {kv.Value.Matches:N0}")),
        "Results returned: " + string.Join("; ", _prepared.OrderBy(kv => kv.Key).Select(kv => $"{Report.ProviderName(kv.Key)} {kv.Value.Results}")),
    ];

    /// <summary>
    /// How many chunks the full-text half matches across the whole table, counted with each searcher's own text
    /// condition (SQL Server: the double-quoted <c>CONTAINSTABLE</c> phrase; Postgres: <c>phraseto_tsquery</c> over the
    /// normalized text). Not timed.
    /// </summary>
    private async Task<long> CountTableWideMatchesAsync(ProviderTarget target, CancellationToken ct)
    {
        await using var db = await target.CreateContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        await using DbCommand command = db.Database.GetDbConnection().CreateCommand();
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@q";
        if (target.Provider == DatabaseProvider.SqlServer)
        {
            command.CommandText = "SELECT COUNT_BIG(*) FROM CONTAINSTABLE(DocumentChunks, Content, @q)";
            parameter.Value = $"\"{_text.Replace("\"", "\"\"")}\"";
        }
        else
        {
            command.CommandText =
                $"SELECT count(*) FROM \"DocumentChunks\" WHERE \"ContentTsv\" @@ phraseto_tsquery('{PostgresFullText.Config}', {PostgresFullText.NormalizeFunction}(@q))";
            parameter.Value = _text;
        }

        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }
}
