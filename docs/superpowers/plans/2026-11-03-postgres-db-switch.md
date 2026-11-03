# Switchable Database (SQL Server or Postgres) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One setting per install (`Database:Provider` = `SqlServer` | `Postgres`) runs the whole GuideAnts server on either database, with Postgres made to behave like SQL Server, both tested in CI, and a parity report (behavior, search, speed) the supervisor can decide from.

**Architecture:** A single `ApplicationDbContext` keeps today's SQL Server model untouched; when the active provider is Npgsql, a `PostgresModelAdapter` pass at the end of `OnModelCreating` rewrites SQL Server-only details (types, defaults, filters, row versions, collation). Postgres has its own migrations assembly (`GuideAntsApi.DataModel.Postgres`) starting from one baseline. Every hand-written SQL site keeps its SQL Server text verbatim and gains a Postgres sibling chosen by provider; search gets a `PostgresHybridSearcher` behind the existing `IHybridSearcher`.

**Tech Stack:** .NET 8 (net8.0), EF Core 9.0.11, Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4, Pgvector.EntityFrameworkCore 0.3.0, Postgres 18 + pgvector (`pgvector/pgvector:0.8.7-pg18`), MSTest 3.10.4, Testcontainers 4.9.0 (MsSql + PostgreSql), Docker Compose ≥ 2.24.4.

**Spec:** [docs/superpowers/specs/2026-11-03-postgres-db-switch-design.md](../specs/2026-11-03-postgres-db-switch-design.md)

## Global Constraints

