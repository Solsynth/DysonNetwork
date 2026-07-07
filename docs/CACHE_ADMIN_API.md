# Cache Admin API

This document describes the Padlock admin endpoints for inspecting cache usage and clearing cache entries.

All endpoints below are implemented in Padlock and require authentication plus the `permissions.cache.manage` permission.

## Route Base

Local development routes:

```text
/api/admin/cache/...
```

Production gateway routes:

```text
/padlock/admin/cache/...
```

## Stats

### `GET /api/admin/cache/stats`

Returns aggregate cache counters collected by the shared Redis-backed cache service.

Example response:

```json
{
  "read_hits": 120,
  "read_misses": 30,
  "write_count": 44,
  "remove_count": 10,
  "group_add_count": 18,
  "group_remove_count": 2,
  "clear_all_count": 1,
  "cleared_key_count": 57,
  "read_count": 150,
  "hit_ratio": 0.8
}
```

Field meanings:

- `read_hits`: successful cache reads
- `read_misses`: unsuccessful cache reads
- `write_count`: cache write operations
- `remove_count`: single-key delete operations
- `group_add_count`: number of key-to-group associations added
- `group_remove_count`: number of group clear operations
- `clear_all_count`: number of namespace-wide clears
- `cleared_key_count`: cumulative number of keys removed by namespace-wide clears
- `read_count`: computed as `read_hits + read_misses`
- `hit_ratio`: computed as `read_hits / read_count`, or `0` when no reads have occurred

Notes:

- These counters are service-level counters, not Redis server-native metrics.
- `hit_ratio` only reflects cache reads going through the shared `ICacheService`.

## Inspect Group

### `GET /api/admin/cache/groups/{group}`

Returns the keys currently registered under a cache group.

Example response:

```json
{
  "group": "auth:account_sessions:8c9ef364-7e4f-4e4b-9f07-2bd1b9fd6b63",
  "count": 2,
  "keys": [
    "auth:session:0fe52b86-b6d5-4999-91aa-84d47ebc8bb4",
    "auth:session:5f0bf8cd-c6c9-4f1b-a4aa-176df86bf0cf"
  ]
}
```

The keys returned here are logical cache keys without the internal `dyson:` prefix.

## Clear By Key

### `POST /api/admin/cache/keys/clear`

Request body:

```json
{
  "key": "auth:session:0fe52b86-b6d5-4999-91aa-84d47ebc8bb4"
}
```

Example response:

```json
{
  "scope": "key",
  "key": "auth:session:0fe52b86-b6d5-4999-91aa-84d47ebc8bb4",
  "group": null,
  "removed_count": 1
}
```

Notes:

- This removes the key and its group index associations.
- The response currently reports `removed_count = 1` for an accepted single-key clear request.

## Clear By Group

### `POST /api/admin/cache/groups/clear`

Request body:

```json
{
  "group": "auth:account_sessions:8c9ef364-7e4f-4e4b-9f07-2bd1b9fd6b63"
}
```

Example response:

```json
{
  "scope": "group",
  "key": null,
  "group": "auth:account_sessions:8c9ef364-7e4f-4e4b-9f07-2bd1b9fd6b63",
  "removed_count": 2
}
```

Notes:

- `removed_count` is computed from the current group membership before deletion.
- Group clear only affects keys registered in that group.

## Clear All

### `POST /api/admin/cache/clear`

Clears all cache entries in the Dyson namespace.

Example response:

```json
{
  "scope": "all",
  "key": null,
  "group": null,
  "removed_count": 57
}
```

Notes:

- This clears keys matching the internal `dyson:*` namespace.
- It does not flush unrelated Redis keys outside the Dyson cache namespace.
- Because this is broad and disruptive, it should be treated as an operator-only action.

## Error Behavior

Typical validation failures return `400 Bad Request`.

Examples:

- missing or blank `key`
- missing or blank `group`

Authorization failures follow the normal Padlock authentication and permission middleware behavior.
