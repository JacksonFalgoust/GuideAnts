using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Model;
using Parity.SqlServer.Sql;

namespace Parity.SqlServer;

public sealed class SqlServerSearchDiagnostics(string profile, string connectionString) : ISearchDiagnostics
{
    private const string VectorSql = """
        DECLARE @queryVec vector(1536) = CAST(@vectorJson AS vector(1536));
        SELECT TOP (@k) dc.Id, CAST(1.0 - vector_distance('cosine', dc.Embedding, @queryVec) AS FLOAT) AS Score
        FROM DocumentChunks dc
        WHERE dc.IndexName = @indexName AND (@notebookId IS NULL OR dc.NotebookId = @notebookId)
        ORDER BY vector_distance('cosine', dc.Embedding, @queryVec), dc.Id;
        """;

    private const string TextMatchSql = """
        SELECT ft.[KEY], CAST(ft.[RANK] AS FLOAT) / 1000.0
        FROM CONTAINSTABLE(DocumentChunks, Content, @phrase) ft
        INNER JOIN DocumentChunks dc ON dc.Id = ft.[KEY]
        WHERE dc.IndexName = @indexName AND (@notebookId IS NULL OR dc.NotebookId = @notebookId);
        """;

    // {candidates} is replaced by one of the two candidate definitions below.
    private const string ScoreSql = """
        DECLARE @queryVec vector(1536) = CAST(@vectorJson AS vector(1536));
        WITH AllMatches AS (
            SELECT ft.[KEY] AS Id, CAST(ft.[RANK] AS FLOAT) / 1000.0 AS R
            FROM CONTAINSTABLE(DocumentChunks, Content, @phrase) ft
        ),
        Cand AS (
            {candidates}
        ),
        CandStats AS (
            SELECT CASE WHEN COUNT(*) = @candidateLimit THEN MIN(R) END AS Nth FROM Cand
        )
        SELECT dc.Id,
               CAST(1.0 - vector_distance('cosine', dc.Embedding, @queryVec) AS FLOAT),
               CAST(COALESCE(c.R, 0) AS FLOAT),
               CAST(CASE WHEN m.Id IS NULL THEN 0 ELSE 1 END AS bit),
               m.R,
               CAST(CASE WHEN c.Id IS NULL THEN 0 ELSE 1 END AS bit),
               s.Nth
        FROM DocumentChunks dc
        CROSS JOIN CandStats s
        LEFT JOIN AllMatches m ON m.Id = dc.Id
        LEFT JOIN Cand c ON c.Id = dc.Id
        WHERE dc.Id IN (SELECT CAST([value] AS uniqueidentifier) FROM OPENJSON(@ids));
        """;

    // Production: CONTAINSTABLE's own top_n, across the whole table.
    private const string ProductionCandidates =
        "SELECT ft.[KEY] AS Id, CAST(ft.[RANK] AS FLOAT) / 1000.0 AS R FROM CONTAINSTABLE(DocumentChunks, Content, @phrase, {n}) ft";

    // Quirk fixed: scope filter first, then the top N.
    private const string ScopedCandidates = """
        SELECT TOP ({n}) m.Id, m.R FROM AllMatches m
                INNER JOIN DocumentChunks scoped ON scoped.Id = m.Id
                WHERE scoped.IndexName = @indexName AND (@notebookId IS NULL OR scoped.NotebookId = @notebookId)
                ORDER BY m.R DESC
        """;

    private const string TokenizeSql = """
        DECLARE @lcid int = (SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'default full-text language');
        DECLARE @accent int = ISNULL(CAST(FULLTEXTCATALOGPROPERTY('DocumentChunks_Catalog', 'AccentSensitivity') AS int), 1);
        SELECT display_term, special_term FROM sys.dm_fts_parser(@phrase, @lcid, 0, @accent) ORDER BY occurrence;
        """;

    private const string DescribeSql = """
        SELECT @@VERSION,
               (SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'default full-text language'),
               CAST(FULLTEXTCATALOGPROPERTY('DocumentChunks_Catalog', 'AccentSensitivity') AS int),
               (SELECT CAST(stoplist_id AS int) FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('DocumentChunks')),
               (SELECT CAST(compatibility_level AS int) FROM sys.databases WHERE name = DB_NAME());
        """;

    private const string CoverageSql = """
        SELECT COUNT(*), ISNULL(CAST(OBJECTPROPERTYEX(OBJECT_ID('DocumentChunks'), 'TableFulltextItemCount') AS int), 0)
        FROM DocumentChunks;
        """;

