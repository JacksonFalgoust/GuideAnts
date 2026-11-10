using System.Text.RegularExpressions;
using Parity.Core;

namespace Parity.Postgres;

public static class PostgresSchema
{
    /// <summary>English stop words like SQL Server's system stoplist, with no stemming (phrase search matches whole words).</summary>
    public const string CustomConfigName = "parity_english_nostem";

    public static readonly string[] CreateCustomTextSearchConfig =
    [
        "CREATE TEXT SEARCH DICTIONARY parity_simple_english (TEMPLATE = pg_catalog.simple, STOPWORDS = english)",
        "CREATE TEXT SEARCH CONFIGURATION parity_english_nostem (COPY = pg_catalog.simple)",
        "ALTER TEXT SEARCH CONFIGURATION parity_english_nostem ALTER MAPPING FOR asciiword, word, numword, asciihword, hword, numhword, hword_asciipart, hword_part, hword_numpart WITH parity_simple_english",
    ];

    public const string CreateTsvIndex = """CREATE INDEX "IX_DocumentChunks_ContentTsv" ON "DocumentChunks" USING GIN ("ContentTsv")""";

    private static readonly Regex ConfigName = new("^[a-z_][a-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>The config name is spliced into DDL, so only plain lowercase identifiers are allowed.</summary>
    public static void ValidateConfigName(string config)
    {
        if (!ConfigName.IsMatch(config))
        {
            throw new ParityConfigurationException(
                $"TextSearchConfig '{config}' isn't a valid configuration name (lowercase letters, digits and underscores only).");
        }
    }

    /// <summary>A text normalizer function name is spliced into queries too, so the same rule applies.</summary>
    public static void ValidateFunctionName(string function)
    {
        if (!ConfigName.IsMatch(function))
        {
            throw new ParityConfigurationException(
                $"Text normalizer '{function}' isn't a valid function name (lowercase letters, digits and underscores only).");
        }
    }

    public static string AddTsvColumn(string config) =>
        $"""ALTER TABLE "DocumentChunks" ADD COLUMN "ContentTsv" tsvector GENERATED ALWAYS AS (to_tsvector('{config}'::regconfig, "Content")) STORED""";
}
