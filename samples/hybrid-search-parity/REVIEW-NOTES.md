# Review notes: the evidence behind the Q09, Q03 and Q33 reviews

This is the read-only investigation done before the corresponding entries in `known-differences.json` were written. It supports those entries; it is not itself part of the verdict. Sections A to C are about the sample's port (`sqlserver` vs `postgres`); D and E are about production search (`prod-sqlserver` vs `prod-postgres`).

Sections A to C: baseline = SQL Server, candidate = Postgres (config `parity_english_nostem`).
Method: `CONTAINSTABLE('"phrase"')` vs `"ContentTsv" @@ phraseto_tsquery(...)` run over the chunk ids from data/chunks.jsonl; `ts_debug` on the sentences.
Postgres queries: `phraseto_tsquery` -> `'guideants' <-> 'guide'` and `'host' <-> 'folder' <-> 'mounts'`.

## A. Q09 "GuideAnts Guide" (SQL-only: P1 developer-config-guide.md#24; P3 guideants-guide-implementation-plan.md#31, published-openai-wire-continuation-gap-report.md#9 and #10)

All four are matched by CONTAINSTABLE and missed by Postgres (verified by id). The cause is the Postgres parser's `file` token, which swallows slash/hyphen paths into one lexeme.

| Chunk | SQL Server matched on | Postgres tokenizes as | True phrase? |
|---|---|---|---|
| developer-config-guide#24 (b74615fd) | file names in prose: `docs/guideants-guide-implementation-plan.md`, `docs/guideants-guide/STATUS.md` | `file` tokens `docs/guideants-guide-implementation-plan.md`, `docs/guideants-guide/status.md` | No. Hyphen-in-path splitting. (The snippet tokens looked identical because the shown window did not contain these paths.) |
| guideants-guide-implementation-plan#31 (3d24c5ba) | paths: `.../bootstrap/guides/guideants-guide/**`, `guideants-guide-admin/**`, `features/guideantsGuide/GuideAntsGuideProvider.tsx` etc. | `file`: `bootstrap/guides/guideants-guide`; camelCase `GuideAntsGuideProvider.tsx` is a single file lexeme | Weak. The slug "guideants-guide" is the guide's real name, but these are path fragments, not prose. SQL's word breaker splits at "-" and "/". |
| published-openai-wire-continuation-gap-report#9 and #10 (8f8dc136, f4b8fc62) | prose: "Merge client-provided tools with GuideAnts guide/internal tools" | `guideants`:2 `guide/internal`:3. `guide/internal` is one `file` token | Yes, a genuine adjacent "GuideAnts guide", but followed by a slash alternation ("guide/internal"). Postgres misses it because `a/b` lexes as a file token. This is the only prose-level true positive. |

Evidence (ts_debug): `with GuideAnts guide/internal tools` -> asciiword GuideAnts, **file guide/internal**, asciiword tools. `see docs/guideants-guide-implementation-plan.md` -> **file** `docs/guideants-guide-implementation-plan.md` (one lexeme).
Not a stop-word gap, possessive or newline issue: the one other prose hit, "GuideAnts' internal guide", is not adjacent in either engine.
The overlapping chunks #9/#10 repeat the same sentence.

