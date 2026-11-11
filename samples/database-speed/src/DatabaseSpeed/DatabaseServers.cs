using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using GuideAntsApi.DataModel.Database;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace DatabaseSpeed;

/// <summary>
/// One database server in a Testcontainers container, started with <see cref="RunPlan.ContainerCpus"/> CPUs and
/// <see cref="RunPlan.ContainerMemoryBytes"/> of memory, like the other one. Disposing it removes the container
/// (Testcontainers' reaper also removes it if the process dies).
/// </summary>
public sealed class DatabaseServer : IAsyncDisposable
{
    public const string DefaultSqlServerImage = "ghcr.io/elumenotion/mssql2025-express-fts:main";
    public const string DefaultPostgresImage = "pgvector/pgvector:0.8.7-pg18";
    public const string SqlServerImageVariable = "GA_SPEED_MSSQL_IMAGE";
    public const string PostgresImageVariable = "GA_SPEED_POSTGRES_IMAGE";

    /// <summary>
    /// The edition docker/docker-compose.ghcr-cpu.yml (and the other stack files) run: Developer, which has no Express
    /// caps (1 socket / 4 cores, 1410 MB buffer pool). The image's own default is Express.
    /// </summary>
    public const string SqlServerEdition = "Developer";

    private const string SqlServerPassword = "Speed_harness_Passw0rd!";

    private readonly IDatabaseContainer _container;

    private DatabaseServer(DatabaseProvider provider, string image, IDatabaseContainer container)
    {
        Provider = provider;
        Image = image;
        _container = container;
    }

    public DatabaseProvider Provider { get; }

    public string Image { get; }

    public string ContainerId => _container.Id;

    /// <summary>The settings the container was started with, beyond the image's defaults.</summary>
    public IReadOnlyList<string> StartSettings { get; private init; } = [];

    public static async Task<DatabaseServer> StartAsync(DatabaseProvider provider, CancellationToken ct)
    {
        DatabaseServer server = provider switch
        {
            DatabaseProvider.SqlServer => SqlServer(ImageFromEnvironment(SqlServerImageVariable, DefaultSqlServerImage)),
            DatabaseProvider.Postgres => Postgres(ImageFromEnvironment(PostgresImageVariable, DefaultPostgresImage)),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };
        await server._container.StartAsync(ct);
        return server;
    }

    /// <summary>
    /// A connection string for <paramref name="database"/> with the options production uses: the SQL Server options of
    /// the compose stacks' <c>ConnectionStrings__DefaultConnection</c> (MARS on, the same timeouts and retries), and
    /// Npgsql's defaults for Postgres, as docker-compose.postgres.yml does.
    /// </summary>
    public string ConnectionStringFor(string database) => Provider switch
    {
        DatabaseProvider.SqlServer => new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = database,
            PersistSecurityInfo = false,
            MultipleActiveResultSets = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = true,
            ConnectTimeout = 30,
            ConnectRetryCount = 3,
            ConnectRetryInterval = 5,
        }.ConnectionString,
        _ => new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString,
    };

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private static DatabaseServer SqlServer(string image)
    {
        var container = new MsSqlBuilder()
            .WithImage(image)
            .WithPassword(SqlServerPassword)
            .WithEnvironment("MSSQL_PID", SqlServerEdition)
            .WithCreateParameterModifier(Limit)
            .Build();
        return new DatabaseServer(DatabaseProvider.SqlServer, image, container)
        {
            StartSettings = [$"MSSQL_PID={SqlServerEdition} (as the compose stack files; the image defaults to Express)"],
        };
    }

    private static DatabaseServer Postgres(string image)
    {
        // Testcontainers' PostgreSqlBuilder starts the server with fsync=off, full_page_writes=off and
        // synchronous_commit=off, which would make every Postgres write unrealistically cheap. Clear its command so the
        // server runs with the image's defaults, as docker-compose.postgres.yml does (the run checks these settings).
        var container = new PostgreSqlBuilder()
            .WithImage(image)
            .WithCommand(new OverwriteEnumerable<string>(Array.Empty<string>()))
            .WithCreateParameterModifier(Limit)
            .Build();
        return new DatabaseServer(DatabaseProvider.Postgres, image, container)
        {
            StartSettings = ["the image's default postgresql.conf (Testcontainers' fsync=off / synchronous_commit=off / full_page_writes=off removed)"],
        };
    }

    private static void Limit(Docker.DotNet.Models.CreateContainerParameters parameters)
    {
        parameters.HostConfig ??= new Docker.DotNet.Models.HostConfig();
        parameters.HostConfig.NanoCPUs = (long)(RunPlan.ContainerCpus * 1_000_000_000);
        parameters.HostConfig.Memory = RunPlan.ContainerMemoryBytes;
    }

    private static string ImageFromEnvironment(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } image ? image : fallback;
}
