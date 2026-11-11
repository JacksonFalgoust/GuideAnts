# Postgres parity report

Can GuideAnts run on Postgres and behave the way it does on SQL Server? This report gives the evidence for the
supervisor deciding whether to adopt Postgres, and for the developers who would carry both databases. It is the final
form of the report built up while carrying out [the plan](superpowers/plans/2026-11-03-postgres-db-switch.md) for
[the design](superpowers/specs/2026-11-03-postgres-db-switch-design.md).

- **Branch:** `feature/postgres-db-switch`. Nothing on it has been pushed since the branch was created, so nothing has
  run on GitHub yet.
- **Code under test:** every test result below comes from local runs on the code at `5c8c928a`, the last commit that
  changed `src/server`. Later commits changed only CI, Docker, the launchers, the speed harness and documentation.
- **Machine:** Apple Silicon. Postgres ran natively; SQL Server's amd64-only image ran emulated, which makes it slow
  but doesn't change results.

Contents: [Verdict](#verdict) · [Decisions needed from you](#decisions-needed-from-you) · [Evidence](#evidence) ·
[Differences](#differences) · [Intentional differences](#intentional-differences) ·
[SQL Server-specific code inventory](#sql-server-specific-code-inventory) · [Ongoing cost](#ongoing-cost) ·
[Limits](#limits) · [Speed](#speed) · [Suggested CLAUDE.md additions](#suggested-claudemd-additions)

## Verdict

**GuideAnts works on Postgres and behaves like it does on SQL Server everywhere the tests look, and search on
Postgres is functionally as good. But the proof isn't finished: CI has never run on GitHub, and the speed comparison
has no numbers yet.** On this evidence Postgres is a working, opt-in second database. The evidence isn't yet enough to
decide to switch.

The design asked for four kinds of proof:

| Part of the proof | Status | In short |
|---|---|---|
| 1. The whole existing test suite passes on both databases, locally and in CI | **Locally: yes. In CI: not run yet.** | Postgres: 364 passed, 0 failed, 2 skipped (of 366). SQL Server: 355 passed, 9 skipped and 1 failure, a known flaky test that passed on rerun (of 365). The CI workflow for both is written but has never run on GitHub. |
| 2. Search measured against the production code | **Yes: SWITCHABLE, with limits** | The app's own searchers on both databases, 152 query/scope pairs. All five conditions pass. The exact-phrase group sits exactly at the 80% same-#1 threshold. |
| 3. Speed compared under the same conditions | **No numbers yet** | The timing harness is built and runs end to end. A valid run needs an x86-64 machine, which means GitHub CI or a Linux box. |
| 4. A written log of every difference | **Yes** (this document) | 56 differences: 29 Fixed, 22 Accepted, 5 Same. None is left open. |

**What is proven:**
- **The app runs on Postgres end to end in the integration suite.** That covers sign-in and registration, projects,
  notebooks, files, conversations and streaming, background jobs, usage reports, search, settings and the System
  Guide. Every integration test that runs on SQL Server also runs on Postgres. The one exception is the SQL
  Server-only MARS test, and #15 explains why it has no Postgres counterpart.
- **Where the two databases' defaults differ, Postgres was made to match SQL Server.** That covers case-insensitive
  text, where NULLs sort, timestamp precision, row versions, NUL characters, foreign-key timing, locking, error
  detection and more. Each fix has a named test, and each difference that stays has a stated reason
  ([Differences](#differences)).
- **The two schemas stay in step.** Tests compare the freshly migrated databases: tables, columns, indexes, foreign
  keys, views and seeded rows. CI is set up to fail when either side has a model change without a migration.
- **Search is switchable.** The `C#` blocker that made the earlier spike "not switchable" is fixed.
- **SQL Server behaves as before.** Its statements are unchanged. That was checked by comparing the emitted SQL, the
  options or the statement text before and after, depending on the change. There are two small, deliberate
  exceptions, and users see neither:
  - `TryClaimAsync(null)` now works instead of throwing. No production caller passes `null` (#21).
  - The job-claim log line's structured properties are now strings. The rendered message is identical.

**What is not proven:**
- **CI on GitHub.** The new jobs have never run on a runner: both integration legs, the parity tests and the migration
  check. The flaky-test retry script was checked only against local test result files.
- **Speed.** It is still unknown whether Postgres is faster or slower. There are smoke-test numbers, but they are
  invalid (SQL Server emulated, tiny data) and are not reported.
- **The launchers.** `--db postgres` has never been run end to end, and the Windows launchers were only reviewed by
  reading.
- **Real-world use.** The evidence comes from the tests' own data, one search corpus of 38 queries and, once it runs,
  one synthetic speed data set. Postgres has not been run under real users. Moving existing SQL Server data was out
  of scope.
- **A human review of search.** Claude classified the full-text differences, as the user asked. No person has checked
  them line by line.
- **Some concurrency claims.** These were reasoned from the code, not run: the later failure points under
  concurrency (#25), the retry limits (#42), the unknown-commit case (#51) and the user impact of #55.

**How to read this for a decision:**
- **Technically, Postgres is a viable second database.** No blocker was found, and every difference is fixed or
  accepted with a stated reason.
- **Before choosing to switch, finish the proof:**
  1. Push and get CI green on both databases.
  2. Get the x86 speed numbers and read them with their limits.
  3. Run the manual launcher test.
  4. Decide row #55 for SQL Server.

  All four are listed below.
- **Then weigh the ongoing cost.** Every schema change needs two migrations, CI gets extra jobs, and provider-specific
  code lives in about 18 places ([Ongoing cost](#ongoing-cost)). Weigh that against what Postgres would bring. This
  report doesn't assess the business reasons for switching, such as hosting options or licence costs.
- **If Postgres is adopted, it can stay opt-in.** SQL Server remains the default until the speed numbers and some
  real use on Postgres are in.

## Decisions needed from you

1. **Fix the Stop race on SQL Server too? (row #55)**
   - **The problem:** a user presses Stop at the moment the AI worker saves a message. An empty assistant message can
     be left marked "still streaming". The chat view hides it, but it goes into the next turn's history as an
     assistant message with no content, and some model providers may reject that request. Anthropic is one example.
     This was reasoned from the code, not seen end to end.
   - **Where it's fixed:** on Postgres only, because the plan forbids changing the statements SQL Server receives.
   - **Where SQL Server has the same race:** whenever the database has `READ_COMMITTED_SNAPSHOT` on.
     - **Where it's on:** the integration-test database, because EF Core turns it on when it creates a database. The
       same goes for a database created by `dotnet ef database update`, and Azure SQL Database has it on by default.
     - **Default Docker and launcher installs:** their database is created by the app's own initializer, which leaves
       it off. The speed harness's database, created that way on the same image, reports it off. With it off, the
       window is narrower but not closed (reasoned, not run).
     - **A correction:** earlier task notes said the SQL Server image turns it on by default. Checking the code showed
       that EF Core does it, not the image.
   - **Options:** leave SQL Server as it is, or apply the same one-statement fix to SQL Server in a separate change.
   - **To check an install:** `SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME();`
2. **Push the branch and run CI.**
   - **Status:** the branch is more than 110 commits ahead of the fork's `main` (9 of them are the upstream commits it
     was rebased onto), and none of its work has been pushed. The first GitHub run may show problems that the local
     runs didn't.
   - **After it's green:** make branch protection require `server-integration-test (SqlServer)`,
     `server-integration-test (Postgres)`, `database-parity` and `migrations-in-sync`.
   - **The speed job:** it runs only when started by hand (`workflow_dispatch`). GitHub offers "Run workflow" only
     for a workflow whose file, with that trigger, is on the default branch. So either get the workflow change onto
     `main` first, or run the harness by hand on an x86-64 Linux machine ([Speed](#speed)).
   - **Note:** a manual dispatch also runs every other job in the workflow.
3. **Rescale the Postgres search text scores? (row #49)**
   - **Today:** they are kept as they are. Postgres text scores are about 5 to 8 times SQL Server's (median 5.68), so
     a text match moves a result up more on Postgres.
   - **What a rescale would do:** dividing by 5.68 would bring the order closer to SQL Server's (top-5 overlap 0.995,
     same #1 in 99.3% of pairs). But today's order finds the exact searched phrase more often: in the top 5 for 45 of
     51 pairs, against 41 for SQL Server and 41 after rescaling.
   - **The change:** one line in `PostgresHybridSearcher`, plus the parity diagnostics, if you prefer SQL Server's
     order.
4. **Run the manual launcher test for `--db postgres`.** It wasn't run because it would collide with the local stack
   that was running (port 5107 and fixed container names). Stop your current stack first. It uses the installer
   layout, so from the repository run `./installer/stop_guideants.sh`.

   Repository-root launcher:
   ```bash
   ./start_macos.sh --db postgres            # or: GA_DB_PROVIDER=postgres ./start_macos.sh
   # expect "Database: Postgres (docker/docker-compose.postgres.yml)"; http://localhost:5107/ loads; register works
   docker ps --format '{{.Names}}' | grep -E 'guideants-postgres|mssql-express'   # guideants-postgres only
   cd docker && docker compose -f docker-compose.ghcr-cpu.yml -f docker-compose.postgres.yml --env-file .env down
   ```
   Use the compose file the launcher logged. `start_*.sh` has no stop script. `installer/stop_guideants.sh` reads
   `installer/.installer_state.env`, not the repository-root state, so it doesn't pair with `start_macos.sh`.

   Installer launcher:
   ```bash
   ./installer/guideants.sh --db postgres    # warns that the provider changed from your saved sqlserver; layout fragments show postgres-separate.yml
   docker ps --format '{{.Names}}'           # guideants-postgres, no mssql-express
   ./installer/stop_guideants.sh             # uses the saved DB_PROVIDER=postgres; `--db postgres` gives the same result
   ./installer/guideants.sh --db sqlserver   # back to your existing SQL Server data
   ```
   On Windows, run `start_windows.cmd --db postgres`, `installer\guideants.cmd --db postgres` and
   `installer\stop_guideants.cmd`, with the same expectations.
5. **Keep the 4 `C#` (Q33) `blocker` entries in `samples/hybrid-search-parity/known-differences.json`?**
   - **Today:** they are kept, scoped with `candidate: postgres` to the earlier demo port's comparison. The
     production verdict counts 0 blockers.
   - **Why they stay:** deleting them would make the port's own comparison report Q33 as unreviewed.
   - **The cost of keeping them:** anyone reading the file sees "blocker" in it.
6. **Upstreaming, if this goes to the upstream repository:**
   - **The iCloud warning** ([Limits](#limits)) is about this machine, not for upstream readers. Drop it there.
   - **The branch history:** this branch merged `feature/postgres-hybrid-search-demo` (merge commit `9a171bb9`). That
     branch's last 50 commits were never pushed, so the history here includes the demo spike's sample commits.
     Decide whether to keep that history or tidy it before proposing upstream.
   - **The two samples:** decide whether `samples/hybrid-search-parity` and `samples/database-speed` should go
     upstream, or stay as evidence in the fork.

## Evidence

### Test results per provider

These are the latest runs, all local. The Run column names the plan task and the commit. No commit after them
changed `src/server`.

| Suite | SQL Server | Postgres | Run |
|---|---|---|---|
| Integration tests (`GuideAntsApi.IntegrationTests`), full | 365 tests: **355 passed, 1 failed, 9 skipped**. The failure is the known flaky #53, and its class (36 tests) passed on rerun | 366 tests: **364 passed, 0 failed, 2 skipped** | Task 14: SQL Server's final full run (`dfca61c1`); Postgres after the fix round (`5c8c928a`) |
| Integration tests, the conversation and `DatabaseBehavior` classes after the #55 fix (150 tests) | 140 passed, 1 failed (the same known flaky test, which passed on rerun), 9 skipped | 148 passed, 0 failed, 2 skipped | Task 14 fix round (`5c8c928a`) |
| Schema and seed parity (`GuideAntsApi.DatabaseParity.Tests`; each test starts both databases) | 16 passed (one suite covering both databases) | | Task 14 fix round |
| `dotnet ef migrations has-pending-model-changes` | "No changes" | "No changes" | Task 14 |
| Unit tests (`GuideAntsApi.Tests`), provider-neutral, including the SQL-shape tests of the Postgres code, which need no database | 2,825 passed, 1 skipped | | Task 14 fix round |
| Search comparison sample (`samples/hybrid-search-parity`), including integration on both databases | 300 passed | | Task 13 fix round |
| Speed harness unit tests (`samples/database-speed`) | 42 passed | | Task 17 |
| CI retry script (`.github/scripts/test_flaky_retry.py`) | 24 passed | | Task 15 |

- **What's skipped.**
  - SQL Server's 9 are the llama test (it needs a llama server, so it is skipped on both databases) and 8
    Postgres-only tests, which report Inconclusive on SQL Server.
  - Postgres's 2 are the llama test and the SQL Server-only MARS test (#15).
  - The fix round added one more Postgres-only test (`Stop_fence_clears_a_streaming_row_the_worker_commits_while_the_fence_waits`),
    so a full SQL Server run would now skip 10. A full SQL Server run hasn't been done since; the 150-test row covers
    the classes that changed.
- **Not affected by this work.**
  - `ScriptExecutionAgent.Tests` references neither the data model nor the API. Locally 6 of its tests fail only
    because `pwsh` isn't installed on this Mac (Task 10). CI runs it as before.
  - No commit of this work changes `src/client`. Its changes on the branch come from upstream commits, so the client
    tests weren't rerun.
- **Clean logs.**
  - No error that looks like MARS ("A command is already in progress", #15) appeared in any Postgres run.
  - The only database errors logged by passing Postgres tests are expected ones:
    - the 40001s of the two concurrency tests (#42, 6 per run);
    - in one run, the handled 23505 of #56.

**Known flaky tests.** They show the same race on both databases, and they pass on rerun. The causes are in #53 and
#54. Here is how often they failed in the 12 full runs of Task 14. 7 of the Postgres runs and 3 of the SQL Server
runs carried a temporary dump of the conversation's rows at the failing assertion; the dump wasn't committed, and it
reads after the race, so it doesn't change it.

| Test | Postgres, 8 full runs | SQL Server, 4 full runs | Rerun of its class |
|---|---|---|---|
| `SendMessageStream_Cancel_finalizes_partial_message_marks_turn_cancelled_and_prunes_incomplete_tool_calls` (#53) | 3 failed | 3 failed | passed: 3 of 3 on Postgres, 1 of 1 on SQL Server (36 tests each) |
| `SendMessageStream_Cancel_during_thinking_persists_thinking_without_zero_token_usage` (#53) | 3 failed | 0 failed | passed: same reruns |
| `RecoverStaleTurns_terminalizes_orphaned_streaming_turn_on_sql` (#54) | 0 failed | 0 failed (it failed once, in Task 7's SQL Server run) | passed: 3 of 3 on Postgres (3 tests each) |

No other test failed in any of the 12 full runs. The cancel tests were already known to be flaky upstream before this
work.

**The CI retry.** CI expects these failures, so `.github/scripts/flaky_retry.py` (tests in `test_flaky_retry.py`)
handles them:
- When the full integration run fails, the script checks the results. It accepts only one case: every non-passing
  result (anything other than Passed or NotExecuted) is one of the three tests above, and the run's own counters agree
  with that list.
- In that case it reruns exactly those tests, once. They must run and pass.
- Any other failure, or a second failure, fails the job.

**How the Postgres suite got here.** Each row is the Postgres integration run after a task.

| After | Change | Tests | Passed | Failed | Skipped | What still failed |
|---|---|---|---|---|---|---|
| Task 6 | the integration tests can target Postgres | 271 | 6 | 264 | 1 | everything that starts the host: it couldn't load its settings from Postgres (#16) |
| Task 11 | settings loader and usage buckets (#16, #22) | 276 | 215 | 59 | 2 | registration and admin lock, #17 (13); FK detection, #18 (3); test cleanup blocked by #19 (36); storage runners, #20 (7) |
| Task 6b | deferred FK checks (#19, #23 to #25) | 296 | 271 | 23 | 2 | #17, #18, #20 |
| Task 7 | case-insensitive text (#26 to #29) | 307 | 282 | 23 | 2 | #17, #18, #20 |
| Task 8 | NULL ordering (#30) | 313 | 287 | 24 | 2 | #17, #18, #20, plus 1 flaky (#53) |
| Task 10 | job claim, application lock, serializable retry, FK detection, version guard (#17, #18, #21, #41 to #43) | 345 | 334 | 9 | 2 | #20 (7), plus 2 flaky (#53) |
| Task 12 | Postgres hybrid search (#44 to #50) | 357 | 347 | 8 | 2 | #20 (7), plus 1 flaky (#53) |
| Task 14 | storage runners, retry tracker, constraint checks (#20, #51, #56) | 365 | 363 | 0 | 2 | none |
| Task 14 fix round | Stop fence (#55) | 366 | 364 | 0 | 2 | none |

- **The SQL Server runs over the same period:**
  - Task 10: 338 passed, 1 failed (#53), 6 skipped of 345.
  - Task 12: 350 passed, 0 failed, 7 skipped of 357.
  - Task 14: 355 passed, 1 failed (#53), 9 skipped of 365.
- **The early working lists:** the 264 failing tests by class and the four failure groups are in this file's history
  (`git show 99f97916:docs/postgres-parity-report.md`).
- **The diagnostic run:** between Tasks 6 and 11, a throwaway patch let the host start on Postgres. That run gave 210
  passed, 59 failed and 2 skipped, which predicted the Task 11 row exactly.

### Search: the production searchers compared

**Verdict: SWITCHABLE** ([`samples/hybrid-search-parity/RESULTS-production.md`](../samples/hybrid-search-parity/RESULTS-production.md)).
- **What ran.** The hybrid search parity harness ran GuideAnts' own searchers: `HybridSearcher` on SQL Server and
  `PostgresHybridSearcher` on Postgres. Each was resolved through production's `AddBackgroundJobs` registration, on a
  database built by the real migrations.
- **The data.** The demo corpus: 1,210 chunks from `docs/*.md` at `9f8cd102`, and 38 saved queries in 4 scopes, giving
  152 query/scope pairs.
- **Provenance.** The report records the commit of the production code under test (the `src/server` projects the
  harness builds from) and flags uncommitted changes there.
- **This run.** It was generated on 2026-11-10 from a clean tree at `ae77f06f`. The production code under test was
  last changed in `1bbaee47`. The only later change to that code, `4cf46545`, lets a context opt out of the retry
  strategy, and its default keeps the old behavior. So the searchers are unchanged since the run.

All five conditions pass in aggregate:

| Condition | Result |
|---|---|
| Vector parity | 152/152 pairs |
| Full-text differences reviewed and accepted | 26 differences, 0 unreviewed, 0 snapshot-changed, 0 blockers |
| Mean top-5 overlap (≥ 0.9) | 0.962 |
| Same #1 (≥ 80%) | 93.4% |
| Unexplained hybrid differences | 0 |

Per query group the margin differs, so the aggregate is not a wide margin everywhere:

| Group | Pairs | Mean top-5 overlap | Same #1 |
|---|---|---|---|
| exact phrases | 40 | 0.910 | 80.0% (at the threshold) |
| paraphrases | 32 | 1.000 | 100% |
| edge cases | 64 | 0.972 | 98.4% |

The results:
- **The `C#` blocker of the earlier port is gone.** There, the bare letter `c` matched 110 extra chunks, and the P2 top
  5 was disjoint. Q33's top 5 and #1 now equal SQL Server's in every scope (#44).
- **The other 22 differences are the port's, with the same chunk IDs.** They are tokenization and punctuation
  differences. Two of them are real Postgres recall misses, accepted as low impact (#45).
- **The explanations are tied to the code under test.** For each of the 1,520 top-5 hits, the comparison's diagnostics
  reproduced the searcher's own text and vector scores within the vector tolerance (1e-5). A disagreement would fail
  the verdict.
- **The text score scale (#49) is the main remaining source of ranking differences.** It is also why the exact-phrase
  group is at the threshold. It was measured and kept, because in 8 of the 10 pairs whose #1 differs, Postgres's #1
  contains the searched phrase and SQL Server's doesn't. The measurement tool and its output are committed:
  [`samples/hybrid-search-parity/tools/scale-measure`](../samples/hybrid-search-parity/tools/scale-measure/README.md).

Limits of this evidence:
- **Who reviewed the differences.** The classification of the full-text differences (`known-differences.json`,
  acceptable or blocker) was delegated to Claude by the user. No human checked it line by line; each entry's
  `reviewedBy` says so.
- **Saved query embeddings.** The harness hands the searcher the saved query vectors. So `ProviderRoutedEmbeddingService`
  and the live embedding path are not exercised; everything after the embedding is production's.
- **No quirk-fixed informational run for production.** Production's searchers have no variant that filters by scope
  before the top N. The `postgres-english` row in the report is the sample's own store, labelled as such, not a
  production variant.
- **No `F#`, `C++` or `.NET` query in the saved set.** The deliberate `F#` difference (SQL Server reads it as the noise
  word `f`, Postgres finds F# chunks) is covered only by row #44 and `HybridSearchBehaviorTests`.
- **One corpus, 38 queries.** It says nothing about other data or query mixes. SQL Server's `CONTAINSTABLE` ranks also
  moved slightly between two seedings of the same data (Q33), with identical verdict metrics.

### Speed comparison: numbers pending

**No speed numbers yet.** The harness (`samples/database-speed`) is built, unit-tested, and has run every operation
end to end on both databases. A valid comparison needs both the harness and Docker on x86-64, so the run has to
happen on GitHub CI or an x86-64 Linux machine. That needs a push, or someone with such a machine; see
[Decisions needed from you](#decisions-needed-from-you), item 2. No local numbers are reported, because SQL Server ran
emulated here. [Speed](#speed) says how to run it, what it times and what to watch for.

### Checked by running vs. only by reading

| Item | How it was checked | Still open |
|---|---|---|
| Integration, unit and parity tests | run locally, on both databases | runs on GitHub CI |
| CI workflow (`.github/workflows/client-test.yml`) | `actionlint` passes; the retry script has 24 tests on made-up result files, and it was also run on 5 real local result files | has never run on a GitHub runner |
| Bash launchers (`start_macos.sh`, `start_linux.sh`, `installer/guideants.sh`, `installer/stop_guideants.sh`) | `bash -n` with bash 3.2 and 5.3, shellcheck, and `docker compose config` on all 18 stack files with and without the Postgres override. The installer's shared wizard script was run next to the old version on the same inputs. The `postgres` service also ran on its own: it was healthy, gave PostgreSQL 18.6 with pgvector 0.8.7, and kept its data across a recreate | `--db postgres` end to end (decision 4) |
| Windows launchers (`start_windows.cmd`, `installer/guideants.cmd`, `installer/stop_guideants.cmd` and their PowerShell scripts) | read only; there's no `pwsh` on this machine | any run |
| Docker Compose `!override` in `docker-compose.postgres.yml` | behavior checked on Compose v5.1.4 | the 2.24.4 minimum comes from Compose's release history, not from a run |
| Speed harness | 42 unit tests, smoke runs on both databases, and one local full-volume seed and job drain (not timing evidence) | a full x86-64 run |
| Search comparison | the full comparison on both databases, from a clean tree | its own limits, listed above |
| Claims marked "reasoned, not run" or "inferred" in the [Differences](#differences) (#25, #42, #51, #53, #55) | reading the code | tests that exercise them |

### How to reproduce

From the repository root:

```bash
# Integration tests on SQL Server (the default). Locally, use the GHCR image (amd64 only, emulated on Apple Silicon).
GA_INTEGRATION_TEST_MSSQL_IMAGE=ghcr.io/elumenotion/mssql2025-express-fts:main \
  dotnet test src/server/GuideAntsApi.IntegrationTests/GuideAntsApi.IntegrationTests.csproj

# Integration tests on Postgres. The image can be changed with GA_INTEGRATION_TEST_POSTGRES_IMAGE (default pgvector/pgvector:0.8.7-pg18).
GA_INTEGRATION_TEST_DB_PROVIDER=Postgres \
  dotnet test src/server/GuideAntsApi.IntegrationTests/GuideAntsApi.IntegrationTests.csproj

# Schema and seed parity (starts both databases).
GA_INTEGRATION_TEST_MSSQL_IMAGE=ghcr.io/elumenotion/mssql2025-express-fts:main \
  dotnet test src/server/GuideAntsApi.DatabaseParity.Tests/GuideAntsApi.DatabaseParity.Tests.csproj
```

From `src/server`, check that both providers' migrations match the model:

```bash
dotnet ef migrations has-pending-model-changes --project GuideAntsApi.DataModel/GuideAntsApi.DataModel.csproj --startup-project GuideAntsApi/GuideAntsApi.csproj
dotnet ef migrations has-pending-model-changes --project GuideAntsApi.DataModel.Postgres/GuideAntsApi.DataModel.Postgres.csproj --startup-project GuideAntsApi/GuideAntsApi.csproj -- --provider Postgres
```

To rerun the search comparison, from `samples/hybrid-search-parity`:

```bash
docker compose up -d
dotnet run --project src/Parity.Cli -- seed --profile prod-sqlserver
dotnet run --project src/Parity.Cli -- seed --profile prod-postgres
dotnet run --project src/Parity.Cli -- compare --baseline prod-sqlserver --candidate prod-postgres --out RESULTS-production.md   # exit code 0
```

## Differences

These are the behavioral differences found between the two databases, and what was done about each. Each row has
one of three resolutions:
- `Fixed`: Postgres now behaves like SQL Server. In #55, and in the `Accepted` #51, the fix applies to Postgres
  only. SQL Server keeps a defect it already had, because this work doesn't change SQL Server.
- `Accepted`: the difference stays, and the row says why that is safe.
- `Same`: checked because a difference was suspected; both databases already behave the same, and the row records
  the proof.

That gives 29 `Fixed`, 22 `Accepted` and 5 `Same`. **No row is open.** The row numbers are stable, because code
comments and the CI workflow refer to them (CI's retry names rows 53 and 54). The last column names each proving test;
unless a row says otherwise, it runs on both databases. "Inconclusive on SQL Server" (or on Postgres) means the test
runs on one database only.

| ID | Area | SQL Server behavior | Postgres default | Resolution | Proving test |
|---|---|---|---|---|---|
| 1 | String length of unbounded strings used in a key, FK or index (`NotebookFiles.RelativePath`, `ProjectFolders.RelativePath`, `UsageEvents.UserId`, `Users.Email`) | `nvarchar(450)`, because the type mapping bounds keys and indexes | `text` | **Fixed**: the adapter maps these to `character varying(450)` (900 if non-Unicode). As in EF's own mapping, the facets come from the principal key | `String_column_lengths_match_sql_server`, `Same_columns` |
| 2 | `[MaxLength(4096)]` `ProjectExternalAuths.HeaderValue` | `nvarchar(max)`: any length over 4000 becomes max | `character varying(4096)` | **Fixed**: the adapter maps a length over 4000 (8000 if non-Unicode) to `text` | `String_column_lengths_match_sql_server`, `Same_columns` |
| 3 | Delete action of the 32 `DeleteBehavior.Restrict` FKs | `NO ACTION` (SQL Server has no `RESTRICT`) | `RESTRICT` | **Fixed**: the adapter sets these FKs to `NO ACTION` | `Restrict_foreign_keys_use_no_action_like_sql_server`, `Same_foreign_keys` |
| 4 | Shape of `DocumentChunk.Embedding` in the migrations snapshot | n/a | primitive collection of `Vector`, so the snapshot fails to build | **Fixed**: the adapter maps it to a scalar pgvector column | `Embedding_maps_to_pgvector`, `Postgres_baseline_migrates_an_empty_database` |
| 5 | `ConversationCurrentState` view | created by raw SQL in the SQL Server migrations | missing, because EF doesn't create views | **Fixed**: the baseline has a hand-written `CREATE VIEW` with the same definition | `Same_views` |
| 6 | `ApplicationSettingsBackup_20260414_ProviderRouting` table | a one-time backup table created by migration 20260414183022; not in the model and never read | absent | **Accepted**: a new install has nothing to back up | `Same_tables` |
| 7 | Filter on `IX_ConversationTurnTraces_ConversationTurnId` | `WHERE [ConversationTurnId] IS NOT NULL`, from a migration without a Designer | no filter | **Accepted**: the column is NOT NULL, so the filter has no effect | `Same_indexes` |
| 8 | Names over 63 characters (3 indexes, 11 FKs) | the full name | shortened to 54 characters + `_` + 8 hex digits | **Accepted**: Postgres limits identifiers to 63 bytes | `Same_tables`, `Same_columns`, `Same_indexes`, `Same_foreign_keys`, `Same_views` (schema parity); unit: `All_identifiers_fit_in_63_chars`, `Long_names_are_shortened_deterministically` |
| 9 | Seeded `ApplicationSettings` (14 default sections) | inserted by the T-SQL migrations 20260414183022 and 20260415112500 | none | **Fixed** in the baseline, with the same text values | `Every_table_has_the_same_rows_after_a_fresh_migrate` |
| 10 | Seeded usage report categories (9) and operations (34) | inserted by the T-SQL migration 20260619195619 | none | **Fixed** in the baseline | `Every_table_has_the_same_rows_after_a_fresh_migrate` |
| 11 | Ids of the rows in #10 | `NEWID()`, so they differ on every SQL Server database | n/a | **Accepted**: Postgres uses fixed ids. The code reads these rows by `Key`/`Operation`, never by id | `Every_table_has_the_same_rows_after_a_fresh_migrate` |
| 12 | `STT`/`stt` and `TTS`/`tts` operations | the seed lists both spellings; the case-insensitive `DISTINCT` keeps one (observed: upper case) | n/a | **Fixed**: Postgres seeds `STT` and `TTS` only | `Every_table_has_the_same_rows_after_a_fresh_migrate` |
| 13 | Rows in `ApplicationSettingsBackup_20260414_ProviderRouting` | 0 on a fresh database | table absent (#6) | **Accepted**: no rows are lost | `Every_table_has_the_same_rows_after_a_fresh_migrate` |
| 14 | Row version granularity (`rowversion` becomes the `xmin` system column) | `rowversion` is 8 bytes and changes on every update statement | `xmin` is the 32-bit id of the transaction (or savepoint) that last wrote the row | **Accepted**: <br>- Two set-based updates of one row in one transaction, with no savepoint between them (two `ExecuteUpdate` calls, or raw SQL), leave one new token on Postgres and two on SQL Server. EF wraps every `SaveChanges` inside a user transaction in a savepoint, and the savepoint's subtransaction id becomes the row's `xmin`, so tracked saves get a new token every time on both providers. That needs auto-savepoints: `ConversationPersistence.ExecuteWriteAsync` turns them off (`AutoSavepointsEnabled = false`, `ConversationPersistence.cs:1175` and `:1229`), so several saves of one row inside those blocks would share one token, but those blocks write conversation, turn, message and lock rows only, and none of these tables has a row version. No production code reads a token between two set-based updates of the same row. <br>- The token is 4 bytes (base64 `AAAB4A==`-style, 8 characters) instead of 8 (12 characters). Clients treat it as an opaque string (`types/settings.ts`, every settings tab echoes it back unchanged). <br>- `xmin` wraps around after 2^32 transactions, so a token could repeat only if one row is read, then updated about 4 billion transactions later, and the stale token is replayed: not a practical risk | `Row_versions_map_to_xmin`, `RowVersion_changes_on_every_save_inside_a_transaction`, `Two_set_based_updates_in_one_transaction_give_one_token_on_postgres_and_two_on_sql_server`, `Settings_endpoint_rejects_stale_row_version` |
| 15 | MARS savepoint warning | SqlClient-only: with MARS on, EF disables savepoints and warns | Npgsql has no MARS | **Accepted**: the test is SQL Server-only (`DatabaseTestProvider.RequireSqlServer()`) and has no Postgres counterpart. A Postgres failure that looks like MARS ("A command is already in progress") would be a Task 14 finding | `Repeated_streaming_writes_do_not_trigger_mars_savepoint_warning` (Inconclusive on Postgres) |
| 16 | DB-backed settings loader at host startup (`ApplicationSettingsConfigurationProvider`) | `SqlConnection`, `OBJECT_ID('dbo.ApplicationSettings')` | `Keyword not supported: 'host'`, so host startup aborts | **Fixed**: the loader takes the provider. On Postgres it uses `NpgsqlConnection`, checks `to_regclass('"ApplicationSettings"')` and reads `SELECT "SectionName", "JsonValue" FROM "ApplicationSettings"`; a missing table gives `Required table 'ApplicationSettings' was not found ...`. The SQL Server statements and messages are unchanged | `Settings_loader_reads_application_settings_rows`, `Settings_loader_fails_loudly_when_the_application_settings_table_is_missing`, `Load_OnPostgres_WhenServerIsUnreachable_NamesHostAndDatabase`, `Load_OnPostgres_WithInvalidConnectionString_ThrowsLoudStartupException`, `PutChatDefaults_ImmediatelyReflectsInNotebookChatReadiness` |
| 17 | Transaction-owned application lock (`EXEC sp_getapplock`) taken by registration (`GuideAnts.Auth.Register`) and the admin user mutations (`GuideAnts.Auth.AdminUsers`) | serializes them: the second caller waits up to 10 s for the first to commit. On timeout `sp_getapplock` returns -1, the return value is ignored, and the transaction carries on without the lock | `42601: syntax error at or near "EXEC"`, so `/api/auth/register` and the admin user routes return 500 | **Fixed**: both callers go through `DatabaseAppLock.AcquireTransactionLockAsync`. <br>- **SQL Server:** it sends exactly the statements the callers sent before (same resource names, `@LockTimeout = 10000`). <br>- **Postgres:** it tries `pg_try_advisory_xact_lock(hashtextextended(<resource>, 0))` every 100 ms until it gets the lock or the timeout passes, then carries on (with a warning in the log), as SQL Server does. The lock is released at commit or rollback, like `@LockOwner = 'Transaction'`. A failed try doesn't abort the transaction. <br>- **Hash collisions:** two resource names with the same 64-bit hash would share a lock (there are two names today). <br>- In a serializable transaction the lock statement takes the Postgres snapshot, so a caller that waited reads data from before the holder committed: see #42 | `App_lock_serializes_holders`, `App_lock_timeout_proceeds_like_sp_getapplock`, `Registration_lock_sends_the_original_sp_getapplock_statement`, `Admin_mutation_lock_sends_the_original_sp_getapplock_statement`, `Concurrent_registrations_both_succeed`, `Concurrent_admin_demotions_keep_one_admin`, and the 13 tests of diagnostic group A (`AdminUsersEndpointsTests` 7, `AuthEndpointsTests` 5, `CliAuthEndpointsTests` 1), which now pass |
| 18 | FK violation detection in `DistributedConversationLockService` (`SqlException.Number == 547`) | locking a missing conversation gives 404 / `KeyNotFoundException` | `23503` isn't recognized, so the caller gets 409 or "Conversation is locked by another user" | **Fixed**: the service calls `DatabaseErrors.IsForeignKeyViolation(ex, "FK_ConversationLocks_NotebookConversations_ConversationId")`. <br>- **SQL Server:** error 547 whose message contains the name, as before. <br>- **Postgres:** SQLSTATE 23503 whose `ConstraintName` is the name. <br>- **Both shapes:** the exception itself or the inner exception of a `DbUpdateException`, so a bare `PostgresException` raised by the commit of a deferred check (#25) is recognized too. <br>- This FK is not deferred (checked in `pg_constraint`), so the violation still comes from the insert | `Lock_for_missing_conversation_returns_NotFound`, `Foreign_key_violation_is_recognized_wrapped_or_raised_by_the_commit`, `Foreign_key_violation_is_recognized_wrapped_in_a_DbUpdateException`, `Foreign_key_violation_is_recognized_as_a_top_level_PostgresException`, `Foreign_key_violation_requires_the_exact_constraint_name`, `Foreign_key_violation_requires_sqlstate_23503`, and the 3 tests of diagnostic group B (`SendMessage_Stream_ForMissingConversation_Returns404`, `UndoLast_ForMissingConversation_Returns404`, `UndoLast_Throws_when_conversation_missing`), which now pass |
| 19 | Deleting a user whose cascaded role row also references that user through a `NO ACTION` FK (`UserRoles.UserId` is CASCADE, `UserRoles.AssignedByUserId` is NO ACTION; the first admin in `AuthEndpoints.cs:76` and the integration test users assign their own role) | one `DELETE FROM Users` deletes the user and the cascaded role: a NO ACTION FK is checked after every cascade of the statement | `23503 ... violates foreign key constraint "FK_UserRoles_Users_AssignedByUserId"`: the check trigger fires before the cascade trigger. Triggers of one row fire in name order, and the names embed OIDs, so the outcome follows constraint creation order (and the OID counter) | **Fixed**: the Postgres baseline declares `FK_UserRoles_Users_AssignedByUserId` `DEFERRABLE INITIALLY DEFERRED`, so the check runs at commit, after the cascade, whatever the creation order (Postgres never defers referential actions). Deleting a user who assigned another user's role still fails on both. Latent in production (no code path deletes users today). In the suite it made `BaseEndpointTest` cleanup fail for 36 tests; they pass now. Check timing: #25 | `Deleting_a_self_assigned_user_with_ExecuteDelete_removes_the_user_and_its_role`, `Deleting_a_self_assigned_user_with_Remove_and_SaveChanges_removes_the_user_and_its_role`, `Deleting_users_who_assigned_each_others_roles_in_one_statement_removes_both`, `Deleting_a_user_who_assigned_another_users_role_fails_with_ExecuteDelete`, `Deleting_a_user_who_assigned_another_users_role_fails_with_SaveChanges`, `Postgres_deferred_no_action_check_waits_for_the_cascade_whatever_the_creation_order` (Postgres-only), `Postgres_defers_exactly_the_no_action_checks_a_cascade_of_the_same_delete_could_outrun` |
| 20 | Storage migration runners (`NamedStorageMigrationRunner`, `AsciiSlugNormalizationRunner`), run by the `--run-named-storage-migration` and `--run-ascii-slug-normalization` switches | build their own context with `UseSqlServer` (vector search, 120 s timeout, no retrying strategy) | `Keyword not supported: 'host'` | **Fixed**: the switches read `Database:Provider` from configuration, as the host does, and pass it to the runners. The runners build their context with `ApplicationDbContextOptions.Configure` and the new `DbProviderTuning.RetryOnFailure: false` (`StorageMigrationDbContext`). <br>- **SQL Server:** the options are the ones the runners built before (same extensions, log fragments and service-provider keys; no execution strategy, no query-splitting setting). <br>- **Postgres:** Npgsql with the host's model settings (Postgres migrations assembly, NULL ordering), 120 s, no retrying strategy. <br>- **Why no retry:** the runners call `BeginTransactionAsync` themselves; with `EnableRetryOnFailure` Npgsql rejects that (`The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support user-initiated transactions`, seen in 3 of the 7 tests when the flag was flipped). <br>- No raw SQL in either runner | `RunAsync_Apply_NormalizesUnsafeSlugsAndMovesDirectories`, `RunAsync_DryRun_DoesNotMoveDirectoriesOrChangeSlugs`, `RunAsync_Apply_AllSlugsSafe_NoChangesRequired`, `Program_switch_runs_the_normalization_on_the_configured_provider` (`AsciiSlugNormalizationRunnerIntegrationTests`), `RunAsync_Apply_MovesGuidDirectoriesToSlugsAndRewritesDbPaths`, `RunAsync_DryRun_DoesNotMoveDirectoriesOrChangeDb`, `RunAsync_Apply_ArchivesUnmappedLegacyGuidDirectories`, `RunAsync_Apply_TargetSlugDirectoryAlreadyExists_ThrowsAndRollsBack`, `Program_switch_runs_the_migration_on_the_configured_provider` (`NamedStorageMigrationRunnerIntegrationTests`); unit: `SqlServer_options_equal_the_runners_original_inline_options`, `Postgres_options_use_npgsql_with_the_host_model_settings_and_no_retrying_strategy`, `Runner_context_does_not_retry_so_it_can_open_its_own_transactions`, `Runner_context_maps_the_model_for_its_provider` |
| 21 | Background job claim (`JobQueueService`, T-SQL `UPDATE ... TOP (1)` CTE) | claims the highest-priority, oldest pending job of the type. The candidate is chosen without a row lock, and the outer `UPDATE` re-checks status and claim token, so a claimer that loses the race for the row updates nothing and gets no job this time | `42601: syntax error at or near "."`, logged 26 times by `BackgroundJobProcessor` in the diagnostic run (no test failed on it there) | **Fixed**: on Postgres the claim runs `JobQueueClaimSql.ClaimPostgres` (`UPDATE ... WHERE "Id" = (SELECT ... ORDER BY "Priority" DESC, "Created" ASC LIMIT 1) AND` the same re-check), with the same placeholders and parameters. <br>- **No `SKIP LOCKED`:** a claimer that picked a row another claimer is updating waits, re-checks the committed row and updates nothing, as on SQL Server. Which claimer loses depends on timing on both. <br>- **Errors:** the `catch` reads `DatabaseErrors.TryGetProviderError`, so a Postgres error is logged and the claim reports no job, like a `SqlException`. The SQL Server log line reads as before (`Job claim failed with SqlException Number=... State=...`); on Postgres it is `PostgresException Number=<SQLSTATE> State=<constraint>`. <br>- **`null` job type (any type):** the claim passed `DBNull.Value`, which EF Core can't map, so it threw `InvalidOperationException` on both providers. No production caller passes `null` (the processor claims each registered type), but the interface allows it. It now passes `null`, so the claim works on both. That is the one SQL Server behavior change in Task 10 | `Concurrent_claims_never_double_claim`, `Claim_order_is_priority_then_created`, `Claim_filters_by_job_type_or_any`, `Claim_logs_a_provider_error_and_returns_no_job`, `TryClaimSql_OnPostgres_RechecksPendingStatusAndEmptyClaimTokenOnOuterUpdate` |
| 22 | Usage report time series buckets (`UsageQueryService` owner and project summaries) | `EF.Functions.DateDiffDay/Week/Month` count the UTC midnights, Sundays and month starts crossed since the anchor | `DateDiff*` can't be translated, so the summary throws | **Fixed**: on Postgres the bucket index is `(Created.Date - anchor.Date).Days` for days, the difference of whole weeks since Sunday 1900-01-07 for weeks, and `(year diff) * 12 + month diff` for months. Npgsql translates these with `date_trunc('day', "Created", 'UTC')` and `AT TIME ZONE 'UTC'`, so the result doesn't depend on the session `TimeZone`. The SQL Server SQL is unchanged (the EF command log before and after is byte-identical) | `Day_buckets_split_at_utc_midnight`, `Week_buckets_split_on_sunday`, `Month_buckets_split_on_the_first`, `Month_buckets_count_across_a_year_boundary`, `Buckets_split_at_utc_boundaries_whatever_the_postgres_session_time_zone` (Postgres-only) |
| 23 | Deleting a guide and one of its crew members in one statement (`GuideMembers.GuideId` is CASCADE, `GuideMembers.AssistantId` is NO ACTION) | the guide's cascade removes the membership row before the NO ACTION check | `23503 ... "FK_GuideMembers_Assistants_AssistantId"` when the member's row is processed first (seen with the member inserted first): same name-order race as #19 | **Fixed**: the same deferral as #19. Latent in production: `GuidesService` deletes one assistant at a time | `Deleting_a_guide_and_its_crew_member_in_one_statement_removes_both` (both insertion orders), `Deleting_a_crew_member_whose_guide_stays_fails`, `Postgres_defers_exactly_the_no_action_checks_a_cascade_of_the_same_delete_could_outrun` |
| 24 | Deleting a notebook whose chat messages attach its files. `MessageAttachments.NotebookFileId` is NO ACTION; the notebook cascades to `NotebookFiles` (one level down) and through conversations and messages to `MessageAttachments` (three levels down) | the notebook, its files, conversations, messages and attachments are deleted | `23503 ... "FK_MessageAttachments_NotebookFiles_NotebookFileId"` in every creation order: the events a cascade queues fire after those already queued, so the files' check runs before the cascade reaches the attachments | **Fixed**: the same deferral as #19. This hits production: `NotebookService.DeleteAsync` loads and removes only the notebook, so on Postgres deleting a notebook whose chats attach its files failed (and so did `ProjectService.DeleteAsync` for such a project) | `Deleting_a_notebook_removes_message_attachments_that_reference_its_files`, `Deleting_a_notebook_file_that_a_message_still_attaches_fails`, `Postgres_defers_exactly_the_no_action_checks_a_cascade_of_the_same_delete_could_outrun` |
| 25 | When the 3 deferred NO ACTION checks of #19, #23 and #24 report a violation, and which of two concurrent transactions loses | at the violating statement; a concurrent delete of the referenced row waits for the inserting transaction (the insert's check holds a `FOR KEY SHARE` lock on that row) and then fails with 547 | at commit; the insert or update check on the referencing side is deferred too, and so is its `FOR KEY SHARE` lock | **Accepted**: <br>- **Single statement:** in autocommit (`ExecuteDelete`, a `SaveChanges` that sends one command) commit is the end of that statement, so the outcome and exception type are the same as on SQL Server (`DbUpdateException` from `SaveChanges`). <br>- **Multi-statement transaction:** the error comes from the commit, and a violation that a later statement of the same transaction repairs is accepted. EF's own multi-command `SaveChanges` counts as one: a deferred violation there can surface from its internal commit as a raw `PostgresException` rather than a `DbUpdateException` (reasoned, not run). Production paths that send several commands touching these FKs: `NotebookFileService.DeleteAsync` (folder delete), the crew recreation in `GuidesService`, and the message-with-attachments insert in `ConversationService`. <br>- **Run serially,** every transaction SQL Server accepts, Postgres accepts with the same final state, so only failing cases differ. <br>- **Under concurrency the loser can flip** (reasoned, not tested). Example: a chat send inserts a `MessageAttachment` for file F while F is deleted. On SQL Server (and on Postgres with an immediate check) the delete waits for the insert and then fails with 547/23503, so the chat send succeeds. With the deferred check the delete commits and the chat send fails at commit with 23503. Adding a crew member (`GuideMembers`) while that assistant is deleted flips the same way. Integrity holds either way: a committed state always satisfies the FK. <br>- All other FKs stay `NOT DEFERRABLE`, so their timing is unchanged. <br>- Hand-off to Task 10 (#18), done: `DatabaseErrors.IsForeignKeyViolation` also recognizes a top-level `PostgresException` (23503 from a commit), not only one inside a `DbUpdateException` (`Foreign_key_violation_is_recognized_wrapped_or_raised_by_the_commit`) | `In_a_transaction_sql_server_rejects_the_violating_delete_and_postgres_rejects_the_commit`, `In_a_transaction_postgres_accepts_a_violation_repaired_before_commit` (the concurrency flip and the EF multi-command case are not covered by a test) |
| 26 | Case-insensitive text comparison: `=`, unique indexes on string columns (emails, slugs, friendly names and others), `Contains`/`StartsWith`/`EndsWith`/`IndexOf` and `EF.Functions.Like`, with a constant, parameter or column argument | the server's default collation ignores case and keeps accents | the database's default collation is deterministic, so case matters. With the collation removed (Task 7 check): `Email = 'ann@...'` misses `Ann@...`, the unique index accepts both spellings, and `Contains("ALPHA")` finds nothing | **Fixed**: every string column uses the nondeterministic ICU collation `guideants_ci` (`und-u-ks-level2`: ignores case, keeps accents). <br>- **Translations.** Npgsql 9.0.4 sends `LIKE` for a constant or parameter argument (the parameter escaped at run time) and `strpos(...) > 0` / `left`/`right(...) = ...` for a column argument; `IndexOf` is always `strpos`. Postgres 18 runs all of them under the collation, ignoring case. <br>- **Postgres 18 is the minimum.** Postgres 17 rejects `LIKE` (`nondeterministic collations are not supported for LIKE`) and `strpos`/`replace` (`... for substring searches`) on this collation. <br>- **No query rewrite needed.** No production LINQ query passes a column as the argument of a string method (Review Focus #1 search, Task 7 report); the column-argument forms are pinned anyway | `Equality_ignores_case`, `Unique_index_ignores_case`, `Contains_and_StartsWith_ignore_case`, `Parameter_arguments_of_Contains_StartsWith_EndsWith_and_IndexOf_ignore_case`, `Member_argument_Contains_ignores_case`, `Member_argument_StartsWith_EndsWith_and_IndexOf_ignore_case`, `Equality_keeps_accents`, `Every_string_column_uses_guideants_ci` |
| 27 | Trailing spaces in `=` | pads the shorter value with spaces, so `'ann@example.com' = 'ann@example.com '` is true | compares them, so it is false (with or without the collation) | **Accepted**: stored and looked-up values don't end in spaces, because the app trims or rejects them at the boundaries. Call sites checked: <br>- `AuthEndpoints.cs:35-36` trims the name and email at registration, before the duplicate check at `:49` and the insert at `:61-62`; `:121` trims the login email before the lookup at `:126`; `UserEndpoints.cs:56-57` trims the profile name and email. <br>- Friendly names: `GuidesPublishingEndpoints.cs:142-149` and `:337-343` reject a name that `Uri.EscapeDataString` changes, so it can't hold a space, before the lookups at `:101`, `:168`, `:363` and `PublishedGuidesEndpoints.cs:120`. <br>- Slugs: `SlugGenerator.Generate` trims and keeps only ASCII letters, digits and dashes (`SlugGenerator.cs:18-46`), before the lookups at `ProjectService.cs:819` and `:833`, `NotebookService.cs:642` and `NotebookCopyService.cs:184`. <br>- What's left: a URL whose slug ends in `%20` finds the row on SQL Server and gives 404 on Postgres | `Trailing_space_comparison` |
| 28 | Sort order of punctuation and of case ties (`ORDER BY` a string column) | word sort: a hyphen has almost no weight, so `ab`, `a-b`, `m`, `-x`; case ties (`a`/`A`) come back in plan order (seen: `_x, a, A, B, b`) | ICU sorts punctuation before digits and letters, so `-x`, `a-b`, `ab`, `m`; case ties also come back in plan order (seen: `_x, A, a, b, B`) | **Accepted**: letters sort the same on both, ignoring case, and `_x` sorts first on both; only the place of a hyphen (and other word-sort punctuation such as `'`) differs. <br>- **Where it shows.** The string-ordered queries return whole lists for display, without paging: admin users (`AdminUserService.cs:133-134`), scheduled jobs (`ProjectScheduledJobService.cs:73`), assistants (`GuidesService.cs:757`) and models (`ApplicationSettingsService.Models.cs:17`, after `DisplayOrder`). <br>- **Why not match it.** ICU can ignore punctuation (`ka-shifted`), but then `ab` would equal `a-b` in `=` and in unique indexes, which SQL Server keeps apart. <br>- **Ties.** Neither database orders a case tie, so the tests pin only the case-folded order | `Ordinal_sort_of_mixed_case_names`, `Hyphen_sort_order_differs_by_provider` |
| 29 | `ILIKE` and regular expressions on a string column (`EF.Functions.ILike`, `Regex.IsMatch`, which Npgsql translates to `~`) | not translated: `ILike` is Npgsql-only and SQL Server's provider has no `Regex` translation, so the query throws | Postgres 18 rejects both on the nondeterministic collation: `0A000: nondeterministic collations are not supported for ILIKE` and `... for regular expressions` | **Accepted**: a query that runs on SQL Server can't contain them, and production code has none (no `EF.Functions` call other than `DateDiff*`; every `Regex` call works on values in memory). A future Postgres-only query that needs them can apply a deterministic collation with `EF.Functions.Collate` | `Postgres_rejects_ILike_and_regular_expressions_on_case_insensitive_columns` (Inconclusive on SQL Server) |
| 30 | Where NULLs sort (`OrderBy`/`ThenBy` and their `Descending` forms on a key that can be NULL; also the `ORDER BY` inside window functions and ordered aggregates) | NULL is the lowest value: first when ascending, last when descending | NULL is the highest value: last when ascending, first when descending | **Fixed**: on Postgres, `ApplicationDbContextOptions` replaces Npgsql's `IQuerySqlGeneratorFactory` with `SqlServerNullOrderingQuerySqlGeneratorFactory`. Its generator derives from `NpgsqlQuerySqlGenerator` and appends `NULLS FIRST` to an ascending ordering and `NULLS LAST` to a descending one. Npgsql 9.0.4's own `ReverseNullOrdering` switch does the same, but it is internal (it exists for Npgsql's tests). <br>- **Keys that can't be NULL get no clause:** a column EF knows is NOT NULL, and a constant. The results are the same, and plain indexes stay usable. Postgres 18 doesn't use a NOT NULL constraint to drop a NULLS clause: on 200,000 rows, `ORDER BY c DESC NULLS LAST LIMIT 10` on a NOT NULL `c` turned an index scan into a sequential scan plus a sort. EF marks a column read through a LEFT JOIN as nullable, so such a column still gets the clause. <br>- **Index caveat (for Task 17):** an ascending index on a nullable column stores NULLs last. It can't serve `ASC NULLS FIRST`, nor `DESC NULLS LAST` with a backward scan. The indexes that served such an order: `IX_GuideMembers_GuideId_DisplayOrder`, `IX_Models_IsActive_DisplayOrder` and `IX_Tools_Category_IsActive_DisplayOrder`. All three are on small tables. A Postgres index column declared `NULLS FIRST` would serve both directions. <br>- **What changes in production:** orderings on nullable keys now put NULLs where SQL Server does. Examples: crew members without a `DisplayOrder` (`GuidesService.cs:239`), models (`CatalogService.cs:27`), tools (`CatalogService.cs:228-229`) and global assistants (`CatalogService.cs:247`). <br>- The SQL Server SQL is unchanged (`SqlServer_order_by_is_unchanged`) | `Ascending_puts_nulls_first`, `Descending_puts_nulls_last`, `ThenBy_puts_nulls_first_and_ThenByDescending_puts_them_last`, `First_of_an_ascending_order_is_the_null_row`, `Computed_ordering_key_puts_nulls_first`, `Ordering_by_a_required_column_of_a_left_join_puts_missing_rows_first`. SQL shape (unit, no database): `Postgres_appends_nulls_first_to_ascending_and_nulls_last_to_descending_nullable_keys`, `Postgres_ThenBy_keys_each_get_their_own_clause`, `Postgres_leaves_keys_that_cannot_be_null_without_a_clause`, `Postgres_treats_a_required_column_read_through_a_left_join_as_nullable`, `Postgres_appends_the_clause_to_a_computed_key`, `Postgres_appends_the_clause_inside_a_window_function`, `Postgres_appends_the_clause_inside_an_ordered_aggregate`, `SqlServer_order_by_is_unchanged` |
| 31 | Sort order of `Guid` values (`uniqueidentifier` / `uuid`) | `uniqueidentifier` order: the last group of the string form weighs most, then the groups to its left | `uuid` sorts byte by byte, which is the order of the string form | **Accepted**: the app orders by a Guid only where any consistent order works. A search of production code covered LINQ `OrderBy`/`ThenBy` on a Guid key, raw `ORDER BY`, in-memory Guid sorts, and `CompareTo` or `>` on Guids. It found these uses: <br>- `RebuildEmbeddingsHandler.cs:50` and `:75` page `DocumentChunks` by `Id` with `Skip`/`Take`. Any total order visits each chunk once. <br>- `NotebookDockerScriptService.cs:312` and `NotebookImageService.cs:654` run `OrderByDescending(t => t.Id).FirstOrDefault()` on `ConversationTurns` filtered by `(NotebookConversationId, TurnIndex)`. A unique index allows at most one such row. <br>- `ConversationCommandService.cs:111` runs `OrderBy(a => a.MessageId).ThenBy(a => a.OrderIndex)` and then groups by `MessageId` in memory. Only the `OrderIndex` order within a message is used. <br>- EF adds key orderings when it loads a collection in a single query. They only keep each parent's rows together. <br>- No raw SQL orders by a Guid, no keyset paging compares Guids, and no code sorts Guids in memory. <br>- What's left: where a Guid breaks a tie, or is the only key, the rows come back in a different order on each provider. The order is consistent within each database | the search above, and `Guid_sort_order_differs_by_provider` (`NullOrderingBehaviorTests`), which pins the two orders |
| 32 | DateTime precision (every `DateTime`/`DateTime?` column) | `datetime2(7)` keeps 100 ns ticks, so a saved value reads back equal | `timestamptz` keeps whole microseconds. Npgsql drops the 7th digit, and for dates before 2000-01-01 (the Postgres epoch) it rounds up toward 2000, so neither the tracked value nor a 7-digit parameter equals the stored value | **Fixed**: on Postgres every DateTime is cut down to whole microseconds before it is written: <br>- the adapter gives every `DateTime`/`DateTime?` property a write converter (`PostgresModelAdapter.ToStoredDateTime`). EF applies a column's converter to every parameter or constant it compares with, or assigns to, that column or an expression built on it (an `ExecuteUpdate` value included); <br>- `EnforceUtcOnDateTimeProperties` cuts the tracked values of added and modified properties too (only when `Database.IsNpgsql()`), so the entity holds what was stored; <br>- `DateTime.MaxValue` is left whole: Npgsql writes it as `infinity` and reads `infinity` back as `MaxValue`; <br>- the message ordering guard (`ValidateMessageTimestampOrdering`, which runs before the cut) compares timestamps at stored precision, so a message saved with the same timestamp as a stored higher-sequence message is not rejected on Postgres only (the residual difference is #38). <br>- What's left: the sub-microsecond digit is lost (a value reads back up to 0.9 µs earlier); `MinValue`/`MaxValue` are stored as `-infinity`/`infinity` (#39). A copy of the value the app kept outside the entity (a local `now`) still has 7 digits, but it compares equal in a query. No production code compares a stored DateTime with a kept value in memory, other than at whole seconds (`NotebookFileHash.IsUnchanged`). DateTime is never a concurrency token (`ConversationTurn` fences on `Status`/`ExecutionId`, `JobQueue` on `ClaimToken`), and the model has no `DateTimeOffset` | `DateTime_round_trips_exactly`, `DateTime_round_trips_exactly_after_an_update`, `DateTime_before_2000_round_trips_exactly`, `DateTime_min_and_max_values_round_trip`, `Message_ordering_check_compares_timestamps_at_stored_precision`, `Message_ordering_check_still_rejects_a_later_timestamp`; unit: `Date_times_are_written_as_utc_whole_microseconds`, `Truncate_to_microseconds_floors_and_keeps_the_kind` |
| 33 | DateTime parameters, constants and `ExecuteUpdate` values whose `Kind` is `Local` or `Unspecified` (Review Focus #4) | compares and stores the raw ticks whatever the `Kind` | Npgsql refuses them for `timestamptz`: `ArgumentException: Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone', only UTC is supported` (parameters, `ExecuteUpdate` values), and `'timestamp with time zone' literal cannot be generated for Unspecified DateTime` (constants) | **Fixed**: the write converter of #32 marks every value UTC with `DateTime.SpecifyKind`, never `ToUniversalTime`, so the raw ticks are compared and stored, as on SQL Server, whatever the machine's time zone. EF applies it to a parameter or constant when it is compared with, or assigned to, a DateTime column or an expression built on one; a DateTime that meets no column gets no converter. The tests can tell raw ticks from `ToUniversalTime` only on a host whose time zone is not UTC (they were run on one, US Eastern); on a UTC host such as CI's Linux runners they prove only that a non-UTC `Kind` doesn't throw. The SaveChanges guard still rejects a `Local` value on a tracked entity on both providers. Raw SQL parameters (`ExecuteSqlRaw`) don't pass through converters: the only one with DateTimes is the job claim (`JobQueueClaimSql`, Task 10), which passes `DateTime.UtcNow` values | `Unspecified_and_Local_query_parameters_compare_raw_ticks`, `Unspecified_and_Local_query_constants_compare_raw_ticks`, `Unspecified_and_Local_ExecuteUpdate_values_store_raw_ticks` |
| 34 | `Kind` of a DateTime read from the database | `Unspecified` (`datetime2` has no offset) | `Utc` | **Accepted**: the app treats `Unspecified` as UTC wherever the `Kind` matters: `UtcDateTimeJsonConverter` writes both with a `Z`; `NotebookFileHash.ToUtcSecondTicks` and `ProjectScheduledJobService` mark `Unspecified` as UTC; the SaveChanges guard marks `Unspecified` as UTC. No production code calls `ToLocalTime`/`ToUniversalTime` on a value read from the database, and `DateTime` equality ignores the `Kind` | `DateTime_round_trips_exactly` (asserts `Utc` on Postgres, `Unspecified` on SQL Server) |
| 35 | Text containing U+0000 (NUL), for example extracted PDF text or model output (Review Focus #3) | `nvarchar` stores it | `text`/`varchar` reject it: `22021: invalid byte sequence for encoding "UTF8": 0x00`, so the save fails | **Accepted**: stored as U+FFFD (the Unicode replacement character). The adapter gives every string property without a converter a write converter (`PostgresModelAdapter.ReplaceNulCharacters`); reads are unchanged. <br>- The converter keeps the string mapping, so collation `guideants_ci`, lengths, keys and indexes are as before; a value without NUL is written as the same string instance. <br>- Query parameters get the column's converter too, so a lookup with the same NUL-containing value still finds the row, ignoring case. <br>- What's left: a value read back has U+FFFD where SQL Server has U+0000, and two values that differ only in U+0000 vs U+FFFD are equal on Postgres. Raw SQL parameters don't pass through converters; the only one that carries user text, the hybrid search query, replaces U+0000 itself (#47) | `Nul_characters_save`, `Nul_characters_in_a_unique_indexed_column_and_a_parameter`, `Text_without_nul_characters_is_stored_unchanged`; unit: `Strings_are_written_with_nul_as_the_replacement_character` |
| 36 | A string longer than its column (`Users.Email`, bounded at 450 by #1; `Users.Name`, `[StringLength(255)]`) | `DbUpdateException` from `2628: String or binary data would be truncated` | the same: `DbUpdateException` from `22001: value too long for type character varying(450)` | **Same**: nothing truncates the value on the way in. EF's Npgsql mapping leaves `DbParameter.Size` at 0 for strings (measured: `Size=0`, `NpgsqlDbType=Varchar`). Npgsql *would* truncate if `Size` were set (measured: a `text` parameter `'abcdef'` with `Size = 3` arrives as `'abc'`), so hand-written `NpgsqlParameter`s (Task 12) must not set `Size` | `String_longer_than_its_column_is_rejected`, `String_of_exactly_its_column_length_is_stored_whole` |
| 37 | Optimistic concurrency with a `[Timestamp]` row version (`ApplicationSettings`, `JobQueue`, `LocalModelInstallations`, `LocalModelOperations`, `ProjectScheduledJobs`), including the settings API's base64 `RowVersion` (Review Focus #5) | the insert returns the token, every update changes it, and a stale token makes EF raise `DbUpdateConcurrencyException`; the settings API answers 409 | no `rowversion` type | **Fixed**: the adapter maps the row version to `xmin` (#14). Proven at runtime: the insert returns a 4-byte token (`RETURNING xmin`); every update changes it; a stale token, an 8-byte token (it converts to `xmin` 0, which no row has) and an unrelated 4-byte token all raise `DbUpdateConcurrencyException`; a settings save with the first response's `RowVersion` returns 409 the second time. That 409 comes from the service's ordinal comparison of the base64 strings before it saves (`ApplicationSettingsService.Sections.cs:89-90`); its other 409 path, a `DbUpdateConcurrencyException` from a concurrent save between that check and `SaveChanges` (`:142`), is covered only indirectly, by the database-level tests of the exception | `Concurrent_update_throws`, `RowVersion_changes_on_every_update`, `Stale_or_foreign_row_version_conflicts`, `RowVersion_changes_on_every_save_inside_a_transaction`, `Settings_endpoint_rejects_stale_row_version`; unit: `Row_versions_map_to_xmin`, `Row_version_converter_round_trips_four_bytes` |
| 38 | Message ordering guard (`ValidateMessageTimestampOrdering`): a lower-sequence message less than 1 µs later than a stored higher-sequence message of the same turn | rejected with `InvalidOperationException` ("Message timestamps must respect sequence order within a turn") | without the #32 change the guard compared a 7-digit new value with a 6-digit stored one, and also rejected a message with exactly the stored timestamp | **Accepted**: on Postgres the guard compares at stored precision, where the two timestamps are equal, so it accepts the message. Once stored, the two rows tie on `Created` and keep their order by `MessageSequence`, which is what the guard protects. A later timestamp by 1 µs or more is still rejected on both | `Message_ordering_check_accepts_a_sub_microsecond_inversion_on_postgres_only`, `Message_ordering_check_compares_timestamps_at_stored_precision`, `Message_ordering_check_still_rejects_a_later_timestamp` |
| 39 | `DateTime.MinValue` / `DateTime.MaxValue` in the database | stored as the dates `0001-01-01T00:00:00` / `9999-12-31T23:59:59.9999999` | Npgsql stores them as `-infinity` / `infinity` (its default; this task keeps `MaxValue` whole so that still happens) | **Accepted**: they round-trip through EF and compare equal in LINQ on both providers. SQL written by hand differs: date arithmetic or `date_trunc` on `-infinity` gives `-infinity` or an error, and a `psql` reader sees `-infinity`. No production code writes either value on purpose (`MinValue`/`MaxValue` only appear in in-memory fields); an unset non-nullable DateTime would be `MinValue` | `DateTime_min_and_max_values_round_trip` (reads the stored text: `infinity -infinity` on Postgres, the two dates on SQL Server) |
| 40 | Column default `GETUTCDATE()` (56 table columns: every `Created`, and the `CreatedUtc`/`UpdatedUtc`/`LastUpdated`/`Updated` columns that declare it) | evaluated per statement: two rows inserted by two statements of one transaction get different times (at `datetime` precision, about 3 ms, stored in `datetime2(7)`) | Task 3 mapped it to `now()`, which is the transaction's start time: both rows got the same time | **Fixed**: the adapter maps `GETUTCDATE()` to `statement_timestamp()`, the start of the current statement, and the Postgres baseline's 56 table-column defaults and its Designer/snapshot were edited in place to match (the 57th property is on the `ConversationCurrentState` view, which has no column default). `has-pending-model-changes` reports no changes for either provider. What's left: Postgres keeps microseconds where `GETUTCDATE()` has `datetime` precision. The seed rows of the baseline still use `now()`, each in one statement, where the two are equal. A database already migrated with the earlier baseline keeps `now()`: the baseline hasn't been released, so it was edited in place rather than followed by a second migration | `Column_defaults_are_evaluated_per_statement`; unit: `No_sqlserver_default_sql_remains` |
| 41 | `SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED` before read-only queries (`ConversationQueryService` conversation and list reads, `ProjectService` notebook activity, which then resets `READ COMMITTED`) | readers don't wait for the locks of retention cleanup, at the cost of dirty reads | outside a transaction block the statement only raises a warning (`SET TRANSACTION can only be used in transaction blocks`); Postgres has no dirty reads (`READ UNCOMMITTED` behaves as `READ COMMITTED`) | **Accepted**: the statements now run only when `Database.IsSqlServer()` (it was `IsRelational()` in `ConversationQueryService` and unconditional in `ProjectService`), so SQL Server sends the same statements and Postgres and InMemory send none. A Postgres reader never waits for a writer (MVCC), which is what the hint buys on SQL Server, and never sees uncommitted rows | `GetConversationWithMessages_Projects_turn_files_thinking_tool_calls_and_filters_duplicates`, `GetList_Orders_by_usage_event_last_activity` (`ConversationServiceIntegrationTests`), `ProjectEndpointsTests.GetProjectDetails_ReturnsFullProjectDetails`; on InMemory (unit): `GetConversationWithMessagesAsync_Filters_duplicate_assistant_rows_without_tool_calls`, `GetConversationWithMessagesAsync_Uses_last_assistant_message_when_turns_do_not_exist` (`PublishedConversationServiceTests`, which read through `ConversationQueryService`). No InMemory test reaches the `ProjectService` statement |
| 42 | A serializable transaction that waited for the #17 lock (Review Focus #2: two registrations at once on an empty `Users` table; two admins demoting each other) | the second caller's reads take range locks after it gets the lock, so it sees the first caller's commit: one Admin and one Pending user; the second demotion hits the last-admin guard | the second caller's snapshot was taken by the lock statement, before the first commit, so Postgres aborts it with `40001: could not serialize access due to read/write dependencies among transactions` at its insert or update. Before the fix the registration caught that `DbUpdateException` and answered 409 "Registration could not be completed." | **Fixed**: <br>- **Retry:** both delegates rethrow the serialization failure to `CreateExecutionStrategy().ExecuteAsync`. Npgsql's `EnableRetryOnFailure` treats 40001 as transient (`PostgresException.IsTransient`) and reruns the whole delegate, which then sees the first commit. The registration rethrows a 40001 (`DatabaseErrors.IsSerializationFailure`: SQLSTATE 40001, bare or in a `DbUpdateException`), so its `catch (DbUpdateException)` that answers 409 no longer takes one. The admin delegate rethrows every exception on Postgres (`catch (Exception) when (_db.Database.IsNpgsql())`, #51), so 40001 is one case of it. <br>- **Clean change tracker:** each delegate clears the change tracker before rethrowing (the registration for a 40001, the admin delegate for any exception, #51). Without it the registration retry also inserts the user of the failed attempt (23505, so 409), and the admin retry reads back its own already-demoted role row (identity resolution) and removes the last admin. <br>- **SQL Server is unchanged:** it never reports 40001, and the admin filter checks for Npgsql, so neither filter matches there. <br>- **Limit (inferred, not run):** after 5 retries the strategy gives up and the request fails (500); that takes 6 consecutive losses. A retry takes its snapshot when it starts waiting, so it loses again whenever another caller holds the lock at that moment. A burst of more than about 6 concurrent conflicting registrations or admin mutations can make the last caller fail with 500 where SQL Server would only make it wait. The tests run 2 at a time. <br>- **Stale read on a waiter that doesn't write (inferred, not run):** a waiter whose guard fails on its old snapshot returns that 4xx without writing, so Postgres raises no 40001 and nothing is retried. Example: one admin approves a pending user while another changes the same user's role. On SQL Server the role change waits, sees the approval and succeeds; on Postgres it can answer "Pending users must be approved before changing role." (InvalidState). Retrying the request succeeds. <br>- **Other transient errors:** #51 | `Concurrent_registrations_both_succeed`, `Concurrent_admin_demotions_keep_one_admin`, `AuthEndpointsTests.ConcurrentInitialRegistrations_CreateExactlyOneAdmin`, `Serialization_failure_is_recognized_wrapped_or_top_level` |
| 43 | Postgres server version | n/a | Postgres 17 migrates the baseline, then every `LIKE`/`strpos` query fails at run time under the `guideants_ci` collation (#26) | **Fixed** (fail fast): `PostgresDatabaseInitializer` reads `SHOW server_version_num` right after connecting and, below `180000`, throws `PostgreSQL 18 or later is required (found <version>).` before it creates or migrates anything. Without the guard a Postgres 17 server migrated without error (bite-checked) | `Rejects_a_Postgres_17_server_before_creating_the_database` (a real `pgvector/pgvector:0.8.7-pg17` server), `Accepts_Postgres_18_or_later`, `Rejects_Postgres_before_18`, `Rejects_an_unreadable_version_number` |
| 44 | Full-text tokenization of `C#`, `C++`, `F#` and `.NET` (hybrid search, `IHybridSearcher`) | the English word breaker keeps `c#`, `c++` and `.net` as tokens, so the phrase `C#` matches only "C#", not "Plan C". `F#` is split into `f`, a noise word, so an `F#` query matches no text | the default parser drops `#`, `+` and the leading dot: `C#` and `C++` become the letter `c` (in the demo corpus `C#` matched 120 chunks against SQL Server's 10, and in scope P2 the top 5 were permission-table headers `\| A \| C \| R \|`), `.NET` becomes `net` | **Fixed**, on Postgres only (SQL Server's index and query are unchanged): migration `AddDocumentChunkFullText` adds the `IMMUTABLE` SQL function `guideants_fts_normalize`. It rewrites the standalone terms (any case, with no letter, digit or underscore directly before or after) to `csharp`, `fsharp`, `cplusplus` and `dotnet`. It is applied in the generated `"ContentTsv"` column and to the query (`phraseto_tsquery('guideants_english_nostem', guideants_fts_normalize(@query))`). It uses only built-in `regexp_replace`, no custom parser or C extension, so it runs on managed Postgres. <br>- The regular expressions run under `COLLATE "C"`: `Content` uses `guideants_ci`, which Postgres rejects for regular expressions (#29). <br>- `F#` now matches on Postgres, which SQL Server can't do. <br>- Not rewritten inside a longer token, on purpose: `ASP.NET`, `VB.NET`, `xc#`, `C#8`, `c++17`. Both providers keep `asp.net` as one token, so `.NET` doesn't find "ASP.NET" on either | `Csharp_query_finds_csharp_chunks_not_bare_c`, `Cplusplus_and_dotnet_queries_match_their_chunks`, `Fsharp_query_matches_fsharp_chunks_on_postgres_only`, `Normalization_rewrites_only_standalone_terms` (`HybridSearchBehaviorTests`). Production comparison ([`RESULTS-production.md`](../samples/hybrid-search-parity/RESULTS-production.md)): for Q33 `C#`, the top 5 and #1 equal SQL Server's in all four scopes. Postgres also matches the 34 chunks whose only C# is a ```` ```csharp ```` code fence (the same lexeme), reviewed `acceptable` (`REVIEW-NOTES.md` D). No saved query uses `F#`, `C++` or `.NET` |
| 45 | Other punctuation and short words in full-text search | the word breaker's tokens: `it's` is one token; `ASP.NET` is indexed as `asp.net`, `asp` and `net`; single letters and numbers (`c`, `8`) are noise words | the default parser's tokens: `it's` is `it` and `s`, both stop words, so a query `it's` matches no text; `ASP.NET` is only the host token `asp.net`, so `net` doesn't find it; `wsl/docker`, `services/api.ts` and `/cli` are single path tokens; single letters and numbers are kept, so `a & b` searches for `b` | **Accepted** (functional): the demo found 22 such differences and recorded them all as acceptable (mean top-5 overlap 0.955, same #1 in 92.1% of pairs). The text weight (0.3) bounds their effect on the ranking, and the vector half is the same on both. They are tokenization differences. Most are SQL Server matching the phrase inside identifiers, file names and paths, which Postgres keeps as one token. But two are real Postgres recall misses, accepted as low impact: Q09, where one prose occurrence ("GuideAnts guide/internal tools") is missed because Postgres lexes `guide/internal` as one token, and Q25, where a URL-prefix search finds nothing because Postgres keeps the longer URL as one token. The phrase search, the stop words (`guideants_english_nostem`: English stop words, no stemming, close to SQL Server's system stoplist) and the scope filters behave alike | `Phrase_search_requires_the_words_together`, `Normalization_rewrites_only_standalone_terms`, `Queries_with_operators_punctuation_or_only_stop_words_still_return_vector_matches`. Production comparison ([`RESULTS-production.md`](../samples/hybrid-search-parity/RESULTS-production.md)): the port's 22 differences recur with the same chunk IDs, all `acceptable` |
| 46 | A search query made of full-text operators, punctuation or stop words only (`'`, `&`, `\|`, `!`, `:`, `(`, `)`, `\`, `"`, `<->`, `foo:* & (bar \| baz)`, `the`, an SQL-injection attempt), or blank | the text is wrapped in a `CONTAINSTABLE` phrase with `"` doubled, and the SQL literal escapes `'`. No input throws: a term-less phrase reports `Informational: The full-text search condition contained noise word(s)` and matches no text, and the vector half still returns its matches. Blank or whitespace queries return nothing (early return) | `phraseto_tsquery` has no operator syntax: it parses the text like a document, so operators are punctuation. A term-less query raises a NOTICE and matches no text | **Same**: every one of these inputs returns the vector match with text score 0 on both providers, with no error logged. Blank queries return nothing on both (same early return) | `Queries_with_operators_punctuation_or_only_stop_words_still_return_vector_matches`, `Blank_query_returns_no_results` |
| 47 | A search query containing U+0000 (NUL) | searched like any other character; the vector half returns its matches | the `text` parameter is rejected (`22021`), the searcher catches the error and returns no results at all | **Fixed**: the Postgres searcher replaces U+0000 with U+FFFD in the query, as the adapter does for stored text (#35) | `Query_with_a_nul_character_still_returns_vector_matches`; unit: `Postgres_command_replaces_nul_in_the_query_like_stored_text` |
| 48 | A chunk whose embedding is all zeros (`RebuildEmbeddingsHandler` zeroes every embedding before it regenerates them; a failed regeneration leaves them so) | `vector_distance('cosine', ...)` is 1: vector score 0, so the chunk is returned only on a text match, ranked by its text score | pgvector's `<=>` is NaN, so the vector score and the score are NaN and sort above every real score | **Fixed**: the Postgres query reads a NaN distance as 1. Without this guard the failure was worse than a bad ranking: a zero-embedding chunk with a text match sorted first with a NaN score, and `MemoryTools` serializes the results with `System.Text.Json`'s default options, which throw on NaN (`ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON`). So the search tool call failed | `Zero_embedding_counts_as_unrelated` |
| 49 | Search scores (`HybridSearchResult.TextScore` and `Score`) | text score `RANK / 1000` from `CONTAINSTABLE` | text score `ts_rank_cd(..., 32)`, which is `rank / (rank + 1)` | **Accepted** (re-evaluated with the production comparison, Task 13; not rescaled). <br>- Measured over the 376 (query, chunk) pairs that both production searchers match: Postgres / SQL Server text score has median 5.68 (p25 4.78, p75 6.49). On the short edge-case chunks it is reversed: SQL Server 0.176, Postgres 0.03 to 0.09, because `CONTAINSTABLE` ranks a short document higher. So no constant divisor maps one scale onto the other. <br>- With the 0.7/0.3 weights, a text match adds about 0.03 to 0.11 to the score on Postgres, against about 0.005 to 0.02 on SQL Server. So a text match moves a chunk up the list more on Postgres: the effective balance shifts toward text. <br>- Effect, from the production comparison: mean top-5 overlap 0.962 and the same #1 in 93.4% of pairs (thresholds 0.9 and 80%). Dividing the Postgres text score by 5.68 would give 0.995 and 99.3%. But in 8 of the 10 pairs whose #1 differs, Postgres's #1 contains the searched phrase and SQL Server's doesn't (in the other 2 both do). These are concentrated in the exact-phrase queries, whose same-#1 rate is 80.0%, at the threshold. Postgres finds an exact-phrase chunk in the top 5 in 45 of 51 query/scope pairs, against 41 for SQL Server and 41 for the rescaled Postgres. So the shift doesn't hurt a user looking for the phrase, and the score is kept. A rescale would be one line in `PostgresHybridSearcher` (plus the parity diagnostics) if matching SQL Server's order is ever preferred over exact-phrase recall. <br>- The same on both: what matches, the candidate limit (`maxResults * 2`, taken across the whole table before the scope filter, as on SQL Server), the weights, the score formula, the `distance < 1` filter and the vector score (`1 - cosine distance`). <br>- No code branches on the values: `MemoryTools` merges adjacent chunks and orders the merged results by score. But its tool result is the serialized `HybridSearchResult` list (`JsonSerializer.Serialize(consolidatedResults)`), so the model sees `Score`, `VectorScore` and `TextScore` | `Vector_only_match_is_returned_when_no_text_matches` (same vector score and score on both); unit: `Postgres_query_searches_the_normalized_phrase_with_the_guideants_configuration`, `Postgres_command_passes_every_value_as_a_parameter_without_a_size`; scale evidence: [`RESULTS-production.md`](../samples/hybrid-search-parity/RESULTS-production.md), `samples/hybrid-search-parity/REVIEW-NOTES.md` E, and the committed tool and output [`samples/hybrid-search-parity/tools/scale-measure`](../samples/hybrid-search-parity/tools/scale-measure/README.md) (ratio distribution, rescale counterfactual, per-group metrics, the 10 differing #1s) |
| 50 | The full-text index on `DocumentChunks(Content)` in schema parity | created by raw SQL in the baseline (`DocumentChunks_Catalog`, key `PK_DocumentChunks`). It lives in the full-text catalog, outside `sys.columns` and `sys.indexes`, so the schema snapshot doesn't see it | the counterpart is a generated `tsvector` column `"ContentTsv"` and the GIN index `IX_DocumentChunks_ContentTsv`, neither in the model (`has-pending-model-changes` reports no changes for either provider). No vector index on either provider: both searches scan the scoped chunks | **Accepted**: the snapshot comparisons (`Same_columns`, `Same_indexes`) leave out the documented Postgres-only column and index, and assert they exist. A separate test checks both sides directly: the SQL Server full-text index and its catalog, and the Postgres generated expression, GIN index and `IMMUTABLE` function. The migration's `Down` removes everything and `Up` reapplies | `Postgres_full_text_column_stands_in_for_the_sql_server_full_text_index`, `Postgres_full_text_migration_rolls_back_and_reapplies`, `Same_columns`, `Same_indexes` |
| 51 | A transient error other than 40001 in the admin user mutations (`AdminUserService`) and in registration: a deadlock (40P01), a lock timeout (55P03) or a broken connection (08xxx), at `SaveChanges` or at the commit | the retrying strategy reruns the delegate after a transient `SqlException` (a deadlock 1205, for example) with the failed attempt's entities still tracked. This is unchanged and was already so before the Postgres work | after Task 10 the same: only 40001 cleared the tracker. After a failed commit, the retry of an admin mutation read back its own changed rows (identity resolution), found nothing to save and reported success with the old values still in the database (reproduced: `ChangeRoleAsync` returned Reader, the row stayed Admin) | **Accepted** (fixed on Postgres only): <br>- **Admin mutations:** on Postgres the delegate clears the change tracker before it rethrows any exception (`catch when (_db.Database.IsNpgsql())`), so every retry reads the committed rows. A non-transient error isn't retried and still fails the request. <br>- **Registration:** not affected, so unchanged. It reads only with `AsNoTracking` and adds new entities in each attempt; the failed attempt's entities were accepted by its successful `SaveChanges` and are never saved again. A transient error at `SaveChanges` is caught as a `DbUpdateException` and answers 409, on both providers, as before. <br>- **Which errors are retried:** Npgsql's strategy retries a `PostgresException` whose `IsTransient` is true (40001, 40P01, 55P03, 55006, 55000, the 08 connection codes including 08007 `TransactionResolutionUnknown`, 53xxx, 57P01/57P02/57P03/57P05, 58000/58030), an `NpgsqlException` caused by an I/O, socket or timeout error, and a `TimeoutException`. The admin catch clears the tracker for every exception, so it covers all of them. <br>- **Commit outcome unknown (same on both providers):** a connection lost during the commit can hide a commit that succeeded, and EF doesn't verify it before retrying. The retry then runs on the committed change: an admin mutation runs again on the already-changed rows (an approval or a role change sets the same values again), and a registration answers 409 "An account with this email already exists." for the account it just created. <br>- **SQL Server:** unchanged, so the dirty retry of the admin mutations after a transient error at commit remains possible there (out of scope: no SQL Server behavior changes) | `Admin_mutation_retried_after_a_transient_commit_failure_persists_on_postgres` (fails without the fix), `Registration_retried_after_a_transient_commit_failure_creates_one_user_on_postgres` (both Postgres-only, Inconclusive on SQL Server; they inject a transient 40P01 into the first commit), `Concurrent_admin_demotions_keep_one_admin` |
| 52 | Settings bootstrap table check (`ApplicationSettingsService.BootstrapAsync`, `ApplicationSettingsTableExistsAsync`) | `SELECT CASE WHEN OBJECT_ID('dbo.ApplicationSettings', 'U') ...`; if the table is missing, the bootstrap seeds nothing | the check runs only when `IsSqlServer()`; every other provider is assumed to have the table, so on Postgres a missing table would fail the bootstrap's first query (42P01) instead of skipping it | **Accepted**: the table can't be missing when the bootstrap runs. Program migrates the database first, and the DB-backed settings loader, which Program adds before `Build()` and so runs before `BootstrapAsync`, already fails host startup loudly when the table is missing (#16). Found by the Task 14 search of production code; the SQL Server statement is unchanged | `Settings_loader_fails_loudly_when_the_application_settings_table_is_missing`, and every Postgres host start runs the bootstrap (for example `Sections_Schema_Readiness_ReturnOk`, `Models_FullCrudLifecycle`) |
| 53 | Two cancel tests of `ConversationServiceIntegrationTests` fail now and then with "Sequence contains more than one element": there are two or more assistant rows after the cancel | `SendMessageStream_Cancel_finalizes_partial_message_marks_turn_cancelled_and_prunes_incomplete_tool_calls` failed in 3 of 4 full runs in Task 14 (always its prune half), and in the SQL Server runs of Tasks 6b, 8, 9, 10 and 11; `SendMessageStream_Cancel_during_thinking_persists_thinking_without_zero_token_usage` in 0 of 4, and never in the SQL Server runs of Tasks 6b to 14 | the same failure: the first test in 3 of 8 full runs in Task 14 (always its prune half); the second in 3 of 8 (both in 2 of them), and also in the Postgres runs of Tasks 8 and 10 | **Same**: known flaky tests, the same mechanism on both providers. The race is in the tests' timing against the engine; no SQL decides it. A dump of the conversation's rows at the failing assertion (temporary instrumentation, not committed) shows: <br>- **Prune half of the first test:** the fake provider's `ToolCallsCancelBeforeExecution` scenario cancels only the HTTP request, and an SSE disconnect doesn't stop the worker (by design, `SendMessageStream_SseDisconnect_keeps_worker_locked_until_explicit_cancel_and_reattach_observe`). The provider returns the same tool call on every round, so the worker keeps doing tool rounds until the test's cancel call lands. The test assumes it lands before the second round. Seen: 5 assistant rows in 20 ms on Postgres, 2 on SQL Server (each with the tool call, plus one tool row "ERROR: IntegrationTestTool is not a valid tool."). <br>- **The thinking test:** the fake streams 10 thinking words 10 ms apart, then the final text, and returns. The test disconnects at 120 ms and then asks the run registry to cancel. When the provider returns first, the final message is saved (row 2, "should not be persisted"), and then the cancellation wins in the engine's "provider returned after cancellation" path (`ConversationStreamEngine.cs:630`). `HandleCancellationAsync` gets the id of the *active* segment, which is null once the final message was saved, so it saves the run's last assistant message again as a new row (row 13, same text). In that timing the test's content check fails too. <br>- **The thinking test, observed on Postgres only so far:** same mechanism; the likely reason is speed (Postgres full runs take about 1 m 35 s, SQL Server's emulated image about 3 m 24 s). The 120 ms count from when the request is sent, so they include the request's setup (lock, turn and user message writes); slower setup leaves less of the 120 ms for the stream, so the cancel lands while the provider is still thinking. Not measured. Nothing in the race reads the database. <br>- **Engine defect behind the duplicate (both providers, latent, not fixed):** a cancellation that wins after the provider returned normally saves the final assistant message twice. No production path reaches it today. The worker's token is cancelled only by `ConversationStreamRunRegistry.RequestHardStop`, which the Stop endpoint uses and which makes `HandleCancellationAsync` skip this write (`IsHardStopRequested`), or by `RequestCancel`. `RequestCancel`'s only production caller is `ConversationTurnRecoveryService.RequestPendingCancellationsAsync` (`:82`), which delivers a `cancel_requested` marker on a turn still `streaming`. Only `ConversationPersistence.RequestTurnCancellationAsync` leaves that state, and it has no production caller; the Stop fence sets the turn `cancelled`. The test reaches it by calling `RequestCancel` directly. Not fixed here: it is provider-neutral stop logic outside the Postgres switch, and fixing it wouldn't make these tests deterministic | `SendMessageStream_Cancel_finalizes_partial_message_marks_turn_cancelled_and_prunes_incomplete_tool_calls`, `SendMessageStream_Cancel_during_thinking_persists_thinking_without_zero_token_usage`; frequencies and reruns: the known flaky tests under [Test results per provider](#test-results-per-provider) |
| 54 | `RecoverStaleTurns_terminalizes_orphaned_streaming_turn_on_sql` fails now and then with "Expected assistant.IsStreaming to be False, but found True" (its turn is already `interrupted`) | failed once, in the Task 7 SQL Server run; passed in the 4 SQL Server full runs of Task 14 | passed in the 8 Postgres full runs of Task 14 and in 3 reruns of its class | **Same** (the same mechanism on both providers; observed once, on SQL Server, never on Postgres so far): the race is between the test and the hosted `ConversationTurnRecoveryService`, which sweeps every second in every test host. The sweep claims a stale turn in one statement (`TryClaimStaleTurnAsync` sets `interrupted`) and clears the assistant's `IsStreaming` in a later `SaveChanges` (`ConversationTurnRecoveryService.cs:172-188`). When the hosted sweep claims the seeded turn first, the test's own sweep skips it and the test can read the message before the hosted sweep's second write. Not database-specific: the claim is a conditional update on both providers, and the window is the gap between two writes | `RecoverStaleTurns_terminalizes_orphaned_streaming_turn_on_sql`, `TryClaimStaleTurnAsync_only_one_concurrent_claim_succeeds` |
| 55 | Stop's fence and a worker's assistant row written at the same moment (`ConversationPersistence.FenceTurnCancellationAsync`, `READ COMMITTED`: it reads the turn's streaming assistant rows, clears their `IsStreaming`, then updates the turn; the worker inserts a row and touches the turn in one transaction, fenced on `Status`/`ExecutionId`) | with `READ_COMMITTED_SNAPSHOT` on, the same race as Postgres below. <br>- **Where it is on:** the suite's SQL Server database has it on (`is_read_committed_snapshot_on = 1`) because EF Core created that database. EF Core's SQL Server provider turns it on whenever it creates a database (`Migrate()`, `dotnet ef database update`): `Microsoft.EntityFrameworkCore.SqlServer` 9.0.11 sends `ALTER DATABASE ... SET READ_COMMITTED_SNAPSHOT ON`. Azure SQL Database also has it on by default. <br>- **The test:** there the test below failed the same way before it was made Postgres-only, with the fence waiting with `LCK_M_U` on the turn row in its `SaveChanges`. <br>- **Where it is off:** default Docker and launcher installs create their database through `SqlServerDatabaseInitializer`, a plain `CREATE DATABASE`, which leaves it off. The speed harness's database, created that way on the same `mssql2025-express-fts` image, reports `READ_COMMITTED_SNAPSHOT OFF`. With it off, the fence's first read of the turn waits for the worker's uncommitted touch, so it sees the row. A worker write that falls entirely between the fence's read and its turn update could still escape. Both are reasoned, not run. <br>- **This fix** doesn't change SQL Server. Whether to fix SQL Server too is an open decision ([Decisions needed from you](#decisions-needed-from-you), item 1). (Earlier notes said the image turns the setting on; it is EF Core) | before the fix: the reads don't wait for the worker (MVCC); only the turn update in `SaveChanges` waits for the worker's row lock, and its predicates still match (the worker changes only `LastUpdated`). So a row the worker committed meanwhile stayed `IsStreaming = true` on a cancelled turn. Seen once in a full run (an empty streaming row after a prune-half failure of #53), and reproduced every time by the test below | **Fixed** on Postgres only (SQL Server keeps its race, as in #51): after `SaveChanges`, inside `if (shouldAdvanceFence && db.Database.IsNpgsql())`, the fence runs one `ExecuteUpdate` that clears `IsStreaming` on the turn's assistant rows, before the commit and while it holds the turn row. That statement reads a fresh snapshot, so it sees a row the worker committed while the fence waited; any later worker write fails its `Status`/`ExecutionId` predicates on the fenced turn and rolls back its insert. SQL Server sends the same statements as before. <br>- **Impact before the fix (reasoned, not verified end to end):** the stray row stayed for good, because recovery only sweeps turns still `streaming`. The message list (`ConversationQueryService.cs:79`, `ConversationMessageMapper.cs:131`) and the live preview (which needs a `streaming` turn) hide it. But the next turn's history loads every message (`ConversationService.cs:801-812`) and `ConversationHistoryBuilder` turns the empty row into an assistant `ChatMessage` with no content (`:676-677`), which providers such as Anthropic may reject. The same applies to SQL Server with `READ_COMMITTED_SNAPSHOT` on, which keeps the race (decision 1) | `Stop_fence_clears_a_streaming_row_the_worker_commits_while_the_fence_waits` (`StopFenceBehaviorTests`, Postgres-only, Inconclusive on SQL Server; it holds the worker's transaction open, waits until the database reports the fence blocked by the worker's session, then commits the worker; it fails without the fix) |
| 56 | Constraint violations recognized by finding the constraint's name in an exception message, with no provider type: `NotebookFileReconciler.IsDuplicateNotebookFileConstraint` (`IX_NotebookFiles_RelativePath_NotebookId`, `IX_NotebookFiles_DocumentId`: two syncs register the same file), the 409 `NotebookFile.DeleteReferenced` of the notebook file DELETE endpoint (`FK_ContentFileVersions_NotebookFiles_OriginNotebookFileId`, `NotebookEndpoints.cs:353`) and `NotebookFileSyncService.IsCorrelationDedupViolation` (`IX_JobQueue_CorrelationDedup`: a reconcile job already queued) | the 2601 and 547 messages name the index or the foreign key | `PostgresException.Message` is `<SQLSTATE>: <message text>`, and the 23505 and 23503 texts name the constraint in quotes | **Same**: each check matches on both providers. The names are under 63 bytes, so #8 doesn't shorten them; a future index or foreign key whose name is 64 bytes or longer would be shortened on Postgres, and a check like these would stop matching there. The foreign key isn't one of the deferred three (#25), so its violation still comes from the delete statement, inside a `DbUpdateException`. Found by the Task 14 search of production code. In 1 of 8 Postgres full runs the log shows a 23505 on `IX_NotebookFiles_DocumentId` from a background sync racing another sync, with no test failing | `Duplicate_notebook_file_registration_is_recognized_by_its_unique_index_name`, `Deleting_a_notebook_file_a_project_version_came_from_is_recognized_by_its_foreign_key_name`, `Duplicate_pending_job_is_recognized_by_its_unique_index_name` (`ConstraintMessageBehaviorTests`; they call `NotebookFileReconciler.IsDuplicateNotebookFileConstraint` and `NotebookFileSyncService.IsCorrelationDedupViolation`, now `internal`, and a copy of the endpoint's inline filter) |

**Why only three foreign keys are deferred (#19, #23 to #25).**
- **How the set is chosen:** it comes from the cascade graph, not from a list of known cases.
  `Postgres_defers_exactly_the_no_action_checks_a_cascade_of_the_same_delete_could_outrun` computes it from the SQL
  Server catalog. A NO ACTION foreign key needs deferral when some delete reaches its referencing table at a deeper
  cascade level than its referenced table.
- **Three more NO ACTION foreign keys** have referencing rows that a cascade of the same delete removes, but at the
  same level as the referenced rows. Postgres removes those before the check, as SQL Server does, so they need no
  deferral:
  - `NotebookConversationMessages` to `ConversationTurns` (deleting a conversation)
  - `AgentInvocations.ParentInvocationId` (deleting a conversation with nested invocations)
  - `ContentFileVersions.OriginVersionId` (deleting content files whose versions reference each other)
- **They stay `NOT DEFERRABLE`,** so writes to those busy tables keep the check at statement time. The proving tests
  are `Deleting_a_conversation_removes_its_turns_and_the_messages_that_reference_them`,
  `Deleting_a_conversation_removes_agent_invocations_that_reference_each_other` and
  `Deleting_content_files_whose_versions_reference_each_other_in_one_statement_removes_them`.
- **For future schema changes:** the test fails when a new foreign key needs the same deferral.

## Intentional differences

Postgres was made to match SQL Server wherever that was practical. These are the places where it deliberately
doesn't, and why each is safe. The rows have the details and the tests.

| What differs on Postgres | Rows | Why it's safe |
|---|---|---|
| Reads don't send `SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED` | #41 | The hint exists so that SQL Server readers don't wait for retention cleanup's locks, at the cost of dirty reads. Postgres readers never wait for writers (MVCC) and never see uncommitted rows. So Postgres gets the benefit without the dirty reads. SQL Server still sends the same statements. |
| The row version is the `xmin` system column: 4 bytes, one value per transaction | #14, #37 | Optimistic concurrency works the same: a stale token raises `DbUpdateConcurrencyException`, and the settings API answers 409. Clients treat the token as an opaque string. The one case that gives fewer tokens (two set-based updates of one row in one transaction) isn't used where a token is read. |
| The registration and admin lock is an advisory lock (`pg_try_advisory_xact_lock`) instead of `sp_getapplock` | #17 | It serializes the same callers, waits up to the same 10 s and then carries on, as `sp_getapplock` does, and it is released at commit or rollback. Two resource names with the same 64-bit hash would share a lock; there are two names. |
| A caller that waited for that lock is retried once the holder commits, instead of just reading on | #42 | The final outcome is the same as on SQL Server: one Admin, and the last-admin guard holds. The limits under bursts are in [Limits](#limits). |
| Three NO ACTION foreign-key checks run at commit | #19, #23 to #25 | Without it, Postgres refuses deletes that SQL Server performs, and one of those hits production (deleting a notebook whose chats attach its files, #24). A single statement behaves the same. A committed state always satisfies the key. The multi-statement and concurrency differences are in #25. |
| Timestamps keep whole microseconds, not 100 ns ticks | #32, #38 | Values are cut before they are written, so the stored value equals what the app holds. The lost digit is under 1 µs. |
| `DateTime.MinValue` / `MaxValue` are stored as `-infinity` / `infinity`, and DateTimes read back with `Kind = Utc` | #34, #39 | They round-trip through EF. The app treats `Unspecified` as UTC wherever the `Kind` matters. |
| U+0000 (NUL) in text is stored as U+FFFD | #35, #47 | Postgres can't store NUL, so without this the save fails. Lookups with the same value still match. |
| Trailing spaces count in `=` | #27 | The app trims or rejects such values where they come in. What's left: a slug ending in `%20` gives 404 instead of a match. |
| The sort order of punctuation, case ties and Guids | #28, #31 | Letters sort the same, ignoring case. The affected lists are short display lists. The app orders by a Guid only where any consistent order works. |
| `ILIKE` and regular expressions are rejected on text columns | #29 | SQL Server can't run them either, and production code has none. |
| Full-text tokenization: punctuation, short words, and `F#` matching | #44, #45 | Search was judged by function, and the verdict is SWITCHABLE. `F#` now finds F# text on Postgres, which SQL Server can't do. |
| The text-score scale (about 5 to 8 times higher) | #49 | The same chunks match. The ranking shift favors chunks with the exact phrase. A rescale is one line (decision 3). |
| Names over 63 bytes are shortened | #8 | Postgres's limit. The shortening is deterministic, and the constraint-name checks in code use shorter names (#56). |
| Seeded rows: fixed ids, one spelling of `STT`/`TTS`, no one-time backup table; one index without its no-op filter | #6, #7, #11 to #13 | Seeded rows are read by key, never by id. A new install has nothing to back up, and the filtered column is NOT NULL. |
| Two defects SQL Server already had are fixed on Postgres only | #51, #55 | This work doesn't change SQL Server. #55 needs your decision (decision 1). |

## SQL Server-specific code inventory

This lists every place in production code that uses a database-specific API, and how it is handled. Production code
here means the non-test `*.cs` files under `src/server`, with the migrations listed separately.
- **The search (Task 14):** it covered `UseSqlServer`, `UseNpgsql`, `Microsoft.Data.SqlClient`, `SqlConnection`,
  `SqlException`, `SqlParameter`, `Npgsql*`, `ExecuteSqlRaw`, `SqlQueryRaw`, `FromSqlRaw`, `FromSql`, `SqlQuery<>`,
  `ExecuteSql(`, `GetDbConnection`, `IsSqlServer()`, `IsNpgsql()`, `IsRelational()`, `ProviderName`, `EF.Functions`,
  `HasDefaultValueSql`, `HasFilter`, `HasColumnType`, check constraints, `FromEnvironment`, and code that recognizes
  a constraint by its name in an exception message.
- **Rechecked for this report** against the current tree. The recheck added three things the first list didn't have:
  #55's Postgres-only branch (written after that search), the model annotations, and the in-memory guards.
- **Not found anywhere in production code:** `FromSqlRaw`, `FromSql`, `ExecuteSqlInterpolated`, or an interpolated
  `SqlQuery<T>`.

Paths are relative to `src/server`. The classifications:
- **ported:** both databases are handled.
- **SQL Server-only by design:** the row names the Postgres counterpart, or why there is none.
- **Postgres-only:** a fix or an adaptation that runs only on Postgres.
- **neutral:** works the same on both databases.

| Site | API | Classification |
|---|---|---|
| `GuideAntsApi.DataModel/Database/ApplicationDbContextOptions.cs:37`, `:57` | `UseSqlServer` / `UseNpgsql` | ported: the single provider switch. On Postgres it also sets the Postgres migrations assembly and the NULL-ordering SQL generator (#30) |
| `GuideAntsApi.DataModel/Database/DatabaseErrors.cs:2`, `:33`, `:65`, `:73` | `Microsoft.Data.SqlClient`, `SqlException`, `NpgsqlException` | ported: recognizes both databases' error shapes (#18, #21, #42) |
| `GuideAntsApi.BackgroundJobs/JobQueueService.cs:119`, `:123` (`JobQueueClaimSql`) | `IsNpgsql()`, `ExecuteSqlRawAsync` | ported: the T-SQL `Claim` or `ClaimPostgres` (#21). `:107` `IsRelational()` is neutral (an in-memory guard) |
| `GuideAntsApi.BackgroundJobs/Services/Search/HybridSearcher.cs:1`, `:92-102` | `SqlClient`, `SqlParameter`, `GetDbConnection` | SQL Server-only by design; `PostgresHybridSearcher` is registered on Postgres (`GuideAntsApi.BackgroundJobs/ServiceCollectionExtensions.cs:59-62`, #44 to #50) |
| `GuideAntsApi.BackgroundJobs/Services/Search/PostgresHybridSearcher.cs:97-152`, `GuideAntsApi.BackgroundJobs/Services/Search/PostgresFullText.cs` | `NpgsqlConnection`, `NpgsqlParameter` | Postgres counterpart (#47 to #49). Its hand-written parameters set no `Size` (#36) |
| `GuideAntsApi/Database/DatabaseAppLock.cs:50`, `:56`, `:76` | `IsNpgsql()`, `ExecuteSqlRawAsync`, `SqlQueryRaw` | ported (#17) |
| `GuideAntsApi/Database/SqlServerDatabaseInitializer.cs:3`, `:41`, `:89` | `SqlConnection` | SQL Server-only by design; Program picks the Postgres initializer (`GuideAntsApi/Program.cs:187-197`). `:89` reads the provider with `FromEnvironment()` ([Limits](#limits)) |
| `GuideAntsApi/Database/PostgresDatabaseInitializer.cs:33-88` | `NpgsqlConnection` | Postgres counterpart, with the version guard (#43) |
| `GuideAntsApi/Services/Conversations/Queries/ConversationQueryService.cs:57`, `:62`, `:326`, `:331` | `IsSqlServer()`, `ExecuteSqlRawAsync` (`READ UNCOMMITTED`) | SQL Server-only by design (#41) |
| `GuideAntsApi/Services/Core/ProjectService.cs:152-164` | `IsSqlServer()`, `ExecuteSqlRawAsync` | SQL Server-only by design (#41) |
| `GuideAntsApi/Settings/ApplicationSettingsConfigurationProvider.cs:3`, `:217`, `:219`, `:239` | `SqlConnection` / `NpgsqlConnection` | ported (#16) |
| `GuideAntsApi/Settings/ApplicationSettingsService.cs:213-232` | `IsSqlServer()`, `GetDbConnection` (`OBJECT_ID`) | SQL Server-only by design (#52) |
| `GuideAntsApi/Services/Auth/AdminUserService.cs:376` | `IsNpgsql()` | Postgres-only: clears the change tracker before a retry (#51) |
| `GuideAntsApi/Services/Conversations/Persistence/ConversationPersistence.cs:368` | `IsNpgsql()`, `ExecuteUpdate` | Postgres-only: the Stop fence clears streaming rows (#55) |
| `GuideAntsApi/Services/UsageService.cs:67`, `:154` | `IsNpgsql()`, `EF.Functions.DateDiff*` | ported (#22) |
| `GuideAntsApi.DataModel/ApplicationDbContext.cs:1260`, `:1385` | `IsNpgsql()` | Postgres-only: applies `PostgresModelAdapter` (#1 to #4, #8, #14, #26, #32 to #35, #40) and the microsecond cut (#32) |
| `GuideAntsApi.DataModel/ApplicationDbContext.cs`: `HasDefaultValueSql("GETUTCDATE()")` and `("NEWID()")`, and `SqlServerIncludeProperties` (covering indexes) | default SQL, `INCLUDE` | ported by `PostgresModelAdapter`: `statement_timestamp()`, `gen_random_uuid()` and Npgsql's include properties (#40). Tests: `No_sqlserver_default_sql_remains`, `Covering_indexes_are_carried_over` |
| `GuideAntsApi.DataModel/ApplicationDbContext.cs`: the 10 index filters written with T-SQL brackets (`HasFilter("[CorrelationId] IS NOT NULL")` and others, e.g. `:113`, `:174`, `:1256`), and the 4 check constraints (`:333`, `:342`, `:364`, `:1034`; three use brackets, one bare column names) | T-SQL identifier quoting | ported: `PostgresModelAdapter` rewrites `[x]`, and the bare column names, as `"x"`. Tests: `Index_filters_use_postgres_quoting`, `Every_sql_server_index_filter_has_a_postgres_counterpart`, `Check_constraints_use_postgres_quoting` |
| `GuideAntsApi.DataModel/ApplicationDbContext.cs`: 23 `HasColumnType("nvarchar(max)")` (e.g. `:449`, `:458`), 2 `varbinary(max)` (`:664`, `:722`) and `vector(1536)` (`:241`) | SQL Server column types | ported: the adapter maps them to `text`, `bytea` and a pgvector column (#4). Tests: `No_sqlserver_column_types_remain`, `Embedding_maps_to_pgvector` |
| `GuideAntsApi.DataModel/Database/SqlServerNullOrderingQuerySqlGeneratorFactory.cs` | Npgsql's internal query SQL generator (`EF1001`) | Postgres-only: SQL Server's NULL order (#30). Re-check it on every Npgsql upgrade ([Ongoing cost](#ongoing-cost)) |
| `GuideAntsApi/Services/Migrations/StorageMigrationDbContext.cs:28`, `GuideAntsApi/Program.cs:539`, `:580` | `Configure` with the configured provider | ported (#20) |
| `GuideAntsApi/Configuration/StartupConfiguration.cs:552-587`, `GuideAntsApi/Program.cs:187`, `:205` | provider parsed from configuration | neutral |
| `GuideAntsApi.DataModel/ApplicationDbContext.cs:9-16` (`Factory`), `AntRunner.Chat/AntRunner.ToolCalling/AssistantDefinitions.Storage/DatabaseStorage.cs:34`, `GuideAnts.Usage/EfUsageRecorder.cs:291-297`, `GuideAnts.Usage/AgentInvocationRecorder.cs:236-237` | `Configure` with `FromEnvironment()` | ported. The provider comes from the process environment, which Program fills from configuration after the host is built (`GuideAntsApi/Program.cs:300-308`); see [Limits](#limits) |
| `GuideAntsApi.DataModel/ApplicationDbContextDesignTimeFactory.cs`, `GuideAntsApi/Database/DesignTimeContexts.cs:7` (`[assembly: DbContext]`) | `-- --provider` argument or the environment | neutral: design time only; lets `dotnet ef` find the context for both databases |
| In-memory guards under `GuideAntsApi/Services/Conversations/` and `GuideAntsApi/Services/LlamaCpp/LocalModelOnboarding/`: `db.Database.ProviderName` compared with `"Microsoft.EntityFrameworkCore.InMemory"` in `ConversationPersistence.cs:153`, `:345`, `:1180`, `:1235`, `:1532`; `DistributedConversationLockService.cs:51`, `:142`, `:198`, `:270`; `ConversationUndoService.cs:260`, `:289`; `ConversationTurnRecoveryService.cs:368`; and `LocalModelOperationService.cs:321-322` (`Contains("InMemory")`) | `ProviderName` | neutral: they set the unit tests' in-memory provider apart and treat SQL Server and Postgres alike |
| `GuideAntsApi.DataModel/Migrations/*`: 19 of the 60 migrations call `migrationBuilder.Sql` | raw T-SQL | SQL Server-only by design. Postgres has its own two migrations in `GuideAntsApi.DataModel.Postgres/Migrations`, with hand-written SQL for the view, the seed rows, the deferred foreign keys and the full-text search |
| `GuideAntsApi/Services/Components/Sync/NotebookFileReconciler.cs:483-485`, `GuideAntsApi/Endpoints/NotebookEndpoints.cs:353`, `GuideAntsApi/Services/Components/NotebookFileSyncService.cs:101-112` | constraint name in an exception message | neutral, proven on both databases (#56) |
| `GuideAntsApi/Services/Conversations/DistributedConversationLockService.cs:114` | `DatabaseErrors.IsForeignKeyViolation` | ported (#18) |

## Ongoing cost

What carrying both databases costs, now and for every later change:
- **Two migrations for every schema change.** Each schema change needs a migration in
  `GuideAntsApi.DataModel/Migrations` (SQL Server) and one in `GuideAntsApi.DataModel.Postgres/Migrations` (Postgres,
  generated with `-- --provider Postgres`). Raw SQL in a migration has to be written in both dialects. Two CI checks
  enforce it:
  - `migrations-in-sync` runs `has-pending-model-changes` for each provider, so a model change without a migration
    fails CI;
  - `database-parity` migrates both databases from empty and compares tables, columns, indexes, foreign keys, views
    and seeded rows.

  The commands are in `src/server/GuideAntsApi.DataModel/EF_COMMANDS.md`.
- **Provider-specific code in about 18 places** ([inventory](#sql-server-specific-code-inventory)). Each one is
  written twice, or behind a provider check:
  - the two hybrid searchers;
  - the job claim, the application lock, the settings loader and the initializers;
  - the READ UNCOMMITTED hints;
  - the Postgres-only fixes;
  - the model adapter.

  New raw SQL has two rules on Postgres:
  - It doesn't pass through EF's value converters, so it has to handle `DateTime` kinds, NUL characters and
    microseconds itself (#32 to #35).
  - A hand-written `NpgsqlParameter` must not set `Size`, because Npgsql truncates to it (#36).
- **CI time: unknown until the first GitHub run.** The new jobs:
  - a second integration-test leg (Postgres);
  - `database-parity`;
  - `migrations-in-sync`;
  - `database-speed`, which runs only when started by hand.

  The only local indication isn't comparable: the Postgres suite took about 1 m 35 s and the emulated SQL Server suite
  about 3 m 24 s, and the integration tests' Postgres containers probably run with Testcontainers' `fsync=off`
  defaults.
- **Known flaky tests and the CI retry.** #53 and #54 fail now and then on both databases. CI reruns exactly those
  tests once. The real fix is to rewrite those tests so they don't race the worker. Until then the retry list in
  `.github/workflows/client-test.yml` has to be kept up to date.
- **Library upgrades need checks.**
  - **NULL ordering (#30)** relies on Npgsql's internal query SQL generator (`EF1001`). On every Npgsql upgrade,
    re-run `NullOrderingSqlTests` and `NullOrderingBehaviorTests`. Re-check the generator if the model gains
    inheritance, owned types, `FromSql` or JSON columns.
  - **The string converter (#35)** applies to every column and relies on EF still translating string methods on
    converted properties. Re-check that on EF or Npgsql upgrades.
  - **Exact error messages:** `Postgres_rejects_ILike_and_regular_expressions_on_case_insensitive_columns` asserts
    Postgres 18's exact messages, so it may need updating across minor versions.
- **Deferred foreign-key checks.** A new NO ACTION foreign key that a cascade can outrun needs the same
  `DEFERRABLE INITIALLY DEFERRED` in the Postgres migration. `Postgres_defers_exactly_the_no_action_checks_a_cascade_of_the_same_delete_could_outrun`
  fails until it gets one.
- **Constraint and index names over 63 bytes** are shortened on Postgres (#8). Code that recognizes a constraint by
  its name (#56) needs names under that length.
- **A possible index follow-up.** Three indexes on nullable sort columns can't serve SQL Server's NULL order on
  Postgres: `IX_GuideMembers_GuideId_DisplayOrder`, `IX_Models_IsActive_DisplayOrder` and
  `IX_Tools_Category_IsActive_DisplayOrder` (#30). All three are on small tables. If the speed numbers show the
  models, tools or crew ordering matters, the fix is `HasNullSortOrder(NullsFirst)` in `PostgresModelAdapter`, plus a
  Postgres migration.
- **Developers run both databases.** A database change needs its tests run with `GA_INTEGRATION_TEST_DB_PROVIDER`
  set to both `SqlServer` and `Postgres`. `dotnet test GuideAntsApi.sln` now includes `GuideAntsApi.DatabaseParity.Tests`,
  which needs Docker and starts both databases. On Apple Silicon the SQL Server image runs emulated and slowly.

## Limits

What the evidence doesn't cover, and what to know before relying on Postgres.

**Limits of the evidence:**
- **x86 CI hardware only.** The speed numbers, once they exist, will come from one shared 4-vCPU, 16 GB GitHub
  runner, not from production hardware.
- **Synthetic data volumes.** The speed data is 50 projects × 10 notebooks × 20 conversations × 50 messages, plus the
  demo corpus replicated to 20,000 chunks. The integration tests use their own small fixtures.
- **The search comparison is one corpus of 38 queries**, and SQL Server's `CONTAINSTABLE` ranks shift slightly between
  two seedings of the same data. More limits are under the search results above:
  - the saved query embeddings, so the live embedding path isn't compared;
  - no `F#`, `C++` or `.NET` query;
  - no run with the scope filter moved before the top N;
  - Claude-reviewed classifications.
- **No data migration.** Switching an install to Postgres starts it on an empty database. Moving SQL Server data
  across was out of scope, and nothing here tests it.
- **Guid and string tie ordering differs**, and no test can make it equal. Where a Guid breaks a tie or is the only
  key, or where strings tie on case, the rows come back in a different order on each database. Within one database
  the order is consistent (#28, #31).
- **Local runs only, on Apple Silicon** ([Checked by running vs. only by reading](#checked-by-running-vs-only-by-reading)).

**Postgres requirements and deployment:**
- **Postgres 18 or later is required.** Postgres 17 rejects `LIKE` and `strpos` on the case-insensitive collation
  (#26). The initializer refuses an older server at startup with "PostgreSQL 18 or later is required" (#43). That
  rules out managed services that don't offer 18 yet.
- **Managed Postgres is untested.** The full-text migration runs `CREATE TEXT SEARCH DICTIONARY` and `CONFIGURATION`,
  and the initializer connects to the `postgres` maintenance database to create the app's database (`CREATEDB`). A
  least-privilege managed-service user may not be allowed either.
  - The design avoids custom extensions: it uses only `vector`, ICU collations and built-in text search.
- **Regular-expression literals.** The full-text normalizer's patterns rely on `standard_conforming_strings = on`
  (Postgres's default) when the migration runs.
- **Full-text ranking cost.** `ts_rank_cd` ranks every table-wide match before the result limit, across all projects.
  A local, native and therefore only indicative run saw the `model` search take about 28 ms with 9,656 matches out of
  20,000 chunks, against 3 to 4 ms with a few hundred matches. SQL Server's `CONTAINSTABLE` has the same table-wide
  shape. The speed run reports the match counts.
- **`F#` differs on purpose.** SQL Server reads it as the noise word `f` and finds nothing; Postgres finds F# text
  (#44).
- **Text scores are on a different scale**, about 5 to 8 times SQL Server's (#49).
  - **Ruling:** not rescaled, because the current order favors exact-phrase matches.
  - **To rescale:** divide the text score in `PostgresHybridSearcher`'s query, and the parity diagnostics, by the
    measured median (5.68). Then rerun the comparison.
  - **What the model sees:** the scores themselves, because the search tool's result serializes `Score`,
    `VectorScore` and `TextScore`.
- **Bursts of concurrent registrations or admin changes** (inferred, not run, #42):
  - **Retries run out:** each caller that waited for the lock is retried, and after 5 retries the request fails. So
    more than about 6 concurrent conflicting registrations or admin user changes can make the last caller fail with
    500, where SQL Server would only make it wait.
  - **A stale read:** a waiter that doesn't write can return a 4xx from its old snapshot. For example, an admin's role
    change can answer "Pending users must be approved before changing role." while another admin's approval commits.
    Retrying the request succeeds.
- **Registration's retry.** Registration clears its change tracker only for a serialization failure (40001). Another
  transient error at `SaveChanges` answers 409, as on SQL Server; the failure is loud, not silent (#51). Admin
  mutations clear the tracker for every error on Postgres.
- **Deferred foreign-key checks change when, and sometimes which, transaction fails** under concurrency (#25,
  reasoned, not tested). Integrity holds either way.
- **The stream defects found while explaining the flaky tests:**
  - **#53:** a duplicate final message when a soft cancellation wins after the provider returned. It is latent, has
    no production path, affects both databases and isn't fixed.
  - **#55:** the stray streaming row, fixed on Postgres only (decision 1).
- **Databases migrated with the earlier Postgres baseline.** Two problems were fixed by editing the unreleased
  baseline in place, not by adding migrations:
  - the non-deferrable foreign keys of #19, #23 and #24;
  - the `now()` defaults of #40.

  A development database migrated before those edits keeps the old behavior. Drop and recreate it. This is fine
  before the first release, but once the baseline has shipped, changes have to be new migrations.

**Configuration:**
- **Where the provider setting is read.** The host, the storage switches and dependency injection read
  `Database:Provider` from configuration. These parts read it from the process environment:
  - `ApplicationDbContext.Factory`;
  - `DatabaseStorage`;
  - the two usage recorders;
  - `SqlServerDatabaseInitializer`.

  Program copies configuration into the environment after the host is built, for keys that aren't already set, so the
  two agree in every normal setup. They can split only in one case: a colon-form environment variable
  (`Database:Provider`) disagrees with a higher-priority source such as the command line. Program calls the SQL
  Server initializer only when configuration says SQL Server, so the initializer's own read matters only in that split
  case.
- **Storage migration switches.** `--run-named-storage-migration` and `--run-ascii-slug-normalization` build their
  context without the retrying execution strategy, because they open their own transactions (#20). A transient error
  there fails the run instead of retrying.
- **Docker and launchers:**
  - `docker-compose.postgres.yml` needs Docker Compose 2.24.4 or later.
  - `start_*` refuse `--backend slim` with Postgres, because the slim UI image bundles SQL Server.
  - `GA_POSTGRES_PASSWORD` must not contain `;`, `=` or quotes.
  - `start_*` don't remember the provider between runs; the installer does.
  - The installer's size estimate ignores the provider.

**Development environment and other observations:**
- **`ScriptExecutionAgent.Tests` need `pwsh`.** Without it, 6 tests fail locally. That's unrelated to the database.
- **This repository's iCloud sync.** The working copy is under an iCloud-synced `~/Documents`. iCloud created conflict
  copies of build outputs (e.g. `GuideAntsApi 24.dll`), which risk test runs against stale binaries. Exclude `bin/` and
  `obj/` from iCloud, or move the repository. This applies to this machine only.
- **SQL Server deadlocks under contention.** A local full-volume check (emulated, so not timing evidence) saw SQL
  Server's job claim produce deadlock victims (1205) under 8 concurrent workers: 4 in 26 drains of 1,000 jobs.
  Production already tolerates them: the claim logs a warning and reports no job. Postgres had none. This is
  existing SQL Server behavior, not a Postgres regression.

## Speed

**Numbers pending.** They come from the first x86-64 CI run. After the branch is pushed, run the `database-speed` job of
`.github/workflows/client-test.yml` by hand (Actions > "Client and Server tests" > Run workflow). It runs only on
`workflow_dispatch`. Download the `database-speed-results` artifact into `samples/database-speed/RESULTS.md` and
summarize it here: the summary table, plus the cause of each row flagged as more than 20% slower, where it's known.
The watch items below are the likely causes. Dispatch it at least twice: one run has no variance estimate. No local numbers are reported: this
branch was built on Apple Silicon, where SQL Server's amd64-only image runs emulated. A full run exits 2 unless both the
harness and the Docker daemon are x86-64.

GitHub requires a workflow with the `workflow_dispatch` trigger to exist on the default branch before `gh workflow run`
(or the Run workflow button) can start it. Until `client-test.yml` with the trigger is on `main`, run the harness by
hand on an x86-64 Linux machine with Docker and the .NET 8 SDK (`docker info` must report `Architecture: x86_64`):

```bash
git checkout feature/postgres-db-switch
cd samples/database-speed
dotnet test DatabaseSpeed.sln
dotnet run -c Release --project src/DatabaseSpeed   # writes samples/database-speed/RESULTS.md
```

**How to read RESULTS.md:** it opens with "Limits of this comparison", directly above the summary. Read it before any
ratio:
- the cold start is the product's fresh-install cost: SQL Server applies its whole migration history, Postgres two
  migrations;
- one run has no variance estimate, so read the delta (ms) next to each ratio on sub-millisecond rows;
- both run stock settings (Postgres `shared_buffers` 128 MB, Npgsql auto-prepare off);
- the job-claim latency covers successful claims only, under busy-spin contention;
- the CPU limits are a quota;
- storage differs (a Postgres volume vs SQL Server's writable layer).

**The harness:** `samples/database-speed` ([README](../samples/database-speed/README.md)) is a console app.
- **Setup:**
  - Testcontainers starts one SQL Server container (`mssql2025-express-fts`, `MSSQL_PID=Developer` as in the compose
    stacks) and one Postgres container (`pgvector/pgvector:0.8.7-pg18`, the image's default settings).
  - Both get the same limits, 4 CPUs and 8 GB, read back with `docker inspect`.
  - Testcontainers' default `fsync=off` / `synchronous_commit=off` on Postgres is removed, and the run checks it.
- **The real code paths:**
  - Each database is created by the host's own initializer.
  - Each provider gets the host's registrations: the scoped context with `EfQueryWarningInterceptor`, the pooled
    factory, `AddBackgroundJobs` (so `HybridSearcher` on SQL Server and `PostgresHybridSearcher` on Postgres), and the
    real services.
- **The seed:** the same deterministic seed goes into both databases: 50 projects × 10 notebooks × 20 conversations ×
  50 messages, 10,000 usage events, and the demo corpus replicated to 20,000 chunks with its saved embeddings.
- **The runs:** each operation does 20 warm-up runs and 200 measured runs, interleaved across the two databases, with
  the order alternating. The cold start does 1 + 10.
- **The report:** per operation, the runs, median, nearest-rank p95, the Postgres / SQL Server median ratio, and a flag
  above 1.2. There's no pass/fail threshold.
- **The header:** hardware, image digests, container limits, server settings, row counts read back from both
  databases, and the commit, with a warning if `src/server` or the harness had uncommitted changes.

**What it times** (every call is the production service unless noted):
- four hybrid searches: a typical phrase (Q01), a common phrase (Q37), a very common word (`model`) and a rare phrase
  (Q22);
- loading a 200-message conversation (`ConversationQueryService`);
- saving one chat turn (`ConversationPersistence`: create turn, user message, streaming assistant message,
  checkpoint, finalize, terminalize);
- listing projects, a project's notebooks (`ProjectService`) and a notebook's conversations;
- the 30-day usage summary (`UsageQueryService`);
- listing models and tools (`CatalogService`);
- a path-prefix lookup on notebook files. This one is the LINQ of `NotebookFileService`'s folder delete, run directly,
  because the method also deletes files on disk;
- 8 workers draining 1,000 jobs (`JobQueueService`), measured as drain time, per-claim latency and empty claims;
- a cold start: the initializer on an empty database plus the first query.

**Smoke run:** `--smoke` (tiny data, 3 + 10 runs, output `RESULTS-smoke.md` labelled NOT VALID FOR COMPARISON,
git-ignored) ran every operation end to end on both providers on Apple Silicon. Its row counts and results matched on
both. Its numbers are not evidence.

**Things to watch in the first real run** (carried over from earlier reviews; RESULTS.md points at the rows):
- **`LIKE 'prefix%'` under the nondeterministic `guideants_ci` collation** can't range-scan a B-tree on Postgres, while
  SQL Server can seek `IX_NotebookFiles_RelativePath_NotebookId` (Task 7). See the path-prefix row and its actual
  plans. The production queries also filter by notebook or project, so the cost should follow files per notebook.
- **Indexes on nullable sort columns.** `IX_GuideMembers_GuideId_DisplayOrder`, `IX_Models_IsActive_DisplayOrder` and
  `IX_Tools_Category_IsActive_DisplayOrder` can't serve the SQL Server `NULLS FIRST` order on Postgres (#30, Task 8).
  See the models and tools rows and plans: 40 models the harness seeds, since the migrations seed none, and the
  migrations' 12 tools. `GuideMembers` isn't timed. The fix, if the numbers say it matters:
  `HasNullSortOrder(NullsFirst)` in `PostgresModelAdapter`, plus a Postgres migration.
- **`ts_rank_cd` before `LIMIT`** (Task 12). Postgres ranks every table-wide full-text match, across all projects,
  before it takes the candidates; a GIN index can't return rows in rank order. For a common term on a large
  multi-project `DocumentChunks` table that is O(matches). SQL Server's `CONTAINSTABLE` `top_n_by_rank` has the same
  table-wide shape. Compare the rare, typical, common and very common search rows; each lists its table-wide match
  count per provider.
- **The Postgres job claim runs without `SKIP LOCKED`, by design** (#21, Task 10). A contended claimer waits, then gets
  no job, as on SQL Server. See the throughput, the empty claims per drain, and the failed database commands. A local
  full-volume check (SQL Server emulated, so no timing evidence) saw SQL Server's claim produce deadlock victims (1205)
  under the 8-worker contention: 4 in 26 drains of 1,000 jobs. Each one is caught by `TryClaimAsync`, which logs
  "Job claim failed" and returns no job (an empty claim). EF's execution strategy didn't retry them. Postgres had none.
  Whether this reproduces on x86 is for the CI run to show.
- **Path-prefix plans:** the same local check showed that, at full volume, both databases read the path-prefix query
  through `IX_NotebookFiles_NotebookId` and apply the `LIKE` as a residual filter. SQL Server doesn't seek the
  `RelativePath` index either, so the collation limit doesn't change this query's plan. The plans are
  architecture-independent, but they come from that local run; RESULTS.md will show them from the CI run.
- **Postgres runs with stock memory settings** (`shared_buffers` 128 MB, as `docker-compose.postgres.yml` ships it).
  SQL Server sizes its buffer pool from the container's memory instead.

## Suggested CLAUDE.md additions

`CLAUDE.md` isn't tracked in this repository, so this work doesn't change it. Here is text to paste into it, under
"Commands → Server" and "Architecture":

````markdown
#### Database provider (SQL Server or Postgres)

`Database:Provider` picks the database per install: `SqlServer` (the default when it's missing) or `Postgres`
(Docker: `Database__Provider`), next to `ConnectionStrings:DefaultConnection` (Npgsql format for Postgres, e.g.
`Host=localhost;Port=5433;Database=guideants-dev;Username=guideants;Password=...`). Postgres must be 18+ with pgvector;
the initializer refuses older servers. One `ApplicationDbContext` serves both: `PostgresModelAdapter` adapts the model
on Postgres, and provider-specific code branches on `Database.IsNpgsql()` / `IsSqlServer()`. Never change the SQL that
SQL Server receives to suit Postgres. Every behavioral difference between the two, with its proving test, is logged in
`docs/postgres-parity-report.md`: add a row when you find a new one.

**Every schema change needs one migration per provider**: SQL Server in `GuideAntsApi.DataModel/Migrations`, Postgres
in `GuideAntsApi.DataModel.Postgres/Migrations`. Raw `migrationBuilder.Sql` has to be written for each dialect. CI fails
if either provider has pending model changes (`migrations-in-sync`) or if the two migrated schemas or seeded rows
differ (`database-parity`). From `src/server`:

```bash
dotnet ef migrations add FeatureName --project GuideAntsApi.DataModel/GuideAntsApi.DataModel.csproj --startup-project GuideAntsApi/GuideAntsApi.csproj
dotnet ef migrations add FeatureName --project GuideAntsApi.DataModel.Postgres/GuideAntsApi.DataModel.Postgres.csproj --startup-project GuideAntsApi/GuideAntsApi.csproj -- --provider Postgres
dotnet ef migrations has-pending-model-changes --project GuideAntsApi.DataModel/GuideAntsApi.DataModel.csproj --startup-project GuideAntsApi/GuideAntsApi.csproj
dotnet ef migrations has-pending-model-changes --project GuideAntsApi.DataModel.Postgres/GuideAntsApi.DataModel.Postgres.csproj --startup-project GuideAntsApi/GuideAntsApi.csproj -- --provider Postgres
```

More commands (`database update`, `remove`) are in `GuideAntsApi.DataModel/EF_COMMANDS.md`.

Tests on each provider (`GA_INTEGRATION_TEST_DB_PROVIDER`: `SqlServer`, the default, or `Postgres`;
`GA_INTEGRATION_TEST_POSTGRES_IMAGE` overrides the Postgres image, default `pgvector/pgvector:0.8.7-pg18`;
`GA_INTEGRATION_TEST_MSSQL_IMAGE` the SQL Server one):

```bash
GA_INTEGRATION_TEST_DB_PROVIDER=Postgres dotnet test GuideAntsApi.IntegrationTests/GuideAntsApi.IntegrationTests.csproj
dotnet test GuideAntsApi.DatabaseParity.Tests/GuideAntsApi.DatabaseParity.Tests.csproj   # needs Docker; starts both databases
```

`dotnet test GuideAntsApi.sln` includes `GuideAntsApi.DatabaseParity.Tests`, so it needs Docker. CI runs the
integration job once per provider (`server-integration-test (SqlServer)` / `(Postgres)`) and reruns only the known
flaky tests (parity report rows 53 and 54) once, via `.github/scripts/flaky_retry.py`.

Postgres for server-on-host dev: `docker compose -f docker/docker-compose.pgsql.yml up -d` (host port 5433). Launchers:
`./start_macos.sh --db postgres` (or `start_linux.sh` / `start_windows.cmd`, or `GA_DB_PROVIDER=postgres`); the
installer's `./installer/guideants.sh --db postgres` remembers the choice, so switching back needs `--db sqlserver`.
Each provider has its own database: data doesn't move between them. See
`docs/developer-config-guide.md#postgres-instead-of-sql-server`.
````