Category: tokenization (Postgres `file`-token lexing of `/` and `-` paths vs SQL Server's word breaker). Mostly SQL Server path-fragment matches; one real prose occurrence missed by Postgres.

## B. Q03 "host folder mounts" P1 (SQL-only: host-agent-and-desktop-runtime-proposal#0,10,11,13,14; project-root-host-folder-mounts#9,10)

Every one is a SQL Server match on a hyphenated/slashed file or URL, with no spaced prose "host folder mounts" anywhere in these chunks. Regex for `host\W{1,3}folder\W{1,3}mounts?` shows only these forms:

- `[host-folder-mounts.md](./host-folder-mounts.md)` (host-agent #0, #13, #14; project-root #9, #10)
- `../plans/project-root-host-folder-mounts.md` (project-root #10)
- `POST /host-folder-mounts (PendingRestart)` (host-agent #10, #11)
- Related plan check (P3): host-folder-notebook-mounts-plan#10 has only `/api/projects/{projectId}/host-folder-mounts...` URLs.

Postgres lexes these as `file` tokens (`docs/host-folder-mounts.md`, `/host-folder-mounts`, `plans/project-root-host-folder-mounts.md`) and so never emits `host`/`folder`/`mounts`. Only a bare `host-folder-mounts` (asciihword) gets split into `host`, `folder`, `mounts` parts (verified), and that does match.
Verdict: not true prose phrase occurrences; compound/path-splitting artifact (SQL Server over-matches, Postgres is stricter). These are arguably semantically relevant (links to the host-folder-mounts doc), but the match is on identifier text.
Category: tokenization/punctuation (hyphen and slash).

## C. Q33 "C#" ranking impact

Query tokens: SQL Server phrase "C#" (only 10 chunks matched table-wide, none in the P2 vector top-n) vs Postgres `phraseto_tsquery` = `'c'` (120 chunks table-wide match the single letter c). The `#` is dropped.

RESULTS.md (Rule 3 per-pair):

| Pair | Overlap@5 | Same #1 | Differences |
|---|---|---|---|
| P1 | 1.00 | yes | 0 |
| P2 | 0.00 | no | 10 |
| P3 | 1.00 | no | 5 |
| P1/N1 | 1.00 | yes | 0 |

- P2 diffs: all SQL top-5 (model-parameter-override-fix-plan#3,#2,#4; skills-support-proposal#12,#13) are displaced. All Postgres top-5 are client-navigation-user-actions#23,#20,#17,#19,#16 as **matched-one-side** (text 0.41/0.375/0.333/0.375/0.333 vs SQL 0).
- P3 diffs: same five codeql-local-solution-runbook chunks in both, reordered (#3 and #5 scored-differently, 0.046/0.050 vs 0.375; #4, #2, #6 top-n-boundary-tie). Same set, different order and #1 (#4 vs #3).
- P1 and P1/N1: Postgres has candidate-only text matches (listed in "Candidate only") but the top-5 is unchanged.

Q20 "llama.cpp" (for comparison): P1 1.00/yes/0; P2 0.80/yes/3 (setup-guide#20 displaced out; llama-router-preset-ui-proposal#11 top-n-cut in at #2; inputs/edge-cases.json#E02 #2 -> #5); P3 0.80/no/6 (chat-providers-architecture-and-extensibility#6 and #7 top-n-cut in at #1 and #3; adding-models#4, #1 and chat-providers#8 displaced); P1/N1 1.00/yes/0.

CLI hybrid search, scope P2, "C#" (score, vector, text):

SQL Server
```
1 0.2010 0.2872 0.0000 docs/model-parameter-override-fix-plan.md#3
2 0.1826 0.2608 0.0000 docs/skills-support-proposal.md#12
3 0.1754 0.2506 0.0000 docs/model-parameter-override-fix-plan.md#2
4 0.1711 0.2444 0.0000 docs/skills-support-proposal.md#13
5 0.1654 0.2362 0.0000 docs/model-parameter-override-fix-plan.md#4
```
Postgres
```
1 0.2377 0.1631 0.4118 docs/client-navigation-user-actions.md#23
2 0.2308 0.1690 0.3750 docs/client-navigation-user-actions.md#20
3 0.2279 0.1827 0.3333 docs/client-navigation-user-actions.md#17
4 0.2222 0.1567 0.3750 docs/client-navigation-user-actions.md#19
5 0.2141 0.1630 0.3333 docs/client-navigation-user-actions.md#16
```

Relevance to the C# language:
- Postgres top-5 are irrelevant. client-navigation-user-actions#23 and #20 contain markdown permission tables with column headers `| A | C | R |`, the single-letter "C" with no C# content (0 occurrences of "C#"). The text weight of 0.3 times 0.33-0.41 adds about 0.10-0.12, which outweighs a vector gap of about 0.12 and pushes these chunks to the top.
- SQL Server top-5 have text 0 and are ranked by vector alone: .NET/C# code and `.cs` paths (model-parameter-override-fix-plan#3 has about 23 `.cs`/.NET references; skills-support-proposal#12 is a C# snippet). They are more plausibly about C#, though only loosely so (code chunks, not "C# the language" docs).

Conclusion: the P2 top-5 are fully disjoint, and the difference is material. A 1-char token artifact (`#` dropped, 'c' matches column headers) distorts the Postgres top results even at text weight 0.3.
Cause: tokenization (punctuation `#` dropped and single-letter lexeme, not filtered as a stop word).

## D. Q33 "C#" on production search (`prod-postgres`, 2026-11-09)

Production's Postgres search normalizes the query and the indexed text with `guideants_fts_normalize`, so `phraseto_tsquery('guideants_english_nostem', guideants_fts_normalize('C#'))` is `'csharp'`. SQL Server's tokens for the query are unchanged (`c#`).

The first production run showed exactly one change from the port: Q33's four entries were `SNAPSHOT CHANGED`. Every other difference had the port's chunk IDs. The new Q33 difference:

| Scope | Baseline only | Candidate only | Overlap@5 | Same #1 | Hybrid differences |
|---|---|---|---|---|---|
| P1 | 0 | 4 | 1.00 | yes | 0 |
| P2 | 0 | 14 | 1.00 | yes | 0 |
| P3 | 0 | 16 | 1.00 | yes | 0 |
| P1/N1 | 0 | 4 (P1's) | 1.00 | yes | 0 |

- Postgres matches every chunk SQL Server matches (no baseline-only chunk), and the bare letter `c` matches nothing.
- Checked by ID against `data/chunks.jsonl`: each of the 34 Postgres-only chunks contains a ```` ```csharp ```` code fence and no standalone `C#`. All 41 occurrences of the word `csharp` in those chunks are fence language tags. So "C#" also finds chunks of C# code, because the tag is the lexeme `C#` is rewritten to. SQL Server keeps `c#` and `csharp` as different tokens.
- The P2 top 5 is identical on both providers (the `.NET` code chunks, text score 0 on both). In the port it was fully disjoint.

Decision: `acceptable` (tokenization), recorded with `"candidate": "prod-postgres"`. A user searching "C#" gets C# content on both engines, and the ranking is the same in every scope. The port's four blockers stay, with `"candidate": "postgres"`, because the port still has the bare-letter behaviour.

## E. The Postgres text score scale (production search, 2026-11-09, re-measured 2026-11-10)

Parity report row #49 asked whether the Postgres text score needs rescaling. `ts_rank_cd(…, 32)` is `r / (r + 1)`, where `r` is the cover density with no length normalization: 0.091 for one occurrence of a one-word phrase, 0.375 for six. SQL Server's `RANK / 1000` is lower on prose chunks and depends on length.

Every number below comes from [`tools/scale-measure`](tools/scale-measure/README.md). Its committed output is [`tools/scale-measure/results/scale-measure.txt`](tools/scale-measure/results/scale-measure.txt), run on freshly seeded `prod-sqlserver` and `prod-postgres` databases.

**Ratio.** Postgres text score / SQL Server text score, for every (query, chunk) that both engines match in scopes P1 to P3 (`TextMatchAsync` on both production profiles): 376 pairs over 25 queries.

| min | p10 | p25 | median | p75 | p90 | max | geometric mean |
|---|---|---|---|---|---|---|---|
| 0.18 | 3.13 | 4.78 | 5.68 | 6.49 | 6.49 | 23.81 | 4.80 |

- On prose chunks the ratio is 4 to 7 (for example Q01: SQL Server 0.0156, Postgres 0.0909).
- On the short edge-case chunks it is reversed: SQL Server gives 0.176 and Postgres 0.032 to 0.091 (Q21 to Q24, Q28, Q31, Q32), because `CONTAINSTABLE` ranks a short document higher. So no single divisor maps one scale onto the other.
- **SQL Server's ranks are not exactly repeatable across seedings.** In the first measurement (2026-11-09, an earlier seeding of the same data) Q33's mean SQL Server rank was 0.0450, against 0.0509 now, and the maximum ratio was 28.57. Every other query's numbers, and all Postgres ranks, were identical. `CONTAINSTABLE`'s RANK depends on the full-text index's internal state. The verdict metrics were the same in both runs.

**Counterfactual.** The comparison was rerun with the official `ComparisonRunner` and `Verdict`, against `prod-sqlserver`. The candidate was production's Postgres SQL (read from `PostgresHybridSearcher.cs`) with the text score divided by `k`. The Postgres diagnostics' ranks were divided by the same `k`, so the explanations stay consistent. `k = 1` reproduces the real run.

"Exact phrase in top 5" counts the 51 query/scope pairs of exact and edge queries whose scope holds at least one ground-truth chunk (a whole-word, case-insensitive phrase match, `data/ground-truth.json`). It counts those whose top 5 includes one.

| k | Mean top-5 overlap | Same #1 | Unexplained | Exact phrase in top 5: SQL Server | Exact phrase in top 5: Postgres |
|---|---|---|---|---|---|
| 1 (production) | 0.962 | 93.4% | 0 | 41/51 | 45/51 |
| 2 | 0.976 | 99.3% | 0 | 41/51 | 44/51 |
| 4 | 0.991 | 99.3% | 0 | 41/51 | 42/51 |
| 5.68 (median ratio) | 0.995 | 99.3% | 0 | 41/51 | 41/51 |
| 8 | 0.993 | 98.7% | 0 | 41/51 | 40/51 |

**Per query group at k = 1.** The aggregate hides how close the exact-phrase group is:

| Group | Pairs | Mean top-5 overlap | Same #1 |
|---|---|---|---|
| exact | 40 | 0.910 | 80.0% (at the 80% threshold) |
| paraphrase | 32 | 1.000 | 100% |
| edge | 64 | 0.972 | 98.4% |
| error | 8 | 1.000 | 100% |
| stress | 8 | 0.950 | 87.5% |

**The 10 pairs whose #1 differs at k = 1.** "Contains the phrase" uses the ground-truth regex (`GroundTruth.PhraseRegex`: the words in order, separated only by whitespace).

| Pair | SQL Server's #1 contains the phrase | Postgres's #1 contains the phrase |
|---|---|---|
| Q01 · P2, Q01 · P1/N1, Q03 · P3, Q05 · P2, Q06 · P1/N1, Q07 · P2, Q20 · P3 | no | yes (a ground-truth chunk) |
| Q37 · P1 | no (E13, "published, guide-adjacent", the punctuation edge case) | yes (stress query, no ground truth recorded) |
| Q07 · P3, Q09 · P3 | yes | yes |

Each engine's #1 is also in the other's top 5 (at rank 2 to 5), except in Q01 · P2 and Q07 · P3: there Postgres's #1 isn't in SQL Server's top 5, and SQL Server's #1 is Postgres's #4 and #2.

Decision: **keep the score as it is.**
- The scale is the main remaining source of ranking differences: rescaling would make Postgres rank almost exactly like SQL Server.
- The verdict passes in aggregate without it (0.962 against 0.9, 93.4% against 80%). But the exact-phrase group sits at the threshold (80.0% same #1), so this is not a wide margin. 8 of its 40 pairs have a different #1, and rescaling removes almost all such differences (99.3% same #1 overall for k = 2 to 5.68).
- In 8 of the 10 pairs whose #1 differs, Postgres puts a chunk containing the searched phrase first and SQL Server puts one that doesn't. In the other 2 both do, and in none is it the other way round.
- A rescale would give up 4 of the 51 exact-phrase finds to match SQL Server's weaker text signal. Under the functional bar (a user finds what they search for), that isn't an improvement.
- A constant divisor would also mis-scale short chunks (above).
- If matching SQL Server's order matters more than exact-phrase recall, the change is one line in `PostgresHybridSearcher` (`text_score / 5.68`), plus the same in the parity diagnostics.
