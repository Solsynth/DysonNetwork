# Presence Sessions & Catalog API

Wakatime-style activity tracking on top of the lease-based presence system. Adds explicit session windows (`started_at` / `ended_at`), a persistent catalog of "things" (games, projects, tracks) that accumulate time across sessions, and per-catalog duration stats.

This is the Passport service. When using the gateway, replace `/api` with `/passport`.

## What this enables

- **Session windows** — an activity is now a time-boxed session with a real start and end, not just a lease that silently expires.
- **Accumulated duration** — the same activity re-started (`manual_id` upsert) keeps accumulating into the same catalog item; total time per thing grows across sessions.
- **Catalog** — a persistent entity per `(account, provider, catalog_key)` holding names, metadata, tags, visibility, and the accumulated `total_seconds` / `session_count`. Clients can answer "how long did I spend in Project Omega this week".
- **Stats** — a per-catalog seconds summary computed from ended session rows, filterable by time range and tags — the "how long per thing" dashboard endpoint.
- **Strong filtering** — the existing history endpoint now also filters by `tags`, `visibility`, `catalog_id`, and session start-time range.
- **Query-friendly tags** — each session carries multiple `{slug, name}` tags (genre of music, gamemode, kind of work) with a denormalized slug array for fast filtering.

## Key concepts

| Concept | Meaning |
|---|---|
| Session | A `SnPresenceActivity` row with `started_at` set and `ended_at` null while active |
| `manual_id` | Client-supplied stable id; re-starting the same id refreshes the active session (no duplicate rows) |
| Catalog item | `SnPresenceCatalogItem` — the accumulating entity (`total_seconds`, `session_count`, `last_active_at`) |
| Tag | `{ slug, name }` — machine query key + human-readable label; a session/catalog item can carry many |
| Tag tables | Normalized `presence_activity_tags` / `presence_catalog_tags` with btree indexes on `slug` — filters and per-tag aggregation are indexed joins |
| Visibility | `Unknown, Public, Friends, Private` |

### Type, Tags, Visibility

- `type` — **free-form string** (no fixed enum). Common values: `"gaming"`, `"music"`, `"workout"`, `"coding"`; clients may use arbitrary values, stored trimmed and matched exactly.
- `tags` — **multiple** `{ slug, name }` pairs per session/catalog item, e.g. music genre (`{ "slug": "jazz", "name": "Jazz" }`), gamemode (`{ "slug": "ranked", "name": "Ranked" }`), or work kind (`{ "slug": "design", "name": "Design" }`). Slugs are trimmed and deduped on write.
- `visibility` — fixed enum `Unknown, Public, Friends, Private`

Clients send `type` (arbitrary label) and `tags` (multi-label classification) independently; there is no automatic mapping between them.

---

## API Endpoints

### Base URL: `/api/activities`

### Authentication
All endpoints require a valid bearer token. User context is extracted automatically.

---

## Start Session

Starts a presence session and resolves/accumulates its catalog item.

**Endpoint:** `POST /api/activities/sessions/start`

**Behavior:**
- If an active (unexpired, not ended) session with the same `manual_id` exists, it is **updated in place** — content fields replaced, lease refreshed, `started_at` preserved. No new row.
- Otherwise a new row is created with `started_at = now`, `ended_at = null`.
- If `catalog_key` is non-empty, the catalog item for `(account, provider, catalog_key)` is found or created. On create, `name` falls back to `title`. `activity.catalog_id` is set.
- Lease is 1-60 minutes (default 5). Artwork references (`large_image` / `small_image`) are validated when present.

**Request Body:**

| Field | Type | Default | Description |
|---|---|---|---|
| `type` | string | required | Free-form activity type (e.g. `"gaming"`, `"coding"`); any string allowed |
| `manual_id` | string | required | Stable session identifier; re-starting refreshes instead of duplicating |
| `tags` | array | `[]` | Multiple `{ slug, name }` labels — e.g. music genre, gamemode, work kind; slugs deduped on write |
| `visibility` | string (enum) | `Public` | Who can see the session |
| `provider` | string? | — | Upstream provider key such as `steam` |
| `reference_id` | string? | — | Upstream object identifier (game id, track id) |
| `title` | string? | — | Main title |
| `subtitle` | string? | — | Secondary line |
| `caption` | string? | — | Extra detail |
| `large_image` / `small_image` | string? | — | Image URL or `sha256:` artwork reference |
| `title_url` / `subtitle_url` | string? | — | Link URLs |
| `queryable_terms` | string[]? | — | Normalized search terms (lowercased, deduped) |
| `meta` | object? | — | Arbitrary JSON metadata |
| `catalog_key` | string? | — | Stable key of the catalog item this session accumulates into |
| `catalog_name` | string? | — | Display name for a newly-created catalog item (falls back to `title`) |
| `lease_minutes` | int | `5` | 1-60 |

