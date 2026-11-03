namespace Parity.Core.Model;

public enum ChunkKind { Doc, Edge }

/// <summary>One chunk exactly as both databases store it. IDs are deterministic (see DeterministicGuid).</summary>
public sealed record ChunkRecord(
    Guid Id,
    string IndexName,
    Guid ProjectId,
    Guid? NotebookId,
    string DocumentId,
    Guid ContentFileId,
    string SourcePath,
    int ChunkIndex,
    ChunkKind Kind,
    string Content,
    float[] Embedding);
