using Parity.Core.Model;

namespace Parity.Core.Comparison;

public sealed record QueryRef(string Id, string Group, string Text);

public sealed record ErrorOutcome(EngineError? Baseline, EngineError? Candidate)
{
    public string Side => (Baseline, Candidate) switch
    {
        (not null, not null) => "both",
        (not null, null) => "baseline",
        (null, not null) => "candidate",
        _ => "none",
    };

    public bool IsOneSided => Side is "baseline" or "candidate";

    public string Code => (Baseline ?? Candidate)?.Code ?? string.Empty;
}

public sealed record SnippetEvidence(
    Guid ChunkId,
    string Side,
    string Snippet,
    IReadOnlyList<Token> BaselineTokens,
    IReadOnlyList<Token> CandidateTokens);

public sealed record TextEvidence(
    IReadOnlyList<Token> BaselineQueryTokens,
    IReadOnlyList<Token> CandidateQueryTokens,
    IReadOnlyList<SnippetEvidence> Snippets);

/// <summary>
/// Everything compare learned about one query in one scope. Hybrid is null when either engine
/// returned an error, which keeps the pair out of the overlap and same-#1 metrics.
/// </summary>
public sealed record PairResult(
    QueryRef Query,
    string Scope,
    VectorParityResult Vector,
    TextDiff? Text,
    ReviewStatus? TextReview,
    TextEvidence? Evidence,
    ErrorOutcome? Error,
    ReviewStatus? ErrorReview,
    HybridPairMetrics? Hybrid,
    IReadOnlyList<RankDifference> Differences,
    IReadOnlyList<SearchHit> BaselineHits,
    IReadOnlyList<SearchHit> CandidateHits);
