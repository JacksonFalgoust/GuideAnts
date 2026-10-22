namespace Parity.Postgres;

/// <summary>
/// The Postgres port of production's hybrid query, built step for step like HybridSearcher's SQL.
/// The phrase and the vector are passed as parameters instead of being spliced into the text.
/// </summary>
public static class PostgresSql
{
    private const string ScopeFilter = """
        AND dc."IndexName" = @indexName
              AND (@notebookId IS NULL OR dc."NotebookId" = @notebookId)
        """;

    private const string Template = """
        WITH q AS (
            SELECT phraseto_tsquery(@cfg::regconfig, @query) AS tsq
        ),
        ft AS (
            SELECT dc."Id", ts_rank_cd(dc."ContentTsv", q.tsq, 32) AS rank
            FROM "DocumentChunks" dc, q
            WHERE dc."ContentTsv" @@ q.tsq
              {ftScope}
            ORDER BY rank DESC
            LIMIT @candidateLimit
        ),
        scored AS (
            SELECT dc."Id", dc."Content", dc."ContentFileId", dc."NotebookFileId", dc."ChunkIndex", dc."IndexName",
                   CAST(COALESCE(ft.rank, 0) AS float8) AS text_score,
                   CAST(dc."Embedding" <=> @vec AS float8) AS distance
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

    /// <param name="filterBeforeTopN">false = production behaviour (top-N across all projects); true = informational variant.</param>
    public static string Hybrid(bool filterBeforeTopN) => Template.Replace("{ftScope}", filterBeforeTopN ? ScopeFilter : string.Empty);
}