**Example:**
```bash
curl -X POST "/api/activities/sessions/start" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "manual_id": "vscode",
    "type": "gaming",
    "tags": [
      { "slug": "coding", "name": "Coding" },
      { "slug": "frontend", "name": "Frontend" }
    ],
    "provider": "local",
    "catalog_key": "project-omega",
    "catalog_name": "Project Omega",
    "title": "Building Project Omega",
    "queryable_terms": ["project", "omega"],
    "lease_minutes": 5
  }'
```

**Response `200 OK`** — the `SnPresenceActivity`:

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "type": "gaming",
  "tags": [
    { "slug": "coding", "name": "Coding" },
    { "slug": "frontend", "name": "Frontend" }
  ],
  "visibility": "Public",
  "provider": "local",
  "reference_id": null,
  "manual_id": "vscode",
  "title": "Building Project Omega",
  "queryable_terms": ["project", "omega"],
  "lease_minutes": 5,
  "lease_expires_at": "2026-08-26T14:35:00Z",
  "started_at": "2026-08-26T14:30:00Z",
  "ended_at": null,
  "catalog_id": "a1b2c3d4-e29b-41d4-a716-446655440000",
  "account_id": "user-guid",
  "created_at": "2026-08-26T14:30:00Z",
  "updated_at": "2026-08-26T14:30:00Z",
  "deleted_at": null
}
```

**Response Codes:**
- `200 OK` — started or refreshed
- `400 Bad Request` — missing `manual_id` (`PASSPORT_ACTIVITY_MANUAL_ID_REQUIRED`), missing `type` (`PASSPORT_ACTIVITY_TYPE_REQUIRED`), lease out of 1-60 range, or invalid artwork reference
- `401 Unauthorized` — invalid authentication

---

## End Session

Ends the active session and accumulates its duration into the catalog item.

**Endpoint:** `POST /api/activities/sessions/end`

**Behavior:**
- Finds the active session by `manual_id`; sets `ended_at = now` and expires the lease immediately.
- If the session has a `catalog_id`, the catalog item accumulates: `total_seconds += floor(ended - started)`, `session_count += 1`, `last_active_at = now`.
- Idempotent: ending an already-ended or missing session returns `404`.

**Request Body:**
```json
{ "manual_id": "vscode" }
```

**Response `200 OK`** — the ended `SnPresenceActivity` with `ended_at` set and `lease_expires_at` ≤ now.

**Response Codes:**
- `200 OK` — session ended and catalog updated
- `400 Bad Request` — missing `manual_id`
- `401 Unauthorized` — invalid authentication
- `404 Not Found` — no active session for that `manual_id` (`PASSPORT_ACTIVITY_NOT_FOUND`)

---

## Get Catalog

Lists the account's catalog items (the accumulated "things"), newest-active first.

**Endpoint:** `GET /api/activities/catalog`

**Query Parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `tags` | string[] | — | Filter by tag slugs (repeatable, e.g. `&tags=jazz&tags=instrumental`); AND semantics — item must carry all |
| `provider` | string? | — | Filter by provider |
| `query` | string? | — | Matches `name` (contains) or an exact normalized `queryable_terms` entry |
| `is_active` | bool? | — | `true` = `last_active_at` within 24h; `false` = 24h+ or never active |
| `offset` | int | `0` | Pagination offset |
| `take` | int | `50` | Page size |

**Response `200 OK`** — array of `SnPresenceCatalogItem`:

```json
[
  {
    "id": "a1b2c3d4-e29b-41d4-a716-446655440000",
    "account_id": "user-guid",
    "provider": "local",
    "catalog_key": "project-omega",
    "reference_id": null,
    "name": "Project Omega",
    "subtitle": null,
    "caption": null,
    "tags": [
      { "slug": "coding", "name": "Coding" },
      { "slug": "frontend", "name": "Frontend" }
    ],
    "visibility": "Public",
    "large_image": null,
    "small_image": null,
    "title_url": null,
    "subtitle_url": null,
    "queryable_terms": ["project", "omega"],
    "meta": {},
    "total_seconds": 3725,
    "session_count": 3,
    "last_active_at": "2026-08-26T14:30:00Z",
    "created_at": "2026-08-20T09:00:00Z",
    "updated_at": "2026-08-26T14:31:00Z",
    "deleted_at": null
  }
]
```

Ordered by `last_active_at ?? updated_at` descending. No `X-Total` header on this endpoint.

**Response Codes:**
- `200 OK` — catalog items
- `401 Unauthorized` — invalid authentication

---

## Get Stats

Per-catalog accumulated duration in seconds — the "how long per thing" summary.

**Endpoint:** `GET /api/activities/stats`

**Query Parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `from` | date-time? | — | Only count sessions ended at or after this instant |
| `to` | date-time? | — | Only count sessions ended before this instant |
| `tags` | string[] | — | Only count catalog items carrying all the given tag slugs (repeatable, AND) |

**Behavior:**
- Computed from **ended session rows** (`ended_at` and `started_at` set, `catalog_id` set) — the immutable source of truth.
- Duration per session is `floor(ended - started)` in whole seconds, clamped at 0; the sum matches the catalog item's accumulated `total_seconds`.
- Legacy lease rows created before sessions (no `started_at`) are excluded.
- Keys are catalog GUIDs; join with `GET /api/activities/catalog` for names and metadata.

**Response `200 OK`** — `Dictionary<string, long>`:

```json
{
  "a1b2c3d4-e29b-41d4-a716-446655440000": 3725,
  "c7d8e9f0-a29b-41d4-a716-446655440000": 1540
}
```

**Response Codes:**
- `200 OK` — per-catalog seconds
- `401 Unauthorized` — invalid authentication

---

## Get Tag Stats

Per-tag accumulated duration in seconds — "how long did I spend in total on `rpg` / `jazz` / `design`".

**Endpoint:** `GET /api/activities/tags/stats`

**Query Parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `from` | date-time? | — | Only count sessions ended at or after this instant |
| `to` | date-time? | — | Only count sessions ended before this instant |

**Behavior:**
- Computed from **ended session rows** joined to `presence_activity_tags`, grouped by `slug`.
- A session contributes its full duration to **every** tag it carries (a session tagged `gaming`+`rpg` counts once toward each).
- Keys are tag slugs; join with a tag-name source (or read `tags` from catalog/activity responses) for display labels.
- Uses the btree index on `slug` — an indexed aggregation, not a jsonb scan.

**Response `200 OK`** — `Dictionary<string, long>`:

```json
{
  "gaming": 3725,
  "rpg": 1540,
  "multiplayer": 3725,
  "coding": 2180
}
```

**Response Codes:**
- `200 OK` — per-tag seconds
- `401 Unauthorized` — invalid authentication

---

## Get Activities (extended filters)

The existing history endpoint now also filters by session fields.

**Endpoint:** `GET /api/activities`

New query parameters (existing `query`, `type`, `provider`, `reference_id`, `term`, `include_expired`, `offset`, `take` unchanged):

| Parameter | Type | Default | Description |
|---|---|---|---|
| `tags` | string[] | — | Filter by tag slugs (repeatable, e.g. `&tags=ranked&tags=solo`); AND — session must carry all |
| `visibility` | string (enum) | — | Filter by activity visibility |
| `catalog_id` | string (guid)? | — | Filter by the session's catalog item; invalid GUID returns an empty result |
| `from` | date-time? | — | Only sessions with `started_at >= from` |
| `to` | date-time? | — | Only sessions with `started_at < to` |

**Behavior:**
- Any of the new filters (or existing ones / `include_expired=true`) routes to the database query with `X-Total` in the response header.
- Results are ordered recent-first by `started_at ?? created_at`.
- Legacy non-session rows (no `started_at`) sort by `created_at` and are excluded by `from`/`to`.

**Example:**
```bash
# Coding sessions visible to friends, started in the last 24 hours
GET "/api/activities?category=Coding&visibility=Friends&from=2026-08-25T14:30:00Z"

