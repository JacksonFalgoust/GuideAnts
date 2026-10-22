using Npgsql;
using NpgsqlTypes;
using Parity.Core.Contracts;
using Parity.Core.Model;
using Pgvector;

namespace Parity.Postgres;

public sealed class PostgresSearchDiagnostics : ISearchDiagnostics, IAsyncDisposable
{
    private const string VectorSql = """
        SELECT "Id", 1.0 - CAST("Embedding" <=> @vec AS float8)
        FROM "DocumentChunks"
        WHERE "IndexName" = @indexName AND (@notebookId IS NULL OR "NotebookId" = @notebookId)
        ORDER BY "Embedding" <=> @vec, "Id"
        LIMIT @k
        """;

    private const string TextMatchSql = """
        WITH q AS (SELECT phraseto_tsquery(@cfg::regconfig, @query) AS tsq)
        SELECT dc."Id", CAST(ts_rank_cd(dc."ContentTsv", q.tsq, 32) AS float8)
        FROM "DocumentChunks" dc, q
        WHERE dc."ContentTsv" @@ q.tsq
          AND dc."IndexName" = @indexName AND (@notebookId IS NULL OR dc."NotebookId" = @notebookId)
        """;

    private const string ScoreSql = """
        WITH q AS (SELECT phraseto_tsquery(@cfg::regconfig, @query) AS tsq),
        all_matches AS (
            SELECT dc."Id", CAST(ts_rank_cd(dc."ContentTsv", q.tsq, 32) AS float8) AS r
            FROM "DocumentChunks" dc, q
            WHERE dc."ContentTsv" @@ q.tsq
        ),
        cand AS (
            SELECT m."Id", m.r
            FROM all_matches m
            JOIN "DocumentChunks" d ON d."Id" = m."Id"
            WHERE NOT @filterBeforeTopN
               OR (d."IndexName" = @indexName AND (@notebookId IS NULL OR d."NotebookId" = @notebookId))
            ORDER BY m.r DESC
            LIMIT @candidateLimit
        ),
        stats AS (SELECT CASE WHEN COUNT(*) = @candidateLimit THEN MIN(r) END AS nth FROM cand)
        SELECT dc."Id",
               1.0 - CAST(dc."Embedding" <=> @vec AS float8),
               COALESCE(c.r, 0),
               m."Id" IS NOT NULL,
               m.r,
               c."Id" IS NOT NULL,
               s.nth
        FROM "DocumentChunks" dc
        CROSS JOIN stats s
        LEFT JOIN all_matches m ON m."Id" = dc."Id"
        LEFT JOIN cand c ON c."Id" = dc."Id"
        WHERE dc."Id" = ANY(@ids)
        """;

    private const string TokenizeSql = """
        SELECT token, array_to_string(lexemes, ' '), alias, (lexemes IS NOT NULL AND cardinality(lexemes) = 0)
        FROM ts_debug(@cfg::regconfig, @text)
        WHERE alias <> 'blank'
        """;

    private const string DescribeSql = """
        SELECT version(), current_setting('server_version'), (SELECT extversion FROM pg_extension WHERE extname = 'vector')
        """;

    private const string CoverageSql = """
        SELECT CAST(COUNT(*) AS int), CAST(COUNT(*) FILTER (WHERE length("ContentTsv") > 0) AS int) FROM "DocumentChunks"
        """;

    private readonly string _profile;
    private readonly string _config;
    private readonly string _host;
    private readonly NpgsqlDataSource _dataSource;

    public PostgresSearchDiagnostics(string profile, string connectionString, string textSearchConfig)
    {
        PostgresSchema.ValidateConfigName(textSearchConfig);
        _profile = profile;
        _config = textSearchConfig;
        _host = PostgresErrors.Host(connectionString);
        _dataSource = PostgresDataSources.Build(connectionString);
    }

    public Task<EngineResult<IReadOnlyList<VectorHit>>> VectorSearchAsync(float[] vector, Scope scope, int k, CancellationToken ct) =>
        QueryAsync(VectorSql, p =>
        {
            p.Add(Vec(vector));
            p.Add(new NpgsqlParameter("k", NpgsqlDbType.Integer) { Value = k });
            AddScope(p, scope);
        }, r => new VectorHit(r.GetGuid(0), r.GetDouble(1)), ct);

