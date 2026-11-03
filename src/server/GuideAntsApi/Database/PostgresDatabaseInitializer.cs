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
}
