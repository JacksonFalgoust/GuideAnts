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
        // Fail closed: a difference with no review attached counts as unreviewed.
        var outcomes = pairs.SelectMany(OpenOutcomes).ToList();
        var unreviewed = outcomes.Count(o => o == ReviewOutcome.Unreviewed);
        var snapshotChanged = outcomes.Count(o => o == ReviewOutcome.SnapshotMismatch);
        var blockers = outcomes.Count(o => o == ReviewOutcome.Blocker);
        var counted = pairs.Where(p => p.Hybrid is not null).Select(p => p.Hybrid!).ToList();
        var meanOverlap = counted.Count == 0 ? 0.0 : counted.Average(m => m.OverlapAt5);
        var sameTopRate = counted.Count == 0 ? 0.0 : counted.Count(m => m.SameTopOne) / (double)counted.Count;
        var unexplained = pairs.Sum(p => p.Differences.Count(d => d.Category == DifferenceCategory.Unexplained));

        var conditions = new List<VerdictCondition>
        {
            new("Vector parity", pairs.Count > 0 && vectorPasses == pairs.Count, $"{vectorPasses}/{pairs.Count} pairs"),
            new("Full-text and error differences reviewed and accepted", outcomes.Count == 0, $"{unreviewed} unreviewed, {snapshotChanged} snapshot-changed, {blockers} blocker"),
            new($"Mean top-5 overlap ≥ {thresholds.MinMeanTop5Overlap.ToString(inv)}", counted.Count > 0 && meanOverlap >= thresholds.MinMeanTop5Overlap, counted.Count == 0 ? "no counted pairs" : meanOverlap.ToString("F3", inv)),
            new($"Same #1 ≥ {(thresholds.MinSameTopOneRate * 100).ToString("0", inv)}%", counted.Count > 0 && sameTopRate >= thresholds.MinSameTopOneRate, counted.Count == 0 ? "no counted pairs" : $"{(sameTopRate * 100).ToString("0.0", inv)}%"),
            new("Zero unexplained differences", unexplained == 0, unexplained.ToString(inv)),
        };
        return new VerdictResult(conditions.All(c => c.Pass), conditions);
    }

    /// <summary>The outcome of each review item (text difference, error) that is not an accepted, matching entry.</summary>
    private static IEnumerable<ReviewOutcome> OpenOutcomes(PairResult p)
    {
        if (p.TextReview?.IsOpen ?? p.Text is { IsEqual: false })
        {
            yield return p.TextReview?.Outcome ?? ReviewOutcome.Unreviewed;
        }

        if (p.ErrorReview?.IsOpen ?? p.Error is { NeedsReview: true })
        {
            yield return p.ErrorReview?.Outcome ?? ReviewOutcome.Unreviewed;
        }
    }
}
