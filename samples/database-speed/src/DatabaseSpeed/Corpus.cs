using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatabaseSpeed;

public sealed record CorpusChunk(
    Guid Id,
    Guid ProjectId,
    Guid? NotebookId,
    string DocumentId,
    Guid ContentFileId,
    string SourcePath,
    int ChunkIndex,
    string Content,
    float[] Embedding);

public sealed record SavedQuery(string Id, string Group, string Text, float[] Vector);

/// <summary>
/// The demo corpus committed with the hybrid-search parity sample (<c>samples/hybrid-search-parity/data</c>): the
/// chunks with their saved embeddings and the saved query embeddings, so no embeddings API is called. The files are
/// checked against <c>data/manifest.json</c>'s SHA-256 hashes.
/// </summary>
public sealed class Corpus
{
    public const int Dimensions = 1536;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private Corpus(IReadOnlyList<CorpusChunk> chunks, IReadOnlyList<SavedQuery> queries, string manifestCommit)
    {
        Chunks = chunks;
        Queries = queries;
        ManifestCommit = manifestCommit;
    }

    public IReadOnlyList<CorpusChunk> Chunks { get; }

    public IReadOnlyList<SavedQuery> Queries { get; }

    /// <summary>The docs commit the corpus was built from (<c>corpusCommit</c> in the manifest).</summary>
    public string ManifestCommit { get; }

    public SavedQuery Query(string id) =>
        Queries.SingleOrDefault(q => q.Id == id) ?? throw new InvalidOperationException($"Saved query {id} isn't in inputs/queries.json.");

    /// <param name="paritySampleRoot">The <c>samples/hybrid-search-parity</c> directory.</param>
    public static Corpus Load(string paritySampleRoot)
    {
        var manifestPath = Path.Combine(paritySampleRoot, "data", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"{manifestPath} not found; the speed harness reads the parity sample's committed corpus.");
        }

        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), Json)
            ?? throw new InvalidOperationException("data/manifest.json is empty.");
        if (manifest.Dimensions != Dimensions)
        {
            throw new InvalidOperationException($"The corpus has {manifest.Dimensions}-dimensional embeddings; production uses {Dimensions}.");
        }

        foreach (var (file, expected) in manifest.Files)
        {
            var path = Path.Combine(paritySampleRoot, file);
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (actual != expected)
            {
                throw new InvalidOperationException($"{file} doesn't match data/manifest.json (sha256 {actual[..12]}..., expected {expected[..12]}...).");
            }
        }

        var lines = File.ReadLines(Path.Combine(paritySampleRoot, "data", "chunks.jsonl"))
            .Where(l => l.Length > 0)
            .Select(l => JsonSerializer.Deserialize<ChunkLine>(l, Json) ?? throw new InvalidOperationException("Empty chunk line."))
            .ToList();
        var vectors = ReadVectors(Path.Combine(paritySampleRoot, "data", "embeddings.f32"), lines.Count);
        var chunks = lines
            .Select((l, i) => new CorpusChunk(l.Id, l.ProjectId, l.NotebookId, l.DocumentId, l.ContentFileId, l.SourcePath, l.ChunkIndex, l.Content, vectors[i]))
            .ToList();

        var definitions = JsonSerializer.Deserialize<List<QueryLine>>(File.ReadAllText(Path.Combine(paritySampleRoot, "inputs", "queries.json")), Json)
            ?? throw new InvalidOperationException("inputs/queries.json is empty.");
        var queryVectors = ReadVectors(Path.Combine(paritySampleRoot, "data", "query-embeddings.f32"), definitions.Count);
        var queries = definitions.Select((q, i) => new SavedQuery(q.Id, q.Group, q.Text, queryVectors[i])).ToList();

        if (chunks.Count != manifest.ChunkCount || queries.Count != manifest.QueryCount)
        {
            throw new InvalidOperationException(
                $"The corpus has {chunks.Count} chunks and {queries.Count} queries; the manifest says {manifest.ChunkCount} and {manifest.QueryCount}.");
        }

        return new Corpus(chunks, queries, manifest.CorpusCommit);
    }

    /// <summary>Little-endian float32, <see cref="Dimensions"/> per vector, no header (the parity sample's format).</summary>
    private static float[][] ReadVectors(string path, int count)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length != (long)count * Dimensions * 4)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} has {bytes.Length} bytes; expected {count} x {Dimensions} x 4.");
        }

        var result = new float[count][];
        for (var i = 0; i < count; i++)
        {
            var vector = new float[Dimensions];
            for (var d = 0; d < Dimensions; d++)
            {
                vector[d] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(((i * Dimensions) + d) * 4, 4));
            }

            result[i] = vector;
        }

        return result;
    }

    private sealed record Manifest(string CorpusCommit, int Dimensions, int ChunkCount, int QueryCount, Dictionary<string, string> Files);

    private sealed record ChunkLine(
        Guid Id, string IndexName, Guid ProjectId, Guid? NotebookId, string DocumentId, Guid ContentFileId, string SourcePath, int ChunkIndex, string Kind, string Content);

    private sealed record QueryLine(string Id, string Group, string Text);
}
