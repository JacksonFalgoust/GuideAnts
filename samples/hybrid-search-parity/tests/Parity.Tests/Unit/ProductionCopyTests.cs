using Parity.Core;
using Parity.Core.Production;
using Parity.SqlServer.Sql;

namespace Parity.Tests.Unit;

public class ProductionCopyTests
{
    [Theory]
    [InlineData(ProductionSources.HybridSearcherPath)]
    [InlineData(ProductionSources.HybridIndexerPath)]
    [InlineData(ProductionSources.SanitizerPath)]
    public void CopiedProductionFileIsUnchangedSinceTheCopy(string path)
    {
        var actual = ProductionSources.HashNormalized(Path.Combine(RepoPaths.FindRepoRoot(), path));
        Assert.True(
            actual == ProductionSources.Sha256AtCommit[path],
            $"production changed since the copy at {ProductionSources.Commit}; re-check and re-sync ({path})");
    }

    [Fact]
    public void EmbeddedSqlIsProductionsQueryVerbatim()
    {
        var source = File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), ProductionSources.HybridSearcherPath)).Replace("\r\n", "\n");
        const string marker = "var sql = $@\"";
        var from = source.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var to = source.IndexOf("\";", from, StringComparison.Ordinal);
        Assert.Equal(source[from..to], SqlServerSql.ProductionTemplate.Replace("\r\n", "\n"));
    }

    [Fact]
    public void FillReplacesPlaceholdersInOnePassWithoutRescanningUserText()
    {
        var sql = SqlServerSql.BuildProductionHybrid([0.5f], "it's {candidateLimit} \"x\"", 10);
        Assert.Contains("CAST('[0.5]' AS vector(1536))", sql);
        Assert.Contains("CONTAINSTABLE(DocumentChunks, Content, '\"it''s {candidateLimit} \"\"x\"\"\"', 10)", sql);
    }

    [Fact]
    public void BlankAndShortTextGiveZeroOrOneChunk()
    {
        Assert.Empty(ProductionChunker.ChunkText("   "));
        Assert.Single(ProductionChunker.ChunkText(new string('a', 1000)));
        Assert.Single(ProductionChunker.ChunkText(new string('a', 2048)));
    }

    [Fact]
    public void ChunksOverlapByHalfAndStopEarlyLikeProduction()
    {
        Assert.Equal(new[] { 2048, 1976 }, ProductionChunker.ChunkText(new string('a', 3000)).Select(c => c.Length).ToArray());
        Assert.Equal(new[] { 2048, 2048, 2048, 1928 }, ProductionChunker.ChunkText(new string('a', 5000)).Select(c => c.Length).ToArray());
    }

    [Fact]
    public void ByteChunkingSplitsMultiByteCharactersLikeProduction()
    {
        var chunks = ProductionChunker.ChunkText(string.Concat(Enumerable.Repeat("€", 1000))); // 3,000 bytes
        Assert.StartsWith("€€", chunks[0]);
        Assert.EndsWith("�", chunks[0]);
    }

    [Fact]
    public void SanitizerReplacesInlineImagePayloads()
    {
        var (text, count) = MarkdownInlineImageSanitizer.ReplaceInlineImageDataUrls("before ![x](data:image/png;base64,iVBORw0KGgo=) after");
        Assert.Equal(1, count);
        Assert.Equal("before ![x]([inline-image omitted]) after", text);
    }

    [Fact]
    public void PreparationSanitizesBeforeChunking()
    {
        // Production's character class includes whitespace, so the ')' is what ends the payload.
        var chunks = ProductionPreparation.PrepareChunks("a (data:image/png;base64,AAAA) b");
        Assert.Equal(new[] { "a ([inline-image omitted]) b" }, chunks);
    }
}