- Setting key `Database:Provider` (env `Database__Provider`); values `SqlServer` and `Postgres`, case-insensitive; missing/empty/whitespace → `SqlServer`; anything else → startup fails with `Unknown Database:Provider '<value>'. Use 'SqlServer' or 'Postgres'.`
- SQL Server behavior must not change: every SQL Server code path sends byte-identical SQL and uses the same EF options (retry 5 × 10 s, command timeout 60 s, or 300 s for the pooled factory, `UseVectorSearch`, `SingleQuery` exactly where they are today).
- Nothing under `src/server/GuideAntsApi.DataModel/Migrations/` is edited, added or moved. `dotnet ef migrations has-pending-model-changes` for SQL Server must report no changes after every task.
- Postgres 18 with pgvector, image `pgvector/pgvector:0.8.7-pg18`. Extensions: `vector` and what ships with Postgres only — no custom C extensions.
- Keep EF's default PascalCase identifiers on Postgres (no snake_case convention). Hand-written Postgres SQL double-quotes every identifier.
- Case-insensitive collation name: `guideants_ci`, `locale: "und-u-ks-level2"`, `provider: "icu"`, `deterministic: false`, applied to every string column (Postgres can't use a nondeterministic collation as a database default).
- Every schema change ships one migration per provider (documented in Task 17).
- Integration-test provider env: `GA_INTEGRATION_TEST_DB_PROVIDER` (`SqlServer` default | `Postgres`); Postgres image env `GA_INTEGRATION_TEST_POSTGRES_IMAGE` (default `pgvector/pgvector:0.8.7-pg18`). SQL Server image env stays `GA_INTEGRATION_TEST_MSSQL_IMAGE`; locally set it to `ghcr.io/elumenotion/mssql2025-express-fts:main` (amd64-only, emulated on Apple Silicon).
- Every behavioral difference found goes into the difference log in `docs/postgres-parity-report.md` (created in Task 6) with: ID, area, SQL Server behavior, Postgres default, resolution (`Fixed` / `Accepted`), proving test.
- Commits: conventional prefixes as in the repo history; **no `Co-Authored-By` trailer** (CLAUDE.md). Ask before the first commit of the session.
- Before changing any integration code (Npgsql, pgvector, Testcontainers, Compose), look up current docs with the `find-docs` skill, one concept per lookup.

## Review Focus

1. **`string.Contains` / `StartsWith` / `EndsWith` / `IndexOf` whose argument is an entity member or computed expression.** Npgsql turns these into `strpos`/`LEFT`/`RIGHT`, which error under nondeterministic collations. Expected: the query works and ignores case, as on SQL Server. Owner: Task 7.
2. **Two registrations at once under `IsolationLevel.Serializable`.** Postgres can raise serialization failure `40001` where SQL Server just blocks. Expected: both requests succeed, one as Admin and one as Pending, never a 500. Owner: Task 10.
3. **Text containing a NUL character (`\u0000`)**, for example extracted PDF text or model output saved to a message or chunk. Postgres `text` rejects it. Expected: the save succeeds on both providers. Owner: Task 9.
4. **Query parameters with `DateTimeKind.Local` or `Unspecified`** (for example `Where(e => e.Created >= someLocalOrParsedDate)`). Npgsql throws for these on `timestamptz`. Expected: the same rows as SQL Server, which compares the raw ticks. Owner: Task 9.
5. **A settings save carrying a stale `RowVersion`** (base64 round-trip through the client). Expected: `409 Conflict` on both providers, and the version changes after every successful save. Owner: Task 9.

---

## Phase 0 — Start

### Task 1: Bring in the search demo and the spec

**Files:**
- Merge: `feature/postgres-hybrid-search-demo` (adds `samples/hybrid-search-parity/**` and its spec/plan docs)
- Add: `docs/superpowers/specs/2026-11-03-postgres-db-switch-design.md`, this plan

**Interfaces:**
- Produces: `samples/hybrid-search-parity` (with `ISearchStore`, the `compare` command, and `known-differences.json`) on this branch.

- [ ] **Step 1:** Confirm the branch is at `upstream/main` (`git log -1 --format=%H` equals `git rev-parse upstream/main`, or descends from it).
- [ ] **Step 2:** `git merge --no-ff feature/postgres-hybrid-search-demo`. Expected: no conflicts, since the demo's only non-parity commit `9f8cd102` is already in `upstream/main`.
- [ ] **Step 3:** Run `cd samples/hybrid-search-parity && dotnet test --filter "Category!=Integration"`. Expected: PASS.
- [ ] **Step 4:** Commit the spec and the plan with `docs(postgres): add switchable database design and plan`.

---

## Phase 1 — The switch, with SQL Server unchanged

### Task 2: Provider setting and one place that configures the DbContext

**Files:**
- Create: `src/server/GuideAntsApi.DataModel/Database/DatabaseProvider.cs`, `DatabaseProviderSettings.cs`, `DatabaseProviderInfo.cs`, `ApplicationDbContextOptions.cs`
- Modify: `src/server/GuideAntsApi.DataModel/GuideAntsApi.DataModel.csproj` (add `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4, `Pgvector.EntityFrameworkCore` 0.3.0)
- Modify (replace `UseSqlServer` with the configurator):
  - `GuideAntsApi/Configuration/StartupConfiguration.cs` (`ConfigureDatabase`, both registrations)
  - `GuideAntsApi/Database/SqlServerDatabaseInitializer.cs` (the migrate context only)
  - `GuideAntsApi.DataModel/ApplicationDbContext.cs` (`Factory()`)
  - `GuideAntsApi.DataModel/ApplicationDbContextDesignTimeFactory.cs`
  - `GuideAnts.Usage/EfUsageRecorder.cs` (both)
  - `GuideAnts.Usage/AgentInvocationRecorder.cs`
  - `AntRunner.Chat/AntRunner.ToolCalling/AssistantDefinitions.Storage/DatabaseStorage.cs`
- Test: `src/server/GuideAntsApi.Tests/DataModel/DatabaseProviderSettingsTests.cs`, `ApplicationDbContextOptionsTests.cs`

**Interfaces:**
- Produces (namespace `GuideAntsApi.DataModel.Database`):
  - `public enum DatabaseProvider { SqlServer, Postgres }`
  - `public static class DatabaseProviderSettings { public const string ConfigurationKey = "Database:Provider"; public static DatabaseProvider Parse(string? value); public static DatabaseProvider FromEnvironment(); }`. `FromEnvironment` reads `Database:Provider`, then `Database__Provider` (Program.cs already copies all configuration into environment variables with colon keys).
  - `public sealed record DatabaseProviderInfo(DatabaseProvider Provider);` (registered as a singleton by the host)
  - `public sealed record DbProviderTuning(int CommandTimeoutSeconds = 60, bool UseVectorSearch = false, bool ExplicitSingleQuery = false);`
  - `public static class ApplicationDbContextOptions { public const string PostgresMigrationsAssembly = "GuideAntsApi.DataModel.Postgres"; public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, DatabaseProvider provider, string connectionString, DbProviderTuning tuning); }`

- [ ] **Step 1: Write failing tests.**
  - `Parse_returns_SqlServer_for_null_empty_and_whitespace`: `Parse(null)`, `Parse("")` and `Parse("  ")` each return `SqlServer`.
  - `Parse_is_case_insensitive`: `Parse("postgres")` and `Parse("POSTGRES")` return `Postgres`; `Parse("sqlserver")` returns `SqlServer`.
  - `Parse_rejects_unknown_values`: `Parse("mysql")` throws `InvalidOperationException` with exactly `Unknown Database:Provider 'mysql'. Use 'SqlServer' or 'Postgres'.`
  - `SqlServer_options_match_todays_settings`: build options with `(SqlServer, cs, new DbProviderTuning(60, UseVectorSearch: true, ExplicitSingleQuery: true))`. Assert that `SqlServerOptionsExtension` has `CommandTimeout == 60`, an execution strategy factory is set (retry), and `QuerySplittingBehavior == SingleQuery`.
  - `Postgres_options_use_npgsql_and_postgres_migrations_assembly`: assert `NpgsqlOptionsExtension` is present, `MigrationsAssembly == "GuideAntsApi.DataModel.Postgres"`, `CommandTimeout == 60`, and the retrying execution strategy is configured.
- [ ] **Step 2:** Run `dotnet test src/server/GuideAntsApi.Tests/GuideAntsApi.Tests.csproj --filter "FullyQualifiedName~DatabaseProviderSettingsTests|FullyQualifiedName~ApplicationDbContextOptionsTests"`. Expected: FAIL (types don't exist).
- [ ] **Step 3: Implement the types.**
  - **SQL Server branch:** exactly today's lambda: `EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)`, `CommandTimeout(tuning.CommandTimeoutSeconds)`, `UseVectorSearch()` only when `tuning.UseVectorSearch`, `UseQuerySplittingBehavior(SingleQuery)` only when `tuning.ExplicitSingleQuery`.
  - **Postgres branch:** `UseNpgsql(cs, o => { o.UseVector(); o.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null); o.CommandTimeout(...); o.MigrationsAssembly(PostgresMigrationsAssembly); if (ExplicitSingleQuery) o.UseQuerySplittingBehavior(SingleQuery); })`.
- [ ] **Step 4: Replace each `UseSqlServer` site,** passing the tuning that reproduces it:

  | Site | Tuning |
  |---|---|
  | `StartupConfiguration` scoped context | `(60, true, true)` |
  | `StartupConfiguration` pooled factory | `(300, true, true)` |
  | `SqlServerDatabaseInitializer` migrate context | `(60, true, true)` |
  | design-time factory | `(60, true, false)` |
  | `ApplicationDbContext.Factory()`, both Usage recorders, `DatabaseStorage` | `(60, false, false)` |

  - In `ConfigureDatabase`, read the provider with `DatabaseProviderSettings.Parse(configuration[DatabaseProviderSettings.ConfigurationKey])` and register `services.AddSingleton(new DatabaseProviderInfo(provider))`.
  - The env-only sites call `DatabaseProviderSettings.FromEnvironment()`.
  - The design-time factory also accepts `--provider <value>` in `args` (it wins over the environment).
- [ ] **Step 5:** Run the Step 2 filter again, then the full unit suite `dotnet test src/server/GuideAntsApi.Tests/GuideAntsApi.Tests.csproj`. Expected: PASS, with the same test count as before plus the new tests.
- [ ] **Step 6:** From `src/server`, run `dotnet ef migrations has-pending-model-changes --project GuideAntsApi.DataModel/GuideAntsApi.DataModel.csproj --startup-project GuideAntsApi/GuideAntsApi.csproj`. Expected: "No changes have been made to the model since the last migration."
- [ ] **Step 7:** Commit with `feat(db): add Database:Provider setting and a single DbContext configurator`.

### Task 3: Postgres model adapter

**Files:**
- Create: `src/server/GuideAntsApi.DataModel/Database/PostgresModelAdapter.cs`
- Modify: `src/server/GuideAntsApi.DataModel/ApplicationDbContext.cs` (the last line of `OnModelCreating`: `if (Database.IsNpgsql()) PostgresModelAdapter.Apply(modelBuilder);`)
- Test: `src/server/GuideAntsApi.Tests/DataModel/PostgresModelAdapterTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContextOptions.Configure` (Task 2).
- Produces: `public static class PostgresModelAdapter { public const string CaseInsensitiveCollation = "guideants_ci"; public static void Apply(ModelBuilder modelBuilder); }`

- [ ] **Step 1: Write failing tests.** Each test builds an Npgsql context with `Configure(new(), Postgres, "Host=localhost;Database=model_only", new())` and reads `context.GetService<IDesignTimeModel>().Model`, which needs no connection.
  - `No_sqlserver_column_types_remain`: no property's column type contains `nvarchar`, `varbinary`, `datetime2` or `uniqueidentifier`.
  - `No_sqlserver_default_sql_remains`: no default value SQL contains `GETUTCDATE` or `NEWID`. The 12 `GETUTCDATE()` defaults become `now()`, and `NEWID()` becomes `gen_random_uuid()`.
  - `Index_filters_use_postgres_quoting`: no filter contains `[` or `]`, and `IX` filters such as `"CorrelationId" IS NOT NULL AND "Status" IN (0, 2)` keep their logic.
  - `Every_string_column_uses_guideants_ci`: every `string` property has collation `guideants_ci`, and the model has a collation `guideants_ci` with locale `und-u-ks-level2`, provider `icu`, deterministic `false`.
  - `Row_versions_map_to_xmin`: the 5 `RowVersion` properties (`ApplicationSetting`, `JobQueue`, `LocalModelOperation`, `LocalModelInstallation`, `ProjectScheduledJob`) have column name `xmin`, column type `xid`, are concurrency tokens, and are `ValueGenerated.OnAddOrUpdate`.
  - `Embedding_maps_to_pgvector`: `DocumentChunk.Embedding` has column type `vector(1536)` and a value converter from `float[]` to `Pgvector.Vector`.
  - `Vector_extension_is_declared`: the model has the Postgres extension `vector`.
  - `All_identifiers_fit_in_63_chars`: every table, column, key, index and foreign key name is ≤ 63 characters.
  - `SqlServer_model_is_untouched`: a SQL Server-configured context still has `nvarchar(max)` on `ApplicationSetting.JsonValue` and filter `[FriendlyName] IS NOT NULL`.
- [ ] **Step 2:** Run `dotnet test src/server/GuideAntsApi.Tests/GuideAntsApi.Tests.csproj --filter "FullyQualifiedName~PostgresModelAdapterTests"`. Expected: FAIL.
- [ ] **Step 3: Implement `Apply`** as one pass over `modelBuilder.Model.GetEntityTypes()`:
  - Map column types: `nvarchar(max)` → `text`, `varbinary(max)` → `bytea`, and keep `vector(1536)` plus a `float[]`↔`Vector` converter.
  - Rewrite default SQL as listed in Step 1.
  - Change filters from `[X]` to `"X"`.
  - Give each `byte[] RowVersion` a `uint`↔`byte[]` (4-byte) converter, `HasColumnName("xmin")`, `HasColumnType("xid")`, `ValueGeneratedOnAddOrUpdate`, `IsConcurrencyToken`.
  - Call `UseCollation(CaseInsensitiveCollation)` on every string property, plus `modelBuilder.HasCollation(...)` and `modelBuilder.HasPostgresExtension("vector")`.
  - If a name is longer than 63 characters, shorten it deterministically. The FK name `FK_ConversationLocks_NotebookConversations_ConversationId` is 57 characters and must stay as it is.
- [ ] **Step 4:** Run the Step 2 filter. Expected: PASS. Then run the Task 2 Step 6 command. Expected: no changes.
- [ ] **Step 5:** Commit with `feat(db): adapt the model for Postgres without changing the SQL Server model`.

### Task 4: Postgres migrations, initializer and schema parity

**Files:**
- Create: `src/server/GuideAntsApi.DataModel.Postgres/GuideAntsApi.DataModel.Postgres.csproj` (net8.0, references DataModel), `Migrations/<timestamp>_PostgresBaselineV1.cs` (generated)
- Create: `src/server/GuideAntsApi/Database/PostgresDatabaseInitializer.cs`
- Create: `src/server/GuideAntsApi.DatabaseParity.Tests/` (MSTest 3.10.4, FluentAssertions 8.8.0, Testcontainers.MsSql + Testcontainers.PostgreSql 4.9.0, `[DoNotParallelize]`). It starts both containers once per assembly and migrates each.
- Modify: `GuideAntsApi.sln` (add both projects); `GuideAntsApi/GuideAntsApi.csproj` (ProjectReference to DataModel.Postgres so the assembly ships); `GuideAntsApi/Program.cs:186` (dispatch on provider)
- Test: `src/server/GuideAntsApi.DatabaseParity.Tests/SchemaParityTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContextOptions`, `DatabaseProviderSettings` (Task 2), `PostgresModelAdapter` (Task 3).
- Produces:
  - `public static class PostgresDatabaseInitializer { public static void EnsureDatabaseAndMigrate(string connectionString, ILogger logger); }`
  - `public sealed class ParityDatabases` (test fixture): `string SqlServerConnectionString`, `string PostgresConnectionString`, `ApplicationDbContext CreateContext(DatabaseProvider)`
  - `public static class SchemaSnapshot { static Task<SchemaSnapshot> ReadAsync(DbConnection, DatabaseProvider) }` (tables, columns, indexes, FKs, normalized to provider-neutral type categories)

- [ ] **Step 1: Write failing tests.**
  - `Postgres_baseline_migrates_an_empty_database`: `MigrateAsync()` on a fresh Postgres database succeeds, and `GetAppliedMigrations()` has exactly 1 entry.
  - `Same_tables`: the table-name sets are equal, ignoring `__EFMigrationsHistory`.
  - `Same_columns`: per table, the same column names, nullability and type category. Type categories: `nvarchar`/`varchar`/`text` → text with the same max length (`max` ↔ unbounded); `int`/`bigint`/`smallint` the same; `tinyint` ↔ `smallint`; `bit` ↔ `boolean`; `datetime2` ↔ `timestamp with time zone`; `uniqueidentifier` ↔ `uuid`; `decimal(p,s)` ↔ `numeric(p,s)` with the same p and s; `float` ↔ `double precision`; `varbinary` ↔ `bytea`; `vector(1536)` ↔ `vector(1536)`. Exception: the SQL Server `rowversion` column ↔ Postgres `xmin`, which is a system column. Assert it's absent as a user column on Postgres.
  - `Same_indexes`: per table, the same index names, column lists, uniqueness, and whether a filter exists.
  - `Same_foreign_keys`: the same names, columns, principal tables and delete rules.
- [ ] **Step 2:** Run `dotnet test src/server/GuideAntsApi.DatabaseParity.Tests --filter "FullyQualifiedName~SchemaParityTests"`. Expected: FAIL (no Postgres migrations).
- [ ] **Step 3:** From `src/server`, generate the baseline with `dotnet ef migrations add PostgresBaselineV1 --project GuideAntsApi.DataModel.Postgres/GuideAntsApi.DataModel.Postgres.csproj --startup-project GuideAntsApi/GuideAntsApi.csproj -- --provider Postgres`.
- [ ] **Step 4: Implement `EnsureDatabaseAndMigrate`.**
  - Connect to the `postgres` maintenance database and check `SELECT 1 FROM pg_database WHERE datname = @name`.
  - If it's missing, run `CREATE DATABASE "<name>"` with `"` doubled inside the name; a name over 63 characters throws `Database name exceeds maximum length (63).`.
  - Log the same messages as the SQL Server initializer, with "Postgres database" in place of "SQL Server catalog".
  - `Migrate()` through the configurator with `(60, true, true)`.
  - In `Program.cs`, branch on `DatabaseProviderSettings.Parse(bootstrapConfiguration[...])`.
