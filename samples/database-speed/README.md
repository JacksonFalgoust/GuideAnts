# Database speed: SQL Server vs Postgres

Times the same GuideAnts server operations on SQL Server and on Postgres, under the same conditions, through the
server's own services. It writes `RESULTS.md`: per operation, the run count, median and p95 for each database, the
Postgres-vs-SQL Server median ratio, and a flag on any operation where the Postgres median is more than 20% slower.
There's no pass/fail threshold; the supervisor reads the numbers. The spec is the "Speed comparison" section of
[the design](../../docs/superpowers/specs/2026-11-03-postgres-db-switch-design.md); the summary goes in the Speed
section of [the parity report](../../docs/postgres-parity-report.md).

## Run it

**Real numbers come only from x86-64 Linux.** SQL Server's image is amd64-only. On Apple Silicon it runs under
emulation and looks much slower than it is, so a full run on any other host exits `2` and says so.

- **In CI (the usual way):** GitHub > Actions > "Client and Server tests" > Run workflow, on the branch to measure. The
  `database-speed` job runs only for manual dispatches, on `ubuntu-latest`. It uploads `RESULTS.md` as the
  `database-speed-results` artifact. Download it into `samples/database-speed/` and commit it.
- **On an x86-64 Linux machine** with Docker:

  ```bash
  cd samples/database-speed
  dotnet test DatabaseSpeed.sln                                  # unit tests (no Docker)
  dotnet run -c Release --project src/DatabaseSpeed               # full run, writes RESULTS.md
  ```

- **Smoke run (any host, Apple Silicon included):** `dotnet run -c Release --project src/DatabaseSpeed -- --smoke`.
  It uses tiny data and few runs, and writes `RESULTS-smoke.md`. That file opens and closes with the banner
  **SMOKE RUN — NOT VALID FOR COMPARISON (emulated SQL Server / tiny data)**, and git ignores it. It only proves that
  every operation runs end to end on both databases.

Other options: `--only <parts of names>` runs a subset (for example `--only search,cold`), and the report then says
it's partial. `--output <dir>` writes the results file somewhere else. The images can be changed with
`GA_SPEED_MSSQL_IMAGE` (default `ghcr.io/elumenotion/mssql2025-express-fts:main`) and `GA_SPEED_POSTGRES_IMAGE`
(default `pgvector/pgvector:0.8.7-pg18`). CI uses the same GHCR SQL Server image as the integration jobs. A full run
takes roughly 30-60 minutes on a GitHub runner; that's an estimate, since there hasn't been an x86 run yet.

The harness starts its own two containers with Testcontainers and removes them when it ends. Testcontainers' reaper
also removes them if the process dies. It doesn't touch any other container.

## Same conditions

