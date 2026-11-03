using Parity.Core.Corpus;
using Parity.Core.Ids;
using Parity.Core.Model;

namespace Parity.Tests.Support;

/// <summary>
/// 18 hand-made chunks across P1 and P2 for engine tests. The "alpha beta" chunks in P2 outrank
/// m17 (the only P1 match), so production's cross-project top-10 cut drops m17's text score.
/// </summary>
public static class MiniCorpus
{
    public static readonly Guid P1 = ParityScopes.ProjectId("P1");
    public static readonly Guid P2 = ParityScopes.ProjectId("P2");
    public static readonly DateTime CreatedUtc = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

    public static Scope ScopeP1 { get; } = new(P1.ToString(), null);
    public static Scope ScopeP2 { get; } = new(P2.ToString(), null);
    public static Scope ScopeN1 { get; } = new(P1.ToString(), ParityScopes.NotebookN1);

    public static IReadOnlyList<ChunkRecord> Chunks { get; } = Build();

    public static Guid Id(string key) => DeterministicGuid.Create($"mini:{key}");

    public static HybridQuery Query(string text, Scope scope, bool filterBeforeTopN = false) =>
        new(text, HashEmbedding.Create(text), scope, MaxResults: 5, VectorWeight: 0.7, TextWeight: 0.3, CandidateMultiplier: 2, filterBeforeTopN);

    private static IReadOnlyList<ChunkRecord> Build()
    {
        var chunks = new List<ChunkRecord>
        {
            Make("m01", P1, null, "The conversation lock is held by the worker until the lease expires."),
            Make("m02", P1, ParityScopes.NotebookN1, "Conversation locks expire after five minutes."),
            Make("m03", P1, null, "Don't restart the container; it can't resume. O'Reilly Media wrote about it."),
            Make("m04", P1, null, "The smoke test should say \"hello world\" twice."),
            Make("m17", P1, null, "A single alpha beta mention in project one, surrounded by many other words that dilute this chunk considerably."),
            Make("m18", P2, null, "Vectors only: nothing lexical here."),
        };
        for (var i = 5; i <= 16; i++)
        {
            chunks.Add(Make($"m{i:00}", P2, null, $"alpha beta alpha beta alpha beta topic {i}"));
        }

        return chunks;
    }

    private static ChunkRecord Make(string key, Guid project, Guid? notebook, string content) =>
        new(Id(key), project.ToString(), project, notebook, $"mini/{key}", DeterministicGuid.Create($"mini-file:{key}"), $"mini/{key}", 0, ChunkKind.Doc, content, HashEmbedding.Create(content));
}
