# Timeline Friend Presence

The timeline now mixes friends' presence activities alongside posts and discovery suggestions. Presence events appear chronologically in the authenticated feed, showing what friends have been playing, listening to, or working out to in the last 24 hours.

## Event Type

```
type: "presence.friend"
```

Presence events are only included for authenticated users. Anonymous (`/api/timeline` without auth) and `ListEventsForAnyone` paths do not include them.

## Event Shape

A `presence.friend` timeline event contains the `SnPresenceActivity` payload under `data.activity`:

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "type": "presence.friend",
  "resource_identifier": "presence:a1b2c3d4-a1b2-c3d4-e5f6-a1b2c3d4e5f6:550e8400-e29b-41d4-a716-446655440000",
  "meta": {},
  "data": {
    "activity": {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "type": "Gaming",
      "manual_id": "elden-ring-session",
      "title": "Elden Ring",
      "subtitle": "Level 72 — Leyndell, Royal Capital",
      "caption": "Fighting Morgott, the Omen King",
      "large_image": "presence/artworks/abc123",
      "small_image": "presence/artworks/def456",
      "title_url": "steam://run/1245620",
      "subtitle_url": null,
      "meta": {
        "platform": "Steam",
        "achievements": 31
      },
      "lease_minutes": 10,
      "lease_expires_at": "2026-05-26T14:30:00Z",
      "account_id": "a1b2c3d4-a1b2-c3d4-e5f6-a1b2c3d4e5f6",
      "created_at": "2026-05-26T14:20:00Z",
      "updated_at": "2026-05-26T14:20:00Z",
      "deleted_at": null,
      "is_active": true,
      "ended_at": null
    }
  },
  "created_at": "2026-05-26T14:20:00Z",
  "updated_at": "2026-05-26T14:20:00Z"
}
```

### Finished Activity Example

When an activity is no longer active (expired or deleted), `is_active` is `false` and `ended_at` is populated:

```json
{
  "id": "660f9511-f30c-52e5-b827-557766551111",
  "type": "presence.friend",
  "resource_identifier": "presence:a1b2c3d4-a1b2-c3d4-e5f6-a1b2c3d4e5f6:660f9511-f30c-52e5-b827-557766551111",
  "data": {
    "activity": {
      "id": "660f9511-f30c-52e5-b827-557766551111",
      "type": "Music",
      "title": "Blinding Lights",
      "subtitle": "The Weeknd",
      "caption": "After Hours",
      "lease_minutes": 3,
      "lease_expires_at": "2026-05-26T12:03:00Z",
      "account_id": "a1b2c3d4-a1b2-c3d4-e5f6-a1b2c3d4e5f6",
      "created_at": "2026-05-26T12:00:00Z",
      "updated_at": "2026-05-26T12:00:00Z",
      "is_active": false,
      "ended_at": "2026-05-26T12:03:00Z"
    }
  },
  "created_at": "2026-05-26T12:00:00Z",
  "updated_at": "2026-05-26T12:00:00Z"
}
```

## Presence Types

| Type | Enum Value | Description |
|------|-----------|-------------|
| `Unknown` | 0 | Unspecified or generic presence |
| `Gaming` | 1 | Playing a game |
| `Music` | 2 | Listening to music |
| `Workout` | 3 | Working out / fitness |

## Per-User Per-Type Limits

To avoid flooding the timeline, each friend contributes at most **3 activities per type** from the last 24 hours. Only non-deleted activities are included.

Example: if a friend played 5 different games and listened to 10 songs in the last 24 hours, the timeline includes at most 3 gaming and 3 music events from that friend.

## Cursor Pagination

Presence events are ordered by `created_at` (which equals the activity's `updated_at`) and merged with posts before cursor pagination. The timeline's `next_cursor` is computed from the `created_at` of the oldest non-empty item in the combined set.

Presence events on page 1 that have `is_active: true` represent activities currently in progress. When the user paginates to page 2, the cursor filters out events with timestamps >= `next_cursor`, including any presence events already shown on page 1.

## gRPC Contract

The timeline fetches presence data from Passport via gRPC.

### Service

```
proto.DyPresenceService
```

### RPC

```
rpc ListFriendsActivities(DyListFriendsActivitiesRequest)
    returns (DyListFriendsActivitiesResponse) {}
```

### Request

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `account_ids` | repeated string | required | Friend account IDs to fetch presence for |
| `max_per_type` | int32 | 3 | Maximum activities per `(account, type)` pair |
| `cursor` | Timestamp? | — | Pagination cursor (exclusive, `updated_at < cursor`) |
| `take` | int32 | 20 | Total results to return |

### Response

| Field | Type | Description |
|-------|------|-------------|
| `activities` | repeated DyPresenceActivity | Flat list of activities sorted by `updated_at DESC` |
| `next_cursor` | Timestamp? | Cursor for next page, null if fewer than `take` results |

### Caching

The timeline caches gRPC responses per `(account_id, cursor, take)` with a **30-second TTL** in Redis. Cache key format:

```
timeline:presence:{account_id}:{cursor_ticks}:{take}
```

## Related Docs

- [Presence Activity API](./PRESENCE_ACTIVITY_API.md) — REST API for managing presence activities
- [WebSocket Presence Broadcasts](./WEBSOCKET_PRESENCE_BROADCASTS.md) — Real-time push to friends on activity change
- [Passport Presence Artwork](./PASSPORT_PRESENCE_ARTWORK.md) — Hash-addressed presence artwork uploads
- [Account Timeline API](./ACCOUNT_TIMELINE_API.md) — Per-account timeline (status + activity)
- [Timeline Ranking](./TIMELINE_RANKING.md) — Post ranking and personalization
- [Timeline Discovery](./TIMELINE_DISCOVERY.md) — Discovery event types