- [ ] **Step 5:** Run the Step 2 filter. Expected: PASS. Fix differences in `PostgresModelAdapter` only, never in the SQL Server model.
- [ ] **Step 6:** Run `dotnet ef migrations has-pending-model-changes` once with `--project GuideAntsApi.DataModel.Postgres/... -- --provider Postgres` and once for SQL Server. Expected: no changes for both.
- [ ] **Step 7:** Commit with `feat(db): add Postgres migrations baseline, initializer and schema parity tests`.

### Task 5: Seed data parity

**Files:**
- Modify: `src/server/GuideAntsApi.DataModel.Postgres/Migrations/<timestamp>_PostgresBaselineV1.cs` (append `migrationBuilder.Sql(...)` blocks at the end of `Up`)
- Test: `src/server/GuideAntsApi.DatabaseParity.Tests/SeedDataParityTests.cs`

**Interfaces:**
- Consumes: `ParityDatabases`, `SchemaSnapshot` (Task 4).

- [ ] **Step 1: Write the failing test** `Every_table_has_the_same_rows_after_a_fresh_migrate`.
  - For every table, read all rows from both freshly migrated databases ordered by primary key, then compare column by column.
  - `DateTime` columns are compared only on null versus not-null, because they're set from `GETUTCDATE()` at migrate time.
  - `RowVersion` and `xmin` are skipped.
  - Strings are compared ordinally.
- [ ] **Step 2:** Run `dotnet test src/server/GuideAntsApi.DatabaseParity.Tests --filter "FullyQualifiedName~SeedDataParityTests"`. Expected: FAIL. It should list the rows that the 19 T-SQL migrations insert or backfill (for example `SeedUsageReportCategoriesAndOperations`) and that are missing on Postgres. `HasData` rows already match.
- [ ] **Step 3:** Add Postgres `INSERT` statements to the baseline that reproduce the final SQL Server rows. Use the test's diff as the source of truth, not a line-by-line port of each T-SQL migration.
- [ ] **Step 4:** Run the Step 2 filter. Expected: PASS.
- [ ] **Step 5:** Commit with `feat(db): seed Postgres with the same rows SQL Server migrations produce`.

---

## Phase 2 — Make Postgres behave like SQL Server

### Task 6: Run the integration suite on either provider

