using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using DatabaseSpeed.Operations;
using GuideAntsApi.DataModel.Database;

namespace DatabaseSpeed;

public sealed record CliOptions(bool Smoke, IReadOnlyList<string> Only, string? OutputDirectory, bool Help)
{
    public const string Usage = """
        Usage: dotnet run -c Release --project samples/database-speed/src/DatabaseSpeed -- [--smoke] [--only <names>] [--output <dir>]

          (no flags)      Full run (x86-64 only): the spec's data volumes, 20 warm-up + 200 measured runs per operation,
                          writes RESULTS.md next to DatabaseSpeed.sln.
          --smoke         Tiny data and few runs, allowed on any host (Apple Silicon included). Proves every operation runs
                          on both providers. Writes RESULTS-smoke.md, labelled NOT VALID FOR COMPARISON.
          --only <names>  Comma-separated parts of operation names to run (e.g. "search,cold"); the report says it's partial.
          --output <dir>  Where to write the results file.

        Exit codes: 0 done, 1 failed, 2 refused (a full run on a host that isn't x86-64).
        Images: GA_SPEED_MSSQL_IMAGE (default ghcr.io/elumenotion/mssql2025-express-fts:main),
                GA_SPEED_POSTGRES_IMAGE (default pgvector/pgvector:0.8.7-pg18).
        """;

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        var smoke = false;
        var help = false;
        string? output = null;
        var only = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--smoke":
                    smoke = true;
                    break;
                case "--help" or "-h":
                    help = true;
                    break;
                case "--only" when i + 1 < args.Count:
                    only.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--output" when i + 1 < args.Count:
                    output = args[++i];
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument '{args[i]}'.");
            }
        }

        return new CliOptions(smoke, only, output, help);
    }
}

