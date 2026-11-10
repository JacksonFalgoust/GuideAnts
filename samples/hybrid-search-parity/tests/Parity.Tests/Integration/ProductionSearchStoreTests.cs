using GuideAntsApi.DataModel.Database;
using Microsoft.Data.SqlClient;
using Npgsql;
using Parity.Cli;
using Parity.Core;
using Parity.Core.Comparison;
using Parity.Core.Corpus;
using Parity.Core.Model;
using Parity.Core.Settings;
using Parity.Production;
using Parity.Tests.Support;

namespace Parity.Tests.Integration;

/// <summary>The production searchers, run against databases built by the real migrations.</summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ProductionSearchStoreTests(IntegrationFixture fixture, Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task Production_store_returns_hits_on_both_providers()
    {
        var query = fixture.FullData.Value.Queries.Single(q => q.Text == "conversation lock");
        var p1 = ParityScopes.Find("P1").Scope;
        var h = new HybridSettings();
        var (sqlServer, postgres) = await fixture.ProductionEnginesAsync();
        await using (sqlServer)
        await using (postgres)
        {
            foreach (var engine in new[] { sqlServer, postgres })
            {
                var result = await engine.Store.HybridSearchAsync(
                    new HybridQuery(query.Text, query.Vector, p1, h.MaxResults, h.VectorWeight, h.TextWeight, h.CandidateMultiplier),
                    CancellationToken.None);

                Assert.True(result.IsSuccess, $"{engine.Profile}: {result.Error?.Message}");
                Assert.NotEmpty(result.Value);
                Assert.All(result.Value, hit => Assert.Equal(p1.IndexName, hit.IndexName));

                // The full-text half works through the migrated schema too. Ranked by text alone, the best table-wide
                // match comes first in its own project (P1's top 5 at the default weights needn't hold one).
                var textOnly = new List<SearchHit>();
                foreach (var project in ParityScopes.ProjectLabels.Select(label => ParityScopes.Find(label).Scope))
                {
                    var ranked = await engine.Store.HybridSearchAsync(
                        new HybridQuery(query.Text, query.Vector, project, h.MaxResults, VectorWeight: 0, TextWeight: 1, h.CandidateMultiplier),
                        CancellationToken.None);
                    Assert.True(ranked.IsSuccess, $"{engine.Profile}: {ranked.Error?.Message}");
                    textOnly.AddRange(ranked.Value);
                }

                Assert.Contains(textOnly, hit => hit.TextScore > 0);
            }
        }
    }

    [Fact]
    public async Task Production_profiles_use_real_searchers()
    {
        var settings = AppConfig.Load(RepoPaths.FindSampleRoot(), includeLocalOverrides: false, environment: _ => null);
        await using var sqlServer = SearchStoreFactory.Create(settings, "prod-sqlserver");
        await using var postgres = SearchStoreFactory.Create(settings, "prod-postgres");

        Assert.Equal(
            "GuideAntsApi.BackgroundJobs.Services.Search.HybridSearcher",
            Assert.IsType<ProductionSearchStore>(sqlServer.Store).ResolveSearcherType().FullName);
        Assert.Equal(
            "GuideAntsApi.BackgroundJobs.Services.Search.PostgresHybridSearcher",
            Assert.IsType<ProductionSearchStore>(postgres.Store).ResolveSearcherType().FullName);
    }

    /// <summary>
    /// Production logs a failed search and returns an empty list. The store reports the logged error instead, so a
    /// failure never reads as "no results": a missing database stops the run, a query error is a failed result.
    /// </summary>
    [Fact]
    public async Task Production_store_reports_failed_searches_instead_of_no_results()
    {
        var query = MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1);
        await using (var unseeded = new ProductionSearchStore(DatabaseProvider.Postgres, fixture.PostgresConnectionString("parity_production_unseeded"), "unseeded"))
        {
            await Assert.ThrowsAsync<NotSeededException>(() => unseeded.HybridSearchAsync(query, CancellationToken.None));
        }

        var cs = fixture.PostgresConnectionString("parity_production_broken");
        await using var broken = new ProductionSearchStore(DatabaseProvider.Postgres, cs, "broken");
        await broken.EnsureSchemaAsync(CancellationToken.None);
        await using (var connection = new NpgsqlConnection(cs))
        {
            await connection.OpenAsync();
            await using var rename = new NpgsqlCommand("""ALTER TABLE "DocumentChunks" RENAME COLUMN "ContentTsv" TO "ContentTsvGone" """, connection);
            await rename.ExecuteNonQueryAsync();
        }

        var result = await broken.HybridSearchAsync(query, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("PG42703", result.Error.Code); // undefined column
    }

    /// <summary>The SQL Server side of the same guarantee: production's CONTAINSTABLE query fails without its index.</summary>
    [Fact]
    public async Task Production_sqlserver_store_reports_a_failed_search_instead_of_no_results()
    {
        var cs = fixture.SqlServerConnectionString("parity_production_broken");
        await using var broken = new ProductionSearchStore(DatabaseProvider.SqlServer, cs, "broken-sqlserver", IntegrationFixture.PopulationTimeout);
        await broken.EnsureSchemaAsync(CancellationToken.None);
        await using (var connection = new SqlConnection(cs))
        {
            await connection.OpenAsync();
            await using var drop = new SqlCommand("DROP FULLTEXT INDEX ON DocumentChunks", connection);
            await drop.ExecuteNonQueryAsync();
        }

        var result = await broken.HybridSearchAsync(MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("SQL7601", result.Error.Code); // not full-text indexed
    }

    /// <summary>The comparison's Postgres diagnostics must parse text the way the production searcher does.</summary>
    [Fact]
    public async Task Production_postgres_diagnostics_normalize_text_like_the_searcher()
    {
        var (sqlServer, postgres) = await fixture.ProductionEnginesAsync();
        await using (sqlServer)
        await using (postgres)
        {
            var tokens = (await postgres.Diagnostics.TokenizeAsync("C# and .NET", CancellationToken.None)).Unwrap("tokenize");
            Assert.Equal(["csharp", "dotnet"], tokens.Where(t => !t.IsStopWord).Select(t => t.Normalized));

            var matches = new List<Guid>();
            foreach (var label in ParityScopes.ProjectLabels)
            {
                matches.AddRange((await postgres.Diagnostics.TextMatchAsync("C#", ParityScopes.Find(label).Scope, CancellationToken.None)).Unwrap("text match").Select(m => m.ChunkId));
            }

            // "C#" is searched as "csharp", so it finds C# and the "```csharp" code fences, never a bare letter c.
            Assert.NotEmpty(matches);
            Assert.All(matches, id => Assert.Matches(@"(?i)(?<![a-z0-9_])(c#|csharp)(?![a-z0-9_])", fixture.FullData.Value.FindChunk(id)!.Content));
            Assert.Contains(matches, id => fixture.FullData.Value.FindChunk(id)!.Content.Contains("C#", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The comparison explains rankings with the diagnostics, whose Postgres side is a copy of the searcher's SQL. In
    /// the production comparison every top-5 hit's own scores (computed by the searchers) must equal the diagnostics'.
    /// </summary>
    [Fact]
    public async Task Production_diagnostics_reproduce_every_production_hit()
    {
        var pairs = await fixture.ProductionComparisonAsync();
        var disagreements = pairs
            .SelectMany(p => p.Differences.Where(d => d.Detail.Contains(" diagnostics disagree with the hit", StringComparison.Ordinal))
                .Select(d => $"{p.Query.Id}·{p.Scope}: {d.ChunkId} ({d.Detail})"))
            .ToList();
        var checkedHits = pairs.Sum(p => p.ScoresChecked);
        output.WriteLine($"checked {checkedHits} top-5 hits in {pairs.Count} pairs");

        Assert.Equal(152, pairs.Count);
        Assert.True(checkedHits >= pairs.Count(p => p.Hybrid is not null) * 2, $"only {checkedHits} hits checked");
        Assert.True(disagreements.Count == 0, string.Join("\n", disagreements));
    }

    /// <summary>The production counterpart of FullParityTests' rule 2 and unexplained checks.</summary>
    [Fact]
    public async Task Production_differences_have_current_reviews_and_explanations()
    {
        var pairs = await fixture.ProductionComparisonAsync();
        var open = pairs
            .Where(p => (p.Text is { IsEqual: false } && (p.TextReview?.IsOpen ?? true)) || (p.Error is { NeedsReview: true } && (p.ErrorReview?.IsOpen ?? true)))
            .Select(p => $"{p.Query.Id}·{p.Scope}: text {ComparisonReport_Status(p.TextReview)}, error {p.Error?.Code}")
            .ToList();
        var unexplained = pairs.SelectMany(p => p.Differences.Where(d => d.Category == DifferenceCategory.Unexplained).Select(d => $"{p.Query.Id}·{p.Scope}: {d.Detail}")).ToList();

        Assert.True(open.Count == 0, "Open production reviews:\n" + string.Join("\n", open));
        Assert.True(unexplained.Count == 0, string.Join("\n", unexplained));
        Assert.All(pairs, p => Assert.True(p.Vector.Pass, p.Vector.Failure));
    }

    private static string ComparisonReport_Status(ReviewStatus? status) => Parity.Core.Reporting.ComparisonReport.Status(status);
}