**Files:**
- Modify: `src/server/GuideAntsApi.IntegrationTests/GuideAntsApi.IntegrationTests.csproj` (add `Testcontainers.PostgreSql` 4.9.0)
- Modify: `src/server/GuideAntsApi.IntegrationTests/Infrastructure/TestContainerManager.cs`, `TestWebApplicationFactory.cs`
- Create: `src/server/GuideAntsApi.IntegrationTests/Infrastructure/DatabaseTestProvider.cs`
- Modify: all raw-SQL test cleanup to use provider-neutral EF `ExecuteDeleteAsync` / `ExecuteUpdateAsync`:
  - `Infrastructure/BaseEndpointTest.cs` (19 statements)
  - `Infrastructure/SettingsRoutingIntegrationTestBase.cs`
  - `Endpoints/CliAuthEndpointsTests.cs`, `AuthEndpointsTests.cs`, `AdminUsersEndpointsTests.cs`, `SystemGuideEndpointsTests.cs`, `NotebookConversationsEndpointsDeepTests.cs`, `PublishedGuidesAppIdentityEndpointsTests.cs`
  - `Services/Conversations/PublishedConversationModelResolutionTests.cs`, `PublishedConversationStreamingTests.cs`
- Modify: `Services/Conversations/ConversationPersistenceMarsSqlIntegrationTests.cs` (skip unless SQL Server)
- Create: `docs/postgres-parity-report.md`, a skeleton with the headings Verdict, Evidence, Differences (the table described in Global Constraints), Intentional differences, Ongoing cost, Limits, and Speed

**Interfaces:**
- Consumes: `ApplicationDbContextOptions`, `DatabaseProvider` (Task 2); the Postgres migrations (Task 4).
- Produces:
  - `public static class DatabaseTestProvider { public static DatabaseProvider Current { get; } public static void RequireSqlServer(); }`. `RequireSqlServer` calls `Assert.Inconclusive("SQL Server-only test")` on Postgres.
  - `TestContainerManager.Provider`, a `DatabaseProvider`.

- [ ] **Step 1:** Record the baseline on SQL Server with `dotnet test src/server/GuideAntsApi.IntegrationTests/GuideAntsApi.IntegrationTests.csproj` and note the passed, failed and skipped counts.
- [ ] **Step 2: Implement the provider switch for tests.**
  - `TestContainerManager` starts `PostgreSqlBuilder().WithImage(env ?? "pgvector/pgvector:0.8.7-pg18")` when `Current == Postgres`, with database `GuideAntsApiTest`, and migrates through the configurator.
  - `TestWebApplicationFactory` registers the context through the configurator.
  - It also sets `["Database:Provider"]` in the in-memory configuration and sets the env vars `Database:Provider` and `Database__Provider`, restoring the prior values on dispose the same way the connection-string env vars are handled.
  - Convert the cleanup SQL, keeping the same delete order and the same rows affected.
  - Call `DatabaseTestProvider.RequireSqlServer()` first in the MARS test. Note that the Testcontainers SQL Server connection string doesn't enable MARS, so today's suite already passes without it. A Postgres failure that looks MARS-related ("A command is already in progress") is a real code-path finding for Task 14, not a test-setup issue.
- [ ] **Step 3:** Run the suite on SQL Server again. Expected: the same passed, failed and skipped counts as Step 1.
- [ ] **Step 4:** Run with `GA_INTEGRATION_TEST_DB_PROVIDER=Postgres dotnet test src/server/GuideAntsApi.IntegrationTests/GuideAntsApi.IntegrationTests.csproj`. Expected: the suite runs to completion, with failures. Save the failing test names into the report's Differences section as a working list, with status `Open`.
- [ ] **Step 5:** Commit with `test(db): run integration tests on SQL Server or Postgres`.

### Task 7: Text comparison behavior

**Files:**
- Create: `src/server/GuideAntsApi.IntegrationTests/DatabaseBehavior/TextComparisonBehaviorTests.cs`
- Modify (only if a test fails): `src/server/GuideAntsApi.DataModel/Database/PostgresModelAdapter.cs`, plus any query found by the Review Focus #1 search
- Modify: `docs/postgres-parity-report.md`

**Interfaces:**
- Consumes: `DatabaseTestProvider` (Task 6), `PostgresModelAdapter.CaseInsensitiveCollation` (Task 3).

