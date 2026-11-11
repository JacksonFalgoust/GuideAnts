using System.Diagnostics;
using GuideAntsApi.Database;
using GuideAntsApi.DataModel.Database;
using GuideAntsApi.Services.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DatabaseSpeed.Operations;

/// <summary>
/// A first start on an empty database: the host's database initializer (create the database, apply every migration)
/// followed by the first request's query, on a database server that is already running.
/// </summary>
public sealed class ColdStartOperation(string storagePath) : IOperation
{
    private readonly Dictionary<DatabaseProvider, int> _runs = [];

    public string Name => "cold start: migrate an empty database + first query";

    public string Description =>
        "On the already-running server, a new database name each run: the initializer `Program` calls before `Build()` " +
        "(`SqlServerDatabaseInitializer.EnsureCatalogAndMigrate`: create the catalog, `RECOVERY SIMPLE`, apply the SQL Server migrations; " +
        "`PostgresDatabaseInitializer.EnsureDatabaseAndMigrate`: version check, create the database, apply the Postgres migrations), then a " +
        "fresh service container like the host's and the Projects page query (`IProjectService.GetProjectsAsync()`). The database is dropped " +
        "afterwards (not timed). Server start-up itself isn't included; both containers were started once, before the run.";

    public bool IsColdStart => true;

    public IReadOnlyList<string> Metrics => [Name];

    public async Task RunAsync(ProviderTarget target, Measurement measurement, CancellationToken ct)
    {
        var run = _runs[target.Provider] = _runs.GetValueOrDefault(target.Provider) + 1;
        var database = $"speed_cold_start_{run:D2}";
        var connectionString = target.Server.ConnectionStringFor(database);
        var logger = target.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseSpeed.ColdStart");

        var stopwatch = Stopwatch.StartNew();
        if (target.Provider == DatabaseProvider.SqlServer)
        {
            SqlServerDatabaseInitializer.EnsureCatalogAndMigrate(connectionString, logger);
        }
        else
        {
            PostgresDatabaseInitializer.EnsureDatabaseAndMigrate(connectionString, logger);
        }

        int projects;
        await using (var services = ProviderTarget.BuildServices(target.Provider, connectionString, target.Log, storagePath))
        await using (var scope = services.CreateAsyncScope())
        {
            projects = (await scope.ServiceProvider.GetRequiredService<IProjectService>().GetProjectsAsync()).Count();
        }

        stopwatch.Stop();
        await DropAsync(target, database, connectionString, ct);
        if (projects != 0)
        {
            throw new InvalidOperationException($"A freshly migrated database listed {projects} projects.");
        }

        measurement.Add(Name, Measurement.Ms(stopwatch));
    }

    private static async Task DropAsync(ProviderTarget target, string database, string connectionString, CancellationToken ct)
    {
        if (target.Provider == DatabaseProvider.SqlServer)
        {
            using (var pooled = new SqlConnection(connectionString))
            {
                SqlConnection.ClearPool(pooled);
            }

            await using var connection = new SqlConnection(target.Server.ConnectionStringFor("master"));
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];";
            await command.ExecuteNonQueryAsync(ct);
        }
        else
        {
            using (var pooled = new NpgsqlConnection(connectionString))
            {
                NpgsqlConnection.ClearPool(pooled);
            }

            await using var connection = new NpgsqlConnection(target.Server.ConnectionStringFor("postgres"));
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE \"{database}\" WITH (FORCE)";
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
