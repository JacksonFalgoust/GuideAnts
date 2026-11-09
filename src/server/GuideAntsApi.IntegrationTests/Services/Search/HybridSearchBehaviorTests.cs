using System.Data.Common;
using FluentAssertions;
using GuideAntsApi.BackgroundJobs;
using GuideAntsApi.BackgroundJobs.Services.Embeddings;
using GuideAntsApi.BackgroundJobs.Services.Search;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using GuideAntsApi.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GuideAntsApi.IntegrationTests.Services.Search;

/// <summary>
/// Hybrid search (full-text plus vector) through the <see cref="IHybridSearcher"/> that
/// <see cref="ServiceCollectionExtensions.AddBackgroundJobs"/> registers for the suite's provider: the production
/// <c>CONTAINSTABLE</c> searcher on SQL Server, the <c>tsvector</c>/pgvector searcher on Postgres. The bar is
/// functional (a user finds what they're looking for), not identical scores: <c>CONTAINSTABLE</c> RANK and
/// <c>ts_rank_cd</c> are different functions, so the tests check which chunks match and how they rank, and SQL
/// Server's results are the reference.
/// </summary>
/// <remarks>
/// <para>
/// Each test writes its chunks under its own <c>IndexName</c> and removes them afterwards. The query embedding is
/// fixed (<see cref="QueryVector"/>, the first axis); a chunk's embedding is a unit vector at a chosen cosine
/// similarity to it, so <c>VectorScore</c> is known in advance.
/// </para>
/// <para>
/// SQL Server fills its full-text index in the background, so the tests wait until the catalog has caught up with
/// the table before they search. Postgres computes the <c>tsvector</c> column as each row is written.
/// </para>
/// </remarks>
[TestClass]
public sealed class HybridSearchBehaviorTests
{
    private const int Dimensions = 1536;
    private const string IndexPrefix = "hybrid-search-test-";

    private static readonly float[] QueryVector = UnitVector(1.0, axis: 1);

    private static TestWebApplicationFactory? _factory;

    private string _connectionString = null!;
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private ServiceProvider _services = null!;
    private ListLoggerProvider _logs = null!;
    private string _indexName = null!;
    private int _nextChunkIndex;

    private static DatabaseProvider Provider => TestContainerManager.Instance.Provider;

