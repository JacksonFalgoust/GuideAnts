using System.Globalization;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DatabaseSpeed;

/// <summary>Facts about the two seeded databases that the "Limits of this comparison" block and the header quote.</summary>
/// <param name="Migrations">Migrations applied to each seeded database (what a fresh install, and so the cold start, runs).</param>
/// <param name="UsedMegabytes">Space the seeded data takes.</param>
/// <param name="Storage">Where each server keeps its data files inside Docker (volume or writable layer).</param>
/// <param name="MaxAutoPrepare">Npgsql's <c>Max Auto Prepare</c> for the Postgres connection string (0 = no automatic preparation).</param>
public sealed record RunFacts(
    IReadOnlyDictionary<DatabaseProvider, int> Migrations,
    IReadOnlyDictionary<DatabaseProvider, long> UsedMegabytes,
    IReadOnlyDictionary<DatabaseProvider, string> Storage,
    int MaxAutoPrepare,
    string PostgresSharedBuffers,
    string PostgresWorkMem)
{
    public static async Task<RunFacts> ReadAsync(IReadOnlyList<ProviderTarget> targets, IReadOnlyList<DatabaseServer> servers, CancellationToken ct)
    {
        var migrations = new Dictionary<DatabaseProvider, int>();
        var used = new Dictionary<DatabaseProvider, long>();
        var storage = new Dictionary<DatabaseProvider, string>();
        string sharedBuffers = "unknown", workMem = "unknown";
        var maxAutoPrepare = 0;
        foreach (var target in targets)
        {
            await using var db = await target.CreateContextAsync(ct);
            migrations[target.Provider] = (await db.Database.GetAppliedMigrationsAsync(ct)).Count();
            var sql = target.Provider == DatabaseProvider.SqlServer
                ? "SELECT CAST(SUM(CAST(a.used_pages AS bigint)) * 8 / 1024 AS bigint) FROM sys.allocation_units AS a"
                : "SELECT pg_database_size(current_database()) / 1048576";
            used[target.Provider] = Convert.ToInt64(await ScalarAsync(db, sql, ct), CultureInfo.InvariantCulture);
            if (target.Provider == DatabaseProvider.Postgres)
            {
                sharedBuffers = Convert.ToString(await ScalarAsync(db, "SELECT current_setting('shared_buffers')", ct), CultureInfo.InvariantCulture) ?? "unknown";
                workMem = Convert.ToString(await ScalarAsync(db, "SELECT current_setting('work_mem')", ct), CultureInfo.InvariantCulture) ?? "unknown";
                maxAutoPrepare = new NpgsqlConnectionStringBuilder(target.ConnectionString).MaxAutoPrepare;
            }
        }

        foreach (var server in servers)
        {
            var mounts = (await Provenance.RunAsync("docker",
                ["inspect", "--format", "{{range .Mounts}}{{.Type}} {{.Destination}};{{end}}", server.ContainerId], null, ct)).Trim().TrimEnd(';');
            storage[server.Provider] = mounts.Length == 0
                ? "the container's writable layer (no volume mounted)"
                : string.Join(", ", mounts.Split(';').Select(m => m.Trim().Split(' ', 2)).Select(m => $"a Docker {m[0]} at `{m.ElementAtOrDefault(1)}`")) +
                  " (an anonymous volume the image declares; not on the writable layer)";
        }

        return new RunFacts(migrations, used, storage, maxAutoPrepare, sharedBuffers, workMem);
    }

    private static async Task<object?> ScalarAsync(GuideAntsApi.DataModel.ApplicationDbContext db, string sql, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(ct);
    }
}
