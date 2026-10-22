using Parity.Core.Comparison;
using Parity.Core.Data;
using Parity.Core.Model;
using Parity.Core.Reporting;
using Parity.Core.Settings;
using static Parity.Tests.Support.Hits;

namespace Parity.Tests.Unit;

public class ReportTests
{
    private static readonly ReviewStatus Unreviewed = new(ReviewOutcome.Unreviewed, null);

    private static ComparisonReport Report(bool appSettingsModified = false, params PairResult[] pairs)
    {
        var context = new RunContext(
            DateTimeOffset.Parse("2026-10-21T09:30:00Z"), "abcd1234", appSettingsModified, "sqlserver", "postgres",
            new EngineInfo("SqlServer", "Microsoft SQL Server 2025 (RTM)", new Dictionary<string, string> { ["fullTextLanguage"] = "1033" }),
            new EngineInfo("Postgres", "PostgreSQL 18.1", new Dictionary<string, string> { ["pgvector"] = "0.8.7" }),
            new Manifest("9f8cd102aaaa", DateTimeOffset.Parse("2026-10-20T00:00:00Z"), "text-embedding-3-small", 1536, 1190, 38,
                new Dictionary<string, string>(), new Dictionary<string, string>()),
            new ThresholdSettings(), 1e-5, 1e-6,
            "ghcr.io/elumenotion/mssql2025-express-fts:main", "[\"ghcr.io/x@sha256:abc\"]", "pgvector/pgvector:0.8.7-pg18", "unknown (docker image inspect failed)");
        return new ComparisonReport(
            context,
            pairs,
            Verdict.Evaluate(pairs, new ThresholdSettings()),
            new InformationalSummary("Quirk fixed on both engines (not production)", true, null, 1, 0.97, 0.95, 0, 0),
            [InformationalSummary.Unavailable("postgres-english", "Profile 'postgres-english' isn't seeded.")],
            [],
            new Dictionary<Guid, string> { [Id("a")] = "docs/a.md#0", [Id("b")] = "inputs/edge-cases.json#E01" });
    }

    private static PairResult Clean(string query = "Q01") =>
        new(new QueryRef(query, "exact", "conversation lock"), "P1", VectorParityResult.Passed, new TextDiff([], []), null, null, null, null,
            new HybridPairMetrics(1.0, true), [], [], []);

    private static PairResult WithTextDiff() =>
        new(new QueryRef("Q19", "edge", "text-embedding-3-small"), "P2", VectorParityResult.Passed, new TextDiff([Id("a")], [Id("b")]), Unreviewed,
            new TextEvidence([new Token("text", "text", "word", false)], [new Token("text-embedding-3-small", "text-embedding-3-small", "hword", false)],
                [new SnippetEvidence(Id("b"), "candidate-only", "default to text-embedding-3-small with", [new Token("the", "the", "Noise Word", true)], [])]),
            null, null, new HybridPairMetrics(0.8, true),
            [new RankDifference(Id("b"), null, 4, DifferenceCategory.MatchedOneSide, "text 0.0000 vs 0.3000")], [], []);

    private static PairResult WithError() =>
        new(new QueryRef("Q36", "error", "!!! ???"), "P1", VectorParityResult.Passed, null, null, null,
            new ErrorOutcome(null, new EngineError("PG42601", "syntax error at or near \"|\"")), Unreviewed, null, [], [], []);

    [Fact]
    public void ASwitchableRunSaysSoWithEveryCondition()
    {
        var markdown = MarkdownReport.Render(Report(false, Clean()));
        Assert.Contains("# Hybrid search parity results", markdown);
        Assert.Contains("**Verdict: SWITCHABLE**", markdown);
        Assert.Contains("| Vector parity | PASS | 1/1 pairs |", markdown);
        Assert.Contains("| Zero unexplained differences | PASS | 0 |", markdown);
        Assert.DoesNotContain("Warning", markdown);
    }

