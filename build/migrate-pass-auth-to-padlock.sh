#!/usr/bin/env bash
set -euo pipefail

# One-time migration helper: copy auth-owned tables from PASS DB to PADLOCK DB.
# Required env vars:
#   PASS_DB_URL     (e.g. postgres://postgres:postgres@localhost:5432/dyson_pass)
#   PADLOCK_DB_URL  (e.g. postgres://postgres:postgres@localhost:5432/dyson_padlock)

if [[ -z "${PASS_DB_URL:-}" || -z "${PADLOCK_DB_URL:-}" ]]; then
  echo "PASS_DB_URL and PADLOCK_DB_URL are required."
  exit 1
fi

TABLES=(
  "accounts"
  "account_contacts"
  "account_auth_factors"
  "account_connections"
  "auth_clients"
  "auth_challenges"
  "auth_sessions"
  "api_keys"
  "permission_groups"
  "permission_group_members"
  "permission_nodes"
)

echo "[1/4] Pre-migration counts (PASS):"
for t in "${TABLES[@]}"; do
  printf "  %-28s" "${t}"
  psql "${PASS_DB_URL}" -Atc "select count(*) from ${t};"
done

tmpdir="$(mktemp -d)"
cleanup() { rm -rf "${tmpdir}"; }
trap cleanup EXIT

echo "[2/4] Export auth tables from PASS..."
for t in "${TABLES[@]}"; do
  pg_dump "${PASS_DB_URL}" --data-only --table="${t}" --column-inserts > "${tmpdir}/${t}.sql"
done

echo "[3/4] Import into PADLOCK..."
for t in "${TABLES[@]}"; do
  psql "${PADLOCK_DB_URL}" -v ON_ERROR_STOP=1 -f "${tmpdir}/${t}.sql"
done

echo "[4/4] Post-migration counts (PADLOCK):"
for t in "${TABLES[@]}"; do
  printf "  %-28s" "${t}"
  psql "${PADLOCK_DB_URL}" -Atc "select count(*) from ${t};"
done

echo "Migration completed."
