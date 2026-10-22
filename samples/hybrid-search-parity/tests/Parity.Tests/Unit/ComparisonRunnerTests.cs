using System.Text.RegularExpressions;
using Parity.Core.Comparison;
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

    [Fact]
    public async Task AnExtraMatchIsReportedWithEvidenceAndExplained()
    {
        // The candidate also matches the plural "locks", like a stemming configuration would.
        var stemming = new InMemoryEngine
        {
            TextRank = (content, query) => query == "conversation lock" && Regex.IsMatch(content, "conversation locks?", RegexOptions.IgnoreCase)
                ? 0.5
                : InMemoryEngine.DefaultTextRank(content, query),
        };
        var pair = Assert.Single(await Runner(new(), stemming, ("Q01", "exact", "conversation lock")).RunAsync([P1], CancellationToken.None));

        Assert.Equal(new[] { MiniCorpus.Id("m02") }, pair.Text!.CandidateOnly);
        Assert.Equal(ReviewOutcome.Unreviewed, pair.TextReview!.Outcome);
        var snippet = Assert.Single(pair.Evidence!.Snippets);
        Assert.Equal("candidate-only", snippet.Side);
        Assert.Contains("locks", snippet.Snippet);
        Assert.DoesNotContain(pair.Differences, d => d.Category == DifferenceCategory.Unexplained);
        // Whether m02 changes rank depends on the random vectors; if it does, the cause must be its one-sided match.
        Assert.All(pair.Differences.Where(d => d.ChunkId == MiniCorpus.Id("m02")), d => Assert.Equal(DifferenceCategory.MatchedOneSide, d.Category));
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
