using Parity.Core.Model;
using Parity.SqlServer;
using Parity.Tests.Support;

namespace Parity.Tests.Integration;

[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SqlServerStoreTests(IntegrationFixture fixture)
{
    private async Task<SqlServerSearchStore> StoreAsync() =>
        new("mini-sqlserver", await fixture.MiniSqlServerAsync(), IntegrationFixture.PopulationTimeout);

    private static SearchHit Hit(IReadOnlyList<SearchHit> hits, string key) => hits.Single(h => h.ChunkId == MiniCorpus.Id(key));

    [Fact]
    public async Task SeedingTwiceRecreatesCleanly()
    {
        var store = new SqlServerSearchStore("reseed", fixture.SqlServerConnectionString("parity_reseed"), IntegrationFixture.PopulationTimeout);
        for (var run = 0; run < 2; run++)
        {
            await store.EnsureSchemaAsync(CancellationToken.None);
            await store.LoadAsync(MiniCorpus.Chunks, MiniCorpus.CreatedUtc, CancellationToken.None);
            await store.WaitUntilSearchableAsync(CancellationToken.None);
        }

        var result = await store.HybridSearchAsync(MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.Count);
        Assert.True(Hit(result.Value, "m01").TextScore > 0); // the full-text index was rebuilt
    }

    [Fact]
    public async Task PhraseMatchesWholeWordsOnly()
    {
        var result = await (await StoreAsync()).HybridSearchAsync(MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(Hit(result.Value, "m01").TextScore > 0);
        Assert.Equal(0, Hit(result.Value, "m02").TextScore); // "locks": no inflection in phrase search
        Assert.All(result.Value, h => Assert.Equal(0.7 * h.VectorScore + 0.3 * h.TextScore, h.Score, 9));
    }

    [Fact]
    public async Task NotebookScopeReturnsOnlyNotebookChunks()
    {
        var result = await (await StoreAsync()).HybridSearchAsync(MiniCorpus.Query("conversation", MiniCorpus.ScopeN1), CancellationToken.None);
        Assert.Equal(new[] { MiniCorpus.Id("m02") }, result.Value!.Select(h => h.ChunkId));
    }

    // Observed on the real engine (default language, system stoplist): every awkward query succeeds. Quote
    // doubling keeps the literal valid, and stop-word-only or punctuation-only phrases return success with
    // all-zero text scores rather than an error. Value = the chunk expected to be the only one with a text match.
    [Theory]
    [InlineData("O'Reilly Media", "m03")]
    [InlineData("don't restart", "m03")]
    [InlineData("say \"hello world\" twice", "m04")]
    [InlineData("the and of", null)]
    [InlineData("!!! ???", null)]
    public async Task AwkwardQueriesSucceedWithTheObservedTextMatches(string text, string? matchedKey)
    {
        var result = await (await StoreAsync()).HybridSearchAsync(MiniCorpus.Query(text, MiniCorpus.ScopeP1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(5, result.Value.Count);
        var matched = result.Value.Where(h => h.TextScore > 0).Select(h => h.ChunkId).ToList();
        Assert.Equal(matchedKey is null ? [] : new List<Guid> { MiniCorpus.Id(matchedKey) }, matched);
    }

    [Fact]
    public async Task ProductionTopNCutDropsTheInScopeMatchAndTheVariantKeepsIt()
    {
        var store = await StoreAsync();
        var asIs = await store.HybridSearchAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1), CancellationToken.None);
        var fixedVariant = await store.HybridSearchAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1, filterBeforeTopN: true), CancellationToken.None);

        Assert.Equal(0, Hit(asIs.Value!, "m17").TextScore);
        Assert.True(Hit(fixedVariant.Value!, "m17").TextScore > 0);
    }

    [Fact]
    public async Task BlankQueriesReturnNothingLikeProduction()
    {
        var result = await (await StoreAsync()).HybridSearchAsync(MiniCorpus.Query("   ", MiniCorpus.ScopeP1), CancellationToken.None);
        Assert.Empty(result.Value!);
    }
}
