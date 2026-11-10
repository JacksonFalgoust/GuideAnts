# scale-measure

This tool measures how the Postgres text score compares with SQL Server's on the production profiles (parity report
row #49). It is the evidence behind the decision not to rescale the score. It isn't part of any verdict.

## What it prints

1. **The ratio.** Postgres `ts_rank_cd(…, 32)` divided by SQL Server `RANK / 1000`, for every (query, chunk) both
   engines match in scopes P1 to P3.
2. **A rescale counterfactual.** The verdict metrics are rerun against `prod-sqlserver` with production's Postgres text
   score divided by each divisor k.
   - The Postgres SQL is read from `PostgresHybridSearcher.cs`, so it can't drift. The Postgres diagnostics are scaled
     the same way, and the official `ComparisonRunner` and `Verdict` do the comparing.
   - k = 1 is production. Its metrics must equal `RESULTS-production.md`.
   - It also counts "exact phrase in top 5": of the exact and edge query/scope pairs whose scope holds a ground-truth
     chunk, those whose top 5 holds one.
3. **Per query group at k = 1.** The overlap and the same-#1 rate.
4. **The pairs whose #1 differs at k = 1.** For each pair, whether each engine's #1 contains the searched phrase
   (`GroundTruth.PhraseRegex`) and is a ground-truth chunk.

## Run it

The production profiles must be seeded first:

```bash
docker compose up -d
dotnet run --project src/Parity.Cli -- seed --profile prod-sqlserver
dotnet run --project src/Parity.Cli -- seed --profile prod-postgres
dotnet run --project tools/scale-measure > tools/scale-measure/results/scale-measure.txt   # optional: a list of divisors, e.g. 1,2,4,5.68,8
```

The first divisor must be 1.

[`results/scale-measure.txt`](results/scale-measure.txt) is the output used in `REVIEW-NOTES.md` section E and in
parity report row #49. It was run on 2026-11-10 against freshly seeded databases.

SQL Server's `CONTAINSTABLE` ranks depend on the full-text index's internal state. They can differ slightly between
seedings of the same data, for example Q33's mean rank. The Postgres ranks and the verdict metrics have been identical
across runs.
