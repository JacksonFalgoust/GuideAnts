namespace Parity.Core.Model;

/// <summary>Same fields as production's HybridSearchResult.</summary>
public sealed record SearchHit(
    Guid ChunkId,
    string Content,
    Guid? ContentFileId,
    Guid? NotebookFileId,
    int ChunkIndex,
    string IndexName,
    double VectorScore,
    double TextScore,
    double Score);

public sealed record VectorHit(Guid ChunkId, double Score);

/// <summary>A full-text match. Rank is on the same 0–1 scale the hybrid query uses for TextScore.</summary>
public sealed record TextMatch(Guid ChunkId, double Rank);

/// <summary>
/// What the hybrid query computes for one chunk. RawRank is its text rank had it been a candidate
/// (null when it doesn't match). NthCandidateRank is the lowest rank in a full candidate set
/// (null when fewer than CandidateLimit chunks matched, so nothing was cut).
/// </summary>
public sealed record ChunkScore(
    Guid ChunkId,
    double VectorScore,
    double TextScore,
    bool Matched,
    double? RawRank,
    bool InCandidates,
    double? NthCandidateRank);

public sealed record Token(string Text, string? Normalized, string Kind, bool IsStopWord);

public sealed record EngineInfo(string Provider, string Version, IReadOnlyDictionary<string, string> Details);

public sealed record IndexCoverage(int Rows, int IndexedRows);
