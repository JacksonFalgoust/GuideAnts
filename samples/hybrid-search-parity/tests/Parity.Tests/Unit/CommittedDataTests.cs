using Parity.Core;
using Parity.Core.Comparison;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Model;
using Parity.Core.Production;

namespace Parity.Tests.Unit;

/// <summary>The committed data/ must be exactly what generate-data builds from the pinned corpus.</summary>
public class CommittedDataTests
{
    private static readonly string Root = RepoPaths.FindSampleRoot();
    private static readonly Lazy<DataSet> Data = new(() => DataStore.Load(Root));

    private static IReadOnlyList<ChunkDraft> FreshDrafts() =>
        CorpusBuilder.Build(new GitDocsSource(RepoPaths.FindRepoRoot(), ProductionSources.Commit).ReadDocs(), DataStore.ReadEdgeCases(Root));

    /// <summary>
    /// Reviews are looked up by query, scope and kind, so for each candidate profile at most one entry may apply to a
    /// given difference; a second would be silently ignored. Q33 is reviewed once per candidate.
    /// </summary>
    [Theory]
    [InlineData("postgres")]
    [InlineData("prod-postgres")]
    public void CommittedKnownDifferencesHaveOneEntryPerDifferenceForEachCandidate(string candidate)
    {
        var entries = KnownDifferences.ForCandidate(KnownDifferences.Load(Path.Combine(Root, "known-differences.json")), candidate);
        var duplicates = entries.GroupBy(e => (e.QueryId, e.Scope, e.Kind)).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.Empty(duplicates);
        Assert.Equal(26, entries.Count);
        Assert.Equal(4, entries.Count(e => e.QueryId == "Q33"));
        Assert.All(entries.Where(e => e.QueryId == "Q33"), e => Assert.Equal(candidate, e.Candidate));
    }

    [Fact]
    public void ManifestRecordsThePinnedCorpusModelAndSources()
    {
        var m = Data.Value.Manifest;
        Assert.StartsWith(ProductionSources.Commit, m.CorpusCommit);
        Assert.Contains(m.Model, new[] { "text-embedding-3-small", "openai/text-embedding-3-small" });
        Assert.Equal((1536, 38), (m.Dimensions, m.QueryCount));
        Assert.Equal(ProductionSources.Sha256AtCommit.OrderBy(kv => kv.Key), m.ProductionSources.OrderBy(kv => kv.Key));
    }

    [Fact]
    public void ChunksAreExactlyTheCorpusBuiltFromGit()
    {
        var fresh = FreshDrafts();
        Assert.Equal(fresh.Select(d => (d.Id, d.Content, d.IndexName, d.NotebookId)), Data.Value.Chunks.Select(c => (c.Id, c.Content, c.IndexName, c.NotebookId)));
        Assert.Equal(20, Data.Value.Chunks.Count(c => c.Kind == ChunkKind.Edge));
        Assert.InRange(Data.Value.Chunks.Count, 1100, 1300);
    }

    [Fact]
    public void VectorsAreRealUnitLengthEmbeddings()
    {
        foreach (var vector in Data.Value.Chunks.Select(c => c.Embedding).Concat(Data.Value.Queries.Select(q => q.Vector)))
        {
            var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
            Assert.InRange(norm, 0.999, 1.001);
            Assert.Contains(vector, v => v < 0);
        }
    }

    [Fact]
    public void GroundTruthMatchesAFreshComputation()
    {
        var fresh = GroundTruth.Compute(DataStore.ReadQueries(Root), FreshDrafts());
        Assert.Equal(fresh.Keys.Order(), Data.Value.GroundTruth.Keys.Order());
        foreach (var (id, matches) in fresh)
        {
            Assert.Equal(matches, Data.Value.GroundTruth[id]);
        }
    }
}
