namespace GuideAntsApi.BackgroundJobs.Services.Search;

/// <summary>
/// Names of the Postgres full-text objects that the <c>AddDocumentChunkFullText</c> migration creates and
/// <see cref="PostgresHybridSearcher"/> queries. The migration spells them out in its own SQL, so it stays fixed if
/// these ever change; a rename needs a new migration.
/// </summary>
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
