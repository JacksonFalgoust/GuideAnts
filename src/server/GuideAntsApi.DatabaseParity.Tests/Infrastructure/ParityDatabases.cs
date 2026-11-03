using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace GuideAntsApi.DatabaseParity.Tests.Infrastructure;

/// <summary>
/// One SQL Server and one Postgres container, each with the GuideAnts schema migrated by its own provider's
/// migrations. <see cref="ParityAssembly"/> starts them once for the whole test assembly.
/// </summary>
public sealed class ParityDatabases : IAsyncDisposable
{
    public const string DatabaseName = "GuideAntsApiParity";

    // Same image settings as GuideAntsApi.IntegrationTests (TestContainerManager).
    private const string DefaultSqlServerImage = "mssql2025-express-fts:latest";
    private const string SqlServerImageEnvVar = "GA_INTEGRATION_TEST_MSSQL_IMAGE";
    private const string SqlServerPassword = "Your_password123";
    private const string DefaultPostgresImage = "pgvector/pgvector:0.8.7-pg18";
    private const string PostgresImageEnvVar = "GA_INTEGRATION_TEST_POSTGRES_IMAGE";

    private readonly MsSqlContainer _sqlServer;
    private readonly PostgreSqlContainer _postgres;

    private ParityDatabases(MsSqlContainer sqlServer, PostgreSqlContainer postgres)
    {
        _sqlServer = sqlServer;
        _postgres = postgres;
        SqlServerConnectionString = new SqlConnectionStringBuilder(sqlServer.GetConnectionString())
        {
            InitialCatalog = DatabaseName,
        }.ConnectionString;
        PostgresConnectionString = PostgresConnectionStringFor(DatabaseName);
    }

    public string SqlServerConnectionString { get; }

    public string PostgresConnectionString { get; }

    public static async Task<ParityDatabases> StartAsync()
    {
        var sqlServer = new MsSqlBuilder()
            .WithImage(ImageFromEnvironment(SqlServerImageEnvVar, DefaultSqlServerImage))
            .WithPassword(SqlServerPassword)
            .Build();
        var postgres = new PostgreSqlBuilder()
            .WithImage(ImageFromEnvironment(PostgresImageEnvVar, DefaultPostgresImage))
            .Build();

        await Task.WhenAll(sqlServer.StartAsync(), postgres.StartAsync());

        var databases = new ParityDatabases(sqlServer, postgres);
        await Task.WhenAll(
            databases.MigrateAsync(DatabaseProvider.SqlServer),
            databases.MigrateAsync(DatabaseProvider.Postgres));
        return databases;
    }

    public ApplicationDbContext CreateContext(DatabaseProvider provider)
        => CreateContext(provider, ConnectionString(provider));

    /// <summary>A context configured the way the host's database initializers configure their migrate context.</summary>
    public static ApplicationDbContext CreateContext(DatabaseProvider provider, string connectionString)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(
            builder, provider, connectionString, new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true));
        return new ApplicationDbContext(builder.Options);
    }

    public string ConnectionString(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => SqlServerConnectionString,
        DatabaseProvider.Postgres => PostgresConnectionString,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    /// <summary>A connection string for another database on the same Postgres server.</summary>
    public string PostgresConnectionStringFor(string database)
        => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_sqlServer.DisposeAsync().AsTask(), _postgres.DisposeAsync().AsTask());
    }

    private async Task MigrateAsync(DatabaseProvider provider)
    {
        await using var context = CreateContext(provider);
        await context.Database.MigrateAsync();
    }

    private static string ImageFromEnvironment(string variable, string fallback)
        => Environment.GetEnvironmentVariable(variable) is { Length: > 0 } image ? image : fallback;
}
