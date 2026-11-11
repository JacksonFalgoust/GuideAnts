#!/usr/bin/env bash
# Validates installer bash syntax + compose fragment merges.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOCKER_DIR="$ROOT_DIR/docker"

sh_files=(
  "$ROOT_DIR/guideants.sh"
  "$ROOT_DIR/stop_guideants.sh"
  "$ROOT_DIR/scripts/installer-wizard.sh"
  "$ROOT_DIR/scripts/guideants-host-mount.sh"
  "$ROOT_DIR/scripts/rocm-runtime-compose.sh"
  "$ROOT_DIR/scripts/rocm-probe.sh"
  "$ROOT_DIR/scripts/install-rocm-wsl.sh"
)

for f in "${sh_files[@]}"; do
  [[ -f "$f" ]] || { echo "FAIL missing: $f" >&2; exit 1; }
  bash -n "$f"
  echo "PASS bash -n: $(basename "$f")"
done

for rel in guideants.cmd stop_guideants.cmd; do
  [[ -f "$ROOT_DIR/$rel" ]] || { echo "FAIL missing: $rel" >&2; exit 1; }
  echo "PASS present: $rel"
done

# postgres-separate.yml copies core-separate.yml's guideants-webapi-ui block. Print a fragment's block without
# blank lines and without the database lines (the depends_on service, Database__Provider, the connection string).
webapi_ui_block() {
  awk '
    /^  guideants-webapi-ui:$/ { inblock = 1; next }
    inblock && (/^[^ ]/ || /^  [^ ]/) { exit }
    inblock && !/^[[:space:]]*$/ && !/^      (mssql-express|postgres):$/ && !/Database__Provider=/ && !/ConnectionStrings__DefaultConnection=/ { print }
  ' "$1"
}
core_webapi_ui="$(webapi_ui_block "$DOCKER_DIR/compose/core-separate.yml")"
postgres_webapi_ui="$(webapi_ui_block "$DOCKER_DIR/compose/postgres-separate.yml")"
[[ -n "$core_webapi_ui" ]] || { echo "FAIL no guideants-webapi-ui block in compose/core-separate.yml" >&2; exit 1; }
if [[ "$core_webapi_ui" != "$postgres_webapi_ui" ]]; then
  echo "FAIL guideants-webapi-ui in compose/postgres-separate.yml differs from compose/core-separate.yml beyond the database lines:" >&2
  diff <(printf '%s\n' "$core_webapi_ui") <(printf '%s\n' "$postgres_webapi_ui") >&2 || true
  exit 1
fi
echo "PASS guideants-webapi-ui: postgres-separate.yml matches core-separate.yml apart from the database lines"

if ! command -v docker >/dev/null 2>&1; then
  echo "SKIP compose config: docker not found"
  exit 0
fi

SEARXNG_SETTINGS="$DOCKER_DIR/volumes/searxng/config/settings.yml"
SEARXNG_LIMITER="$DOCKER_DIR/volumes/searxng/config/limiter.toml"
[[ -f "$SEARXNG_SETTINGS" ]] || { echo "FAIL missing SearXNG settings seed: $SEARXNG_SETTINGS" >&2; exit 1; }
[[ -f "$SEARXNG_LIMITER" ]] || { echo "FAIL missing SearXNG limiter seed: $SEARXNG_LIMITER" >&2; exit 1; }
echo "PASS searxng config seeds present"

combos=(
  "compose/base.yml compose/core-bundled.yml"
  "compose/base.yml compose/core-separate.yml"
  "compose/base.yml compose/core-bundled.yml compose/ai-slim.yml"
  "compose/base.yml compose/core-separate.yml compose/ai-cuda13.yml compose/docling-cuda.yml compose/documentserver.yml compose/plantuml.yml compose/searxng.yml"
  "compose/base.yml compose/core-separate.yml compose/ai-rocm.yml"
  "compose/base.yml compose/postgres-separate.yml compose/ai-slim.yml"
  "compose/base.yml compose/postgres-separate.yml compose/ai-cpu.yml compose/docling-cpu.yml compose/documentserver.yml compose/plantuml.yml compose/searxng.yml"
)

cd "$DOCKER_DIR"
for combo in "${combos[@]}"; do
  # shellcheck disable=SC2206
  files=($combo)
  args=(--project-directory "$DOCKER_DIR")
  for f in "${files[@]}"; do args+=(-f "$f"); done
  docker compose "${args[@]}" --env-file .env config --quiet
  echo "PASS compose: ${files[-1]}"
done

rendered="$(docker compose --project-directory "$DOCKER_DIR" \
  -f compose/base.yml -f compose/core-bundled.yml -f compose/searxng.yml \
  --env-file .env config)"
expected="$DOCKER_DIR/volumes/searxng/config"
wrong="$DOCKER_DIR/compose/volumes/searxng/config"
case "$rendered" in
  *"$expected"*) ;;
  *) echo "FAIL searxng bind must resolve under docker/volumes/searxng/config" >&2; exit 1 ;;
esac
case "$rendered" in
  *"$wrong"*) echo "FAIL searxng bind incorrectly resolves under compose/volumes/" >&2; exit 1 ;;
esac
echo "PASS searxng bind path resolves to docker/volumes/searxng"

echo "All installer validation checks passed."
