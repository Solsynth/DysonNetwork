#!/usr/bin/env bash
set -euo pipefail

# One-time migration helper: copy auth-owned tables from PASS DB to PADLOCK DB.
#
# Required env vars:
#   PASS_DB_URL     e.g. postgres://postgres:postgres@localhost:5432/dyson_pass
#   PADLOCK_DB_URL  e.g. postgres://postgres:postgres@localhost:5432/dyson_padlock
#
# Optional env vars:
#   MIGRATION_MODE  "merge" (default) or "replace"
#     - merge: keep target rows, insert missing rows only (ON CONFLICT DO NOTHING)
#     - replace: TRUNCATE target tables first, then import all rows
#   DISABLE_TRIGGERS_DURING_IMPORT "1" (default) or "0"
#     - Applies per table import session to avoid FK cycle issues.

if [[ -z "${PASS_DB_URL:-}" || -z "${PADLOCK_DB_URL:-}" ]]; then
  echo "PASS_DB_URL and PADLOCK_DB_URL are required."
  exit 1
fi

MIGRATION_MODE="${MIGRATION_MODE:-merge}"
DISABLE_TRIGGERS_DURING_IMPORT="${DISABLE_TRIGGERS_DURING_IMPORT:-1}"

if [[ "${MIGRATION_MODE}" != "merge" && "${MIGRATION_MODE}" != "replace" ]]; then
  echo "MIGRATION_MODE must be 'merge' or 'replace'."
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

quote_ident() {
  local s="$1"
  s="${s//\"/\"\"}"
  printf '"%s"' "$s"
}

list_columns() {
  local db_url="$1"
  local table="$2"
  psql "$db_url" -At -v ON_ERROR_STOP=1 -c \
    "select column_name from information_schema.columns where table_schema='public' and table_name='${table}' order by ordinal_position;"
}

build_common_column_list() {
  local table="$1"
  local pass_cols=()
  local pad_cols=()
  local c

  while IFS= read -r c; do
    [[ -n "$c" ]] && pass_cols+=("$c")
  done < <(list_columns "$PASS_DB_URL" "$table")

  while IFS= read -r c; do
    [[ -n "$c" ]] && pad_cols+=("$c")
  done < <(list_columns "$PADLOCK_DB_URL" "$table")

  if [[ ${#pass_cols[@]} -eq 0 ]]; then
    echo ""
    return
  fi
  if [[ ${#pad_cols[@]} -eq 0 ]]; then
    echo ""
    return
  fi

  declare -A pass_set=()
  for c in "${pass_cols[@]}"; do
    pass_set["$c"]=1
  done

  local out=()
  for c in "${pad_cols[@]}"; do
    if [[ -n "${pass_set[$c]:-}" ]]; then
      out+=("$(quote_ident "$c")")
    fi
  done

  local IFS=,
  echo "${out[*]}"
}

count_rows() {
  local db_url="$1"
  local table="$2"
  psql "$db_url" -At -v ON_ERROR_STOP=1 -c "select count(*) from public.$(quote_ident "$table");"
}

echo "Mode: ${MIGRATION_MODE}"
echo "[1/5] Pre-migration counts"
for t in "${TABLES[@]}"; do
  src_count="$(count_rows "$PASS_DB_URL" "$t")"
  dst_count="$(count_rows "$PADLOCK_DB_URL" "$t")"
  printf "  %-28s PASS=%-10s PADLOCK=%-10s\n" "$t" "$src_count" "$dst_count"
done

tmpdir="$(mktemp -d)"
cleanup() { rm -rf "${tmpdir}"; }
trap cleanup EXIT

if [[ "${MIGRATION_MODE}" == "replace" ]]; then
  echo "[2/5] Truncate target tables (replace mode)..."
  psql "$PADLOCK_DB_URL" -v ON_ERROR_STOP=1 <<SQL
TRUNCATE TABLE
  public.permission_nodes,
  public.permission_group_members,
  public.permission_groups,
  public.api_keys,
  public.auth_sessions,
  public.auth_challenges,
  public.auth_clients,
  public.account_connections,
  public.account_auth_factors,
  public.account_contacts,
  public.accounts
CASCADE;
SQL
else
  echo "[2/5] Skip truncate (merge mode)..."
fi

echo "[3/5] Export PASS tables to CSV (shared columns only)..."
for t in "${TABLES[@]}"; do
  cols="$(build_common_column_list "$t")"
  if [[ -z "$cols" ]]; then
    echo "  ${t}: no shared columns found, skipping"
    continue
  fi

  out_csv="${tmpdir}/${t}.csv"
  psql "$PASS_DB_URL" -v ON_ERROR_STOP=1 -c "\\copy (select ${cols} from public.$(quote_ident "$t")) to '${out_csv}' csv"
  echo "  ${t}: exported -> ${out_csv}"
done

echo "[4/5] Import into PADLOCK..."
for t in "${TABLES[@]}"; do
  cols="$(build_common_column_list "$t")"
  csv_file="${tmpdir}/${t}.csv"

  if [[ -z "$cols" || ! -f "$csv_file" ]]; then
    echo "  ${t}: skipped"
    continue
  fi

  stage="__stage_${t}_$$"

  if [[ "$DISABLE_TRIGGERS_DURING_IMPORT" == "1" ]]; then
    if ! psql "$PADLOCK_DB_URL" -v ON_ERROR_STOP=1 <<SQL
SET session_replication_role = replica;
CREATE TEMP TABLE ${stage} (LIKE public.$(quote_ident "$t") INCLUDING DEFAULTS) ON COMMIT DROP;
\\copy ${stage} (${cols}) from '${csv_file}' csv
INSERT INTO public.$(quote_ident "$t") (${cols})
SELECT ${cols}
FROM ${stage}
ON CONFLICT DO NOTHING;
SET session_replication_role = origin;
SQL
    then
      echo "  ${t}: trigger-disable import failed; retrying without trigger disable"
      psql "$PADLOCK_DB_URL" -v ON_ERROR_STOP=1 <<SQL
CREATE TEMP TABLE ${stage} (LIKE public.$(quote_ident "$t") INCLUDING DEFAULTS) ON COMMIT DROP;
\\copy ${stage} (${cols}) from '${csv_file}' csv
INSERT INTO public.$(quote_ident "$t") (${cols})
SELECT ${cols}
FROM ${stage}
ON CONFLICT DO NOTHING;
SQL
    fi
  else
    psql "$PADLOCK_DB_URL" -v ON_ERROR_STOP=1 <<SQL
CREATE TEMP TABLE ${stage} (LIKE public.$(quote_ident "$t") INCLUDING DEFAULTS) ON COMMIT DROP;
\\copy ${stage} (${cols}) from '${csv_file}' csv
INSERT INTO public.$(quote_ident "$t") (${cols})
SELECT ${cols}
FROM ${stage}
ON CONFLICT DO NOTHING;
SQL
  fi

  echo "  ${t}: imported"
done

echo "[5/5] Post-migration counts"
failed=0
for t in "${TABLES[@]}"; do
  src_count="$(count_rows "$PASS_DB_URL" "$t")"
  dst_count="$(count_rows "$PADLOCK_DB_URL" "$t")"
  printf "  %-28s PASS=%-10s PADLOCK=%-10s\n" "$t" "$src_count" "$dst_count"

  if [[ "$MIGRATION_MODE" == "replace" && "$src_count" != "$dst_count" ]]; then
    echo "    !! count mismatch in replace mode"
    failed=1
  fi
done

if [[ $failed -ne 0 ]]; then
  echo "Migration finished with count mismatches."
  exit 2
fi

echo "Migration completed."
