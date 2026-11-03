using Parity.Core.Model;

namespace Parity.Core.Contracts;

/// <summary>Diagnostic queries used only by compare and the tests. Production has no equivalent.</summary>
public interface ISearchDiagnostics
{
    Task<EngineResult<IReadOnlyList<VectorHit>>> VectorSearchAsync(float[] vector, Scope scope, int k, CancellationToken ct);

    /// <summary>The full phrase-match set within the scope, with no top-N limit.</summary>
    Task<EngineResult<IReadOnlyList<TextMatch>>> TextMatchAsync(string text, Scope scope, CancellationToken ct);

    Task<EngineResult<IReadOnlyList<ChunkScore>>> ScoreChunksAsync(HybridQuery query, IReadOnlyCollection<Guid> chunkIds, CancellationToken ct);

    Task<EngineResult<IReadOnlyList<Token>>> TokenizeAsync(string text, CancellationToken ct);

    Task<EngineInfo> DescribeAsync(CancellationToken ct);

    /// <summary>Throws NotSeededException when the database or table doesn't exist.</summary>
    Task<IndexCoverage> GetIndexCoverageAsync(CancellationToken ct);
}
