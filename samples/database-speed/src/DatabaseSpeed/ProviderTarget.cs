using GuideAntsApi.BackgroundJobs;
using GuideAntsApi.BackgroundJobs.Services.Embeddings;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.Extensions;
using GuideAntsApi.Services;
using GuideAntsApi.Services.Conversations.Persistence;
using GuideAntsApi.Services.Conversations.Queries;
using GuideAntsApi.Services.Core;
using GuideAntsApi.Services.Guides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DatabaseSpeed;

/// <summary>The saved query vector the searcher's embeddings call returns in this scope.</summary>
public sealed class QueryVector
{
    public float[] Value { get; set; } = [];
}

/// <summary>Stands in for the embeddings provider: returns the saved vector, so no API is called.</summary>
internal sealed class SavedVectorEmbeddingService(QueryVector vector) : IEmbeddingService
{
    public Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken = default) =>
        Task.FromResult(texts.Select(_ => vector.Value).ToArray());
}

/// <summary>
/// One provider's database and the GuideAnts services over it, registered the way the host registers them
/// (<c>StartupConfiguration.ConfigureDatabase</c> and <c>RegisterServices</c>): the scoped <see cref="ApplicationDbContext"/>
/// with the query-warning interceptor (60 s timeout), the pooled factory (300 s), <c>AddBackgroundJobs</c> (which picks
/// <c>HybridSearcher</c> or <c>PostgresHybridSearcher</c> from <see cref="DatabaseProviderInfo"/>), and the scoped
/// project, catalog, conversation and usage services. Only the embeddings provider is replaced.
/// </summary>
public sealed class ProviderTarget : IAsyncDisposable
{
    private ProviderTarget(DatabaseServer server, string database, string storagePath)
    {
        Server = server;
        Database = database;
        ConnectionString = server.ConnectionStringFor(database);
        Log = new LogSink();
        Services = BuildServices(server.Provider, ConnectionString, Log, storagePath);
    }

    public DatabaseServer Server { get; }

    public DatabaseProvider Provider => Server.Provider;

    public string Name => Report.ProviderName(Provider);

    public string Database { get; }

    public string ConnectionString { get; }

    public LogSink Log { get; }

    public ServiceProvider Services { get; }

    public static ProviderTarget Create(DatabaseServer server, string database, string storagePath) => new(server, database, storagePath);

    public static ServiceProvider BuildServices(DatabaseProvider provider, string connectionString, LogSink log, string storagePath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:Path"] = storagePath })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders().AddProvider(log).SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(configuration);

        // StartupConfiguration.ConfigureDatabase (Release build: no DEBUG-only options).
        services.AddSingleton(new DatabaseProviderInfo(provider));
        services.AddSingleton<EfQueryWarningInterceptor>();
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            ApplicationDbContextOptions.Configure(options, provider, connectionString, new DbProviderTuning(60, true, true));
            options.AddInterceptors(sp.GetRequiredService<EfQueryWarningInterceptor>());
            options.ConfigureWarnings(warnings => warnings.Log(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning,
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.RowLimitingOperationWithoutOrderByWarning));
        }, contextLifetime: ServiceLifetime.Scoped, optionsLifetime: ServiceLifetime.Singleton);
        services.AddPooledDbContextFactory<ApplicationDbContext>(options =>
        {
            ApplicationDbContextOptions.Configure(options, provider, connectionString, new DbProviderTuning(300, true, true));
            options.ConfigureWarnings(warnings => warnings.Log(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning,
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.RowLimitingOperationWithoutOrderByWarning));
        });

        // StartupConfiguration.RegisterServices, the services the operations call.
        services.AddSingleton<IStoragePathResolver, StoragePathResolver>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IConversationQueryService, ConversationQueryService>();
        services.AddScoped<IConversationPersistence, ConversationPersistence>();
        services.AddScoped<IUsageQueryService, UsageQueryService>();
        services.AddBackgroundJobs(configuration);

        // The one substitution: the saved query vector instead of an embeddings API (registered last, so it wins).
        services.AddScoped<QueryVector>();
        services.AddScoped<IEmbeddingService, SavedVectorEmbeddingService>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public Task<ApplicationDbContext> CreateContextAsync(CancellationToken ct) =>
        Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync(ct);

    public ValueTask DisposeAsync() => Services.DisposeAsync();
}
