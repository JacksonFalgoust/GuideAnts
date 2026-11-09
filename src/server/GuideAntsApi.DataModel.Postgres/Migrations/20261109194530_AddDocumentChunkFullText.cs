using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuideAntsApi.DataModel.Postgres.Migrations
{
    /// <summary>
    /// Full-text search over <c>DocumentChunks.Content</c>, the Postgres counterpart of SQL Server's
    /// <c>DocumentChunks_Catalog</c> full-text index (created by raw SQL in the SQL Server baseline). The model doesn't
    /// change: the generated <c>ContentTsv</c> column is not mapped, and EF never reads or writes it.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>guideants_english_nostem</c>: the <c>simple</c> configuration with English stop words for word tokens
    /// and no stemming, so a phrase matches whole words, close to SQL Server's system stoplist.</item>
    /// <item><c>guideants_fts_normalize</c>: the default parser drops <c>#</c> and <c>+</c> and the leading dot of
    /// <c>.NET</c>, so <c>C#</c> would become the letter <c>c</c>. The function rewrites the standalone terms
    /// <c>C#</c>, <c>F#</c>, <c>C++</c> and <c>.NET</c> (any case) to one word each, with spaces around it so it never
    /// merges with neighbouring punctuation into a path or host token. "Standalone" means no letter, digit or
    /// underscore directly before or after. The regular expressions run under the <c>"C"</c> collation:
    /// <c>Content</c> uses the nondeterministic <c>guideants_ci</c> collation, which Postgres rejects for regular
    /// expressions. The searcher applies the same function to the query.</item>
    /// <item>No vector index: SQL Server's <c>vector_distance</c> search is a scan too.</item>
    /// </list>
    /// The names are the constants of <c>PostgresFullText</c> (GuideAntsApi.BackgroundJobs), spelled out here so this
    /// migration never changes.
    /// </remarks>
    public partial class AddDocumentChunkFullText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TEXT SEARCH DICTIONARY guideants_simple_english (TEMPLATE = pg_catalog.simple, STOPWORDS = english);");
            migrationBuilder.Sql(
                "CREATE TEXT SEARCH CONFIGURATION guideants_english_nostem (COPY = pg_catalog.simple);");
            migrationBuilder.Sql(
                """
                ALTER TEXT SEARCH CONFIGURATION guideants_english_nostem
                    ALTER MAPPING FOR asciiword, word, numword, asciihword, hword, numhword, hword_asciipart, hword_part, hword_numpart
                    WITH guideants_simple_english;
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION guideants_fts_normalize(input text) RETURNS text
                    LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
                RETURN regexp_replace(regexp_replace(regexp_replace(regexp_replace(
                    input COLLATE "C",
                    '(?<![[:alnum:]_])c#(?![[:alnum:]_])', ' csharp ', 'gi'),
                    '(?<![[:alnum:]_])f#(?![[:alnum:]_])', ' fsharp ', 'gi'),
                    '(?<![[:alnum:]_])c\+\+(?![[:alnum:]_])', ' cplusplus ', 'gi'),
                    '(?<![[:alnum:]_])\.net(?![[:alnum:]_])', ' dotnet ', 'gi');
                """);
            migrationBuilder.Sql(
                """
                COMMENT ON FUNCTION guideants_fts_normalize(text) IS
                    'Rewrites standalone C#, F#, C++ and .NET (any case) to csharp, fsharp, cplusplus and dotnet before full-text parsing. Used by DocumentChunks.ContentTsv and by the hybrid search query.';
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "DocumentChunks" ADD COLUMN "ContentTsv" tsvector
                    GENERATED ALWAYS AS (to_tsvector('guideants_english_nostem'::regconfig, guideants_fts_normalize("Content"))) STORED;
                """);
            migrationBuilder.Sql(
                """CREATE INDEX "IX_DocumentChunks_ContentTsv" ON "DocumentChunks" USING GIN ("ContentTsv");""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_DocumentChunks_ContentTsv";""");
            migrationBuilder.Sql("""ALTER TABLE "DocumentChunks" DROP COLUMN IF EXISTS "ContentTsv";""");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS guideants_fts_normalize(text);");
            migrationBuilder.Sql("DROP TEXT SEARCH CONFIGURATION IF EXISTS guideants_english_nostem;");
            migrationBuilder.Sql("DROP TEXT SEARCH DICTIONARY IF EXISTS guideants_simple_english;");
        }
    }
}
