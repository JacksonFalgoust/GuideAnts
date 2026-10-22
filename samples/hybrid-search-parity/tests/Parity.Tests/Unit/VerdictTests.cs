using Parity.Core;
using Parity.Core.Comparison;
using Parity.Core.Model;
using Parity.Core.Settings;
using static Parity.Tests.Support.Hits;

namespace Parity.Tests.Unit;

public sealed class VerdictTests : IDisposable
{
    private const double Tol = 1e-5;
    private const double Tie = 1e-6;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"parity-known-{Guid.NewGuid():N}");

    public VerdictTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ChunkScore Score(double vector, double text, bool matched, bool inCandidates, double? raw = null, double? nth = null) =>
        new(Id("x"), vector, text, matched, raw, inCandidates, nth);

    private static IReadOnlyDictionary<Guid, ChunkScore> Map(params ChunkScore[] scores) => scores.ToDictionary(s => s.ChunkId);

    [Fact]
    public void ClassifiesEachCause()
    {
        Assert.Equal(DifferenceCategory.ScoredDifferently, DifferenceClassifier.Classify(Score(0.5, 0.4, true, true), Score(0.5, 0.2, true, true), Tol, Tie));
        Assert.Equal(DifferenceCategory.MatchedOneSide, DifferenceClassifier.Classify(Score(0.5, 0.4, true, true), Score(0.5, 0, false, false), Tol, Tie));
        Assert.Equal(DifferenceCategory.TopNCut, DifferenceClassifier.Classify(Score(0.5, 0.4, true, true, 0.4), Score(0.5, 0, true, false, 0.3, 0.35), Tol, Tie));
        Assert.Equal(DifferenceCategory.TopNBoundaryTie, DifferenceClassifier.Classify(Score(0.5, 0.4, true, true, 0.4), Score(0.5, 0, true, false, 0.35, 0.35), Tol, Tie));
        Assert.Equal(DifferenceCategory.Displaced, DifferenceClassifier.Classify(Score(0.5, 0.4, true, true), Score(0.5, 0.4, true, true), Tol, Tie));
        Assert.Equal(DifferenceCategory.Unexplained, DifferenceClassifier.Classify(Score(0.5, 0.4, true, true), Score(0.6, 0.4, true, true), Tol, Tie));
    }

    [Fact]
    public void DisplacementIsExplainedOnlyWhenAnotherDifferenceCausesIt()
    {
        var hitsA = new[] { Hit("a", 0.9), Hit("d", 0.5) };
        var hitsB = new[] { Hit("c", 0.95), Hit("a", 0.9) };
        var same = Score(0.5, 0.4, true, true) with { ChunkId = Id("a") };
        var cause = (Score(0.5, 0, false, false) with { ChunkId = Id("c") }, Score(0.5, 0.9, true, true) with { ChunkId = Id("c") });

        var alone = DifferenceClassifier.Explain([Id("a")], Map(same), Map(same), hitsA, hitsB, Tol, Tie);
        Assert.Equal(DifferenceCategory.Unexplained, Assert.Single(alone).Category);

        var withCause = DifferenceClassifier.Explain(
            [Id("a"), Id("c")],
            Map(same, cause.Item1),
            Map(same, cause.Item2),
            hitsA, hitsB, Tol, Tie);
        Assert.Equal(new[] { DifferenceCategory.Displaced, DifferenceCategory.MatchedOneSide }, withCause.Select(d => d.Category));
        Assert.Equal((1, 2), (withCause[0].BaselineRank, withCause[0].CandidateRank));
    }

    private string WriteKnown(string json)
    {
        var path = Path.Combine(_dir, "known-differences.json");
        File.WriteAllText(path, json);
        return path;
    }

    private string Entry(string decision, params Guid[] candidateOnly) => $$"""
        [ { "queryId": "Q19", "scope": "P2", "kind": "text-match", "baselineOnly": [],
            "candidateOnly": [{{string.Join(",", candidateOnly.Select(g => $"\"{g}\""))}}],
            "category": "tokenization", "reason": "hyphens", "decision": "{{decision}}" } ]
        """;

    [Fact]
    public void ReviewMatchesTheExactSnapshot()
    {
        var diff = new TextDiff([], [Id("b"), Id("a")]);
        Assert.Equal(ReviewOutcome.Unreviewed, KnownDifferences.ReviewText([], "Q19", "P2", diff).Outcome);
        Assert.Equal(ReviewOutcome.Acceptable, KnownDifferences.ReviewText(KnownDifferences.Load(WriteKnown(Entry("acceptable", Id("a"), Id("b")))), "Q19", "P2", diff).Outcome);
        Assert.Equal(ReviewOutcome.Blocker, KnownDifferences.ReviewText(KnownDifferences.Load(WriteKnown(Entry("blocker", Id("a"), Id("b")))), "Q19", "P2", diff).Outcome);
        Assert.Equal(ReviewOutcome.SnapshotMismatch, KnownDifferences.ReviewText(KnownDifferences.Load(WriteKnown(Entry("acceptable", Id("a")))), "Q19", "P2", diff).Outcome);
    }

    [Fact]
    public void ErrorEntriesMatchSideAndCode()
    {
        var entries = KnownDifferences.Load(WriteKnown("""
            [ { "queryId": "Q36", "scope": "P1", "kind": "error", "errorSide": "baseline", "errorCode": "SQL7619",
                "category": "error-handling", "reason": "punctuation only", "decision": "acceptable" } ]
            """));
        Assert.Equal(ReviewOutcome.Acceptable, KnownDifferences.ReviewError(entries, "Q36", "P1", new ErrorOutcome(new EngineError("SQL7619", "x"), null))!.Outcome);
        Assert.Equal(ReviewOutcome.SnapshotMismatch, KnownDifferences.ReviewError(entries, "Q36", "P1", new ErrorOutcome(null, new EngineError("PG42601", "x")))!.Outcome);
        Assert.Null(KnownDifferences.ReviewError(entries, "Q36", "P1", new ErrorOutcome(new EngineError("a", "x"), new EngineError("b", "y"))));
    }

    [Fact]
    public void MissingFileMeansNoEntriesAndInvalidValuesAreRejected()
    {
        Assert.Empty(KnownDifferences.Load(Path.Combine(_dir, "absent.json")));
        var error = Assert.Throws<ParityConfigurationException>(() => KnownDifferences.Load(WriteKnown(Entry("maybe"))));
        Assert.Contains("decision 'maybe'", error.Message);
    }

    private static PairResult Pair(
        string query = "Q01",
        bool vectorPass = true,
        TextDiff? text = null,
        ReviewStatus? textReview = null,
        ErrorOutcome? error = null,
        ReviewStatus? errorReview = null,
        HybridPairMetrics? hybrid = null,
        RankDifference[]? differences = null) =>
        new(new QueryRef(query, "exact", "q"), "P1", vectorPass ? VectorParityResult.Passed : VectorParityResult.Failed("x"),
            text, textReview, null, error, errorReview, error is null ? hybrid ?? new HybridPairMetrics(1.0, true) : null, differences ?? [], [], []);

    [Fact]
    public void AllConditionsPassingIsSwitchable()
    {
        var verdict = Verdict.Evaluate([Pair(), Pair(hybrid: new(0.8, true))], new ThresholdSettings());
        Assert.True(verdict.Switchable);
        Assert.Equal(5, verdict.Conditions.Count);
        Assert.Equal("0.900", verdict.Conditions[2].Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EachFailingConditionBlocksTheVerdict(int failing)
    {
        var pair = failing switch
        {
            0 => Pair(vectorPass: false),
            1 => Pair(text: new TextDiff([Id("a")], []), textReview: new ReviewStatus(ReviewOutcome.Unreviewed, null)),
            2 => Pair(hybrid: new HybridPairMetrics(0.6, true)),
            3 => Pair(hybrid: new HybridPairMetrics(1.0, false)),
            _ => Pair(differences: [new RankDifference(Id("a"), 1, null, DifferenceCategory.Unexplained, "x")]),
        };
        var verdict = Verdict.Evaluate([pair], new ThresholdSettings());
        Assert.False(verdict.Switchable);
        Assert.False(verdict.Conditions[failing].Pass);
        Assert.Equal(4, verdict.Conditions.Count(c => c.Pass));
    }

    [Fact]
    public void ErrorPairsAreLeftOutOfTheMetricsButMustBeReviewed()
    {
        var error = new ErrorOutcome(new EngineError("SQL7619", "x"), null);
        var accepted = Verdict.Evaluate([Pair(), Pair(error: error, errorReview: new ReviewStatus(ReviewOutcome.Acceptable, null))], new ThresholdSettings());
        Assert.True(accepted.Switchable);
        Assert.Equal("1.000", accepted.Conditions[2].Value);

        var open = Verdict.Evaluate([Pair(), Pair(error: error, errorReview: new ReviewStatus(ReviewOutcome.SnapshotMismatch, null))], new ThresholdSettings());
        Assert.False(open.Conditions[1].Pass);
    }

    [Fact]
    public void StaleEntriesAreThoseWithNoMatchingDifference()
    {
        var entries = KnownDifferences.Load(WriteKnown(Entry("acceptable", Id("a"))));
        Assert.Single(KnownDifferences.FindStale(entries, [Pair(query: "Q19")]));
        Assert.Empty(KnownDifferences.FindStale(entries, [Pair(query: "Q19", text: new TextDiff([], [Id("a")])) with { Scope = "P2" }]));
    }
}
