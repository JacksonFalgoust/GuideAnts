using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;

namespace GuideAntsApi.IntegrationTests.Infrastructure;

public class TestContainerManager : IAsyncDisposable
{
    private const string DatabaseName = "GuideAntsApiTest";
    private const string DefaultSqlImage = "mssql2025-express-fts:latest";
    private const string SqlImageEnvVar = "GA_INTEGRATION_TEST_MSSQL_IMAGE";
    private const string DefaultPostgresImage = "pgvector/pgvector:0.8.7-pg18";
    private const string PostgresImageEnvVar = "GA_INTEGRATION_TEST_POSTGRES_IMAGE";
    private static readonly Lazy<TestContainerManager> _instance = new(() => new TestContainerManager());
    private IContainer? _container;
    private string? _connectionString;
    private bool _initialized;
    private readonly object _lock = new object();

    public static TestContainerManager Instance => _instance.Value;

    /// <summary>The provider of the database this manager starts, from <see cref="DatabaseTestProvider.Current"/>.</summary>
    public DatabaseProvider Provider { get; } = DatabaseTestProvider.Current;

    private TestContainerManager()
    {
        // Don't initialize in constructor - do it in EnsureInitializedAsync
    }

    public async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;

            _container = Provider switch
            {
                DatabaseProvider.SqlServer => new MsSqlBuilder()
                    .WithImage(Environment.GetEnvironmentVariable(SqlImageEnvVar) ?? DefaultSqlImage)
                    .WithPassword("Your_password123")
                    .Build(),
                DatabaseProvider.Postgres => new PostgreSqlBuilder()
                    .WithImage(Environment.GetEnvironmentVariable(PostgresImageEnvVar) ?? DefaultPostgresImage)
                    .WithDatabase(DatabaseName)
                    .Build(),
                _ => throw new InvalidOperationException($"Unsupported database provider '{Provider}'."),
            };
        }

        await _container.StartAsync();
        _connectionString = _container switch
        {
            MsSqlContainer sqlServer => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(sqlServer.GetConnectionString())
            {
                InitialCatalog = DatabaseName
            }.ConnectionString,
            PostgreSqlContainer postgres => new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
            {
                Database = DatabaseName
            }.ConnectionString,
            _ => throw new InvalidOperationException($"Unexpected container type '{_container.GetType().Name}'."),
        };

        // Initialize database
        await InitializeDatabaseAsync();
        _initialized = true;
    }

    private async Task InitializeDatabaseAsync()
    {
        var services = new ServiceCollection();
        // Same options the host's database initializers migrate with.
        services.AddDbContext<ApplicationDbContext>(options =>
            ApplicationDbContextOptions.Configure(
                options,
                Provider,
                _connectionString!,
                new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true)));

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.Database.MigrateAsync();
    }

    public Task<string> GetConnectionStringAsync()
    {
        if (!_initialized || _connectionString == null)
            throw new InvalidOperationException("Container not initialized. Call EnsureInitializedAsync first.");

        return Task.FromResult(_connectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
            _container = null;
        }
        _connectionString = null;
        _initialized = false;
        GC.SuppressFinalize(this);
    }
}
