using GuideAntsApi.BackgroundJobs.Services.Embeddings;
using GuideAntsApi.DataModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace GuideAntsApi.BackgroundJobs.Services.Search;

/// <summary>
/// The Postgres version of <see cref="HybridSearcher"/>: the same candidate limit, weights, score formula, filters and
/// result mapping, with <c>CONTAINSTABLE</c> replaced by the generated <c>"ContentTsv"</c> column
/// (<c>AddDocumentChunkFullText</c> migration) and <c>vector_distance('cosine', ...)</c> by pgvector's <c>&lt;=&gt;</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Text: the query is normalized like the indexed text (<see cref="PostgresFullText.NormalizeFunction"/>) and
/// parsed with <c>phraseto_tsquery</c>, the counterpart of the double-quoted <c>CONTAINSTABLE</c> phrase. It has no
/// operator syntax, so punctuation and operators in the query can't break the statement.</item>
/// <item>Text score: <c>ts_rank_cd(..., 32)</c>, which is <c>rank / (rank + 1)</c>. It is in 0-1 like SQL Server's
/// <c>RANK / 1000</c>, but for the same chunk it is usually about 5 to 8 times higher (demo corpus: 0.017 vs 0.091,
/// 0.058 vs 0.286). With the default 0.7/0.3 weights a text match adds about 0.03-0.11 to the score here, against about
/// 0.005-0.02 on SQL Server, so text matches reorder results more on Postgres. The production comparison measured it
/// and kept it: a chunk containing the searched phrase comes first more often than on SQL Server, and no constant
/// divisor fits both long and short chunks (parity report #49). The scores reach the model: <c>MemoryTools</c> returns
/// the serialized results.</item>
/// <item>Like <c>CONTAINSTABLE</c>'s <c>top_n_by_rank</c>, the full-text candidates are the best
/// <c>maxResults * 2</c> across the whole table, taken before the index and notebook filter.</item>
/// <item>A zero embedding (<c>RebuildEmbeddingsHandler</c> zeroes them before regenerating) has a NaN cosine
/// distance in pgvector and 1 in SQL Server; it is read as 1. A NaN score would sort first and make
/// <c>MemoryTools</c>' JSON serialization throw.</item>
/// <item>Every value is a parameter, and no parameter sets <c>Size</c> (Npgsql would truncate the value).</item>
/// </list>
/// </remarks>
internal sealed class PostgresHybridSearcher(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IEmbeddingService embeddingService,
    ILogger<PostgresHybridSearcher> log) : IHybridSearcher
{
    private const int CandidateMultiplier = 2;

    internal const string Sql = $$"""
        WITH q AS (
            SELECT phraseto_tsquery('{{PostgresFullText.Config}}', {{PostgresFullText.NormalizeFunction}}(@query)) AS tsq
        ),
        ft AS (
            SELECT dc."Id", ts_rank_cd(dc."ContentTsv", q.tsq, 32) AS rank
            FROM "DocumentChunks" dc, q
            WHERE dc."ContentTsv" @@ q.tsq
            ORDER BY rank DESC
            LIMIT @candidateLimit
        ),
        scored AS (
            SELECT dc."Id", dc."Content", dc."ContentFileId", dc."NotebookFileId", dc."ChunkIndex", dc."IndexName",
                   CAST(COALESCE(ft.rank, 0) AS float8) AS text_score,
                   COALESCE(NULLIF(CAST(dc."Embedding" <=> @vec AS float8), 'NaN'::float8), 1.0) AS distance
            FROM "DocumentChunks" dc
            LEFT JOIN ft ON ft."Id" = dc."Id"
            WHERE dc."IndexName" = @indexName
              AND (@notebookId IS NULL OR dc."NotebookId" = @notebookId)
        )
        SELECT "Id", "Content", "ContentFileId", "NotebookFileId", "ChunkIndex", "IndexName",
               1.0 - distance AS vector_score,
               text_score,
               @vectorWeight * (1.0 - distance) + @textWeight * text_score AS score
        FROM scored
        WHERE text_score > 0 OR distance < 1.0
        ORDER BY score DESC
        LIMIT @maxResults
        """;

    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory = dbFactory;
    private readonly IEmbeddingService _embeddingService = embeddingService;
    private readonly ILogger<PostgresHybridSearcher> _log = log;

    public async Task<IReadOnlyList<HybridSearchResult>> SearchAsync(
        string query,
        string indexName,
        Guid? notebookId = null,
        int maxResults = 5,
        double vectorWeight = 0.7,
        double textWeight = 0.3,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<HybridSearchResult>();

        var queryEmbeddings = await _embeddingService.GetEmbeddingsAsync([query], EmbeddingPurpose.Query, ct);
        var queryVector = queryEmbeddings[0];

        await using var context = await _dbFactory.CreateDbContextAsync(ct);

        try
        {
            await context.Database.OpenConnectionAsync(ct);
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            await using var command = CreateCommand(
                connection, query, queryVector, indexName, notebookId, maxResults, vectorWeight, textWeight);

            var results = new List<HybridSearchResult>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(new HybridSearchResult
                {
                    ChunkId = reader.GetGuid(0),
                    Content = reader.GetString(1),
                    ContentFileId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    NotebookFileId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    ChunkIndex = reader.GetInt32(4),
                    IndexName = reader.GetString(5),
                    VectorScore = reader.GetDouble(6),
                    TextScore = reader.GetDouble(7),
                    Score = reader.GetDouble(8)
                });
            }

            _log.LogInformation(
                "Hybrid search returned {Count} results (vector: {VectorWeight:F2}, text: {TextWeight:F2}) for query: {Query}",
                results.Count, vectorWeight, textWeight, LogValueSanitizer.Sanitize(query));

            return results;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Hybrid search failed for query: {Query}", LogValueSanitizer.Sanitize(query));
            return Array.Empty<HybridSearchResult>();
        }
    }

    /// <summary>The search command with every value bound as a parameter.</summary>
    internal static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        string query,
        float[] queryVector,
        string indexName,
        Guid? notebookId,
        int maxResults,
        double vectorWeight,
        double textWeight)
    {
        var command = new NpgsqlCommand(Sql, connection);
        // Postgres text can't hold U+0000; stored text gets U+FFFD in its place (PostgresModelAdapter), so the query does too.
        command.Parameters.Add(new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = query.Replace('\0', '\uFFFD') });
        command.Parameters.Add(new NpgsqlParameter("vec", new Vector(queryVector)));
        command.Parameters.Add(new NpgsqlParameter("indexName", NpgsqlDbType.Text) { Value = indexName });
        command.Parameters.Add(new NpgsqlParameter("notebookId", NpgsqlDbType.Uuid) { Value = (object?)notebookId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("candidateLimit", NpgsqlDbType.Integer) { Value = maxResults * CandidateMultiplier });
        command.Parameters.Add(new NpgsqlParameter("maxResults", NpgsqlDbType.Integer) { Value = maxResults });
        command.Parameters.Add(new NpgsqlParameter("vectorWeight", NpgsqlDbType.Double) { Value = vectorWeight });
        command.Parameters.Add(new NpgsqlParameter("textWeight", NpgsqlDbType.Double) { Value = textWeight });
        return command;
    }
}
