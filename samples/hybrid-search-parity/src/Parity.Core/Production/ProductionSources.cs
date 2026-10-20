using System.Security.Cryptography;

namespace Parity.Core.Production;

/// <summary>The production files this sample copies, and their SHA-256 (LF line endings) at the copied commit.</summary>
public static class ProductionSources
{
    public const string Commit = "9f8cd102";
    public const string HybridSearcherPath = "src/server/GuideAntsApi.BackgroundJobs/Services/Search/HybridSearcher.cs";
    public const string HybridIndexerPath = "src/server/GuideAntsApi.BackgroundJobs/Services/Indexing/HybridIndexer.cs";
    public const string SanitizerPath = "src/server/GuideAntsApi.BackgroundJobs/Services/Indexing/MarkdownInlineImageSanitizer.cs";

    public static readonly IReadOnlyDictionary<string, string> Sha256AtCommit = new Dictionary<string, string>
    {
        [HybridSearcherPath] = "c8d0dabc3cd8a45c69e645862cc2bef0c8a39c848a37eeeb8b366c7a5750fc16",
        [HybridIndexerPath] = "0612e4d5d05fa049bd25efc0ade2a23266c79f93d37a4525e54aa0f42341e362",
        [SanitizerPath] = "e7b77e1854f6e6b72006f0eda7a7ca11e5ee8990b8f4abb6382b44ce2f7145ff",
    };

    /// <summary>SHA-256 of the file's bytes with CRLF turned into LF, so Windows checkouts hash the same.</summary>
    public static string HashNormalized(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var normalized = new List<byte>(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\r' && i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n')
            {
                continue;
            }

            normalized.Add(bytes[i]);
        }

        return Convert.ToHexString(SHA256.HashData(normalized.ToArray())).ToLowerInvariant();
    }
}
