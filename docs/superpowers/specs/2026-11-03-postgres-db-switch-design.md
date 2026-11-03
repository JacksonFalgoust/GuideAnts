# Switchable database: SQL Server or Postgres

- **Date:** 2026-11-03
- **Branch:** `feature/postgres-db-switch` (cut from fork `main` at `3f7d818c`; rebase onto `upstream/main`, 9 commits ahead, before building)
- **Status:** design, not yet approved

## Why

Jackson's supervisor wants to find out whether GuideAnts can move from SQL Server to Postgres. The work is to run the real app on Postgres and show that it behaves the same as on SQL Server, without committing to the switch: SQL Server stays, and a setting on each install chooses which database it uses. The final decision about switching belongs to the supervisor, made from the evidence this work produces.

The work is meant to land in the upstream Elumenotion GuideAnts repo eventually, so other developers will live with it: SQL Server users must notice nothing, and the cost of carrying two databases has to stay small and obvious.

An earlier spike on `feature/postgres-hybrid-search-demo` (local only, 50 commits not pushed) compared hybrid search between the two databases in a standalone sample (`samples/hybrid-search-parity/`). Its verdict was **not switchable**, and the only blocker was the query `C#`: Postgres's parser drops the `#`. Its spec is `docs/superpowers/specs/2026-10-20-hybrid-search-parity-design.md` on that branch.

## What success looks like

The supervisor can see that everything runs the same on Postgres as on SQL Server. The proof has three parts:

1. **The whole existing test suite passes on both databases**, locally and in CI. Every integration test that runs against SQL Server today also runs against Postgres.
2. **Search parity is measured against production code.** The demo's comparison method is pointed at the app's real `HybridSearcher` running on each database, instead of at a copy of it, and reaches a "switchable" verdict. The bar for search is functional: a user can easily find what they're looking for. It isn't identical tokenization or scores.
3. **Speed is compared.** The same key operations are timed on both databases, under the same conditions.
4. **A written parity report** lists every behavioral difference found between the two databases and what was done about each one, plus the speed comparison. The supervisor reads this to make the decision.

## Goals

- One setting per install picks SQL Server or Postgres, and that covers developer machines, Docker installs and the release launcher.
- With the setting on Postgres, the full app works: sign-in, projects, notebooks, files, conversations, background jobs, usage reports, search, settings and the System Guide.
- Postgres is made to behave like SQL Server wherever their defaults differ (see "Matching SQL Server").
- Both databases are tested in CI, so neither drifts.
- Future schema changes have a clear, documented, CI-enforced way to stay in step on both databases.

## Non-goals

- Moving existing data from a SQL Server install into Postgres.
- Removing SQL Server, or making Postgres the default.
- Changing anything about how the app behaves on SQL Server.

## Anti-goals (failure even if it technically works)

- **Any behavior change for SQL Server users.** That covers the SQL text, query plans, migrations, the defaults in the Docker installs, and startup. SQL Server code paths stay byte-for-byte as they are wherever possible. Where shared code has to change, the SQL it sends to SQL Server must not.
- **Postgres quietly behaving differently.** Examples are a case-sensitive email lookup letting in a duplicate account, a different sort order, or a date that comes back shifted. A difference that is visible but undocumented counts as a failure.
- **A second database that only works for one developer.** If Postgres isn't in CI and documented, it will break within weeks of landing upstream.

## Decided

Decided with Jackson:

- SQL Server remains the default. Postgres is opt-in through a setting.
- The switch applies to every kind of install: dev, Docker and release launcher.
- **Postgres matches SQL Server, not the other way round.** Where defaults differ, extra work goes into making Postgres behave like SQL Server. SQL Server is never changed to meet Postgres halfway.
- Success means the proof described above.
- Search must be functional, so a user can easily find what they're looking for. The `C#` blocker must be fixed. The demo's 22 differences marked "acceptable" (tokenization and punctuation, where top results still mostly agree) can stay. Exact matching of tokens and scores isn't required.
- The parity report includes a speed comparison.
- No deadline.
- The work will be proposed upstream.

Claude's calls, open to change:

