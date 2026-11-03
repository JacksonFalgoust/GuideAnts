using Parity.Core.Model;
using Parity.Postgres;
using Parity.Tests.Support;

namespace Parity.Tests.Integration;

[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresStoreTests(IntegrationFixture fixture)
{
    private async Task<PostgresSearchStore> StoreAsync(string config = PostgresSchema.CustomConfigName) =>
        new($"mini-postgres-{config}", await fixture.MiniPostgresAsync(config), config);

    private static SearchHit Hit(IReadOnlyList<SearchHit> hits, string key) => hits.Single(h => h.ChunkId == MiniCorpus.Id(key));

    [Fact]
    public async Task SeedingTwiceRecreatesCleanly()
    {
        await using var store = new PostgresSearchStore("reseed", fixture.PostgresConnectionString("parity_reseed"), PostgresSchema.CustomConfigName);
        for (var run = 0; run < 2; run++)
        {
            await store.EnsureSchemaAsync(CancellationToken.None);
            await store.LoadAsync(MiniCorpus.Chunks, MiniCorpus.CreatedUtc, CancellationToken.None);
            await store.WaitUntilSearchableAsync(CancellationToken.None);
        }

        var result = await store.HybridSearchAsync(MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(5, result.Value.Count);
    }

    [Fact]
    public async Task CustomConfigMatchesWholeWordsWithoutStemming()
    {
        await using var store = await StoreAsync();
        var result = await store.HybridSearchAsync(MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(Hit(result.Value, "m01").TextScore > 0);
        Assert.Equal(0, Hit(result.Value, "m02").TextScore);
        Assert.All(result.Value, h => Assert.Equal(0.7 * h.VectorScore + 0.3 * h.TextScore, h.Score, 9));
    }

    [Fact]
    public async Task EnglishConfigStems()
    {
        await using var store = await StoreAsync("english");
        var result = await store.HybridSearchAsync(MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1), CancellationToken.None);
        Assert.True(Hit(result.Value!, "m02").TextScore > 0); // "locks" stems to "lock"
    }

    [Fact]
    public async Task NotebookScopeReturnsOnlyNotebookChunks()
    {
        await using var store = await StoreAsync();
        var result = await store.HybridSearchAsync(MiniCorpus.Query("conversation", MiniCorpus.ScopeN1), CancellationToken.None);
        Assert.Equal(new[] { MiniCorpus.Id("m02") }, result.Value!.Select(h => h.ChunkId));
    }

    // Observed on the real engine (parity_english_nostem): every awkward query succeeds. phraseto_tsquery drops
    // stop words and punctuation ("don't" becomes nothing, "O'Reilly" becomes 'o' <-> 'reilly'), and a
    // stop-word-only or punctuation-only phrase yields an empty tsquery that matches nothing (a NOTICE, not
    // an error), so text scores are all zero. Value = the chunk expected to be the only one with a text match.
    [Theory]
    [InlineData("O'Reilly Media", "m03")]
    [InlineData("don't restart", "m03")]
    [InlineData("say \"hello world\" twice", "m04")]
    [InlineData("the and of", null)]
    [InlineData("!!! ???", null)]
    public async Task AwkwardQueriesSucceedWithTheObservedTextMatches(string text, string? matchedKey)
    {
        await using var store = await StoreAsync();
        var result = await store.HybridSearchAsync(MiniCorpus.Query(text, MiniCorpus.ScopeP1), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(5, result.Value.Count);
        var matched = result.Value.Where(h => h.TextScore > 0).Select(h => h.ChunkId).ToList();
        Assert.Equal(matchedKey is null ? [] : new List<Guid> { MiniCorpus.Id(matchedKey) }, matched);
    }

    [Fact]
    public async Task ProductionTopNCutDropsTheInScopeMatchAndTheVariantKeepsIt()
    {
        await using var store = await StoreAsync();
        var asIs = await store.HybridSearchAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1), CancellationToken.None);
        var fixedVariant = await store.HybridSearchAsync(MiniCorpus.Query("alpha beta", MiniCorpus.ScopeP1, filterBeforeTopN: true), CancellationToken.None);

        Assert.Equal(0, Hit(asIs.Value!, "m17").TextScore);
        Assert.True(Hit(fixedVariant.Value!, "m17").TextScore > 0);
    }

    [Fact]
    public async Task BlankQueriesReturnNothingLikeProduction()
    {
        await using var store = await StoreAsync();
        Assert.Empty((await store.HybridSearchAsync(MiniCorpus.Query(" ", MiniCorpus.ScopeP1), CancellationToken.None)).Value!);
    }
}