    [Fact]
    public void TheEnvironmentRecordsVersionsDigestsDataAndThresholds()
    {
        var markdown = MarkdownReport.Render(Report(false, Clean()));
        Assert.Contains("Sample commit: `abcd1234`", markdown);
        Assert.Contains("Microsoft SQL Server 2025 (RTM)", markdown);
        Assert.Contains("pgvector: `0.8.7`", markdown);
        Assert.Contains("ghcr.io/x@sha256:abc", markdown);
        Assert.Contains("corpus `9f8cd102`", markdown);
        Assert.Contains("mean top-5 overlap ≥ 0.9", markdown);
    }

    [Fact]
    public void ModifiedThresholdsAreFlagged() =>
        Assert.Contains("`appsettings.json` has uncommitted changes", MarkdownReport.Render(Report(true, Clean())));

    [Fact]
    public void TextDifferencesShowStatusChunksTokensAndSnippets()
    {
        var markdown = MarkdownReport.Render(Report(false, WithTextDiff()));
        Assert.Contains("**Verdict: NOT SWITCHABLE**", markdown);
        Assert.Contains("### `Q19` · P2", markdown);
        Assert.Contains("**UNREVIEWED**", markdown);
        Assert.Contains("inputs/edge-cases.json#E01", markdown);
        Assert.Contains("text-embedding-3-small", markdown);
        Assert.Contains("~~the~~", markdown);
        Assert.Contains("matched-one-side", markdown);
    }

    [Fact]
    public void ErrorsAndUnavailableInformationalRunsAreListed()
    {
        var markdown = MarkdownReport.Render(Report(false, Clean(), WithError()));
        Assert.Contains("## Errors", markdown);
        Assert.Contains("PG42601", markdown);
        Assert.Contains("syntax error at or near \"\\|\"", markdown); // table cells escape pipes
        Assert.Contains("not available: Profile 'postgres-english' isn't seeded.", markdown);
        Assert.Contains("Quirk fixed on both engines (not production)", markdown);
    }

    [Fact]
    public void ConsoleSummaryListsConditionsAndOpenItems()
    {
        var text = ConsoleSummary.Render(Report(false, Clean(), WithTextDiff(), WithError()));
        Assert.Contains("Hybrid search parity: NOT SWITCHABLE (sqlserver vs postgres, 3 pairs)", text);
        Assert.Contains("PASS  Vector parity", text);
        Assert.Contains("FAIL  Full-text and error differences reviewed and accepted", text);
        Assert.Contains("Q19 · P2  full-text  UNREVIEWED", text);
        Assert.Contains("Q36 · P1  error  UNREVIEWED", text);
    }

    private static PairResult TextDiffNullReview() =>
        WithTextDiff() with { TextReview = null };

    private static PairResult ErrorNullReview() =>
        WithError() with { ErrorReview = null };

    [Fact]
    public void ConsoleListsEveryPairTheVerdictCountsAsOpenEvenWithNullReviews()
    {
        var report = Report(false, TextDiffNullReview(), ErrorNullReview());
        Assert.Contains("2 open", report.Verdict.Conditions[1].Value);
        var text = ConsoleSummary.Render(report);
        Assert.Contains("Q19 · P2  full-text  UNREVIEWED", text);
        Assert.Contains("Q36 · P1  error  UNREVIEWED", text);
        var markdown = MarkdownReport.Render(report);
        Assert.Contains("**UNREVIEWED**", markdown);
        Assert.Contains("| UNREVIEWED |", markdown);
    }

    [Fact]
    public void ErrorPairsThatAlsoCarryATextDiffAppearInRuleTwo()
    {
        var pair = WithError() with { Text = new TextDiff([Id("a")], []), TextReview = Unreviewed };
        var markdown = MarkdownReport.Render(Report(false, pair));
        Assert.Contains("### `Q36` · P1", markdown);
        Assert.Contains("## Errors", markdown);
    }
}
