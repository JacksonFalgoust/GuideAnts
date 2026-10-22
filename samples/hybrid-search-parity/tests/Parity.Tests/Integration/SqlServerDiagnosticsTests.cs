using Parity.Core;
using Parity.SqlServer;
using Parity.Tests.Support;

namespace Parity.Tests.Integration;

[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SqlServerDiagnosticsTests(IntegrationFixture fixture)
{
    private async Task<SqlServerSearchDiagnostics> DiagnosticsAsync() => new("mini-sqlserver", await fixture.MiniSqlServerAsync());

    private static float[] VectorOf(string key) => MiniCorpus.Chunks.Single(c => c.Id == MiniCorpus.Id(key)).Embedding;

    [Fact]
    public async Task VectorSearchRanksAChunksOwnVectorFirst()
    {
        var hits = (await (await DiagnosticsAsync()).VectorSearchAsync(VectorOf("m01"), MiniCorpus.ScopeP1, 10, CancellationToken.None)).Value!;
        Assert.Equal(5, hits.Count); // P1 has five chunks
        Assert.Equal(MiniCorpus.Id("m01"), hits[0].ChunkId);
        Assert.True(hits[0].Score >= 1 - 1e-6);
    }

    [Fact]
    public async Task TextMatchReturnsTheFullSetWithoutTopN()
    {
        var diagnostics = await DiagnosticsAsync();
        Assert.Equal(12, (await diagnostics.TextMatchAsync("alpha beta", MiniCorpus.ScopeP2, CancellationToken.None)).Value!.Count);
        Assert.Equal(new[] { MiniCorpus.Id("m17") }, (await diagnostics.TextMatchAsync("alpha beta", MiniCorpus.ScopeP1, CancellationToken.None)).Value!.Select(m => m.ChunkId));
    }

    [Fact]
    public async Task ScoreChunksReportsTheTopNCutAndTheVariantKeepsTheChunk()
    {
        var diagnostics = await DiagnosticsAsync();
        var ids = new[] { MiniCorpus.Id("m17") };

        var asIs = Assert.Single((await diagnostics.ScoreChunksAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1), ids, CancellationToken.None)).Value!);
        Assert.True(asIs.Matched);
        Assert.False(asIs.InCandidates);
        Assert.Equal(0, asIs.TextScore);
        Assert.NotNull(asIs.NthCandidateRank);
        Assert.True(asIs.RawRank < asIs.NthCandidateRank);

        var scoped = Assert.Single((await diagnostics.ScoreChunksAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1, filterBeforeTopN: true), ids, CancellationToken.None)).Value!);
        Assert.True(scoped.InCandidates);
        Assert.Equal(scoped.RawRank!.Value, scoped.TextScore, 12);
    }

    [Fact]
    public async Task ScoreChunksAgreesWithTheHybridQuery()
    {
        var cs = await fixture.MiniSqlServerAsync();
        var query = MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1);
        var hits = (await new SqlServerSearchStore("mini-sqlserver", cs, IntegrationFixture.PopulationTimeout).HybridSearchAsync(query, CancellationToken.None)).Value!;
        var scores = (await new SqlServerSearchDiagnostics("mini-sqlserver", cs).ScoreChunksAsync(query, hits.Select(h => h.ChunkId).ToList(), CancellationToken.None)).Value!
            .ToDictionary(s => s.ChunkId);

        Assert.All(hits, h =>
        {
            Assert.Equal(h.VectorScore, scores[h.ChunkId].VectorScore, 9);
            Assert.Equal(h.TextScore, scores[h.ChunkId].TextScore, 9);
        });
    }

    [Fact]
    public async Task TokenizeMarksNoiseWords()
    {
        var tokens = (await (await DiagnosticsAsync()).TokenizeAsync("the end of the line", CancellationToken.None)).Value!;
        Assert.Contains(tokens, t => t.IsStopWord);
        Assert.Contains(tokens, t => t.Text == "end" && !t.IsStopWord);
    }

    [Fact]
    public async Task DescribeReportsVersionAndFullTextSettings()
    {
        var info = await (await DiagnosticsAsync()).DescribeAsync(CancellationToken.None);
        Assert.Equal("SqlServer", info.Provider);
        Assert.Contains("Microsoft SQL Server 2025", info.Version);
        Assert.Equal("1033", info.Details["fullTextLanguage"]);
        Assert.Equal("system", info.Details["stoplist"]);
    }

    [Fact]
    public async Task CoverageCountsEveryRow()
    {
        var coverage = await (await DiagnosticsAsync()).GetIndexCoverageAsync(CancellationToken.None);
        Assert.Equal((18, 18), (coverage.Rows, coverage.IndexedRows));
    }

    [Fact]
    public async Task MissingDatabaseIsReportedAsNotSeeded()
    {
        var diagnostics = new SqlServerSearchDiagnostics("absent", fixture.SqlServerConnectionString("parity_absent"));
        var error = await Assert.ThrowsAsync<NotSeededException>(() => diagnostics.GetIndexCoverageAsync(CancellationToken.None));
        Assert.Contains("seed --profile absent", error.Message);
    }
}
