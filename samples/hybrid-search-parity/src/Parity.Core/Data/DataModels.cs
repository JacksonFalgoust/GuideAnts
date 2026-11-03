using Parity.Core.Model;

namespace Parity.Core.Data;

/// <summary>One line of data/chunks.jsonl. The vector lives in data/embeddings.f32 at the same position.</summary>
public sealed record ChunkLine(
    Guid Id,
    string IndexName,
    Guid ProjectId,
    Guid? NotebookId,
    string DocumentId,
    Guid ContentFileId,
    string SourcePath,
    int ChunkIndex,
    ChunkKind Kind,
    string Content);

public sealed record EdgeCaseDefinition(string Id, string Content);

public sealed record QueryDefinition(string Id, string Group, string Text);

public static class QueryGroups
{
    public const string Exact = "exact";
    public const string Paraphrase = "paraphrase";
    public const string Edge = "edge";
    public const string Error = "error";
    public const string Stress = "stress";

    public static readonly string[] All = [Exact, Paraphrase, Edge, Error, Stress];

    public static bool HasGroundTruth(string group) => group is Exact or Edge;
}

public sealed record Manifest(
    string CorpusCommit,
    DateTimeOffset GeneratedUtc,
    string Model,
    int Dimensions,
    int ChunkCount,
    int QueryCount,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyDictionary<string, string> ProductionSources);

public sealed record SavedQuery(string Id, string Group, string Text, float[] Vector);

public sealed record GeneratedData(
    string CorpusCommit,
    DateTimeOffset GeneratedUtc,
    string Model,
    IReadOnlyList<ChunkRecord> Chunks,
    IReadOnlyList<QueryDefinition> Queries,
    IReadOnlyList<float[]> QueryVectors,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> GroundTruth,
    IReadOnlyDictionary<string, string> ProductionSources);
