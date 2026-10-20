using Parity.Core;
using Parity.Core.Corpus;
using Parity.Core.Data;
using Parity.Core.Ids;
using Parity.Core.Model;
using Parity.Core.Production;

namespace Parity.Tests.Unit;

public class CorpusTests
{
    [Fact]
    public void GitSourceReadsTheSeventyNineTopLevelDocsAtThePinnedCommit()
    {
        var source = new GitDocsSource(RepoPaths.FindRepoRoot(), ProductionSources.Commit);
        var docs = source.ReadDocs();

        Assert.StartsWith(ProductionSources.Commit, source.ResolvedCommit);
        Assert.Equal(79, docs.Count);
        Assert.All(docs, d => Assert.Matches(@"^docs/[^/]+\.md$", d.Path));
        Assert.Equal(docs.Select(d => d.Path).OrderBy(p => p, StringComparer.Ordinal), docs.Select(d => d.Path));
        Assert.DoesNotContain(docs, d => d.Path.EndsWith("efdoctor-findings-2026-10-15.md", StringComparison.Ordinal));
        Assert.Equal(1_194_882, docs.Sum(d => System.Text.Encoding.UTF8.GetByteCount(d.Content)));
    }

    [Fact]
    public void FilesAreAssignedRoundRobinAndEveryThirdP1FileGetsTheNotebook()
    {
        var docs = Enumerable.Range(0, 9).Select(i => new CorpusFile($"docs/{(char)('a' + i)}.md", $"file {i}")).Reverse().ToList();
        var drafts = CorpusBuilder.Build(docs, []);

        var projectOf = drafts.ToDictionary(d => d.SourcePath, d => d.ProjectId);
        Assert.Equal(ParityScopes.ProjectId("P1"), projectOf["docs/a.md"]);
        Assert.Equal(ParityScopes.ProjectId("P2"), projectOf["docs/b.md"]);
        Assert.Equal(ParityScopes.ProjectId("P3"), projectOf["docs/c.md"]);
        Assert.Equal(ParityScopes.ProjectId("P1"), projectOf["docs/d.md"]);
        var withNotebook = drafts.Where(d => d.NotebookId == ParityScopes.NotebookN1).Select(d => d.SourcePath).ToList();
        Assert.Equal(new[] { "docs/g.md" }, withNotebook);
    }

    [Fact]
    public void ChunkFieldsAreDeterministic()
    {
        var drafts = CorpusBuilder.Build([new CorpusFile("docs/a.md", new string('x', 3000))], [new EdgeCaseDefinition("E01", "edge")]);

        var doc = drafts[1];
        Assert.Equal(DeterministicGuid.Create("doc:docs/a.md#1"), doc.Id);
        Assert.Equal(ParityScopes.ProjectId("P1").ToString(), doc.IndexName);
        Assert.Equal(DeterministicGuid.Create("file:docs/a.md"), doc.ContentFileId);
        Assert.Equal(("docs/a.md", 1, ChunkKind.Doc), (doc.DocumentId, doc.ChunkIndex, doc.Kind));

        var edge = drafts[^1];
        Assert.Equal(DeterministicGuid.Create("edge:E01"), edge.Id);
        Assert.Equal(("edge:E01", "inputs/edge-cases.json#E01", 0, ChunkKind.Edge), (edge.DocumentId, edge.SourcePath, edge.ChunkIndex, edge.Kind));
        Assert.Null(edge.NotebookId);
    }

    [Fact]
    public void EdgeCasesAreSpreadAcrossProjectsInFileOrder()
    {
        var edges = Enumerable.Range(1, 4).Select(i => new EdgeCaseDefinition($"E0{i}", $"edge {i}")).ToList();
        var labels = CorpusBuilder.Build([], edges).Select(d => d.ProjectId).ToList();
        Assert.Equal(new[] { "P1", "P2", "P3", "P1" }.Select(ParityScopes.ProjectId), labels);
    }

    [Theory]
    [InlineData("lock", "the conversation lock is held", true)]
    [InlineData("lock", "a block of text", false)]
    [InlineData("conversation lock", "CONVERSATION\n  LOCK.", true)]
    [InlineData("C#", "written in C# and F#", true)]
    [InlineData("C#", "written in C#x", false)]
    [InlineData("say \"hello world\" twice", "should say \"hello world\" twice before", true)]
    [InlineData("published guide", "the published, guide-adjacent pages", false)]
    public void GroundTruthIsWholeWordCaseInsensitiveAndWhitespaceFlexible(string phrase, string text, bool expected)
    {
        Assert.Equal(expected, GroundTruth.PhraseRegex(phrase).IsMatch(text));
    }

    [Fact]
    public void ScopesAreTheFourFromTheSpec()
    {
        Assert.Equal(new[] { "P1", "P2", "P3", "P1/N1" }, ParityScopes.All.Select(s => s.Label));
        Assert.Equal(ParityScopes.NotebookN1, ParityScopes.Find("p1/n1").Scope.NotebookId);
        var error = Assert.Throws<ParityConfigurationException>(() => ParityScopes.Find("P9"));
        Assert.Contains("P1, P2, P3, P1/N1", error.Message);
    }

    [Fact]
    public void ReviewedInputsMatchTheSpecAppendices()
    {
        var root = RepoPaths.FindSampleRoot();
        var queries = DataStore.ReadQueries(root);
        var edges = DataStore.ReadEdgeCases(root);

        Assert.Equal(Enumerable.Range(1, 38).Select(i => $"Q{i:00}"), queries.Select(q => q.Id));
        Assert.Equal(Enumerable.Range(1, 20).Select(i => $"E{i:00}"), edges.Select(e => e.Id));
        Assert.All(queries, q => Assert.Contains(q.Group, QueryGroups.All));
        var counts = queries.GroupBy(q => q.Group).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(10, counts[QueryGroups.Exact]);
        Assert.Equal(8, counts[QueryGroups.Paraphrase]);
        Assert.Equal(16, counts[QueryGroups.Edge]);
        Assert.Equal(2, counts[QueryGroups.Error]);
        Assert.Equal(2, counts[QueryGroups.Stress]);
    }

    [Fact]
    public void EveryExactAndEdgeQueryHasGroundTruthExceptTheCjkPhrase()
    {
        var root = RepoPaths.FindSampleRoot();
        var docs = new GitDocsSource(RepoPaths.FindRepoRoot(), ProductionSources.Commit).ReadDocs();
        var drafts = CorpusBuilder.Build(docs, DataStore.ReadEdgeCases(root));
        var truth = GroundTruth.Compute(DataStore.ReadQueries(root), drafts);

        Assert.Equal(26, truth.Count);
        foreach (var (id, matches) in truth)
        {
            if (id == "Q34")
            {
                // \w boundaries never fire inside running CJK text, so the oracle is empty for Q34 by design.
                Assert.Empty(matches);
            }
            else
            {
                Assert.True(matches.Count > 0, $"{id} has no ground-truth match");
            }
        }
    }
}
