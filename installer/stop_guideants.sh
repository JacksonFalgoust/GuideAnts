#!/usr/bin/env bash
#
# Stop the GuideAnts stack that was started by guideants.sh.
#
# Reads saved component selections from .installer_state.env and runs
# docker compose down on the matching compose fragment list.
#
# Flags:
#   --db <sqlserver|postgres>  Override the saved database provider (env GA_DB_PROVIDER).
#   --help   Show this help.

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOCKER_DIR="$ROOT_DIR/docker"
ENV_FILE="$DOCKER_DIR/.env"
IMAGES_ENV_FILE="$DOCKER_DIR/images.env"
STATE_FILE="$ROOT_DIR/.installer_state.env"

log()  { printf '[guideants] %s\n' "$*"; }
fail() { printf '[guideants][error] %s\n' "$*" >&2; exit 1; }

# shellcheck source=scripts/rocm-runtime-compose.sh
. "$ROOT_DIR/scripts/rocm-runtime-compose.sh"
# shellcheck source=scripts/installer-wizard.sh
. "$ROOT_DIR/scripts/installer-wizard.sh"

usage() {
  sed -n '3,12p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

DB_PROVIDER_OVERRIDE="${GA_DB_PROVIDER:-}"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --db)
      [[ $# -ge 2 ]] || fail "Missing value for --db"
      DB_PROVIDER_OVERRIDE="$2"; shift 2 ;;
    --help|-h) usage; exit 0 ;;
    *) fail "Unknown option: $1 (try --help)" ;;
  esac
done
DB_PROVIDER_OVERRIDE="$(printf '%s' "$DB_PROVIDER_OVERRIDE" | tr '[:upper:]' '[:lower:]')"
[[ -z "$DB_PROVIDER_OVERRIDE" || "$DB_PROVIDER_OVERRIDE" =~ ^(sqlserver|postgres)$ ]] \
  || fail "--db (or GA_DB_PROVIDER) must be sqlserver or postgres"

[[ -f "$STATE_FILE" ]] || fail "No saved state found ($STATE_FILE). Run guideants.sh first."

installer_legacy_state
installer_build_compose_args_from_state "$ROOT_DIR" "$STATE_FILE" 1 1 0 "$DB_PROVIDER_OVERRIDE"

if [[ "$AI_BACKEND" == "rocm" ]]; then
  select_rocm_runtime "$DOCKER_DIR" "$ROOT_DIR"
  [[ -f "$DOCKER_DIR/docker-compose.rocm-runtime.generated.yml" ]] && \
    COMPOSE_ARGS+=(-f "$DOCKER_DIR/docker-compose.rocm-runtime.generated.yml")
fi

installer_compose_env_args
[[ "$DB_PROVIDER" == "postgres" ]] && log "Database: Postgres"
log "Stopping GuideAnts (DB=$DB_LAYOUT, AI=$AI_BACKEND)..."
docker compose "${COMPOSE_ARGS[@]}" "${COMPOSE_ENV_ARGS[@]}" down
log "GuideAnts stopped."
