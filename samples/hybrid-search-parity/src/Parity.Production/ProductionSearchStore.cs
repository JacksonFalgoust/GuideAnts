using GuideAntsApi.BackgroundJobs;
using GuideAntsApi.BackgroundJobs.Services.Embeddings;
using GuideAntsApi.BackgroundJobs.Services.Search;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.DataModel.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Parity.Core;
using Parity.Core.Contracts;
using Parity.Core.Corpus;
using Parity.Core.Model;
using Parity.Postgres;
using Parity.SqlServer;

namespace Parity.Production;

/// <summary>
/// GuideAnts' own hybrid search on a database built by its own migrations: the <see cref="IHybridSearcher"/> that
/// <see cref="ServiceCollectionExtensions.AddBackgroundJobs"/> registers for the provider (<c>HybridSearcher</c> on
/// SQL Server, <c>PostgresHybridSearcher</c> on Postgres), over the pooled <see cref="ApplicationDbContext"/> factory
/// configured the way the host configures it. Nothing about the query is copied.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The query embedding is the saved one (<see cref="HybridQuery.Vector"/>, from <c>data/</c>): the searcher's
/// <see cref="IEmbeddingService"/> is a <see cref="PrecomputedEmbeddingService"/>, so no embeddings API is called.</item>
/// <item>Production catches every error and returns an empty list. The store records what the searcher logged and
/// reports it as a failed result, so a failure never looks like "no results".</item>
/// <item>Production computes its own full-text candidate limit, <c>maxResults * 2</c>. A query with another
/// <see cref="HybridQuery.CandidateMultiplier"/> is refused, so the diagnostics never explain a ranking with a
/// different query shape. There is no quirk-fixed variant (<see cref="HybridQuery.FilterBeforeTopN"/>).</item>
/// </list>
/// </remarks>
public sealed class ProductionSearchStore : ISearchStore, IAsyncDisposable
{
    /// <summary>
    /// Production's full-text candidate multiplier: <c>candidateMultiplier = 2</c> in <c>HybridSearcher</c> and
    /// <c>CandidateMultiplier = 2</c> in <c>PostgresHybridSearcher</c> (a unit test reads both sources).
    /// </summary>
    public const int ProductionCandidateMultiplier = 2;

    /// <summary>The host's pooled factory settings (<c>StartupConfiguration.ConfigureDatabase</c>), which the searchers use.</summary>
    private static readonly DbProviderTuning HostPooledFactoryTuning = new(300, UseVectorSearch: true, ExplicitSingleQuery: true);

    private readonly DatabaseProvider _provider;
    private readonly string _connectionString;
    private readonly string _profile;
    private readonly TimeSpan _populationTimeout;
    private readonly string _host;
    private readonly string? _database;
    private readonly ErrorCapture _errors = new();
    private readonly ServiceProvider _services;

    public ProductionSearchStore(DatabaseProvider provider, string connectionString, string? profile = null, TimeSpan? populationTimeout = null)
    {
        _provider = provider;
        _connectionString = connectionString;
        _profile = profile ?? $"production-{provider}";
        _populationTimeout = populationTimeout ?? TimeSpan.FromMinutes(10);
        (_host, _database) = provider switch
        {
            DatabaseProvider.SqlServer => (SqlServerErrors.Host(connectionString), new SqlConnectionStringBuilder(connectionString).InitialCatalog),
            DatabaseProvider.Postgres => (PostgresErrors.Host(connectionString), new NpgsqlConnectionStringBuilder(connectionString).Database),
            _ => throw new ParityConfigurationException($"Profile '{_profile}' has unsupported provider '{provider}'."),
        };
        _services = BuildServices();
    }

