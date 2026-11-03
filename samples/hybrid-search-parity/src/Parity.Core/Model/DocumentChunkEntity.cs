namespace Parity.Core.Model;

/// <summary>
/// Production's DocumentChunk columns (GuideAntsApi.DataModel/Models/DocumentChunk.cs @ 9f8cd102),
/// without navigation properties. Embedding stays float[], as in production.
/// </summary>
public sealed class DocumentChunkEntity
{
    public Guid Id { get; set; }
    public string IndexName { get; set; } = string.Empty;
    public Guid? ContentFileId { get; set; }
    public Guid? NotebookFileId { get; set; }
    public Guid? AssistantFileId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? NotebookId { get; set; }
    public string? DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public DateTime CreatedUtc { get; set; }

    public static DocumentChunkEntity From(ChunkRecord chunk, DateTime createdUtc) => new()
    {
        Id = chunk.Id,
        IndexName = chunk.IndexName,
        ContentFileId = chunk.ContentFileId,
        NotebookFileId = null,
        AssistantFileId = null,
        ProjectId = chunk.ProjectId,
        NotebookId = chunk.NotebookId,
        DocumentId = chunk.DocumentId,
        ChunkIndex = chunk.ChunkIndex,
        Content = chunk.Content,
        Embedding = chunk.Embedding,
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc),
    };
}
