# Hybrid search parity: SQL Server vs PostgreSQL

Can GuideAnts' hybrid search (full-text + vector) move from SQL Server to PostgreSQL and give the same results? This sample answers that with evidence. **The current answer is in [RESULTS.md](RESULTS.md).** The design is in [the spec](../../docs/superpowers/specs/2026-10-20-hybrid-search-parity-design.md).

- SQL Server runs production's `HybridSearcher` query, copied verbatim from commit `9f8cd102`. A test fails if production changes.
- Postgres runs the candidate port: pgvector cosine distance, a generated `tsvector`, `phraseto_tsquery`, and `ts_rank_cd(…, 32)`.
- Both databases hold byte-identical chunks and vectors from `data/`, built from `docs/*.md` at `9f8cd102`.
- The `search` command knows only `ISearchStore`; `appsettings.json` decides which database it gets.

## Result

The current verdict is **NOT SWITCHABLE**: four of the five conditions pass (vector parity 152/152 pairs, mean top-5 overlap 0.955, same #1 in 92.1% of pairs, zero unexplained hybrid differences). The only failing condition is review: 4 full-text differences are recorded as `blocker`, all for the query `C#` (Q33). Postgres's parser drops the `#`, so the phrase becomes the bare letter `c` and matches 114 chunks that SQL Server does not; in the P2 scope the top-5 lists are fully disjoint. A custom tokenization or dictionary for Postgres would be the fix. The other 22 differences are recorded as `acceptable` (tokenization and punctuation). See [RESULTS.md](RESULTS.md) for the evidence.

## Prerequisites

- Docker Desktop. The SQL Server image is amd64-only, so it's emulated (and slow to start) on Apple Silicon.
- .NET 8 SDK (`global.json` pins 8.0.4xx).
- An embeddings key, **only** for `generate-data` and live queries. Either OpenAI: `export OPENAI_API_KEY=...`, or OpenRouter: `export OPENROUTER_API_KEY=...`. Alternatively use user secrets: `dotnet user-secrets set OpenAI:ApiKey <key> --project src/Parity.Cli`, plus optionally `OpenAI:Endpoint` and `OpenAI:Model` to point at another compatible endpoint or model.

## Run it

```bash
docker compose up -d                                         # SQL Server on 14333, Postgres on 54329
dotnet run --project src/Parity.Cli -- seed --all            # recreate and load all three profiles
dotnet run --project src/Parity.Cli -- search "conversation lock" --profile postgres --scope P1
dotnet run --project src/Parity.Cli -- compare --out RESULTS.md
dotnet test --filter "Category!=Integration"                 # fast unit tests
dotnet test                                                  # everything, including Testcontainers suites
docker compose down -v                                       # stop and remove the demo databases
```

`docker-compose.yml` starts the two demo databases (SQL Server and Postgres with pgvector) used by `seed`, `search` and `compare`. The integration tests start their own Testcontainers and don't need it.

`compare` exits `0` when the verdict is switchable, `1` when it isn't, and `2` on configuration, data or connection errors.

While a `blocker` is recorded in `known-differences.json`, the integration test `Rule2EveryTextAndErrorDifferenceIsReviewedAndAccepted` fails by design: it asserts every difference is reviewed and accepted. Everything else passes.

## How the verdict works

"Switchable" requires all five conditions, with thresholds fixed in `appsettings.json` before the first run:

1. Vector results match: same top 10, scores within 1e-5.
2. Every full-text match difference, and every error that happens on one side only, is reviewed in `known-differences.json` and marked `acceptable`.
3. Mean top-5 hybrid overlap is at least 0.9.
4. The same #1 result appears in at least 80% of query/scope pairs.
5. There are zero unexplained hybrid differences.

Two runs are informational and never affect the verdict: production's cross-project top-N quirk fixed on both engines, and the stock `english` Postgres configuration (with stemming).

## Reviewing differences

When `compare` reports an `UNREVIEWED` difference, read its tokenization evidence in `RESULTS.md`. Then add an entry to `known-differences.json`, copying the exact chunk IDs, with `decision` set to `acceptable` or `blocker`. If a later run's difference doesn't match the recorded IDs exactly, the entry shows as `SNAPSHOT CHANGED` and needs another look.

## Regenerating the data

```bash
dotnet run --project src/Parity.Cli -- generate-data   # costs a few cents; reads docs from git at 9f8cd102
```

Chunk IDs are deterministic, so the existing `known-differences.json` entries stay valid when the data is regenerated from the same commit.
