using System.Globalization;
using System.Text;
using Parity.Core.Comparison;
using Parity.Core.Model;

namespace Parity.Core.Reporting;

/// <summary>Spec section 7.5: the RESULTS.md layout.</summary>
public static class MarkdownReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Render(ComparisonReport report)
    {
        var sb = new StringBuilder();
        var c = report.Context;

        sb.AppendLine("# Hybrid search parity results").AppendLine();
        sb.AppendLine(string.Create(Inv,
            $"**Verdict: {(report.Verdict.Switchable ? "SWITCHABLE" : "NOT SWITCHABLE")}**. `{c.BaselineProfile}` (baseline) vs `{c.CandidateProfile}` (candidate), {report.Pairs.Count} query/scope pairs, run {c.RunAtUtc:yyyy-MM-dd HH:mm} UTC."));
        sb.AppendLine();
        if (c.SampleDirty)
        {
            sb.AppendLine("> **Warning:** the sample directory has uncommitted changes (other than RESULTS.md), so this run may not be reproducible from the commit below.").AppendLine();
        }

        if (c.SettingsDifferences.Count > 0)
        {
            sb.AppendLine("> **Warning:** the effective settings differ from the committed `appsettings.json`, so the thresholds and tolerances below may not be the pre-registered ones:");
            foreach (var difference in c.SettingsDifferences)
            {
                sb.AppendLine($"> - {Md(difference)}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("| Condition | Result | Value |").AppendLine("|---|---|---|");
        foreach (var condition in report.Verdict.Conditions)
        {
            sb.AppendLine($"| {Cell(condition.Name)} | {(condition.Pass ? "PASS" : "FAIL")} | {Cell(condition.Value)} |");
        }

        sb.AppendLine();
        WriteEnvironment(sb, c);
        RuleOne(sb, report);
        RuleTwo(sb, report);
        RuleThree(sb, report);
        Informational(sb, report);
        Errors(sb, report);
        Stale(sb, report);
        Limits(sb, report);
        return sb.ToString();
    }

    private static void WriteEnvironment(StringBuilder sb, RunContext c)
    {
        var m = c.Manifest;
        sb.AppendLine("## Environment").AppendLine();
        sb.AppendLine($"- Sample commit: `{c.SampleCommit}`{(c.SampleDirty ? " (sample directory has uncommitted changes)" : "")}");
        sb.AppendLine($"- known-differences.json: SHA-256 `{c.KnownDifferencesSha256}`");
        sb.AppendLine("- Data files (SHA-256 from data/manifest.json):");
        foreach (var (file, hash) in m.Files.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"  - {Code(file)}: `{hash}`");
        }

        sb.AppendLine(string.Create(Inv, $"- Data: corpus `{m.CorpusCommit[..Math.Min(8, m.CorpusCommit.Length)]}`, model `{m.Model}`, {m.ChunkCount} chunks, {m.QueryCount} queries, generated {m.GeneratedUtc:yyyy-MM-dd}"));
        sb.AppendLine(string.Create(Inv,
            $"- Thresholds: mean top-5 overlap ≥ {c.Thresholds.MinMeanTop5Overlap}, same #1 ≥ {c.Thresholds.MinSameTopOneRate * 100:0}%; vector tolerance {c.VectorTolerance:0.#####E0}, tie tolerance {c.TieTolerance:0.#####E0}"));
        foreach (var (role, info) in new[] { ("Baseline", c.BaselineInfo), ("Candidate", c.CandidateInfo) })
        {
            sb.AppendLine($"- {role} ({info.Provider}): {info.Version}");
            foreach (var (key, value) in info.Details.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"  - {key}: `{value}`");
            }
        }

        sb.AppendLine($"- Images: SQL Server `{c.SqlServerImage}` → {c.SqlServerImageDigest}; Postgres `{c.PostgresImage}` → {c.PostgresImageDigest}");
        sb.AppendLine();
    }

    private static void RuleOne(StringBuilder sb, ComparisonReport report)
    {
        var failures = report.Pairs.Where(p => !p.Vector.Pass).ToList();
        sb.AppendLine("## Rule 1: vector parity").AppendLine();
        sb.AppendLine($"{report.Pairs.Count - failures.Count}/{report.Pairs.Count} pairs pass.");
        foreach (var p in failures)
        {
            sb.AppendLine($"- `{p.Query.Id}` · {p.Scope}: {Md(p.Vector.Failure)}");
        }

        sb.AppendLine();
    }

    private static void RuleTwo(StringBuilder sb, ComparisonReport report)
    {
        var differing = report.Pairs.Where(p => p.Text is { IsEqual: false } || (p.TextReview?.IsOpen ?? false)).ToList();
        sb.AppendLine("## Rule 2: full-text match parity").AppendLine();
        sb.AppendLine($"{differing.Count} pairs differ; {differing.Count(p => p.TextReview?.IsOpen ?? true)} are open. Stop words are shown ~~struck through~~.");
        sb.AppendLine();
        foreach (var p in differing)
        {
            var review = p.TextReview;
            sb.AppendLine($"### `{p.Query.Id}` · {p.Scope}: \"{Md(p.Query.Text)}\"").AppendLine();
            sb.AppendLine($"Status: **{ComparisonReport.Status(review)}**{(review?.Entry is { } e ? $" ({Md(e.Category)}: {Md(e.Reason)}){ReviewedBy(e)}" : "")}").AppendLine();
            if (p.Text is null)
            {
                sb.AppendLine("- (no diff recorded)");
            }
            else
            {
                sb.AppendLine($"- Baseline only: {Labels(report, p.Text.BaselineOnly)}");
                sb.AppendLine($"- Candidate only: {Labels(report, p.Text.CandidateOnly)}");
            }
            if (p.Evidence is { } evidence)
            {
                sb.AppendLine($"- Query tokens: baseline {Tokens(evidence.BaselineQueryTokens)}; candidate {Tokens(evidence.CandidateQueryTokens)}");
                foreach (var s in evidence.Snippets)
                {
                    sb.AppendLine($"- {s.Side} `{report.Label(s.ChunkId)}`: \"{Md(s.Snippet)}\"");
                    sb.AppendLine($"  - baseline tokens {Tokens(s.BaselineTokens)}; candidate tokens {Tokens(s.CandidateTokens)}");
                }
            }

            sb.AppendLine();
        }
    }

    private static void RuleThree(StringBuilder sb, ComparisonReport report)
    {
        var counted = report.Pairs.Where(p => p.Hybrid is not null).ToList();
        var differences = report.Pairs.SelectMany(p => p.Differences).ToList();
        sb.AppendLine("## Rule 3: hybrid ranking").AppendLine();
        if (counted.Count > 0)
        {
            sb.AppendLine(string.Create(Inv,
                $"Mean top-5 overlap {counted.Average(p => p.Hybrid!.OverlapAt5):F3} over {counted.Count} pairs; same #1 in {counted.Count(p => p.Hybrid!.SameTopOne) * 100.0 / counted.Count:0.0}%."));
        }

        sb.AppendLine($"{differences.Count} differences: {string.Join(", ", differences.GroupBy(d => d.Category).OrderBy(g => g.Key).Select(g => $"{g.Count()} {ComparisonReport.Kebab(g.Key)}"))}.");
        sb.AppendLine();
        sb.AppendLine("| Query | Scope | Overlap@5 | Same #1 | Differences |").AppendLine("|---|---|---|---|---|");
        foreach (var p in counted)
        {
            sb.AppendLine(string.Create(Inv, $"| `{p.Query.Id}` | {p.Scope} | {p.Hybrid!.OverlapAt5:F2} | {(p.Hybrid.SameTopOne ? "yes" : "no")} | {p.Differences.Count} |"));
        }

        sb.AppendLine();
        foreach (var p in report.Pairs.Where(p => p.Differences.Count > 0))
        {
            foreach (var d in p.Differences)
            {
                sb.AppendLine($"- `{p.Query.Id}` · {p.Scope}: `{report.Label(d.ChunkId)}` rank {d.BaselineRank?.ToString(Inv) ?? "–"} → {d.CandidateRank?.ToString(Inv) ?? "–"}: **{ComparisonReport.Kebab(d.Category)}** ({Md(d.Detail)})");
            }
        }

        sb.AppendLine();
    }

    private static void Informational(StringBuilder sb, ComparisonReport report)
    {
        sb.AppendLine("## Informational (not part of the verdict)").AppendLine();
        sb.AppendLine("Extra and lost matches are distinct chunks across scopes P1 to P3 (P1/N1 is a subset of P1). The quirk-fixed run shows none: its text match has no top-N, so they would equal Rule 2's totals.").AppendLine();
        sb.AppendLine("| Run | Pairs | Mean top-5 overlap | Same #1 | Extra matches | Lost matches |").AppendLine("|---|---|---|---|---|---|");
        foreach (var s in report.Profiles.Prepend(report.QuirkFixed))
        {
            sb.AppendLine(s.Available
                ? string.Create(Inv, $"| {Cell(s.Label)} | {s.PairsCounted} | {s.MeanOverlap:F3} | {s.SameTopOneRate * 100:0.0}% | {(s.ExtraMatches?.ToString(Inv) ?? "–")} | {(s.LostMatches?.ToString(Inv) ?? "–")} |")
                : $"| {Cell(s.Label)} | not available: {Cell(s.UnavailableReason ?? "")} | | | | |");
        }

        sb.AppendLine();
    }

    private static void Errors(StringBuilder sb, ComparisonReport report)
    {
        var errors = report.Pairs.Where(p => p.Error is not null || (p.ErrorReview?.IsOpen ?? false)).ToList();
        if (errors.Count == 0)
        {
            return;
        }

        sb.AppendLine("## Errors").AppendLine();
        sb.AppendLine("| Query | Scope | Side | Baseline | Candidate | Review |").AppendLine("|---|---|---|---|---|---|");
        foreach (var p in errors)
        {
            if (p.Error is not { } e)
            {
                sb.AppendLine($"| `{p.Query.Id}` | {p.Scope} | (no error recorded) | | | {ComparisonReport.Status(p.ErrorReview)} |");
                continue;
            }

            sb.AppendLine($"| `{p.Query.Id}` | {p.Scope} | {e.Side} | {Cell(Describe(e.Baseline))} | {Cell(Describe(e.Candidate))} | {(e.NeedsReview ? ComparisonReport.Status(p.ErrorReview) + (p.ErrorReview?.Entry is { } entry ? ReviewedBy(entry) : "") : "no review needed (both hybrid calls failed: empty results on both engines)")} |");
        }

        sb.AppendLine();
    }

    private static void Stale(StringBuilder sb, ComparisonReport report)
    {
        if (report.StaleEntries.Count == 0)
        {
            return;
        }

        sb.AppendLine("## Stale known-differences entries").AppendLine();
        sb.AppendLine("These entries no longer match any observed difference. They don't affect the verdict; remove them when convenient.");
        foreach (var e in report.StaleEntries)
        {
            sb.AppendLine($"- {Code(e.QueryId)} · {Md(e.Scope)} ({Md(e.Kind)}, {Md(e.Decision)}): {Md(e.Reason)}");
        }

        sb.AppendLine();
    }

    private static string ReviewedBy(KnownDifference entry) =>
        string.IsNullOrWhiteSpace(entry.ReviewedBy) ? " [reviewer not recorded]" : $" [reviewed by {Md(entry.ReviewedBy)}{(string.IsNullOrWhiteSpace(entry.ReviewedOn) ? "" : $" on {Md(entry.ReviewedOn)}")}]";

    private static void Limits(StringBuilder sb, ComparisonReport report)
    {
        var m = report.Context.Manifest;
        var reviewed = report.Pairs.Count(p => p.TextReview?.Entry is not null) + report.Pairs.Count(p => p.ErrorReview?.Entry is not null);
        var ties = report.Pairs.SelectMany(p => p.Differences).Count(d => d.Category == DifferenceCategory.TopNBoundaryTie);
        sb.AppendLine("## Limits").AppendLine();
        sb.AppendLine($"- **Review.** {reviewed} difference reviews in this run came from `known-differences.json`. In the committed file the user delegated them to Claude (\"do whatever you think is best\") and none was reviewed line by line by a human; each entry's `reviewedBy` says so. Edit the file and re-run `compare` to change a decision.");
        sb.AppendLine("- **Real recall misses were accepted.** Two accepted Postgres differences lose true matches: Q09 (one real prose occurrence, \"GuideAnts guide/internal tools\", is missed because Postgres lexes `guide/internal` as one token) and Q25 (a URL-prefix search finds nothing, because Postgres keeps the longer URL as one token).");
        sb.AppendLine($"- **Tie-break explanations.** {ties} hybrid differences are explained as `top-n-boundary-tie`. They come from diagnostics whose tie-breaking among equal text ranks is unspecified, the same nondeterminism as production's own query.");
        sb.AppendLine("- **Stop lists differ.** SQL Server's system stoplist and Postgres's `english` list are not the same (for example single letters, digits, `not` and `use`). The token evidence above shows it where it matters.");
        sb.AppendLine(string.Create(Inv, $"- **One corpus.** The evidence is a single corpus (`docs/*.md` at `{m.CorpusCommit[..Math.Min(8, m.CorpusCommit.Length)]}`) and {m.QueryCount} saved queries."));
        sb.AppendLine();
    }

    private static string Describe(EngineError? error) => error is null ? "ok" : $"{error.Code}: {error.Message}";

    private static string Labels(ComparisonReport report, IReadOnlyList<Guid> ids) =>
        ids.Count == 0 ? "none" : string.Join(", ", ids.Select(id => $"`{report.Label(id)}`"));

    private static string Tokens(IReadOnlyList<Token> tokens) =>
        tokens.Count == 0 ? "(none)" : string.Join(" | ", tokens.Select(t => t.IsStopWord ? $"~~{Code(t.Text)}~~" : Code(t.Normalized ?? t.Text)));

    private static string Cell(string value) => Md(value);

    /// <summary>Sanitises untrusted free text: one line, bounded length, markdown/HTML characters escaped.</summary>
    internal static string Md(string? value)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(value ?? "", @"\s+", " ").Trim();
        if (text.Length > 200)
        {
            text = text[..200].TrimEnd() + "…";
        }

        text = System.Text.RegularExpressions.Regex.Replace(text, @"[\\`*_~|<>#\[\]!]", m => "\\" + m.Value);
        return System.Text.RegularExpressions.Regex.Replace(text, @"^(\d+)([.)])|^([-+])", m => m.Value.Replace(".", "\\.").Replace(")", "\\)").Replace("-", "\\-").Replace("+", "\\+"));
    }

    /// <summary>A code span whose fence is longer than any backtick run inside, padded so edge backticks are safe.</summary>
    internal static string Code(string? value)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(value ?? "", @"\s+", " ");
        var longest = System.Text.RegularExpressions.Regex.Matches(text, "`+").Select(m => m.Length).DefaultIfEmpty(0).Max();
        var fence = new string('`', longest + 1);
        return longest == 0 ? $"`{text}`" : $"{fence} {text} {fence}";
    }
}