    private readonly string _host = SqlServerErrors.Host(connectionString);

    public Task<EngineResult<IReadOnlyList<VectorHit>>> VectorSearchAsync(float[] vector, Scope scope, int k, CancellationToken ct) =>
        QueryAsync(VectorSql, p =>
        {
            p.Add(new SqlParameter("@vectorJson", SqlDbType.NVarChar, -1) { Value = ProductionEscaping.VectorJson(vector) });
            p.Add(new SqlParameter("@k", SqlDbType.Int) { Value = k });
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

        var limit = query.CandidateLimit.ToString(CultureInfo.InvariantCulture);
        var candidates = (query.FilterBeforeTopN ? ScopedCandidates : ProductionCandidates).Replace("{n}", limit);
        return QueryAsync(ScoreSql.Replace("{candidates}", candidates), p =>
        {
            p.Add(new SqlParameter("@vectorJson", SqlDbType.NVarChar, -1) { Value = ProductionEscaping.VectorJson(query.Vector) });
            p.Add(new SqlParameter("@candidateLimit", SqlDbType.Int) { Value = query.CandidateLimit });
            p.Add(new SqlParameter("@ids", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(chunkIds) });
            AddPhrase(p, query.Text);
            AddScope(p, query.Scope);
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
        QueryAsync(TokenizeSql, p => AddPhrase(p, text), r =>
        {
            var term = r.IsDBNull(0) ? string.Empty : r.GetString(0);
            var kind = r.IsDBNull(1) ? string.Empty : r.GetString(1);
            return new Token(term, term, kind, kind == "Noise Word");
        }, ct);

    public async Task<EngineInfo> DescribeAsync(CancellationToken ct)
    {
        await using var connection = await SqlServerConnections.OpenAsync(connectionString, profile, ct);
        await using var command = new SqlCommand(DescribeSql, connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var version = reader.GetString(0).Split('\n')[0].Trim();
        var details = new Dictionary<string, string>
        {
            ["fullTextLanguage"] = reader.IsDBNull(1) ? "unknown" : reader.GetInt32(1).ToString(CultureInfo.InvariantCulture),
            ["accentSensitive"] = reader.IsDBNull(2) ? "unknown" : (reader.GetInt32(2) == 1 ? "yes" : "no"),
            ["stoplist"] = reader.IsDBNull(3) ? "off" : reader.GetInt32(3) == 0 ? "system" : $"custom ({reader.GetInt32(3)})",
            ["compatibilityLevel"] = reader.IsDBNull(4) ? "unknown" : reader.GetInt32(4).ToString(CultureInfo.InvariantCulture),
        };
        return new EngineInfo("SqlServer", version, details);
    }

    public async Task<IndexCoverage> GetIndexCoverageAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = await SqlServerConnections.OpenAsync(connectionString, profile, ct);
            await using var command = new SqlCommand(CoverageSql, connection) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return new IndexCoverage(reader.GetInt32(0), reader.GetInt32(1));
        }
        catch (SqlException ex) when (SqlServerErrors.Translate(ex, profile, _host, connecting: false) is { } fatal)
        {
            throw fatal;
        }
    }

    private async Task<EngineResult<IReadOnlyList<T>>> QueryAsync<T>(string sql, Action<SqlParameterCollection> bind, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        try
        {
            await using var connection = await SqlServerConnections.OpenAsync(connectionString, profile, ct);
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
            bind(command.Parameters);
            var rows = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(map(reader));
            }

            return EngineResult<IReadOnlyList<T>>.Ok(rows);
        }
        catch (SqlException ex)
        {
            if (SqlServerErrors.Translate(ex, profile, _host, connecting: false) is { } fatal)
            {
                throw fatal;
            }

            return EngineResult<IReadOnlyList<T>>.Fail(new EngineError($"SQL{ex.Number}", ex.Message));
        }
    }

    // The same phrase production builds, passed as a parameter, so no single-quote escaping is needed.
    private static void AddPhrase(SqlParameterCollection p, string text) =>
        p.Add(new SqlParameter("@phrase", SqlDbType.NVarChar, 4000) { Value = ProductionEscaping.FullTextPhrase(text) });

    private static void AddScope(SqlParameterCollection p, Scope scope)
    {
        p.Add(new SqlParameter("@indexName", SqlDbType.NVarChar, 255) { Value = scope.IndexName });
        p.Add(new SqlParameter("@notebookId", SqlDbType.UniqueIdentifier) { Value = (object?)scope.NotebookId ?? DBNull.Value });
    }
}
