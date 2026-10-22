using System.Text.RegularExpressions;
using Parity.Core.Comparison;
using Parity.Core.Contracts;
using Parity.Core.Data;
using Parity.Core.Model;
using Parity.Core.Settings;
using Parity.Tests.Support;

namespace Parity.Tests.Unit;

public class ComparisonRunnerTests
{
    private static readonly NamedScope P1 = new("P1", MiniCorpus.ScopeP1);
    private static readonly NamedScope N1 = new("P1/N1", MiniCorpus.ScopeN1);

    private static ComparisonRunner Runner(InMemoryEngine baseline, InMemoryEngine candidate, params (string, string, string)[] queries) =>
        new(TestData.Engine("a", baseline, MiniCorpus.Chunks), TestData.Engine("b", candidate, MiniCorpus.Chunks),
            TestData.DataSet(MiniCorpus.Chunks, queries), new ParitySettings(), []);

    [Fact]
    public async Task IdenticalEnginesAgreeOnEverything()
    {
        var pairs = await Runner(new(), new(), ("Q01", "exact", "conversation lock"), ("Q37", "stress", "alpha beta"))
            .RunAsync([P1, N1], CancellationToken.None);

        Assert.Equal(4, pairs.Count);
        Assert.All(pairs, p =>
        {
            Assert.True(p.Vector.Pass, p.Vector.Failure);
            Assert.True(p.Text!.IsEqual);
            Assert.Equal(new HybridPairMetrics(1.0, true), p.Hybrid);
            Assert.Empty(p.Differences);
        });
        Assert.True(Verdict.Evaluate(pairs, new ThresholdSettings()).Switchable);
    }

    private static readonly TextRankFunc Stemming = (content, query) =>
        query == "conversation lock" && Regex.IsMatch(content, "conversation locks?", RegexOptions.IgnoreCase)
            ? 0.5
            : InMemoryEngine.DefaultTextRank(content, query);

    private delegate double? TextRankFunc(string content, string query);

    [Fact]
    public async Task AnExtraMatchIsReportedWithEvidenceAndExplained()
    {
        // The candidate also matches the plural "locks", like a stemming configuration would.
        var stemming = new InMemoryEngine { TextRank = (c, q) => Stemming(c, q) };
        // The query vector leans towards m02 but sits close to m01 (the only baseline text match). On the
        // baseline m01 outranks m02 thanks to its text score; the candidate's extra text score for m02
        // lifts it above m01. The corpus embeddings are deterministic, so this rank change is fixed.
        var m01 = MiniCorpus.Chunks.Single(c => c.Id == MiniCorpus.Id("m01")).Embedding;
        var m02Embedding = MiniCorpus.Chunks.Single(c => c.Id == MiniCorpus.Id("m02")).Embedding;
        var vector = m02Embedding.Zip(m01, (b, a) => b + (0.9f * a)).ToArray();
        var data = TestData.DataSet(MiniCorpus.Chunks);
        var queries = new[] { new SavedQuery("Q01", "exact", "conversation lock", vector) };
        var runner = new ComparisonRunner(TestData.Engine("a", new InMemoryEngine(), MiniCorpus.Chunks), TestData.Engine("b", stemming, MiniCorpus.Chunks),
            new DataSet(data.Manifest, data.Chunks, queries, data.GroundTruth), new ParitySettings(), []);
        var pair = Assert.Single(await runner.RunAsync([P1], CancellationToken.None));

        Assert.Equal(new[] { MiniCorpus.Id("m02") }, pair.Text!.CandidateOnly);
        Assert.Equal(ReviewOutcome.Unreviewed, pair.TextReview!.Outcome);
        var snippet = Assert.Single(pair.Evidence!.Snippets);
        Assert.Equal("candidate-only", snippet.Side);
        Assert.Contains("locks", snippet.Snippet);
        Assert.DoesNotContain(pair.Differences, d => d.Category == DifferenceCategory.Unexplained);
        var m02 = Assert.Single(pair.Differences, d => d.ChunkId == MiniCorpus.Id("m02"));
        Assert.Equal(DifferenceCategory.MatchedOneSide, m02.Category);
    }

    [Fact]
    public async Task AHybridOnlyErrorKeepsTheTextDifferenceAndItsReview()
    {
        var stemming = new InMemoryEngine
        {
            TextRank = (c, q) => Stemming(c, q),
            FailOnHybrid = text => text == "conversation lock" ? new EngineError("PG42601", "syntax error") : null,
        };
        var pair = Assert.Single(await Runner(new(), stemming, ("Q01", "exact", "conversation lock")).RunAsync([P1], CancellationToken.None));

        Assert.Equal("candidate", pair.Error!.Side);
        Assert.Null(pair.Hybrid);
        Assert.Empty(pair.Differences);
        Assert.False(pair.Text!.IsEqual);
        Assert.Equal(ReviewOutcome.Unreviewed, pair.TextReview!.Outcome);
        Assert.NotNull(pair.Evidence);
        Assert.NotNull(pair.ErrorReview);

        // Accepting the error alone must not make the verdict pass while the text difference is open.
        var known = new[]
        {
            new KnownDifference("Q01", "P1", "error", null, null, "candidate", "PG42601", "error-handling", "reviewed", "acceptable", "t", "2026-10-22"),
        };
        var reviewed = new ComparisonRunner(TestData.Engine("a", new InMemoryEngine(), MiniCorpus.Chunks), TestData.Engine("b", stemming, MiniCorpus.Chunks),
            TestData.DataSet(MiniCorpus.Chunks, ("Q01", "exact", "conversation lock")), new ParitySettings(), known);
        var pairs = await reviewed.RunAsync([P1], CancellationToken.None);
        Assert.Equal(ReviewOutcome.Acceptable, pairs[0].ErrorReview!.Outcome);
        Assert.Equal(ReviewOutcome.Unreviewed, pairs[0].TextReview!.Outcome);
        Assert.False(Verdict.Evaluate(pairs, new ThresholdSettings()).Switchable);
    }

