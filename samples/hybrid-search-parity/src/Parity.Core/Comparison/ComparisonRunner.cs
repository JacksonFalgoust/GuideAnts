using Parity.Core.Contracts;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Model;
using Parity.Core.Settings;

namespace Parity.Core.Comparison;

public sealed record InformationalSummary(
    string Label,
    bool Available,
    string? UnavailableReason,
    int PairsCounted,
    double MeanOverlap,
    double SameTopOneRate,
    int? ExtraMatches,
    int? LostMatches)
{
    public static InformationalSummary Unavailable(string label, string reason) => new(label, false, reason, 0, 0, 0, null, null);

    /// <summary>
    /// Extra and lost matches count distinct chunk ids across the three project scopes only: P1/N1 is a subset of P1, so
    /// including it would double-count. Pass <paramref name="withMatchCounts"/> false where they'd just repeat rule 2
    /// (the quirk-fixed run's text match has no top-N, so its totals equal the verdict run's).
    /// </summary>
    public static InformationalSummary From(string label, IReadOnlyList<PairResult> pairs, bool withMatchCounts = true)
    {
        var projectPairs = pairs.Where(p => ParityScopes.ProjectLabels.Contains(p.Scope)).ToList();
        var counted = pairs.Where(p => p.Hybrid is not null).Select(p => p.Hybrid!).ToList();
        return new InformationalSummary(
            label,
            true,
            null,
            counted.Count,
            counted.Count == 0 ? 0 : counted.Average(m => m.OverlapAt5),
            counted.Count == 0 ? 0 : counted.Count(m => m.SameTopOne) / (double)counted.Count,
            withMatchCounts ? projectPairs.SelectMany(p => p.Text?.CandidateOnly ?? []).Distinct().Count() : null,
            withMatchCounts ? projectPairs.SelectMany(p => p.Text?.BaselineOnly ?? []).Distinct().Count() : null);
    }
}

/// <summary>Runs every saved query in every scope against both engines and applies rules 1–3.</summary>
public sealed class ComparisonRunner(SearchEngine baseline, SearchEngine candidate, DataSet data, ParitySettings settings, IReadOnlyList<KnownDifference> known)
{
    private const int MaxSnippetsPerPair = 5;

    public async Task<IReadOnlyList<PairResult>> RunAsync(IReadOnlyList<NamedScope> scopes, CancellationToken ct, bool filterBeforeTopN = false, IProgress<string>? progress = null)
    {
        var results = new List<PairResult>();
        var total = data.Queries.Count * scopes.Count;
        foreach (var query in data.Queries)
        {
            foreach (var scope in scopes)
            {
                results.Add(await RunPairAsync(query, scope, ct, filterBeforeTopN));
                if (results.Count % 20 == 0 || results.Count == total)
                {
                    progress?.Report($"{results.Count}/{total} query/scope pairs");
                }
            }
        }

        return results;
    }

