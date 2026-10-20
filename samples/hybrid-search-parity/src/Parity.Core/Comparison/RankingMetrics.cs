using Parity.Core.Model;

namespace Parity.Core.Comparison;

public sealed record HybridPairMetrics(double OverlapAt5, bool SameTopOne);

public sealed record PairEvaluation(HybridPairMetrics Metrics, IReadOnlyList<Guid> DifferingChunks);

/// <summary>Spec section 7.3, rule 3: overlap@5, same #1, and which chunks differ.</summary>
public static class RankingMetrics
{
    public const int TopN = 5;

    public static double HybridScore(ChunkScore score, double vectorWeight, double textWeight) =>
        (vectorWeight * score.VectorScore) + (textWeight * score.TextScore);

    /// <param name="baselineSideScores">Hybrid score on the baseline engine for every chunk in either top 5.</param>
    /// <param name="candidateSideScores">Hybrid score on the candidate engine for every chunk in either top 5.</param>
    public static PairEvaluation EvaluatePair(
        IReadOnlyList<SearchHit> baseline,
        IReadOnlyList<SearchHit> candidate,
        IReadOnlyDictionary<Guid, double> baselineSideScores,
        IReadOnlyDictionary<Guid, double> candidateSideScores,
        double tieTolerance)
    {
        var a = baseline.Take(TopN).ToList();
        var b = candidate.Take(TopN).ToList();
        var idsA = a.Select(h => h.ChunkId).ToHashSet();
        var idsB = b.Select(h => h.ChunkId).ToHashSet();

        bool TiesWithFifth(IReadOnlyList<SearchHit> list, IReadOnlyDictionary<Guid, double> sideScores, Guid id) =>
            list.Count == TopN && sideScores.TryGetValue(id, out var s) && Math.Abs(s - list[TopN - 1].Score) <= tieTolerance;

        var denominator = Math.Max(a.Count, b.Count);
        var overlap = denominator == 0
            ? 1.0
            : a.Count(h => idsB.Contains(h.ChunkId) || TiesWithFifth(b, candidateSideScores, h.ChunkId)) / (double)denominator;

        var sameTop = (a.Count, b.Count) switch
        {
            (0, 0) => true,
            (0, _) or (_, 0) => false,
            _ => a[0].ChunkId == b[0].ChunkId
                 || (baselineSideScores.TryGetValue(b[0].ChunkId, out var s) && Math.Abs(s - a[0].Score) <= tieTolerance),
        };

        var differing = new List<Guid>();
        differing.AddRange(a.Where(h => !idsB.Contains(h.ChunkId) && !TiesWithFifth(b, candidateSideScores, h.ChunkId)).Select(h => h.ChunkId));
        differing.AddRange(b.Where(h => !idsA.Contains(h.ChunkId) && !TiesWithFifth(a, baselineSideScores, h.ChunkId)).Select(h => h.ChunkId));
        foreach (var hit in a.Where(h => idsB.Contains(h.ChunkId)))
        {
            var other = b.First(x => x.ChunkId == hit.ChunkId);
            if (StrictlyAbove(a, hit.Score, tieTolerance) != StrictlyAbove(b, other.Score, tieTolerance))
            {
                differing.Add(hit.ChunkId);
            }
        }

        return new PairEvaluation(new HybridPairMetrics(overlap, sameTop), differing);
    }

    // Rank as "how many hits score strictly higher", which ignores reordering inside a tie.
    private static int StrictlyAbove(IReadOnlyList<SearchHit> list, double score, double tolerance) =>
        list.Count(x => x.Score > score + tolerance);
}