    [Fact]
    public async Task ABaselineSidedErrorIsAttributedToTheBaseline()
    {
        var failing = new InMemoryEngine { FailOn = text => text == "!!! ???" ? new EngineError("PG42601", "syntax error") : null };
        var pair = Assert.Single(await Runner(failing, new(), ("Q36", "error", "!!! ???")).RunAsync([P1], CancellationToken.None));

        Assert.Equal("baseline", pair.Error!.Side);
        Assert.NotNull(pair.ErrorReview);
        Assert.Equal(ReviewOutcome.Unreviewed, pair.ErrorReview!.Outcome);
        Assert.Null(pair.Hybrid);
        Assert.Null(pair.Text);
    }

    [Fact]
    public async Task AMissingScoredChunkThrowsNamingTheEngineAndChunk()
    {
        var inner = new InMemoryEngine();
        var baseline = TestData.Engine("a", inner, MiniCorpus.Chunks);
        var dropping = new SearchEngine("b", "InMemory", inner, new DroppingDiagnostics(inner));
        var runner = new ComparisonRunner(baseline, dropping, TestData.DataSet(MiniCorpus.Chunks, ("Q01", "exact", "conversation lock")), new ParitySettings(), []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync([P1], CancellationToken.None));
        Assert.Contains("b", ex.Message);
        Assert.Contains(DroppingDiagnostics.Dropped.ToString(), ex.Message);
    }

    private sealed class DroppingDiagnostics(InMemoryEngine inner) : ISearchDiagnostics
    {
        public static Guid Dropped => MiniCorpus.Id("m01");

        public async Task<EngineResult<IReadOnlyList<ChunkScore>>> ScoreChunksAsync(HybridQuery query, IReadOnlyCollection<Guid> chunkIds, CancellationToken ct)
        {
            var result = await inner.ScoreChunksAsync(query, chunkIds, ct);
            return EngineResult<IReadOnlyList<ChunkScore>>.Ok(result.Value!.Where(s => s.ChunkId != Dropped).ToList());
        }

        public Task<EngineResult<IReadOnlyList<VectorHit>>> VectorSearchAsync(float[] vector, Scope scope, int k, CancellationToken ct) => inner.VectorSearchAsync(vector, scope, k, ct);
        public Task<EngineResult<IReadOnlyList<TextMatch>>> TextMatchAsync(string text, Scope scope, CancellationToken ct) => inner.TextMatchAsync(text, scope, ct);
        public Task<EngineResult<IReadOnlyList<Token>>> TokenizeAsync(string text, CancellationToken ct) => inner.TokenizeAsync(text, ct);
        public Task<EngineInfo> DescribeAsync(CancellationToken ct) => inner.DescribeAsync(ct);
        public Task<IndexCoverage> GetIndexCoverageAsync(CancellationToken ct) => inner.GetIndexCoverageAsync(ct);
    }

    [Fact]
    public async Task AOneSidedErrorIsLeftOutOfTheMetricsAndNeedsReview()
    {
        var failing = new InMemoryEngine { FailOn = text => text == "!!! ???" ? new EngineError("PG42601", "syntax error") : null };
        var pair = Assert.Single(await Runner(new(), failing, ("Q36", "error", "!!! ???")).RunAsync([P1], CancellationToken.None));

        Assert.Equal("candidate", pair.Error!.Side);
        Assert.Null(pair.Hybrid);
        Assert.Equal(ReviewOutcome.Unreviewed, pair.ErrorReview!.Outcome);
        Assert.True(pair.Vector.Pass);
    }

    [Fact]
    public async Task TheQuirkFixedRunPassesTheFlagToBothEngines()
    {
        InMemoryEngine a = new(), b = new();
        await Runner(a, b, ("Q37", "stress", "alpha beta")).RunAsync([P1], CancellationToken.None, filterBeforeTopN: true);
        Assert.All(a.HybridQueries.Concat(b.HybridQueries), q => Assert.True(q.FilterBeforeTopN));
    }

    [Fact]
    public void InformationalSummaryCountsExtraAndLostMatches()
    {
        var pair = new PairResult(new QueryRef("Q01", "exact", "x"), "P1", VectorParityResult.Passed,
            new TextDiff([MiniCorpus.Id("m01")], [MiniCorpus.Id("m02"), MiniCorpus.Id("m03")]), null, null, null, null,
            new HybridPairMetrics(0.8, false), [], [], []);
        var summary = InformationalSummary.From("postgres-english", [pair]);
        Assert.Equal((2, 1, 0.8, 0.0), (summary.ExtraMatches, summary.LostMatches, summary.MeanOverlap, summary.SameTopOneRate));
    }

    [Theory]
    [InlineData("aaa conversation locks expire bbb", "conversation lock", "conversation locks expire")]
    [InlineData("no match at all", "zzz", "no match at all")]
    public void SnippetsCentreOnTheLongestQueryWord(string content, string query, string expectedFragment) =>
        Assert.Contains(expectedFragment, Snippets.Around(content, query));
}
