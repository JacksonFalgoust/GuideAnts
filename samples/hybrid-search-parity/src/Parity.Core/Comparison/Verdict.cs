using System.Globalization;
using Parity.Core.Settings;

namespace Parity.Core.Comparison;

public sealed record VerdictCondition(string Name, bool Pass, string Value);

public sealed record VerdictResult(bool Switchable, IReadOnlyList<VerdictCondition> Conditions);

/// <summary>Spec section 2: switchable only when all five conditions hold.</summary>
public static class Verdict
{
    public static VerdictResult Evaluate(IReadOnlyList<PairResult> pairs, ThresholdSettings thresholds)
    {
        var inv = CultureInfo.InvariantCulture;
        var vectorPasses = pairs.Count(p => p.Vector.Pass);
        var open = pairs.Count(p => (p.TextReview?.IsOpen ?? false) || (p.ErrorReview?.IsOpen ?? false));
        var counted = pairs.Where(p => p.Hybrid is not null).Select(p => p.Hybrid!).ToList();
        var meanOverlap = counted.Count == 0 ? 0.0 : counted.Average(m => m.OverlapAt5);
        var sameTopRate = counted.Count == 0 ? 0.0 : counted.Count(m => m.SameTopOne) / (double)counted.Count;
        var unexplained = pairs.Sum(p => p.Differences.Count(d => d.Category == DifferenceCategory.Unexplained));

        var conditions = new List<VerdictCondition>
        {
            new("Vector parity", vectorPasses == pairs.Count, $"{vectorPasses}/{pairs.Count} pairs"),
            new("Full-text and error differences reviewed and accepted", open == 0, $"{open} open"),
            new($"Mean top-5 overlap ≥ {thresholds.MinMeanTop5Overlap.ToString(inv)}", meanOverlap >= thresholds.MinMeanTop5Overlap, meanOverlap.ToString("F3", inv)),
            new($"Same #1 ≥ {(thresholds.MinSameTopOneRate * 100).ToString("0", inv)}%", sameTopRate >= thresholds.MinSameTopOneRate, $"{(sameTopRate * 100).ToString("0.0", inv)}%"),
            new("Zero unexplained differences", unexplained == 0, unexplained.ToString(inv)),
        };
        return new VerdictResult(conditions.All(c => c.Pass), conditions);
    }
}