    public Task<EngineResult<IReadOnlyList<TextMatch>>> TextMatchAsync(string text, Scope scope, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(text)
            ? Task.FromResult(EngineResult<IReadOnlyList<TextMatch>>.Ok(Array.Empty<TextMatch>()))
            : QueryAsync(TextMatchSql, p =>
            {
                AddPhrase(p, text);
                AddScope(p, scope);
            }, r => new TextMatch(r.GetGuid(0), r.GetDouble(1)), ct);

    public Task<EngineResult<IReadOnlyList<ChunkScore>>> ScoreChunksAsync(HybridQuery query, IReadOnlyCollection<Guid> chunkIds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Text) || chunkIds.Count == 0)
        {
            return Task.FromResult(EngineResult<IReadOnlyList<ChunkScore>>.Ok(Array.Empty<ChunkScore>()));
        }

        return QueryAsync(ScoreSql, p =>
        {
            AddPhrase(p, query.Text);
            AddScope(p, query.Scope);
            p.Add(Vec(query.Vector));
            p.Add(new NpgsqlParameter("filterBeforeTopN", NpgsqlDbType.Boolean) { Value = query.FilterBeforeTopN });
            p.Add(new NpgsqlParameter("candidateLimit", NpgsqlDbType.Integer) { Value = query.CandidateLimit });
            p.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = chunkIds.ToArray() });
        }, r => new ChunkScore(
            r.GetGuid(0),
            r.GetDouble(1),
            r.GetDouble(2),
            r.GetBoolean(3),
            r.IsDBNull(4) ? null : r.GetDouble(4),
            r.GetBoolean(5),
            r.IsDBNull(6) ? null : r.GetDouble(6)), ct);
    }

    public Task<EngineResult<IReadOnlyList<Token>>> TokenizeAsync(string text, CancellationToken ct) =>
        QueryAsync(TokenizeSql, p =>
        {
            p.Add(new NpgsqlParameter("cfg", NpgsqlDbType.Text) { Value = _config });
            p.Add(new NpgsqlParameter("text", NpgsqlDbType.Text) { Value = text });
        }, r => new Token(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetBoolean(3)), ct);

    public async Task<EngineInfo> DescribeAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(DescribeSql, connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var details = new Dictionary<string, string>
        {
            ["serverVersion"] = reader.GetString(1),
            ["pgvector"] = reader.IsDBNull(2) ? "not installed" : reader.GetString(2),
            ["textSearchConfig"] = _config,
            ["rankFunction"] = "ts_rank_cd(…, 32)",
        };
        return new EngineInfo("Postgres", reader.GetString(0), details);
    }

    public async Task<IndexCoverage> GetIndexCoverageAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var command = new NpgsqlCommand(CoverageSql, connection) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return new IndexCoverage(reader.GetInt32(0), reader.GetInt32(1));
        }
        catch (NpgsqlException ex) when (PostgresErrors.Translate(ex, _profile, _host) is { } fatal)
        {
            throw fatal;
        }
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        try
        {
            return await _dataSource.OpenConnectionAsync(ct);
        }
        catch (Exception ex) when (PostgresErrors.Translate(ex, _profile, _host) is { } fatal)
        {
            throw fatal;
        }
    }

    private async Task<EngineResult<IReadOnlyList<T>>> QueryAsync<T>(string sql, Action<NpgsqlParameterCollection> bind, Func<NpgsqlDataReader, T> map, CancellationToken ct)
    {
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 120 };
            bind(command.Parameters);
            var rows = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(map(reader));
            }

            return EngineResult<IReadOnlyList<T>>.Ok(rows);
        }
        catch (NpgsqlException ex)
        {
            // Only connection-level failures throw; anything raised while the query ran is a failed result.
            if (PostgresErrors.Translate(ex, _profile, _host, connecting: false) is { } fatal)
            {
                throw fatal;
            }

            return EngineResult<IReadOnlyList<T>>.Fail(PostgresErrors.ToEngineError(ex));
        }
    }

    private void AddPhrase(NpgsqlParameterCollection p, string text)
    {
        p.Add(new NpgsqlParameter("cfg", NpgsqlDbType.Text) { Value = _config });
        p.Add(new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = text });
    }

    private static void AddScope(NpgsqlParameterCollection p, Scope scope)
    {
        p.Add(new NpgsqlParameter("indexName", NpgsqlDbType.Text) { Value = scope.IndexName });
        p.Add(new NpgsqlParameter("notebookId", NpgsqlDbType.Uuid) { Value = (object?)scope.NotebookId ?? DBNull.Value });
    }

    private static NpgsqlParameter Vec(float[] vector) => new("vec", new Vector(new ReadOnlyMemory<float>(vector)));
}
