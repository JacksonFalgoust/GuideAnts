using Parity.Core.Comparison;
using Parity.Core.Model;
using Parity.Postgres;
using Parity.SqlServer;
using Parity.Tests.Support;

namespace Parity.Tests.Integration;

/// <summary>Both engines, the same mini corpus: rule 1 must hold, and plain-word phrases must match identically.</summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MiniParityTests(IntegrationFixture fixture)
{
    public static TheoryData<string> Vectors => new() { "chunk:m01", "chunk:m05", "query:conversation lock" };

    [Theory]
    [MemberData(nameof(Vectors))]
    public async Task VectorParityHoldsInEveryScope(string source)
    {
        var vector = source.StartsWith("chunk:", StringComparison.Ordinal)
            ? MiniCorpus.Chunks.Single(c => c.Id == MiniCorpus.Id(source[6..])).Embedding
            : HashEmbedding.Create(source[6..]);
        var sql = new SqlServerSearchDiagnostics("mini-sqlserver", await fixture.MiniSqlServerAsync());
        await using var pg = new PostgresSearchDiagnostics("mini-postgres", await fixture.MiniPostgresAsync(), PostgresSchema.CustomConfigName);

        foreach (var scope in new[] { MiniCorpus.ScopeP1, MiniCorpus.ScopeP2, MiniCorpus.ScopeN1 })
        {
            var a = (await sql.VectorSearchAsync(vector, scope, 10, CancellationToken.None)).Value!;
            var b = (await pg.VectorSearchAsync(vector, scope, 10, CancellationToken.None)).Value!;
            var result = VectorParity.Compare(a, b, 1e-5, 1e-6);
            Assert.True(result.Pass, result.Failure);
        }
    }

    [Theory]
    [InlineData("alpha beta")]
    [InlineData("conversation lock")]
    public async Task PlainWordPhrasesMatchTheSameChunks(string phrase)
    {
        var sql = new SqlServerSearchDiagnostics("mini-sqlserver", await fixture.MiniSqlServerAsync());
        await using var pg = new PostgresSearchDiagnostics("mini-postgres", await fixture.MiniPostgresAsync(), PostgresSchema.CustomConfigName);

        foreach (var scope in new[] { MiniCorpus.ScopeP1, MiniCorpus.ScopeP2 })
        {
            var diff = TextParity.Diff(
                (await sql.TextMatchAsync(phrase, scope, CancellationToken.None)).Value!,
                (await pg.TextMatchAsync(phrase, scope, CancellationToken.None)).Value!);
            Assert.True(diff.IsEqual, $"baseline-only {diff.BaselineOnly.Count}, candidate-only {diff.CandidateOnly.Count}");
        }
    }

    [Fact]
    public async Task HybridVectorScoresAgree()
    {
        var query = MiniCorpus.Query("conversation lock", MiniCorpus.ScopeP1);
        var sql = await new SqlServerSearchStore("mini-sqlserver", await fixture.MiniSqlServerAsync(), IntegrationFixture.PopulationTimeout).HybridSearchAsync(query, CancellationToken.None);
        await using var pgStore = new PostgresSearchStore("mini-postgres", await fixture.MiniPostgresAsync(), PostgresSchema.CustomConfigName);
        var pg = await pgStore.HybridSearchAsync(query, CancellationToken.None);

        var result = VectorParity.CompareHybridVectorScores(sql.Value!, pg.Value!, 1e-5);
        Assert.True(result.Pass, result.Failure);
    }
}
