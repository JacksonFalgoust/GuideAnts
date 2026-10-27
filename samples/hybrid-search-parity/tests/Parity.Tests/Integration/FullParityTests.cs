using Parity.Core.Comparison;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Xunit.Abstractions;

namespace Parity.Tests.Integration;

/// <summary>
/// Rules 1 and 2 and "zero unexplained", plus the sanity checks that stop a false pass. The verdict
/// thresholds are deliberately not asserted: they're the experiment's result, not a code-correctness check.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class FullParityTests(IntegrationFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task BothIndexesCoverEveryRow()
    {
        var (baseline, candidate) = await fixture.FullEnginesAsync();
        await using (baseline)
        await using (candidate)
        {
            var rows = fixture.FullData.Value.Chunks.Count;
            var sql = await baseline.Diagnostics.GetIndexCoverageAsync(CancellationToken.None);
            var pg = await candidate.Diagnostics.GetIndexCoverageAsync(CancellationToken.None);
            Assert.Equal((rows, rows), (sql.Rows, sql.IndexedRows));
            Assert.Equal((rows, rows), (pg.Rows, pg.IndexedRows));
        }
    }

    [Fact]
    public async Task Rule1VectorParityHoldsForEveryPair()
    {
        var failures = (await fixture.FullComparisonAsync()).Where(p => !p.Vector.Pass).Select(p => $"{p.Query.Id}·{p.Scope}: {p.Vector.Failure}").ToList();
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task Rule2EveryTextAndErrorDifferenceIsReviewedAndAccepted()
    {
        var open = (await fixture.FullComparisonAsync())
            .Where(p => (p.TextReview?.IsOpen ?? false) || (p.ErrorReview?.IsOpen ?? false))
            .Select(p => $"{p.Query.Id}·{p.Scope}: text {p.TextReview?.Outcome.ToString() ?? "-"}, error {p.ErrorReview?.Outcome.ToString() ?? "-"}")
            .ToList();
        Assert.True(open.Count == 0, "Open differences (review them into known-differences.json):\n" + string.Join("\n", open));
    }

    [Fact]
    public async Task ThereAreNoUnexplainedHybridDifferences()
    {
        var unexplained = (await fixture.FullComparisonAsync())
            .SelectMany(p => p.Differences.Where(d => d.Category == DifferenceCategory.Unexplained).Select(d => $"{p.Query.Id}·{p.Scope}: {d.ChunkId} ({d.Detail})"))
            .ToList();
        Assert.True(unexplained.Count == 0, string.Join("\n", unexplained));
    }

    /// <summary>
    /// A harness check, not a parity verdict: each engine's own text matching must reach the ground truth for most
    /// queries (otherwise the harness is broken). A query one engine tokenises differently (for example a URL) is a
    /// legitimate engine difference that Rule 2 flags for review, so it only counts against the threshold; exact-phrase
    /// queries are plain words and must overlap on both engines.
    /// </summary>
    [Fact]
    public async Task EachEnginesMatchesOverlapTheGroundTruth()
    {
        const double requiredShare = 0.9;
        var data = fixture.FullData.Value;
        var (baseline, candidate) = await fixture.FullEnginesAsync();
        await using (baseline)
        await using (candidate)
        {
            var queries = data.Queries.Where(q => QueryGroups.HasGroundTruth(q.Group) && data.GroundTruth[q.Id].Count > 0).ToList();
            var problems = new List<string>();
            foreach (var engine in new[] { baseline, candidate })
            {
                var misses = new List<string>();
                foreach (var query in queries)
                {
                    var truth = data.GroundTruth[query.Id].ToHashSet();
                    var matched = new HashSet<Guid>();
                    foreach (var scope in ParityScopes.All)
                    {
                        var result = await engine.Diagnostics.TextMatchAsync(query.Text, scope.Scope, CancellationToken.None);
                        if (result.IsSuccess)
                        {
                            matched.UnionWith(result.Value.Select(m => m.ChunkId));
                        }
                    }

                    if (matched.Count == 0 || !matched.Overlaps(truth))
                    {
                        var line = $"{engine.Profile} {query.Id} [{query.Group}] \"{query.Text}\": {matched.Count} matches, none in the ground truth";
                        misses.Add(line);
                        if (query.Group == QueryGroups.Exact)
                        {
                            problems.Add("exact-phrase query must overlap on every engine: " + line);
                        }
                    }
                }

                output.WriteLine($"{engine.Profile}: {queries.Count - misses.Count}/{queries.Count} queries overlap the ground truth");
                foreach (var miss in misses)
                {
                    output.WriteLine("  miss: " + miss);
                }

                if ((double)(queries.Count - misses.Count) / queries.Count < requiredShare)
                {
                    problems.Add($"{engine.Profile} overlaps the ground truth for only {queries.Count - misses.Count}/{queries.Count} queries (need {requiredShare:P0}):\n" + string.Join("\n", misses));
                }
            }

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }

    [Fact]
    public async Task ExactPhraseQueriesSurfaceAGroundTruthChunkInSomeScope()
    {
        var data = fixture.FullData.Value;
        var pairs = await fixture.FullComparisonAsync();
        var problems = new List<string>();
        foreach (var query in data.Queries.Where(q => q.Group == QueryGroups.Exact))
        {
            var truth = data.GroundTruth[query.Id].ToHashSet();
            var forQuery = pairs.Where(p => p.Query.Id == query.Id).ToList();
            if (!forQuery.Any(p => p.BaselineHits.Take(5).Any(h => truth.Contains(h.ChunkId))))
            {
                problems.Add($"baseline {query.Id}");
            }

            if (!forQuery.Any(p => p.CandidateHits.Take(5).Any(h => truth.Contains(h.ChunkId))))
            {
                problems.Add($"candidate {query.Id}");
            }
        }

        Assert.True(problems.Count == 0, "No ground-truth chunk in any scope's top 5: " + string.Join(", ", problems));
    }
}
