using Parity.Core.Comparison;
using Parity.Core.Model;
using static Parity.Tests.Support.Hits;

namespace Parity.Tests.Unit;

public class ComparisonPrimitivesTests
{
    private const double Tol = 1e-5;
    private const double Tie = 1e-6;

    [Fact]
    public void TieGroupsJoinScoresWithinToleranceOfTheGroupsFirstItem()
    {
        var groups = TieGroups.Build(new[] { 1.0, 1.0 - 5e-7, 0.9 }, s => s, Tie);
        Assert.Equal(new[] { 2, 1 }, groups.Select(g => g.Count));
    }

    [Fact]
    public void IdenticalVectorResultsPass() =>
        Assert.True(VectorParity.Compare([Vec("a", 0.9), Vec("b", 0.8)], [Vec("a", 0.9), Vec("b", 0.8)], Tol, Tie).Pass);

    [Fact]
    public void ReorderingTiedVectorHitsPasses() =>
        Assert.True(VectorParity.Compare([Vec("a", 0.9), Vec("b", 0.9), Vec("c", 0.5)], [Vec("b", 0.9), Vec("a", 0.9), Vec("c", 0.5)], Tol, Tie).Pass);

    [Fact]
    public void ReorderingUntiedVectorHitsFails()
    {
        var result = VectorParity.Compare([Vec("a", 0.9), Vec("b", 0.8), Vec("c", 0.5)], [Vec("b", 0.9), Vec("a", 0.8), Vec("c", 0.5)], Tol, Tie);
        Assert.False(result.Pass);
        Assert.Contains("position 1", result.Failure);
    }

    [Fact]
    public void ScoresOutsideToleranceFail()
    {
        var result = VectorParity.Compare([Vec("a", 0.9)], [Vec("a", 0.9 + 2e-5)], Tol, Tie);
        Assert.False(result.Pass);
        Assert.Contains(Id("a").ToString()[..8], result.Failure);
    }

    [Fact]
    public void ANonLastTieGroupHitWhoseScoreDriftsFailsWithThePerChunkScoreMessage()
    {
        // Same order and membership; only the first hit's score differs, by more than the tolerance.
        var result = VectorParity.Compare([Vec("a", 0.9), Vec("b", 0.5)], [Vec("a", 0.9 + 2e-5), Vec("b", 0.5)], Tol, Tie);
        Assert.False(result.Pass);
        Assert.StartsWith($"chunk {Id("a").ToString()[..8]}: score", result.Failure);
    }

    [Fact]
    public void ScoresWithinToleranceAcrossEnginesPass() =>
        Assert.True(VectorParity.Compare([Vec("a", 0.9)], [Vec("a", 0.9 + 4e-6)], Tol, Tie).Pass);

    [Fact]
    public void DifferentCountsFail() =>
        Assert.False(VectorParity.Compare([Vec("a", 0.9)], [], Tol, Tie).Pass);

    [Fact]
    public void ADifferentButTiedChunkAtTheCutOffPasses() =>
        Assert.True(VectorParity.Compare([Vec("a", 0.9), Vec("b", 0.5)], [Vec("a", 0.9), Vec("z", 0.5)], Tol, Tie).Pass);

    [Fact]
    public void ADifferentUntiedChunkAtTheCutOffFails() =>
        Assert.False(VectorParity.Compare([Vec("a", 0.9), Vec("b", 0.5)], [Vec("a", 0.9), Vec("z", 0.4)], Tol, Tie).Pass);

    [Fact]
    public void HybridVectorScoresMustAgreeForSharedChunks()
    {
        Assert.True(VectorParity.CompareHybridVectorScores([Hit("a", 0.9, vectorScore: 0.7)], [Hit("a", 0.8, vectorScore: 0.7 + 1e-6), Hit("b", 0.5)], Tol).Pass);
        Assert.False(VectorParity.CompareHybridVectorScores([Hit("a", 0.9, vectorScore: 0.7)], [Hit("a", 0.9, vectorScore: 0.71)], Tol).Pass);
    }

