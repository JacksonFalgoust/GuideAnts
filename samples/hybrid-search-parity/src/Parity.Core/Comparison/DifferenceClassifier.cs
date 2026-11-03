using System.Globalization;
using Parity.Core.Model;

namespace Parity.Core.Comparison;

public enum DifferenceCategory { ScoredDifferently, MatchedOneSide, TopNCut, TopNBoundaryTie, Displaced, Unexplained }

public sealed record RankDifference(Guid ChunkId, int? BaselineRank, int? CandidateRank, DifferenceCategory Category, string Detail);

/// <summary>Spec section 7.3: why a chunk's hybrid placement differs between the engines.</summary>
public static class DifferenceClassifier
{
    public static DifferenceCategory Classify(ChunkScore baseline, ChunkScore candidate, double vectorTolerance, double tieTolerance)
    {
        if (Math.Abs(baseline.VectorScore - candidate.VectorScore) > vectorTolerance)
        {
            return DifferenceCategory.Unexplained; // rule 1 should have caught this
        }

        if (Math.Abs(baseline.TextScore - candidate.TextScore) <= tieTolerance)
        {
            return DifferenceCategory.Displaced; // its own scores agree; something else moved it
        }

        if (baseline.Matched != candidate.Matched)
        {
            return DifferenceCategory.MatchedOneSide;
        }

        if (baseline.InCandidates != candidate.InCandidates)
        {
            var cutSide = baseline.InCandidates ? candidate : baseline;
            return cutSide.RawRank is double raw && cutSide.NthCandidateRank is double nth && Math.Abs(raw - nth) <= tieTolerance
                ? DifferenceCategory.TopNBoundaryTie
                : DifferenceCategory.TopNCut;
        }

        return baseline.InCandidates && candidate.InCandidates
            ? DifferenceCategory.ScoredDifferently
            : DifferenceCategory.Unexplained;
    }

    public static IReadOnlyList<RankDifference> Explain(
        IReadOnlyList<Guid> differing,
        IReadOnlyDictionary<Guid, ChunkScore> baselineScores,
        IReadOnlyDictionary<Guid, ChunkScore> candidateScores,
        IReadOnlyList<SearchHit> baselineHits,
        IReadOnlyList<SearchHit> candidateHits,
        double vectorTolerance,
        double tieTolerance)
    {
        var classified = differing.Select(id =>
        {
            var hasBoth = baselineScores.TryGetValue(id, out var b) & candidateScores.TryGetValue(id, out var c);
            var category = hasBoth ? Classify(b!, c!, vectorTolerance, tieTolerance) : DifferenceCategory.Unexplained;
            var detail = hasBoth ? Describe(b!, c!) : "no score from one engine";
            return (Id: id, Category: category, Detail: detail);
        }).ToList();

        var hasCause = classified.Any(x => x.Category is not (DifferenceCategory.Displaced or DifferenceCategory.Unexplained));
        return classified
            .Select(x => new RankDifference(
                x.Id,
                Rank(baselineHits, x.Id),
                Rank(candidateHits, x.Id),
                x.Category == DifferenceCategory.Displaced && !hasCause ? DifferenceCategory.Unexplained : x.Category,
                x.Detail))
            .ToList();
    }

    private static int? Rank(IReadOnlyList<SearchHit> hits, Guid id)
    {
        for (var i = 0; i < Math.Min(hits.Count, RankingMetrics.TopN); i++)
        {
            if (hits[i].ChunkId == id)
            {
                return i + 1;
            }
        }

        return null;
    }

    private static string Describe(ChunkScore b, ChunkScore c) => string.Create(
        CultureInfo.InvariantCulture,
        $"text {b.TextScore:F4} vs {c.TextScore:F4}; matched {b.Matched}/{c.Matched}; candidate {b.InCandidates}/{c.InCandidates}");
}