    public async Task PingServerAsync(CancellationToken ct)
    {
        if (_provider == DatabaseProvider.SqlServer)
        {
            await SqlServerStore().PingServerAsync(ct);
            return;
        }

        var admin = new NpgsqlConnectionStringBuilder(_connectionString) { Database = "postgres", Timeout = 5, Pooling = false };
        try
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(ct);
        }
        catch (Exception ex) when (PostgresErrors.Translate(ex, _profile, _host) is { } fatal)
        {
            throw fatal;
        }
    }

    /// <summary>Drops the database and recreates it with production's migrations (<c>Database.Migrate()</c>).</summary>
    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_database))
        {
            throw new ParityConfigurationException($"Profile '{_profile}' connection string has no database.");
        }

        SeedSafety.RequireSafeToDrop(_profile, _provider == DatabaseProvider.SqlServer ? _host : new NpgsqlConnectionStringBuilder(_connectionString).Host, _database);
        await using (var db = await CreateContextAsync(ct))
        {
            await db.Database.EnsureDeletedAsync(ct);
        }

        ClearPools();
        await using (var db = await CreateContextAsync(ct))
        {
            await db.Database.MigrateAsync(ct);
        }

        if (_provider == DatabaseProvider.Postgres)
        {
            // The data source may have loaded its types before the migrations created the vector extension.
            await using var db = await CreateContextAsync(ct);
            await db.Database.OpenConnectionAsync(ct);
            await ((NpgsqlConnection)db.Database.GetDbConnection()).ReloadTypesAsync();
            return;
        }

        // The baseline skips the full-text catalog and index when full-text search isn't installed; production would
        // then match no text at all, which would make every text comparison pass falsely.
        await using (var db = await CreateContextAsync(ct))
        {
            var indexes = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('DocumentChunks')")
                .SingleAsync(ct);
            if (indexes != 1)
            {
                throw new ParityConfigurationException(
                    $"The migrations created no full-text index on profile '{_profile}'. Use the ghcr.io/elumenotion/mssql2025-express-fts image.");
            }
        }
    }

    /// <summary>
    /// The parent rows the <c>DocumentChunks</c> foreign keys need (one <see cref="Project"/> per project ID and one
    /// <see cref="ContentFile"/> per content file ID, with the sample's deterministic IDs), then the chunks, all through EF.
    /// </summary>
    public async Task LoadAsync(IReadOnlyList<ChunkRecord> chunks, DateTime createdUtc, CancellationToken ct)
    {
        var created = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        var labels = ParityScopes.ProjectLabels.ToDictionary(ParityScopes.ProjectId);
        await using (var db = await CreateContextAsync(ct))
        {
            foreach (var projectId in chunks.Select(c => c.ProjectId).Distinct().Order())
            {
                var label = labels.TryGetValue(projectId, out var known) ? known : projectId.ToString("N")[..8];
                db.Projects.Add(new Project
                {
                    Id = projectId,
                    Title = $"Parity {label}",
                    Slug = $"parity-{label.ToLowerInvariant()}",
                    Description = "Hybrid search parity corpus (samples/hybrid-search-parity).",
                    Created = created,
                });
            }

            foreach (var file in chunks.GroupBy(c => c.ContentFileId).OrderBy(g => g.Key))
            {
                var first = file.First();
                var contentFile = new ContentFile
                {
                    Id = file.Key,
                    ProjectId = first.ProjectId,
                    FileName = Path.GetFileName(first.SourcePath),
                    Path = first.SourcePath,
                    RelativePath = first.SourcePath,
                    FileSize = file.Sum(c => (long)System.Text.Encoding.UTF8.GetByteCount(c.Content)),
                    ContentType = first.Kind == ChunkKind.Doc ? "text/markdown" : "text/plain",
                    Created = created,
                };
                contentFile.OnSaveOrUpdate();
                db.ContentFiles.Add(contentFile);
            }

            await db.SaveChangesAsync(ct);
        }

        foreach (var batch in chunks.Chunk(200))
        {
            await using var db = await CreateContextAsync(ct);
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.DocumentChunks.AddRange(batch.Select(c => new DocumentChunk
            {
                Id = c.Id,
                IndexName = c.IndexName,
                ContentFileId = c.ContentFileId,
                NotebookFileId = null,
                AssistantFileId = null,
                ProjectId = c.ProjectId,
                NotebookId = c.NotebookId,
                DocumentId = c.DocumentId,
                ChunkIndex = c.ChunkIndex,
                Content = c.Content,
                Embedding = c.Embedding,
                CreatedUtc = created,
            }));
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>SQL Server fills its full-text index in the background; Postgres computes <c>"ContentTsv"</c> on write.</summary>
    public Task WaitUntilSearchableAsync(CancellationToken ct) =>
        _provider == DatabaseProvider.SqlServer ? SqlServerStore().WaitUntilSearchableAsync(ct) : Task.CompletedTask;

    public async Task<EngineResult<IReadOnlyList<SearchHit>>> HybridSearchAsync(HybridQuery query, CancellationToken ct)
    {
        if (query.FilterBeforeTopN)
        {
            throw new UnsupportedQueryException(
                $"Profile '{_profile}' runs the production searcher, which has no quirk-fixed variant (the full-text candidates are always table-wide).");
        }

        if (query.CandidateMultiplier != ProductionCandidateMultiplier)
        {
            throw new ParityConfigurationException(
                $"Hybrid:CandidateMultiplier is {query.CandidateMultiplier}, but production's searchers always use {ProductionCandidateMultiplier} " +
                $"(maxResults * {ProductionCandidateMultiplier}); profile '{_profile}' can't run another query shape.");
        }

        await using var scope = _services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<QueryVector>().Value = query.Vector;
        var searcher = scope.ServiceProvider.GetRequiredService<IHybridSearcher>();

        IReadOnlyList<HybridSearchResult> results;
        Exception? error;
        string? message;
        using (var recording = _errors.Begin())
        {
            results = await searcher.SearchAsync(query.Text, query.Scope.IndexName, query.Scope.NotebookId, query.MaxResults, query.VectorWeight, query.TextWeight, ct);
            (error, message) = (recording.Error, recording.Message);
        }

        ct.ThrowIfCancellationRequested();
        if (message is not null)
        {
            if (Fatal(error) is { } fatal)
            {
                throw fatal;
            }

            return EngineResult<IReadOnlyList<SearchHit>>.Fail(ToEngineError(error, message));
        }

        return EngineResult<IReadOnlyList<SearchHit>>.Ok(results
            .Select(r => new SearchHit(r.ChunkId, r.Content, r.ContentFileId, r.NotebookFileId, r.ChunkIndex, r.IndexName, r.VectorScore, r.TextScore, r.Score))
            .ToList());
    }

    /// <summary>The concrete <see cref="IHybridSearcher"/> production's registration gives this provider.</summary>
    public Type ResolveSearcherType()
    {
        using var scope = _services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IHybridSearcher>().GetType();
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders().AddProvider(_errors).SetMinimumLevel(LogLevel.Error));
        services.AddSingleton(new DatabaseProviderInfo(_provider));
        services.AddPooledDbContextFactory<ApplicationDbContext>(options =>
            ApplicationDbContextOptions.Configure(options, _provider, _connectionString, HostPooledFactoryTuning));
        services.AddBackgroundJobs(new ConfigurationBuilder().Build());
        services.AddScoped<QueryVector>();
        services.AddScoped<IEmbeddingService>(sp => new PrecomputedEmbeddingService(sp.GetRequiredService<QueryVector>().Value));
        return services.BuildServiceProvider();
    }

    private Task<ApplicationDbContext> CreateContextAsync(CancellationToken ct) =>
        _services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync(ct);

    private SqlServerSearchStore SqlServerStore() => new(_profile, _connectionString, _populationTimeout);

    private void ClearPools()
    {
        if (_provider == DatabaseProvider.SqlServer)
        {
            SqlConnection.ClearAllPools();
        }
        else
        {
            NpgsqlConnection.ClearAllPools();
        }
    }

    /// <summary>A connection-level failure stops the run, as in the sample's other stores; anything else is a result.</summary>
    private ParityException? Fatal(Exception? error) => FirstOfType<SqlException>(error) is { } sql
        ? SqlServerErrors.Translate(sql, _profile, _host, connecting: false)
        : FirstOfType<NpgsqlException>(error) is { } npgsql
            ? PostgresErrors.Translate(npgsql, _profile, _host, connecting: false)
            : null;

    private static EngineError ToEngineError(Exception? error, string message) => error switch
    {
        _ when FirstOfType<SqlException>(error) is { } sql => new EngineError($"SQL{sql.Number}", sql.Message),
        _ when FirstOfType<PostgresException>(error) is { } pg => PostgresErrors.ToEngineError(pg),
        null => new EngineError("LOGGED", message),
        _ => new EngineError(error.GetType().Name, error.Message),
    };

    private static T? FirstOfType<T>(Exception? error)
        where T : Exception
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>The saved vector of the query running in this scope.</summary>
    private sealed class QueryVector
    {
        public float[] Value { get; set; } = [];
    }
}
