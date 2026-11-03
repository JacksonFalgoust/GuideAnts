using System.Globalization;
using Parity.Core.Model;

namespace Parity.Core.Comparison;

public sealed record VectorParityResult(bool Pass, string? Failure)
{
    public static readonly VectorParityResult Passed = new(true, null);

    public static VectorParityResult Failed(string reason) => new(false, reason);
}

/// <summary>Spec section 7.1, rule 1: vector parity must pass.</summary>
public static class VectorParity
{
    public static VectorParityResult Compare(IReadOnlyList<VectorHit> baseline, IReadOnlyList<VectorHit> candidate, double scoreTolerance, double tieTolerance)
    {
        if (baseline.Count != candidate.Count)
        {
            return VectorParityResult.Failed($"result count differs: {baseline.Count} vs {candidate.Count}");
        }

        var groups = TieGroups.Build(baseline, h => h.Score, tieTolerance);
        var position = 0;
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            var slice = candidate.Skip(position).Take(group.Count).ToList();
            if (g < groups.Count - 1)
            {
                var expected = group.Select(h => h.ChunkId).ToHashSet();
                if (!slice.All(h => expected.Contains(h.ChunkId)))
                {
                    return VectorParityResult.Failed(
                        $"position {position + 1}{(group.Count > 1 ? $"-{position + group.Count}" : "")}: expected {Ids(group)}, got {Ids(slice)}");
                }
            }
            else
            {
                // The last group may be cut off by k differently on each engine; it only has to be tied.
                var anchor = group[0].Score;
                if (slice.Any(h => Math.Abs(h.Score - anchor) > tieTolerance + scoreTolerance))
                {
                    return VectorParityResult.Failed($"position {position + 1}: expected scores tied at {Format(anchor)}, got {Ids(slice)}");
                }
            }

            position += group.Count;
        }

        var candidateScores = candidate.ToDictionary(h => h.ChunkId, h => h.Score);
        foreach (var hit in baseline)
        {
            if (candidateScores.TryGetValue(hit.ChunkId, out var other) && Math.Abs(other - hit.Score) > scoreTolerance)
            {
                return VectorParityResult.Failed($"chunk {Short(hit.ChunkId)}: score {Format(hit.Score)} vs {Format(other)}");
            }
        }

        return VectorParityResult.Passed;
    }

    public static VectorParityResult CompareHybridVectorScores(IReadOnlyList<SearchHit> baseline, IReadOnlyList<SearchHit> candidate, double scoreTolerance)
    {
        var candidateScores = candidate.ToDictionary(h => h.ChunkId, h => h.VectorScore);
        foreach (var hit in baseline)
        {
            if (candidateScores.TryGetValue(hit.ChunkId, out var other) && Math.Abs(other - hit.VectorScore) > scoreTolerance)
            {
                return VectorParityResult.Failed($"hybrid hit {Short(hit.ChunkId)}: vector score {Format(hit.VectorScore)} vs {Format(other)}");
            }
        }

        return VectorParityResult.Passed;
    }

    private static string Ids<T>(IEnumerable<T> hits) where T : notnull =>
        "{" + string.Join(", ", hits.Select(h => h switch
        {
            VectorHit v => $"{Short(v.ChunkId)}={Format(v.Score)}",
            _ => h.ToString(),
        })) + "}";

    private static string Short(Guid id) => id.ToString()[..8];

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
