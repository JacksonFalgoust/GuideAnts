using System.Globalization;
using System.Text.RegularExpressions;

namespace Parity.SqlServer.Sql;

// Copied from HybridSearcher.SearchAsync @ 9f8cd102: the three values production interpolates into its SQL.
public static class ProductionEscaping
{
    public static string VectorJson(float[] queryVector) => System.Text.Json.JsonSerializer.Serialize(queryVector);

    /// <summary>production: var ftQuery = $"\"{query.Replace("\"", "\"\"")}\"";</summary>
    public static string FullTextPhrase(string query) => $"\"{query.Replace("\"", "\"\"")}\"";

    /// <summary>production: var escapedFtQuery = ftQuery.Replace("'", "''");</summary>
    public static string SqlLiteral(string ftQuery) => ftQuery.Replace("'", "''");
}

public static class SqlServerSql
{
    private static readonly Regex Placeholder = new(@"\{(vectorJson|escapedFtQuery|candidateLimit)\}", RegexOptions.Compiled);

    /// <summary>Production's SQL text, extracted byte for byte from HybridSearcher.cs @ 9f8cd102.</summary>
    public static string ProductionTemplate { get; } = LoadResource("Parity.SqlServer.ProductionHybrid.sql");

    /// <summary>The production line that picks the top-N text candidates across every project.</summary>
    public const string ProductionCandidateJoin = "LEFT JOIN CONTAINSTABLE(DocumentChunks, Content, '{escapedFtQuery}', {candidateLimit}) ft";

    private const string ScopedCandidateJoin = """
        LEFT JOIN (
                            SELECT TOP ({candidateLimit}) ct.[KEY], ct.[RANK]
                            FROM CONTAINSTABLE(DocumentChunks, Content, '{escapedFtQuery}') ct
                            INNER JOIN DocumentChunks scoped ON scoped.Id = ct.[KEY]
                            WHERE scoped.IndexName = @indexName
                                AND (@notebookId IS NULL OR scoped.NotebookId = @notebookId)
                            ORDER BY ct.[RANK] DESC
                        ) ft
        """;

    /// <summary>Informational only, never production: the scope filter is applied before the top-N cut.</summary>
    public static string QuirkFixedTemplate { get; } = ProductionTemplate.Replace(ProductionCandidateJoin, ScopedCandidateJoin);

    public static string BuildQuirkFixedHybrid(float[] vector, string query, int candidateLimit) =>
        Fill(QuirkFixedTemplate, vector, query, candidateLimit);

    public static string BuildProductionHybrid(float[] vector, string query, int candidateLimit) =>
        Fill(ProductionTemplate, vector, query, candidateLimit);

    /// <summary>
    /// Fills the template the way production's $@"" interpolation does. A single regex pass means
    /// placeholder-like text inside the user's query is never re-scanned.
    /// </summary>
    public static string Fill(string template, float[] vector, string query, int candidateLimit)
    {
        var values = new Dictionary<string, string>
        {
            ["vectorJson"] = ProductionEscaping.VectorJson(vector),
            ["escapedFtQuery"] = ProductionEscaping.SqlLiteral(ProductionEscaping.FullTextPhrase(query)),
            ["candidateLimit"] = candidateLimit.ToString(CultureInfo.InvariantCulture),
        };
        return Placeholder.Replace(template, m => values[m.Groups[1].Value]);
    }

    private static string LoadResource(string name)
    {
        using var stream = typeof(SqlServerSql).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
