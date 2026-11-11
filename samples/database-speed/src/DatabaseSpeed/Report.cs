using System.Globalization;
using System.Text;
using GuideAntsApi.DataModel.Database;

namespace DatabaseSpeed;

/// <summary>Every sample of one metric, per provider (milliseconds).</summary>
/// <param name="Operation">The operation the metric belongs to.</param>
/// <param name="Metric">The metric's name; an operation may time more than one thing.</param>
public sealed record MetricSamples(string Operation, string Metric, IReadOnlyDictionary<DatabaseProvider, IReadOnlyList<double>> Samples)
{
    public IReadOnlyList<double> For(DatabaseProvider provider) =>
        Samples.TryGetValue(provider, out var samples) ? samples : Array.Empty<double>();
}

public sealed record ProviderSummary(DatabaseProvider Provider, int Runs, double MedianMs, double P95Ms);

/// <param name="Ratio">Postgres median / SQL Server median, or null when either median is missing, NaN or the SQL Server one is not positive.</param>
/// <param name="Flagged">The Postgres median is more than <see cref="Report.FlagThreshold"/> times the SQL Server one.</param>
public sealed record MetricSummary(MetricSamples Source, ProviderSummary SqlServer, ProviderSummary Postgres, double? Ratio, bool Flagged);

/// <param name="Notes">Extra lines under the operation's table (throughput, counts, caveats).</param>
public sealed record OperationResult(string Name, string Description, IReadOnlyList<MetricSamples> Metrics, IReadOnlyList<string> Notes);

/// <param name="Lines">Markdown lines, rendered as they are.</param>
public sealed record ReportSection(string Title, IReadOnlyList<string> Lines);

/// <param name="Sections">Rendered before the summary (hardware, images, limits, data volumes, provenance).</param>
/// <param name="Trailer">Rendered after the operations (things to watch).</param>
public sealed record RunReport(
    bool Smoke,
    string Title,
    IReadOnlyList<ReportSection> Sections,
    IReadOnlyList<OperationResult> Operations,
    IReadOnlyList<ReportSection> Trailer);

/// <summary>
/// Turns the samples into RESULTS.md: per metric, the run count, median and nearest-rank p95 for each provider, the
/// Postgres-vs-SQL Server median ratio, and a flag when the Postgres median is more than 20% slower. There is no
/// pass/fail threshold; the flag only points the reader at a row.
/// </summary>
public static class Report
{
    public const double FlagThreshold = 1.20;
    public const string FlagText = "**FLAG: Postgres >20% slower**";
    public const string SmokeBanner = "SMOKE RUN — NOT VALID FOR COMPARISON (emulated SQL Server / tiny data)";

    private static readonly DatabaseProvider[] Providers = [DatabaseProvider.SqlServer, DatabaseProvider.Postgres];

    public static MetricSummary Summarize(MetricSamples metric)
    {
        var sqlServer = SummarizeProvider(DatabaseProvider.SqlServer, metric.For(DatabaseProvider.SqlServer));
        var postgres = SummarizeProvider(DatabaseProvider.Postgres, metric.For(DatabaseProvider.Postgres));
        var ratio = Comparison.Ratio(sqlServer.MedianMs, postgres.MedianMs);
        return new MetricSummary(metric, sqlServer, postgres, ratio, Comparison.IsFlagged(ratio));
    }

