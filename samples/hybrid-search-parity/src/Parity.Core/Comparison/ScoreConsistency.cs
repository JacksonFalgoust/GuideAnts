using System.Globalization;
using Parity.Core.Model;

namespace Parity.Core.Comparison;

/// <summary>
/// Rules 2 and 3 are explained with the diagnostics' own queries (on Postgres, a copy of the searcher's SQL), while the
/// hits come from the store under test. Each top-5 hit carries the text and vector scores that code computed, so the
/// diagnostics must reproduce them; a hit they don't reproduce becomes an unexplained difference, which fails the
/// verdict. This keeps the explanations tied to the code under test if either one changes.
/// </summary>
public static class ScoreConsistency
{
    public const string Baseline = "baseline";
    public const string Candidate = "candidate";

    /// <param name="side"><see cref="Baseline"/> or <see cref="Candidate"/>: whose hits and diagnostics these are.</param>
    /// <param name="scoreTolerance">The vector tolerance; it bounds both scores.</param>
    /// <param name="tieTolerance">How close a raw rank must be to the last candidate's rank to count as tied with it.</param>
    public static IReadOnlyList<RankDifference> Check(
        string side,
        IReadOnlyList<SearchHit> hits,
        IReadOnlyDictionary<Guid, ChunkScore> scores,
        double scoreTolerance,
        double tieTolerance)
    {
        var mismatches = new List<RankDifference>();
        foreach (var (hit, index) in hits.Take(RankingMetrics.TopN).Select((h, i) => (h, i)))
        {
            var detail = !scores.TryGetValue(hit.ChunkId, out var score)
                ? $"{side} diagnostics disagree with the hit: no diagnostics score for this chunk"
                : Disagreement(side, hit, score, scoreTolerance, tieTolerance);
            if (detail is not null)
            {
                var rank = index + 1;
                mismatches.Add(new RankDifference(hit.ChunkId, side == Baseline ? rank : null, side == Candidate ? rank : null, DifferenceCategory.Unexplained, detail));
            }
        }

        return mismatches;
    }

    private static string? Disagreement(string side, SearchHit hit, ChunkScore score, double tolerance, double tie)
    {
        var vectorAgrees = Math.Abs(hit.VectorScore - score.VectorScore) <= tolerance;
        var textAgrees = Math.Abs(hit.TextScore - score.TextScore) <= tolerance || TiedAtTheCandidateCut(hit, score, tolerance, tie);
        return vectorAgrees && textAgrees
            ? null
            : string.Create(CultureInfo.InvariantCulture,
                $"{side} diagnostics disagree with the hit: text {hit.TextScore:F6} vs {score.TextScore:F6}, vector {hit.VectorScore:F6} vs {score.VectorScore:F6} (hit vs diagnostics)");
    }

    /// <summary>
    /// Chunks whose rank ties the last full-text candidate's are kept or cut arbitrarily (a LIMIT with no tie-breaker,
    /// in production and in the diagnostics). For such a chunk the hit's text score may be its raw rank or 0, and
    /// nothing else.
    /// </summary>
    private static bool TiedAtTheCandidateCut(SearchHit hit, ChunkScore score, double tolerance, double tie) =>
        score is { Matched: true, RawRank: double raw, NthCandidateRank: double nth }
        && Math.Abs(raw - nth) <= tie
        && (Math.Abs(hit.TextScore - raw) <= tolerance || Math.Abs(hit.TextScore) <= tolerance);
}
