
using FluentAssertions;
using GuideAntsApi.DataModel.Database;

namespace DatabaseSpeed.Tests;

[TestClass]
public class ReportTests
{
    [TestMethod]
    public void Report_flags_only_medians_over_20_percent_slower()
    {
        var atThreshold = Report.Summarize(Metric("at-threshold", Constant(200, 10.0), Constant(200, 12.0)));
        var overThreshold = Report.Summarize(Metric("over-threshold", Constant(200, 10.0), Constant(200, 12.1)));

        atThreshold.SqlServer.MedianMs.Should().Be(10.0);
        atThreshold.Postgres.MedianMs.Should().Be(12.0);
        atThreshold.Ratio.Should().BeApproximately(1.20, 1e-9);
        atThreshold.Flagged.Should().BeFalse("12.0 is exactly 20% slower, not more");

        overThreshold.Ratio.Should().BeApproximately(1.21, 1e-9);
        overThreshold.Flagged.Should().BeTrue();

        // p95 by nearest rank on 200 samples: rank ceil(0.95 * 200) = 190, so the 190th smallest value.
        var shuffled = Enumerable.Range(1, 200).Select(i => (double)i).OrderBy(_ => Random.Shared.Next()).ToList();
        var spread = Report.Summarize(Metric("spread", shuffled, shuffled));
        spread.SqlServer.Runs.Should().Be(200);
        spread.SqlServer.P95Ms.Should().Be(190.0);
        spread.SqlServer.MedianMs.Should().Be(100.5);
        spread.Postgres.P95Ms.Should().Be(190.0);
        spread.Flagged.Should().BeFalse();

        var markdown = Report.Render(Run(smoke: false,
            Operation("at-threshold", Metric("at-threshold", Constant(200, 10.0), Constant(200, 12.0))),
            Operation("over-threshold", Metric("over-threshold", Constant(200, 10.0), Constant(200, 12.1)))));
        var summaryRows = markdown.Split('\n').Where(l => l.StartsWith("| at-threshold") || l.StartsWith("| over-threshold")).ToList();
        summaryRows.Should().HaveCount(2);
        summaryRows.Single(l => l.StartsWith("| over-threshold")).Should().Contain(Report.FlagText);
        summaryRows.Single(l => l.StartsWith("| at-threshold")).Should().NotContain(Report.FlagText);
        Section(markdown, "at-threshold").Should().NotContain(Report.FlagText);
        Section(markdown, "over-threshold").Should().Contain(Report.FlagText);
    }

    /// <summary>The text of one operation's section, from its heading to the next heading.</summary>
    private static string Section(string markdown, string operation)
    {
        var start = markdown.IndexOf("### " + operation + "\n", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = markdown.IndexOf("\n#", start + 4, StringComparison.Ordinal);
        return end < 0 ? markdown[start..] : markdown[start..end];
    }

    [TestMethod]
    public void Rendered_tables_show_runs_median_p95_and_ratio_per_provider()
    {
        var markdown = Report.Render(Run(smoke: false,
            Operation("load", Metric("load", Constant(200, 10.0), Constant(200, 12.1)))));

        markdown.Should().Contain("| Provider | Runs | Median ms | p95 ms | Postgres / SQL Server (median) | Flag |");
        markdown.Should().Contain("| SQL Server | 200 | 10.00 | 10.00 |");
        markdown.Should().Contain("| Postgres | 200 | 12.10 | 12.10 | 1.21 | " + Report.FlagText + " |");
    }

    [TestMethod]
    public void Smoke_report_carries_the_not_valid_banner_and_a_full_report_does_not()
    {
        var smoke = Report.Render(Run(smoke: true, Operation("op", Metric("op", Constant(10, 1.0), Constant(10, 1.0)))));
        var full = Report.Render(Run(smoke: false, Operation("op", Metric("op", Constant(10, 1.0), Constant(10, 1.0)))));

        smoke.Should().StartWith("> **" + Report.SmokeBanner + "**");
        smoke.Split('\n').Count(l => l.Contains(Report.SmokeBanner)).Should().BeGreaterThan(1, "the banner repeats at the end too");
        full.Should().NotContain(Report.SmokeBanner);
    }

    [TestMethod]
    public void A_metric_without_samples_for_a_provider_is_reported_as_not_available()
    {
        var summary = Report.Summarize(new MetricSamples("op", "op", new Dictionary<DatabaseProvider, IReadOnlyList<double>>
        {
            [DatabaseProvider.SqlServer] = Constant(5, 2.0),
            [DatabaseProvider.Postgres] = Array.Empty<double>(),
        }));

        summary.Postgres.Runs.Should().Be(0);
        double.IsNaN(summary.Postgres.MedianMs).Should().BeTrue();
        summary.Ratio.Should().BeNull();
        summary.Flagged.Should().BeFalse();
        Report.Render(Run(smoke: false, new OperationResult("op", "d", [summary.Source], []))).Should().Contain("| Postgres | 0 | n/a | n/a | n/a |");
    }

    [TestMethod]
    public void Header_sections_and_notes_are_rendered_in_order()
    {
        var report = new RunReport(
            Smoke: false,
            Title: "Title",
            Sections: [new ReportSection("Hardware", ["- CPU: test"]), new ReportSection("Images", ["- image: x"])],
            Operations: [new OperationResult("op", "Times op.", [Metric("op", Constant(3, 1.0), Constant(3, 1.0))], ["Extra note."])],
            Trailer: [new ReportSection("Things to watch", ["- item"])]);

        var markdown = Report.Render(report);

        markdown.IndexOf("## Hardware", StringComparison.Ordinal).Should().BeLessThan(markdown.IndexOf("## Images", StringComparison.Ordinal));
        markdown.IndexOf("## Summary", StringComparison.Ordinal).Should().BeGreaterThan(markdown.IndexOf("## Images", StringComparison.Ordinal));
        markdown.Should().Contain("Times op.").And.Contain("Extra note.");
        markdown.IndexOf("## Things to watch", StringComparison.Ordinal).Should().BeGreaterThan(markdown.IndexOf("### op", StringComparison.Ordinal));
    }

    private static IReadOnlyList<double> Constant(int count, double value) => Enumerable.Repeat(value, count).ToList();

    private static MetricSamples Metric(string name, IReadOnlyList<double> sqlServer, IReadOnlyList<double> postgres) =>
        new(name, name, new Dictionary<DatabaseProvider, IReadOnlyList<double>>
        {
            [DatabaseProvider.SqlServer] = sqlServer,
            [DatabaseProvider.Postgres] = postgres,
        });

    private static OperationResult Operation(string name, params MetricSamples[] metrics) => new(name, $"Description of {name}.", metrics, []);

    private static RunReport Run(bool smoke, params OperationResult[] operations) =>
        new(smoke, "Database speed", [], operations, []);
}
