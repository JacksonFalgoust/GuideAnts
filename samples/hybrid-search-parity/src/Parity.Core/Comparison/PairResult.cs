using Parity.Core.Model;

namespace Parity.Core.Comparison;

public sealed record QueryRef(string Id, string Group, string Text);

/// <summary>
/// The first error each engine hit for a query/scope (text match, then hybrid). <see cref="BaselineHybrid"/> and
/// <see cref="CandidateHybrid"/> record the two hybrid calls' errors separately, because a pair where both engines'
/// hybrid calls failed looks identical to a user (empty results) while any other both-sided error does not.
/// </summary>
public sealed record ErrorOutcome(EngineError? Baseline, EngineError? Candidate, EngineError? BaselineHybrid = null, EngineError? CandidateHybrid = null)
{
    public string Side => (Baseline, Candidate) switch
    {
        (not null, not null) => "both",
        (not null, null) => "baseline",
        (null, not null) => "candidate",
        _ => "none",
    };

    public bool IsOneSided => Side is "baseline" or "candidate";

    public bool BothHybridFailed => BaselineHybrid is not null && CandidateHybrid is not null;

    /// <summary>One-sided errors, and both-sided ones unless production's visible behaviour (empty results) is identical.</summary>
    public bool NeedsReview => IsOneSided || (Side == "both" && !BothHybridFailed);

    /// <summary>The error code; a both-sided error is "&lt;baseline code&gt;/&lt;candidate code&gt;".</summary>
    public string Code => Side == "both"
        ? $"{Baseline!.Code}/{Candidate!.Code}"
        : (Baseline ?? Candidate)?.Code ?? string.Empty;
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