    private static bool OnPostgres => Provider == DatabaseProvider.Postgres;

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
            _factory = null;
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        _connectionString = await TestContainerManager.Instance.GetConnectionStringAsync();
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(
            builder, Provider, _connectionString, new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true));
        _options = builder.Options;
        _indexName = IndexPrefix + Guid.NewGuid().ToString("N");

        _logs = new ListLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton(new DatabaseProviderInfo(Provider));
        // The host's pooled factory, which the searchers use (StartupConfiguration.ConfigureDatabase).
        services.AddPooledDbContextFactory<ApplicationDbContext>(options => ApplicationDbContextOptions.Configure(
            options, Provider, _connectionString, new DbProviderTuning(300, UseVectorSearch: true, ExplicitSingleQuery: true)));
        services.AddBackgroundJobs(new ConfigurationBuilder().Build());
        services.AddSingleton<IEmbeddingService>(new FixedEmbeddingService(QueryVector));
        _services = services.BuildServiceProvider();

        // Chunks left by an earlier, interrupted run would compete for the table-wide full-text candidates.
        await using var db = CreateContext();
        await db.DocumentChunks.Where(c => c.IndexName.StartsWith(IndexPrefix)).ExecuteDeleteAsync();
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await using (var db = CreateContext())
        {
            await db.DocumentChunks.Where(c => c.IndexName == _indexName).ExecuteDeleteAsync();
        }

        await _services.DisposeAsync();
    }

    // ----- Terms with symbols (C#, C++, .NET, F#) -----

    /// <summary>
    /// Postgres's parser drops the <c>#</c>, so without normalization the phrase <c>C#</c> is the bare letter
    /// <c>c</c> and matches "Plan C". SQL Server's word breaker keeps <c>c#</c> as one token.
    /// </summary>
    [TestMethod]
    public async Task Csharp_query_finds_csharp_chunks_not_bare_c()
    {
        var csharp = await AddChunkAsync("Writing C# services", UnitVector(0.5, axis: 2));
        var planC = await AddChunkAsync("Plan C is the fallback", UnitVector(0.5, axis: 3));
        var cplusplus = await AddChunkAsync("C++ templates", UnitVector(0.5, axis: 4));

        var results = await SearchAsync("C#", vectorWeight: 0, textWeight: 1);

        results.Should().HaveCount(3);
        results[0].ChunkId.Should().Be(csharp);
        results[0].TextScore.Should().BePositive();
        TextScoreOf(results, planC).Should().Be(0);
        TextScoreOf(results, cplusplus).Should().Be(0);
    }

    [TestMethod]
    public async Task Cplusplus_and_dotnet_queries_match_their_chunks()
    {
        var cplusplus = await AddChunkAsync("C++ templates", UnitVector(0.5, axis: 2));
        var planC = await AddChunkAsync("Plan C is the fallback", UnitVector(0.5, axis: 3));
        var csharp = await AddChunkAsync("Writing C# services", UnitVector(0.5, axis: 4));
        var dotnet = await AddChunkAsync(".NET 8 hosting", UnitVector(0.5, axis: 5));
        var net = await AddChunkAsync("The net result of hosting", UnitVector(0.5, axis: 6));

        var cplusplusResults = await SearchAsync("C++", vectorWeight: 0, textWeight: 1);
        cplusplusResults[0].ChunkId.Should().Be(cplusplus);
        cplusplusResults[0].TextScore.Should().BePositive();
        cplusplusResults.Where(r => r.ChunkId != cplusplus).Should().OnlyContain(r => r.TextScore == 0);

        var dotnetResults = await SearchAsync(".NET", vectorWeight: 0, textWeight: 1);
        dotnetResults[0].ChunkId.Should().Be(dotnet);
        dotnetResults[0].TextScore.Should().BePositive();
        TextScoreOf(dotnetResults, net).Should().Be(0, "\".NET\" is not the word \"net\"");
        dotnetResults.Where(r => r.ChunkId != dotnet).Should().OnlyContain(r => r.TextScore == 0);

        // Lower case and a phrase around the term match too.
        (await SearchAsync("c++ templates", vectorWeight: 0, textWeight: 1))[0].ChunkId.Should().Be(cplusplus);
        (await SearchAsync(".net", vectorWeight: 0, textWeight: 1))[0].ChunkId.Should().Be(dotnet);
        (await SearchAsync("c# services", vectorWeight: 0, textWeight: 1))[0].ChunkId.Should().Be(csharp);
        TextScoreOf(await SearchAsync("C++", vectorWeight: 0, textWeight: 1), planC).Should().Be(0);
    }

    /// <summary>
    /// SQL Server's English word breaker has no <c>F#</c> token: it splits off the letter <c>f</c>, a noise word, so
    /// an <c>F#</c> query matches nothing there. On Postgres the normalization rewrites <c>F#</c> like <c>C#</c>, so
    /// the query finds the F# chunk (parity report: Postgres is the more useful of the two here).
    /// </summary>
    [TestMethod]
    public async Task Fsharp_query_matches_fsharp_chunks_on_postgres_only()
    {
        var fsharp = await AddChunkAsync("Pattern matching in F# modules", UnitVector(0.5, axis: 2));
        var planF = await AddChunkAsync("Plan F is the last resort", UnitVector(0.5, axis: 3));

        var results = await SearchAsync("F#", vectorWeight: 0, textWeight: 1);

        results.Should().HaveCount(2);
        if (OnPostgres)
        {
            results[0].ChunkId.Should().Be(fsharp);
            results[0].TextScore.Should().BePositive();
            TextScoreOf(results, planF).Should().Be(0);
        }
        else
        {
            results.Should().OnlyContain(r => r.TextScore == 0, "SQL Server reads \"F#\" as the noise word \"f\"");
        }
    }

    /// <summary>
    /// The terms are rewritten only where they stand alone: inside a longer word (<c>xc#</c>, <c>C#x</c>,
    /// <c>ASP.NET</c>) the text is left to the parser. SQL Server's word breaker also indexes <c>asp.net</c> as one
    /// token (plus <c>asp</c> and <c>net</c>), so <c>.NET</c> doesn't find "ASP.NET" there either.
    /// Punctuation around a term (brackets, a slash, a full stop, a hyphen) still counts as a boundary.
    /// </summary>
    [TestMethod]
    public async Task Normalization_rewrites_only_standalone_terms()
    {
        DatabaseTestProvider.RequirePostgres();

        var cases = new Dictionary<string, string>
        {
            ["Writing C# services"] = "'csharp':2 'services':3 'writing':1",
            // "and" is a stop word: it keeps its position but has no lexeme.
            ["(C#), C#. and C#/.NET"] = "'csharp':1,2,4 'dotnet':5",
            ["c#-based f# and .Net"] = "'based':2 'csharp':1 'dotnet':5 'fsharp':3",
            // "/cli" is the parser's file-path token, like any other slash-prefixed word.
            ["C++ and c++/cli"] = "'/cli':4 'cplusplus':1,3",
            ["xc# C#x ASP.NET"] = "'asp.net':4 'c':2 'x':3 'xc':1",
            ["Plan C is the fallback"] = "'c':2 'fallback':5 'plan':1",
        };

        await using var db = CreateContext();
        foreach (var (text, expected) in cases)
        {
            var vector = await db.Database
                .SqlQuery<string>($"SELECT to_tsvector({PostgresFullText.Config}::regconfig, guideants_fts_normalize({text}))::text AS \"Value\"")
                .SingleAsync();
            vector.Should().Be(expected, $"\"{text}\" normalizes to those lexemes");
        }
    }

    // ----- Phrases, scope and the vector half -----

    [TestMethod]
    public async Task Phrase_search_requires_the_words_together()
    {
        var together = await AddChunkAsync("the conversation lock expires", UnitVector(0.5, axis: 2));
        var apart = await AddChunkAsync("lock the conversation", UnitVector(0.5, axis: 3));

        var results = await SearchAsync("conversation lock");

        results.Select(r => r.ChunkId).Should().ContainInOrder(together, apart);
        TextScoreOf(results, together).Should().BePositive();
        TextScoreOf(results, apart).Should().Be(0);
    }

    [TestMethod]
    public async Task Notebook_scope_filters_results()
    {
        var notebookA = Guid.NewGuid();
        var notebookB = Guid.NewGuid();
        var inA = await AddChunkAsync("Retention notes for notebook A", UnitVector(0.6, axis: 2), notebookA);
        var alsoInA = await AddChunkAsync("Unrelated text in notebook A", UnitVector(0.4, axis: 3), notebookA);
        var inB = await AddChunkAsync("Retention notes for notebook B", UnitVector(0.6, axis: 4), notebookB);

        var scoped = await SearchAsync("retention notes", notebookId: notebookA);
        scoped.Select(r => r.ChunkId).Should().BeEquivalentTo([inA, alsoInA]);
        scoped[0].ChunkId.Should().Be(inA);

        var whole = await SearchAsync("retention notes");
        whole.Select(r => r.ChunkId).Should().BeEquivalentTo([inA, alsoInA, inB]);
    }

    [TestMethod]
    public async Task Vector_only_match_is_returned_when_no_text_matches()
    {
        var near = await AddChunkAsync("Quarterly revenue summary", UnitVector(0.9, axis: 2));
        var far = await AddChunkAsync("Holiday schedule for the office", UnitVector(0.2, axis: 3));

        var results = await SearchAsync("zebra quantum");

        results.Select(r => r.ChunkId).Should().Equal(near, far);
        results[0].TextScore.Should().Be(0);
        results[0].VectorScore.Should().BeApproximately(0.9, 1e-3);
        results[0].Score.Should().BeApproximately(0.7 * 0.9, 1e-3);
        results[1].VectorScore.Should().BeApproximately(0.2, 1e-3);
    }

    /// <summary>
    /// <c>RebuildEmbeddingsHandler</c> zeroes every embedding before it regenerates them. SQL Server's cosine
    /// distance to a zero vector is 1 (similarity 0); pgvector's is NaN, which would sort above every real score.
    /// </summary>
    [TestMethod]
    public async Task Zero_embedding_counts_as_unrelated()
    {
        var zeroed = await AddChunkAsync("Retention policy draft", new float[Dimensions]);
        var embedded = await AddChunkAsync("Holiday schedule", UnitVector(0.5, axis: 2));

        var results = await SearchAsync("retention policy");

        results.Select(r => r.ChunkId).Should().BeEquivalentTo([zeroed, embedded]);
        var zeroedResult = results.Single(r => r.ChunkId == zeroed);
        zeroedResult.VectorScore.Should().Be(0);
        zeroedResult.TextScore.Should().BePositive();
        zeroedResult.Score.Should().BeApproximately(0.3 * zeroedResult.TextScore, 1e-9);
        results.Should().OnlyContain(r => !double.IsNaN(r.Score) && !double.IsNaN(r.VectorScore));

        // With no text match a zero vector is not "near" anything, so it drops out, as on SQL Server.
        (await SearchAsync("holiday schedule")).Select(r => r.ChunkId).Should().Equal(embedded);
    }

    // ----- Inputs the full-text syntax could trip over -----

    /// <summary>
    /// Neither searcher passes the text through a query syntax: SQL Server wraps it in a <c>CONTAINSTABLE</c>
    /// phrase, Postgres parses it with <c>phraseto_tsquery</c>, which treats operators as punctuation. Text with
    /// only punctuation or stop words has no searchable term, so it matches no text (SQL Server reports
    /// "Informational: The full-text search condition contained noise word(s)", Postgres a NOTICE) and the vector
    /// half still answers.
    /// </summary>
    [TestMethod]
    public async Task Queries_with_operators_punctuation_or_only_stop_words_still_return_vector_matches()
    {
        var chunk = await AddChunkAsync("Quarterly revenue summary", UnitVector(0.9, axis: 2));
        string[] queries =
        [
            "'", "&", "|", "!", ":", "(", ")", "\\", "\"", "''", "\"\"",
            "a & b | !c", "foo:* & (bar | baz)", "<->", "it's", "the", "the and of", "C:\\temp\\file.txt",
            "x' OR '1'='1", "'; DROP TABLE \"DocumentChunks\"; --",
        ];

        foreach (var query in queries)
        {
            var results = await SearchAsync(query);

            results.Should().ContainSingle($"query {query} still gets its vector match")
                .Which.ChunkId.Should().Be(chunk);
            results[0].TextScore.Should().Be(0);
        }

        _logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Error);
    }

    /// <summary>
    /// Postgres text can't hold U+0000, so a NUL in a query parameter fails the whole statement. The Postgres searcher
    /// replaces it with U+FFFD, as the model adapter does for stored text (parity report #35).
    /// </summary>
    [TestMethod]
    public async Task Query_with_a_nul_character_still_returns_vector_matches()
    {
        var chunk = await AddChunkAsync("Quarterly revenue summary", UnitVector(0.9, axis: 2));

        var results = await SearchAsync("revenue\0summary");

        results.Should().ContainSingle().Which.ChunkId.Should().Be(chunk);
        _logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Error);
    }

    [TestMethod]
    public async Task Blank_query_returns_no_results()
    {
        await AddChunkAsync("Quarterly revenue summary", UnitVector(0.9, axis: 2));

        foreach (var query in new[] { "", " ", "\t\n " })
        {
            (await SearchAsync(query)).Should().BeEmpty();
        }
    }

    // ----- Registration in the host -----

    [TestMethod]
    public async Task Host_resolves_the_searcher_for_its_provider()
    {
        _factory ??= await StartFactoryAsync();
        using var scope = _factory.Services.CreateScope();

        var searcher = scope.ServiceProvider.GetRequiredService<IHybridSearcher>();

        searcher.GetType().Name.Should().Be(OnPostgres ? "PostgresHybridSearcher" : "HybridSearcher");
    }

    // ----- Helpers -----

    private ApplicationDbContext CreateContext() => new(_options);

    private async Task<IReadOnlyList<HybridSearchResult>> SearchAsync(
        string query, Guid? notebookId = null, double vectorWeight = 0.7, double textWeight = 0.3)
    {
        await using var scope = _services.CreateAsyncScope();
        var searcher = scope.ServiceProvider.GetRequiredService<IHybridSearcher>();
        return await searcher.SearchAsync(query, _indexName, notebookId, maxResults: 10, vectorWeight, textWeight);
    }

    private static double TextScoreOf(IEnumerable<HybridSearchResult> results, Guid chunkId)
        => results.Should().ContainSingle(r => r.ChunkId == chunkId).Which.TextScore;

    private async Task<Guid> AddChunkAsync(string content, float[] embedding, Guid? notebookId = null)
    {
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            IndexName = _indexName,
            ProjectId = Guid.Empty,
            NotebookId = notebookId,
            ChunkIndex = _nextChunkIndex++,
            Content = content,
            Embedding = embedding,
        };
        await using (var db = CreateContext())
        {
            db.DocumentChunks.Add(chunk);
            await db.SaveChangesAsync();
        }

        await WaitUntilSearchableAsync();
        return chunk.Id;
    }

    /// <summary>SQL Server only: waits until the full-text catalog is idle and has indexed every row of the table.</summary>
    private async Task WaitUntilSearchableAsync()
    {
        if (OnPostgres)
        {
            return;
        }

        const string Status = """
            SELECT ISNULL(CAST(FULLTEXTCATALOGPROPERTY('DocumentChunks_Catalog', 'PopulateStatus') AS int), -1),
                   ISNULL(CAST(OBJECTPROPERTYEX(OBJECT_ID('DocumentChunks'), 'TableFulltextPendingChanges') AS int), -1),
                   ISNULL(CAST(OBJECTPROPERTYEX(OBJECT_ID('DocumentChunks'), 'TableFulltextItemCount') AS int), -1),
                   (SELECT COUNT(*) FROM DocumentChunks)
            """;

        await using var db = CreateContext();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Status;
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            var (status, pending, indexed, rows) = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
            if (status == 0 && pending == 0 && indexed == rows)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Full-text population did not finish (status {status}, pending {pending}, indexed {indexed}/{rows}).");
            }

            await Task.Delay(250);
        }
    }

    /// <summary>A unit vector with cosine similarity <paramref name="similarity"/> to <see cref="QueryVector"/>.</summary>
    private static float[] UnitVector(double similarity, int axis)
    {
        var vector = new float[Dimensions];
        vector[0] = (float)similarity;
        if (axis > 0 && similarity < 1.0)
        {
            vector[axis] = (float)Math.Sqrt(1.0 - similarity * similarity);
        }
        return vector;
    }

    private static async Task<TestWebApplicationFactory> StartFactoryAsync()
    {
        var factory = new TestWebApplicationFactory();
        await factory.InitializeAsync();
        return factory;
    }

    private sealed class FixedEmbeddingService(float[] vector) : IEmbeddingService
    {
        public Task<float[][]> GetEmbeddingsAsync(
            IEnumerable<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken = default)
            => Task.FromResult(texts.Select(_ => vector).ToArray());
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new ListLogger(Entries, categoryName);

        public void Dispose() { }
    }

    private sealed class ListLogger(List<LogEntry> sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (sink)
            {
                sink.Add(new LogEntry(category, logLevel, formatter(state, exception) + (exception is null ? "" : $" {exception}")));
            }
        }
    }
}
