namespace GuideAntsApi.BackgroundJobs.Services.Search;

/// <summary>
/// Names of the Postgres full-text objects that the <c>AddDocumentChunkFullText</c> migration creates and
/// <see cref="PostgresHybridSearcher"/> queries. The migration spells them out in its own SQL, so it stays fixed if
/// these ever change; a rename needs a new migration.
/// </summary>
/// <remarks>
/// Maintenance: a later Postgres migration that changes the type or collation of <c>DocumentChunks."Content"</c>
/// must drop <c>"ContentTsv"</c> (and its index) first and recreate them afterwards, because Postgres can't alter the
/// type of a column that a generated column uses ("cannot alter type of a column used by a generated column"). A
/// rename is fine: the stored expression follows the column.
/// </remarks>
public static class PostgresFullText
{
    /// <summary>
    /// Text search configuration: the <c>simple</c> parser mapping with English stop words for word tokens and no
    /// stemming, close to SQL Server's system stoplist and its whole-word phrase matching.
    /// </summary>
    public const string Config = "guideants_english_nostem";

    /// <summary>
    /// <c>IMMUTABLE</c> SQL function that rewrites the standalone terms <c>C#</c>, <c>F#</c>, <c>C++</c> and
    /// <c>.NET</c> (any case) to <c>csharp</c>, <c>fsharp</c>, <c>cplusplus</c> and <c>dotnet</c>, which the parser
    /// would otherwise reduce to a bare letter or <c>net</c>. It is applied to the indexed text and to the query.
    /// </summary>
    public const string NormalizeFunction = "guideants_fts_normalize";
}
