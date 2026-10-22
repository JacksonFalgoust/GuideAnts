using Npgsql;
using NpgsqlTypes;
using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Model;
using Pgvector;

namespace Parity.Postgres;

public sealed class PostgresSearchStore : ISearchStore, IAsyncDisposable
{
    private readonly string _profile;
    private readonly string _connectionString;
    private readonly string _config;
    private readonly string _host;
    private NpgsqlDataSource _dataSource;

    public PostgresSearchStore(string profile, string connectionString, string textSearchConfig)
    {
        PostgresSchema.ValidateConfigName(textSearchConfig);
        _profile = profile;
        _connectionString = connectionString;
        _config = textSearchConfig;
        _host = PostgresErrors.Host(connectionString);
        _dataSource = PostgresDataSources.Build(connectionString);
    }

    public async Task PingServerAsync(CancellationToken ct)
    {
        var admin = new NpgsqlConnectionStringBuilder(_connectionString) { Database = "postgres", Timeout = 5, Pooling = false };
        try
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(ct);
        }
        catch (Exception ex) when (PostgresErrors.Translate(ex, _profile, _host) is { } fatal)
        {
            throw fatal;
        }
    }

    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        var database = new NpgsqlConnectionStringBuilder(_connectionString).Database
            ?? throw new ParityConfigurationException($"Profile '{_profile}' connection string has no Database.");
        var admin = new NpgsqlConnectionStringBuilder(_connectionString) { Database = "postgres", Pooling = false };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {Quote(database)} WITH (FORCE)", ct);
            await ExecuteAsync(connection, $"CREATE DATABASE {Quote(database)}", ct);
        }

        // Pooled connections point at the dropped database; start from a fresh data source.
        NpgsqlConnection.ClearAllPools();
        await _dataSource.DisposeAsync();
        _dataSource = PostgresDataSources.Build(_connectionString);

        await using (var context = PostgresParityDbContext.Create(_dataSource))
        {
            await context.Database.EnsureCreatedAsync(ct); // creates the vector extension and the table
        }

        await using var schema = await _dataSource.OpenConnectionAsync(ct);
        await schema.ReloadTypesAsync(); // the vector type didn't exist when the data source loaded types
        if (_config == PostgresSchema.CustomConfigName)
        {
            foreach (var statement in PostgresSchema.CreateCustomTextSearchConfig)
            {
                await ExecuteAsync(schema, statement, ct);
            }
        }

        await ExecuteAsync(schema, PostgresSchema.AddTsvColumn(_config), ct);
        await ExecuteAsync(schema, PostgresSchema.CreateTsvIndex, ct);
    }

    public async Task LoadAsync(IReadOnlyList<ChunkRecord> chunks, DateTime createdUtc, CancellationToken ct)
    {
        foreach (var batch in chunks.Chunk(200))
        {
            await using var context = PostgresParityDbContext.Create(_dataSource);
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            context.DocumentChunks.AddRange(batch.Select(c => DocumentChunkEntity.From(c, createdUtc)));
            await context.SaveChangesAsync(ct);
        }
    }

    // A generated tsvector column is filled as each row is written; there's nothing to wait for.
    public Task WaitUntilSearchableAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<EngineResult<IReadOnlyList<SearchHit>>> HybridSearchAsync(HybridQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return EngineResult<IReadOnlyList<SearchHit>>.Ok(Array.Empty<SearchHit>()); // production returns empty for blank queries
        }

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand(PostgresSql.Hybrid(query.FilterBeforeTopN), connection) { CommandTimeout = 120 };
            command.Parameters.Add(new NpgsqlParameter("cfg", NpgsqlDbType.Text) { Value = _config });
            command.Parameters.Add(new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = query.Text });
            command.Parameters.Add(new NpgsqlParameter("vec", new Vector(new ReadOnlyMemory<float>(query.Vector))));
            command.Parameters.Add(new NpgsqlParameter("indexName", NpgsqlDbType.Text) { Value = query.Scope.IndexName });
            command.Parameters.Add(new NpgsqlParameter("notebookId", NpgsqlDbType.Uuid) { Value = (object?)query.Scope.NotebookId ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("candidateLimit", NpgsqlDbType.Integer) { Value = query.CandidateLimit });
            command.Parameters.Add(new NpgsqlParameter("maxResults", NpgsqlDbType.Integer) { Value = query.MaxResults });
            command.Parameters.Add(new NpgsqlParameter("vectorWeight", NpgsqlDbType.Double) { Value = query.VectorWeight });
            command.Parameters.Add(new NpgsqlParameter("textWeight", NpgsqlDbType.Double) { Value = query.TextWeight });

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
        catch (NpgsqlException ex)
        {
            // Only connection-level failures throw; anything raised while the query ran is reported as a failed result.
            if (PostgresErrors.Translate(ex, _profile, _host, connecting: false) is { } fatal)
            {
                throw fatal;
            }

            return EngineResult<IReadOnlyList<SearchHit>>.Fail(PostgresErrors.ToEngineError(ex));
        }
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(ct);
    }
}
