BEGIN;

-- One-time cleanup for duplicate auth_clients rows that share the same
-- (account_id, device_id). We keep a single survivor per pair, move any
-- auth_sessions.client_id references onto that survivor, then delete extras.
--
-- Survivor selection:
-- 1. Prefer rows where deleted_at IS NULL
-- 2. Then keep the oldest created_at
-- 3. Then the lowest id as a stable tiebreaker

WITH ranked_clients AS (
    SELECT
        id,
        account_id,
        device_id,
        device_name,
        device_label,
        platform,
        deleted_at,
        created_at,
        FIRST_VALUE(id) OVER (
            PARTITION BY account_id, device_id
            ORDER BY
                CASE WHEN deleted_at IS NULL THEN 0 ELSE 1 END,
                created_at ASC,
                id ASC
        ) AS keeper_id,
        ROW_NUMBER() OVER (
            PARTITION BY account_id, device_id
            ORDER BY
                CASE WHEN deleted_at IS NULL THEN 0 ELSE 1 END,
                created_at ASC,
                id ASC
        ) AS row_num
    FROM auth_clients
),
duplicate_map AS (
    SELECT
        id AS duplicate_id,
        keeper_id
    FROM ranked_clients
    WHERE row_num > 1
),
best_metadata AS (
    SELECT DISTINCT ON (keeper_id)
        keeper_id,
        NULLIF(BTRIM(device_name), '') AS best_device_name,
        NULLIF(BTRIM(device_label), '') AS best_device_label,
        platform AS best_platform
    FROM ranked_clients
    WHERE
        row_num = 1
        OR NULLIF(BTRIM(device_name), '') IS NOT NULL
        OR NULLIF(BTRIM(device_label), '') IS NOT NULL
        OR platform <> 0
    ORDER BY
        keeper_id,
        CASE WHEN row_num = 1 THEN 0 ELSE 1 END,
        CASE WHEN NULLIF(BTRIM(device_name), '') IS NOT NULL THEN 0 ELSE 1 END,
        CASE WHEN NULLIF(BTRIM(device_label), '') IS NOT NULL THEN 0 ELSE 1 END,
        CASE WHEN platform <> 0 THEN 0 ELSE 1 END,
        created_at ASC,
        id ASC
),
updated_sessions AS (
    UPDATE auth_sessions s
    SET
        client_id = d.keeper_id,
        updated_at = NOW()
    FROM duplicate_map d
    WHERE s.client_id = d.duplicate_id
    RETURNING s.id
),
updated_keepers AS (
    UPDATE auth_clients c
    SET
        device_name = COALESCE(b.best_device_name, c.device_name),
        device_label = COALESCE(b.best_device_label, c.device_label),
        platform = CASE
            WHEN c.platform = 0 AND b.best_platform <> 0 THEN b.best_platform
            ELSE c.platform
        END,
        updated_at = NOW()
    FROM best_metadata b
    WHERE c.id = b.keeper_id
    RETURNING c.id
)
DELETE FROM auth_clients c
USING duplicate_map d
WHERE c.id = d.duplicate_id;

COMMIT;

-- Optional verification queries:
-- SELECT account_id, device_id, COUNT(*)
-- FROM auth_clients
-- GROUP BY account_id, device_id
-- HAVING COUNT(*) > 1;
--
-- SELECT COUNT(*)
-- FROM auth_sessions s
-- LEFT JOIN auth_clients c ON c.id = s.client_id
-- WHERE s.client_id IS NOT NULL AND c.id IS NULL;
