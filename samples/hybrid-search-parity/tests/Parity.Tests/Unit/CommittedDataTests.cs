using Parity.Core;
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
