using Parity.Core.Contracts;
using Parity.Core.Data;
using Parity.Core.Model;

namespace Parity.Tests.Support;

public static class TestData
{
    public static DataSet DataSet(IReadOnlyList<ChunkRecord> chunks, params (string Id, string Group, string Text)[] queries) =>
        new(
            new Manifest("test0000", DateTimeOffset.Parse("2026-10-20T00:00:00Z"), "fake-hash", VectorFile.Dimensions, chunks.Count, queries.Length,
                new Dictionary<string, string>(), new Dictionary<string, string>()),
            chunks,
            queries.Select(q => new SavedQuery(q.Id, q.Group, q.Text, HashEmbedding.Create(q.Text))).ToList(),
            new Dictionary<string, IReadOnlyList<Guid>>());

    /// <summary>An in-memory engine already loaded with <paramref name="chunks"/>, wrapped as a profile.</summary>
    public static SearchEngine Engine(string profile, InMemoryEngine engine, IReadOnlyList<ChunkRecord> chunks)
    {
        engine.LoadAsync(chunks, DateTime.UtcNow, CancellationToken.None).GetAwaiter().GetResult();
        return new SearchEngine(profile, "InMemory", engine, engine);
    }
}