public static class Program
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static async Task<int> Main(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(CliOptions.Usage);
            return 1;
        }

        if (options.Help)
        {
            Console.WriteLine(CliOptions.Usage);
            return 0;
        }

        var dockerArchitecture = await DockerHost.ArchitectureAsync(CancellationToken.None);
        var decision = HostGuard.Evaluate(RuntimeInformation.OSArchitecture, dockerArchitecture, options.Smoke);
        if (!decision.Proceed)
        {
            Console.Error.WriteLine(decision.Message);
            return decision.ExitCode;
        }

        if (decision.Message is not null)
        {
            Console.WriteLine(decision.Message);
        }

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        try
        {
            await RunAsync(options, decision.SqlServerEmulated, dockerArchitecture, cancel.Token);
            return 0;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            Console.Error.WriteLine("Cancelled; the containers were removed.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"database-speed failed: {ex}");
            return 1;
        }
    }

    private static async Task RunAsync(CliOptions options, bool sqlServerEmulated, string? dockerArchitecture, CancellationToken ct)
    {
        // SqlServerDatabaseInitializer reads the provider from these; the harness chooses providers itself.
        Environment.SetEnvironmentVariable("Database:Provider", null);
        Environment.SetEnvironmentVariable("Database__Provider", null);

        CommandFailures.Start();
        var plan = RunPlan.For(options.Smoke);
        var sampleRoot = FindUp(AppContext.BaseDirectory, dir => File.Exists(Path.Combine(dir, "DatabaseSpeed.sln")))
            ?? FindUp(Directory.GetCurrentDirectory(), dir => File.Exists(Path.Combine(dir, "DatabaseSpeed.sln")))
            ?? throw new InvalidOperationException("Can't find samples/database-speed/DatabaseSpeed.sln above the harness or the current directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(sampleRoot, "..", ".."));
        var storagePath = Path.Combine(Path.GetTempPath(), "guideants-database-speed");

        Log($"{(plan.Smoke ? "SMOKE run" : "Full run")}; loading the corpus");
        var corpus = Corpus.Load(Path.Combine(repoRoot, "samples", "hybrid-search-parity"));
        var seed = new SeedData(plan.Volumes, corpus, DateTime.UtcNow);
        var code = await Provenance.CodeAsync(repoRoot, ct);
        var operations = SelectOperations(BuildOperations(seed, corpus, storagePath), options.Only);

        var servers = new List<DatabaseServer>();
        var targets = new List<ProviderTarget>();
        try
        {
            Log("starting SQL Server and Postgres containers (4 CPUs, 8 GB each)");
            var starts = new[] { DatabaseProvider.SqlServer, DatabaseProvider.Postgres }
                .Select(p => DatabaseServer.StartAsync(p, ct))
                .ToList();
            try
            {
                await Task.WhenAll(starts);
            }
            finally
            {
                servers.AddRange(starts.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result));
            }

            var preparation = new List<string>();
            foreach (var server in servers)
            {
                var target = ProviderTarget.Create(server, DatabasePreparation.DatabaseName, storagePath);
                targets.Add(target);
                preparation.AddRange(await DatabasePreparation.PrepareAsync(target, seed, Log, ct));
            }

            // Both servers settled (statistics, SQL Server's full-text merge) and checkpointed, after the second seed too.
            foreach (var target in targets)
            {
                await DatabasePreparation.CheckpointAsync(target, ct);
            }

            preparation.Add("- `CHECKPOINT` on both servers after both seeds, before the first timed operation");
            var facts = await RunFacts.ReadAsync(targets, servers, ct);
            var header = await HeaderAsync(plan, seed, code, servers, targets, operations, options, dockerArchitecture, preparation, facts, ct);

            var results = new List<OperationResult>();
            foreach (var operation in operations)
            {
                Log($"{operation.Name}");
                results.Add(await Runner.RunAsync(operation, targets, plan, Log, ct));
            }

            var trailer = await TrailerAsync(operations, targets, ct);
            var report = new RunReport(plan.Smoke, "Database speed: SQL Server vs Postgres", header, results, trailer, Limits(facts), sqlServerEmulated);
            var outputDirectory = options.OutputDirectory ?? sampleRoot;
            Directory.CreateDirectory(outputDirectory);
            var path = Path.Combine(outputDirectory, plan.ResultsFileName);
            await File.WriteAllTextAsync(path, Report.Render(report), ct);

            foreach (var summary in results.SelectMany(r => r.Metrics).Select(Report.Summarize))
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {summary.Source.Metric,-62} SQL Server {Report.Ms(summary.SqlServer.MedianMs),9} ms  Postgres {Report.Ms(summary.Postgres.MedianMs),9} ms  ratio {(summary.Ratio is { } r ? r.ToString("F2", CultureInfo.InvariantCulture) : "n/a"),5}{(summary.Flagged ? "  FLAG" : string.Empty)}"));
            }

            Log($"wrote {path}");
        }
        finally
        {
            foreach (var target in targets)
            {
                await target.DisposeAsync();
            }

            foreach (var server in servers)
            {
                await server.DisposeAsync();
            }

            Log("containers removed");
        }
    }

    private static List<IOperation> BuildOperations(SeedData seed, Corpus corpus, string storagePath)
    {
        var typical = corpus.Query("Q01");
        var common = corpus.Query("Q37");
        var rare = corpus.Query("Q22");
        var longQuery = corpus.Query("Q14");
        return
        [
            new HybridSearchOperation($"hybrid search: typical phrase ({typical.Id})", typical.Text, typical.Vector,
                $"Text and vector: saved query {typical.Id} (\"{typical.Text}\").", seed),
            new HybridSearchOperation($"hybrid search: common phrase ({common.Id})", common.Text, common.Vector,
                $"Text and vector: saved query {common.Id} (\"{common.Text}\"), a phrase in about 7% of the corpus's chunks.", seed),
            new HybridSearchOperation("hybrid search: very common word", "model", longQuery.Vector,
                $"Text `model` (in about half the chunks) isn't a saved query, so it uses {longQuery.Id}'s saved vector (\"{longQuery.Text}\"); " +
                "the vector half's cost doesn't depend on which vector it is.", seed),
            new HybridSearchOperation($"hybrid search: rare phrase ({rare.Id})", rare.Text, rare.Vector,
                $"Text and vector: saved query {rare.Id} (\"{rare.Text}\"), in one chunk of the corpus.", seed),
            new LoadConversationOperation(seed),
            new SaveChatTurnOperation(seed),
            new ListProjectsOperation(seed),
            new ListNotebooksOperation(seed),
            new ListConversationsOperation(seed),
            new UsageSummaryOperation(seed),
            new ListModelsOperation(),
            new ListToolsOperation(),
            new PathPrefixOperation(seed),
            new JobClaimOperation(seed.Volumes),
            new ColdStartOperation(storagePath),
        ];
    }

    private static List<IOperation> SelectOperations(List<IOperation> all, IReadOnlyList<string> only)
    {
        if (only.Count == 0)
        {
            return all;
        }

        var selected = all.Where(o => only.Any(part => o.Name.Contains(part, StringComparison.OrdinalIgnoreCase))).ToList();
        return selected.Count > 0 ? selected : throw new ArgumentException($"--only {string.Join(',', only)} matches no operation.");
    }

    private static async Task<IReadOnlyList<ReportSection>> HeaderAsync(
        RunPlan plan,
        SeedData seed,
        CodeState code,
        IReadOnlyList<DatabaseServer> servers,
        IReadOnlyList<ProviderTarget> targets,
        IReadOnlyList<IOperation> operations,
        CliOptions options,
        string? dockerArchitecture,
        IReadOnlyList<string> preparation,
        RunFacts facts,
        CancellationToken ct)
    {
        var run = new List<string>
        {
            $"- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC by `samples/database-speed` ({(plan.Smoke ? "**--smoke**" : "full run")})",
            $"- Runs per operation: {plan.WarmupRuns} warm-up (discarded) + {plan.MeasuredRuns} measured; cold start {plan.ColdStartWarmupRuns} + {plan.ColdStartMeasuredRuns}",
            "- Interleaved: run by run, both providers, alternating which goes first; one operation at a time, nothing else running in the harness",
            "- Timed: only the production call(s) an operation is about (`Stopwatch` around them in this process), so the times include EF, the " +
                "client driver and the round trips; DI scope creation and result checks are outside the timer",
            "- p95 is nearest-rank (the value at rank ceil(0.95 n)); medians of an even count average the two middle values",
        };
        if (options.Only.Count > 0)
        {
            run.Add($"- **Partial run**: only the operations matching `--only {string.Join(',', options.Only)}`");
        }

        var images = new List<string>();
        var limits = new List<string>();
        foreach (var server in servers)
        {
            images.AddRange(await Provenance.ImageLinesAsync(server, dockerArchitecture, ct));
            limits.Add(await Provenance.LimitsLineAsync(server, ct));
            limits.Add($"- {Report.ProviderName(server.Provider)} data directory: {facts.Storage[server.Provider]}");
        }

        foreach (var target in targets)
        {
            limits.AddRange(await Provenance.ServerSettingsAsync(target, ct));
        }

        var volumes = new List<string>
        {
            $"Seed (identical on both, deterministic): {seed.Volumes.Projects} projects x {seed.Volumes.NotebooksPerProject} notebooks x " +
                $"{seed.Volumes.ConversationsPerNotebook} conversations x {seed.Volumes.MessagesPerConversation} messages; {seed.Volumes.UsageEvents:N0} usage events; " +
                $"the demo corpus of samples/hybrid-search-parity ({seed.CorpusChunks:N0} chunks) {(seed.Volumes.Chunks > seed.CorpusChunks ? "replicated" : "cut")} to " +
                $"{seed.Volumes.Chunks:N0} chunks, copy k in project k; plus a {seed.Volumes.LongConversationMessages}-message conversation, an empty conversation " +
                $"for the save operation, {seed.Volumes.NotebookFilesPerNotebook} notebook files per notebook for the path-prefix operation, and " +
                $"{SeedData.CatalogModels} catalog models (the migrations seed none). Row counts read back after seeding (identical on both, or the run stops):",
            string.Empty,
            "| Table | Planned | SQL Server | Postgres |",
            "|---|---:|---:|---:|",
        };
        foreach (var (table, rows, addedToMigrations) in seed.PlannedRows())
        {
            var counts = new List<long>();
            foreach (var target in targets)
            {
                counts.Add(await Provenance.RowCountAsync(target, table, ct));
            }

            if (counts.Distinct().Count() != 1 || (addedToMigrations ? counts[0] < rows : counts[0] != rows))
            {
                throw new InvalidOperationException($"{table} has {string.Join(" / ", counts)} rows; the seed planned {rows}{(addedToMigrations ? " plus the migrations' rows" : string.Empty)}.");
            }

            var plannedText = addedToMigrations
                ? (rows == 0 ? "the migrations' rows" : $"{rows:N0} + the migrations' rows")
                : rows.ToString("N0", CultureInfo.InvariantCulture);
            volumes.Add(string.Create(CultureInfo.InvariantCulture,
                $"| {table} | {plannedText} | {string.Join(" | ", counts.Select(c => c.ToString("N0", CultureInfo.InvariantCulture)))} |"));
        }

        volumes.Add(string.Empty);
        volumes.Add(string.Create(CultureInfo.InvariantCulture,
            $"Space used after seeding: SQL Server {facts.UsedMegabytes[DatabaseProvider.SqlServer]:N0} MB (allocated pages, full-text catalog not included), " +
            $"Postgres {facts.UsedMegabytes[DatabaseProvider.Postgres]:N0} MB (`pg_database_size`). Migrations applied to the seeded databases: " +
            $"SQL Server {facts.Migrations[DatabaseProvider.SqlServer]}, Postgres {facts.Migrations[DatabaseProvider.Postgres]}."));
        volumes.Add(string.Empty);
        volumes.Add("Settling before the first timed operation:");
        volumes.Add(string.Empty);
        volumes.Add("- statistics refreshed after seeding (`EXEC sp_updatestats` / `VACUUM ANALYZE`, which also flushes the GIN pending list)");
        volumes.AddRange(preparation);

        return
        [
            new ReportSection("Code under test", Provenance.CodeLines(code)),
            new ReportSection("Run", run),
            new ReportSection("Hardware", await Provenance.HardwareLinesAsync(ct)),
            new ReportSection("Images", images),
            new ReportSection("Containers and server settings", limits),
            new ReportSection("Data volumes", volumes),
        ];
    }

    private static async Task<IReadOnlyList<ReportSection>> TrailerAsync(IReadOnlyList<IOperation> operations, IReadOnlyList<ProviderTarget> targets, CancellationToken ct)
    {
        var plans = new List<string>();
        foreach (var explainable in operations.OfType<IExplainable>())
        {
            plans.Add($"**{explainable.ExplainTitle}**");
            plans.Add(string.Empty);
            foreach (var target in targets)
            {
                plans.Add($"{target.Name}:");
                plans.Add(string.Empty);
                plans.Add("```text");
                plans.Add(await QueryPlans.ExplainAsync(target, explainable, ct));
                plans.Add("```");
                plans.Add(string.Empty);
            }
        }

        var warnings = new List<string>();
        foreach (var target in targets)
        {
            if (target.Log.WarningCounts.Count == 0)
            {
                warnings.Add($"- {target.Name}: none");
                continue;
            }

            foreach (var (key, count) in target.Log.WarningCounts.OrderByDescending(kv => kv.Value).Take(10))
            {
                warnings.Add($"- {target.Name}: {count:N0} x `{key}`");
            }
        }

        var sections = new List<ReportSection> { new("Things to watch", WatchItems) };
        if (plans.Count > 0)
        {
            sections.Add(new ReportSection("Query plans (actual, on the seeded database, after the runs)", plans));
        }

        sections.Add(new ReportSection("Warnings the services logged (all runs)", warnings));
        return sections;
    }

    /// <summary>What the numbers can't tell, printed directly above the summary so nobody reads a ratio without it.</summary>
    private static IReadOnlyList<string> Limits(RunFacts facts) =>
    [
        $"- **(a) Cold start compares migration histories, not engines.** It is the product's fresh-install cost: SQL Server applies " +
            $"{facts.Migrations[DatabaseProvider.SqlServer]} migrations (the LiteBaselineV1 baseline and every later one, including the full-text " +
            $"catalog), Postgres {facts.Migrations[DatabaseProvider.Postgres]} (PostgresBaselineV1 and AddDocumentChunkFullText). Its ratio says nothing about either engine's speed.",
        "- **(b) One run, no variance estimate.** This is a single run on one machine (in CI, a shared VM). Medians of interleaved runs damp " +
            "noise, but nothing here measures run-to-run variance, and on sub-millisecond rows a >20% flag can be about 0.1 ms. Read the " +
            "delta (ms) next to each ratio, and dispatch the job at least twice before drawing conclusions from small differences.",
        $"- **(c) Stock settings, as shipped.** Postgres runs the image's defaults (shared_buffers {facts.PostgresSharedBuffers}, work_mem " +
            $"{facts.PostgresWorkMem}), and Npgsql's `Max Auto Prepare` is {facts.MaxAutoPrepare}" +
            (facts.MaxAutoPrepare == 0 ? " (its default: no automatic prepared statements)" : string.Empty) +
            ". SQL Server sizes its own memory and caches plans for the parameterized statements. That matches what GuideAnts ships, not a " +
            $"tuned setup. The seeded data (SQL Server {facts.UsedMegabytes[DatabaseProvider.SqlServer]:N0} MB, Postgres " +
            $"{facts.UsedMegabytes[DatabaseProvider.Postgres]:N0} MB) fits in each container's 8 GB, so after the warm-up runs it is served " +
            "from memory on both (SQL Server's buffer pool; Postgres' shared buffers plus the OS page cache).",
        "- **(d) Job claims are a stress test.** Per-claim latency counts successful claims only. Lost races, empty claims at the drain's " +
            "tail and SQL Server deadlock victims (1205, which `TryClaimAsync` catches, logs and turns into \"no job\") count as empty claims; " +
            "the drain time and throughput include them. The workers retry at once (busy-spin) instead of polling like the background " +
            "processor, so this measures contention, not the processor's normal pace.",
        "- **(e) Container limits are a quota.** Each container gets a 4-CPU time quota and an 8 GB memory cap, not dedicated cores. On a " +
            "4-vCPU GitHub runner the quota is the whole machine, shared with the harness process. Fairness comes from running one provider " +
            "at a time, interleaved run by run, not from isolation.",
        $"- **(f) Storage differs (effect unverified).** Postgres keeps its data on {facts.Storage[DatabaseProvider.Postgres]}; SQL Server on " +
            $"{facts.Storage[DatabaseProvider.SqlServer]}. Write-heavy rows (save one chat turn, job claims, cold start) may be affected; " +
            "this run doesn't measure by how much.",
    ];

    /// <summary>Carry-overs from earlier reviews; RESULTS.md says where to look for each, the parity report says what they mean.</summary>
    private static readonly IReadOnlyList<string> WatchItems =
    [
        "1. **Path-prefix `LIKE` under `guideants_ci`** (Task 7): Postgres can't range-scan a B-tree for `LIKE 'prefix%'` under the " +
            "nondeterministic collation; SQL Server can seek. Read *path-prefix lookup (notebook files)* and its plans. The production queries " +
            "also filter by `NotebookId`/`ProjectId`, so the cost depends on files per notebook, not on the table.",
        "2. **Nullable sort columns** (Task 8): `IX_Models_IsActive_DisplayOrder`, `IX_Tools_Category_IsActive_DisplayOrder` and " +
            "`IX_GuideMembers_GuideId_DisplayOrder` can't serve the SQL Server `NULLS FIRST` order on Postgres. Read *list models* and *list tools* " +
            "and their plans (40 seeded models, every fifth without a `DisplayOrder`; the migrations' tools). `GuideMembers` isn't timed: no guides are " +
            "seeded. The fix, if it matters: " +
            "`HasNullSortOrder(NullsFirst)` on those index columns in `PostgresModelAdapter` plus a Postgres migration.",
        "3. **`ts_rank_cd` before `LIMIT`** (Task 12): Postgres ranks every table-wide full-text match, across all projects, before taking the " +
            "candidates (SQL Server's `CONTAINSTABLE` top-n has the same table-wide shape). Compare the *rare*, *typical*, *common* and *very common* " +
            "hybrid-search rows; each lists its table-wide match count per provider.",
        "4. **Job claims without `SKIP LOCKED`** (Task 10, by design): a contended claimer waits for the winner and then gets no job, like SQL Server. " +
            "Read *job claims*: throughput, per-claim latency, empty claims per drain, and the failed database commands (a SQL Server " +
            "deadlock victim, 1205, stalls a claimer for the deadlock monitor's interval before `TryClaimAsync` returns no job).",
        "5. **Stock Postgres settings**: Postgres runs the image's `postgresql.conf` (shared_buffers 128 MB, work_mem 4 MB), as " +
            "docker-compose.postgres.yml ships it, while SQL Server Developer sizes its buffer pool from the container's 8 GB. Tuning is a lever " +
            "this comparison doesn't pull.",
        "6. **The query-warning interceptor**: the host's `EfQueryWarningInterceptor` is registered as in production. It builds a stack trace " +
            "(with file info) for every command with more than three JOINs, and logs only when it finds a GuideAntsApi frame, so a provider whose " +
            "SQL has more JOINs pays more of that cost, here as in the host, even when nothing is logged.",
    ];

    private static string? FindUp(string start, Func<string, bool> found)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (found(dir.FullName))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    private static void Log(string message) =>
        Console.WriteLine($"[{Clock.Elapsed:hh\\:mm\\:ss}] {message}");
}
