using Parity.Core.Contracts;
using Parity.Core.Corpus;
using Parity.Core.Model;

namespace Parity.Tests.Integration;

/// <summary>
/// Production splices the phrase into its SQL as a non-N literal, which the database's Latin-1 collation converts
/// to the code page. The SQL Server diagnostics must go through the same conversion, so what they report is
/// what production's own query searches for.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SqlServerCodePageTests(IntegrationFixture fixture)
{
    private static HybridQuery QueryFor(string text, float[] vector, Scope scope) =>
        new(text, vector, scope, MaxResults: 50, VectorWeight: 0.7, TextWeight: 0.3, CandidateMultiplier: 2);

    private async Task<(HashSet<Guid> Diagnostic, HashSet<Guid> Production)> MatchSetsAsync(string text, float[] vector)
    {
        var (baseline, candidate) = await fixture.FullEnginesAsync();
        await using (baseline)
        await using (candidate)
        {
            var diagnostic = new HashSet<Guid>();
            var production = new HashSet<Guid>();
            foreach (var scope in ParityScopes.All)
            {
                diagnostic.UnionWith((await baseline.Diagnostics.TextMatchAsync(text, scope.Scope, CancellationToken.None)).Value!.Select(m => m.ChunkId));
                var hits = (await baseline.Store.HybridSearchAsync(QueryFor(text, vector, scope.Scope), CancellationToken.None)).Value!;
                production.UnionWith(hits.Where(h => h.TextScore > 0).Select(h => h.ChunkId));
            }

            return (diagnostic, production);
        }
    }

    [Fact]
    public async Task DiagnosticsAgreeWithProductionForTextOutsideTheCodePage()
    {
        var data = fixture.FullData.Value;
        var query = data.Queries.Single(q => q.Id == "Q34");
        Assert.Equal("テキスト検索", query.Text);

        var (diagnostic, production) = await MatchSetsAsync(query.Text, query.Vector);

        Assert.True(diagnostic.SetEquals(production), $"diagnostics {diagnostic.Count} matches, production {production.Count} chunks with a text score");
    }

    [Fact]
    public async Task AnAccentedPhraseInTheCodePageStillMatches()
    {
        var data = fixture.FullData.Value;
        var target = data.Chunks.Single(c => c.Content.Contains("café crème"));
        var (diagnostic, production) = await MatchSetsAsync("café crème", target.Embedding);

        Assert.Contains(target.Id, diagnostic);
        Assert.Contains(target.Id, production);
        Assert.True(diagnostic.SetEquals(production));
    }
}
