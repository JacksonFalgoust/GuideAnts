using Parity.Core.Data;
using Parity.Core.Ids;
using Parity.Core.Model;
using Parity.Core.Production;

namespace Parity.Core.Corpus;

public sealed record ChunkDraft(
    Guid Id,
    string IndexName,
    Guid ProjectId,
    Guid? NotebookId,
    string DocumentId,
    Guid ContentFileId,
    string SourcePath,
    int ChunkIndex,
    ChunkKind Kind,
    string Content)
{
    public ChunkRecord WithEmbedding(float[] embedding) =>
        new(Id, IndexName, ProjectId, NotebookId, DocumentId, ContentFileId, SourcePath, ChunkIndex, Kind, Content, embedding);
}

/// <summary>Spec section 5.2: project, notebook and ID assignment.</summary>
public static class CorpusBuilder
{
    public static IReadOnlyList<ChunkDraft> Build(IReadOnlyList<CorpusFile> docs, IReadOnlyList<EdgeCaseDefinition> edgeCases)
    {
        var drafts = new List<ChunkDraft>();
        var ordered = docs.OrderBy(d => d.Path, StringComparer.Ordinal).ToList();
        var p1Files = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var file = ordered[i];
            var label = ParityScopes.ProjectLabels[i % ParityScopes.ProjectLabels.Length];
            Guid? notebookId = null;
            if (label == "P1")
            {
                if (p1Files % 3 == 2)
                {
                    notebookId = ParityScopes.NotebookN1; // every third file assigned to P1
                }

                p1Files++;
            }

            var projectId = ParityScopes.ProjectId(label);
            var chunks = ProductionPreparation.PrepareChunks(file.Content);
            for (var c = 0; c < chunks.Count; c++)
            {
                drafts.Add(new ChunkDraft(
                    DeterministicGuid.Create($"doc:{file.Path}#{c}"),
                    projectId.ToString(),
                    projectId,
                    notebookId,
                    file.Path,
                    DeterministicGuid.Create($"file:{file.Path}"),
                    file.Path,
                    c,
                    ChunkKind.Doc,
                    chunks[c]));
            }
        }

        for (var j = 0; j < edgeCases.Count; j++)
        {
            var edge = edgeCases[j];
            var projectId = ParityScopes.ProjectId(ParityScopes.ProjectLabels[j % ParityScopes.ProjectLabels.Length]);
            drafts.Add(new ChunkDraft(
                DeterministicGuid.Create($"edge:{edge.Id}"),
                projectId.ToString(),
                projectId,
                null,
                $"edge:{edge.Id}",
                DeterministicGuid.Create($"file:edge:{edge.Id}"),
                $"inputs/edge-cases.json#{edge.Id}",
                0,
                ChunkKind.Edge,
                edge.Content));
        }

        return drafts;
    }
}
