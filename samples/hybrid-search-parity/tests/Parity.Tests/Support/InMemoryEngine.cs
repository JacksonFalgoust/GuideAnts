using Parity.Core.Contracts;
using Parity.Core.Corpus;
using Parity.Core.Model;

namespace Parity.Tests.Support;

/// <summary>
/// A database-free engine with production's algorithm: cosine vectors, a text rank, top-N
/// candidates across every project (unless FilterBeforeTopN), and the same WHERE and ORDER BY.
/// </summary>
public sealed class InMemoryEngine : ISearchStore, ISearchDiagnostics
{
    private readonly List<ChunkRecord> _chunks = [];

    /// <summary>Text rank for (content, query), or null when it doesn't match. Default: n/(n+1) phrase occurrences.</summary>
    public Func<string, string, double?> TextRank { get; init; } = DefaultTextRank;

    /// <summary>Return an error to simulate a database rejecting a query.</summary>
    public Func<string, EngineError?> FailOn { get; init; } = _ => null;

    public List<HybridQuery> HybridQueries { get; } = [];

    public static double? DefaultTextRank(string content, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var count = GroundTruth.PhraseRegex(query).Matches(content).Count;
        return count == 0 ? null : count / (count + 1.0);
    }

    public Task PingServerAsync(CancellationToken ct) => Task.CompletedTask;

    public Task EnsureSchemaAsync(CancellationToken ct)
    {
        _chunks.Clear();
        return Task.CompletedTask;
    }

    public Task LoadAsync(IReadOnlyList<ChunkRecord> chunks, DateTime createdUtc, CancellationToken ct)
    {
        _chunks.AddRange(chunks);
        return Task.CompletedTask;
    }

    public Task WaitUntilSearchableAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<EngineResult<IReadOnlyList<SearchHit>>> HybridSearchAsync(HybridQuery q, CancellationToken ct)
    {
        HybridQueries.Add(q);
        if (string.IsNullOrWhiteSpace(q.Text))
        {
            return Ok<IReadOnlyList<SearchHit>>([]);
        }

        if (FailOn(q.Text) is { } error)
        {
            return Task.FromResult(EngineResult<IReadOnlyList<SearchHit>>.Fail(error));
        }

        var candidates = Candidates(q);
        IReadOnlyList<SearchHit> hits = InScope(q.Scope)
            .Select(c =>
            {
                var vs = Cosine(c.Embedding, q.Vector);
                var ts = candidates.GetValueOrDefault(c.Id);
                return new SearchHit(c.Id, c.Content, c.ContentFileId, null, c.ChunkIndex, c.IndexName, vs, ts, (q.VectorWeight * vs) + (q.TextWeight * ts));
            })
            .Where(h => h.TextScore > 0 || (1 - h.VectorScore) < 1.0)
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.ChunkId)
            .Take(q.MaxResults)
            .ToList();
        return Ok(hits);
    }

    public Task<EngineResult<IReadOnlyList<VectorHit>>> VectorSearchAsync(float[] vector, Scope scope, int k, CancellationToken ct) =>
        Ok<IReadOnlyList<VectorHit>>(InScope(scope)
            .Select(c => new VectorHit(c.Id, Cosine(c.Embedding, vector)))
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.ChunkId)
            .Take(k)
            .ToList());

    public Task<EngineResult<IReadOnlyList<TextMatch>>> TextMatchAsync(string text, Scope scope, CancellationToken ct)
    {
        if (FailOn(text) is { } error)
        {
            return Task.FromResult(EngineResult<IReadOnlyList<TextMatch>>.Fail(error));
        }

        return Ok<IReadOnlyList<TextMatch>>(InScope(scope)
            .Select(c => (c.Id, Rank: TextRank(c.Content, text)))
            .Where(x => x.Rank is not null)
            .Select(x => new TextMatch(x.Id, x.Rank!.Value))
            .ToList());
    }

    public Task<EngineResult<IReadOnlyList<ChunkScore>>> ScoreChunksAsync(HybridQuery q, IReadOnlyCollection<Guid> chunkIds, CancellationToken ct)
    {
        var all = _chunks.Select(c => (c.Id, Rank: TextRank(c.Content, q.Text))).Where(x => x.Rank is not null).ToDictionary(x => x.Id, x => x.Rank!.Value);
        var candidates = Candidates(q);
        double? nth = candidates.Count == q.CandidateLimit ? candidates.Values.Min() : null;
        return Ok<IReadOnlyList<ChunkScore>>(chunkIds
            .Select(id => _chunks.Single(c => c.Id == id))
            .Select(c => new ChunkScore(
                c.Id,
                Cosine(c.Embedding, q.Vector),
                candidates.GetValueOrDefault(c.Id),
                all.ContainsKey(c.Id),
                all.TryGetValue(c.Id, out var raw) ? raw : null,
                candidates.ContainsKey(c.Id),
                nth))
            .ToList());
    }

    public Task<EngineResult<IReadOnlyList<Token>>> TokenizeAsync(string text, CancellationToken ct) =>
        Ok<IReadOnlyList<Token>>(text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => new Token(w, w.ToLowerInvariant(), "word", false)).ToList());

    public Task<EngineInfo> DescribeAsync(CancellationToken ct) =>
        Task.FromResult(new EngineInfo("InMemory", "test", new Dictionary<string, string>()));

    public Task<IndexCoverage> GetIndexCoverageAsync(CancellationToken ct) =>
        Task.FromResult(new IndexCoverage(_chunks.Count, _chunks.Count));

    private Dictionary<Guid, double> Candidates(HybridQuery q) =>
        (q.FilterBeforeTopN ? InScope(q.Scope) : _chunks)
            .Select(c => (c.Id, Rank: TextRank(c.Content, q.Text)))
            .Where(x => x.Rank is not null)
            .OrderByDescending(x => x.Rank)
            .ThenBy(x => x.Id)
            .Take(q.CandidateLimit)
            .ToDictionary(x => x.Id, x => x.Rank!.Value);

    private IEnumerable<ChunkRecord> InScope(Scope scope) =>
        _chunks.Where(c => c.IndexName == scope.IndexName && (scope.NotebookId is null || c.NotebookId == scope.NotebookId));

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private static Task<EngineResult<T>> Ok<T>(T value) => Task.FromResult(EngineResult<T>.Ok(value));
}