- **Containers:** both run with 4 CPUs and 8 GB of memory. These limits are read back with `docker inspect` and
  recorded, and the run stops if they differ. The CPU limit is a CPU-time quota (`--cpus`, like the compose files'
  `cpus: "4.0"`), not a CPU set, so each server still sees every host core. SQL Server runs as `MSSQL_PID=Developer`, as the compose stack files
  run it (the image's own default is Express, with its 1410 MB buffer-pool cap). Postgres runs the image's default
  configuration, as `docker-compose.postgres.yml` runs it. Testcontainers' default `fsync=off`,
  `synchronous_commit=off` and `full_page_writes=off` are removed, and the run checks that all three are `on`.
- **Database:** each seeded database is created by the host's own initializer:
  `SqlServerDatabaseInitializer.EnsureCatalogAndMigrate` or `PostgresDatabaseInitializer.EnsureDatabaseAndMigrate`.
  The connection strings use production's options: the compose stacks' SQL Server options (MARS on, the same
  timeouts and retries), and Npgsql's defaults for Postgres.
- **Services:** each provider gets a service container registered the way `StartupConfiguration` registers the real
  services:
  - the scoped `ApplicationDbContext` with `EfQueryWarningInterceptor` (60 s timeout), and the pooled factory (300 s);
  - `AddBackgroundJobs`, which picks `HybridSearcher` or `PostgresHybridSearcher` from `DatabaseProviderInfo`;
  - `ProjectService`, `CatalogService`, `ConversationQueryService`, `ConversationPersistence` and `UsageQueryService`.

  The only substitution is the embeddings provider: it returns the saved query vector, so no API is called.
- **Data:** the same deterministic seed goes into both databases, written through EF. That's 50 projects × 10
  notebooks × 20 conversations × 50 messages (25 turns of a user message and an assistant reply), and 10,000 usage
  events over 45 days. The parity sample's demo corpus (`samples/hybrid-search-parity/data`: 1,210 chunks, their
  embeddings and the saved query embeddings, checked against its manifest) is replicated to 20,000 chunks. Copy *k*
  goes to project *k*, with its own content files. On top of that:
  - a 200-message conversation for the load operation;
  - an empty conversation that the save operation appends to;
  - 20 notebook files per notebook, for the path-prefix operation, and 40 catalog models, for the models listing
    (neither is in the spec's list; see below).

  The row counts are read back from both databases and must match the plan. After seeding, SQL Server's full-text
  population is awaited, and statistics are refreshed (`sp_updatestats` / `VACUUM ANALYZE`).
- **Runs:** every operation does 20 warm-up runs, then 200 measured runs. They're interleaved run by run across the
  two databases, and which one goes first alternates. The cold start does 1 discarded warm-up plus 10 measured runs.
  Only the production call is timed, with a `Stopwatch` in the harness process, so the times include EF, the client
  driver and the round trips. Each result is checked after the timer stops, and a run that did no work fails the whole
  run instead of looking fast. Any error the services log fails the run too, because several of them swallow their
  own errors. p95 is nearest-rank.

## What it times

| Operation | Production call |
|---|---|
| hybrid search ×4: typical phrase (Q01), common phrase (Q37), very common word, rare phrase (Q22) | `IHybridSearcher.SearchAsync` scoped to one project's index, 5 results, 0.7/0.3 weights, saved query vectors |
| load a 200-message conversation | `IConversationQueryService.GetConversationWithMessagesAsync` |
| save one chat turn | `IConversationPersistence`: `CreateNextTurnAsync` (streaming) → `CreateUserMessageAsync` → `StartAssistantMessageAsync` → `CheckpointTurnAsync` → `AppendOrFinalizeAssistantMessageAsync` (finalize) → `TerminalizeTurnAsync` |
| list projects | `IProjectService.GetProjectsAsync` |
| list a project's notebooks | `IProjectService.GetProjectDetailsAsync` |
| list a notebook's conversations | `IConversationQueryService.GetListAsync` |
| usage summary over 30 days | `IUsageQueryService.GetOwnerUsageSummaryAsync(now - 30 d, now, Day)` |
| list models / list tools | `ICatalogService.GetModelsAsync` / `GetToolsAsync` (40 seeded models, since the migrations seed none; the migrations' 12 tools) |
| path-prefix lookup (notebook files) | the LINQ of `NotebookFileService`'s folder delete (`:1794`), run on the scoped context, because the method also deletes files on disk |
| job claims | 8 workers draining 1,000 jobs with `IJobQueueService.TryClaimAsync` + `CompleteAsync`: drain time (throughput), per-claim latency, empty claims |
| cold start | the initializer on a new, empty database, plus `GetProjectsAsync` on a fresh service container (server start-up isn't included) |

The "very common word" search uses the text `model`, which is in about half the chunks. It isn't a saved query, so it
uses Q14's saved vector; the vector half costs the same whichever vector it is.

## Things to watch

These are carry-overs from earlier reviews. RESULTS.md repeats them next to the numbers, and adds query plans for the
first two.

1. **`LIKE 'prefix%'` under `guideants_ci`.** Under the nondeterministic collation, Postgres can't range-scan a B-tree
   for a prefix, and SQL Server can seek. Read the path-prefix row and its plans. The production prefix queries also
   filter by notebook or project, so the cost should follow files per notebook, not table size.
2. **Indexes on nullable sort columns.** `IX_Models_IsActive_DisplayOrder`, `IX_Tools_Category_IsActive_DisplayOrder`
   and `IX_GuideMembers_GuideId_DisplayOrder` can't serve SQL Server's `NULLS FIRST` order on Postgres. Read the
   models and tools rows and their plans. `GuideMembers` isn't timed, because no guides are seeded. The fix, if it
   matters: `HasNullSortOrder(NullsFirst)` in `PostgresModelAdapter`, plus a Postgres migration.
3. **`ts_rank_cd` before `LIMIT`.** Postgres ranks every table-wide full-text match, across all projects, before it
   takes the candidates. Compare the four search rows; each one lists its table-wide match count on both databases.
4. **No `SKIP LOCKED` in the Postgres job claim**, by design. A contended claimer waits, then gets no job, as on SQL
   Server. Read the job-claim throughput and the empty claims per drain.
5. **Stock Postgres memory settings**, as shipped (`shared_buffers` 128 MB). SQL Server sizes its buffer pool from the
   container's memory instead.

## Layout

- `src/DatabaseSpeed/`: the console harness.
  - `Program.cs` (CLI, orchestration, report header);
  - `Operations/` (one file per operation, plus the shared `Operation.cs` and `QueryPlans.cs`);
  - `SeedData.cs`, `Corpus.cs`, `DatabaseServers.cs`, `ProviderTarget.cs`, `DatabasePreparation.cs`, `Runner.cs`,
    `Report.cs`, `Stats.cs`, `HostGuard.cs`, `RunPlan.cs`, `Provenance.cs`, `LogSink.cs`.
- `tests/DatabaseSpeed.Tests/`: unit tests of the report, statistics, ratio guard, host guard and run plan. They use
  MSTest and FluentAssertions, as the server tests do, and need no Docker.
- `DatabaseSpeed.sln` is its own solution, like `samples/hybrid-search-parity`'s, so `dotnet test samples/database-speed`
  runs the unit tests.
