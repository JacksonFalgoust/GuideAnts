using Parity.Core;
using Parity.Postgres;
using Parity.Tests.Support;

namespace Parity.Tests.Integration;

[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresDiagnosticsTests(IntegrationFixture fixture)
{
    private async Task<PostgresSearchDiagnostics> DiagnosticsAsync() =>
        new("mini-postgres", await fixture.MiniPostgresAsync(), PostgresSchema.CustomConfigName);

    private static float[] VectorOf(string key) => MiniCorpus.Chunks.Single(c => c.Id == MiniCorpus.Id(key)).Embedding;

    [Fact]
    public async Task VectorSearchRanksAChunksOwnVectorFirst()
    {
        await using var diagnostics = await DiagnosticsAsync();
        var hits = (await diagnostics.VectorSearchAsync(VectorOf("m01"), MiniCorpus.ScopeP1, 10, CancellationToken.None)).Value!;
        Assert.Equal(5, hits.Count);
        Assert.Equal(MiniCorpus.Id("m01"), hits[0].ChunkId);
        Assert.True(hits[0].Score >= 1 - 1e-6);
    }

    [Fact]
    public async Task TextMatchReturnsTheFullSetWithoutTopN()
    {
        await using var diagnostics = await DiagnosticsAsync();
        Assert.Equal(12, (await diagnostics.TextMatchAsync("alpha beta", MiniCorpus.ScopeP2, CancellationToken.None)).Value!.Count);
        Assert.Equal(new[] { MiniCorpus.Id("m17") }, (await diagnostics.TextMatchAsync("alpha beta", MiniCorpus.ScopeP1, CancellationToken.None)).Value!.Select(m => m.ChunkId));
    }

    [Fact]
    public async Task ScoreChunksReportsTheTopNCutAndTheVariantKeepsTheChunk()
    {
        await using var diagnostics = await DiagnosticsAsync();
        var ids = new[] { MiniCorpus.Id("m17") };

        var asIs = Assert.Single((await diagnostics.ScoreChunksAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1), ids, CancellationToken.None)).Value!);
        Assert.True(asIs.Matched);
        Assert.False(asIs.InCandidates);
        Assert.Equal(0, asIs.TextScore);
        Assert.True(asIs.RawRank < asIs.NthCandidateRank);

        var scoped = Assert.Single((await diagnostics.ScoreChunksAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1, filterBeforeTopN: true), ids, CancellationToken.None)).Value!);
        Assert.True(scoped.InCandidates);
        Assert.Equal(scoped.RawRank!.Value, scoped.TextScore, 12);
    }

    [Fact]
    public async Task ScoreChunksAgreesWithTheHybridQuery()
    {
        var cs = await fixture.MiniPostgresAsync();
        var query = MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1);
        await using var store = new PostgresSearchStore("mini-postgres", cs, PostgresSchema.CustomConfigName);
        await using var diagnostics = new PostgresSearchDiagnostics("mini-postgres", cs, PostgresSchema.CustomConfigName);
        var hits = (await store.HybridSearchAsync(query, CancellationToken.None)).Value!;
        var scores = (await diagnostics.ScoreChunksAsync(query, hits.Select(h => h.ChunkId).ToList(), CancellationToken.None)).Value!.ToDictionary(s => s.ChunkId);

        Assert.NotEmpty(hits);
        Assert.All(hits, h =>
        {
            Assert.Equal(h.VectorScore, scores[h.ChunkId].VectorScore, 9);
            Assert.Equal(h.TextScore, scores[h.ChunkId].TextScore, 9);
        });
    }

    [Fact]
    public async Task TokenizeMarksStopWords()
    {
        await using var diagnostics = await DiagnosticsAsync();
        var tokens = (await diagnostics.TokenizeAsync("the end of the line", CancellationToken.None)).Value!;
        Assert.Contains(tokens, t => t.Text == "the" && t.IsStopWord);
        Assert.Contains(tokens, t => t.Text == "end" && !t.IsStopWord && t.Normalized == "end");
    }

    [Fact]
    public async Task DescribeReportsVersionsAndConfig()
    {
        await using var diagnostics = await DiagnosticsAsync();
        var info = await diagnostics.DescribeAsync(CancellationToken.None);
        Assert.Equal("Postgres", info.Provider);
        Assert.Contains("PostgreSQL 18", info.Version);
        Assert.Equal("0.8.7", info.Details["pgvector"]);
        Assert.Equal(PostgresSchema.CustomConfigName, info.Details["textSearchConfig"]);
    }

    [Fact]
    public async Task CoverageCountsEveryRow()
    {
        await using var diagnostics = await DiagnosticsAsync();
        var coverage = await diagnostics.GetIndexCoverageAsync(CancellationToken.None);
        Assert.Equal((18, 18), (coverage.Rows, coverage.IndexedRows));
    }

    [Fact]
    public async Task StopWordOnlyChunkStillCountsAsIndexed()
    {
        // An all-stop-word chunk legitimately yields an empty tsvector; it is still indexed (the generated column is populated).
        var stopWordChunk = MiniCorpus.Chunks[0] with { Id = MiniCorpus.Id("stop-words-only"), ChunkIndex = 99, Content = "the and of" };
        IReadOnlyList<Parity.Core.Model.ChunkRecord> chunks = [.. MiniCorpus.Chunks, stopWordChunk];
        var cs = await fixture.SeedOnceAsync(
            "stop-word-chunk-postgres",
            () => fixture.PostgresConnectionString("parity_stopword_chunk"),
            c => new PostgresSearchStore("stop-word-chunk", c, PostgresSchema.CustomConfigName),
            chunks,
            MiniCorpus.CreatedUtc);

        await using var diagnostics = new PostgresSearchDiagnostics("stop-word-chunk", cs, PostgresSchema.CustomConfigName);
        var coverage = await diagnostics.GetIndexCoverageAsync(CancellationToken.None);
        Assert.Equal((chunks.Count, chunks.Count), (coverage.Rows, coverage.IndexedRows));
    }

    [Fact]
    public async Task MissingDatabaseIsReportedAsNotSeeded()
    {
        await using var diagnostics = new PostgresSearchDiagnostics("absent", fixture.PostgresConnectionString("parity_absent"), PostgresSchema.CustomConfigName);
        var error = await Assert.ThrowsAsync<NotSeededException>(() => diagnostics.GetIndexCoverageAsync(CancellationToken.None));
        Assert.Contains("seed --profile absent", error.Message);
    }
}
