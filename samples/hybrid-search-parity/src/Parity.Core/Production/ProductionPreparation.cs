namespace Parity.Core.Production;

/// <summary>Mirrors HybridIndexer.IndexContentFileAsync: SanitizeForEmbedding, then ChunkText.</summary>
public static class ProductionPreparation
{
    public static List<string> PrepareChunks(string fileText)
    {
        var text = string.IsNullOrEmpty(fileText)
            ? fileText
            : MarkdownInlineImageSanitizer.ReplaceInlineImageDataUrls(fileText).SanitizedText;
        return ProductionChunker.ChunkText(text);
    }
}
