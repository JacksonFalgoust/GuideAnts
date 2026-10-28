# Hybrid search parity results

**Verdict: NOT SWITCHABLE**. `sqlserver` (baseline) vs `postgres` (candidate), 152 query/scope pairs, run 2026-10-28 00:56 UTC.

| Condition | Result | Value |
|---|---|---|
| Vector parity | PASS | 152/152 pairs |
| Full-text and error differences reviewed and accepted | FAIL | 0 unreviewed, 0 snapshot-changed, 4 blocker |
| Mean top-5 overlap ≥ 0.9 | PASS | 0\.955 |
| Same \#1 ≥ 80% | PASS | 92\.1% |
| Zero unexplained differences | PASS | 0 |

## Environment

- Sample commit: `ef720af6`
- known-differences.json: SHA-256 `981ba747d6dec26e803097c5cf15da971812cc19c54a2553127460d3017af653`
- Data files (SHA-256 from data/manifest.json):
  - `data/chunks.jsonl`: `0db7380eaa975ae042982c4cbe28955ad4065ba55382f87bca1cd18bd993be42`
  - `data/embeddings.f32`: `389e1cb98e501722cda35de1e6b9b2d709e3411c8db9e340b680911cf9df5c04`
  - `data/ground-truth.json`: `c3cb44a7827916e205ca7210c13ca0a80d51bdcadf77f27fb330742341ef0ea3`
  - `data/query-embeddings.f32`: `ead7c3bd1b32de5aacfa3c2b08f4b9441d0eabbcab3ef76ae874a7e5e664973e`
  - `inputs/edge-cases.json`: `ac1d89d3118384a4c16d31363eda1a5f2877e1e07027f6c0794fba936236b376`
  - `inputs/queries.json`: `211ecfab695052699635a5ee34faee4cf8fceb65481b451e6cc6d554518a98f4`
- Data: corpus `9f8cd102`, model `openai/text-embedding-3-small`, 1210 chunks, 38 queries, generated 2026-10-27
- Thresholds: mean top-5 overlap ≥ 0.9, same #1 ≥ 80%; vector tolerance 1E-5, tie tolerance 1E-6
- Baseline (SqlServer): Microsoft SQL Server 2025 (RTM-CU7) (KB5096981) - 17.0.4065.4 (X64)
  - accentSensitive: `yes`
  - compatibilityLevel: `170`
  - fullTextLanguage: `1033`
  - stoplist: `system`
- Candidate (Postgres): PostgreSQL 18.6 (Debian 18.6-1.pgdg12+2) on aarch64-unknown-linux-gnu, compiled by gcc (Debian 12.2.0-14+deb12u1) 12.2.0, 64-bit
  - pgvector: `0.8.7`
  - rankFunction: `ts_rank_cd(…, 32)`
  - serverVersion: `18.6 (Debian 18.6-1.pgdg12+2)`
  - textSearchConfig: `parity_english_nostem`
- Images: SQL Server `ghcr.io/elumenotion/mssql2025-express-fts:main` → ["ghcr.io/elumenotion/mssql2025-express-fts@sha256:8a2b653da64921c9ee00003df1947863d219addb30c5fba791ec4895a8f3d620"]; Postgres `pgvector/pgvector:0.8.7-pg18` → ["pgvector/pgvector@sha256:2358fcba361ed2233a5ed81b5fe4ca779ccb304120ce531a3bf51c0ed7e2bc11"]

## Rule 1: vector parity

152/152 pairs pass.

## Rule 2: full-text match parity

26 pairs differ; 4 are open. Stop words are shown ~~struck through~~.

### `Q02` · P1: "router preset"

