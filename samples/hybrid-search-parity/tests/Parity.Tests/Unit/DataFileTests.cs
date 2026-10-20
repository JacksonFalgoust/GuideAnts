using Parity.Core;
using Parity.Core.Data;
using Parity.Core.Model;

namespace Parity.Tests.Unit;

public sealed class DataFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"parity-data-{Guid.NewGuid():N}");

    public DataFileTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "inputs"));
        File.WriteAllText(Path.Combine(_root, DataStore.EdgeCasesFile), """[ { "id": "E01", "content": "edge text" } ]""");
        File.WriteAllText(Path.Combine(_root, DataStore.QueriesFile), """[ { "id": "Q01", "group": "exact", "text": "edge text" } ]""");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void VectorsRoundTripBitForBit()
    {
        var special = new[] { 0f, -0f, float.Epsilon, 1e-38f, float.MaxValue, float.MinValue, 0.1f, -0.333333343f };
        var vector = Enumerable.Range(0, VectorFile.Dimensions).Select(i => special[i % special.Length] * (i % 7 == 0 ? -1 : 1)).ToArray();
        var path = Path.Combine(_root, "v.f32");

        VectorFile.Write(path, [vector, vector.Reverse().ToArray()]);
        var read = VectorFile.Read(path, 2);

        Assert.Equal(vector.Select(BitConverter.SingleToInt32Bits), read[0].Select(BitConverter.SingleToInt32Bits));
        Assert.Equal(VectorFile.Dimensions * 4 * 2, new FileInfo(path).Length);
    }

    [Fact]
    public void RejectsWrongDimensionsNonFiniteValuesAndWrongLength()
    {
        var path = Path.Combine(_root, "bad.f32");
        Assert.Throws<DataFileException>(() => VectorFile.Write(path, [new float[3]]));
        var withNaN = new float[VectorFile.Dimensions];
        withNaN[5] = float.NaN;
        Assert.Throws<DataFileException>(() => VectorFile.Write(path, [withNaN]));

        VectorFile.Write(path, [new float[VectorFile.Dimensions]]);
        var error = Assert.Throws<DataFileException>(() => VectorFile.Read(path, 2));
        Assert.Contains("expected", error.Message);
    }

    [Fact]
    public void WrittenDataLoadsBackIdentically()
    {
        DataStore.WriteAtomically(_root, SampleData(), CancellationToken.None);
        var data = DataStore.Load(_root);

        var chunk = Assert.Single(data.Chunks);
        Assert.Equal("edge text", chunk.Content);
        Assert.Equal(ChunkKind.Edge, chunk.Kind);
        Assert.Equal(0.25f, chunk.Embedding[0]);
        var query = Assert.Single(data.Queries);
        Assert.Equal(("Q01", "exact", "edge text"), (query.Id, query.Group, query.Text));
        Assert.Equal(0.5f, query.Vector[0]);
        Assert.Equal(new[] { chunk.Id }, data.GroundTruth["Q01"]);
        Assert.Equal("abc1234", data.Manifest.CorpusCommit);
        Assert.Same(chunk, data.FindChunk(chunk.Id));
        Assert.Same(query, data.FindQueryByText("  edge text "));
        Assert.Null(data.FindQueryByText("Edge Text"));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "data")));
    }

    [Fact]
    public void LoadRefusesTamperedDataAndChangedInputs()
    {
        DataStore.WriteAtomically(_root, SampleData(), CancellationToken.None);
        File.AppendAllText(Path.Combine(_root, DataStore.QueriesFile), " ");

        var error = Assert.Throws<DataFileException>(() => DataStore.Load(_root));
        Assert.Contains("inputs/queries.json doesn't match data/manifest.json", error.Message);
    }

    [Fact]
    public void LoadExplainsMissingData()
    {
        var error = Assert.Throws<DataFileException>(() => DataStore.Load(_root));
        Assert.Contains("generate-data", error.Message);
    }

    [Fact]
    public void PreCancelledWriteLeavesNothingBehind()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => DataStore.WriteAtomically(_root, SampleData(), cts.Token));
        Assert.False(File.Exists(Path.Combine(_root, DataStore.ManifestFile)));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "data")));
    }

    private static GeneratedData SampleData()
    {
        var vector = new float[VectorFile.Dimensions];
        vector[0] = 0.25f;
        var queryVector = new float[VectorFile.Dimensions];
        queryVector[0] = 0.5f;
        var chunk = new ChunkRecord(Guid.NewGuid(), "idx", Guid.NewGuid(), null, "edge:E01", Guid.NewGuid(), "inputs/edge-cases.json#E01", 0, ChunkKind.Edge, "edge text", vector);
        return new GeneratedData(
            "abc1234",
            DateTimeOffset.Parse("2026-10-20T00:00:00Z"),
            "fake",
            [chunk],
            [new QueryDefinition("Q01", "exact", "edge text")],
            [queryVector],
            new Dictionary<string, IReadOnlyList<Guid>> { ["Q01"] = [chunk.Id] },
            new Dictionary<string, string> { ["x.cs"] = "hash" });
    }
}
