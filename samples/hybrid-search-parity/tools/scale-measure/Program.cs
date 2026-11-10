using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using Parity.Cli;
using Parity.Core;
using Parity.Core.Comparison;
using Parity.Core.Contracts;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Model;
using Parity.Postgres;
using Pgvector;

// Reads the seeded prod-sqlserver and prod-postgres profiles (seed them first). Prints:
//   1. the Postgres / SQL Server text score ratio for every (query, chunk) both match in P1 to P3;
//   2. the verdict metrics with production's Postgres text score divided by each divisor k (k = 1 is production);
//   3. per query group, the overlap and same #1 at k = 1;
//   4. the pairs whose #1 differs at k = 1, with whether each #1 contains the searched phrase.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var root = RepoPaths.FindSampleRoot();
var settings = AppConfig.Load(root, includeLocalOverrides: false, environment: _ => null);
var data = DataStore.Load(root);
await using var sql = SearchStoreFactory.Create(settings, "prod-sqlserver");
await using var pg = SearchStoreFactory.Create(settings, "prod-postgres");
var divisors = args.Length > 0 ? args[0].Split(',').Select(double.Parse).ToArray() : [1.0, 2, 4, 5.68, 8];
if (divisors[0] != 1.0)
{
    throw new ParityConfigurationException("The first divisor must be 1 (production), the reference for sections 3 and 4.");
}

Console.WriteLine("## 1. Text score ratio, Postgres / SQL Server (chunks both match, scopes P1 to P3)");
var ratios = new List<(string Query, double Sql, double Pg)>();
foreach (var q in data.Queries)
foreach (var label in ParityScopes.ProjectLabels)
{
    var scope = ParityScopes.Find(label).Scope;
    var a = await sql.Diagnostics.TextMatchAsync(q.Text, scope, default);
    var b = await pg.Diagnostics.TextMatchAsync(q.Text, scope, default);
    if (!a.IsSuccess || !b.IsSuccess)
    {
        continue;
    }

    var pgRanks = b.Value.ToDictionary(m => m.ChunkId, m => m.Rank);
    ratios.AddRange(a.Value.Where(m => m.Rank > 0 && pgRanks.ContainsKey(m.ChunkId)).Select(m => (q.Id, m.Rank, pgRanks[m.ChunkId])));
}

var rs = ratios.Select(x => x.Pg / x.Sql).Order().ToList();
double P(double p) => rs[(int)Math.Round(p * (rs.Count - 1))];
Console.WriteLine($"{rs.Count} pairs over {ratios.Select(x => x.Query).Distinct().Count()} queries");
Console.WriteLine($"min {rs[0]:F2} | p10 {P(.1):F2} | p25 {P(.25):F2} | median {P(.5):F2} | p75 {P(.75):F2} | p90 {P(.9):F2} | max {rs[^1]:F2} | geometric mean {Math.Exp(rs.Average(Math.Log)):F2}");
foreach (var g in ratios.GroupBy(x => x.Query).OrderBy(g => g.Key, StringComparer.Ordinal))
{
    var gr = g.Select(x => x.Pg / x.Sql).Order().ToList();
    Console.WriteLine($"  {g.Key}: n={gr.Count} median ratio {gr[gr.Count / 2]:F2}, mean text SQL Server {g.Average(x => x.Sql):F4}, Postgres {g.Average(x => x.Pg):F4}");
}

Console.WriteLine();
Console.WriteLine("## 2. Production's Postgres text score divided by k, against prod-sqlserver");
Console.WriteLine("Exact phrase in top 5: of the exact and edge query/scope pairs whose scope holds a ground-truth chunk, those whose top 5 holds one.");
Console.WriteLine("k | mean top-5 overlap | same #1 | unexplained | exact phrase in top 5, SQL Server | exact phrase in top 5, Postgres");
var known = KnownDifferences.ForCandidate(KnownDifferences.Load(Path.Combine(root, "known-differences.json")), "prod-postgres");
var pgConnection = settings.Profiles["prod-postgres"].ConnectionString;
IReadOnlyList<PairResult>? production = null;
foreach (var k in divisors)
{
    await using var store = new ScaledStore(pgConnection, k);
    var candidate = new SearchEngine($"prod-postgres/{k}", "Postgres", store, new ScaledDiagnostics(pg.Diagnostics, k));
    var pairs = await new ComparisonRunner(sql, candidate, data, settings, known).RunAsync(ParityScopes.All, default);
    var v = Verdict.Evaluate(pairs, settings.Parity.Thresholds);
    var (n, foundSql, foundPg) = ExactPhraseFound(pairs);
    Console.WriteLine($"{k} | {v.Conditions[2].Value} | {v.Conditions[3].Value} | {v.Conditions[4].Value} | {foundSql}/{n} | {foundPg}/{n}");
    production ??= pairs;
}

