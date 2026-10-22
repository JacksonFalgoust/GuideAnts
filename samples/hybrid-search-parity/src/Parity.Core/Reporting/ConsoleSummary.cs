using System.Globalization;
using System.Text;
using Parity.Core.Comparison;

namespace Parity.Core.Reporting;

public static class ConsoleSummary
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Render(ComparisonReport report)
    {
        var sb = new StringBuilder();
        var c = report.Context;
        sb.AppendLine($"Hybrid search parity: {(report.Verdict.Switchable ? "SWITCHABLE" : "NOT SWITCHABLE")} ({c.BaselineProfile} vs {c.CandidateProfile}, {report.Pairs.Count} pairs)");
        foreach (var condition in report.Verdict.Conditions)
        {
            sb.AppendLine($"  {(condition.Pass ? "PASS" : "FAIL")}  {condition.Name,-55} {condition.Value}");
        }

        sb.AppendLine("Informational:");
        foreach (var s in report.Profiles.Prepend(report.QuirkFixed))
        {
            sb.AppendLine(s.Available
                ? string.Create(Inv, $"  {s.Label}: overlap {s.MeanOverlap:F3}, same #1 {s.SameTopOneRate * 100:0.0}%, +{s.ExtraMatches}/-{s.LostMatches} matches")
                : $"  {s.Label}: not available ({s.UnavailableReason})");
        }

        var open = new List<string>();
        foreach (var p in report.Pairs)
        {
            if ((p.Text is { IsEqual: false } && (p.TextReview?.IsOpen ?? true)) || (p.TextReview?.IsOpen ?? false))
            {
                open.Add($"  {p.Query.Id} · {p.Scope}  full-text  {ComparisonReport.Status(p.TextReview)}  (baseline-only {p.Text?.BaselineOnly.Count ?? 0}, candidate-only {p.Text?.CandidateOnly.Count ?? 0})");
            }

            if ((p.Error is { IsOneSided: true } && (p.ErrorReview?.IsOpen ?? true)) || (p.ErrorReview?.IsOpen ?? false))
            {
                open.Add($"  {p.Query.Id} · {p.Scope}  error  {ComparisonReport.Status(p.ErrorReview)}  ({p.Error?.Side} {p.Error?.Code})");
            }

            if (!p.Vector.Pass)
            {
                open.Add($"  {p.Query.Id} · {p.Scope}  vector  FAIL  {p.Vector.Failure}");
            }

            foreach (var d in p.Differences.Where(d => d.Category == DifferenceCategory.Unexplained))
            {
                open.Add($"  {p.Query.Id} · {p.Scope}  hybrid  unexplained  {report.Label(d.ChunkId)} ({d.Detail})");
            }
        }

        if (open.Count > 0)
        {
            sb.AppendLine("Open items:");
            open.ForEach(line => sb.AppendLine(line));
        }

        return sb.ToString();
    }
}
