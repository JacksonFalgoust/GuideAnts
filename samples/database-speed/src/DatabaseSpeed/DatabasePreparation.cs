using System.Diagnostics;
using GuideAntsApi.Database;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DatabaseSpeed;

/// <summary>Builds the seeded database on one server. Nothing here is timed.</summary>
public static class DatabasePreparation
{
    public const string DatabaseName = "guideants_speed";

    private static readonly TimeSpan FullTextTimeout = TimeSpan.FromMinutes(20);

    public static async Task PrepareAsync(ProviderTarget target, SeedData seed, Action<string> log, CancellationToken ct)
    {
        var total = Stopwatch.StartNew();
        var logger = target.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseSpeed.Initializer");

        // The host's own initializer, as Program runs it before Build().
        if (target.Provider == DatabaseProvider.SqlServer)
        {
            SqlServerDatabaseInitializer.EnsureCatalogAndMigrate(target.ConnectionString, logger);
        }
        else
        {
            PostgresDatabaseInitializer.EnsureDatabaseAndMigrate(target.ConnectionString, logger);
            // The connection pool loaded its types before the migrations created the vector extension.
            await using var db = await target.CreateContextAsync(ct);
            await db.Database.OpenConnectionAsync(ct);
            await ((NpgsqlConnection)db.Database.GetDbConnection()).ReloadTypesAsync();
        }

        target.Log.ThrowIfFailed($"Migrating {target.Name}");
        log($"{target.Name}: migrated ({total.Elapsed.TotalSeconds:0}s)");

        await seed.WriteAsync(
            target.Services.GetRequiredService<IDbContextFactory<GuideAntsApi.DataModel.ApplicationDbContext>>(),
            message => log($"{target.Name}: seeded {message} ({total.Elapsed.TotalSeconds:0}s)"),
            ct);
        target.Log.ThrowIfFailed($"Seeding {target.Name}");

        if (target.Provider == DatabaseProvider.SqlServer)
        {
            await WaitForFullTextAsync(target, ct);
            log($"{target.Name}: full-text index populated ({total.Elapsed.TotalSeconds:0}s)");
        }

        // A running system has current statistics; a freshly bulk-loaded one doesn't until autovacuum or auto-stats
        // catch up, which could otherwise happen in the middle of the measured runs.
        await using (var db = await target.CreateContextAsync(ct))
        {
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));
            await db.Database.ExecuteSqlRawAsync(target.Provider == DatabaseProvider.SqlServer ? "EXEC sp_updatestats" : "VACUUM ANALYZE", ct);
        }

        log($"{target.Name}: statistics refreshed ({total.Elapsed.TotalSeconds:0}s)");
    }

    /// <summary>SQL Server fills its full-text index in the background; searches before it finishes match too little.</summary>
    private static async Task WaitForFullTextAsync(ProviderTarget target, CancellationToken ct)
    {
        const string status = """
            SELECT ISNULL(CAST(FULLTEXTCATALOGPROPERTY('DocumentChunks_Catalog', 'PopulateStatus') AS int), -1),
                   ISNULL(CAST(OBJECTPROPERTYEX(OBJECT_ID('DocumentChunks'), 'TableFulltextPendingChanges') AS int), -1),
                   ISNULL(CAST(OBJECTPROPERTYEX(OBJECT_ID('DocumentChunks'), 'TableFulltextItemCount') AS int), -1),
                   (SELECT COUNT(*) FROM DocumentChunks)
            """;
        var deadline = DateTime.UtcNow + FullTextTimeout;
        while (true)
        {
            await using var db = await target.CreateContextAsync(ct);
            await db.Database.OpenConnectionAsync(ct);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = status;
            int populate, pending, indexed, rows;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                await reader.ReadAsync(ct);
                (populate, pending, indexed, rows) = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
            }

            if (populate == -1 || indexed == -1)
            {
                throw new InvalidOperationException("SQL Server has no DocumentChunks full-text index; use the mssql2025-express-fts image.");
            }

            if (populate == 0 && pending == 0 && indexed == rows)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Full-text population didn't finish in {FullTextTimeout} (status {populate}, pending {pending}, indexed {indexed}/{rows}).");
            }

            await Task.Delay(1000, ct);
        }
    }
}
