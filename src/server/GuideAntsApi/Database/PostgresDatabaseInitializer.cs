using System.Globalization;
using System.Text;
using GuideAntsApi.DataModel;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GuideAntsApi.Database;

public static class PostgresDatabaseInitializer
{
    /// <summary>Postgres identifiers, database names included, are at most NAMEDATALEN - 1 bytes.</summary>
    private const int MaxDatabaseNameLength = 63;

    private const string MaintenanceDatabase = "postgres";

    /// <summary>
    /// <c>server_version_num</c> of Postgres 18.0. Postgres 17 migrates the baseline, but rejects <c>LIKE</c> and
    /// <c>strpos</c> under the nondeterministic <c>guideants_ci</c> collation, so every text search would fail at run
    /// time (parity report #26).
    /// </summary>
    private const int MinimumServerVersionNum = 180000;

    /// <summary>
    /// Ensures the Postgres database exists, then applies the Postgres EF Core migrations.
    /// The Postgres counterpart of <see cref="SqlServerDatabaseInitializer.EnsureCatalogAndMigrate"/>; it must also run
    /// before <see cref="Microsoft.AspNetCore.Builder.WebApplicationBuilder.Build"/> so DB-backed configuration can load.
    /// </summary>
    public static void EnsureDatabaseAndMigrate(string connectionString, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var database = builder.Database;
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException(
                "Connection string must specify Database for automatic database setup.");
        }

        // The limit is in bytes; Postgres would silently truncate a longer name.
        if (Encoding.UTF8.GetByteCount(database) > MaxDatabaseNameLength)
        {
            throw new InvalidOperationException($"Database name exceeds maximum length ({MaxDatabaseNameLength}).");
        }

        builder.Database = MaintenanceDatabase;
        bool createdDatabase;
        using (var connection = new NpgsqlConnection(builder.ConnectionString))
        {
            connection.Open();

            using (var version = new NpgsqlCommand("SHOW server_version_num", connection))
            {
                var versionNum = Convert.ToString(version.ExecuteScalar(), CultureInfo.InvariantCulture);
                EnsureSupportedServerVersion(versionNum, connection.ServerVersion);
            }

            using (var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection))
            {
                exists.Parameters.AddWithValue("name", database);
                createdDatabase = exists.ExecuteScalar() is null;
            }

            if (createdDatabase)
            {
                // CREATE DATABASE takes no parameters and cannot run in a transaction; the name is quoted instead.
                using var create = new NpgsqlCommand($"CREATE DATABASE \"{database.Replace("\"", "\"\"")}\"", connection);
                create.ExecuteNonQuery();
            }
        }

        if (createdDatabase)
        {
            logger.LogInformation("Created Postgres database '{Catalog}'.", database);
        }
        else
        {
            logger.LogInformation("Postgres database '{Catalog}' already exists.", database);
        }

        logger.LogInformation("Postgres database '{Catalog}' is ready; applying EF Core migrations.", database);

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        ApplicationDbContextOptions.Configure(optionsBuilder, DatabaseProvider.Postgres, connectionString, new DbProviderTuning(60, true, true));

        using var context = new ApplicationDbContext(optionsBuilder.Options);
        context.Database.Migrate();
    }

    /// <summary>
    /// Fails fast on a server older than Postgres 18, before anything is created or migrated.
    /// </summary>
    /// <param name="serverVersionNum">The <c>server_version_num</c> setting, e.g. <c>180001</c>.</param>
    /// <param name="serverVersion">The readable version for the message, e.g. <c>18.1</c>.</param>
    internal static void EnsureSupportedServerVersion(string? serverVersionNum, string serverVersion)
    {
        if (!int.TryParse(serverVersionNum, NumberStyles.None, CultureInfo.InvariantCulture, out var versionNum)
            || versionNum < MinimumServerVersionNum)
        {
            throw new InvalidOperationException($"PostgreSQL 18 or later is required (found {serverVersion}).");
        }
    }
}
