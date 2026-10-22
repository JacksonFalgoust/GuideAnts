using System.Text.Json;
using Parity.Core.Data;

namespace Parity.Core.Comparison;

public sealed record KnownDifference(
    string QueryId,
    string Scope,
    string Kind,
    IReadOnlyList<Guid>? BaselineOnly,
    IReadOnlyList<Guid>? CandidateOnly,
    string? ErrorSide,
    string? ErrorCode,
    string Category,
    string Reason,
    string Decision,
    string? ReviewedBy,
    string? ReviewedOn);

public enum ReviewOutcome { Unreviewed, Acceptable, Blocker, SnapshotMismatch }

public sealed record ReviewStatus(ReviewOutcome Outcome, KnownDifference? Entry)
{
    /// <summary>Anything other than an accepted, matching entry keeps the verdict from passing.</summary>
    public bool IsOpen => Outcome != ReviewOutcome.Acceptable;
}

/// <summary>Spec section 7.4: reviewed differences, matched only against their exact snapshot.</summary>
public static class KnownDifferences
{
    private static readonly HashSet<string> Kinds = ["text-match", "error"];
    private static readonly HashSet<string> Categories = ["tokenization", "stop-words", "punctuation", "accents", "error-handling", "other"];
    private static readonly HashSet<string> Decisions = ["acceptable", "blocker"];

    public static IReadOnlyList<KnownDifference> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        List<KnownDifference> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<KnownDifference>>(File.ReadAllText(path), ParityJson.Options) ?? [];
        }
        catch (JsonException ex)
        {
            throw new ParityConfigurationException($"{Path.GetFileName(path)} isn't valid JSON: {ex.Message}", ex);
        }

        foreach (var e in entries)
        {
            if (!Kinds.Contains(e.Kind) || !Categories.Contains(e.Category) || !Decisions.Contains(e.Decision))
            {
                throw new ParityConfigurationException(
                    $"known-differences.json entry {e.QueryId}/{e.Scope} has kind '{e.Kind}', category '{e.Category}', decision '{e.Decision}'. " +
                    $"Valid kinds: {string.Join(", ", Kinds)}; categories: {string.Join(", ", Categories)}; decisions: {string.Join(", ", Decisions)}.");
            }
        }

        return entries;
    }

    public static ReviewStatus ReviewText(IReadOnlyList<KnownDifference> entries, string queryId, string scope, TextDiff diff)
    {
        var entry = entries.FirstOrDefault(e => e.Kind == "text-match" && e.QueryId == queryId && e.Scope == scope);
        if (entry is null)
        {
            return new ReviewStatus(ReviewOutcome.Unreviewed, null);
        }

        var matches = SameSet(entry.BaselineOnly, diff.BaselineOnly) && SameSet(entry.CandidateOnly, diff.CandidateOnly);
        return new ReviewStatus(matches ? Decided(entry) : ReviewOutcome.SnapshotMismatch, entry);
    }

    /// <summary>Null when no review is needed (no error, or both engines failed).</summary>
    public static ReviewStatus? ReviewError(IReadOnlyList<KnownDifference> entries, string queryId, string scope, ErrorOutcome error)
    {
        if (!error.IsOneSided)
        {
            return null;
        }

        var entry = entries.FirstOrDefault(e => e.Kind == "error" && e.QueryId == queryId && e.Scope == scope);
        if (entry is null)
        {
            return new ReviewStatus(ReviewOutcome.Unreviewed, null);
        }

        var matches = entry.ErrorSide == error.Side && entry.ErrorCode == error.Code;
        return new ReviewStatus(matches ? Decided(entry) : ReviewOutcome.SnapshotMismatch, entry);
    }

    /// <summary>Entries that no longer correspond to any observed difference. They're reported, never failing.</summary>
    public static IReadOnlyList<KnownDifference> FindStale(IReadOnlyList<KnownDifference> entries, IReadOnlyList<PairResult> pairs) =>
        entries.Where(e => !pairs.Any(p =>
                p.Query.Id == e.QueryId && p.Scope == e.Scope &&
                (e.Kind == "text-match" ? p.Text is { IsEqual: false } : p.Error is { IsOneSided: true })))
            .ToList();

    private static ReviewOutcome Decided(KnownDifference entry) =>
        entry.Decision == "acceptable" ? ReviewOutcome.Acceptable : ReviewOutcome.Blocker;

    private static bool SameSet(IReadOnlyList<Guid>? recorded, IReadOnlyList<Guid> observed) =>
        (recorded ?? Array.Empty<Guid>()).ToHashSet().SetEquals(observed);
}