Status: **acceptable** (tokenization: SQL Server's word breaker also indexes the parts of hyphen, dot and slash compounds (file names and paths such as \`host-folder-mounts.md\`, \`SKILL.md\`, \`docs/notebook-header-service-toolbar-spec.md\`) w…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/model-chat-behavior-contract.md#12`, `docs/model-chat-behavior-contract.md#11`
- Candidate only: none
- Query tokens: baseline `router` | `preset`; candidate `router` | `preset`
- baseline-only `docs/model-chat-behavior-contract.md#12`: "ator walkthrough: \[adding-models.md\](adding-models.md) - Local llama lifecycle (router, load/unload — not chat authority): \[llama-model-download-and-runtime-man"
  - baseline tokens `ator` | `walkthrough` | `adding-models.md` | `adding` | `models` | `md` | `adding-models.md` | `adding` | `models` | `md` | `local` | `llama` | `lifecycle` | `router` | `load` | `unload` | `not` | `chat` | `authority` | `llama-model-download-and-runtime-man` | `llama` | `model` | `download` | ~~`and`~~ | `runtime` | `man`; candidate tokens `ator` | `walkthrough` | `adding-models.md` | `adding-models.md` | `local` | `llama` | `lifecycle` | `router` | `load/unload` | ~~`not`~~ | `chat` | `authority` | `llama-model-download-and-runtime-man` | `llama` | `model` | `download` | ~~`and`~~ | `runtime` | `man`
- baseline-only `docs/model-chat-behavior-contract.md#11`: "ator walkthrough: \[adding-models.md\](adding-models.md) - Local llama lifecycle (router, load/unload — not chat authority): \[llama-model-download-and-runtime-man"
  - baseline tokens `ator` | `walkthrough` | `adding-models.md` | `adding` | `models` | `md` | `adding-models.md` | `adding` | `models` | `md` | `local` | `llama` | `lifecycle` | `router` | `load` | `unload` | `not` | `chat` | `authority` | `llama-model-download-and-runtime-man` | `llama` | `model` | `download` | ~~`and`~~ | `runtime` | `man`; candidate tokens `ator` | `walkthrough` | `adding-models.md` | `adding-models.md` | `local` | `llama` | `lifecycle` | `router` | `load/unload` | ~~`not`~~ | `chat` | `authority` | `llama-model-download-and-runtime-man` | `llama` | `model` | `download` | ~~`and`~~ | `runtime` | `man`

### `Q02` · P2: "router preset"

Status: **acceptable** (tokenization: SQL Server's word breaker also indexes the parts of hyphen, dot and slash compounds (file names and paths such as \`host-folder-mounts.md\`, \`SKILL.md\`, \`docs/notebook-header-service-toolbar-spec.md\`) w…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/llama-router-preset-ui-proposal.md#0`
- Candidate only: none
- Query tokens: baseline `router` | `preset`; candidate `router` | `preset`
- baseline-only `docs/llama-router-preset-ui-proposal.md#0`: "et llama server” panel and no \> \`/api/settings/llama/runtime/fleet-preset\` API. Router-process switches such as \`parallel\`, \> \`threads\`, and \`jinja\` are configu"
  - baseline tokens `et` | `llama` | `server` | `panel` | ~~`and`~~ | ~~`no`~~ | `api` | `settings` | `llama` | `runtime` | `fleet-preset` | `fleet` | `preset` | `api` | `END OF FILE` | `router-process` | `router` | `process` | `switches` | ~~`such`~~ | ~~`as`~~ | `parallel` | `threads` | ~~`and`~~ | `jinja` | ~~`are`~~ | `configu`; candidate tokens `et` | `llama` | `server` | `panel` | ~~`and`~~ | ~~`no`~~ | `/api/settings/llama/runtime/fleet-preset` | `api` | `router-process` | `router` | `process` | `switches` | ~~`such`~~ | ~~`as`~~ | `parallel` | `threads` | ~~`and`~~ | `jinja` | ~~`are`~~ | `configu`

### `Q03` · P1: "host folder mounts"

Status: **acceptable** (tokenization: Verified false positives: every SQL-Server-only chunk matches only inside \`host-folder-mounts.md\` links or the \`/host-folder-mounts\` URL; none contains the spaced prose phrase (investigation task-19-i…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/host-agent-and-desktop-runtime-proposal.md#10`, `docs/host-agent-and-desktop-runtime-proposal.md#0`, `docs/project-root-host-folder-mounts.md#10`, `docs/host-agent-and-desktop-runtime-proposal.md#11`, `docs/host-agent-and-desktop-runtime-proposal.md#13`, `docs/host-agent-and-desktop-runtime-proposal.md#14`, `docs/project-root-host-folder-mounts.md#9`
- Candidate only: none
- Query tokens: baseline `host` | `folder` | `mounts`; candidate `host` | `folder` | `mounts`
- baseline-only `docs/host-agent-and-desktop-runtime-proposal.md#10`: "ime\_\_ComposeFile\`, \`\_\_HostMountOverrideFile\`, etc.) stay unchanged — see \[\`host-folder-notebook-mounts-plan.md\` §5\](./host-folder-notebook-mounts-plan.md). ---"
  - baseline tokens `ime__composefile` | `ime` | `composefile` | `hostmountoverridefile` | `etc` | `stay` | `unchanged` | ~~`see`~~ | `host-folder-notebook-mounts-plan.md` | `host` | `folder` | `notebook` | `mounts` | `plan` | `md` | ~~`5`~~ | ~~`nn5`~~ | `host-folder-notebook-mounts-plan.md` | `host` | `folder` | `notebook` | `mounts` | `plan` | `md`; candidate tokens `ime` | `composefile` | `hostmountoverridefile` | `etc` | `stay` | `unchanged` | `see` | `host-folder-notebook-mounts-plan.md` | `5` | `/host-folder-notebook-mounts-plan.md`
- baseline-only `docs/host-agent-and-desktop-runtime-proposal.md#0`: "generated overrides, env, scoped service restarts). It extends the shipped host-folder mount helpers with automatic apply/remove and optional Electron integrati"
  - baseline tokens `generated` | `overrides` | `env` | `scoped` | `service` | `restarts` | `END OF FILE` | ~~`it`~~ | `extends` | ~~`the`~~ | `shipped` | `host-folder` | `host` | `folder` | `mount` | `helpers` | ~~`with`~~ | `automatic` | `apply` | `remove` | ~~`and`~~ | `optional` | `electron` | `integrati`; candidate tokens `generated` | `overrides` | `env` | `scoped` | `service` | `restarts` | ~~`It`~~ | `extends` | ~~`the`~~ | `shipped` | `host-folder` | `host` | `folder` | `mount` | `helpers` | ~~`with`~~ | `automatic` | `apply/remove` | ~~`and`~~ | `optional` | `electron` | `integrati`
- baseline-only `docs/project-root-host-folder-mounts.md#10`: "ted directory scanner (new) \| \| \`Services/Components/ProjectFolderService.cs\` \| Project tree overlay with mount nodes \| \| \`Services/Components/NotebookFileServi"
  - baseline tokens `ted` | `directory` | `scanner` | `new` | `services/components/projectfolderservice.cs` | `services` | `components` | `projectfolderservice` | `cs` | `project` | `tree` | `overlay` | ~~`with`~~ | `mount` | `nodes` | `services` | `components` | `notebookfileservi`; candidate tokens `ted` | `directory` | `scanner` | `new` | `services/components/projectfolderservice.cs` | `project` | `tree` | `overlay` | ~~`with`~~ | `mount` | `nodes` | `services/components/notebookfileservi`
- baseline-only `docs/host-agent-and-desktop-runtime-proposal.md#11`: "ime\_\_ComposeFile\`, \`\_\_HostMountOverrideFile\`, etc.) stay unchanged — see \[\`host-folder-notebook-mounts-plan.md\` §5\](./host-folder-notebook-mounts-plan.md). ---"
  - baseline tokens `ime__composefile` | `ime` | `composefile` | `hostmountoverridefile` | `etc` | `stay` | `unchanged` | ~~`see`~~ | `host-folder-notebook-mounts-plan.md` | `host` | `folder` | `notebook` | `mounts` | `plan` | `md` | ~~`5`~~ | ~~`nn5`~~ | `host-folder-notebook-mounts-plan.md` | `host` | `folder` | `notebook` | `mounts` | `plan` | `md`; candidate tokens `ime` | `composefile` | `hostmountoverridefile` | `etc` | `stay` | `unchanged` | `see` | `host-folder-notebook-mounts-plan.md` | `5` | `/host-folder-notebook-mounts-plan.md`
- baseline-only `docs/host-agent-and-desktop-runtime-proposal.md#13`: "\# 12. Related documents \| Document \| Purpose \| \|----------\|---------\| \| \[\`host-folder-mounts.md\`\](./host-folder-mounts.md) \| Shipped operator runbook (manual c"
  - baseline tokens `12` | `nn12` | `END OF FILE` | `related` | `documents` | `document` | `purpose` | `host-folder-mounts.md` | `host` | `folder` | `mounts` | `md` | `host-folder-mounts.md` | `host` | `folder` | `mounts` | `md` | `shipped` | `operator` | `runbook` | `manual` | ~~`c`~~; candidate tokens `12` | `related` | `documents` | `document` | `purpose` | `host-folder-mounts.md` | `/host-folder-mounts.md` | `shipped` | `operator` | `runbook` | `manual` | `c`

### `Q03` · P2: "host folder mounts"

Status: **acceptable** (tokenization: Verified false positives: every SQL-Server-only chunk matches only inside \`host-folder-mounts.md\` links or the \`/host-folder-mounts\` URL; none contains the spaced prose phrase (investigation task-19-i…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/secure-cli-auth.md#12`, `docs/secure-cli-auth.md#3`, `docs/host-folder-mounts.md#4`, `docs/secure-cli-auth.md#13`, `docs/host-folder-mounts.md#8`, `docs/host-folder-mounts.md#3`, `docs/secure-cli-auth.md#2`, `docs/host-folder-mounts.md#7`
- Candidate only: none
- Query tokens: baseline `host` | `folder` | `mounts`; candidate `host` | `folder` | `mounts`
- baseline-only `docs/secure-cli-auth.md#12`: "i-auth-device-code-flow.md) — the design plan this feature implements. - \[\`host-folder-mounts.md\`\](./host-folder-mounts.md) — the host folder mount operator/adm"
  - baseline tokens `i-auth-device-code-flow` | ~~`i`~~ | `auth` | `device` | `code` | `flow` | `md` | ~~`the`~~ | `design` | `plan` | ~~`this`~~ | `feature` | `implements` | `END OF FILE` | `host-folder-mounts.md` | `host` | `folder` | `mounts` | `md` | `host-folder-mounts.md` | `host` | `folder` | `mounts` | `md` | ~~`the`~~ | `host` | `folder` | `mount` | `operator` | `adm`; candidate tokens `i-auth-device-code-flow.md` | ~~`the`~~ | `design` | `plan` | ~~`this`~~ | `feature` | `implements` | `host-folder-mounts.md` | `/host-folder-mounts.md` | ~~`the`~~ | `host` | `folder` | `mount` | `operator/adm`
- baseline-only `docs/secure-cli-auth.md#3`: "short-lived JWT, mark Consumed use token IN MEMORY for projects/notebooks/host-folder-mounts run scripts/guideants-host-mount.sh apply (token discarded on e"
  - baseline tokens `short-lived` | `short` | `lived` | `jwt` | `mark` | `consumed` | ~~`use`~~ | `token` | ~~`in`~~ | `memory` | ~~`for`~~ | `projects` | `notebooks` | `host-folder-mounts` | `host` | `folder` | `mounts` | `run` | `scripts/guideants-host-mount.sh` | `scripts` | `guideants` | `host` | `mount` | `sh` | `apply` | `token` | `discarded` | ~~`on`~~ | ~~`e`~~; candidate tokens `short-lived` | `short` | `lived` | `jwt` | `mark` | `consumed` | `use` | `token` | ~~`IN`~~ | `memory` | ~~`for`~~ | `projects/notebooks/host-folder-mounts` | `run` | `scripts/guideants-host-mount.sh` | `apply` | `token` | `discarded` | ~~`on`~~ | `e`
- baseline-only `docs/host-folder-mounts.md#4`: "elper scripts also bind-mount host folders into \*\*\`guideants-webapi-ui\`\*\*, \*\*\`guideants-ai\`\*\*, and \*\*\`plantuml\`\*\*, and restart only those services after apply/r"
  - baseline tokens `elper` | `scripts` | ~~`also`~~ | `bind-mount` | `bind` | `mount` | `host` | `folders` | ~~`into`~~ | `guideants-webapi-ui` | `guideants` | `webapi` | `ui` | `guideants-ai` | `guideants` | `ai` | ~~`and`~~ | `plantuml` | ~~`and`~~ | `restart` | ~~`only`~~ | ~~`those`~~ | `services` | ~~`after`~~ | `apply` | ~~`r`~~; candidate tokens `elper` | `scripts` | `also` | `bind-mount` | `bind` | `mount` | `host` | `folders` | ~~`into`~~ | `guideants-webapi-ui` | `guideants` | `webapi` | `ui` | `guideants-ai` | `guideants` | `ai` | ~~`and`~~ | `plantuml` | ~~`and`~~ | `restart` | ~~`only`~~ | ~~`those`~~ | `services` | ~~`after`~~ | `apply/r`
- baseline-only `docs/secure-cli-auth.md#13`: "i-auth-device-code-flow.md) — the design plan this feature implements. - \[\`host-folder-mounts.md\`\](./host-folder-mounts.md) — the host folder mount operator/adm"
  - baseline tokens `i-auth-device-code-flow` | ~~`i`~~ | `auth` | `device` | `code` | `flow` | `md` | ~~`the`~~ | `design` | `plan` | ~~`this`~~ | `feature` | `implements` | `END OF FILE` | `host-folder-mounts.md` | `host` | `folder` | `mounts` | `md` | `host-folder-mounts.md` | `host` | `folder` | `mounts` | `md` | ~~`the`~~ | `host` | `folder` | `mount` | `operator` | `adm`; candidate tokens `i-auth-device-code-flow.md` | ~~`the`~~ | `design` | `plan` | ~~`this`~~ | `feature` | `implements` | `host-folder-mounts.md` | `/host-folder-mounts.md` | ~~`the`~~ | `host` | `folder` | `mount` | `operator/adm`
- baseline-only `docs/host-folder-mounts.md#8`: "d path Confirm \`.guideants/mounts.json\` exists under the notebook \`.guideants\` folder, lists the leaf with the correct \`containerSourcePath\`, and that \`writabl"
  - baseline tokens ~~`d`~~ | `path` | `confirm` | `.guideants/mounts.json` | `guideants` | `mounts` | `json` | `exists` | ~~`under`~~ | ~~`the`~~ | `notebook` | `guideants` | `folder` | `lists` | ~~`the`~~ | `leaf` | ~~`with`~~ | ~~`the`~~ | `correct` | `containersourcepath` | ~~`and`~~ | ~~`that`~~ | `writabl`; candidate tokens `d` | `path` | `confirm` | `guideants/mounts.json` | `exists` | ~~`under`~~ | ~~`the`~~ | `notebook` | `guideants` | `folder` | `lists` | ~~`the`~~ | `leaf` | ~~`with`~~ | ~~`the`~~ | `correct` | `containersourcepath` | ~~`and`~~ | ~~`that`~~ | `writabl`

### `Q03` · P3: "host folder mounts"

Status: **acceptable** (tokenization: Verified false positives: every SQL-Server-only chunk matches only inside \`host-folder-mounts.md\` links or the \`/host-folder-mounts\` URL; none contains the spaced prose phrase (investigation task-19-i…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/host-folder-notebook-mounts-plan.md#11`, `docs/host-folder-notebook-mounts-plan.md#12`, `docs/host-folder-notebook-mounts-plan.md#10`
- Candidate only: none
- Query tokens: baseline `host` | `folder` | `mounts`; candidate `host` | `folder` | `mounts`
- baseline-only `docs/host-folder-notebook-mounts-plan.md#11`: "Endpoints Admin-only endpoints: \`\`\`text GET /api/projects/{projectId}/host-folder-mounts POST /api/projects/{projectId}/host-folder-mounts GET /api/pr"
  - baseline tokens `endpoints` | `admin-only` | `admin` | ~~`only`~~ | `endpoints` | `text` | ~~`get`~~ | `api` | `projects` | `projectid` | `host-folder-mounts` | `host` | `folder` | `mounts` | `post` | `api` | `projects` | `projectid` | `host-folder-mounts` | `host` | `folder` | `mounts` | ~~`get`~~ | `api` | `pr`; candidate tokens `endpoints` | `admin-only` | `admin` | ~~`only`~~ | `endpoints` | `text` | `get` | `/api/projects` | `projectid` | `/host-folder-mounts` | `post` | `/api/projects` | `projectid` | `/host-folder-mounts` | `get` | `/api/pr`
- baseline-only `docs/host-folder-notebook-mounts-plan.md#12`: "/{projectId}/host-folder-mounts/{mountId} \`\`\` Request for create: \`\`\`json { "notebookId": "optional-guid", "scope": "Project", "hostPath": "D:\\\\Data\\\\Sh"
  - baseline tokens `projectid` | `host-folder-mounts` | `host` | `folder` | `mounts` | `mountid` | `request` | ~~`for`~~ | `create` | `json` | `notebookid` | `optional-guid` | `optional` | `guid` | `scope` | `project` | `hostpath` | `d:\\data\\sh` | ~~`d`~~ | `data` | `sh`; candidate tokens `projectid` | `/host-folder-mounts` | `mountid` | `request` | ~~`for`~~ | `create` | `json` | `notebookid` | `optional-guid` | `optional` | `guid` | `scope` | `project` | `hostpath` | `d` | `data` | `sh`
- baseline-only `docs/host-folder-notebook-mounts-plan.md#10`: "rs - are \`.\` or \`..\` - contain null characters - collide with reserved notebook folders - collide with an existing normal file or folder in any target notebook"
  - baseline tokens `rs` | ~~`are`~~ | ~~`or`~~ | `contain` | `null` | `characters` | `collide` | ~~`with`~~ | `reserved` | `notebook` | `folders` | `collide` | ~~`with`~~ | ~~`an`~~ | `existing` | `normal` | `file` | ~~`or`~~ | `folder` | ~~`in`~~ | ~~`any`~~ | `target` | `notebook`; candidate tokens `rs` | ~~`are`~~ | ~~`or`~~ | `contain` | `null` | `characters` | `collide` | ~~`with`~~ | `reserved` | `notebook` | `folders` | `collide` | ~~`with`~~ | ~~`an`~~ | `existing` | `normal` | `file` | ~~`or`~~ | `folder` | ~~`in`~~ | ~~`any`~~ | `target` | `notebook`

### `Q03` · P1/N1: "host folder mounts"

Status: **acceptable** (tokenization: Verified false positives: every SQL-Server-only chunk matches only inside \`host-folder-mounts.md\` links or the \`/host-folder-mounts\` URL; none contains the spaced prose phrase (investigation task-19-i…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/project-root-host-folder-mounts.md#10`, `docs/project-root-host-folder-mounts.md#9`
- Candidate only: none
- Query tokens: baseline `host` | `folder` | `mounts`; candidate `host` | `folder` | `mounts`
- baseline-only `docs/project-root-host-folder-mounts.md#10`: "ted directory scanner (new) \| \| \`Services/Components/ProjectFolderService.cs\` \| Project tree overlay with mount nodes \| \| \`Services/Components/NotebookFileServi"
  - baseline tokens `ted` | `directory` | `scanner` | `new` | `services/components/projectfolderservice.cs` | `services` | `components` | `projectfolderservice` | `cs` | `project` | `tree` | `overlay` | ~~`with`~~ | `mount` | `nodes` | `services` | `components` | `notebookfileservi`; candidate tokens `ted` | `directory` | `scanner` | `new` | `services/components/projectfolderservice.cs` | `project` | `tree` | `overlay` | ~~`with`~~ | `mount` | `nodes` | `services/components/notebookfileservi`
- baseline-only `docs/project-root-host-folder-mounts.md#9`: "tCommandDialog\`, \`HostMountStateBadge\`, \`hostMountDisplayState\` utilities, \`hostFolderMountsApi\` service. \#\#\# Installer CLI \`installer/guideants.sh\` relabels"
  - baseline tokens `tcommanddialog` | `hostmountstatebadge` | `hostmountdisplaystate` | `utilities` | `hostfoldermountsapi` | `service` | `END OF FILE` | `installer` | `cli` | `installer/guideants.sh` | `installer` | `guideants` | `sh` | `relabels`; candidate tokens `tcommanddialog` | `hostmountstatebadge` | `hostmountdisplaystate` | `utilities` | `hostfoldermountsapi` | `service` | `installer` | `cli` | `installer/guideants.sh` | `relabels`

### `Q04` · P2: "script execution agent"

Status: **acceptable** (tokenization: SQL Server's word breaker also indexes the parts of hyphen, dot and slash compounds (file names and paths such as \`host-folder-mounts.md\`, \`SKILL.md\`, \`docs/notebook-header-service-toolbar-spec.md\`) w…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/mcp-tool-execution-design.md#0`
- Candidate only: none
- Query tokens: baseline `script` | `execution` | `agent`; candidate `script` | `execution` | `agent`
- baseline-only `docs/mcp-tool-execution-design.md#0`: "\# MCP Tool Execution — Design (draft) Last updated: 2026-06-29 Status: \*\*DRAFT — iterate here before implementation\*\* Related: - \[\`mcp-tool-execution/00-or"
  - baseline tokens `mcp` | `tool` | `execution` | `design` | `draft` | `last` | `updated` | `2026-06-29` | `dd20260629` | `2026` | `06` | `29` | `status` | `draft` | `iterate` | ~~`here`~~ | ~~`before`~~ | `implementation` | `related` | `mcp-tool-execution` | `mcp` | `tool` | `execution` | `00-or` | `00` | ~~`nn0`~~ | ~~`or`~~; candidate tokens `mcp` | `tool` | `execution` | `design` | `draft` | `last` | `updated` | `2026` | `-06` | `-29` | `status` | `draft` | `iterate` | ~~`here`~~ | ~~`before`~~ | `implementation` | `related` | `mcp-tool-execution` | `mcp` | `tool` | `execution` | `/00-or`

### `Q05` · P2: "tool call limits"

Status: **acceptable** (tokenization: SQL Server's word breaker also indexes the parts of hyphen, dot and slash compounds (file names and paths such as \`host-folder-mounts.md\`, \`SKILL.md\`, \`docs/notebook-header-service-toolbar-spec.md\`) w…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/conversation-stream-reconnect-and-cancel-proposal.md#1`, `docs/conversation-stream-reconnect-and-cancel-proposal.md#2`
- Candidate only: none
- Query tokens: baseline `tool` | `call` | `limits`; candidate `tool` | `call` | `limits`
- baseline-only `docs/conversation-stream-reconnect-and-cancel-proposal.md#1`: "etry instructions + repeated tool rounds; no per-assistant tool cap (\[tool-call-limits-proposal.md\](./tool-call-limits-proposal.md)) \| \#\# 3. Root Cause Analysi"
  - baseline tokens `etry` | `instructions` | `repeated` | `tool` | `rounds` | ~~`no`~~ | `per-assistant` | `per` | `assistant` | `tool` | `cap` | `tool-call-limits-proposal.md` | `tool` | `call` | `limits` | `proposal` | `md` | `tool-call-limits-proposal.md` | `tool` | `call` | `limits` | `proposal` | `md` | ~~`3`~~ | ~~`nn3`~~ | `END OF FILE` | `root` | `cause` | `analysi`; candidate tokens `etry` | `instructions` | `repeated` | `tool` | `rounds` | ~~`no`~~ | `per-assistant` | `per` | `assistant` | `tool` | `cap` | `tool-call-limits-proposal.md` | `/tool-call-limits-proposal.md` | `3` | `root` | `cause` | `analysi`
- baseline-only `docs/conversation-stream-reconnect-and-cancel-proposal.md#2`: "etry instructions + repeated tool rounds; no per-assistant tool cap (\[tool-call-limits-proposal.md\](./tool-call-limits-proposal.md)) \| \#\# 3. Root Cause Analysi"
  - baseline tokens `etry` | `instructions` | `repeated` | `tool` | `rounds` | ~~`no`~~ | `per-assistant` | `per` | `assistant` | `tool` | `cap` | `tool-call-limits-proposal.md` | `tool` | `call` | `limits` | `proposal` | `md` | `tool-call-limits-proposal.md` | `tool` | `call` | `limits` | `proposal` | `md` | ~~`3`~~ | ~~`nn3`~~ | `END OF FILE` | `root` | `cause` | `analysi`; candidate tokens `etry` | `instructions` | `repeated` | `tool` | `rounds` | ~~`no`~~ | `per-assistant` | `per` | `assistant` | `tool` | `cap` | `tool-call-limits-proposal.md` | `/tool-call-limits-proposal.md` | `3` | `root` | `cause` | `analysi`

### `Q07` · P1: "Docker Desktop"

Status: **acceptable** (punctuation: Apostrophe: \`Desktop's\` matches only in Postgres in P1; \`wsl/docker\` is one token in Postgres in P2. Mixed direction, small counts, bounded by the 0.3 text weight.) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: none
- Candidate only: `docs/developer-config-guide.md#1`
- Query tokens: baseline `docker` | `desktop`; candidate `docker` | `desktop`
- candidate-only `docs/developer-config-guide.md#1`: "t build\` / \`dotnet run\`. \#\# Windows-only host requirement - \*\*WSL2\*\* — Docker Desktop's default backend on Windows. Verify with \`wsl --status\` (the Windows la"
  - baseline tokens ~~`t`~~ | `build` | `dotnet` | `run` | `END OF FILE` | `windows-only` | `windows` | ~~`only`~~ | `host` | `requirement` | `wsl2` | `docker` | `desktop's` | `default` | `backend` | ~~`on`~~ | `windows` | `END OF FILE` | `verify` | ~~`with`~~ | `wsl` | `status` | ~~`the`~~ | `windows` | `la`; candidate tokens ~~`t`~~ | `build` | `dotnet` | `run` | `windows-only` | `windows` | ~~`only`~~ | `host` | `requirement` | `wsl2` | `docker` | `desktop` | ~~`s`~~ | `default` | `backend` | ~~`on`~~ | `windows` | `verify` | ~~`with`~~ | `wsl` | `status` | ~~`the`~~ | `windows` | `la`

### `Q07` · P2: "Docker Desktop"

Status: **acceptable** (punctuation: Apostrophe: \`Desktop's\` matches only in Postgres in P1; \`wsl/docker\` is one token in Postgres in P2. Mixed direction, small counts, bounded by the 0.3 text weight.) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/rocm-container-runtime-and-wsl-setup.md#1`
- Candidate only: none
- Query tokens: baseline `docker` | `desktop`; candidate `docker` | `desktop`
- baseline-only `docs/rocm-container-runtime-and-wsl-setup.md#1`: "ritten every launch. \#\#\# Native Linux override (written when NOT in WSL/Docker Desktop) \`\`\`yaml services: guideants-ai: devices: - /dev/kfd"
  - baseline tokens `ritten` | `every` | `launch` | `END OF FILE` | `native` | `linux` | `override` | `written` | ~~`when`~~ | `not` | ~~`in`~~ | `wsl` | `docker` | `desktop` | `yaml` | `services` | `guideants-ai` | `guideants` | `ai` | `devices` | `dev` | `kfd`; candidate tokens `ritten` | `every` | `launch` | `native` | `linux` | `override` | `written` | ~~`when`~~ | ~~`NOT`~~ | ~~`in`~~ | `wsl/docker` | `desktop` | `yaml` | `services` | `guideants-ai` | `guideants` | `ai` | `devices` | `/dev/kfd`

### `Q08` · P2: "bearer token"

Status: **acceptable** (punctuation: \`\<token\>\` and \`services/api.ts\` are single Postgres tokens. Mixed direction, small counts, bounded by the 0.3 text weight.) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/auth-system-plan.md#37`, `docs/auth-system-plan.md#36`
- Candidate only: none
- Query tokens: baseline `bearer` | `token`; candidate `bearer` | `token`
- baseline-only `docs/auth-system-plan.md#37`: "\[ \] \*\*Attach credentials in \`services/api.ts\`\*\*: add the \`Authorization: Bearer \<token\>\` header in \`callApi()\` (D1), which currently sends only \`Co"
  - baseline tokens `attach` | `credentials` | ~~`in`~~ | `services` | `api` | `ts` | `add` | ~~`the`~~ | `authorization` | `bearer` | `token` | `header` | ~~`in`~~ | `callapi` | `d1` | ~~`which`~~ | `currently` | `sends` | ~~`only`~~ | `co`; candidate tokens `attach` | `credentials` | ~~`in`~~ | `services/api.ts` | `add` | ~~`the`~~ | `authorization` | `bearer` | `<token>` | `header` | ~~`in`~~ | `callapi` | `d1` | ~~`which`~~ | `currently` | `sends` | ~~`only`~~ | `co`
- baseline-only `docs/auth-system-plan.md#36`: "\[ \] \*\*Attach credentials in \`services/api.ts\`\*\*: add the \`Authorization: Bearer \<token\>\` header in \`callApi()\` (D1), which currently sends only \`Co"
  - baseline tokens `attach` | `credentials` | ~~`in`~~ | `services` | `api` | `ts` | `add` | ~~`the`~~ | `authorization` | `bearer` | `token` | `header` | ~~`in`~~ | `callapi` | `d1` | ~~`which`~~ | `currently` | `sends` | ~~`only`~~ | `co`; candidate tokens `attach` | `credentials` | ~~`in`~~ | `services/api.ts` | `add` | ~~`the`~~ | `authorization` | `bearer` | `<token>` | `header` | ~~`in`~~ | `callapi` | `d1` | ~~`which`~~ | `currently` | `sends` | ~~`only`~~ | `co`

### `Q09` · P1: "GuideAnts Guide"

Status: **acceptable** (tokenization: 3 of 4 SQL-Server-only chunks are path/slug false positives (\`docs/guideants-guide-implementation-plan.md\`, \`guideants-guide/\`, \`GuideAntsGuideProvider.tsx\`); one is a TRUE prose occurrence ("GuideAnt…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/developer-config-guide.md#24`
- Candidate only: none
- Query tokens: baseline `guideants` | `guide`; candidate `guideants` | `guide`
- baseline-only `docs/developer-config-guide.md#24`: "dden \*\*system project\*\*, each published with \*\*App Identity\*\* auth so signed-in GuideAnts users chat without API keys or webhooks. \| \| \*\*Visibility\*\* \| The syst"
  - baseline tokens `dden` | `system` | `project` | ~~`each`~~ | `published` | ~~`with`~~ | `app` | `identity` | `auth` | ~~`so`~~ | `signed-in` | `signed` | ~~`in`~~ | `guideants` | `users` | `chat` | `without` | `api` | `keys` | ~~`or`~~ | `webhooks` | `END OF FILE` | `visibility` | ~~`the`~~ | `syst`; candidate tokens `dden` | `system` | `project` | ~~`each`~~ | `published` | ~~`with`~~ | `app` | `identity` | `auth` | ~~`so`~~ | `signed-in` | `signed` | ~~`in`~~ | `guideants` | `users` | `chat` | `without` | `api` | `keys` | ~~`or`~~ | `webhooks` | `visibility` | ~~`The`~~ | `syst`

### `Q09` · P3: "GuideAnts Guide"

Status: **acceptable** (tokenization: 3 of 4 SQL-Server-only chunks are path/slug false positives (\`docs/guideants-guide-implementation-plan.md\`, \`guideants-guide/\`, \`GuideAntsGuideProvider.tsx\`); one is a TRUE prose occurrence ("GuideAnt…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/guideants-guide-implementation-plan.md#31`, `docs/published-openai-wire-continuation-gap-report.md#9`, `docs/published-openai-wire-continuation-gap-report.md#10`
- Candidate only: none
- Query tokens: baseline `guideants` | `guide`; candidate `guideants` | `guide`
- baseline-only `docs/guideants-guide-implementation-plan.md#31`: "odel/Models/PublishedGuideAuthMode.cs\` (or inline enum) - \`GuideAntsApi/Services/Auth/IAppJwtValidator.cs\` (or reuse existing validator) - \`GuideAntsApi/Service"
  - baseline tokens `odel/models/publishedguideauthmode.cs` | `odel` | `models` | `publishedguideauthmode` | `cs` | ~~`or`~~ | `inline` | `enum` | `guideantsapi/services/auth/iappjwtvalidator.cs` | `guideantsapi` | `services` | `auth` | `iappjwtvalidator` | `cs` | ~~`or`~~ | `reuse` | `existing` | `validator` | `guideantsapi` | `service`; candidate tokens `odel/models/publishedguideauthmode.cs` | ~~`or`~~ | `inline` | `enum` | `guideantsapi/services/auth/iappjwtvalidator.cs` | ~~`or`~~ | `reuse` | `existing` | `validator` | `guideantsapi/service`
- baseline-only `docs/published-openai-wire-continuation-gap-report.md#9`: "s The published wire layer must bridge provider tool calling without bypassing GuideAnts' internal guide execution model. Inbound request handling: - Parse p"
  - baseline tokens ~~`s`~~ | ~~`the`~~ | `published` | `wire` | `layer` | ~~`must`~~ | `bridge` | `provider` | `tool` | `calling` | `without` | `bypassing` | `guideants` | `internal` | `guide` | `execution` | `model` | `END OF FILE` | `inbound` | `request` | `handling` | `parse` | ~~`p`~~; candidate tokens ~~`s`~~ | ~~`The`~~ | `published` | `wire` | `layer` | `must` | `bridge` | `provider` | `tool` | `calling` | `without` | `bypassing` | `guideants` | `internal` | `guide` | `execution` | `model` | `inbound` | `request` | `handling` | `parse` | `p`
- baseline-only `docs/published-openai-wire-continuation-gap-report.md#10`: "s The published wire layer must bridge provider tool calling without bypassing GuideAnts' internal guide execution model. Inbound request handling: - Parse p"
  - baseline tokens ~~`s`~~ | ~~`the`~~ | `published` | `wire` | `layer` | ~~`must`~~ | `bridge` | `provider` | `tool` | `calling` | `without` | `bypassing` | `guideants` | `internal` | `guide` | `execution` | `model` | `END OF FILE` | `inbound` | `request` | `handling` | `parse` | ~~`p`~~; candidate tokens ~~`s`~~ | ~~`The`~~ | `published` | `wire` | `layer` | `must` | `bridge` | `provider` | `tool` | `calling` | `without` | `bypassing` | `guideants` | `internal` | `guide` | `execution` | `model` | `inbound` | `request` | `handling` | `parse` | `p`

### `Q10` · P3: "service toolbar"

Status: **acceptable** (tokenization: SQL Server's word breaker also indexes the parts of hyphen, dot and slash compounds (file names and paths such as \`host-folder-mounts.md\`, \`SKILL.md\`, \`docs/notebook-header-service-toolbar-spec.md\`) w…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/notebook-header-service-toolbar-implementation-plan.md#30`, `docs/notebook-header-service-toolbar-implementation-plan.md#28`, `docs/notebook-header-service-toolbar-implementation-plan.md#29`
- Candidate only: none
- Query tokens: baseline `service` | `toolbar`; candidate `service` | `toolbar`
- baseline-only `docs/notebook-header-service-toolbar-implementation-plan.md#30`: "; see spec \*\*§24.2\*\* \| \*\*Authoritative deviation list:\*\* \`docs/notebook-header-service-toolbar-spec.md\` \*\*§24\*\*."
  - baseline tokens ~~`see`~~ | `spec` | `24.2` | `nn24d2` | `authoritative` | `deviation` | `list` | `docs/notebook-header-service-toolbar-spec.md` | `docs` | `notebook` | `header` | `service` | `toolbar` | `spec` | `md` | `24` | `nn24`; candidate tokens `see` | `spec` | `24.2` | `authoritative` | `deviation` | `list` | `docs/notebook-header-service-toolbar-spec.md` | `24`
- baseline-only `docs/notebook-header-service-toolbar-implementation-plan.md#28`: "d spec \*\*§24\*\*. 5. Local \`Off\` / \`On\` works for every in-scope local-capable service and actually releases/restores memory. 6. No setup flows, downloads,"
  - baseline tokens ~~`d`~~ | `spec` | `24` | `nn24` | `END OF FILE` | ~~`5`~~ | ~~`nn5`~~ | `END OF FILE` | `local` | `off` | ~~`on`~~ | `works` | ~~`for`~~ | `every` | `in-scope` | ~~`in`~~ | `scope` | `local-capable` | `local` | `capable` | `service` | ~~`and`~~ | `actually` | `releases` | `restores` | `memory` | `END OF FILE` | ~~`6`~~ | ~~`nn6`~~ | `END OF FILE` | ~~`no`~~ | `setup` | `flows` | `downloads`; candidate tokens `d` | `spec` | `24` | `5` | `local` | ~~`Off`~~ | ~~`On`~~ | `works` | ~~`for`~~ | `every` | `in-scope` | ~~`in`~~ | `scope` | `local-capable` | `local` | `capable` | `service` | ~~`and`~~ | `actually` | `releases/restores` | `memory` | `6` | ~~`No`~~ | `setup` | `flows` | `downloads`
- baseline-only `docs/notebook-header-service-toolbar-implementation-plan.md#29`: "; see spec \*\*§24.2\*\* \| \*\*Authoritative deviation list:\*\* \`docs/notebook-header-service-toolbar-spec.md\` \*\*§24\*\*."
  - baseline tokens ~~`see`~~ | `spec` | `24.2` | `nn24d2` | `authoritative` | `deviation` | `list` | `docs/notebook-header-service-toolbar-spec.md` | `docs` | `notebook` | `header` | `service` | `toolbar` | `spec` | `md` | `24` | `nn24`; candidate tokens `see` | `spec` | `24.2` | `authoritative` | `deviation` | `list` | `docs/notebook-header-service-toolbar-spec.md` | `24`

### `Q20` · P1: "llama.cpp"

Status: **acceptable** (tokenization: SQL Server also indexes \`llama\` and \`cpp\` as separate words, so it matches chunks without the dotted compound \`llama.cpp\` (110 SQL-Server-only chunks over 4 scopes); hybrid impact measured: overlap@5…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/model-chat-behavior-contract.md#7`, `docs/model-chat-behavior-contract.md#5`, `docs/rocm-build-support-plan.md#1`, `docs/settings-architecture.md#13`, `docs/parameter-surface-test-plan.md#8`, `docs/llama-model-download-and-runtime-management.md#1`, `docs/parameter-surface-test-plan.md#12`, `docs/rocm-build-support-plan.md#7`, `docs/model-chat-behavior-contract.md#11`, `docs/parameter-surface-test-plan.md#19`, `docs/developer-config-guide.md#18`, `docs/model-chat-behavior-contract.md#3`, `docs/rocm-build-support-plan.md#6`, `docs/settings-architecture.md#12`, `docs/rocm-build-support-plan.md#0`, `docs/model-chat-behavior-contract.md#10`, `docs/llama-model-download-and-runtime-management.md#2`, `docs/settings-architecture.md#0`, `docs/model-chat-behavior-contract.md#1`, `docs/parameter-surface-test-plan.md#18`, `docs/rocm-build-support-plan.md#3`, `docs/model-chat-behavior-contract.md#2`, `docs/model-chat-behavior-contract.md#0`, `docs/model-chat-behavior-contract.md#9`, `docs/parameter-surface-test-plan.md#10`, `docs/parameter-surface-test-plan.md#11`, `docs/model-chat-behavior-contract.md#8`, `docs/developer-config-guide.md#3`, `docs/parameter-surface-test-plan.md#9`, `docs/rocm-build-support-plan.md#2`, `docs/developer-config-guide.md#17`, `docs/model-chat-behavior-contract.md#4`
- Candidate only: none
- Query tokens: baseline `llama.cpp` | `llama` | `cpp`; candidate `llama.cpp`
- baseline-only `docs/model-chat-behavior-contract.md#7`: "Resolver\` validates manifest JSON; \`CommitFinalizationAsync\` writes row columns without \`IRuntimeProfileResolver\`. 4. \*\*Legacy HF\*\* — Requires \`providerConfig\`"
  - baseline tokens `resolver` | `validates` | `manifest` | `json` | `commitfinalizationasync` | `writes` | `row` | `columns` | `without` | `iruntimeprofileresolver` | `END OF FILE` | ~~`4`~~ | ~~`nn4`~~ | `END OF FILE` | `legacy` | `hf` | `requires` | `providerconfig`; candidate tokens `resolver` | `validates` | `manifest` | `json` | `commitfinalizationasync` | `writes` | `row` | `columns` | `without` | `iruntimeprofileresolver` | `4` | `legacy` | `hf` | `requires` | `providerconfig`
- baseline-only `docs/model-chat-behavior-contract.md#5`: "��narrow” means: \*\*one lifecycle moment\*\* (local row creation via curated/legacy HF), \*\*one provider family\*\* (llama-cpp), \*\*copy-only\*\* — not a second authorit"
  - baseline tokens `narrow` | `means` | `one` | `lifecycle` | `moment` | `local` | `row` | `creation` | `via` | `curated` | `legacy` | `hf` | `one` | `provider` | `family` | `llama-cpp` | `llama` | `cpp` | `copy-only` | `copy` | ~~`only`~~ | `not` | ~~`a`~~ | `second` | `authorit`; candidate tokens `narrow` | `means` | `one` | `lifecycle` | `moment` | `local` | `row` | `creation` | `via` | `curated/legacy` | `hf` | `one` | `provider` | `family` | `llama-cpp` | `llama` | `cpp` | `copy-only` | `copy` | ~~`only`~~ | ~~`not`~~ | ~~`a`~~ | `second` | `authorit`
- baseline-only `docs/rocm-build-support-plan.md#1`: "ompile flags: \`-DSD\_CUDA=ON -DGGML\_CUDA=ON\` - Runtime base: \`ghcr.io/ggml-org/llama.cpp:server-cuda13\` - Torch index: \`https://download.pytorch.org/whl/cu13"
  - baseline tokens `ompile` | `flags` | `dsd_cuda` | `dsd` | `cuda` | ~~`on`~~ | `dggml_cuda` | `dggml` | `cuda` | ~~`on`~~ | `runtime` | `base` | `ghcr.io/ggml-org/llama.cpp` | `ghcr` | `io` | `ggml-org` | `llama` | `cpp` | `server-cuda13` | `server` | `cuda13` | `torch` | `index` | `https://download.pytorch.org/whl/cu13` | `https` | `download` | `pytorch` | `org` | `whl` | `cu13`; candidate tokens `ompile` | `flags` | `dsd` | `cuda` | ~~`ON`~~ | `dggml` | `cuda` | ~~`ON`~~ | `runtime` | `base` | `ghcr.io/ggml-org/llama.cpp:server-cuda13` | `ghcr.io` | `/ggml-org/llama.cpp:server-cuda13` | `torch` | `index` | `https://` | `download.pytorch.org/whl/cu13` | `download.pytorch.org` | `/whl/cu13`
- baseline-only `docs/settings-architecture.md#13`: "ntime host sections (\`LlamaCpp\`, \`LocalServiceHosts\`) - Telemetry \#\# 7) Persistence architecture \#\# 7.1 \`ApplicationSettings\` table (section JSON) Entity: \`A"
  - baseline tokens `ntime` | `host` | `sections` | `llamacpp` | `localservicehosts` | `telemetry` | ~~`7`~~ | ~~`nn7`~~ | `persistence` | `architecture` | `7.1` | `nn7d1` | `applicationsettings` | `table` | `section` | `json` | `entity` | ~~`a`~~; candidate tokens `ntime` | `host` | `sections` | `llamacpp` | `localservicehosts` | `telemetry` | `7` | `persistence` | `architecture` | `7.1` | `applicationsettings` | `table` | `section` | `json` | `entity` | ~~`A`~~
- baseline-only `docs/parameter-surface-test-plan.md#8`: "hints) \| Surface \| Navigation \| \|---------\|------------\| \| Settings Overview \| \`/settings\` → Overview tab \| \| Catalog \| \`/settings\` → Models & Runtime → Catal"
  - baseline tokens `hints` | `surface` | `navigation` | `settings` | `overview` | `settings` | `END OF FILE` | `overview` | `tab` | `catalog` | `settings` | `END OF FILE` | `models` | `runtime` | `END OF FILE` | `catal`; candidate tokens `hints` | `surface` | `navigation` | `settings` | `overview` | `/settings` | `overview` | `tab` | `catalog` | `/settings` | `models` | `runtime` | `catal`

### `Q20` · P2: "llama.cpp"

Status: **acceptable** (tokenization: SQL Server also indexes \`llama\` and \`cpp\` as separate words, so it matches chunks without the dotted compound \`llama.cpp\` (110 SQL-Server-only chunks over 4 scopes); hybrid impact measured: overlap@5…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/setup-guide.md#18`, `docs/provider-model-service-config-2026-05-25.md#8`, `docs/llama-router-preset-ui-proposal.md#61`, `docs/llama-router-preset-ui-proposal.md#42`, `docs/openai-compatible-provider-plan.md#4`, `docs/setup-guide.md#20`, `docs/provider-model-service-config-2026-05-25.md#6`, `docs/setup-guide.md#19`, `docs/hf-repository-file-picker-multi-agent-plan.md#11`, `docs/guideants-feature-value-framing-v2.md#7`, `docs/guideants-feature-value-framing-v2.md#6`, `docs/setup-guide.md#17`, `docs/hf-repository-file-picker-multi-agent-plan.md#9`, `inputs/edge-cases.json#E08`, `docs/openai-compatible-provider-plan.md#3`, `docs/setup-guide.md#16`, `docs/hf-repository-file-picker-multi-agent-plan.md#8`, `docs/hf-repository-file-picker-multi-agent-plan.md#3`, `docs/llama-router-preset-ui-proposal.md#43`, `docs/hf-repository-file-picker-multi-agent-plan.md#12`, `docs/model-parameter-override-fix-plan.md#5`, `docs/llama-router-preset-ui-proposal.md#60`, `docs/model-parameter-override-fix-plan.md#4`, `docs/openai-compatible-provider-plan.md#2`, `docs/conversation-stream-reconnect-and-cancel-proposal.md#1`, `docs/openai-compatible-provider-plan.md#0`, `docs/add-ai-services-wizard.md#6`, `docs/add-ai-services-wizard.md#7`, `docs/hf-repository-file-picker-multi-agent-plan.md#2`, `docs/provider-model-service-config-2026-05-25.md#7`, `docs/llama-router-preset-ui-proposal.md#38`, `docs/hf-repository-file-picker-multi-agent-plan.md#0`, `docs/openai-compatible-provider-plan.md#1`, `docs/setup-guide.md#21`, `docs/conversation-stream-reconnect-and-cancel-proposal.md#2`
- Candidate only: none
- Query tokens: baseline `llama.cpp` | `llama` | `cpp`; candidate `llama.cpp`
- baseline-only `docs/setup-guide.md#18`: "inventory and run load/unload/delete alias actions. For local llama onboarding, use \`Add Model\` with source \`Install from Hugging Face\` or \`Attach existing ali"
  - baseline tokens `inventory` | ~~`and`~~ | `run` | `load` | `unload` | `delete` | `alias` | `actions` | `END OF FILE` | ~~`for`~~ | `local` | `llama` | `onboarding` | ~~`use`~~ | `add` | `model` | ~~`with`~~ | `source` | `install` | ~~`from`~~ | `hugging` | `face` | ~~`or`~~ | `attach` | `existing` | `ali`; candidate tokens `inventory` | ~~`and`~~ | `run` | `load/unload/delete` | `alias` | `actions` | ~~`For`~~ | `local` | `llama` | `onboarding` | `use` | `add` | `model` | ~~`with`~~ | `source` | `install` | ~~`from`~~ | `hugging` | `face` | ~~`or`~~ | `attach` | `existing` | `ali`
- baseline-only `docs/provider-model-service-config-2026-05-25.md#8`: "31B-it-Q5\_K\_M (provider=llama-cpp, active=True), runtimeProfileId=gemma4 - qwen3.5-27b (provider=llama-cpp, active=True), runtimeProfileId=qwen3\_5 - qwen3.5"
  - baseline tokens `31b-it-q5_k_m` | `31b` | ~~`it`~~ | `q5` | ~~`k`~~ | ~~`m`~~ | `provider` | `llama-cpp` | `llama` | `cpp` | `active` | `true` | `runtimeprofileid` | `gemma4` | `qwen3` | `5-27b` | ~~`5`~~ | ~~`nn5`~~ | `27b` | `provider` | `llama-cpp` | `llama` | `cpp` | `active` | `true` | `runtimeprofileid` | `qwen3_5` | `qwen3` | ~~`5`~~ | ~~`nn5`~~ | `qwen3` | ~~`5`~~ | ~~`nn5`~~; candidate tokens `31b-it-q5` | `31b` | ~~`it`~~ | `q5` | `k` | `m` | `provider` | `llama-cpp` | `llama` | `cpp` | `active` | `true` | `runtimeprofileid` | `gemma4` | `qwen3.5-27b` | `provider` | `llama-cpp` | `llama` | `cpp` | `active` | `true` | `runtimeprofileid` | `qwen3` | `5` | `qwen3.5`
- baseline-only `docs/llama-router-preset-ui-proposal.md#61`: "sion; 3. captures the resolved commit; 4. groups complete model artifact sets; 5. resolves the declared projector; 6. enriches quant rows with authored \`quantMe"
  - baseline tokens `sion` | ~~`3`~~ | ~~`nn3`~~ | `END OF FILE` | `captures` | ~~`the`~~ | `resolved` | `commit` | ~~`4`~~ | ~~`nn4`~~ | `END OF FILE` | `groups` | `complete` | `model` | `artifact` | `sets` | ~~`5`~~ | ~~`nn5`~~ | `END OF FILE` | `resolves` | ~~`the`~~ | `declared` | `projector` | ~~`6`~~ | ~~`nn6`~~ | `END OF FILE` | `enriches` | `quant` | `rows` | ~~`with`~~ | `authored` | `quantme`; candidate tokens `sion` | `3` | `captures` | ~~`the`~~ | `resolved` | `commit` | `4` | `groups` | `complete` | `model` | `artifact` | `sets` | `5` | `resolves` | ~~`the`~~ | `declared` | `projector` | `6` | `enriches` | `quant` | `rows` | ~~`with`~~ | `authored` | `quantme`
- baseline-only `docs/llama-router-preset-ui-proposal.md#42`: ", "step": 1, "default": 20, "displayOrder": 2, "exposedInGuideBuilder": true }, "min\_p": { "key": "min\_p", "label": "Min P", "min": 0.0, "max": 1.0, "step":"
  - baseline tokens `step` | ~~`1`~~ | ~~`nn1`~~ | `default` | `20` | `nn20` | `displayorder` | ~~`2`~~ | ~~`nn2`~~ | `exposedinguidebuilder` | `true` | `min_p` | `min` | ~~`p`~~ | `key` | `min_p` | `min` | ~~`p`~~ | `label` | `min` | ~~`p`~~ | `min` | `0.0` | ~~`nn0`~~ | `max` | `1.0` | ~~`nn1`~~ | `step`; candidate tokens `step` | `1` | `default` | `20` | `displayorder` | `2` | `exposedinguidebuilder` | `true` | `min` | `p` | `key` | `min` | `p` | `label` | `min` | `p` | `min` | `0.0` | `max` | `1.0` | `step`
- baseline-only `docs/openai-compatible-provider-plan.md#4`: "he row's \`LocalRuntimeJson\`) - \`ProbeChatTargetAsync\`: add a branch for \`openai-compatible\` that parses \`LocalRuntimeJson\` and adds a blocker if \`baseUrl\` is ab"
  - baseline tokens ~~`he`~~ | `row's` | `localruntimejson` | `probechattargetasync` | `add` | ~~`a`~~ | `branch` | ~~`for`~~ | `openai-compatible` | `openai` | `compatible` | ~~`that`~~ | `parses` | `localruntimejson` | ~~`and`~~ | `adds` | ~~`a`~~ | `blocker` | ~~`if`~~ | `baseurl` | ~~`is`~~ | `ab`; candidate tokens ~~`he`~~ | `row` | ~~`s`~~ | `localruntimejson` | `probechattargetasync` | `add` | ~~`a`~~ | `branch` | ~~`for`~~ | `openai-compatible` | `openai` | `compatible` | ~~`that`~~ | `parses` | `localruntimejson` | ~~`and`~~ | `adds` | ~~`a`~~ | `blocker` | ~~`if`~~ | `baseurl` | ~~`is`~~ | `ab`

### `Q20` · P3: "llama.cpp"

Status: **acceptable** (tokenization: SQL Server also indexes \`llama\` and \`cpp\` as separate words, so it matches chunks without the dotted compound \`llama.cpp\` (110 SQL-Server-only chunks over 4 scopes); hybrid impact measured: overlap@5…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/hf-repository-file-picker-requirements.md#17`, `docs/adding-models.md#1`, `docs/hf-repository-file-picker-requirements.md#8`, `docs/local-ai-setup-guide.md#1`, `docs/setup-wizard-local-ai-test-plan.md#1`, `docs/routing-literal-audit-2026-05-22.md#1`, `docs/routing-literal-audit-2026-05-22.md#7`, `docs/adding-models.md#3`, `docs/hf-repository-file-picker-requirements.md#22`, `docs/hf-repository-file-picker-requirements.md#2`, `docs/local-ai-setup-guide.md#2`, `docs/hf-repository-file-picker-requirements.md#7`, `docs/hf-repository-file-picker-requirements.md#15`, `docs/hf-repository-file-picker-requirements.md#3`, `docs/hf-repository-file-picker-requirements.md#1`, `docs/model-sampling-policy-regression-fix.md#0`, `docs/settings-and-llama-completion-requirements.md#4`, `docs/chat-providers-architecture-and-extensibility.md#9`, `docs/hf-repository-file-picker-requirements.md#21`, `docs/routing-literal-audit-2026-05-22.md#0`, `docs/model-sampling-policy-regression-fix.md#2`, `docs/adding-models.md#0`, `docs/hf-repository-file-picker-requirements.md#0`, `docs/setup-wizard-local-ai-test-plan.md#0`, `docs/routing-literal-audit-2026-05-22.md#2`, `docs/chat-providers-architecture-and-extensibility.md#8`, `docs/adding-models.md#2`, `docs/adding-models.md#4`, `docs/hf-repository-file-picker-requirements.md#4`, `docs/hf-repository-file-picker-requirements.md#16`, `docs/settings-and-llama-completion-requirements.md#3`, `docs/routing-literal-audit-2026-05-22.md#3`, `docs/model-sampling-policy-regression-fix.md#1`, `docs/notebook-header-service-toolbar-implementation-plan.md#17`, `docs/notebook-header-service-toolbar-implementation-plan.md#16`
- Candidate only: none
- Query tokens: baseline `llama.cpp` | `llama` | `cpp`; candidate `llama.cpp`
- baseline-only `docs/hf-repository-file-picker-requirements.md#17`: "ions over \`HuggingFaceRepositoryFileDto\[\]\` plus a small context record; they are unit-testable and MUST have coverage for: valid pick, ambiguous pick → no auto-"
  - baseline tokens `ions` | ~~`over`~~ | `huggingfacerepositoryfiledto` | `plus` | ~~`a`~~ | `small` | `context` | `record` | ~~`they`~~ | ~~`are`~~ | `unit-testable` | `unit` | `testable` | ~~`and`~~ | ~~`must`~~ | ~~`have`~~ | `coverage` | ~~`for`~~ | `valid` | `pick` | `ambiguous` | `pick` | `END OF FILE` | ~~`no`~~ | `auto`; candidate tokens `ions` | ~~`over`~~ | `huggingfacerepositoryfiledto` | `plus` | ~~`a`~~ | `small` | `context` | `record` | ~~`they`~~ | ~~`are`~~ | `unit-testable` | `unit` | `testable` | ~~`and`~~ | `must` | ~~`have`~~ | `coverage` | ~~`for`~~ | `valid` | `pick` | `ambiguous` | `pick` | ~~`no`~~ | `auto`
- baseline-only `docs/adding-models.md#1`: "\*\*Local Llama Runtime\*\* \| Inventory, load/unload, and lifecycle actions for local llama-cpp models \| --- \#\# 2. Model chat behavior (on the catalog row) Param"
  - baseline tokens `local` | `llama` | `runtime` | `inventory` | `load` | `unload` | ~~`and`~~ | `lifecycle` | `actions` | ~~`for`~~ | `local` | `llama-cpp` | `llama` | `cpp` | `models` | ~~`2`~~ | ~~`nn2`~~ | `END OF FILE` | `model` | `chat` | `behavior` | ~~`on`~~ | ~~`the`~~ | `catalog` | `row` | `param`; candidate tokens `local` | `llama` | `runtime` | `inventory` | `load/unload` | ~~`and`~~ | `lifecycle` | `actions` | ~~`for`~~ | `local` | `llama-cpp` | `llama` | `cpp` | `models` | `2` | `model` | `chat` | `behavior` | ~~`on`~~ | ~~`the`~~ | `catalog` | `row` | `param`
- baseline-only `docs/hf-repository-file-picker-requirements.md#8`: "t it. This is already true of \`HuggingFaceTokenResolver\`; regressions are blocked by existing tests. \#\#\# 4.5 Accessibility and operator ergonomics - Repo inpu"
  - baseline tokens ~~`t`~~ | ~~`it`~~ | `END OF FILE` | ~~`this`~~ | ~~`is`~~ | `already` | `true` | ~~`of`~~ | `huggingfacetokenresolver` | `regressions` | ~~`are`~~ | `blocked` | ~~`by`~~ | `existing` | `tests` | `END OF FILE` | `4.5` | `nn4d5` | `accessibility` | ~~`and`~~ | `operator` | `ergonomics` | `repo` | `inpu`; candidate tokens ~~`t`~~ | ~~`it`~~ | ~~`This`~~ | ~~`is`~~ | `already` | `true` | ~~`of`~~ | `huggingfacetokenresolver` | `regressions` | ~~`are`~~ | `blocked` | ~~`by`~~ | `existing` | `tests` | `4.5` | `accessibility` | ~~`and`~~ | `operator` | `ergonomics` | `repo` | `inpu`
- baseline-only `docs/local-ai-setup-guide.md#1`: "t=png\` - Speech Synthesis: \`SpeechSynthesis.LocalTts.Http\`, \`TimeoutSeconds=300\` - Document Intelligence: \`DocumentIntelligence.LocalDocling.Http\`, \`TimeoutSeco"
  - baseline tokens ~~`t`~~ | `png` | `speech` | `synthesis` | `speechsynthesis` | `localtts` | `http` | `timeoutseconds` | `300` | `nn300` | `document` | `intelligence` | `documentintelligence` | `localdocling` | `http` | `timeoutseco`; candidate tokens ~~`t`~~ | `png` | `speech` | `synthesis` | `speechsynthesis.localtts.http` | `timeoutseconds` | `300` | `document` | `intelligence` | `documentintelligence.localdocling.http` | `timeoutseco`
- baseline-only `docs/setup-wizard-local-ai-test-plan.md#1`: "runtime containers are running. 4. HF token precondition is already satisfied. \#\# 5. Target Configuration (frozen) After wizard completion, this exact config"
  - baseline tokens `runtime` | `containers` | ~~`are`~~ | `running` | `END OF FILE` | ~~`4`~~ | ~~`nn4`~~ | `END OF FILE` | `hf` | `token` | `precondition` | ~~`is`~~ | `already` | `satisfied` | `END OF FILE` | ~~`5`~~ | ~~`nn5`~~ | `END OF FILE` | `target` | `configuration` | `frozen` | ~~`after`~~ | `wizard` | `completion` | ~~`this`~~ | `exact` | `config`; candidate tokens `runtime` | `containers` | ~~`are`~~ | `running` | `4` | `hf` | `token` | `precondition` | ~~`is`~~ | `already` | `satisfied` | `5` | `target` | `configuration` | `frozen` | ~~`After`~~ | `wizard` | `completion` | ~~`this`~~ | `exact` | `config`

### `Q20` · P1/N1: "llama.cpp"

Status: **acceptable** (tokenization: SQL Server also indexes \`llama\` and \`cpp\` as separate words, so it matches chunks without the dotted compound \`llama.cpp\` (110 SQL-Server-only chunks over 4 scopes); hybrid impact measured: overlap@5…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/rocm-build-support-plan.md#1`, `docs/llama-model-download-and-runtime-management.md#1`, `docs/rocm-build-support-plan.md#7`, `docs/rocm-build-support-plan.md#6`, `docs/rocm-build-support-plan.md#0`, `docs/llama-model-download-and-runtime-management.md#2`, `docs/rocm-build-support-plan.md#3`, `docs/rocm-build-support-plan.md#2`
- Candidate only: none
- Query tokens: baseline `llama.cpp` | `llama` | `cpp`; candidate `llama.cpp`
- baseline-only `docs/rocm-build-support-plan.md#1`: "ompile flags: \`-DSD\_CUDA=ON -DGGML\_CUDA=ON\` - Runtime base: \`ghcr.io/ggml-org/llama.cpp:server-cuda13\` - Torch index: \`https://download.pytorch.org/whl/cu13"
  - baseline tokens `ompile` | `flags` | `dsd_cuda` | `dsd` | `cuda` | ~~`on`~~ | `dggml_cuda` | `dggml` | `cuda` | ~~`on`~~ | `runtime` | `base` | `ghcr.io/ggml-org/llama.cpp` | `ghcr` | `io` | `ggml-org` | `llama` | `cpp` | `server-cuda13` | `server` | `cuda13` | `torch` | `index` | `https://download.pytorch.org/whl/cu13` | `https` | `download` | `pytorch` | `org` | `whl` | `cu13`; candidate tokens `ompile` | `flags` | `dsd` | `cuda` | ~~`ON`~~ | `dggml` | `cuda` | ~~`ON`~~ | `runtime` | `base` | `ghcr.io/ggml-org/llama.cpp:server-cuda13` | `ghcr.io` | `/ggml-org/llama.cpp:server-cuda13` | `torch` | `index` | `https://` | `download.pytorch.org/whl/cu13` | `download.pytorch.org` | `/whl/cu13`
- baseline-only `docs/llama-model-download-and-runtime-management.md#1`: "t token override is part of the normal Settings add/download flow. \#\# 3. Core backend components - \`ILlamaRuntimeCoordinator\`: per-alias operation serializati"
  - baseline tokens ~~`t`~~ | `token` | `override` | ~~`is`~~ | `part` | ~~`of`~~ | ~~`the`~~ | `normal` | `settings` | `add` | `download` | `flow` | `END OF FILE` | ~~`3`~~ | ~~`nn3`~~ | `END OF FILE` | `core` | `backend` | `components` | `illamaruntimecoordinator` | `per-alias` | `per` | `alias` | `operation` | `serializati`; candidate tokens ~~`t`~~ | `token` | `override` | ~~`is`~~ | `part` | ~~`of`~~ | ~~`the`~~ | `normal` | `settings` | `add/download` | `flow` | `3` | `core` | `backend` | `components` | `illamaruntimecoordinator` | `per-alias` | `per` | `alias` | `operation` | `serializati`
- baseline-only `docs/rocm-build-support-plan.md#7`: "s for \`/llama-cpp\`, \`/asr\`, \`/tts\`, \`/emb\`, \`/sd\` - Crash envelope still classifies OOM cases correctly \#\# Phase 3: Compose profiles and startup orchestration"
  - baseline tokens ~~`s`~~ | ~~`for`~~ | `llama-cpp` | `llama` | `cpp` | `asr` | `tts` | `emb` | `sd` | `crash` | `envelope` | ~~`still`~~ | `classifies` | `oom` | `cases` | `correctly` | `phase` | ~~`3`~~ | ~~`nn3`~~ | `compose` | `profiles` | ~~`and`~~ | `startup` | `orchestration`; candidate tokens ~~`s`~~ | ~~`for`~~ | `/llama-cpp` | `/asr` | `/tts` | `/emb` | `/sd` | `crash` | `envelope` | `still` | `classifies` | `oom` | `cases` | `correctly` | `phase` | `3` | `compose` | `profiles` | ~~`and`~~ | `startup` | `orchestration`
- baseline-only `docs/rocm-build-support-plan.md#6`: "\` tag - \`.env\` gets \`GA\_AI\_ROCM\_IMAGE=\<new-tag\>\` \#\# Phase 2: Runtime internals hardening (remove CUDA-only assumptions) \#\#\# Files - \`docker/build/guideants-a"
  - baseline tokens `tag` | `env` | `gets` | `ga_ai_rocm_image` | `ga` | `ai` | `rocm` | `image` | `new-tag` | `new` | `tag` | `phase` | ~~`2`~~ | ~~`nn2`~~ | `runtime` | `internals` | `hardening` | `remove` | `cuda-only` | `cuda` | ~~`only`~~ | `assumptions` | `files` | `docker` | `build` | `guideants-a` | `guideants` | ~~`a`~~; candidate tokens `tag` | `env` | `gets` | `ga` | `ai` | `rocm` | `image` | `<new-tag>` | `phase` | `2` | `runtime` | `internals` | `hardening` | `remove` | `cuda-only` | `cuda` | ~~`only`~~ | `assumptions` | `files` | `docker/build/guideants-a`
- baseline-only `docs/rocm-build-support-plan.md#0`: "ompile flags: \`-DSD\_CUDA=ON -DGGML\_CUDA=ON\` - Runtime base: \`ghcr.io/ggml-org/llama.cpp:server-cuda13\` - Torch index: \`https://download.pytorch.org/whl/cu13"
  - baseline tokens `ompile` | `flags` | `dsd_cuda` | `dsd` | `cuda` | ~~`on`~~ | `dggml_cuda` | `dggml` | `cuda` | ~~`on`~~ | `runtime` | `base` | `ghcr.io/ggml-org/llama.cpp` | `ghcr` | `io` | `ggml-org` | `llama` | `cpp` | `server-cuda13` | `server` | `cuda13` | `torch` | `index` | `https://download.pytorch.org/whl/cu13` | `https` | `download` | `pytorch` | `org` | `whl` | `cu13`; candidate tokens `ompile` | `flags` | `dsd` | `cuda` | ~~`ON`~~ | `dggml` | `cuda` | ~~`ON`~~ | `runtime` | `base` | `ghcr.io/ggml-org/llama.cpp:server-cuda13` | `ghcr.io` | `/ggml-org/llama.cpp:server-cuda13` | `torch` | `index` | `https://` | `download.pytorch.org/whl/cu13` | `download.pytorch.org` | `/whl/cu13`

### `Q25` · P2: "https://github.com/ggml-org/llama.cpp"

Status: **acceptable** (tokenization: Postgres lexes the chunk's URL as one longer token (\`github.com/ggml-org/llama.cpp/releases\`) so the shorter URL phrase never matches (also under the stock \`english\` and \`simple\` configs); SQL Server…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `inputs/edge-cases.json#E08`
- Candidate only: none
- Query tokens: baseline `https://github.com/ggml-org/llama.cpp` | `https` | `github` | `com` | `ggml` | `org` | `llama` | `cpp`; candidate `https://` | `github.com/ggml-org/llama.cpp` | `github.com` | `/ggml-org/llama.cpp`
- baseline-only `inputs/edge-cases.json#E08`: "Source and release notes: https://github.com/ggml-org/llama.cpp/releases"
  - baseline tokens `source` | ~~`and`~~ | `release` | `notes` | `https://github.com/ggml-org/llama.cpp/releases` | `https` | `github` | `com` | `ggml` | `org` | `llama` | `cpp` | `releases`; candidate tokens `source` | ~~`and`~~ | `release` | `notes` | `https://` | `github.com/ggml-org/llama.cpp/releases` | `github.com` | `/ggml-org/llama.cpp/releases`

### `Q27` · P2: "SKILL.md"

Status: **acceptable** (tokenization: SQL Server's word breaker also indexes the parts of hyphen, dot and slash compounds (file names and paths such as \`host-folder-mounts.md\`, \`SKILL.md\`, \`docs/notebook-header-service-toolbar-spec.md\`) w…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/skills-support-proposal.md#12`, `docs/skills-support-proposal.md#9`, `docs/skills-support-proposal.md#11`, `docs/skills-support-proposal.md#8`
- Candidate only: none
- Query tokens: baseline `skill.md` | `skill` | `md`; candidate `skill.md`
- baseline-only `docs/skills-support-proposal.md#12`: ".FirstOrDefault(f =\> f.RelativePath.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase)); if (manifest?.ContentBytes is null) continue"
  - baseline tokens `firstordefault` | ~~`f`~~ | ~~`f`~~ | `relativepath.endswith("/skill.md` | `relativepath` | `endswith` | `"` | `skill` | `md` | `stringcomparison` | `ordinalignorecase` | ~~`if`~~ | `manifest` | `contentbytes` | ~~`is`~~ | `null` | `continue`; candidate tokens `firstordefault` | `f` | `f.relativepath.endswith` | `/skill.md` | `stringcomparison.ordinalignorecase` | ~~`if`~~ | `manifest` | `contentbytes` | ~~`is`~~ | `null` | `continue`
- baseline-only `docs/skills-support-proposal.md#9`: "ssistantId - FolderKind = "Skill" - RelativePath = "Skills/\<skill-name\>/SKILL.md" \| "Skills/\<skill-name\>/references/\<file\>""
  - baseline tokens `ssistantid` | `folderkind` | `skill` | `relativepath` | `skills` | `skill-name` | `skill` | `name` | `skill.md` | `skill` | `md` | `skills` | `skill-name` | `skill` | `name` | `references` | `file`; candidate tokens `ssistantid` | `folderkind` | `skill` | `relativepath` | `skills` | `<skill-name>` | `/skill.md` | `skills` | `<skill-name>` | `/references` | `<file>`
- baseline-only `docs/skills-support-proposal.md#11`: "var manifest = group.FirstOrDefault(f =\> f.RelativePath.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase)); if (manifest?.ContentBy"
  - baseline tokens `var` | `manifest` | `group` | `firstordefault` | ~~`f`~~ | ~~`f`~~ | `relativepath.endswith("/skill.md` | `relativepath` | `endswith` | `"` | `skill` | `md` | `stringcomparison` | `ordinalignorecase` | ~~`if`~~ | `manifest` | `contentby`; candidate tokens `var` | `manifest` | `group.firstordefault` | `f` | `f.relativepath.endswith` | `/skill.md` | `stringcomparison.ordinalignorecase` | ~~`if`~~ | `manifest` | `contentby`
- baseline-only `docs/skills-support-proposal.md#8`: "ssistantId - FolderKind = "Skill" - RelativePath = "Skills/\<skill-name\>/SKILL.md" \| "Skills/\<skill-name\>/references/\<file\>""
  - baseline tokens `ssistantid` | `folderkind` | `skill` | `relativepath` | `skills` | `skill-name` | `skill` | `name` | `skill.md` | `skill` | `md` | `skills` | `skill-name` | `skill` | `name` | `references` | `file`; candidate tokens `ssistantid` | `folderkind` | `skill` | `relativepath` | `skills` | `<skill-name>` | `/skill.md` | `skills` | `<skill-name>` | `/references` | `<file>`

### `Q33` · P1: "C\#"

Status: **BLOCKER** (punctuation: Postgres's parser drops the \`\#\`, so the phrase "C\#" becomes the bare letter \`c\` (it matches 120 chunks table-wide against 10 for SQL Server; the Postgres-only matches are 30 in P1, 65 in P2 and 15 in…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: none
- Candidate only: `docs/script-execution-agent-admin-api-requirements-plan.md#9`, `docs/compaction-adr-algorithmic-extraction.md#3`, `docs/parameter-surface-test-plan.md#4`, `docs/host-agent-and-desktop-runtime-proposal.md#2`, `docs/compaction-adr-algorithmic-extraction.md#6`, `docs/parameter-surface-test-plan.md#8`, `docs/parameter-surface-test-plan.md#17`, `docs/parameter-surface-test-plan.md#15`, `docs/parameter-surface-test-plan.md#12`, `docs/parameter-surface-test-plan.md#16`, `docs/compaction-adr-algorithmic-extraction.md#2`, `docs/tool-sources-guide-builder-proposal.md#3`, `docs/hf-provider-completion-plan-for-iteration.md#12`, `docs/compaction-adr-algorithmic-extraction.md#5`, `docs/parameter-surface-test-plan.md#3`, `docs/parameter-surface-test-plan.md#2`, `docs/script-execution-agent-admin-api-requirements-plan.md#10`, `docs/guideants-feature-value-framing-v4.md#9`, `docs/parameter-surface-test-plan.md#18`, `docs/parameter-surface-test-plan.md#21`, `docs/parameter-surface-test-plan.md#20`, `docs/parameter-surface-test-plan.md#10`, `docs/host-agent-and-desktop-runtime-proposal.md#1`, `docs/parameter-surface-test-plan.md#11`, `docs/conversation-service-composition-plan.md#5`, `docs/parameter-surface-test-plan.md#5`, `docs/parameter-surface-test-plan.md#9`, `docs/conversation-service-composition-plan.md#8`, `docs/hf-provider-completion-plan-for-iteration.md#13`, `docs/notebook-header-service-toolbar-spec.md#8`
- Query tokens: baseline `c#`; candidate `c`
- candidate-only `docs/script-execution-agent-admin-api-requirements-plan.md#9`: "\`execvp\`. 6. Compose drops \`SYS\_PTRACE\` from the \`guideants-ai\` container. \#\#\# Phase 7: Testing Status: complete for agent and compose validation 1. Agent t"
  - baseline tokens `execvp` | `END OF FILE` | ~~`6`~~ | ~~`nn6`~~ | `END OF FILE` | `compose` | `drops` | `sys_ptrace` | `sys` | `ptrace` | ~~`from`~~ | ~~`the`~~ | `guideants-ai` | `guideants` | `ai` | `container` | `END OF FILE` | `phase` | ~~`7`~~ | ~~`nn7`~~ | `testing` | `status` | `complete` | ~~`for`~~ | `agent` | ~~`and`~~ | `compose` | `validation` | ~~`1`~~ | ~~`nn1`~~ | `END OF FILE` | `agent` | ~~`t`~~; candidate tokens `execvp` | `6` | `compose` | `drops` | `sys` | `ptrace` | ~~`from`~~ | ~~`the`~~ | `guideants-ai` | `guideants` | `ai` | `container` | `phase` | `7` | `testing` | `status` | `complete` | ~~`for`~~ | `agent` | ~~`and`~~ | `compose` | `validation` | `1` | `agent` | ~~`t`~~
- candidate-only `docs/compaction-adr-algorithmic-extraction.md#3`: "proportional to conversation length, on every press \| \| Determinism \| None, or approximate at best — the same input can produce different summaries across calls"
  - baseline tokens `proportional` | ~~`to`~~ | `conversation` | `length` | ~~`on`~~ | `every` | `press` | `determinism` | `none` | ~~`or`~~ | `approximate` | ~~`at`~~ | `best` | ~~`the`~~ | ~~`same`~~ | `input` | ~~`can`~~ | `produce` | `different` | `summaries` | `across` | `calls`; candidate tokens `proportional` | ~~`to`~~ | `conversation` | `length` | ~~`on`~~ | `every` | `press` | `determinism` | `none` | ~~`or`~~ | `approximate` | ~~`at`~~ | `best` | ~~`the`~~ | ~~`same`~~ | `input` | ~~`can`~~ | `produce` | `different` | `summaries` | `across` | `calls`
- candidate-only `docs/parameter-surface-test-plan.md#4`: "rameter-surface-baseline.bak .\\output\\db\\ \`\`\` Re-create this baseline only when the intentional starting catalog changes (new provider, migration, etc.). \#\#\#"
  - baseline tokens `rameter-surface-baseline.bak` | `rameter-surface-baseline` | `bak` | `\output\db\` | `output` | `db` | `re-create` | ~~`re`~~ | `create` | ~~`this`~~ | `baseline` | ~~`only`~~ | ~~`when`~~ | ~~`the`~~ | `intentional` | `starting` | `catalog` | `changes` | `new` | `provider` | `migration` | `etc`; candidate tokens `rameter-surface-baseline.bak` | `output` | `db` | `re-create` | `re` | `create` | ~~`this`~~ | `baseline` | ~~`only`~~ | ~~`when`~~ | ~~`the`~~ | `intentional` | `starting` | `catalog` | `changes` | `new` | `provider` | `migration` | `etc`
- candidate-only `docs/host-agent-and-desktop-runtime-proposal.md#2`: "chestrator\*\* \| \`guideants.cmd\` / \`guideants.sh\` \| Start/stop session \| \| \*\*Host agent\*\* \| \`guideants-host-agent.mjs\` (localhost HTTP) \| Runs while stack is up \|"
  - baseline tokens `chestrator` | `guideants.cmd` | `guideants` | `cmd` | `guideants.sh` | `guideants` | `sh` | `start` | `stop` | `session` | `host` | `agent` | `guideants-host-agent.mjs` | `guideants-host-agent` | `mjs` | `localhost` | `http` | `runs` | ~~`while`~~ | `stack` | ~~`is`~~ | ~~`up`~~; candidate tokens `chestrator` | `guideants.cmd` | `guideants.sh` | `start/stop` | `session` | `host` | `agent` | `guideants-host-agent.mjs` | `localhost` | `http` | `runs` | ~~`while`~~ | `stack` | ~~`is`~~ | ~~`up`~~
- candidate-only `docs/compaction-adr-algorithmic-extraction.md#6`: "not covered by the fixed vocabulary or not readable from the tool-call protocol are invisible in the summary until a user reaches for Recall (D7). - \*\*To r"
  - baseline tokens `not` | `covered` | ~~`by`~~ | ~~`the`~~ | `fixed` | `vocabulary` | ~~`or`~~ | `not` | `readable` | ~~`from`~~ | ~~`the`~~ | `tool-call` | `tool` | `call` | `protocol` | ~~`are`~~ | `invisible` | ~~`in`~~ | ~~`the`~~ | `summary` | `until` | ~~`a`~~ | `user` | `reaches` | ~~`for`~~ | `recall` | `d7` | `END OF FILE` | ~~`to`~~ | ~~`r`~~; candidate tokens ~~`not`~~ | `covered` | ~~`by`~~ | ~~`the`~~ | `fixed` | `vocabulary` | ~~`or`~~ | ~~`not`~~ | `readable` | ~~`from`~~ | ~~`the`~~ | `tool-call` | `tool` | `call` | `protocol` | ~~`are`~~ | `invisible` | ~~`in`~~ | ~~`the`~~ | `summary` | ~~`until`~~ | ~~`a`~~ | `user` | `reaches` | ~~`for`~~ | `recall` | `d7` | ~~`To`~~ | `r`

### `Q33` · P2: "C\#"

Status: **BLOCKER** (punctuation: Postgres's parser drops the \`\#\`, so the phrase "C\#" becomes the bare letter \`c\` (it matches 120 chunks table-wide against 10 for SQL Server; the Postgres-only matches are 30 in P1, 65 in P2 and 15 in…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: none
- Candidate only: `docs/mcp-tool-execution-design.md#15`, `docs/client-navigation-user-actions.md#17`, `docs/client-navigation-user-actions.md#28`, `docs/client-navigation-user-actions.md#0`, `docs/client-navigation-user-actions.md#13`, `docs/compaction-adr-manual-trigger.md#3`, `docs/model-parameter-override-fix-plan.md#8`, `docs/client-navigation-user-actions.md#16`, `docs/conversation-stream-reconnect-and-cancel-proposal.md#18`, `docs/client-navigation-user-actions.md#18`, `docs/rocm-container-runtime-and-wsl-setup.md#12`, `docs/client-navigation-user-actions.md#3`, `docs/name-based-storage-migration-plan.md#25`, `docs/secure-cli-auth.md#11`, `docs/name-based-storage-migration-plan.md#15`, `docs/compaction-llamacpp-e2e-runbook.md#0`, `docs/secure-cli-auth.md#12`, `docs/client-navigation-user-actions.md#21`, `docs/client-navigation-user-actions.md#27`, `docs/conversation-stream-reconnect-and-cancel-proposal.md#15`, `docs/model-parameter-override-fix-plan.md#9`, `docs/conversation-stream-reconnect-and-cancel-proposal.md#17`, `docs/client-navigation-user-actions.md#23`, `docs/client-navigation-user-actions.md#1`, `docs/llama-router-preset-ui-proposal.md#10`, `docs/tool-call-limits-proposal.md#14`, `docs/client-navigation-user-actions.md#29`, `docs/client-navigation-user-actions.md#32`, `docs/conversation-stream-reconnect-and-cancel-proposal.md#14`, `docs/client-navigation-user-actions.md#7`, `docs/rocm-container-runtime-and-wsl-setup.md#11`, `docs/rocm-container-runtime-and-wsl-setup.md#7`, `docs/tool-call-limits-proposal.md#13`, `docs/client-navigation-user-actions.md#24`, `docs/client-navigation-user-actions.md#26`, `docs/client-navigation-user-actions.md#20`, `docs/name-based-storage-migration-plan.md#16`, `docs/client-navigation-user-actions.md#31`, `docs/llama-router-preset-ui-proposal.md#68`, `docs/client-navigation-user-actions.md#9`, `docs/client-navigation-user-actions.md#6`, `docs/client-navigation-user-actions.md#10`, `docs/client-navigation-user-actions.md#15`, `docs/client-navigation-user-actions.md#14`, `docs/rocm-container-runtime-and-wsl-setup.md#13`, `docs/compaction-llamacpp-e2e-runbook.md#1`, `docs/llama-router-preset-ui-proposal.md#14`, `docs/rocm-container-runtime-and-wsl-setup.md#8`, `docs/client-navigation-user-actions.md#33`, `docs/mcp-tool-execution-design.md#13`, `docs/llama-router-preset-ui-proposal.md#8`, `docs/mcp-tool-execution-design.md#16`, `docs/client-navigation-user-actions.md#19`, `docs/mcp-tool-execution-design.md#14`, `docs/name-based-storage-migration-plan.md#24`, `docs/llama-router-preset-ui-proposal.md#13`, `docs/mcp-tool-execution-design.md#9`, `docs/llama-router-preset-ui-proposal.md#15`, `docs/compaction-adr-manual-trigger.md#2`, `docs/client-navigation-user-actions.md#2`, `docs/client-navigation-user-actions.md#8`, `docs/mcp-tool-execution-design.md#8`, `docs/llama-router-preset-ui-proposal.md#9`, `docs/client-navigation-user-actions.md#22`, `docs/client-navigation-user-actions.md#25`
- Query tokens: baseline `c#`; candidate `c`
- candidate-only `docs/mcp-tool-execution-design.md#15`: "\| \*\*LOCKED\*\* \| \| E13 \| Wire architecture \| Thin adapter over \`SendMessageStreamAsync\`; live streaming all facades \| \*\*LOCKED\*\* \| \| E14 \| Wire tool visibility \|"
  - baseline tokens `locked` | `e13` | `wire` | `architecture` | `thin` | `adapter` | ~~`over`~~ | `sendmessagestreamasync` | `live` | `streaming` | ~~`all`~~ | `facades` | `locked` | `e14` | `wire` | `tool` | `visibility`; candidate tokens `locked` | `e13` | `wire` | `architecture` | `thin` | `adapter` | ~~`over`~~ | `sendmessagestreamasync` | `live` | `streaming` | ~~`all`~~ | `facades` | `locked` | `e14` | `wire` | `tool` | `visibility`
- candidate-only `docs/client-navigation-user-actions.md#17`: ".tsx\` \#\#\# Search bar \| Action \| Result \| A \| C \| R \| \|--------\|--------\|---\|---\|---\| \| Type in search \| Filter notebooks + files \| ✓ \| ✓ \| ✓ \| \| \*\*×\*\* clear \|"
  - baseline tokens `tsx` | `search` | `bar` | `action` | `result` | ~~`a`~~ | ~~`c`~~ | ~~`r`~~ | `type` | ~~`in`~~ | `search` | `filter` | `notebooks` | `files` | `END OF FILE` | `clear`; candidate tokens `tsx` | `search` | `bar` | `action` | `result` | ~~`A`~~ | `c` | `r` | `type` | ~~`in`~~ | `search` | `filter` | `notebooks` | `files` | `clear`
- candidate-only `docs/client-navigation-user-actions.md#28`: "ebook\*\* \| Click \| \`SaveAssistantContentDialog\` \| ✓ \| ✓ \| — \| \| \*\*Edit message\*\* \| Click \| Edit assistant output (last message only) \| ✓ \| ✓ \| — \| \| \*\*Copy to cl"
  - baseline tokens `ebook` | `click` | `saveassistantcontentdialog` | `END OF FILE` | `edit` | `message` | `click` | `edit` | `assistant` | `output` | `last` | `message` | ~~`only`~~ | `END OF FILE` | `copy` | ~~`to`~~ | `cl`; candidate tokens `ebook` | `click` | `saveassistantcontentdialog` | `edit` | `message` | `click` | `edit` | `assistant` | `output` | `last` | `message` | ~~`only`~~ | `copy` | ~~`to`~~ | `cl`
- candidate-only `docs/client-navigation-user-actions.md#0`: "\# GuideAnts Client — Navigation & User Action Catalog Action-level inventory of what a user can click, type, right-click, or trigger via keyboard in the GuideA"
  - baseline tokens `guideants` | `client` | `navigation` | `user` | `action` | `catalog` | `action-level` | `action` | `level` | `inventory` | ~~`of`~~ | ~~`what`~~ | ~~`a`~~ | `user` | ~~`can`~~ | `click` | `type` | `right-click` | `right` | `click` | ~~`or`~~ | `trigger` | `via` | `keyboard` | ~~`in`~~ | ~~`the`~~ | `guidea`; candidate tokens `guideants` | `client` | `navigation` | `user` | `action` | `catalog` | `action-level` | `action` | `level` | `inventory` | ~~`of`~~ | ~~`what`~~ | ~~`a`~~ | `user` | ~~`can`~~ | `click` | `type` | `right-click` | `right` | `click` | ~~`or`~~ | `trigger` | `via` | `keyboard` | ~~`in`~~ | ~~`the`~~ | `guidea`
- candidate-only `docs/client-navigation-user-actions.md#13`: "Chat defaults model pickers + \*\*Save chat defaults\*\* \| Update global chat model \| \| Per-row \*\*Open in Connections\*\* / \*\*Open in Services\*\* / \*\*Open Models & Run"
  - baseline tokens `chat` | `defaults` | `model` | `pickers` | `save` | `chat` | `defaults` | `update` | `global` | `chat` | `model` | `per-row` | `per` | `row` | `open` | ~~`in`~~ | `connections` | `open` | ~~`in`~~ | `services` | `open` | `models` | `run`; candidate tokens `chat` | `defaults` | `model` | `pickers` | `save` | `chat` | `defaults` | `update` | `global` | `chat` | `model` | `per-row` | `per` | `row` | `open` | ~~`in`~~ | `connections` | `open` | ~~`in`~~ | `services` | `open` | `models` | `run`

### `Q33` · P3: "C\#"

Status: **BLOCKER** (punctuation: Postgres's parser drops the \`\#\`, so the phrase "C\#" becomes the bare letter \`c\` (it matches 120 chunks table-wide against 10 for SQL Server; the Postgres-only matches are 30 in P1, 65 in P2 and 15 in…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: none
- Candidate only: `docs/guideants-guide-implementation-plan.md#25`, `docs/guideants-feature-list-analysis-approach.md#4`, `docs/hf-repository-file-picker-requirements.md#22`, `docs/guideants-guide-implementation-plan.md#24`, `docs/hf-repository-file-picker-requirements.md#21`, `docs/spa-unmatched-route-404-design.md#12`, `docs/chat-providers-architecture-and-extensibility.md#12`, `docs/spa-unmatched-route-404-design.md#11`, `docs/guideants-feature-value-framing-v3.md#0`, `docs/guideants-feature-list-analysis-approach.md#3`, `docs/media-extraction-ai-router-plan.md#3`, `docs/notebook-header-service-toolbar-implementation-plan.md#16`, `docs/media-extraction-ai-router-plan.md#2`, `docs/chat-providers-architecture-and-extensibility.md#11`, `docs/published-openai-wire-continuation-gap-report.md#13`
- Query tokens: baseline `c#`; candidate `c`
- candidate-only `docs/guideants-guide-implementation-plan.md#25`: "trols in that state 4. Component test: App Identity guide shows indicator, controls disabled 5. Optional: \`GuideCard\` tooltip / badge \#\#\# Workstream C — Data m"
  - baseline tokens `trols` | ~~`in`~~ | ~~`that`~~ | `state` | ~~`4`~~ | ~~`nn4`~~ | `END OF FILE` | `component` | `test` | `app` | `identity` | `guide` | `shows` | `indicator` | `controls` | `disabled` | ~~`5`~~ | ~~`nn5`~~ | `END OF FILE` | `optional` | `guidecard` | `tooltip` | `badge` | `workstream` | ~~`c`~~ | `data` | ~~`m`~~; candidate tokens `trols` | ~~`in`~~ | ~~`that`~~ | `state` | `4` | `component` | `test` | `app` | `identity` | `guide` | `shows` | `indicator` | `controls` | `disabled` | `5` | `optional` | `guidecard` | `tooltip` | `badge` | `workstream` | `c` | `data` | `m`
- candidate-only `docs/guideants-feature-list-analysis-approach.md#4`: "gap notes \| This keeps detail available while preserving executive readability. \#\# 5. Editorial rules (to avoid dump vs undersell) - Write feature names as o"
  - baseline tokens `gap` | `notes` | ~~`this`~~ | `keeps` | `detail` | `available` | ~~`while`~~ | `preserving` | `executive` | `readability` | `END OF FILE` | ~~`5`~~ | ~~`nn5`~~ | `END OF FILE` | `editorial` | `rules` | ~~`to`~~ | `avoid` | `dump` | `vs` | `undersell` | `write` | `feature` | `names` | ~~`as`~~ | ~~`o`~~; candidate tokens `gap` | `notes` | ~~`This`~~ | `keeps` | `detail` | `available` | ~~`while`~~ | `preserving` | `executive` | `readability` | `5` | `editorial` | `rules` | ~~`to`~~ | `avoid` | `dump` | `vs` | `undersell` | `write` | `feature` | `names` | ~~`as`~~ | `o`
- candidate-only `docs/hf-repository-file-picker-requirements.md#22`: "renders, download completes. - \`SpeechSynthesis\`: browse the locked \`hexgrad/Kokoro-82M\` repo, preview renders + voices counted, download completes. - \`Embe"
  - baseline tokens `renders` | `download` | `completes` | `END OF FILE` | `speechsynthesis` | `browse` | ~~`the`~~ | `locked` | `hexgrad` | `kokoro-82m` | `kokoro` | `82m` | `repo` | `preview` | `renders` | `voices` | `counted` | `download` | `completes` | `END OF FILE` | `embe`; candidate tokens `renders` | `download` | `completes` | `speechsynthesis` | `browse` | ~~`the`~~ | `locked` | `hexgrad/kokoro-82m` | `repo` | `preview` | `renders` | `voices` | `counted` | `download` | `completes` | `embe`
- candidate-only `docs/guideants-guide-implementation-plan.md#24`: "ls unprompted, phase 1 manual test can use instruction "call AppEcho with message hello". Admin guide OpenAPI lists future tools; client registers only tools p"
  - baseline tokens `ls` | `unprompted` | `phase` | ~~`1`~~ | ~~`nn1`~~ | `manual` | `test` | ~~`can`~~ | ~~`use`~~ | `instruction` | `call` | `appecho` | ~~`with`~~ | `message` | `hello` | `END OF FILE` | `admin` | `guide` | `openapi` | `lists` | `future` | `tools` | `client` | `registers` | ~~`only`~~ | `tools` | ~~`p`~~; candidate tokens `ls` | `unprompted` | `phase` | `1` | `manual` | `test` | ~~`can`~~ | `use` | `instruction` | `call` | `appecho` | ~~`with`~~ | `message` | `hello` | `admin` | `guide` | `openapi` | `lists` | `future` | `tools` | `client` | `registers` | ~~`only`~~ | `tools` | `p`
- candidate-only `docs/hf-repository-file-picker-requirements.md#21`: "ble test set per service. All classifiers have unit tests; each integrated editor has at least one Playwright-CLI acceptance. - \`common/RepositoryFilePicker.te"
  - baseline tokens `ble` | `test` | `set` | `per` | `service` | `END OF FILE` | ~~`all`~~ | `classifiers` | ~~`have`~~ | `unit` | `tests` | ~~`each`~~ | `integrated` | `editor` | ~~`has`~~ | ~~`at`~~ | `least` | `one` | `playwright-cli` | `playwright` | `cli` | `acceptance` | `END OF FILE` | `common` | `repositoryfilepicker` | `te`; candidate tokens `ble` | `test` | `set` | `per` | `service` | ~~`All`~~ | `classifiers` | ~~`have`~~ | `unit` | `tests` | ~~`each`~~ | `integrated` | `editor` | ~~`has`~~ | ~~`at`~~ | `least` | `one` | `playwright-cli` | `playwright` | `cli` | `acceptance` | `common/repositoryfilepicker.te`

### `Q33` · P1/N1: "C\#"

Status: **BLOCKER** (punctuation: Postgres's parser drops the \`\#\`, so the phrase "C\#" becomes the bare letter \`c\` (it matches 120 chunks table-wide against 10 for SQL Server; the Postgres-only matches are 30 in P1, 65 in P2 and 15 in…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: none
- Candidate only: `docs/tool-sources-guide-builder-proposal.md#3`, `docs/guideants-feature-value-framing-v4.md#9`, `docs/conversation-service-composition-plan.md#5`, `docs/conversation-service-composition-plan.md#8`
- Query tokens: baseline `c#`; candidate `c`
- candidate-only `docs/tool-sources-guide-builder-proposal.md#3`: "in the sandbox. The URL host/path identifies the init script. \| \| \`tool://\` \| Invoke a local .NET static method or property by reflection. \| The Guide Builder"
  - baseline tokens ~~`in`~~ | ~~`the`~~ | `sandbox` | `END OF FILE` | ~~`the`~~ | `url` | `host` | `path` | `identifies` | ~~`the`~~ | `init` | `script` | `END OF FILE` | `tool` | `:/` | `invoke` | ~~`a`~~ | `local` | `.net` | `static` | `method` | ~~`or`~~ | `property` | ~~`by`~~ | `reflection` | `END OF FILE` | ~~`the`~~ | `guide` | `builder`; candidate tokens ~~`in`~~ | ~~`the`~~ | `sandbox` | ~~`The`~~ | `url` | `host/path` | `identifies` | ~~`the`~~ | `init` | `script` | `tool://` | `invoke` | ~~`a`~~ | `local` | `net` | `static` | `method` | ~~`or`~~ | `property` | ~~`by`~~ | `reflection` | ~~`The`~~ | `guide` | `builder`
- candidate-only `docs/guideants-feature-value-framing-v4.md#9`: "es/configTabs/AuthTab.tsx\` - \`src/client/src/pages/PublicGuide.tsx\` - \`src/server/GuideAntsApi/Endpoints/GuidesPublishingEndpoints.cs\` - \`src/server/GuideAntsAp"
  - baseline tokens `es/configtabs/authtab.tsx` | `es` | `configtabs` | `authtab` | `tsx` | `src/client/src/pages/publicguide.tsx` | `src` | `client` | `src` | `pages` | `publicguide` | `tsx` | `src/server/guideantsapi/endpoints/guidespublishingendpoints.cs` | `src` | `server` | `guideantsapi` | `endpoints` | `guidespublishingendpoints` | `cs` | `src` | `server` | `guideantsap`; candidate tokens `es/configtabs/authtab.tsx` | `src/client/src/pages/publicguide.tsx` | `src/server/guideantsapi/endpoints/guidespublishingendpoints.cs` | `src/server/guideantsap`
- candidate-only `docs/conversation-service-composition-plan.md#5`: "managers before adding stores Before introducing \`ConversationTurnStore\` and \`ConversationMessageStore\`, decide the fate of: - \`TurnManager\` - \`ITurnManager\`"
  - baseline tokens `managers` | ~~`before`~~ | `adding` | `stores` | ~~`before`~~ | `introducing` | `conversationturnstore` | ~~`and`~~ | `conversationmessagestore` | `decide` | ~~`the`~~ | `fate` | ~~`of`~~ | `turnmanager` | `iturnmanager`; candidate tokens `managers` | ~~`before`~~ | `adding` | `stores` | ~~`Before`~~ | `introducing` | `conversationturnstore` | ~~`and`~~ | `conversationmessagestore` | `decide` | ~~`the`~~ | `fate` | ~~`of`~~ | `turnmanager` | `iturnmanager`
- candidate-only `docs/conversation-service-composition-plan.md#8`: "Service\` and filters: \`\`\`csharp db.NotebookConversationMessages .Where(m =\> m.UserId == currentUser.UserId) .Select(m =\> m.NotebookConversationId)"
  - baseline tokens `service` | ~~`and`~~ | `filters` | `csharp` | `db` | `notebookconversationmessages` | ~~`where`~~ | ~~`m`~~ | ~~`m`~~ | `userid` | `currentuser` | `userid` | `select` | ~~`m`~~ | ~~`m`~~ | `notebookconversationid`; candidate tokens `service` | ~~`and`~~ | `filters` | `csharp` | `db.notebookconversationmessages` | ~~`Where`~~ | `m` | `m.userid` | `currentuser.userid` | `select` | `m` | `m.notebookconversationid`

### `Q37` · P1: "published guide"

Status: **acceptable** (punctuation: Hyphen/slash compounds such as \`published/private\`, \`guide-adjacent\`, \`PublishedConversationService.cs\` shift token positions so the phrase matches only in SQL Server. Mixed direction, small counts, b…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/conversation-service-composition-plan.md#3`, `docs/conversation-service-composition-plan.md#4`, `inputs/edge-cases.json#E13`
- Candidate only: none
- Query tokens: baseline `published` | `guide`; candidate `published` | `guide`
- baseline-only `docs/conversation-service-composition-plan.md#3`: "PublishedConversationService.cs Queries/ ConversationQueryService.cs UserConversationQueryService.cs Commands/ ConversationCommandService.cs"
  - baseline tokens `publishedconversationservice.cs` | `publishedconversationservice` | `cs` | `queries` | `conversationqueryservice.cs` | `conversationqueryservice` | `cs` | `userconversationqueryservice.cs` | `userconversationqueryservice` | `cs` | `commands` | `conversationcommandservice.cs` | `conversationcommandservice` | `cs`; candidate tokens `publishedconversationservice.cs` | `queries` | `conversationqueryservice.cs` | `userconversationqueryservice.cs` | `commands` | `conversationcommandservice.cs`
- baseline-only `docs/conversation-service-composition-plan.md#4`: "e files when the extracted service makes the ownership boundary real. \#\#\# Make published/private differences explicit Private and published conversations shou"
  - baseline tokens ~~`e`~~ | `files` | ~~`when`~~ | ~~`the`~~ | `extracted` | `service` | `makes` | ~~`the`~~ | `ownership` | `boundary` | `real` | `END OF FILE` | ~~`make`~~ | `published` | `private` | `differences` | `explicit` | `private` | ~~`and`~~ | `published` | `conversations` | `shou`; candidate tokens `e` | `files` | ~~`when`~~ | ~~`the`~~ | `extracted` | `service` | `makes` | ~~`the`~~ | `ownership` | `boundary` | `real` | `make` | `published/private` | `differences` | `explicit` | `private` | ~~`and`~~ | `published` | `conversations` | `shou`
- baseline-only `inputs/edge-cases.json#E13`: "Notes on the published, guide-adjacent pages: punctuation sits between the words."
  - baseline tokens `notes` | ~~`on`~~ | ~~`the`~~ | `published` | `guide-adjacent` | `guide` | `adjacent` | `pages` | `punctuation` | `sits` | ~~`between`~~ | ~~`the`~~ | `words`; candidate tokens `notes` | ~~`on`~~ | ~~`the`~~ | `published` | `guide-adjacent` | `guide` | `adjacent` | `pages` | `punctuation` | `sits` | ~~`between`~~ | ~~`the`~~ | `words`

### `Q37` · P1/N1: "published guide"

Status: **acceptable** (punctuation: Hyphen/slash compounds such as \`published/private\`, \`guide-adjacent\`, \`PublishedConversationService.cs\` shift token positions so the phrase matches only in SQL Server. Mixed direction, small counts, b…) [reviewed by Claude (on behalf of JacksonFalgoust, who delegated the review) on 2026-10-27]

- Baseline only: `docs/conversation-service-composition-plan.md#3`, `docs/conversation-service-composition-plan.md#4`
- Candidate only: none
- Query tokens: baseline `published` | `guide`; candidate `published` | `guide`
- baseline-only `docs/conversation-service-composition-plan.md#3`: "PublishedConversationService.cs Queries/ ConversationQueryService.cs UserConversationQueryService.cs Commands/ ConversationCommandService.cs"
  - baseline tokens `publishedconversationservice.cs` | `publishedconversationservice` | `cs` | `queries` | `conversationqueryservice.cs` | `conversationqueryservice` | `cs` | `userconversationqueryservice.cs` | `userconversationqueryservice` | `cs` | `commands` | `conversationcommandservice.cs` | `conversationcommandservice` | `cs`; candidate tokens `publishedconversationservice.cs` | `queries` | `conversationqueryservice.cs` | `userconversationqueryservice.cs` | `commands` | `conversationcommandservice.cs`
- baseline-only `docs/conversation-service-composition-plan.md#4`: "e files when the extracted service makes the ownership boundary real. \#\#\# Make published/private differences explicit Private and published conversations shou"
  - baseline tokens ~~`e`~~ | `files` | ~~`when`~~ | ~~`the`~~ | `extracted` | `service` | `makes` | ~~`the`~~ | `ownership` | `boundary` | `real` | `END OF FILE` | ~~`make`~~ | `published` | `private` | `differences` | `explicit` | `private` | ~~`and`~~ | `published` | `conversations` | `shou`; candidate tokens `e` | `files` | ~~`when`~~ | ~~`the`~~ | `extracted` | `service` | `makes` | ~~`the`~~ | `ownership` | `boundary` | `real` | `make` | `published/private` | `differences` | `explicit` | `private` | ~~`and`~~ | `published` | `conversations` | `shou`

## Rule 3: hybrid ranking

Mean top-5 overlap 0.955 over 152 pairs; same #1 in 92.1%.
145 differences: 41 scored-differently, 10 matched-one-side, 9 top-n-cut, 8 top-n-boundary-tie, 77 displaced.

| Query | Scope | Overlap@5 | Same #1 | Differences |
|---|---|---|---|---|
| `Q01` | P1 | 1.00 | yes | 0 |
| `Q01` | P2 | 0.40 | no | 8 |
| `Q01` | P3 | 1.00 | yes | 0 |
| `Q01` | P1/N1 | 0.80 | no | 6 |
| `Q02` | P1 | 1.00 | yes | 0 |
| `Q02` | P2 | 0.80 | yes | 3 |
| `Q02` | P3 | 0.80 | yes | 5 |
| `Q02` | P1/N1 | 1.00 | yes | 0 |
| `Q03` | P1 | 0.80 | yes | 3 |
| `Q03` | P2 | 1.00 | yes | 4 |
| `Q03` | P3 | 0.80 | no | 6 |
| `Q03` | P1/N1 | 0.80 | yes | 3 |
| `Q04` | P1 | 1.00 | yes | 0 |
| `Q04` | P2 | 0.80 | yes | 4 |
| `Q04` | P3 | 0.80 | yes | 5 |
| `Q04` | P1/N1 | 1.00 | yes | 0 |
| `Q05` | P1 | 1.00 | yes | 0 |
| `Q05` | P2 | 0.80 | no | 6 |
| `Q05` | P3 | 1.00 | yes | 0 |
| `Q05` | P1/N1 | 1.00 | yes | 0 |
| `Q06` | P1 | 1.00 | yes | 0 |
| `Q06` | P2 | 1.00 | yes | 0 |
| `Q06` | P3 | 1.00 | yes | 0 |
| `Q06` | P1/N1 | 1.00 | no | 3 |
| `Q07` | P1 | 1.00 | yes | 0 |
| `Q07` | P2 | 0.80 | no | 6 |
| `Q07` | P3 | 0.80 | no | 6 |
| `Q07` | P1/N1 | 1.00 | yes | 0 |
| `Q08` | P1 | 1.00 | yes | 0 |
| `Q08` | P2 | 0.80 | yes | 5 |
| `Q08` | P3 | 1.00 | yes | 0 |
| `Q08` | P1/N1 | 1.00 | yes | 0 |
| `Q09` | P1 | 0.80 | yes | 5 |
| `Q09` | P2 | 1.00 | yes | 0 |
| `Q09` | P3 | 0.80 | no | 6 |
| `Q09` | P1/N1 | 1.00 | yes | 0 |
| `Q10` | P1 | 1.00 | yes | 0 |
| `Q10` | P2 | 0.80 | yes | 5 |
| `Q10` | P3 | 1.00 | yes | 0 |
| `Q10` | P1/N1 | 1.00 | yes | 0 |
| `Q11` | P1 | 1.00 | yes | 0 |
| `Q11` | P2 | 1.00 | yes | 0 |
| `Q11` | P3 | 1.00 | yes | 0 |
| `Q11` | P1/N1 | 1.00 | yes | 0 |
| `Q12` | P1 | 1.00 | yes | 0 |
| `Q12` | P2 | 1.00 | yes | 0 |
| `Q12` | P3 | 1.00 | yes | 0 |
| `Q12` | P1/N1 | 1.00 | yes | 0 |
| `Q13` | P1 | 1.00 | yes | 0 |
| `Q13` | P2 | 1.00 | yes | 0 |
| `Q13` | P3 | 1.00 | yes | 0 |
| `Q13` | P1/N1 | 1.00 | yes | 0 |
| `Q14` | P1 | 1.00 | yes | 0 |
| `Q14` | P2 | 1.00 | yes | 0 |
| `Q14` | P3 | 1.00 | yes | 0 |
| `Q14` | P1/N1 | 1.00 | yes | 0 |
| `Q15` | P1 | 1.00 | yes | 0 |
| `Q15` | P2 | 1.00 | yes | 0 |
| `Q15` | P3 | 1.00 | yes | 0 |
| `Q15` | P1/N1 | 1.00 | yes | 0 |
| `Q16` | P1 | 1.00 | yes | 0 |
| `Q16` | P2 | 1.00 | yes | 0 |
| `Q16` | P3 | 1.00 | yes | 0 |
| `Q16` | P1/N1 | 1.00 | yes | 0 |
| `Q17` | P1 | 1.00 | yes | 0 |
| `Q17` | P2 | 1.00 | yes | 0 |
| `Q17` | P3 | 1.00 | yes | 0 |
| `Q17` | P1/N1 | 1.00 | yes | 0 |
| `Q18` | P1 | 1.00 | yes | 0 |
| `Q18` | P2 | 1.00 | yes | 0 |
| `Q18` | P3 | 1.00 | yes | 0 |
| `Q18` | P1/N1 | 1.00 | yes | 0 |
| `Q19` | P1 | 1.00 | yes | 0 |
| `Q19` | P2 | 0.40 | yes | 7 |
| `Q19` | P3 | 1.00 | yes | 0 |
| `Q19` | P1/N1 | 1.00 | yes | 0 |
| `Q20` | P1 | 1.00 | yes | 0 |
| `Q20` | P2 | 0.80 | yes | 3 |
| `Q20` | P3 | 0.80 | no | 6 |
| `Q20` | P1/N1 | 1.00 | yes | 0 |
| `Q21` | P1 | 1.00 | yes | 0 |
| `Q21` | P2 | 1.00 | yes | 0 |
| `Q21` | P3 | 1.00 | yes | 0 |
| `Q21` | P1/N1 | 1.00 | yes | 0 |
| `Q22` | P1 | 1.00 | yes | 0 |
| `Q22` | P2 | 1.00 | yes | 0 |
| `Q22` | P3 | 1.00 | yes | 0 |
| `Q22` | P1/N1 | 1.00 | yes | 0 |
| `Q23` | P1 | 1.00 | yes | 0 |
| `Q23` | P2 | 1.00 | yes | 0 |
| `Q23` | P3 | 1.00 | yes | 0 |
| `Q23` | P1/N1 | 1.00 | yes | 0 |
| `Q24` | P1 | 1.00 | yes | 0 |
| `Q24` | P2 | 1.00 | yes | 0 |
| `Q24` | P3 | 1.00 | yes | 0 |
| `Q24` | P1/N1 | 1.00 | yes | 0 |
| `Q25` | P1 | 1.00 | yes | 0 |
| `Q25` | P2 | 1.00 | yes | 0 |
| `Q25` | P3 | 1.00 | yes | 0 |
| `Q25` | P1/N1 | 1.00 | yes | 0 |
| `Q26` | P1 | 1.00 | yes | 3 |
| `Q26` | P2 | 1.00 | yes | 0 |
| `Q26` | P3 | 1.00 | yes | 0 |
| `Q26` | P1/N1 | 1.00 | yes | 0 |
| `Q27` | P1 | 1.00 | yes | 0 |
| `Q27` | P2 | 0.60 | yes | 4 |
| `Q27` | P3 | 0.80 | yes | 3 |
| `Q27` | P1/N1 | 1.00 | yes | 0 |
| `Q28` | P1 | 1.00 | yes | 0 |
| `Q28` | P2 | 1.00 | yes | 0 |
| `Q28` | P3 | 1.00 | yes | 0 |
| `Q28` | P1/N1 | 1.00 | yes | 0 |
| `Q29` | P1 | 1.00 | yes | 2 |
| `Q29` | P2 | 1.00 | yes | 0 |
| `Q29` | P3 | 1.00 | yes | 0 |
| `Q29` | P1/N1 | 1.00 | yes | 0 |
| `Q30` | P1 | 1.00 | yes | 0 |
| `Q30` | P2 | 1.00 | yes | 0 |
| `Q30` | P3 | 0.80 | yes | 3 |
| `Q30` | P1/N1 | 1.00 | yes | 0 |
| `Q31` | P1 | 1.00 | yes | 0 |
| `Q31` | P2 | 1.00 | yes | 0 |
| `Q31` | P3 | 1.00 | yes | 0 |
| `Q31` | P1/N1 | 1.00 | yes | 0 |
| `Q32` | P1 | 1.00 | yes | 0 |
| `Q32` | P2 | 1.00 | yes | 0 |
| `Q32` | P3 | 1.00 | yes | 0 |
| `Q32` | P1/N1 | 1.00 | yes | 0 |
| `Q33` | P1 | 1.00 | yes | 0 |
| `Q33` | P2 | 0.00 | no | 10 |
| `Q33` | P3 | 1.00 | no | 5 |
| `Q33` | P1/N1 | 1.00 | yes | 0 |
| `Q34` | P1 | 1.00 | yes | 0 |
| `Q34` | P2 | 1.00 | yes | 0 |
| `Q34` | P3 | 1.00 | yes | 0 |
| `Q34` | P1/N1 | 1.00 | yes | 0 |
| `Q35` | P1 | 1.00 | yes | 0 |
| `Q35` | P2 | 1.00 | yes | 0 |
| `Q35` | P3 | 1.00 | yes | 0 |
| `Q35` | P1/N1 | 1.00 | yes | 0 |
| `Q36` | P1 | 1.00 | yes | 0 |
| `Q36` | P2 | 1.00 | yes | 0 |
| `Q36` | P3 | 1.00 | yes | 0 |
| `Q36` | P1/N1 | 1.00 | yes | 0 |
| `Q37` | P1 | 1.00 | no | 3 |
| `Q37` | P2 | 1.00 | yes | 0 |
| `Q37` | P3 | 0.60 | yes | 5 |
| `Q37` | P1/N1 | 1.00 | yes | 2 |
| `Q38` | P1 | 1.00 | yes | 0 |
| `Q38` | P2 | 1.00 | yes | 0 |
| `Q38` | P3 | 1.00 | yes | 0 |
| `Q38` | P1/N1 | 1.00 | yes | 0 |

- `Q01` · P2: `docs/conversation-stream-reconnect-and-cancel-proposal.md#11` rank 3 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q01` · P2: `docs/llama-router-preset-ui-proposal.md#14` rank 4 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q01` · P2: `docs/client-navigation-user-actions.md#25` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q01` · P2: `docs/conversation-stream-reconnect-and-cancel-proposal.md#1` rank – → 1: **scored-differently** (text 0.0170 vs 0.0909; matched True/True; candidate True/True)
- `Q01` · P2: `docs/conversation-stream-reconnect-and-cancel-proposal.md#0` rank – → 2: **scored-differently** (text 0.0160 vs 0.0909; matched True/True; candidate True/True)
- `Q01` · P2: `docs/compaction-adr-manual-trigger.md#4` rank – → 3: **scored-differently** (text 0.0170 vs 0.0909; matched True/True; candidate True/True)
- `Q01` · P2: `docs/client-navigation-user-actions.md#24` rank 1 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q01` · P2: `docs/tool-call-limits-proposal.md#10` rank 2 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q01` · P1/N1: `docs/conversation-service-composition-plan.md#10` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q01` · P1/N1: `docs/conversation-service-composition-plan.md#7` rank – → 3: **scored-differently** (text 0.0140 vs 0.0909; matched True/True; candidate True/True)
- `Q01` · P1/N1: `docs/conversation-service-composition-plan.md#16` rank 1 → 2: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q01` · P1/N1: `docs/conversation-service-composition-plan.md#6` rank 2 → 1: **scored-differently** (text 0.0140 vs 0.0909; matched True/True; candidate True/True)
- `Q01` · P1/N1: `docs/conversation-service-composition-plan.md#9` rank 3 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q01` · P1/N1: `docs/llama-model-download-and-runtime-management.md#3` rank 4 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q02` · P2: `docs/llama-router-preset-ui-proposal.md#34` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched True/True; candidate False/False)
- `Q02` · P2: `docs/llama-router-preset-ui-proposal.md#19` rank – → 4: **scored-differently** (text 0.0580 vs 0.2857; matched True/True; candidate True/True)
- `Q02` · P2: `docs/llama-router-preset-ui-proposal.md#30` rank 4 → 5: **displaced** (text 0.0000 vs 0.0000; matched True/True; candidate False/False)
- `Q02` · P3: `docs/openrouter-provider-completion-plan.md#1` rank 4 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q02` · P3: `docs/adding-models.md#4` rank – → 5: **scored-differently** (text 0.0350 vs 0.1667; matched True/True; candidate True/True)
- `Q02` · P3: `docs/spa-unmatched-route-404-design.md#1` rank 2 → 3: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q02` · P3: `docs/spa-unmatched-route-404-design.md#2` rank 3 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q02` · P3: `docs/adding-models.md#3` rank 5 → 2: **scored-differently** (text 0.0320 vs 0.1667; matched True/True; candidate True/True)
- `Q03` · P1: `docs/project-root-host-folder-mounts.md#10` rank 4 → –: **matched-one-side** (text 0.0700 vs 0.0000; matched True/False; candidate True/False)
- `Q03` · P1: `docs/mounted-file-context-menus.md#4` rank – → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q03` · P1: `docs/mounted-file-context-menus.md#8` rank 5 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q03` · P2: `docs/host-folder-mounts.md#8` rank 2 → 5: **matched-one-side** (text 0.1020 vs 0.0000; matched True/False; candidate True/False)
- `Q03` · P2: `docs/host-folder-mounts.md#2` rank 3 → 2: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q03` · P2: `docs/host-folder-mounts.md#5` rank 4 → 3: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q03` · P2: `docs/host-folder-mounts.md#1` rank 5 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q03` · P3: `docs/host-folder-notebook-mounts-plan.md#10` rank 5 → –: **matched-one-side** (text 0.0750 vs 0.0000; matched True/False; candidate True/False)
- `Q03` · P3: `docs/host-folder-notebook-mounts-plan.md#17` rank – → 5: **top-n-cut** (text 0.0000 vs 0.0909; matched True/True; candidate False/True)
- `Q03` · P3: `docs/host-folder-notebook-mounts-plan.md#3` rank 1 → 2: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q03` · P3: `docs/host-folder-notebook-mounts-plan.md#23` rank 2 → 1: **top-n-cut** (text 0.0000 vs 0.0909; matched True/True; candidate False/True)
- `Q03` · P3: `docs/host-folder-notebook-mounts-plan.md#20` rank 3 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q03` · P3: `docs/host-folder-notebook-mounts-plan.md#22` rank 4 → 3: **top-n-cut** (text 0.0000 vs 0.0909; matched True/True; candidate False/True)
- `Q03` · P1/N1: `docs/project-root-host-folder-mounts.md#10` rank 4 → –: **matched-one-side** (text 0.0700 vs 0.0000; matched True/False; candidate True/False)
- `Q03` · P1/N1: `docs/mounted-file-context-menus.md#4` rank – → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q03` · P1/N1: `docs/mounted-file-context-menus.md#8` rank 5 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q04` · P2: `docs/hf-repository-file-picker-multi-agent-plan.md#4` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q04` · P2: `docs/host-folder-mounts.md#4` rank – → 3: **top-n-cut** (text 0.0000 vs 0.0909; matched True/True; candidate False/True)
- `Q04` · P2: `docs/hf-repository-file-picker-multi-agent-plan.md#9` rank 3 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q04` · P2: `docs/hf-repository-file-picker-multi-agent-plan.md#8` rank 4 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q04` · P3: `docs/setup-wizard-local-ai-test-plan.md#0` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q04` · P3: `docs/media-extraction-ai-router-plan.md#1` rank – → 4: **scored-differently** (text 0.0270 vs 0.1667; matched True/True; candidate True/True)
- `Q04` · P3: `docs/project-and-notebook-files-system.md#16` rank 2 → 3: **top-n-boundary-tie** (text 0.0170 vs 0.0000; matched True/True; candidate True/False)
- `Q04` · P3: `docs/host-folder-notebook-mounts-plan.md#13` rank 3 → 2: **scored-differently** (text 0.0160 vs 0.0909; matched True/True; candidate True/True)
- `Q04` · P3: `docs/project-and-notebook-files-system.md#19` rank 4 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q05` · P2: `docs/tool-call-limits-proposal.md#12` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q05` · P2: `docs/tool-call-limits-proposal.md#16` rank – → 3: **scored-differently** (text 0.0220 vs 0.0909; matched True/True; candidate True/True)
- `Q05` · P2: `docs/tool-call-limits-proposal.md#13` rank 1 → 2: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q05` · P2: `docs/tool-call-limits-proposal.md#0` rank 2 → 1: **scored-differently** (text 0.0640 vs 0.0909; matched True/True; candidate True/True)
- `Q05` · P2: `docs/tool-call-limits-proposal.md#7` rank 3 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q05` · P2: `docs/tool-call-limits-proposal.md#5` rank 4 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q06` · P1/N1: `docs/llama-model-download-and-runtime-management.md#2` rank 1 → 3: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q06` · P1/N1: `docs/llama-model-download-and-runtime-management.md#1` rank 2 → 1: **scored-differently** (text 0.0160 vs 0.0909; matched True/True; candidate True/True)
- `Q06` · P1/N1: `docs/llama-model-download-and-runtime-management.md#0` rank 3 → 2: **scored-differently** (text 0.0160 vs 0.0909; matched True/True; candidate True/True)
- `Q07` · P2: `docs/rocm-container-runtime-and-wsl-setup.md#11` rank 4 → –: **displaced** (text 0.0000 vs 0.0000; matched True/True; candidate False/False)
- `Q07` · P2: `docs/rocm-container-runtime-and-wsl-setup.md#6` rank – → 3: **scored-differently** (text 0.0640 vs 0.2857; matched True/True; candidate True/True)
- `Q07` · P2: `docs/setup-guide.md#9` rank 1 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q07` · P2: `docs/setup-guide.md#10` rank 2 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q07` · P2: `docs/rocm-container-runtime-and-wsl-setup.md#4` rank 3 → 2: **scored-differently** (text 0.0350 vs 0.1667; matched True/True; candidate True/True)
- `Q07` · P2: `docs/rocm-container-runtime-and-wsl-setup.md#3` rank 5 → 1: **scored-differently** (text 0.0440 vs 0.2308; matched True/True; candidate True/True)
- `Q07` · P3: `inputs/edge-cases.json#E03` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q07` · P3: `docs/host-folder-notebook-mounts-plan.md#1` rank – → 1: **top-n-boundary-tie** (text 0.0000 vs 0.1667; matched True/True; candidate False/True)
- `Q07` · P3: `docs/host-folder-notebook-mounts-plan.md#21` rank 1 → 2: **displaced** (text 0.0000 vs 0.0000; matched True/True; candidate False/False)
- `Q07` · P3: `docs/routing-literal-audit-2026-05-22.md#3` rank 2 → 3: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q07` · P3: `docs/routing-literal-audit-2026-05-22.md#4` rank 3 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q07` · P3: `docs/release-runbook.md#4` rank 4 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q08` · P2: `docs/secure-cli-auth.md#5` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q08` · P2: `docs/secure-cli-auth.md#0` rank – → 3: **scored-differently** (text 0.0320 vs 0.1667; matched True/True; candidate True/True)
- `Q08` · P2: `docs/secure-cli-auth.md#4` rank 2 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q08` · P2: `docs/secure-cli-auth.md#1` rank 3 → 2: **scored-differently** (text 0.0160 vs 0.0909; matched True/True; candidate True/True)
- `Q08` · P2: `docs/secure-cli-auth.md#2` rank 4 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q09` · P1: `docs/guideants-feature-value-framing-v4.md#6` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q09` · P1: `docs/developer-config-guide.md#23` rank – → 2: **scored-differently** (text 0.0350 vs 0.1667; matched True/True; candidate True/True)
- `Q09` · P1: `docs/guideants-feature-value-framing-v4.md#0` rank 2 → 3: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q09` · P1: `docs/guideants-feature-value-framing-v4.md#7` rank 3 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q09` · P1: `docs/guideants-feature-value-framing-v4.md#2` rank 4 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q09` · P3: `docs/guideants-feature-value-framing-v3.md#0` rank 4 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q09` · P3: `docs/guideants-guide-implementation-plan.md#14` rank – → 4: **scored-differently** (text 0.0580 vs 0.2857; matched True/True; candidate True/True)
- `Q09` · P3: `docs/guideants-guide-implementation-plan.md#4` rank 1 → 2: **scored-differently** (text 0.0390 vs 0.1667; matched True/True; candidate True/True)
- `Q09` · P3: `docs/guideants-guide-implementation-plan.md#0` rank 2 → 1: **scored-differently** (text 0.1610 vs 0.3750; matched True/True; candidate True/True)
- `Q09` · P3: `docs/guideants-guide-implementation-plan.md#5` rank 3 → 5: **scored-differently** (text 0.0390 vs 0.1667; matched True/True; candidate True/True)
- `Q09` · P3: `docs/guideants-guide-implementation-plan.md#15` rank 5 → 3: **scored-differently** (text 0.0580 vs 0.2857; matched True/True; candidate True/True)
- `Q10` · P2: `docs/client-navigation-user-actions.md#12` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q10` · P2: `docs/client-navigation-user-actions.md#3` rank – → 3: **scored-differently** (text 0.0440 vs 0.2308; matched True/True; candidate True/True)
- `Q10` · P2: `docs/client-navigation-user-actions.md#28` rank 2 → 4: **top-n-boundary-tie** (text 0.0160 vs 0.0000; matched True/True; candidate True/False)
- `Q10` · P2: `docs/client-navigation-user-actions.md#2` rank 3 → 2: **scored-differently** (text 0.0480 vs 0.2308; matched True/True; candidate True/True)
- `Q10` · P2: `docs/full-provider-test-plan.md#8` rank 4 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q19` · P2: `docs/auth-system-plan.md#31` rank 3 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q19` · P2: `inputs/edge-cases.json#E17` rank 4 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q19` · P2: `docs/llama-router-preset-ui-proposal.md#48` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q19` · P2: `docs/provider-model-service-config-2026-05-25.md#3` rank – → 2: **scored-differently** (text 0.0270 vs 0.1667; matched True/True; candidate True/True)
- `Q19` · P2: `docs/provider-model-service-config-2026-05-25.md#2` rank – → 3: **scored-differently** (text 0.0290 vs 0.1667; matched True/True; candidate True/True)
- `Q19` · P2: `docs/full-provider-test-plan.md#5` rank – → 5: **scored-differently** (text 0.0120 vs 0.0909; matched True/True; candidate True/True)
- `Q19` · P2: `docs/client-navigation-user-actions.md#25` rank 2 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q20` · P2: `docs/setup-guide.md#20` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched True/False; candidate False/False)
- `Q20` · P2: `docs/llama-router-preset-ui-proposal.md#11` rank – → 2: **top-n-cut** (text 0.0000 vs 0.2308; matched True/True; candidate False/True)
- `Q20` · P2: `inputs/edge-cases.json#E02` rank 2 → 5: **top-n-cut** (text 0.1760 vs 0.0000; matched True/True; candidate True/False)
- `Q20` · P3: `docs/settings-service-provider-model-requirements.md#0` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q20` · P3: `docs/chat-providers-architecture-and-extensibility.md#7` rank – → 3: **top-n-cut** (text 0.0000 vs 0.1667; matched True/True; candidate False/True)
- `Q20` · P3: `docs/adding-models.md#4` rank 1 → 2: **displaced** (text 0.0000 vs 0.0000; matched True/False; candidate False/False)
- `Q20` · P3: `docs/adding-models.md#1` rank 2 → 4: **displaced** (text 0.0000 vs 0.0000; matched True/False; candidate False/False)
- `Q20` · P3: `docs/chat-providers-architecture-and-extensibility.md#8` rank 3 → 5: **displaced** (text 0.0000 vs 0.0000; matched True/False; candidate False/False)
- `Q20` · P3: `docs/chat-providers-architecture-and-extensibility.md#6` rank 4 → 1: **top-n-cut** (text 0.0000 vs 0.1667; matched True/True; candidate False/True)
- `Q26` · P1: `docs/developer-config-guide.md#6` rank 3 → 4: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q26` · P1: `docs/developer-config-guide.md#7` rank 4 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q26` · P1: `docs/release-notes-v0.9.18.md#1` rank 5 → 3: **scored-differently** (text 0.0170 vs 0.0909; matched True/True; candidate True/True)
- `Q27` · P2: `docs/skills-support-proposal.md#12` rank 3 → –: **displaced** (text 0.0000 vs 0.0000; matched True/False; candidate False/False)
- `Q27` · P2: `docs/skills-support-proposal.md#11` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched True/False; candidate False/False)
- `Q27` · P2: `docs/skills-support-proposal.md#17` rank – → 3: **scored-differently** (text 0.0640 vs 0.2857; matched True/True; candidate True/True)
- `Q27` · P2: `docs/skills-support-proposal.md#1` rank – → 5: **scored-differently** (text 0.0580 vs 0.2308; matched True/True; candidate True/True)
- `Q27` · P3: `docs/guideants-feature-list-analysis-approach.md#2` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q27` · P3: `docs/project-and-notebook-files-system.md#4` rank – → 4: **top-n-cut** (text 0.0000 vs 0.1667; matched True/True; candidate False/True)
- `Q27` · P3: `docs/guideants-feature-value-framing-v3.md#8` rank 4 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q29` · P1: `docs/auth-flow.md#3` rank 4 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q29` · P1: `docs/20260423-github-container-registry-slim-image.md#4` rank 5 → 4: **scored-differently** (text 0.0250 vs 0.0909; matched True/True; candidate True/True)
- `Q30` · P3: `docs/project-and-notebook-files-system.md#32` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q30` · P3: `docs/project-and-notebook-files-system.md#15` rank – → 4: **scored-differently** (text 0.0170 vs 0.0909; matched True/True; candidate True/True)
- `Q30` · P3: `docs/project-and-notebook-files-system.md#8` rank 4 → 5: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q33` · P2: `docs/model-parameter-override-fix-plan.md#3` rank 1 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q33` · P2: `docs/skills-support-proposal.md#12` rank 2 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q33` · P2: `docs/model-parameter-override-fix-plan.md#2` rank 3 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q33` · P2: `docs/skills-support-proposal.md#13` rank 4 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q33` · P2: `docs/model-parameter-override-fix-plan.md#4` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q33` · P2: `docs/client-navigation-user-actions.md#23` rank – → 1: **matched-one-side** (text 0.0000 vs 0.4118; matched False/True; candidate False/True)
- `Q33` · P2: `docs/client-navigation-user-actions.md#20` rank – → 2: **matched-one-side** (text 0.0000 vs 0.3750; matched False/True; candidate False/True)
- `Q33` · P2: `docs/client-navigation-user-actions.md#17` rank – → 3: **matched-one-side** (text 0.0000 vs 0.3333; matched False/True; candidate False/True)
- `Q33` · P2: `docs/client-navigation-user-actions.md#19` rank – → 4: **matched-one-side** (text 0.0000 vs 0.3750; matched False/True; candidate False/True)
- `Q33` · P2: `docs/client-navigation-user-actions.md#16` rank – → 5: **matched-one-side** (text 0.0000 vs 0.3333; matched False/True; candidate False/True)
- `Q33` · P3: `docs/codeql-local-solution-runbook.md#4` rank 1 → 3: **top-n-boundary-tie** (text 0.0280 vs 0.0000; matched True/True; candidate True/False)
- `Q33` · P3: `docs/codeql-local-solution-runbook.md#3` rank 2 → 1: **scored-differently** (text 0.0460 vs 0.3750; matched True/True; candidate True/True)
- `Q33` · P3: `docs/codeql-local-solution-runbook.md#2` rank 3 → 4: **top-n-boundary-tie** (text 0.0460 vs 0.0000; matched True/True; candidate True/False)
- `Q33` · P3: `docs/codeql-local-solution-runbook.md#6` rank 4 → 5: **top-n-boundary-tie** (text 0.0500 vs 0.0000; matched True/True; candidate True/False)
- `Q33` · P3: `docs/codeql-local-solution-runbook.md#5` rank 5 → 2: **scored-differently** (text 0.0500 vs 0.3750; matched True/True; candidate True/True)
- `Q37` · P1: `inputs/edge-cases.json#E13` rank 1 → 5: **matched-one-side** (text 0.1760 vs 0.0000; matched True/False; candidate True/False)
- `Q37` · P1: `docs/guideants-feature-value-framing-v4.md#6` rank 2 → 1: **scored-differently** (text 0.0520 vs 0.2308; matched True/True; candidate True/True)
- `Q37` · P1: `docs/guideants-feature-value-framing-v4.md#7` rank 5 → 2: **top-n-boundary-tie** (text 0.0000 vs 0.2308; matched True/True; candidate False/True)
- `Q37` · P3: `docs/guideants-guide-implementation-plan.md#31` rank 4 → –: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q37` · P3: `docs/guideants-guide-implementation-plan.md#4` rank 5 → –: **displaced** (text 0.0000 vs 0.0000; matched True/True; candidate False/False)
- `Q37` · P3: `docs/guideants-guide-implementation-plan.md#6` rank – → 3: **scored-differently** (text 0.0800 vs 0.3333; matched True/True; candidate True/True)
- `Q37` · P3: `docs/guideants-feature-value-framing-v3.md#3` rank – → 5: **scored-differently** (text 0.0520 vs 0.2308; matched True/True; candidate True/True)
- `Q37` · P3: `docs/guideants-guide-implementation-plan.md#5` rank 3 → 4: **scored-differently** (text 0.0580 vs 0.2308; matched True/True; candidate True/True)
- `Q37` · P1/N1: `docs/guideants-feature-value-framing-v4.md#8` rank 2 → 3: **displaced** (text 0.0000 vs 0.0000; matched False/False; candidate False/False)
- `Q37` · P1/N1: `docs/guideants-feature-value-framing-v4.md#7` rank 3 → 2: **top-n-boundary-tie** (text 0.0000 vs 0.2308; matched True/True; candidate False/True)

## Informational (not part of the verdict)

Extra and lost matches are distinct chunks across scopes P1 to P3 (P1/N1 is a subset of P1). The quirk-fixed run shows none: its text match has no top-N, so they would equal Rule 2's totals.

| Run | Pairs | Mean top-5 overlap | Same #1 | Extra matches | Lost matches |
|---|---|---|---|---|---|
| Quirk fixed on both engines (not production) | 152 | 0.939 | 92.8% | – | – |
| postgres-english | 152 | 0.949 | 91.4% | 152 | 135 |

## Limits

- **Review.** 26 difference reviews in this run came from `known-differences.json`. In the committed file the user delegated them to Claude ("do whatever you think is best") and none was reviewed line by line by a human; each entry's `reviewedBy` says so. Edit the file and re-run `compare` to change a decision.
- **Real recall misses were accepted.** Two accepted Postgres differences lose true matches: Q09 (one real prose occurrence, "GuideAnts guide/internal tools", is missed because Postgres lexes `guide/internal` as one token) and Q25 (a URL-prefix search finds nothing, because Postgres keeps the longer URL as one token).
- **Tie-break explanations.** 8 hybrid differences are explained as `top-n-boundary-tie`. They come from diagnostics whose tie-breaking among equal text ranks is unspecified, the same nondeterminism as production's own query.
- **Stop lists differ.** SQL Server's system stoplist and Postgres's `english` list are not the same (for example single letters, digits, `not` and `use`). The token evidence above shows it where it matters.
- **One corpus.** The evidence is a single corpus (`docs/*.md` at `9f8cd102`) and 38 saved queries.

