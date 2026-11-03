using Parity.Core.Model;

namespace Parity.Core.Contracts;

/// <summary>The only thing the search path depends on. Implementations are chosen by configuration.</summary>
public interface ISearchStore
{
    /// <summary>Connects to the server (not the demo database). Throws EngineUnavailableException.</summary>
    Task PingServerAsync(CancellationToken ct);

    /// <summary>Drops and recreates the database, table and full-text objects.</summary>
    Task EnsureSchemaAsync(CancellationToken ct);

    Task LoadAsync(IReadOnlyList<ChunkRecord> chunks, DateTime createdUtc, CancellationToken ct);

    /// <summary>Blocks until full-text indexing has finished (a real wait on SQL Server).</summary>
    Task WaitUntilSearchableAsync(CancellationToken ct);

    Task<EngineResult<IReadOnlyList<SearchHit>>> HybridSearchAsync(HybridQuery query, CancellationToken ct);
}
