using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using GuideAntsApi.DataModel.Database;
using Microsoft.EntityFrameworkCore;

namespace DatabaseSpeed;

/// <summary>Code under test as git sees it.</summary>
/// <param name="DirtyPaths">Uncommitted changes (untracked files included) under the paths the numbers depend on.</param>
public sealed record CodeState(string Commit, string ShortCommit, string Branch, string LastServerCommit, IReadOnlyList<string> DirtyPaths);

/// <summary>
/// The facts RESULTS.md records so a reader can tell what was measured where: the commit and whether the tree was
/// clean, the host, the images and their digests, the containers' real limits, and each server's settings. A missing
/// tool gives "unknown", never a failure.
/// </summary>
public static class Provenance
{
    /// <summary>What the numbers depend on: the server code, this harness, and the corpus it loads.</summary>
    public static readonly string[] CodePaths = ["src/server", "samples/database-speed", "samples/hybrid-search-parity/data", "samples/hybrid-search-parity/inputs"];

    public static async Task<CodeState> CodeAsync(string repoRoot, CancellationToken ct)
    {
        var commit = (await RunAsync("git", ["rev-parse", "HEAD"], repoRoot, ct)).Trim();
        var shortCommit = (await RunAsync("git", ["rev-parse", "--short=8", "HEAD"], repoRoot, ct)).Trim();
        var branch = (await RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], repoRoot, ct)).Trim();
        var lastServer = (await RunAsync("git", ["log", "-1", "--format=%h", "--abbrev=8", "--", "src/server"], repoRoot, ct)).Trim();
        var status = await RunAsync("git",
            ["status", "--porcelain", "--untracked-files=all", "--", .. CodePaths, ":!samples/database-speed/RESULTS.md", ":!samples/database-speed/RESULTS-smoke.md"],
            repoRoot, ct);
        var dirty = status.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd()).ToList();
        return new CodeState(Or(commit), Or(shortCommit), Or(branch), Or(lastServer), dirty);
    }

    public static IReadOnlyList<string> CodeLines(CodeState code)
    {
        var lines = new List<string>
        {
            $"- Commit: `{code.Commit}` (branch `{code.Branch}`); last commit touching `src/server`: `{code.LastServerCommit}`",
        };
        if (code.DirtyPaths.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"> **Warning: the working tree had {code.DirtyPaths.Count} uncommitted change(s) under {string.Join(", ", CodePaths.Select(p => $"`{p}`"))}, " +
                      $"so these numbers are not for commit `{code.ShortCommit}` as committed.**");
            lines.AddRange(code.DirtyPaths.Take(20).Select(p => $">     {p}"));
            if (code.DirtyPaths.Count > 20)
            {
                lines.Add($">     ... and {code.DirtyPaths.Count - 20} more");
            }
        }
        else
        {
            lines.Add("- Working tree: clean under " + string.Join(", ", CodePaths.Select(p => $"`{p}`")));
        }

        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            lines.Add($"- CI: GitHub Actions run {Environment.GetEnvironmentVariable("GITHUB_RUN_ID")} " +
                      $"(attempt {Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT")}), runner `{Environment.GetEnvironmentVariable("RUNNER_NAME")}`");
        }

        return lines;
    }

    public static async Task<IReadOnlyList<string>> HardwareLinesAsync(CancellationToken ct)
    {
        var (cpu, memory) = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? ((await RunAsync("sysctl", ["-n", "machdep.cpu.brand_string"], null, ct)).Trim(), ParseLong(await RunAsync("sysctl", ["-n", "hw.memsize"], null, ct)))
            : (LinuxCpuModel(), LinuxMemoryBytes());
        var docker = (await RunAsync("docker", ["info", "--format", "{{.ServerVersion}}|{{.OperatingSystem}}|{{.Architecture}}|{{.NCPU}}|{{.MemTotal}}"], null, ct)).Trim().Split('|');
#if DEBUG
        const string build = "Debug (**not optimized: rebuild with -c Release for real numbers**)";
#else
        const string build = "Release";
#endif
        var lines = new List<string>
        {
            $"- OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}); process {RuntimeInformation.ProcessArchitecture}",
            $"- CPU: {Or(cpu)}; {Environment.ProcessorCount} logical cores",
            $"- RAM: {Gib(memory)}",
            $"- .NET: {RuntimeInformation.FrameworkDescription}; harness build {build}",
        };
        lines.Add(docker.Length == 5
            ? $"- Docker: server {docker[0]} on {docker[1]} ({docker[2]}), {docker[3]} CPUs and {Gib(ParseLong(docker[4]))} available to containers"
            : "- Docker: unknown (docker info failed)");
        return lines;
    }

    /// <param name="dockerArchitecture">The Docker daemon's architecture (<c>x86_64</c>, <c>aarch64</c>); an image of another one runs emulated.</param>
    public static async Task<IReadOnlyList<string>> ImageLinesAsync(DatabaseServer server, string? dockerArchitecture, CancellationToken ct)
    {
        var inspect = (await RunAsync("docker", ["image", "inspect", "--format", "{{json .RepoDigests}}|{{.Architecture}}|{{.Id}}", server.Image], null, ct)).Trim().Split('|');
        var daemon = HostGuard.IsX86_64(dockerArchitecture) ? "amd64" : dockerArchitecture is "aarch64" or "arm64" ? "arm64" : dockerArchitecture ?? "unknown";
        var emulated = inspect.Length == 3 && inspect[1] != daemon ? $" (**{inspect[1]} image on the {dockerArchitecture ?? "unknown"} Docker daemon: emulated**)" : string.Empty;
        return inspect.Length == 3
            ? [$"- {Report.ProviderName(server.Provider)}: `{server.Image}`, digest {inspect[0]}, image id `{inspect[2]}`, {inspect[1]}{emulated}"]
            : [$"- {Report.ProviderName(server.Provider)}: `{server.Image}` (docker image inspect failed)"];
    }

    /// <summary>The limits Docker actually applied, read back from the container.</summary>
    public static async Task<string> LimitsLineAsync(DatabaseServer server, CancellationToken ct)
    {
        var inspect = (await RunAsync("docker", ["inspect", "--format", "{{.HostConfig.NanoCpus}}|{{.HostConfig.Memory}}", server.ContainerId], null, ct)).Trim().Split('|');
        var cpus = inspect.Length == 2 ? ParseLong(inspect[0]) / 1e9 : double.NaN;
        var memory = inspect.Length == 2 ? ParseLong(inspect[1]) : 0;
        if (Math.Abs(cpus - RunPlan.ContainerCpus) > 1e-9 || memory != RunPlan.ContainerMemoryBytes)
        {
            throw new InvalidOperationException(
                $"The {Report.ProviderName(server.Provider)} container runs with {cpus} CPUs and {memory} bytes, not the planned {RunPlan.ContainerCpus} CPUs and {RunPlan.ContainerMemoryBytes} bytes.");
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"- {Report.ProviderName(server.Provider)} container: {cpus:0.##} CPUs, {Gib(memory)} memory (read back with docker inspect); {string.Join("; ", server.StartSettings)}");
    }

    /// <summary>
    /// The server settings that decide write and cache behaviour. On Postgres, fsync, synchronous_commit and
    /// full_page_writes must be on (the image's defaults), or the comparison is unfair to SQL Server and the run stops.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ServerSettingsAsync(ProviderTarget target, CancellationToken ct)
    {
        await using var db = await target.CreateContextAsync(ct);
        if (target.Provider == DatabaseProvider.SqlServer)
        {
            var row = await ScalarRowAsync(db, """
                SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)),
                       CAST(SERVERPROPERTY('Edition') AS nvarchar(128)),
                       CAST((SELECT value_in_use FROM sys.configurations WHERE name = 'max server memory (MB)') AS nvarchar(32)),
                       CAST((SELECT cpu_count FROM sys.dm_os_sys_info) AS nvarchar(16)),
                       CAST((SELECT physical_memory_kb / 1024 FROM sys.dm_os_sys_info) AS nvarchar(32)),
                       CAST((SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()) AS nvarchar(8)),
                       CAST((SELECT recovery_model_desc FROM sys.databases WHERE name = DB_NAME()) AS nvarchar(32)),
                       CAST((SELECT committed_target_kb / 1024 FROM sys.dm_os_sys_info) AS nvarchar(32))
                """, ct);
            // SQL Server on Linux reads the container's memory limit (and by default uses 80% of it). If it saw more than
            // the limit, it could size its buffer pool past the cap, which would make it slower or get it killed.
            var limitMb = RunPlan.ContainerMemoryBytes / (1024 * 1024);
            var seenMb = ParseLong(row[4]);
            var targetMb = ParseLong(row[7]);
            var memory = seenMb > 0 && seenMb <= limitMb && targetMb <= limitMb
                ? $"- SQL Server honours the memory limit: it sees {seenMb:N0} MB of the {limitMb:N0} MB cap (on Linux it uses 80% of the limit by default), memory target {targetMb:N0} MB"
                : $"- **Warning: SQL Server sees {seenMb:N0} MB with a memory target of {targetMb:N0} MB, against the {limitMb:N0} MB container limit; it may not be honouring the limit.**";
            return
            [
                $"- SQL Server {row[0]}, {row[1]}; sees {row[3]} CPUs; max server memory {row[2]} MB",
                memory,
                $"- Seeded database: READ_COMMITTED_SNAPSHOT {(row[5] == "1" ? "ON" : "OFF")}, recovery {row[6]} (as the initializer created it)",
            ];
        }

        var settings = await ScalarRowAsync(db, """
            SELECT current_setting('server_version'), current_setting('fsync'), current_setting('synchronous_commit'),
                   current_setting('full_page_writes'), current_setting('shared_buffers'), current_setting('work_mem'),
                   current_setting('effective_cache_size'), current_setting('max_connections'),
                   (SELECT extversion FROM pg_extension WHERE extname = 'vector')
            """, ct);
        if (settings[1] != "on" || settings[2] != "on" || settings[3] != "on")
        {
            throw new InvalidOperationException(
                $"Postgres runs with fsync={settings[1]}, synchronous_commit={settings[2]}, full_page_writes={settings[3]}; all must be on for a fair comparison.");
        }

        return
        [
            $"- Postgres {settings[0]}, pgvector {settings[8]}; fsync {settings[1]}, synchronous_commit {settings[2]}, full_page_writes {settings[3]}",
            $"- Postgres memory settings (image defaults, as docker-compose.postgres.yml runs it): shared_buffers {settings[4]}, work_mem {settings[5]}, " +
                $"effective_cache_size {settings[6]}, max_connections {settings[7]}",
        ];
    }

    public static async Task<long> RowCountAsync(ProviderTarget target, string table, CancellationToken ct)
    {
        await using var db = await target.CreateContextAsync(ct);
        var sql = target.Provider == DatabaseProvider.SqlServer ? $"SELECT COUNT_BIG(*) FROM [{table}]" : $"SELECT count(*) FROM \"{table}\"";
        return ParseLong((await ScalarRowAsync(db, sql, ct))[0]);
    }

    private static async Task<string[]> ScalarRowAsync(GuideAntsApi.DataModel.ApplicationDbContext db, string sql, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Enumerable.Range(0, reader.FieldCount)
            .Select(i => reader.IsDBNull(i) ? "null" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "null")
            .ToArray();
    }

    public static async Task<string> RunAsync(string executable, string[] args, string? workingDirectory, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (workingDirectory is not null)
        {
            start.WorkingDirectory = workingDirectory;
        }

        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return string.Empty;
            }

            var stderr = process.StandardError.ReadToEndAsync(ct);
            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            await stderr;
            return process.ExitCode == 0 ? stdout : string.Empty;
        }
        catch (Win32Exception)
        {
            return string.Empty; // not installed
        }
    }

    private static string LinuxCpuModel()
    {
        try
        {
            return File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal))?.Split(':', 2)[1].Trim() ?? string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static long LinuxMemoryBytes()
    {
        try
        {
            var line = File.ReadLines("/proc/meminfo").FirstOrDefault(l => l.StartsWith("MemTotal:", StringComparison.Ordinal));
            return line is null ? 0 : ParseLong(line.Split(':', 2)[1].Replace("kB", string.Empty)) * 1024;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static long ParseLong(string text) =>
        long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string Gib(long bytes) =>
        bytes <= 0 ? "unknown" : string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):0.0} GiB");

    private static string Or(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value;
}
