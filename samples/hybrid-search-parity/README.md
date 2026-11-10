# Hybrid search parity: SQL Server vs PostgreSQL

Can GuideAnts' hybrid search (full-text + vector) move from SQL Server to PostgreSQL and give the same results? This sample answers that with evidence. **The answer for GuideAnts' own search code is in [RESULTS-production.md](RESULTS-production.md).** [RESULTS.md](RESULTS.md) is the earlier comparison of the sample's own Postgres port, which led to that code. The design is in [the spec](../../docs/superpowers/specs/2026-10-20-hybrid-search-parity-design.md).

There are two comparisons, over the same data:

- **Production** (`prod-sqlserver` vs `prod-postgres`, [RESULTS-production.md](RESULTS-production.md)): the real `HybridSearcher` and `PostgresHybridSearcher`, resolved through production's own `AddBackgroundJobs` registration, on databases built by the real EF Core migrations (`src/Parity.Production`). Nothing about the query is copied.
- **Port** (`sqlserver` vs `postgres`, [RESULTS.md](RESULTS.md)): production's SQL Server query copied verbatim from commit `9f8cd102` (a test fails if production changes), against the sample's Postgres port: pgvector cosine distance, a generated `tsvector`, `phraseto_tsquery`, and `ts_rank_cd(…, 32)`.

In both, the databases hold byte-identical chunks and vectors from `data/`, built from `docs/*.md` at `9f8cd102`, and the query vectors are the saved ones (no embeddings API is called). The `search` command knows only `ISearchStore`; `appsettings.json` decides which database and which searcher it gets.

## Result

### Production search: SWITCHABLE

All five conditions pass ([RESULTS-production.md](RESULTS-production.md)): vector parity 152/152 pairs, every full-text difference reviewed and `acceptable` (26, 0 open, 0 blockers), mean top-5 overlap 0.962, the same #1 in 93.4% of pairs, and zero unexplained hybrid differences. `compare` exits `0`.

- **The `C#` blocker is gone.** Production's Postgres search rewrites the standalone terms `C#`, `F#`, `C++` and `.NET` to `csharp`, `fsharp`, `cplusplus` and `dotnet` in the indexed text and the query. For Q33 (`C#`) the top 5 and the #1 now equal SQL Server's in all four scopes. Postgres also matches the 34 chunks whose only C# is a ```` ```csharp ```` code fence (the same lexeme); that is recorded as `acceptable`.
- **The other 22 differences are the port's, unchanged.** They have exactly the port's chunk IDs, so their reviews carry over.
- **The text score scale shifts the ranking toward text matches, and was kept.** `ts_rank_cd(…, 32)` is about 5.7 times SQL Server's `RANK / 1000` on prose chunks (median of 376 chunks both engines match). Dividing it by 5.68 would raise the overlap to 0.995 and the same #1 to 99.3%. But in 7 of the 10 pairs whose #1 differs today, Postgres's #1 contains the exact phrase and SQL Server's doesn't (in the other 3 both do), and Postgres finds an exact-phrase chunk in the top 5 in 45 of 51 query/scope pairs against SQL Server's 41. So it was kept as it is. The measurements are in [REVIEW-NOTES.md](REVIEW-NOTES.md#e-the-postgres-text-score-scale-production-search-2026-11-09).

### The sample's port: NOT SWITCHABLE

