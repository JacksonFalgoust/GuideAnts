using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Model;
using Parity.SqlServer.Sql;

[assembly: InternalsVisibleTo("Parity.Tests")]

namespace Parity.SqlServer;

public sealed class SqlServerSearchStore(string profile, string connectionString, TimeSpan populationTimeout) : ISearchStore
{
    private readonly string _host = SqlServerErrors.Host(connectionString);

    public async Task PingServerAsync(CancellationToken ct)
    {
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", ConnectTimeout = 5 };
        try
        {
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync(ct);
        }
        catch (SqlException ex)
        {
            throw new EngineUnavailableException(
                $"Can't reach profile '{profile}' (SqlServer) at {_host}: {ex.Message} Is `docker compose up -d` running, and has SQL Server finished starting?", ex);
        }
    }

    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        await using (var context = SqlServerParityDbContext.Create(connectionString))
        {
            await context.Database.EnsureDeletedAsync(ct);
            await context.Database.EnsureCreatedAsync(ct);
        }

        SqlConnection.ClearAllPools();
        await using var connection = await OpenAsync(ct);
        if (await ScalarIntAsync(connection, SqlServerSchema.IsFullTextInstalled, ct) != 1)
        {
            // Production would silently skip the index; here that would make every text match empty on both sides.
            throw new ParityConfigurationException(
                $"Full-text search isn't installed on profile '{profile}'. Use the ghcr.io/elumenotion/mssql2025-express-fts image.");
        }

        await ExecuteAsync(connection, SqlServerSchema.CreateFullTextCatalog, ct);
        await ExecuteAsync(connection, SqlServerSchema.CreateFullTextIndex, ct);
    }

    public async Task LoadAsync(IReadOnlyList<ChunkRecord> chunks, DateTime createdUtc, CancellationToken ct)
    {
        foreach (var batch in chunks.Chunk(200))
        {
            await using var context = SqlServerParityDbContext.Create(connectionString);
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            context.DocumentChunks.AddRange(batch.Select(c => DocumentChunkEntity.From(c, createdUtc)));
            await context.SaveChangesAsync(ct);
        }
    }

    public async Task WaitUntilSearchableAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + populationTimeout;
        await using var connection = await OpenAsync(ct);
        while (true)
        {
            int status, pending, indexed, rows;
            await using (var command = new SqlCommand(SqlServerSchema.PopulationStatus, connection))
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                await reader.ReadAsync(ct);
                (status, pending, indexed, rows) = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
            }

            if (status == 0 && pending == 0 && indexed == rows)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new ParityConfigurationException(
                    $"Full-text population on profile '{profile}' didn't finish within {populationTimeout.TotalSeconds:0}s " +
                    $"(status {status}, pending {pending}, indexed {indexed}/{rows}).");
            }

            await Task.Delay(500, ct);
        }
    }

    public async Task<EngineResult<IReadOnlyList<SearchHit>>> HybridSearchAsync(HybridQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return EngineResult<IReadOnlyList<SearchHit>>.Ok(Array.Empty<SearchHit>()); // production returns empty for blank queries
        }

        var sql = query.FilterBeforeTopN
            ? SqlServerSql.BuildQuirkFixedHybrid(query.Vector, query.Text, query.CandidateLimit)
            : SqlServerSql.BuildProductionHybrid(query.Vector, query.Text, query.CandidateLimit);
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var command = new SqlCommand(sql, connection);
            // The same parameters, types and sizes as production.
            command.Parameters.Add(new SqlParameter("@indexName", SqlDbType.NVarChar, 255) { Value = query.Scope.IndexName });
            command.Parameters.Add(new SqlParameter("@notebookId", SqlDbType.UniqueIdentifier) { Value = (object?)query.Scope.NotebookId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@maxResults", SqlDbType.Int) { Value = query.MaxResults });
            command.Parameters.Add(new SqlParameter("@vectorWeight", SqlDbType.Float) { Value = query.VectorWeight });
            command.Parameters.Add(new SqlParameter("@textWeight", SqlDbType.Float) { Value = query.TextWeight });

            var hits = new List<SearchHit>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                hits.Add(new SearchHit(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetDouble(6),
                    reader.GetDouble(7),
                    reader.GetDouble(8)));
            }

            return EngineResult<IReadOnlyList<SearchHit>>.Ok(hits);
        }
        catch (SqlException ex)
        {
            if (SqlServerErrors.Translate(ex, profile, _host) is { } fatal)
            {
                throw fatal;
            }

            return EngineResult<IReadOnlyList<SearchHit>>.Fail(new EngineError($"SQL{ex.Number}", ex.Message));
        }
    }

    internal async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch (SqlException ex)
        {
            await connection.DisposeAsync();
            throw SqlServerErrors.Translate(ex, profile, _host) ?? (Exception)ex;
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> ScalarIntAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }
}