# All sessions belonging to one catalog item
GET "/api/activities?catalog_id=a1b2c3d4-e29b-41d4-a716-446655440000&include_expired=true"
```

---

## Example flow

```bash
# 1. Start a session for "Project Omega"
POST /api/activities/sessions/start
{ "manual_id": "vscode", "type": "gaming",
  "tags": [ { "slug": "coding", "name": "Coding" }, { "slug": "frontend", "name": "Frontend" } ],
  "provider": "local", "catalog_key": "project-omega", "catalog_name": "Project Omega",
  "title": "Building Project Omega", "lease_minutes": 5 }

# 2. (5 minutes later) end it — duration accumulates into the catalog
POST /api/activities/sessions/end
{ "manual_id": "vscode" }

# 3. See the accumulated time
GET /api/activities/catalog?query=omega
GET /api/activities/stats
```

Repeating steps 1-2 with the same `manual_id` increments `session_count` and grows `total_seconds` on the same catalog item.

---

## Data Models

### SnPresenceActivity (new fields)

```csharp
public class SnPresenceActivity : ModelBase
{
    // ... existing fields (Id, Type, Provider, ReferenceId, ManualId, Title, ...)
    public string? Type { get; set; }                       // free-form activity type
    [NotMapped] public List<SnPresenceTag> Tags { get; set; }  // display list, populated from child rows
    public List<SnPresenceActivityTag> ActivityTags { get; set; }  // normalized child rows (PK: activity_id+slug, index: slug)
    public PresenceVisibility Visibility { get; set; }      // new
    public Instant? StartedAt { get; set; }                 // new — session start
    public Instant? EndedAt { get; set; }                   // new — null while active
    public Guid? CatalogId { get; set; }                    // new — FK to catalog item
    [NotMapped] public SnPresenceCatalogItem? Catalog { get; set; }
}