Console.WriteLine();
Console.WriteLine("## 3. Per query group at k = 1 (pairs with no error)");
var reference = production ?? throw new InvalidOperationException("no k = 1 run");
foreach (var g in reference.Where(p => p.Hybrid is not null).GroupBy(p => p.Query.Group).OrderBy(g => g.Key, StringComparer.Ordinal))
{
    Console.WriteLine($"  {g.Key}: {g.Count()} pairs, mean top-5 overlap {g.Average(p => p.Hybrid!.OverlapAt5):F3}, same #1 {g.Count(p => p.Hybrid!.SameTopOne) * 100.0 / g.Count():F1}%");
}

Console.WriteLine();
Console.WriteLine("## 4. Pairs whose #1 differs at k = 1");
Console.WriteLine("pair | SQL Server #1 (phrase? ground truth?) | Postgres #1 (phrase? ground truth?) | SQL Server #1's rank on Postgres | Postgres #1's rank on SQL Server");
foreach (var p in reference.Where(p => p.Hybrid is { SameTopOne: false }))
{
    var query = data.Queries.Single(q => q.Id == p.Query.Id);
    var truth = data.GroundTruth.TryGetValue(query.Id, out var t) ? t.ToHashSet() : [];
    var phrase = GroundTruth.PhraseRegex(query.Text);
    string Describe(SearchHit hit) =>
        $"{Label(hit.ChunkId)} ({(phrase.IsMatch(data.FindChunk(hit.ChunkId)!.Content) ? "phrase" : "no phrase")}, {(truth.Contains(hit.ChunkId) ? "ground truth" : "not ground truth")})";
    string RankIn(IReadOnlyList<SearchHit> hits, Guid id)
    {
        var i = hits.Take(RankingMetrics.TopN).ToList().FindIndex(h => h.ChunkId == id);
        return i < 0 ? "not in top 5" : $"#{i + 1}";
    }

    var (a, b) = (p.BaselineHits[0], p.CandidateHits[0]);
    Console.WriteLine($"{p.Query.Id} · {p.Scope} ({query.Group}, \"{query.Text}\") | {Describe(a)} | {Describe(b)} | {RankIn(p.CandidateHits, a.ChunkId)} | {RankIn(p.BaselineHits, b.ChunkId)}");
}

string Label(Guid id) => data.FindChunk(id) is { } c ? (c.Kind == ChunkKind.Edge ? c.SourcePath : $"{c.SourcePath}#{c.ChunkIndex}") : id.ToString()[..8];

(int Pairs, int Sql, int Pg) ExactPhraseFound(IReadOnlyList<PairResult> pairs)
{
    int n = 0, a = 0, b = 0;
    foreach (var p in pairs)
    {
        var q = data.Queries.Single(x => x.Id == p.Query.Id);
        if (!QueryGroups.HasGroundTruth(q.Group) || !data.GroundTruth.TryGetValue(q.Id, out var truthList))
        {
            continue;
        }

        var scope = ParityScopes.Find(p.Scope).Scope;
        var truth = truthList.Where(id => data.FindChunk(id) is { } c && c.IndexName == scope.IndexName && (scope.NotebookId is null || c.NotebookId == scope.NotebookId)).ToHashSet();
        if (truth.Count == 0)
        {
            continue;
        }

        n++;
        a += p.BaselineHits.Take(RankingMetrics.TopN).Any(h => truth.Contains(h.ChunkId)) ? 1 : 0;
        b += p.CandidateHits.Take(RankingMetrics.TopN).Any(h => truth.Contains(h.ChunkId)) ? 1 : 0;
    }

    return (n, a, b);
}

/// <summary>
/// Production's Postgres query, read from PostgresHybridSearcher.cs so it can't drift, with the text score divided by
/// @k. Dividing doesn't change which chunks are full-text candidates (the order is the same), only the hybrid score.
/// </summary>
internal sealed class ScaledStore : ISearchStore, IAsyncDisposable
{
    private const string TextScore = "CAST(COALESCE(ft.rank, 0) AS float8) AS text_score";
    private static readonly string Sql = Load();
    private readonly NpgsqlDataSource _dataSource;
    private readonly double _k;

    public ScaledStore(string connectionString, double k)
    {
        _dataSource = PostgresDataSources.Build(connectionString);
        _k = k;
    }