The port's verdict is **NOT SWITCHABLE**: four of the five conditions pass (vector parity 152/152 pairs, mean top-5 overlap 0.955, same #1 in 92.1% of pairs, zero unexplained hybrid differences). The only failing condition is review: 4 full-text differences are recorded as `blocker`, all for the query `C#` (Q33). Postgres's parser drops the `#`, so the phrase becomes the bare letter `c`. Counted per scope, the Postgres-only matches are 30 (P1) + 65 (P2) + 15 (P3) = 110 chunks (P1/N1's 4 are a subset of P1's 30), and in the P2 scope the top-5 lists are fully disjoint. A custom dictionary cannot fix this, because the default parser drops `#` before any dictionary sees the text. The realistic options are a custom text search parser, or normalising terms like `C#`, `C++` and `.NET` (for example to `csharp`) in both the indexed text and the query. Production took the second option. The other 22 differences are recorded as `acceptable` (tokenization and punctuation). See [RESULTS.md](RESULTS.md) for the evidence and [REVIEW-NOTES.md](REVIEW-NOTES.md) for the investigation behind the Q03, Q09 and Q33 reviews, and read [Limits and review](#limits-and-review) before relying on either verdict.

## Prerequisites

- Docker Desktop. The SQL Server image is amd64-only, so it's emulated (and slow to start) on Apple Silicon.
- .NET 8 SDK (`global.json` pins 8.0.4xx).
- An embeddings key, **only** for `generate-data` and live queries. Either OpenAI: `export OPENAI_API_KEY=...`, or OpenRouter: `export OPENROUTER_API_KEY=...`. Alternatively use user secrets: `dotnet user-secrets set OpenAI:ApiKey <key> --project src/Parity.Cli`, plus optionally `OpenAI:Endpoint` and `OpenAI:Model` to point at another compatible endpoint or model.

## Run it

```bash
docker compose up -d                                         # SQL Server on 14333, Postgres on 54329
dotnet run --project src/Parity.Cli -- seed --all            # recreate and load all five profiles
dotnet run --project src/Parity.Cli -- search "conversation lock" --profile prod-postgres --scope P1
dotnet run --project src/Parity.Cli -- compare --baseline prod-sqlserver --candidate prod-postgres --out RESULTS-production.md
dotnet run --project src/Parity.Cli -- compare --out RESULTS.md   # the port (Parity:Baseline and Parity:Candidate)
dotnet test --filter "Category!=Integration"                 # fast unit tests
dotnet test                                                  # everything, including Testcontainers suites
docker compose down -v                                       # stop and remove the demo databases
```

`docker-compose.yml` starts the two demo servers (SQL Server and Postgres with pgvector) used by `seed`, `search` and `compare`. Each profile has its own database: `parity` and `parity_english` for the port, `parity_production` on both servers for the production profiles. The integration tests start their own Testcontainers and don't need it.

Seeding a production profile drops its database and runs production's migrations (`Database.Migrate()`), including the SQL Server full-text catalog and the Postgres `"ContentTsv"` column. Then it inserts the parent rows the `DocumentChunks` foreign keys need (one `Projects` row per project and one `ContentFiles` row per file, with the sample's deterministic IDs) and the chunks, through EF. As for every profile, only a local server or a database whose name starts with `parity` can be dropped. Production's searchers catch every error and return an empty list; the production store reports the logged error instead, so a failure never reads as "no results". They always use `maxResults * 2` full-text candidates, so the production profiles refuse any other `Hybrid:CandidateMultiplier`, and they have no quirk-fixed variant, so that informational run shows "not available".

`compare` exits `0` when the verdict is switchable, `1` when it isn't, and `2` on configuration, data or connection errors.

The whole test suite is green even while `known-differences.json` records blockers. The integration test `Rule2EveryDifferenceHasACurrentReview` checks code correctness, not the verdict: it fails only for a difference with no review or whose recorded snapshot no longer matches (`UNREVIEWED` or `SNAPSHOT CHANGED`), and it writes recorded blockers to the test output as information. Whether the databases are switchable is what `compare` reports.

## How the verdict works

"Switchable" requires all five conditions, with thresholds fixed in `appsettings.json` before the first run:

1. Vector results match: same top 10, scores within 1e-5.
2. Every full-text match difference, and every error that needs review (one-sided, or both-sided unless both hybrid calls failed and so return empty results on both engines), is reviewed in `known-differences.json` and marked `acceptable`.
3. Mean top-5 hybrid overlap is at least 0.9.
4. The same #1 result appears in at least 80% of query/scope pairs.
5. There are zero unexplained hybrid differences.

Two runs are informational and never affect the verdict: production's cross-project top-N quirk fixed on both engines, and the stock `english` Postgres configuration (with stemming).