    [Fact]
    public void TextDiffListsEachSidesExtras()
    {
        var diff = TextParity.Diff([new TextMatch(Id("a"), 0.1), new TextMatch(Id("b"), 0.1)], [new TextMatch(Id("b"), 0.2), new TextMatch(Id("c"), 0.2)]);
        Assert.Equal(new[] { Id("a") }, diff.BaselineOnly);
        Assert.Equal(new[] { Id("c") }, diff.CandidateOnly);
        Assert.False(diff.IsEqual);
        Assert.True(TextParity.Diff([new TextMatch(Id("a"), 0.1)], [new TextMatch(Id("a"), 0.9)]).IsEqual);
    }

    private static SearchHit[] Five(string last, double lastScore) =>
        [Hit("a", 0.9), Hit("b", 0.8), Hit("c", 0.7), Hit("d", 0.6), Hit(last, lastScore)];

    [Fact]
    public void IdenticalHybridListsHaveFullOverlapAndNoDifferences()
    {
        var evaluation = RankingMetrics.EvaluatePair(Five("e", 0.5), Five("e", 0.5), Scores(), Scores(), Tie);
        Assert.Equal(new HybridPairMetrics(1.0, true), evaluation.Metrics);
        Assert.Empty(evaluation.DifferingChunks);
    }

    [Fact]
    public void ASwappedChunkLowersOverlapAndBothChunksDiffer()
    {
        var evaluation = RankingMetrics.EvaluatePair(Five("e", 0.5), Five("f", 0.55), Scores(("f", 0.45)), Scores(("e", 0.5)), Tie);
        Assert.Equal(0.8, evaluation.Metrics.OverlapAt5, 9);
        Assert.Equal(new[] { Id("e"), Id("f") }, evaluation.DifferingChunks);
    }

    [Fact]
    public void AChunkTiedWithTheOtherSidesFifthHitIsNotADifference()
    {
        var evaluation = RankingMetrics.EvaluatePair(Five("e", 0.5), Five("f", 0.5), Scores(("f", 0.5)), Scores(("e", 0.5)), Tie);
        Assert.Equal(1.0, evaluation.Metrics.OverlapAt5);
        Assert.Empty(evaluation.DifferingChunks);
    }

    [Fact]
    public void ReorderingWithinATieIsNotADifference()
    {
        var sides = Scores(("a", 0.9), ("b", 0.9));
        var evaluation = RankingMetrics.EvaluatePair([Hit("a", 0.9), Hit("b", 0.9)], [Hit("b", 0.9), Hit("a", 0.9)], sides, sides, Tie);
        Assert.True(evaluation.Metrics.SameTopOne);
        Assert.Empty(evaluation.DifferingChunks);
    }

    [Fact]
    public void ReorderingOutsideATieMakesBothChunksDiffer()
    {
        var evaluation = RankingMetrics.EvaluatePair([Hit("a", 0.9), Hit("b", 0.8)], [Hit("b", 0.9), Hit("a", 0.8)], Scores(("b", 0.8)), Scores(("a", 0.8)), Tie);
        Assert.False(evaluation.Metrics.SameTopOne);
        Assert.Equal(new[] { Id("a"), Id("b") }.Order(), evaluation.DifferingChunks.Order());
    }

    [Fact]
    public void EmptyAndShortListsUseTheLargerCountAsDenominator()
    {
        Assert.Equal(new HybridPairMetrics(1.0, true), RankingMetrics.EvaluatePair([], [], Scores(), Scores(), Tie).Metrics);
        Assert.Equal(new HybridPairMetrics(0.0, false), RankingMetrics.EvaluatePair([Hit("a", 0.9)], [], Scores(), Scores(), Tie).Metrics);
        Assert.Equal(0.5, RankingMetrics.EvaluatePair([Hit("a", 0.9), Hit("b", 0.8)], [Hit("a", 0.9)], Scores(), Scores(), Tie).Metrics.OverlapAt5);
    }

    [Fact]
    public void SameTopOneAcceptsATieOnTheBaselineSide()
    {
        // b scores just under a on the baseline side, but within the tie tolerance.
        var evaluation = RankingMetrics.EvaluatePair([Hit("a", 0.9), Hit("b", 0.9 - 5e-7)], [Hit("b", 0.9), Hit("a", 0.9)], Scores(("b", 0.9 - 5e-7)), Scores(("a", 0.9)), Tie);
        Assert.True(evaluation.Metrics.SameTopOne);
    }
}
