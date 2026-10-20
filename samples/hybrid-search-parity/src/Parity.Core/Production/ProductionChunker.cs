namespace Parity.Core.Production;

// Copied verbatim from src/server/GuideAntsApi.BackgroundJobs/Services/Indexing/HybridIndexer.cs @ 9f8cd102
// (MaxChunkBytes and ChunkText). Only the class wrapper and the method's visibility differ.
public static class ProductionChunker
{
    private const int MaxChunkBytes = 2048;

    public static List<string> ChunkText(string text)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;

        // Byte-based chunking with 50% overlap
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        int start = 0;

        while (start < bytes.Length)
        {
            int chunkSize = Math.Min(MaxChunkBytes, bytes.Length - start);
            var chunkBytes = new byte[chunkSize];
            Array.Copy(bytes, start, chunkBytes, 0, chunkSize);
            
            var chunkText = System.Text.Encoding.UTF8.GetString(chunkBytes);
            chunks.Add(chunkText);

            // Move start position with 50% overlap
            int stepSize = Math.Max(1, chunkSize / 2);
            start += stepSize;
            
            // Break if remaining text is less than half a chunk
            if (start >= bytes.Length - stepSize) break;
        }

        return chunks;
    }
}
