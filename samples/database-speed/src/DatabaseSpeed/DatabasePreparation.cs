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

    /// <summary>The catalog the LiteBaselineV1 migration creates (<c>CREATE FULLTEXT CATALOG DocumentChunks_Catalog</c>).</summary>
    public const string FullTextCatalog = "DocumentChunks_Catalog";

    private static readonly TimeSpan FullTextTimeout = TimeSpan.FromMinutes(20);

    /// <returns>Lines for the RESULTS header about how the database was settled.</returns>
    public static async Task<IReadOnlyList<string>> PrepareAsync(ProviderTarget target, SeedData seed, Action<string> log, CancellationToken ct)
    {
        var facts = new List<string>();
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

            // The incremental population leaves the index in many fragments, and every CONTAINSTABLE reads them all. A
            // running system's master merge eventually combines them; do it now so the measured searches see a settled
            // index, the counterpart of Postgres' VACUUM ANALYZE flushing the GIN pending list.
            var before = await FullTextFragmentsAsync(target, ct);
            await using (var db = await target.CreateContextAsync(ct))
            {
                await db.Database.ExecuteSqlRawAsync($"ALTER FULLTEXT CATALOG {FullTextCatalog} REORGANIZE", ct);
            }

            await WaitForFullTextAsync(target, ct, waitForMerge: true);
            var after = await FullTextFragmentsAsync(target, ct);
            facts.Add($"- SQL Server full-text index: {before} fragment(s) after the population, {after} after `ALTER FULLTEXT CATALOG {FullTextCatalog} REORGANIZE` " +
                      "and its master merge (the counterpart of Postgres' `VACUUM ANALYZE` flushing the GIN pending list)");
            log($"{target.Name}: full-text catalog reorganized, {before} -> {after} fragment(s) ({total.Elapsed.TotalSeconds:0}s)");
        }

        // A running system has current statistics; a freshly bulk-loaded one doesn't until autovacuum or auto-stats
        // catch up, which could otherwise happen in the middle of the measured runs.
        await using (var db = await target.CreateContextAsync(ct))
        {
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));
            await db.Database.ExecuteSqlRawAsync(target.Provider == DatabaseProvider.SqlServer ? "EXEC sp_updatestats" : "VACUUM ANALYZE", ct);
        }

        log($"{target.Name}: statistics refreshed ({total.Elapsed.TotalSeconds:0}s)");
        return facts;
    }

    /// <summary>Writes the dirty pages out, so the first timed operation doesn't pay for the seed's checkpoint.</summary>
    public static async Task CheckpointAsync(ProviderTarget target, CancellationToken ct)
    {
        await using var db = await target.CreateContextAsync(ct);
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));
        await db.Database.ExecuteSqlRawAsync("CHECKPOINT", ct);
    }

    private static async Task<int> FullTextFragmentsAsync(ProviderTarget target, CancellationToken ct)
    {
        await using var db = await target.CreateContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.fulltext_index_fragments WHERE table_id = OBJECT_ID('DocumentChunks') AND status IN (4, 6)";
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// SQL Server fills its full-text index in the background; searches before it finishes match too little. With
    /// <paramref name="waitForMerge"/>, also waits for a master merge (REORGANIZE) to finish.
    /// </summary>
    private static async Task WaitForFullTextAsync(ProviderTarget target, CancellationToken ct, bool waitForMerge = false)
    {
        const string status = """
            SELECT ISNULL(CAST(FULLTEXTCATALOGPROPERTY('DocumentChunks_Catalog', 'PopulateStatus') AS int), -1),
                   ISNULL(CAST(OBJECTPROPERTYEX(OBJECT_ID('DocumentChunks'), 'TableFulltextPendingChanges') AS int), -1),
                   ISNULL(CAST(OBJECTPROPERTYEX(OBJECT_ID('DocumentChunks'), 'TableFulltextItemCount') AS int), -1),
                   (SELECT COUNT(*) FROM DocumentChunks),
                   ISNULL(CAST(FULLTEXTCATALOGPROPERTY('DocumentChunks_Catalog', 'MergeStatus') AS int), -1),
                   (SELECT COUNT(*) FROM sys.fulltext_index_fragments WHERE table_id = OBJECT_ID('DocumentChunks') AND status = 6)
            """;
        if (waitForMerge)
        {
            await Task.Delay(1000, ct); // the master merge starts asynchronously
        }

        var deadline = DateTime.UtcNow + FullTextTimeout;
        while (true)
        {
            await using var db = await target.CreateContextAsync(ct);
            await db.Database.OpenConnectionAsync(ct);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = status;
            int populate, pending, indexed, rows, merging, mergingFragments;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                await reader.ReadAsync(ct);
                (populate, pending, indexed, rows) = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
                (merging, mergingFragments) = (reader.GetInt32(4), reader.GetInt32(5));
            }

            if (populate == -1 || indexed == -1)
            {
                throw new InvalidOperationException("SQL Server has no DocumentChunks full-text index; use the mssql2025-express-fts image.");
            }

            if (populate == 0 && pending == 0 && indexed == rows && (!waitForMerge || (merging == 0 && mergingFragments == 0)))
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"Full-text population didn't finish in {FullTextTimeout} (status {populate}, pending {pending}, indexed {indexed}/{rows}, merge {merging}, merging fragments {mergingFragments}).");
            }

            await Task.Delay(1000, ct);
        }
    }
}
