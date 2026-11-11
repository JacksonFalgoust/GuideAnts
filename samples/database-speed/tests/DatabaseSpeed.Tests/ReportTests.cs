
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
    public void Rendered_tables_show_runs_median_p95_delta_and_ratio_per_provider()
    {
        var markdown = Report.Render(Run(smoke: false,
            Operation("load", Metric("load", Constant(200, 10.0), Constant(200, 12.1)))));

        markdown.Should().Contain("| Operation | SQL Server median ms | Postgres median ms | Postgres - SQL Server (ms) | Postgres / SQL Server | Flag |");
        markdown.Should().Contain("| load | 10.00 | 12.10 | +2.10 | 1.21 | " + Report.FlagText + " |");
        markdown.Should().Contain("| Provider | Runs | Median ms | p95 ms | Postgres - SQL Server (median ms) | Postgres / SQL Server (median) | Flag |");
        markdown.Should().Contain("| SQL Server | 200 | 10.00 | 10.00 |");
        markdown.Should().Contain("| Postgres | 200 | 12.10 | 12.10 | +2.10 | 1.21 | " + Report.FlagText + " |");
    }

    [TestMethod]
    public void A_faster_Postgres_shows_a_negative_delta()
    {
        var markdown = Report.Render(Run(smoke: false, Operation("fast", Metric("fast", Constant(5, 0.50), Constant(5, 0.38)))));

        markdown.Should().Contain("| fast | 0.50 | 0.38 | -0.12 | 0.76 |  |");
    }

    [TestMethod]
    public void Smoke_report_on_an_x86_daemon_does_not_claim_emulation()
    {
        var emulated = Report.Render(Run(smoke: true, emulated: true, Operation("op", Metric("op", Constant(10, 1.0), Constant(10, 1.0)))));
        var native = Report.Render(Run(smoke: true, emulated: false, Operation("op", Metric("op", Constant(10, 1.0), Constant(10, 1.0)))));
        var full = Report.Render(Run(smoke: false, Operation("op", Metric("op", Constant(10, 1.0), Constant(10, 1.0)))));

        Report.SmokeBanner(emulated: true).Should().Be("SMOKE RUN — NOT VALID FOR COMPARISON (emulated SQL Server / tiny data)");
        Report.SmokeBanner(emulated: false).Should().Be("SMOKE RUN — NOT VALID FOR COMPARISON (tiny data)");
        emulated.Should().StartWith("> **" + Report.SmokeBanner(emulated: true) + "**");
        emulated.Split('\n').Count(l => l.Contains(Report.SmokeBanner(emulated: true))).Should().BeGreaterThan(1, "the banner repeats at the end too");
        native.Should().StartWith("> **" + Report.SmokeBanner(emulated: false) + "**").And.NotContain("emulat");
        full.Should().NotContain(Report.SmokeBannerPrefix);
    }

    [TestMethod]
    public void The_limits_block_comes_directly_before_the_summary()
    {
        var report = new RunReport(false, "Title", [new ReportSection("Images", ["- x"])],
            [Operation("op", Metric("op", Constant(3, 1.0), Constant(3, 1.0)))], [], Limits: ["- (a) first limit", "- (b) second limit"]);

        var markdown = Report.Render(report);

        var limits = markdown.IndexOf("## Limits of this comparison", StringComparison.Ordinal);
        limits.Should().BeGreaterThan(markdown.IndexOf("## Images", StringComparison.Ordinal));
        markdown.IndexOf("- (b) second limit", StringComparison.Ordinal).Should().BeLessThan(markdown.IndexOf("## Summary", StringComparison.Ordinal));
        markdown[limits..markdown.IndexOf("## Summary", StringComparison.Ordinal)].Should().NotContain("\n## ", "nothing else sits between the limits and the summary")
            .And.Contain("- (a) first limit");
    }

    [TestMethod]
    public void A_metric_caveat_is_printed_beside_its_summary_row_and_above_its_table()
    {
        var metric = Metric("cold start", Constant(10, 3000.0), Constant(10, 300.0)) with { Caveat = "SQL Server applies 60 migrations, Postgres 2" };
        var markdown = Report.Render(Run(smoke: false, Operation("cold start", metric)));

        markdown.Split('\n').Should().Contain(l => l.StartsWith("| cold start") && l.Contains("SQL Server applies 60 migrations, Postgres 2"));
        Section(markdown, "cold start").Should().Contain("SQL Server applies 60 migrations, Postgres 2");
    }

    [TestMethod]
    public void Results_that_differ_between_providers_are_marked_in_the_summary()
    {
        var differs = new OperationResult("list", "d", [Metric("list", Constant(3, 1.0), Constant(3, 1.0))], [], ResultMismatch: "SQL Server 50 projects; Postgres 49 projects");
        var same = Operation("other", Metric("other", Constant(3, 1.0), Constant(3, 1.0)));

        var markdown = Report.Render(Run(smoke: false, differs, same));

        var rows = markdown.Split('\n').Where(l => l.StartsWith("| list") || l.StartsWith("| other")).ToList();
        rows.Single(l => l.StartsWith("| list")).Should().Contain(Report.MismatchText);
        rows.Single(l => l.StartsWith("| other")).Should().NotContain(Report.MismatchText);
        Section(markdown, "list").Should().Contain("SQL Server 50 projects; Postgres 49 projects");
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
        Report.Render(Run(smoke: false, new OperationResult("op", "d", [summary.Source], []))).Should().Contain("| Postgres | 0 | n/a | n/a | n/a | n/a |");
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
        new(smoke, "Database speed", [], operations, [], Emulated: smoke);

    private static RunReport Run(bool smoke, bool emulated, params OperationResult[] operations) =>
        new(smoke, "Database speed", [], operations, [], Emulated: emulated);
}