## Reviewing differences

When `compare` reports an `UNREVIEWED` difference, read its tokenization evidence in `RESULTS.md` (or `RESULTS-production.md`). Then add an entry to `known-differences.json`, copying the exact chunk IDs, with `decision` set to `acceptable` or `blocker`. If a later run's difference doesn't match the recorded IDs exactly, the entry shows as `SNAPSHOT CHANGED` and needs another look.

An entry applies to every comparison unless it names a `candidate` profile. Q33 needs that: the port (`"candidate": "postgres"`) and the production searcher (`"candidate": "prod-postgres"`) give different snapshots, reviewed separately (blocker and acceptable).

## Regenerating the data

```bash
dotnet run --project src/Parity.Cli -- generate-data   # costs a few cents; reads docs from git at 9f8cd102
```

Chunk IDs are deterministic, so the existing `known-differences.json` entries stay valid when the data is regenerated from the same commit.

## Limits and review

- **Who reviewed the differences.** The review of the 30 recorded differences (26 per comparison; the 4 Q33 entries exist once for each candidate) was delegated to Claude by the user ("do whatever you think is best"). None was reviewed line by line by a human. Each entry says so in its `reviewedBy` field. To change any decision, edit `known-differences.json` and re-run `compare`.
- **Two accepted differences are real Postgres recall misses.** Q09: one true prose occurrence ("GuideAnts guide/internal tools") is missed because Postgres lexes `guide/internal` as one token. Q25: searching a URL prefix finds nothing, because Postgres keeps the longer URL as one token. Q09 was accepted as low impact; Q25 is recorded with its effect. Neither is a match-for-match equivalence, so weigh them yourself.
- **Tie-break explanations are not proof.** 8 hybrid differences in the port's run and 5 in the production run are explained as `top-n-boundary-tie`. They come from diagnostics whose tie-breaking among equal text ranks is unspecified, the same nondeterminism as production's own query.
- **The engines use different stop lists.** SQL Server's system stoplist and Postgres's `english` list differ (for example single letters, digits, `not` and `use`). The token evidence in `RESULTS.md` shows it per query.
- **One corpus, 38 queries.** The evidence is `docs/*.md` at `9f8cd102` and the saved queries in `inputs/queries.json`. It says nothing about other data or query mixes. No saved query uses `F#`, `C++` or `.NET`: on those terms production's Postgres search deliberately differs from SQL Server (SQL Server reads `F#` as the noise word `f` and finds no text; Postgres finds F# chunks). That is covered by `HybridSearchBehaviorTests` and parity report row #44, not by this comparison.
- **The production profiles skip the embeddings provider.** The searcher gets the saved query vector through a stand-in `IEmbeddingService`, so `ProviderRoutedEmbeddingService` and the live embedding models aren't exercised. Everything after the embedding (the SQL, the scores, the filters, the result mapping) is production's.
- **The text score scale was measured on this corpus only.** The ratio between the engines' text scores depends on chunk length (short chunks score higher on SQL Server), so the 5.68 divisor in the review notes is a calibration for this data, not a constant.
- **SQL Server cannot match CJK queries through production's code path here.** Production splices the phrase into its SQL as a non-`N` literal, and the demo database's default collation is `SQL_Latin1_General_CP1_CI_AS`, which turns characters outside code page 1252 into `?`. Confirmed on the demo container: `CAST('テキスト検索' AS varchar(20))` returns `??????` (bytes `0x3F` six times), while the `nvarchar` form keeps the text. Q34 (`テキスト検索`) matches 0 chunks on SQL Server with either literal form, although a `LIKE` scan finds one chunk containing it. Postgres also matches 0 for Q34 (it lexes the phrase as one token), so Rule 2 sees no difference and Q34 is not evidence of parity for non-Latin text. The SQL Server diagnostics send the phrase as `varchar`, so they run the same code-page conversion production does. This is a limit of this collation and the copied query, and says nothing about SQL Server databases with a different collation.