    public async Task<PairResult> RunPairAsync(SavedQuery query, NamedScope scope, CancellationToken ct, bool filterBeforeTopN = false)
    {
        var p = settings.Parity;
        var h = settings.Hybrid;
        var queryRef = new QueryRef(query.Id, query.Group, query.Text);

        // Rule 1: vector-only top k. A failure here is infrastructure, not a finding, so it throws.
        var vectorA = (await baseline.Diagnostics.VectorSearchAsync(query.Vector, scope.Scope, p.VectorTopK, ct)).Unwrap($"{baseline.Profile} vector search");
        var vectorB = (await candidate.Diagnostics.VectorSearchAsync(query.Vector, scope.Scope, p.VectorTopK, ct)).Unwrap($"{candidate.Profile} vector search");
        var vector = VectorParity.Compare(vectorA, vectorB, p.VectorTolerance, p.TieTolerance);

        // Rule 2 inputs and the hybrid run.
        var textA = await baseline.Diagnostics.TextMatchAsync(query.Text, scope.Scope, ct);
        var textB = await candidate.Diagnostics.TextMatchAsync(query.Text, scope.Scope, ct);
        var hybridQuery = new HybridQuery(query.Text, query.Vector, scope.Scope, h.MaxResults, h.VectorWeight, h.TextWeight, h.CandidateMultiplier, filterBeforeTopN);
        var hybridA = await baseline.Store.HybridSearchAsync(hybridQuery, ct);
        var hybridB = await candidate.Store.HybridSearchAsync(hybridQuery, ct);

        // Rule 2 is decided whenever both text-match calls succeeded, even if a hybrid call failed:
        // a genuine text difference must never be dropped just because the pair also has an error.
        TextDiff? textDiff = null;
        ReviewStatus? textReview = null;
        TextEvidence? evidence = null;
        if (textA.IsSuccess && textB.IsSuccess)
        {
            textDiff = TextParity.Diff(textA.Value!, textB.Value!);
            if (!textDiff.IsEqual)
            {
                textReview = KnownDifferences.ReviewText(known, query.Id, scope.Label, textDiff);
                evidence = await GatherEvidenceAsync(query.Text, textDiff, ct);
            }
        }

        var error = new ErrorOutcome(textA.Error ?? hybridA.Error, textB.Error ?? hybridB.Error, hybridA.Error, hybridB.Error);
        if (error.Side != "none")
        {
            return new PairResult(queryRef, scope.Label, vector, textDiff, textReview, evidence, error,
                KnownDifferences.ReviewError(known, query.Id, scope.Label, error), null, [],
                hybridA.IsSuccess ? hybridA.Value : [], hybridB.IsSuccess ? hybridB.Value : []);
        }

        var hitsA = hybridA.Value!;
        var hitsB = hybridB.Value!;
        if (vector.Pass)
        {
            vector = VectorParity.CompareHybridVectorScores(hitsA, hitsB, p.VectorTolerance);
        }

        // Rule 3: score every chunk in either top 5 on both engines, then measure and explain.
        var union = hitsA.Take(RankingMetrics.TopN).Concat(hitsB.Take(RankingMetrics.TopN)).Select(x => x.ChunkId).Distinct().ToList();
        var scoresA = (await baseline.Diagnostics.ScoreChunksAsync(hybridQuery, union, ct)).Unwrap($"{baseline.Profile} score chunks").ToDictionary(s => s.ChunkId);
        var scoresB = (await candidate.Diagnostics.ScoreChunksAsync(hybridQuery, union, ct)).Unwrap($"{candidate.Profile} score chunks").ToDictionary(s => s.ChunkId);
        RequireAllScored(baseline.Profile, union, scoresA);
        RequireAllScored(candidate.Profile, union, scoresB);
        var evaluation = RankingMetrics.EvaluatePair(hitsA, hitsB, SideScores(scoresA), SideScores(scoresB), p.TieTolerance);
        var differences = DifferenceClassifier.Explain(evaluation.DifferingChunks, scoresA, scoresB, hitsA, hitsB, p.VectorTolerance, p.TieTolerance)
            .Concat(ScoreConsistency.Check(ScoreConsistency.Baseline, hitsA, scoresA, p.VectorTolerance, p.TieTolerance))
            .Concat(ScoreConsistency.Check(ScoreConsistency.Candidate, hitsB, scoresB, p.VectorTolerance, p.TieTolerance))
            .ToList();
        var checkedHits = hitsA.Take(RankingMetrics.TopN).Count() + hitsB.Take(RankingMetrics.TopN).Count();

        return new PairResult(queryRef, scope.Label, vector, textDiff, textReview, evidence, null, null, evaluation.Metrics, differences, hitsA, hitsB, checkedHits);
    }

    private static void RequireAllScored(string profile, IReadOnlyList<Guid> union, IReadOnlyDictionary<Guid, ChunkScore> scores)
    {
        var missing = union.Where(id => !scores.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"{profile} did not return scores for chunk(s) {string.Join(", ", missing)}.");
        }
    }

    private IReadOnlyDictionary<Guid, double> SideScores(IReadOnlyDictionary<Guid, ChunkScore> scores) =>
        scores.ToDictionary(kv => kv.Key, kv => RankingMetrics.HybridScore(kv.Value, settings.Hybrid.VectorWeight, settings.Hybrid.TextWeight));

    private async Task<TextEvidence> GatherEvidenceAsync(string queryText, TextDiff diff, CancellationToken ct)
    {
        var snippets = new List<SnippetEvidence>();
        var targets = diff.BaselineOnly.Select(id => (Id: id, Side: "baseline-only"))
            .Concat(diff.CandidateOnly.Select(id => (Id: id, Side: "candidate-only")))
            .Take(MaxSnippetsPerPair);
        foreach (var (id, side) in targets)
        {
            var snippet = Snippets.Around(data.FindChunk(id)?.Content ?? string.Empty, queryText);
            snippets.Add(new SnippetEvidence(id, side, snippet, await TokensAsync(baseline, snippet, ct), await TokensAsync(candidate, snippet, ct)));
        }

        return new TextEvidence(await TokensAsync(baseline, queryText, ct), await TokensAsync(candidate, queryText, ct), snippets);
    }

    private static async Task<IReadOnlyList<Token>> TokensAsync(SearchEngine engine, string text, CancellationToken ct)
    {
        var result = await engine.Diagnostics.TokenizeAsync(text, ct);
        return result.IsSuccess ? result.Value : [new Token($"(tokenizer error {result.Error.Code})", null, "error", false)];
    }
}