- [ ] **Step 1: Write the tests.** They run on the active provider and use `Users` and `Projects` rows created by each test.
  - `Equality_ignores_case`: a user with `Email = "Ann@Example.com"`. Then `Users.AnyAsync(u => u.Email == "ann@example.com")` is `true`.
  - `Unique_index_ignores_case`: insert `"Ann@Example.com"`, then `"ANN@example.com"`. The second `SaveChangesAsync` throws `DbUpdateException`.
  - `Contains_and_StartsWith_ignore_case`: a project named `"Alpha Project"`. `Name.Contains("ALPHA")`, `Name.StartsWith("alpha")` and `EF.Functions.Like(Name, "%PROJECT")` each find it.
  - `Member_argument_Contains_ignores_case` (Review Focus #1): two projects where `p.Name.Contains(p.Slug)` is true only when case is ignored. It finds the matching row and doesn't throw.
  - `Trailing_space_comparison` pins the Postgres result, so a later change shows up: on SQL Server `Email == "ann@example.com "` is `true`, and on Postgres it's `false`.
  - `Ordinal_sort_of_mixed_case_names` pins the order of `["b", "A", "a", "B", "_x"]` for each provider.
- [ ] **Step 2:** Run with `GA_INTEGRATION_TEST_DB_PROVIDER=Postgres dotnet test src/server/GuideAntsApi.IntegrationTests --filter "FullyQualifiedName~TextComparisonBehaviorTests"`, then without the variable (SQL Server). Expected: PASS on both.
- [ ] **Step 3: Prove the tests bite.** Comment out the `UseCollation` call in the adapter, re-run on Postgres, and confirm the first three tests FAIL. Then restore it.
- [ ] **Step 4: Search the codebase for Review Focus #1:**

  ```
  grep -rnE "\.(Contains|StartsWith|EndsWith|IndexOf)\((\w+)\.\w+" src/server --include=*.cs
  ```

  Restrict it to LINQ-to-database expressions. Rewrite only the Postgres path of any query that fails, and add a test per rewrite.
- [ ] **Step 5:** Add difference-log rows:
  - case-insensitive comparison: `Fixed`
  - trailing spaces: `Accepted`, because inputs are trimmed at the boundaries (`AuthEndpoints` trims name and email). Cite the call sites checked.
  - string sort order for punctuation and case ties: `Accepted`
- [ ] **Step 6:** Commit with `feat(db): match SQL Server's case-insensitive text comparison on Postgres`.

### Task 8: NULL ordering

**Files:**
- Create: `src/server/GuideAntsApi.DataModel/Database/SqlServerNullOrderingQuerySqlGeneratorFactory.cs` (only if Npgsql 9.0.4 has no public option)
- Modify: `src/server/GuideAntsApi.DataModel/Database/ApplicationDbContextOptions.cs` (Postgres branch)
- Create: `src/server/GuideAntsApi.IntegrationTests/DatabaseBehavior/NullOrderingBehaviorTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContextOptions.Configure` (Task 2).

- [ ] **Step 1: Write the failing tests.** Use an entity with a nullable `string` or `DateTime?` column, with three rows whose values are `null`, `"b"` and `"a"`.
  - `Ascending_puts_nulls_first`: the ascending order is `[null, "a", "b"]`.
  - `Descending_puts_nulls_last`: the descending order is `["b", "a", null]`.
- [ ] **Step 2:** Run on Postgres. Expected: FAIL. Run on SQL Server. Expected: PASS.
- [ ] **Step 3: Make Postgres order NULLs like SQL Server (`ASC` → `NULLS FIRST`, `DESC` → `NULLS LAST`).**
  - Use a public Npgsql `ReverseNullOrdering` option if 9.0.4 exposes one (look it up with `find-docs`).
  - Otherwise, write a generator deriving from `NpgsqlQuerySqlGenerator` that overrides `VisitOrdering`, and register it with `ReplaceService<IQuerySqlGeneratorFactory, SqlServerNullOrderingQuerySqlGeneratorFactory>()` in the Postgres branch only.
- [ ] **Step 4:** Run on both providers. Expected: PASS.
- [ ] **Step 5:** Add a difference-log row: `Fixed`. Also add `Accepted` for Guid sort order (`uniqueidentifier` byte order), which is used only as a tiebreak.
- [ ] **Step 6:** Commit with `feat(db): order NULLs on Postgres the way SQL Server does`.

### Task 9: Dates, NUL characters and concurrency tokens

**Files:**
- Modify: `src/server/GuideAntsApi.DataModel/Database/PostgresModelAdapter.cs`
- Modify: `src/server/GuideAntsApi.DataModel/ApplicationDbContext.cs` (`EnforceUtcOnDateTimeProperties`)
- Create: `src/server/GuideAntsApi.IntegrationTests/DatabaseBehavior/DateAndConcurrencyBehaviorTests.cs`

**Interfaces:**
- Consumes: `PostgresModelAdapter` (Task 3); the existing `SaveChangesWithGuards`.

- [ ] **Step 1: Write the failing tests.**
  - `DateTime_round_trips_exactly`: save an entity whose `DateTime` has 7 fractional digits (`new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234567)`). Read it back with a new context. The value equals the tracked value after `SaveChanges`, and `Kind == Utc`.
  - `Unspecified_and_Local_query_parameters_compare_raw_ticks` (Review Focus #4): one row with `Created = 2026-01-02T00:00:00Z`. Querying `Created >= new DateTime(2026, 1, 1, 0, 0, 0, Unspecified)` and with `Local` both find the row, and neither throws.
  - `Nul_characters_save` (Review Focus #3): saving `NotebookConversationMessage.Content = "a\u0000b"` succeeds. Reading it back gives `"a\u0000b"` on SQL Server and `"a�b"` on Postgres; this decision is pinned as an accepted difference.
  - `Concurrent_update_throws`: two contexts load the same `ApplicationSetting` and both modify `JsonValue`. The second `SaveChangesAsync` throws `DbUpdateConcurrencyException`.
  - `RowVersion_changes_on_every_update`: the base64 of `RowVersion` differs before and after an update.
  - `Settings_endpoint_rejects_stale_row_version` (Review Focus #5): saving a settings section twice with the first response's `RowVersion` returns `409` the second time.
- [ ] **Step 2:** Run on Postgres. Expected: the date, NUL and concurrency tests FAIL. Run on SQL Server. Expected: PASS.
- [ ] **Step 3: Implement the Postgres-only changes.**
  - The adapter gives every `DateTime`/`DateTime?` a write converter: `DateTime.SpecifyKind(v, Utc)` (never `ToUniversalTime`), with the ticks truncated to whole microseconds (`v.Ticks - v.Ticks % 10`). The read side is `SpecifyKind(v, Utc)`.
  - `EnforceUtcOnDateTimeProperties` also truncates tracked values to microseconds when `Database.IsNpgsql()`, so tracked and stored values stay equal.
  - Every string property gets a write converter replacing `\u0000` with `�`.
- [ ] **Step 4:** Run on both providers. Expected: PASS.
- [ ] **Step 5:** Add difference-log rows for date precision (`Fixed`), parameter `Kind` (`Fixed`), NUL characters (`Accepted`: stored as U+FFFD) and row versions (`Fixed`).
- [ ] **Step 6:** Commit with `feat(db): match SQL Server date, row version and text storage on Postgres`.

### Task 10: Job claims, application locks, isolation level and FK detection

**Files:**
- Create: `src/server/GuideAntsApi.DataModel/Database/DatabaseErrors.cs`, `src/server/GuideAntsApi/Database/DatabaseAppLock.cs`
- Modify:
  - `GuideAntsApi.BackgroundJobs/JobQueueService.cs` (add `JobQueueClaimSql.ClaimPostgres`, branch, catch)
  - `GuideAntsApi/Endpoints/AuthEndpoints.cs:43-45`
  - `GuideAntsApi/Services/Auth/AdminUserService.cs:86,359`
  - `GuideAntsApi/Services/Conversations/Queries/ConversationQueryService.cs:57-62,329`
  - `GuideAntsApi/Services/Core/ProjectService.cs:151,157`
  - `GuideAntsApi/Services/Conversations/DistributedConversationLockService.cs:112-121`
- Test: `src/server/GuideAntsApi.IntegrationTests/DatabaseBehavior/QueueAndLockBehaviorTests.cs`

**Interfaces:**
- Produces:
  - `public static class DatabaseErrors { public static bool IsForeignKeyViolation(DbUpdateException ex, string constraintName); public static bool TryGetProviderError(Exception ex, out string code, out string state); }`. For SQL Server, `code` is `SqlException.Number` and `state` is `State`. For Postgres, `code` is `PostgresException.SqlState` and `state` is `ConstraintName`.
  - `public static class DatabaseAppLock { public static Task AcquireTransactionLockAsync(DatabaseFacade database, string resource, TimeSpan timeout, CancellationToken ct); }`

- [ ] **Step 1: Write the failing tests.**
  - `Concurrent_claims_never_double_claim`: 2 pending jobs and 8 concurrent `TryClaimAsync` calls give exactly 2 non-null results with distinct ids.
  - `Claim_order_is_priority_then_created`: jobs `(priority 1, t0)`, `(priority 5, t1)`, `(priority 5, t0)` are claimed as `(5, t0)`, `(5, t1)`, `(1, t0)`.
  - `Claim_filters_by_job_type_or_any`: a claim with a type only matches that type, and a claim with `null` matches any type.
  - `App_lock_serializes_holders`: transaction A takes `"Test.Lock"` and holds it for 500 ms, while B waits. B acquires it after A commits (B's acquire time ≥ A's commit time).
  - `App_lock_timeout_proceeds_like_sp_getapplock`: while A holds the lock, B with a 200 ms timeout returns after ≥ 200 ms, doesn't throw, and its transaction is still usable. That matches the ignored `-1` return from `sp_getapplock`.
  - `Concurrent_registrations_both_succeed` (Review Focus #2): two parallel `POST /api/auth/register` calls with distinct emails on an empty Users table give two 2xx responses, one Admin and one Pending.
  - `Lock_for_missing_conversation_returns_NotFound`: `TryAcquireLockAsync(Guid.NewGuid(), ...)` returns `NotFound`.
- [ ] **Step 2:** Run on Postgres. Expected: FAIL (T-SQL errors). Run on SQL Server. Expected: PASS, except the two `App_lock_*` tests, which fail until `DatabaseAppLock` exists.
- [ ] **Step 3: Implement.**
  - **`ClaimPostgres`** keeps the same placeholders and semantics, with no `SKIP LOCKED`, so a contended claimer gets `0` rows like SQL Server:
    ```sql
    UPDATE "JobQueue" AS j
    SET "Status" = {4}, "ClaimToken" = {5}, "LeaseUntil" = {6}, "UpdatedUtc" = {1}
    WHERE j."Id" = (
        SELECT c."Id" FROM "JobQueue" AS c
        WHERE c."Status" = {0} AND c."AvailableAt" <= {1} AND c."ClaimToken" = {2}
          AND (CAST({3} AS text) IS NULL OR c."JobType" = CAST({3} AS text))
        ORDER BY c."Priority" DESC, c."Created" ASC
        LIMIT 1)
      AND j."Status" = {0} AND j."ClaimToken" = {2}
    ```
    The `catch` uses `DatabaseErrors.TryGetProviderError` with the same log message shape.
  - **`DatabaseAppLock`:**
    - On SQL Server, run the existing `EXEC sp_getapplock … @LockTimeout = <ms>` text. For the two current callers it must equal today's strings exactly, with `10000` ms.
    - On Postgres, poll `SELECT pg_try_advisory_xact_lock(hashtextextended({0}, 0))` every 100 ms until it's acquired or the timeout passes, then return. Log a warning on timeout.
  - **Serializable registration on Postgres:** if Review Focus #2 fails with `40001`, confirm that Npgsql's retrying strategy retries it. If it doesn't, retry the registration delegate on `40001`, Postgres branch only.
  - **`READ UNCOMMITTED`:** run the statement only when `Database.IsSqlServer()`. That covers both `ConversationQueryService` sites (replacing `IsRelational()`) and both `ProjectService` statements.
  - **`DistributedConversationLockService`:** call `DatabaseErrors.IsForeignKeyViolation(ex, "FK_ConversationLocks_NotebookConversations_ConversationId")`. On SQL Server that means number `547` plus the message containing the name; on Postgres it means `SqlState == "23503"` plus `ConstraintName` equal to the name.
- [ ] **Step 4:** Run on both providers. Expected: PASS.
- [ ] **Step 5:** Add difference-log rows: `READ UNCOMMITTED` (`Accepted`: Postgres readers never block, and it has no dirty reads), the app lock (`Fixed`), job claims (`Fixed`), and serializable retries (`Fixed` or `Accepted`, per Step 3).
- [ ] **Step 6:** Commit with `feat(db): port job claims, app locks and FK detection to Postgres`.

### Task 11: DB-backed settings loader and usage buckets

**Files:**
- Modify: `src/server/GuideAntsApi/Settings/ApplicationSettingsConfigurationProvider.cs`, `src/server/GuideAntsApi/Program.cs:193` (pass the provider)
- Modify: `src/server/GuideAntsApi/Services/UsageService.cs:62-72,136-146`
- Test: `src/server/GuideAntsApi.IntegrationTests/DatabaseBehavior/UsageBucketBehaviorTests.cs`. The settings loader is covered by the existing settings integration tests running on Postgres.

**Interfaces:**
- Consumes: `DatabaseProvider` (Task 2).
- Produces: `ApplicationSettingsConfigurationSource(string connectionString, DatabaseProvider provider, ISettingsSectionRegistry registry, string contentRootPath, SettingsSecretsOptions settingsSecrets)`, with the provider as the new second parameter, and the same addition on `ApplicationSettingsConfigurationProvider`.

- [ ] **Step 1: Write the failing tests.** They call the usage summary method with fixed events. The expected values follow SQL Server `DATEDIFF` semantics, where weeks cross on Sunday.
  - `Day_buckets_split_at_utc_midnight`: events at `2026-03-01T23:59:59Z` and `2026-03-02T00:00:00Z` fall in two buckets, starting `2026-03-01` and `2026-03-02`.
  - `Week_buckets_split_on_sunday`: events at `2026-03-07T12:00Z` (Saturday) and `2026-03-08T12:00Z` (Sunday) fall in two buckets.
  - `Month_buckets_split_on_the_first`: events at `2026-01-31T23:00Z` and `2026-02-01T01:00Z` fall in two buckets.
- [ ] **Step 2:** Run on Postgres. Expected: FAIL (`DateDiff*` isn't translatable). The settings tests fail at startup.
- [ ] **Step 3: Implement.**
  - **Settings loader:**
    - SQL Server keeps every current statement verbatim.
    - Postgres uses `NpgsqlConnection`, with the existence check `SELECT to_regclass('"ApplicationSettings"') IS NOT NULL` and the query `SELECT "SectionName", "JsonValue" FROM "ApplicationSettings"`.
    - `DescribeConnectionTarget` uses `NpgsqlConnectionStringBuilder` (`Host`/`Database`).
    - The Postgres missing-table message is `Required table 'ApplicationSettings' was not found while loading DB-backed application settings.`
  - **Usage buckets:** the Postgres branch replaces the `EF.Functions.DateDiff*` selector with:
    - day: `(e.Created.Date - anchor.Date).Days`
    - week: `((e.Created.Date - Sunday0).Days / 7) - ((anchor.Date - Sunday0).Days / 7)`, with `Sunday0 = new DateTime(1900, 1, 7, 0, 0, 0, DateTimeKind.Utc)`
    - month: `(e.Created.Year - anchor.Year) * 12 + e.Created.Month - anchor.Month`

    The SQL Server branch stays exactly as it is.
- [ ] **Step 4:** Run on both providers. Expected: PASS.
- [ ] **Step 5:** Add difference-log rows, both `Fixed`.
- [ ] **Step 6:** Commit with `feat(db): load DB-backed settings and bucket usage on Postgres`.

---

## Phase 3 — Search

### Task 12: Postgres hybrid search with the `C#` fix

**Files:**
- Create: `src/server/GuideAntsApi.DataModel.Postgres/Migrations/<timestamp>_AddDocumentChunkFullText.cs` (empty model diff plus hand-written SQL: `Up` creates, `Down` drops)
- Create: `src/server/GuideAntsApi.BackgroundJobs/Services/Search/PostgresHybridSearcher.cs`, `PostgresFullText.cs`
- Modify: `src/server/GuideAntsApi.BackgroundJobs/ServiceCollectionExtensions.cs:56`
- Test: `src/server/GuideAntsApi.IntegrationTests/Services/Search/HybridSearchBehaviorTests.cs`

**Interfaces:**
- Consumes: `DatabaseProviderInfo` (Task 2); `IHybridSearcher`, `HybridSearchResult` (existing).
- Produces:
  - `internal sealed class PostgresHybridSearcher : IHybridSearcher`, with the same constructor shape as `HybridSearcher`
  - `public static class PostgresFullText { public const string Config = "guideants_english_nostem"; public const string NormalizeFunction = "guideants_fts_normalize"; }`

- [ ] **Step 1: Write failing tests.** They run on both providers, with chunks inserted through EF plus a stub `IEmbeddingService` that returns fixed 1536-d vectors.
  - `Csharp_query_finds_csharp_chunks_not_bare_c`: chunks `"Writing C# services"`, `"Plan C is the fallback"` and `"C++ templates"`. Searching `"C#"` with `vectorWeight: 0`, `textWeight: 1` puts the `C#` chunk first, and the `"Plan C"` chunk has `TextScore == 0`.
  - `Cplusplus_and_dotnet_queries_match_their_chunks`: `"C++"` → the `C++` chunk is first; `".NET"` → a `".NET 8 hosting"` chunk is first.
  - `Phrase_search_requires_the_words_together`: `"conversation lock"` ranks `"the conversation lock expires"` above `"lock the conversation"`.
  - `Notebook_scope_filters_results`: with `notebookId` set, only that notebook's chunks are returned.
  - `Vector_only_match_is_returned_when_no_text_matches`: a query with no word overlap still returns the nearest chunk with `TextScore == 0` and `VectorScore > 0`.
- [ ] **Step 2:** Run on Postgres. Expected: FAIL (it resolves the SQL Server `HybridSearcher`).
- [ ] **Step 3: Implement.**
  - **The migration:**
    - Create the demo's stop-word, no-stemming configuration, renamed to `guideants_english_nostem` (see `samples/hybrid-search-parity/src/Parity.Postgres/PostgresSchema.cs`).
    - Create an `IMMUTABLE` SQL function `guideants_fts_normalize(text) RETURNS text`. It uses word-bounded, case-insensitive `regexp_replace` to rewrite `C#` → `csharp`, `F#` → `fsharp`, `C++` → `cplusplus` and `.NET` → `dotnet`.
    - Add the generated column `"ContentTsv" tsvector GENERATED ALWAYS AS (to_tsvector('guideants_english_nostem', guideants_fts_normalize("Content"))) STORED` and a GIN index `IX_DocumentChunks_ContentTsv`.
    - Add no vector index, to match SQL Server's brute-force `vector_distance`.
  - **`PostgresHybridSearcher`:**
    - Port the query in `samples/hybrid-search-parity/src/Parity.Postgres/PostgresSql.cs` with `filterBeforeTopN = false`, which is production behavior.
    - Wrap the query in `phraseto_tsquery('guideants_english_nostem', guideants_fts_normalize(@query))`.
    - Use the same `candidateLimit`, weights, `Score` formula and result mapping as `HybridSearcher`, with all values passed as `NpgsqlParameter`s, including the vector as a `Pgvector.Vector`.
  - **Registration:** `services.AddScoped<IHybridSearcher>(sp => sp.GetRequiredService<DatabaseProviderInfo>().Provider == DatabaseProvider.Postgres ? ActivatorUtilities.CreateInstance<PostgresHybridSearcher>(sp) : ActivatorUtilities.CreateInstance<HybridSearcher>(sp))`. Wherever `DatabaseProviderInfo` isn't registered, fall back to `HybridSearcher`.
- [ ] **Step 4:** Run on both providers. Expected: PASS. Then run `SchemaParityTests` and update them to expect `ContentTsv` as a Postgres-only column, the counterpart of SQL Server's full-text catalog.
- [ ] **Step 5:** Add difference-log rows: tokenization of `C#`/`C++`/`F#`/`.NET` (`Fixed`), and other punctuation tokenization (`Accepted`, functional).
- [ ] **Step 6:** Commit with `feat(search): add Postgres hybrid search with term normalization`.

### Task 13: Point the parity harness at production search

**Files:**
- Create: `samples/hybrid-search-parity/src/Parity.Production/Parity.Production.csproj` (references `src/server/GuideAntsApi.BackgroundJobs`, `GuideAntsApi.DataModel`, `GuideAntsApi.DataModel.Postgres`), `ProductionSearchStore.cs`, `PrecomputedEmbeddingService.cs`
- Modify:
  - `src/server/GuideAntsApi.BackgroundJobs/GuideAntsApi.BackgroundJobs.csproj` (`InternalsVisibleTo Parity.Production`)
  - `samples/hybrid-search-parity/src/Parity.Cli/SearchStoreFactory.cs` and `appsettings.json` (add profiles `prod-sqlserver` and `prod-postgres`)
  - `HybridSearchParity.sln`, `samples/hybrid-search-parity/known-differences.json`
- Create: `samples/hybrid-search-parity/RESULTS-production.md` (generated)

**Interfaces:**
- Consumes: `ISearchStore` (demo); `HybridSearcher`, `PostgresHybridSearcher` (Task 12); `ApplicationDbContextOptions` (Task 2).
- Produces: `public sealed class ProductionSearchStore(DatabaseProvider provider, string connectionString) : ISearchStore`, and `internal sealed class PrecomputedEmbeddingService(float[] vector) : IEmbeddingService`, which returns `vector` for every input.

- [ ] **Step 1: Write failing tests** in the demo's test project, category `Integration`:
  - `Production_store_returns_hits_on_both_providers`: seeding the demo corpus, then searching `"conversation lock"` in scope P1, returns ≥ 1 hit on each provider.
  - `Production_profiles_use_real_searchers`: the store's resolved `IHybridSearcher` type is `HybridSearcher` for `prod-sqlserver` and `PostgresHybridSearcher` for `prod-postgres`.
- [ ] **Step 2:** Run `dotnet test samples/hybrid-search-parity --filter "FullyQualifiedName~ProductionSearchStore"`. Expected: FAIL.
- [ ] **Step 3: Implement the store.**
  - `EnsureSchemaAsync` drops and recreates the demo database and runs the real `Migrate()` for the provider.
  - `LoadAsync` inserts the minimal parent rows the `DocumentChunk` foreign keys need, using the demo's deterministic ids, then the chunks through EF.
  - `WaitUntilSearchableAsync` reuses the demo's SQL Server full-text wait and is a no-op on Postgres.
  - `HybridSearchAsync` builds the real searcher for this provider with `new PrecomputedEmbeddingService(query.Vector)` (`HybridQuery` already carries the vector), then calls `SearchAsync(query.Text, indexName, notebookId, query.MaxResults, query.VectorWeight, query.TextWeight)`, mapping `Scope` to the index name and notebook id the same way the demo's stores do.
  - The production searchers compute their own candidate limit. If the demo's `CandidateMultiplier` setting disagrees with production's, record the production value in the profile, so the report doesn't compare a different query shape.
  - Keep the demo's existing safety rules (`SeedSafety`: loopback or `parity`-named databases only).
- [ ] **Step 4:** Run the Step 2 filter. Expected: PASS.
- [ ] **Step 5:** Run the comparison: `docker compose up -d`, `dotnet run --project src/Parity.Cli -- seed --profile prod-sqlserver`, the same for `prod-postgres`, then `dotnet run --project src/Parity.Cli -- compare --baseline prod-sqlserver --candidate prod-postgres --out RESULTS-production.md`.
  - Review every new or changed difference in `known-differences.json`, following the demo's review process in `REVIEW-NOTES.md`.
  - Expected: verdict **SWITCHABLE**, with zero `blocker` entries and exit code `0`.
  - If it isn't switchable, fix the search in Task 12's code and repeat. Don't relax thresholds.
- [ ] **Step 6:** Commit with `feat(parity): compare production search on SQL Server and Postgres`.

---

## Phase 4 — Green everywhere

### Task 14: Full integration suite green on Postgres

**Files:**
- Modify: as each failure requires. Production fixes go in Postgres paths only.
- Modify: `docs/postgres-parity-report.md`

**Interfaces:**
- Consumes: everything above.

- [ ] **Step 1:** Run with `GA_INTEGRATION_TEST_DB_PROVIDER=Postgres dotnet test src/server/GuideAntsApi.IntegrationTests/GuideAntsApi.IntegrationTests.csproj` and list the failures.
- [ ] **Step 2:** Fix the failures one at a time, using superpowers:systematic-debugging.
  - Find the root cause first.
  - Then add or extend a `DatabaseBehavior` test that reproduces the difference on Postgres and passes on SQL Server.
  - Fix the Postgres path, and add a difference-log row.
  - A test that is SQL Server-only by nature calls `DatabaseTestProvider.RequireSqlServer()`. Its row explains why, and names its Postgres counterpart or states that it has none.
- [ ] **Step 3:** Run the suite on Postgres. Expected: 0 failed. The only inconclusive tests are those calling `RequireSqlServer()`.
- [ ] **Step 4:** Run the suite on SQL Server. Expected: the same passed, failed and skipped counts as Task 6 Step 1, plus the new behavior tests.
- [ ] **Step 5:** Run `dotnet test src/server/GuideAntsApi.DatabaseParity.Tests`. Expected: PASS.
- [ ] **Step 6:** Commit after each fix with `fix(db): <what> on Postgres`.

### Task 15: CI runs both databases

**Files:**
- Modify: `.github/workflows/client-test.yml`

- [ ] **Step 1: Turn `server-integration-test` into a matrix** with `db: [SqlServer, Postgres]`.
  - Set `GA_INTEGRATION_TEST_DB_PROVIDER: ${{ matrix.db }}`, plus `GA_INTEGRATION_TEST_POSTGRES_IMAGE: pgvector/pgvector:0.8.7-pg18`.
  - Name the jobs `server-integration-test (SqlServer)` and `server-integration-test (Postgres)`.
- [ ] **Step 2:** Add a `database-parity` job with the same GHCR login, running `dotnet test GuideAntsApi.DatabaseParity.Tests/GuideAntsApi.DatabaseParity.Tests.csproj --configuration Release`.
- [ ] **Step 3:** Add a `migrations-in-sync` step in the `database-parity` job.
  - Run `dotnet tool install --global dotnet-ef --version 9.0.11`.
  - Then run `has-pending-model-changes` for SQL Server and for Postgres (with `-- --provider Postgres`).
  - The step fails if either reports changes.
- [ ] **Step 4:** Push the branch and confirm that all four jobs (unit, both integration jobs, parity) pass on the PR run. Then tell the user that branch protection must be updated to require both integration jobs. That's a repository setting, not a code change.
- [ ] **Step 5:** Commit with `ci: run integration tests on SQL Server and Postgres and check migrations are in sync`.

### Task 16: Docker installs and launchers

**Files:**
- Create: `docker/docker-compose.postgres.yml`, `installer/docker/docker-compose.postgres.yml` (override files), `docker/docker-compose.pgsql.yml` (database only, for server-on-host development, mirroring `docker-compose.mssql.yml`)
- Modify: `start_macos.sh`, `start_linux.sh`, `start_windows.cmd`, `installer/guideants.sh`, `installer/guideants.cmd`, `installer/stop_guideants.sh`, `installer/stop_guideants.cmd`
- Modify: `installer/docker/compose/core-separate.yml` layout: add a matching `installer/docker/compose/postgres-separate.yml`, whose `depends_on` lists only `postgres`

**Interfaces:**
- Produces:
  - Launcher flag `--db sqlserver|postgres` (default `sqlserver`); the env `GA_DB_PROVIDER` is used when the flag is absent.
  - Compose env: `GA_POSTGRES_IMAGE`, `GA_POSTGRES_PASSWORD`, plus the existing `GA_DB_NAME`.

- [ ] **Step 1: Write the override.**
  - **`postgres` service:** image `${GA_POSTGRES_IMAGE:-pgvector/pgvector:0.8.7-pg18}`, `container_name: guideants-postgres`, user `guideants`, password `${GA_POSTGRES_PASSWORD:-YourStrong!Passw0rd}`, database `${GA_DB_NAME:-guideants-dev}`, a `pg_isready` healthcheck, port `5433:5432`, `cpus: "4.0"`, volume `postgres_data` at the Postgres 18 image's data path (verify with `find-docs`), and network `guideants-network`.
  - **`mssql-express`:** `profiles: ["sqlserver"]`, so it doesn't start.
  - **`guideants-webapi-ui`:** `depends_on: !override` with `postgres: service_healthy` and `guideants-ai: service_started`. Its environment gets `Database__Provider=Postgres` and `ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=${GA_DB_NAME:-guideants-dev};Username=guideants;Password=${GA_POSTGRES_PASSWORD:-YourStrong!Passw0rd}`.
- [ ] **Step 2: Verify the merged config.** For each top-level compose file `F` in `docker/` and `installer/docker/` that defines `mssql-express`, run `docker compose -f F -f docker-compose.postgres.yml config`. Expected:
  - it exits `0`
  - `guideants-webapi-ui.environment.Database__Provider == "Postgres"`
  - `depends_on` has `postgres` and no `mssql-express`

  Running `docker compose -f F config` without the override must stay identical to `git stash`-ed output.
- [ ] **Step 3: Wire the launchers.**
  - Each launcher appends `-f docker-compose.postgres.yml` to `compose_args` when the provider is postgres, after the existing overrides, and logs `Database: Postgres`.
  - The stop scripts do the same so that `down` sees the same services.
  - The default path issues byte-identical `docker compose` commands.
- [ ] **Step 4:** Run `./start_macos.sh --db postgres` (or Linux) on a machine with Docker. Expected: `http://localhost:5107/` loads, registration works, and `docker ps` shows `guideants-postgres` and no `mssql-express`. Then run `./installer/stop_guideants.sh --db postgres`, which stops everything.
- [ ] **Step 5:** Commit with `feat(docker): add a Postgres option to compose and the launchers`.

---

## Phase 5 — Evidence

### Task 17: Speed comparison

**Files:**
- Create: `samples/database-speed/DatabaseSpeed.csproj` (console; references the server projects; Testcontainers MsSql + PostgreSql), `Program.cs`, `Operations/*.cs` (one file per operation), `SeedData.cs`, `Report.cs`, `README.md`
- Create: `samples/database-speed/RESULTS.md` (generated)
- Modify: `.github/workflows/client-test.yml` (a `database-speed` job on `workflow_dispatch` only, `ubuntu-latest`, that uploads `RESULTS.md` as an artifact)

**Interfaces:**
- Consumes: the real services: `IHybridSearcher`, `JobQueueService`, `ConversationQueryService`, `ConversationPersistence`, `ProjectService`, `UsageService`, `PostgresDatabaseInitializer`/`SqlServerDatabaseInitializer`.
- Produces:
  - `RESULTS.md`, with one table per operation: provider, runs, median ms, p95 ms, and Postgres-vs-SQL Server median ratio
  - A flag column marking any Postgres median more than 20% slower
  - A header recording the hardware, image tags, resource limits, data volumes and commit SHA

- [ ] **Step 1: Write the failing test** `Report_flags_only_medians_over_20_percent_slower`, a unit test of `Report`.
  - SQL Server median `10.0`, Postgres median `12.0` → not flagged.
  - SQL Server median `10.0`, Postgres median `12.1` → flagged.
  - p95 is computed by nearest-rank on 200 samples.
- [ ] **Step 2:** Run `dotnet test samples/database-speed`. Expected: FAIL.
- [ ] **Step 3: Implement the harness.**
  - Both containers run with the same limits: 4 CPUs and 8 GB.
  - The same seed on both: 50 projects × 10 notebooks × 20 conversations × 50 messages, 10,000 usage events, and the demo corpus chunks replicated to 20,000.
  - Each operation does 20 warm-up runs, then 200 measured runs, interleaved across providers.
  - **Operations:**
    - a hybrid search query
    - job claims with 8 concurrent workers draining 1,000 jobs (throughput plus per-claim latency)
    - loading a 200-message conversation
    - saving one chat turn's messages
    - listing projects and notebooks
    - the usage summary over 30 days
    - a cold start: migrating an empty database plus the first query (10 runs)
  - Running on a non-x86-64 host exits `2` with a message saying to run it in CI.
- [ ] **Step 4:** Run the Step 2 command. Expected: PASS. Then trigger the `database-speed` workflow and download `RESULTS.md` into `samples/database-speed/`.
- [ ] **Step 5:** Commit with `feat(parity): add a database speed comparison`.

### Task 18: Parity report and documentation

**Files:**
- Modify: `docs/postgres-parity-report.md`
- Modify:
  - `src/server/GuideAntsApi.DataModel/EF_COMMANDS.md`: both providers' `migrations add` and `database update` commands, and the rule that every schema change needs one migration per provider
  - `docs/developer-config-guide.md`: Postgres env setup, `docker/docker-compose.pgsql.yml`, the example connection string and `Database:Provider`
  - `CLAUDE.md`: commands, the dual-migration rule, and `GA_INTEGRATION_TEST_DB_PROVIDER`
  - `src/server/GuideAntsApi/appsettings.Development.example.json`: add `"Database": { "Provider": "SqlServer" }`
  - the installer README: the `--db postgres` launcher flag

- [ ] **Step 1: Fill in the report**, written for the supervisor in plain language.
  - **Verdict:** from the evidence.
  - **Evidence:** links to the green CI runs for both providers, `RESULTS-production.md` with its verdict, and the speed `RESULTS.md` summary table with the flagged rows explained.
  - **Differences:** the complete log, with no `Open` rows left.
  - **Intentional differences:** each one and why it's safe.
  - **Ongoing cost:** dual migrations, plus the extra CI minutes measured from the Task 15 runs.
  - **Limits:** at minimum, these four:
    - x86 CI hardware only
    - synthetic data volumes
    - no data migration
    - Guid and string tie ordering
- [ ] **Step 2: Check the report against the code.** Every proving test named in the log must exist (`grep` each name), and every `Fixed` row's test must pass in the latest CI run.
- [ ] **Step 3:** Commit with `docs(postgres): add the parity report and Postgres setup docs`.
- [ ] **Step 4:** Open the PR against fork `main` with `gh pr create`, per CLAUDE.md, summarizing the verdict and linking the report.