    private static string Load()
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), "src", "server", "GuideAntsApi.BackgroundJobs", "Services", "Search", "PostgresHybridSearcher.cs");
        var source = File.ReadAllText(path);
        const string start = "internal const string Sql = $$\"\"\"";
        var from = source.IndexOf(start, StringComparison.Ordinal) + start.Length;
        var to = source.IndexOf("\"\"\";", from, StringComparison.Ordinal);
        var sql = source[from..to]
            .Replace("{{PostgresFullText.Config}}", GuideAntsApi.BackgroundJobs.Services.Search.PostgresFullText.Config)
            .Replace("{{PostgresFullText.NormalizeFunction}}", GuideAntsApi.BackgroundJobs.Services.Search.PostgresFullText.NormalizeFunction);
        if (!sql.Contains(TextScore, StringComparison.Ordinal) || sql.Contains("{{", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"PostgresHybridSearcher's SQL changed; update {nameof(ScaledStore)}.");
        }

        return sql.Replace(TextScore, "CAST(COALESCE(ft.rank, 0) AS float8) / @k AS text_score");
    }

    public Task PingServerAsync(CancellationToken ct) => Task.CompletedTask;

    public Task EnsureSchemaAsync(CancellationToken ct) => throw new NotSupportedException("Seed prod-postgres with the CLI.");

    public Task LoadAsync(IReadOnlyList<ChunkRecord> chunks, DateTime createdUtc, CancellationToken ct) => throw new NotSupportedException("Seed prod-postgres with the CLI.");

    public Task WaitUntilSearchableAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<EngineResult<IReadOnlyList<SearchHit>>> HybridSearchAsync(HybridQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return EngineResult<IReadOnlyList<SearchHit>>.Ok([]);
        }

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(Sql, connection);
        command.Parameters.Add(new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = query.Text.Replace('\0', '�') });
        command.Parameters.Add(new NpgsqlParameter("vec", new Vector(query.Vector)));
        command.Parameters.Add(new NpgsqlParameter("indexName", NpgsqlDbType.Text) { Value = query.Scope.IndexName });
        command.Parameters.Add(new NpgsqlParameter("notebookId", NpgsqlDbType.Uuid) { Value = (object?)query.Scope.NotebookId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("candidateLimit", NpgsqlDbType.Integer) { Value = query.CandidateLimit });
        command.Parameters.Add(new NpgsqlParameter("maxResults", NpgsqlDbType.Integer) { Value = query.MaxResults });
        command.Parameters.Add(new NpgsqlParameter("vectorWeight", NpgsqlDbType.Double) { Value = query.VectorWeight });
        command.Parameters.Add(new NpgsqlParameter("textWeight", NpgsqlDbType.Double) { Value = query.TextWeight });
        command.Parameters.Add(new NpgsqlParameter("k", NpgsqlDbType.Double) { Value = _k });
        var hits = new List<SearchHit>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            hits.Add(new SearchHit(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetInt32(4), reader.GetString(5), reader.GetDouble(6), reader.GetDouble(7), reader.GetDouble(8)));
        }

        return EngineResult<IReadOnlyList<SearchHit>>.Ok(hits);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}

/// <summary>Production's Postgres diagnostics with the text ranks divided by k, so the explanations match the scaled scores.</summary>
internal sealed class ScaledDiagnostics(ISearchDiagnostics inner, double k) : ISearchDiagnostics
{
    public Task<EngineResult<IReadOnlyList<VectorHit>>> VectorSearchAsync(float[] vector, Scope scope, int n, CancellationToken ct) => inner.VectorSearchAsync(vector, scope, n, ct);

    public async Task<EngineResult<IReadOnlyList<TextMatch>>> TextMatchAsync(string text, Scope scope, CancellationToken ct)
    {
        var result = await inner.TextMatchAsync(text, scope, ct);
        return result.IsSuccess ? EngineResult<IReadOnlyList<TextMatch>>.Ok(result.Value.Select(m => m with { Rank = m.Rank / k }).ToList()) : result;
    }

    public async Task<EngineResult<IReadOnlyList<ChunkScore>>> ScoreChunksAsync(HybridQuery query, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var result = await inner.ScoreChunksAsync(query, ids, ct);
        return result.IsSuccess
            ? EngineResult<IReadOnlyList<ChunkScore>>.Ok(result.Value.Select(s => s with { TextScore = s.TextScore / k, RawRank = s.RawRank / k, NthCandidateRank = s.NthCandidateRank / k }).ToList())
            : result;
    }

    public Task<EngineResult<IReadOnlyList<Token>>> TokenizeAsync(string text, CancellationToken ct) => inner.TokenizeAsync(text, ct);

    public Task<EngineInfo> DescribeAsync(CancellationToken ct) => inner.DescribeAsync(ct);

    public Task<IndexCoverage> GetIndexCoverageAsync(CancellationToken ct) => inner.GetIndexCoverageAsync(ct);
}
