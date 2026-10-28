using Parity.Core.Comparison;
using Parity.Core.Data;
using Parity.Core.Model;
using Parity.Core.Settings;

namespace Parity.Core.Reporting;

public sealed record RunContext(
    DateTimeOffset RunAtUtc,
    string SampleCommit,
    bool SampleDirty,
    string BaselineProfile,
    string CandidateProfile,
    EngineInfo BaselineInfo,
    EngineInfo CandidateInfo,
    Manifest Manifest,
    ThresholdSettings Thresholds,
    double VectorTolerance,
    double TieTolerance,
    string SqlServerImage,
    string SqlServerImageDigest,
    string PostgresImage,
    string PostgresImageDigest,
    string KnownDifferencesSha256,
    IReadOnlyList<string> SettingsDifferences);

public sealed record ComparisonReport(
    RunContext Context,
    IReadOnlyList<PairResult> Pairs,
    VerdictResult Verdict,
    InformationalSummary QuirkFixed,
    IReadOnlyList<InformationalSummary> Profiles,
    IReadOnlyList<KnownDifference> StaleEntries,
    IReadOnlyDictionary<Guid, string> ChunkLabels)
{
    public string Label(Guid id) => ChunkLabels.TryGetValue(id, out var label) ? label : id.ToString()[..8];

    public static string Status(ReviewStatus? status) => status?.Outcome switch
    {
        ReviewOutcome.Acceptable => "acceptable",
        ReviewOutcome.Blocker => "BLOCKER",
        ReviewOutcome.SnapshotMismatch => "SNAPSHOT CHANGED",
        _ => "UNREVIEWED",
    };

    public static string Kebab(DifferenceCategory category) => category switch
    {
        DifferenceCategory.ScoredDifferently => "scored-differently",
        DifferenceCategory.MatchedOneSide => "matched-one-side",
        DifferenceCategory.TopNCut => "top-n-cut",
        DifferenceCategory.TopNBoundaryTie => "top-n-boundary-tie",
        DifferenceCategory.Displaced => "displaced",
        _ => "unexplained",
    };
}
