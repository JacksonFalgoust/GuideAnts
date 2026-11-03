# Review notes: the evidence behind the Q09, Q03 and Q33 reviews

This is the read-only investigation done before the corresponding entries in `known-differences.json` were written. It supports those entries; it is not itself part of the verdict.

Baseline = SQL Server, candidate = Postgres (config `parity_english_nostem`).
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
