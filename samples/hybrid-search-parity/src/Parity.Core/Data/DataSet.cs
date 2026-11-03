using Parity.Core.Model;

namespace Parity.Core.Data;

public sealed class DataSet
{
    private readonly Dictionary<Guid, ChunkRecord> _chunksById;

    public DataSet(
        Manifest manifest,
        IReadOnlyList<ChunkRecord> chunks,
        IReadOnlyList<SavedQuery> queries,
        IReadOnlyDictionary<string, IReadOnlyList<Guid>> groundTruth)
    {
        Manifest = manifest;
        Chunks = chunks;
        Queries = queries;
        GroundTruth = groundTruth;
        _chunksById = chunks.ToDictionary(c => c.Id);
    }

    public Manifest Manifest { get; }
    public IReadOnlyList<ChunkRecord> Chunks { get; }
    public IReadOnlyList<SavedQuery> Queries { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<Guid>> GroundTruth { get; }

    public ChunkRecord? FindChunk(Guid id) => _chunksById.GetValueOrDefault(id);

    /// <summary>A saved query whose text equals <paramref name="text"/> after trimming (case-sensitive).</summary>
    public SavedQuery? FindQueryByText(string text) =>
        Queries.FirstOrDefault(q => string.Equals(q.Text, text.Trim(), StringComparison.Ordinal));
}
