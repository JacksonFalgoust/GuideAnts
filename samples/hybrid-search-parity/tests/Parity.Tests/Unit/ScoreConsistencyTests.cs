using Parity.Core.Comparison;
using Parity.Core.Contracts;
using Parity.Core.Model;
using Parity.Core.Settings;
using Parity.Tests.Support;
using static Parity.Tests.Support.Hits;

namespace Parity.Tests.Unit;

/// <summary>
/// The diagnostics explain rankings with their own queries (on Postgres a copy of the searcher's SQL). Each top-5 hit
/// carries the scores the code under test computed, so the diagnostics must reproduce them, or the explanations are
/// about some other code.
/// </summary>
public sealed class ScoreConsistencyTests
{
    private const double Tol = 1e-5;
    private const double Tie = 1e-6;

    private static ChunkScore Diag(string key, double vector, double text, bool matched = true, bool inCandidates = true, double? raw = null, double? nth = null) =>
        new(Id(key), vector, text, matched, raw ?? (matched ? text : null), inCandidates, nth);

    private static IReadOnlyDictionary<Guid, ChunkScore> Map(params ChunkScore[] scores) => scores.ToDictionary(s => s.ChunkId);

    [Fact]
    public void Hits_whose_scores_the_diagnostics_reproduce_pass()
    {
        var hits = new[] { Hit("a", 0.6, vectorScore: 0.5, textScore: 0.3), Hit("b", 0.35, vectorScore: 0.5, textScore: 0) };
        var mismatches = ScoreConsistency.Check(ScoreConsistency.Baseline, hits, Map(Diag("a", 0.5, 0.3), Diag("b", 0.5000001, 0, matched: false, inCandidates: false)), Tol, Tie);
        Assert.Empty(mismatches);
    }

    [Fact]
    public void A_text_score_the_diagnostics_do_not_reproduce_is_unexplained()
    {
        var hits = new[] { Hit("a", 0.6, vectorScore: 0.5, textScore: 0.3) };
        var mismatch = Assert.Single(ScoreConsistency.Check(ScoreConsistency.Candidate, hits, Map(Diag("a", 0.5, 0.05)), Tol, Tie));

        Assert.Equal(DifferenceCategory.Unexplained, mismatch.Category);
        Assert.Equal((null, 1), (mismatch.BaselineRank, mismatch.CandidateRank));
        Assert.Contains("candidate diagnostics disagree with the hit", mismatch.Detail);
        Assert.Contains("text 0.300000 vs 0.050000", mismatch.Detail);
    }

    [Fact]
    public void A_vector_score_outside_the_vector_tolerance_is_unexplained()
    {
        var hits = new[] { Hit("a", 0.35, vectorScore: 0.5, textScore: 0) };
        var mismatch = Assert.Single(ScoreConsistency.Check(ScoreConsistency.Baseline, hits, Map(Diag("a", 0.50002, 0, matched: false, inCandidates: false)), Tol, Tie));
        Assert.Equal((1, null), (mismatch.BaselineRank, mismatch.CandidateRank));
        Assert.Contains("vector 0.500000 vs 0.500020", mismatch.Detail);
    }

    [Fact]
    public void A_hit_missing_from_the_diagnostics_is_unexplained()
    {
        var mismatch = Assert.Single(ScoreConsistency.Check(ScoreConsistency.Baseline, [Hit("a", 0.35)], Map(), Tol, Tie));
        Assert.Contains("no diagnostics score", mismatch.Detail);
    }