- The setting is `Database:Provider` with the values `SqlServer` (the default when it's missing) and `Postgres`, read once at startup next to `ConnectionStrings:DefaultConnection`. Docker passes it in as `Database__Provider`.
- Entity Framework Core uses the Npgsql provider and pgvector. The table definitions stay in one `ApplicationDbContext`, and each provider-specific detail branches on the active provider.
- Postgres gets its own migrations, starting from a single baseline that reproduces what the 118 SQL Server migrations produce today, including seeded rows. The SQL Server migrations aren't moved or edited.
- The target is Postgres 18 with pgvector (`pgvector/pgvector:pg18`). Version 18 is the first that supports `LIKE` on nondeterministic (case-insensitive) collations, which "Matching SQL Server" relies on. Verify this before committing to it.
- The demo's comparison harness comes into this branch from `feature/postgres-hybrid-search-demo` and gets retargeted, so it isn't rebuilt from scratch.
- Use no Postgres extensions beyond `vector` and what ships with Postgres (ICU collations, built-in text search). No custom C extensions such as a custom text search parser, so a Postgres GuideAnts can run on managed hosting (Azure, AWS, and similar), where custom extensions usually aren't allowed.

## The switch

| Install kind | How it's set |
|---|---|
| Developer (`dotnet run`) | `Database:Provider` in `appsettings.Development.json` or user secrets, plus a Postgres connection string. The `*.example.json` templates show both shapes. |
| Docker compose (`docker/`, `installer/docker/`) | `Database__Provider` and `ConnectionStrings__DefaultConnection` environment variables, plus a Postgres service that starts only when it's selected. SQL Server stays the service that starts by default. |
| Release launcher (`start_*.sh`, `start_windows.cmd`, `installer/guideants.*`) | One launcher-level choice (for example `GA_DB_PROVIDER=postgres`) that picks the database service and passes the two variables through. |
| Integration tests | An environment variable picks the Testcontainer: SQL Server (today's) or Postgres. CI runs the suite once per provider. |

Changing the setting points the install at a different, empty database. It never moves data (see Non-goals).

## Where SQL Server is wired in today

Found during brainstorming. All paths are under `src/server/` unless noted. The plan should re-check this list against `upstream/main` after the rebase.

**Provider registration (six places call `UseSqlServer`):**
- `GuideAntsApi/Configuration/StartupConfiguration.cs` (around lines 551 and 593): the scoped context and the pooled factory, both with `UseVectorSearch()` and retry-on-failure.
- `GuideAntsApi.DataModel/ApplicationDbContext.cs`: the static `Factory()`.
- `GuideAntsApi.DataModel/ApplicationDbContextDesignTimeFactory.cs`.
- `GuideAnts.Usage/EfUsageRecorder.cs` and `GuideAnts.Usage/AgentInvocationRecorder.cs`.
- `AntRunner.Chat/AntRunner.ToolCalling/AssistantDefinitions.Storage/DatabaseStorage.cs`.

**Model configuration (`ApplicationDbContext.cs`, 53 SQL Server-specific details):**
- `GETUTCDATE()` and `NEWID()` defaults.
- `nvarchar(max)` and `varbinary(max)` column types.
- `vector(1536)` on `DocumentChunk.Embedding`.
- Filtered unique indexes written in SQL Server bracket syntax (`[Col] IS NOT NULL`).
- `IsRowVersion()` concurrency tokens.

**Hand-written SQL and SQL Server-only APIs:**

| Where | What it does | Postgres needs |
|---|---|---|
| `GuideAntsApi.BackgroundJobs/JobQueueService.cs` (`JobQueueClaimSql`) | Claims a job with `TOP (1)` in a CTE plus `UPDATE … FROM`, guarded by the claim token. It also catches `SqlException`. | Same semantics in Postgres syntax (likely `FOR UPDATE SKIP LOCKED` with `LIMIT 1`), and a provider-aware catch. |
| `GuideAntsApi/Endpoints/AuthEndpoints.cs`, `GuideAntsApi/Services/Auth/AdminUserService.cs` | `sp_getapplock`, a transaction-scoped exclusive lock with a 10 s timeout, for registration and admin changes. | A transaction-scoped advisory lock with the same timeout behavior. |
| `GuideAntsApi/Services/Conversations/Queries/ConversationQueryService.cs` (×2), `GuideAntsApi/Services/Core/ProjectService.cs` | `SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED`, used so readers don't block. | Postgres readers don't block and it has no dirty reads, so skip the statement. Record this in the report as an intentional difference. |
| `GuideAntsApi/Settings/ApplicationSettingsConfigurationProvider.cs` | Opens a raw `SqlConnection` before dependency injection is set up, checks that `dbo.ApplicationSettings` exists, and uses `SqlConnectionStringBuilder`. | A provider-aware connection and existence check. |
| `GuideAntsApi/Services/Conversations/DistributedConversationLockService.cs` | Treats `SqlException` number 547 on `FK_ConversationLocks_NotebookConversations_ConversationId` as "conversation not found". | The Postgres foreign-key violation (SQLSTATE `23503`) on the same constraint. |
| `GuideAntsApi/Services/UsageService.cs` | `EF.Functions.DateDiffDay/Week/Month` (SQL Server only) to bucket usage. | A translation that counts boundaries crossed exactly as SQL Server's `DATEDIFF` does, including its week boundary. |
| `GuideAntsApi/Database/SqlServerDatabaseInitializer.cs` | Creates the database if missing, then migrates. | A Postgres initializer that also enables the `vector` extension (and anything "Matching SQL Server" needs). |
| `GuideAntsApi.BackgroundJobs/Services/Search/HybridSearcher.cs` | `CONTAINSTABLE` plus `vector_distance('cosine', …)`, with the vector built from a JSON literal. | The demo's Postgres query (pgvector, generated `tsvector`, `phraseto_tsquery`, `ts_rank_cd(…, 32)`), plus the `C#` fix. |

**Migrations:** 118 files. 19 of them use `migrationBuilder.Sql` with T-SQL (seed data, backfills, the full-text catalog and index in `LiteBaselineV1`).

**MARS:** every connection string sets `MultipleActiveResultSets`, and `GuideAntsApi.IntegrationTests/Services/Conversations/ConversationPersistenceMarsSqlIntegrationTests.cs` exists. Postgres has no equivalent. Any code path that keeps one reader open while issuing another command on the same connection will fail on Postgres, and those paths have to be found and fixed. The fix must not change the SQL Server behavior.

**Operations:** about 20 compose files in `docker/` and `installer/docker/` (including `installer/docker/compose/core-separate.yml`) hard-code the `mssql-express` service and connection string. `GuideAntsApi.IntegrationTests/Infrastructure/TestContainerManager.cs` and `TestWebApplicationFactory.cs` know only SQL Server (`mssql2025-express-fts`). The CI integration job in `.github/workflows/client-test.yml` runs only SQL Server. Some unit tests (`GuidesServiceSqlTranslationTests`, `CompactionBenchmarkTests`) build SQL Server contexts.

## Matching SQL Server

These are the places where Postgres's defaults differ from how GuideAnts behaves on SQL Server. Each one either gets fixed to match, or gets listed in the parity report with a reason it can't or doesn't need to match. Finding the full set is part of the work, and this list is the starting point.

- **Case-insensitive text.** SQL Server's default collation ignores case, and the app relies on that: 31 unique indexes (emails, slugs, friendly names and others) plus equality and `Contains` lookups. Postgres must compare, sort and enforce uniqueness the same way, for example with a case-insensitive ICU nondeterministic collation applied by default, which is the reason for targeting Postgres 18.
- **Trailing spaces.** SQL Server ignores trailing spaces in `=` comparisons and unique checks, and Postgres doesn't.
- **Sort order.** SQL Server puts NULLs first in ascending order, and Postgres puts them last. Any `OrderBy` on a nullable column can change order. Guid ordering also differs, since SQL Server sorts `uniqueidentifier` by an unusual byte order.
- **Dates and times.** There are 101 `DateTime` fields. Npgsql's `timestamptz` rejects non-UTC `DateTime` values, and its precision is 1 µs against SQL Server's 100 ns `datetime2(7)`. Values written and read back must compare equal, including where they serve as concurrency tokens.
- **Concurrency tokens.** `IsRowVersion()` byte-array tokens need a Postgres equivalent with the same "conflicting edit throws `DbUpdateConcurrencyException`" behavior.
- **Identifier length.** Postgres truncates names over 63 characters. Make sure no index or constraint name collides, and that the constraint name the conversation lock checks survives intact.
- **Full-text search: functional, not identical.** The `C#` blocker from the demo must be fixed on the Postgres side only, so that searches for `C#`, `C++`, `.NET` and similar terms find the chunks a user would expect, which are the ones SQL Server finds. One option is to normalize those terms in the Postgres `tsvector` expression and the query alike. SQL Server's indexing and query stay untouched. Differences in tokenization and punctuation that leave the top results useful are acceptable. Record them in the report and don't chase them.
- **Search scores.** `CONTAINSTABLE` RANK (0–1000) and `ts_rank_cd` aren't the same function, so raw scores won't match. The bar is the demo's five-condition verdict ("switchable") run against the production searcher, with no blockers.

## Migrations and keeping the two schemas in step

- The SQL Server migrations stay exactly where and how they are.
- Postgres migrations live separately, either in their own project or a separate migrations assembly, so `dotnet ef` targets each one explicitly. They start from one baseline that equals the SQL Server schema and seeded data after all 118 migrations on an empty database.
- A test compares the two freshly migrated databases: the same tables, columns (by meaning), indexes, uniqueness rules and seeded rows. It fails when one side changes without the other.
- CI fails if either provider has pending model changes without a migration (`dotnet ef migrations has-pending-model-changes`, or the equivalent).
- `GuideAntsApi.DataModel/EF_COMMANDS.md`, `docs/developer-config-guide.md` and `CLAUDE.md` document the rule that a schema change ships with a migration for each database, along with the commands for each.

## Tests and CI

- `TestContainerManager` can start either a SQL Server or a Postgres (pgvector) container, chosen by an environment variable such as `GA_INTEGRATION_TEST_DB_PROVIDER`. `TestWebApplicationFactory` registers the matching provider.
- Every existing integration test runs on both. A test that is truly SQL Server-only, like the MARS test, is marked as such, and its Postgres counterpart (or the reason it has none) goes in the report.
- The CI integration job runs once per provider, and both are required. The unit test job is unchanged except where unit tests build provider-specific contexts.
- New tests cover each behavior in "Matching SQL Server" on both providers. Each one should fail on stock Postgres defaults and pass after the fix, which proves it tests something real.

## Speed comparison

Time the same operations on both databases, under the same conditions, and put the results in the parity report.

- **What to time** (Claude's call, open to change):
  - a hybrid search query
  - claiming jobs from the queue with several workers at once
  - loading a long conversation
  - saving the messages of a chat turn
  - listing projects and notebooks
  - the usage report aggregation
  - a cold start, which includes migrating an empty database
- **Same conditions:** the same machine, the same seeded data and the same container resource limits for both databases, on x86-64 Linux. On Apple Silicon the SQL Server image is emulated, so a Mac run would make SQL Server look slower than it is. Run the timings in CI or on an x86 machine, not on a Mac.
- **What to report:** the median and p95 for each operation, with the run count and hardware. Use realistic data volumes; the demo corpus is a starting point for search.
- **No pass/fail threshold.** The supervisor judges the numbers. Claude's call: the report flags any operation where Postgres's median is more than 20% slower and explains why if the cause is known.
- `GuideAntsApi.Tests/Benchmarks/CompactionBenchmarkTests.cs` shows the repo's existing benchmark style. Whether to build on it, use BenchmarkDotNet or write a small harness is left to the builder.

## The parity report

Write a document in `docs/` (for example `docs/postgres-parity-report.md`) for the supervisor. It should cover:

- the verdict, and the evidence behind it: test results per provider, the search comparison run against production `HybridSearcher`, and the speed comparison
- every difference found, what was done about it, and where the test proving it lives
- any intentional differences (such as `READ UNCOMMITTED`) and why they're safe
- the ongoing cost of keeping both databases: the dual migrations and the CI time
- known limits of the evidence

## Left to the builder

- The exact way provider-specific code is organized (branches inside existing classes, small per-provider strategy classes, and so on). Keep SQL Server's emitted SQL unchanged.
- Whether to bring the demo branch over by merge or cherry-pick, and how to retarget its harness at production `HybridSearcher`.
- The exact Postgres mechanisms for each "Matching SQL Server" item, as long as the tests prove the match.
- The compose structure (profiles, override files, or generated files) for making the database service selectable, provided SQL Server stays the default and today's commands keep working unchanged.
- How to order and split the work into reviewable pieces.

## Risks

- **MARS dependency.** Code that keeps one reader open while issuing another command could be widespread. Find it early, because it changes how big the job is.
- **Case-insensitive collation side effects.** Nondeterministic collations limit some operations, such as pattern matching on older versions and some index types. Prove this out early, with Postgres 18 confirmed.
- **Baseline drift.** If `upstream/main` adds migrations while this work is in flight, the Postgres baseline has to absorb them. Rebase often.
- **SQL Server images on Apple Silicon** are emulated and slow, which makes local runs of both suites slow. CI is the source of truth.
