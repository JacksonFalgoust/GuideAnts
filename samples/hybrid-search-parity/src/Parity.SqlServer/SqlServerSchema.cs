namespace Parity.SqlServer;

public static class SqlServerSchema
{
    // Copied verbatim from GuideAntsApi.DataModel/Migrations/20260325193437_LiteBaselineV1.cs @ 9f8cd102.
    public const string CreateFullTextCatalog = @"
                IF NOT EXISTS (SELECT * FROM sys.fulltext_catalogs WHERE name = 'DocumentChunks_Catalog')
                IF FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 1
                BEGIN
                    CREATE FULLTEXT CATALOG DocumentChunks_Catalog;
                END
                ";

    // Copied verbatim from the same migration: default language, system stoplist, automatic change tracking.
    public const string CreateFullTextIndex = @"
                IF FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 1 AND NOT EXISTS (SELECT * FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('DocumentChunks'))
                BEGIN
                    CREATE FULLTEXT INDEX ON DocumentChunks(Content)
                    KEY INDEX PK_DocumentChunks
                    ON DocumentChunks_Catalog;
                END
                ";

    public const string IsFullTextInstalled = "SELECT CAST(FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') AS int)";

    /// <summary>Populate status (0 = idle), pending changes, indexed rows, total rows.</summary>
    public const string PopulationStatus = """
        SELECT ISNULL(CAST(FULLTEXTCATALOGPROPERTY('DocumentChunks_Catalog', 'PopulateStatus') AS int), -1),
               ISNULL(CAST(OBJECTPROPERTYEX(OBJECT_ID('DocumentChunks'), 'TableFulltextPendingChanges') AS int), -1),
               ISNULL(CAST(OBJECTPROPERTYEX(OBJECT_ID('DocumentChunks'), 'TableFulltextItemCount') AS int), -1),
               (SELECT COUNT(*) FROM DocumentChunks)
        """;
}
