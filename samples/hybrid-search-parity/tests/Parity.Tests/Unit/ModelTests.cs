using Parity.Core.Model;
using Parity.Core.Settings;

namespace Parity.Tests.Unit;

public class ModelTests
{
    [Fact]
    public void CandidateLimitIsMaxResultsTimesMultiplier()
    {
        var query = new HybridQuery("q", new float[1536], new Scope("p", null), MaxResults: 5, VectorWeight: 0.7, TextWeight: 0.3, CandidateMultiplier: 2);
        Assert.Equal(10, query.CandidateLimit);
    }

    [Fact]
    public void EngineResultCarriesValueOrError()
    {
        var ok = EngineResult<int>.Ok(3);
        var failed = EngineResult<int>.Fail(new EngineError("SQL7619", "noise words only"));

        Assert.True(ok.IsSuccess);
        Assert.Equal(3, ok.Value);
        Assert.False(failed.IsSuccess);
        Assert.Equal("SQL7619", failed.Error!.Code);
        var thrown = Assert.Throws<InvalidOperationException>(() => failed.Unwrap("vector search"));
        Assert.Contains("vector search failed: [SQL7619] noise words only", thrown.Message);
    }

    [Fact]
    public void DocumentChunkEntityCopiesEveryColumnAndForcesUtc()
    {
        var chunk = new ChunkRecord(Guid.NewGuid(), "idx", Guid.NewGuid(), Guid.NewGuid(), "docs/a.md", Guid.NewGuid(), "docs/a.md", 3, ChunkKind.Doc, "text", [1f, 2f]);
        var entity = DocumentChunkEntity.From(chunk, new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Unspecified));

        Assert.Equal(chunk.Id, entity.Id);
        Assert.Equal("idx", entity.IndexName);
        Assert.Equal(chunk.ContentFileId, entity.ContentFileId);
        Assert.Null(entity.NotebookFileId);
        Assert.Null(entity.AssistantFileId);
        Assert.Equal(chunk.ProjectId, entity.ProjectId);
        Assert.Equal(chunk.NotebookId, entity.NotebookId);
        Assert.Equal("docs/a.md", entity.DocumentId);
        Assert.Equal(3, entity.ChunkIndex);
        Assert.Equal("text", entity.Content);
        Assert.Same(chunk.Embedding, entity.Embedding);
        Assert.Equal(DateTimeKind.Utc, entity.CreatedUtc.Kind);
    }

    [Fact]
    public void SettingDefaultsMatchTheSpec()
    {
        var settings = new ParitySettings();
        Assert.Equal(5, settings.Hybrid.MaxResults);
        Assert.Equal(0.7, settings.Hybrid.VectorWeight);
        Assert.Equal(0.3, settings.Hybrid.TextWeight);
        Assert.Equal(2, settings.Hybrid.CandidateMultiplier);
        Assert.Equal(10, settings.Parity.VectorTopK);
        Assert.Equal(1e-5, settings.Parity.VectorTolerance);
        Assert.Equal(1e-6, settings.Parity.TieTolerance);
        Assert.Equal(0.9, settings.Parity.Thresholds.MinMeanTop5Overlap);
        Assert.Equal(0.8, settings.Parity.Thresholds.MinSameTopOneRate);
        Assert.Equal("ghcr.io/elumenotion/mssql2025-express-fts:main", settings.Images.SqlServer);
        Assert.Equal("pgvector/pgvector:0.8.7-pg18", settings.Images.Postgres);
        Assert.Equal("text-embedding-3-small", settings.OpenAI.Model);
    }

    [Fact]
    public void ProfileLookupIgnoresCase()
    {
        var settings = new ParitySettings();
        settings.Profiles["Postgres"] = new ProfileSettings { Provider = "Postgres" };
        Assert.True(settings.Profiles.ContainsKey("postgres"));
    }
}