public class SnPresenceTag
{
    public string Slug { get; set; }      // machine query key, e.g. "jazz", "ranked", "design"
    public string? Name { get; set; }     // human-readable label, e.g. "Jazz"
}
```

### SnPresenceCatalogItem

```csharp
public class SnPresenceCatalogItem : ModelBase
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string? Provider { get; set; }        // "steam", "spotify", "local", ...
    public string? CatalogKey { get; set; }      // stable per-provider key
    public string? ReferenceId { get; set; }
    public string? Name { get; set; }
    public string? Subtitle { get; set; }
    public string? Caption { get; set; }
    [NotMapped] public List<SnPresenceTag> Tags { get; set; }  // display list
    public List<SnPresenceCatalogTag> CatalogTags { get; set; }  // normalized child rows (PK: catalog_id+slug, index: slug)
    public PresenceVisibility Visibility { get; set; }
    public string? LargeImage { get; set; }
    public string? SmallImage { get; set; }
    public string? TitleUrl { get; set; }
    public string? SubtitleUrl { get; set; }
    public string[] QueryableTerms { get; set; }             // jsonb
    public Dictionary<string, object?> Meta { get; set; }    // jsonb
    public long TotalSeconds { get; set; }                   // accumulated duration
    public int SessionCount { get; set; }                    // ended sessions
    public Instant? LastActiveAt { get; set; }
}
```

All JSON property names are `snake_case` (project-wide convention).

## Notes & limitations

- **Duration semantics**: whole seconds via `floor(ended - started)`. Sub-second sessions accumulate 0 seconds but still increment `session_count`.
- **Stats source of truth**: the stats endpoint recomputes from ended session rows; the catalog `total_seconds` is written at end time. They are designed to match and are verified equal.
- **Steam integration**: the Steam presence scanner uses the session lifecycle — repeated scans refresh the single `"steam"` session, and ending it (game no longer running) accumulates the session into that game's catalog item (`catalog_key` = Steam app id). With one `manual_id` for Steam, switching games mid-lease updates the same row; duration accrues to the game catalog item at end.
- **Tags replace the old category enum**: the legacy `category` column was migrated to a single `{slug, name}` tag (`1→gaming`, `2→coding`, `3→music`, ...); the column is dropped.
- **Tag storage is normalized, not jsonb**: tags live in `presence_activity_tags` / `presence_catalog_tags` (PK `(parent_id, slug)`, btree index on `slug`) so filters, per-tag duration, and aggregation use indexed joins rather than jsonb containment scans. The `tags: [{slug,name}]` JSON shape in API responses is assembled from these rows.
- **Tag filtering is AND + exact-slug**: each `tags` query value must be carried by the row (joined on `slug`); order is irrelevant, case-sensitive.
- **Legacy rows**: activities created before this feature have null `started_at`/`ended_at` — treated as non-session rows: excluded from stats, excluded from `from`/`to` filters, and sorted by `created_at`.
- **Catalog accumulation is per-account**: catalog items are scoped to `(account, provider, catalog_key)`; re-using the same `catalog_key` across sessions (even different `manual_id`s) accumulates into the same item.
- **Feature flags**: session endpoints are `ApiFeature("presences.activities", Revision = 2)`; catalog and stats are `ApiFeature("presences.catalog", Revision = 1)` and `ApiFeature("presences.stats", Revision = 1)`.

## Related docs

- [Presence Activity API](./PRESENCE_ACTIVITY_API.md) — lease-based activity CRUD (unchanged)
- [Presence Queryable Fields](./PRESENCE_QUERYABLE_FIELDS.md)
- [Passport Presence Artwork](./PASSPORT_PRESENCE_ARTWORK.md) — `sha256:` artwork references used by `large_image` / `small_image`
- [WebSocket Presence Broadcasts](./WEBSOCKET_PRESENCE_BROADCASTS.md) — active-activity broadcasts also carry the new session fields
