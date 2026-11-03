using Parity.Core.Model;

namespace Parity.Core.Comparison;

public sealed record TextDiff(IReadOnlyList<Guid> BaselineOnly, IReadOnlyList<Guid> CandidateOnly)
{
    public bool IsEqual => BaselineOnly.Count == 0 && CandidateOnly.Count == 0;
}

/// <summary>Spec section 7.2, rule 2: the two match sets must be equal.</summary>
public static class TextParity
{
    public static TextDiff Diff(IReadOnlyList<TextMatch> baseline, IReadOnlyList<TextMatch> candidate)
    {
        var a = baseline.Select(m => m.ChunkId).ToHashSet();
        var b = candidate.Select(m => m.ChunkId).ToHashSet();
        return new TextDiff(a.Except(b).Order().ToList(), b.Except(a).Order().ToList());
    }
}