    public static string Render(RunReport report)
    {
        var md = new StringBuilder();
        if (report.Smoke)
        {
            md.Append("> **").Append(SmokeBanner).Append("**\n");
            md.Append("> This file proves every operation runs end to end on both providers. Its numbers mean nothing: ");
            md.Append("SQL Server ran under amd64 emulation and the data volumes are tiny.\n\n");
        }

        md.Append("# ").Append(report.Title).Append("\n\n");
        foreach (var section in report.Sections)
        {
            AppendSection(md, section);
        }

        var summaries = report.Operations
            .Select(op => (Operation: op, Metrics: op.Metrics.Select(Summarize).ToList()))
            .ToList();

        md.Append("## Summary\n\n");
        md.Append("Medians in ms. Ratio = Postgres median / SQL Server median (above 1 means Postgres is slower). ");
        md.Append("A flag marks a Postgres median more than 20% slower; there is no pass/fail threshold.\n\n");
        md.Append("| Operation | SQL Server median ms | Postgres median ms | Postgres / SQL Server | Flag |\n");
        md.Append("|---|---:|---:|---:|---|\n");
        foreach (var (_, metrics) in summaries)
        {
            foreach (var s in metrics)
            {
                md.Append("| ").Append(Escape(s.Source.Metric))
                  .Append(" | ").Append(Ms(s.SqlServer.MedianMs))
                  .Append(" | ").Append(Ms(s.Postgres.MedianMs))
                  .Append(" | ").Append(RatioText(s.Ratio))
                  .Append(" | ").Append(s.Flagged ? FlagText : string.Empty)
                  .Append(" |\n");
            }
        }

        md.Append('\n');
        md.Append("## Operations\n\n");
        foreach (var (operation, metrics) in summaries)
        {
            md.Append("### ").Append(operation.Name).Append("\n\n");
            md.Append(operation.Description).Append("\n\n");
            foreach (var s in metrics)
            {
                if (metrics.Count > 1 || s.Source.Metric != operation.Name)
                {
                    md.Append("**").Append(Escape(s.Source.Metric)).Append("**\n\n");
                }

                md.Append("| Provider | Runs | Median ms | p95 ms | Postgres / SQL Server (median) | Flag |\n");
                md.Append("|---|---:|---:|---:|---:|---|\n");
                foreach (var provider in Providers)
                {
                    var p = provider == DatabaseProvider.SqlServer ? s.SqlServer : s.Postgres;
                    md.Append("| ").Append(ProviderName(provider))
                      .Append(" | ").Append(p.Runs.ToString(CultureInfo.InvariantCulture))
                      .Append(" | ").Append(Ms(p.MedianMs))
                      .Append(" | ").Append(Ms(p.P95Ms))
                      .Append(" | ").Append(provider == DatabaseProvider.Postgres ? RatioText(s.Ratio) : string.Empty)
                      .Append(" | ").Append(provider == DatabaseProvider.Postgres && s.Flagged ? FlagText : string.Empty)
                      .Append(" |\n");
                }

                md.Append('\n');
            }

            foreach (var note in operation.Notes)
            {
                md.Append("- ").Append(note).Append('\n');
            }

            if (operation.Notes.Count > 0)
            {
                md.Append('\n');
            }
        }

        foreach (var section in report.Trailer)
        {
            AppendSection(md, section);
        }

        if (report.Smoke)
        {
            md.Append("> **").Append(SmokeBanner).Append("**\n");
        }

        return md.ToString();
    }

    public static string ProviderName(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => "SQL Server",
        DatabaseProvider.Postgres => "Postgres",
        _ => provider.ToString(),
    };

    public static string Ms(double value) =>
        double.IsNaN(value) ? "n/a" : value.ToString(value >= 1000 ? "F0" : "F2", CultureInfo.InvariantCulture);

    private static string RatioText(double? ratio) =>
        ratio is { } r ? r.ToString("F2", CultureInfo.InvariantCulture) : "n/a";

    private static ProviderSummary SummarizeProvider(DatabaseProvider provider, IReadOnlyList<double> samples) =>
        new(provider, samples.Count, Stats.Median(samples), samples.Count == 0 ? double.NaN : Stats.PercentileNearestRank(samples, 95));

    private static void AppendSection(StringBuilder md, ReportSection section)
    {
        md.Append("## ").Append(section.Title).Append("\n\n");
        foreach (var line in section.Lines)
        {
            md.Append(line).Append('\n');
        }

        md.Append('\n');
    }

    private static string Escape(string cell) => cell.Replace("|", "\\|");
}

/// <summary>The Postgres-vs-SQL Server comparison of two medians.</summary>
public static class Comparison
{
    /// <returns>Postgres / SQL Server, or null when the ratio means nothing (a missing or NaN median, or a SQL Server median that isn't positive).</returns>
    public static double? Ratio(double sqlServerMedian, double postgresMedian)
    {
        if (double.IsNaN(sqlServerMedian) || double.IsNaN(postgresMedian) || double.IsInfinity(sqlServerMedian)
            || double.IsInfinity(postgresMedian) || sqlServerMedian <= 0 || postgresMedian < 0)
        {
            return null;
        }

        return postgresMedian / sqlServerMedian;
    }

    /// <summary>
    /// More than <see cref="Report.FlagThreshold"/>. The tolerance keeps a ratio that is exactly 1.2 in decimal (10.0 vs
    /// 12.0) from being flagged through floating-point error.
    /// </summary>
    public static bool IsFlagged(double? ratio) => ratio is { } r && r > Report.FlagThreshold + 1e-9;
}
