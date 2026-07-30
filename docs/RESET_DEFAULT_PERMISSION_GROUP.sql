BEGIN;

SELECT key
FROM permission_nodes
WHERE group_id = (
    SELECT id
    FROM permission_groups
    WHERE key = 'default'
)
ORDER BY key;

DELETE FROM permission_nodes
WHERE group_id = (
    SELECT id
    FROM permission_groups
    WHERE key = 'default'
);

COMMIT;