    /// <summary>
    /// Chunks tied at the last full-text candidate slot are kept or cut arbitrarily (LIMIT without a tie-breaker, in
    /// production and in the diagnostics alike). Only then may the hit's text score be its raw rank where the
    /// diagnostics have 0, or the other way round.
    /// </summary>
    [Fact]
    public void A_text_score_may_differ_only_for_a_chunk_tied_at_the_candidate_cut()
    {
        var keptByProduction = new[] { Hit("a", 0.6, vectorScore: 0.5, textScore: 0.2) };
        var cutByDiagnostics = Diag("a", 0.5, 0, matched: true, inCandidates: false, raw: 0.2, nth: 0.2);
        Assert.Empty(ScoreConsistency.Check(ScoreConsistency.Baseline, keptByProduction, Map(cutByDiagnostics), Tol, Tie));

        var cutByProduction = new[] { Hit("a", 0.35, vectorScore: 0.5, textScore: 0) };
        var keptByDiagnostics = Diag("a", 0.5, 0.2, matched: true, inCandidates: true, raw: 0.2, nth: 0.2);
        Assert.Empty(ScoreConsistency.Check(ScoreConsistency.Baseline, cutByProduction, Map(keptByDiagnostics), Tol, Tie));

        var notAtTheCut = Diag("a", 0.5, 0, matched: true, inCandidates: false, raw: 0.2, nth: 0.3);
        Assert.Single(ScoreConsistency.Check(ScoreConsistency.Baseline, keptByProduction, Map(notAtTheCut), Tol, Tie));

        var tiedButAnotherValue = new[] { Hit("a", 0.6, vectorScore: 0.5, textScore: 0.25) };
        Assert.Single(ScoreConsistency.Check(ScoreConsistency.Baseline, tiedButAnotherValue, Map(cutByDiagnostics), Tol, Tie));
    }

    /// <summary>Diagnostics that report another text score than the store's hits.</summary>
    private sealed class DriftedDiagnostics(InMemoryEngine inner, double factor) : ISearchDiagnostics
    {
        public Task<EngineResult<IReadOnlyList<VectorHit>>> VectorSearchAsync(float[] vector, Scope scope, int k, CancellationToken ct) => inner.VectorSearchAsync(vector, scope, k, ct);

        public Task<EngineResult<IReadOnlyList<TextMatch>>> TextMatchAsync(string text, Scope scope, CancellationToken ct) => inner.TextMatchAsync(text, scope, ct);

        public async Task<EngineResult<IReadOnlyList<ChunkScore>>> ScoreChunksAsync(HybridQuery query, IReadOnlyCollection<Guid> chunkIds, CancellationToken ct)
        {
            var scores = (await inner.ScoreChunksAsync(query, chunkIds, ct)).Value!;
            return EngineResult<IReadOnlyList<ChunkScore>>.Ok(scores.Select(s => s with { TextScore = s.TextScore * factor, RawRank = s.RawRank * factor, NthCandidateRank = s.NthCandidateRank * factor }).ToList());
        }

        public Task<EngineResult<IReadOnlyList<Token>>> TokenizeAsync(string text, CancellationToken ct) => inner.TokenizeAsync(text, ct);

        public Task<EngineInfo> DescribeAsync(CancellationToken ct) => inner.DescribeAsync(ct);

        public Task<IndexCoverage> GetIndexCoverageAsync(CancellationToken ct) => inner.GetIndexCoverageAsync(ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_runner_checks_every_top_five_hit_and_fails_the_verdict_on_drift(bool drifted)
    {
        var candidateEngine = new InMemoryEngine();
        var candidate = TestData.Engine("b", candidateEngine, MiniCorpus.Chunks);
        if (drifted)
        {
            candidate = new SearchEngine("b", "InMemory", candidateEngine, new DriftedDiagnostics(candidateEngine, 0.5));
        }

        var runner = new ComparisonRunner(TestData.Engine("a", new InMemoryEngine(), MiniCorpus.Chunks), candidate,
            TestData.DataSet(MiniCorpus.Chunks, ("Q01", "exact", "conversation lock")), new ParitySettings(), []);
        var pair = Assert.Single(await runner.RunAsync([new NamedScope("P1", MiniCorpus.ScopeP1)], CancellationToken.None));

        Assert.Equal(pair.BaselineHits.Take(5).Count() + pair.CandidateHits.Take(5).Count(), pair.ScoresChecked);
        var verdict = Verdict.Evaluate([pair], new ThresholdSettings());
        if (drifted)
        {
            Assert.Contains(pair.Differences, d => d.Category == DifferenceCategory.Unexplained && d.Detail.StartsWith("candidate diagnostics disagree", StringComparison.Ordinal));
            Assert.False(verdict.Conditions[4].Pass);
        }
        else
        {
            Assert.Empty(pair.Differences);
            Assert.True(verdict.Switchable);
        }
    }
}
