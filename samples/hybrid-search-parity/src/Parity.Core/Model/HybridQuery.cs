namespace Parity.Core.Model;

/// <summary>
/// Production's HybridSearcher.SearchAsync inputs. FilterBeforeTopN = true is the informational
/// "quirk fixed" variant and is never production behaviour.
/// </summary>
public sealed record HybridQuery(
    string Text,
    float[] Vector,
    Scope Scope,
    int MaxResults,
    double VectorWeight,
    double TextWeight,
    int CandidateMultiplier,
    bool FilterBeforeTopN = false)
{
    public int CandidateLimit => MaxResults * CandidateMultiplier;
}
