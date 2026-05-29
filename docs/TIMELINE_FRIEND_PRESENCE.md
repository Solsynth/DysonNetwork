# Timeline Friend Social Events

The Sphere home timeline now mixes friend social events alongside posts and discovery suggestions.

These social events are only included for authenticated users on `GET /api/timeline`.

Current friend social event types:

- `presence.friend`
- `status.friend`

Anonymous timeline requests do not include either of these event types.

## `presence.friend`

`presence.friend` shows a friend's current or recent presence activity such as gaming, music, or workout activity.

### Event Shape

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
      "provider": "steam",
      "reference_id": "1245620",
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
      "queryable_terms": [
        "steam",
        "elden ring",
        "1245620"
      ],
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

### Inclusion Rules

- Only authenticated timelines include friend presence.
- Presence is fetched from Passport over `DyPresenceService.ListFriendsActivities`.
- Each friend contributes at most `1` latest activity per presence type.
- The current user’s own presence may also be included through the same fetch path.

## `status.friend`

`status.friend` shows a friend's explicit account status update.

This is based on friend statuses from Passport and is merged into the Sphere home timeline as another timeline event type.

### Event Shape

```json
{
  "id": "0f1e2d3c-4b5a-6978-8091-a2b3c4d5e6f7",
  "type": "status.friend",
  "resource_identifier": "status:a1b2c3d4-a1b2-c3d4-e5f6-a1b2c3d4e5f6:0f1e2d3c-4b5a-6978-8091-a2b3c4d5e6f7",
  "meta": {},
  "data": {
    "status": {
      "id": "0f1e2d3c-4b5a-6978-8091-a2b3c4d5e6f7",
      "attitude": "Neutral",
      "is_online": true,
      "is_idle": false,
      "idle_since": null,
      "is_customized": true,
      "type": "Busy",
      "label": "heads down",
      "symbol": ":hammer_and_wrench:",
      "meta": {},
      "cleared_at": null,
      "app_identifier": null,
      "is_automated": false,
      "account_id": "a1b2c3d4-a1b2-c3d4-e5f6-a1b2c3d4e5f6",
      "created_at": "2026-05-29T13:10:00Z",
      "updated_at": "2026-05-29T13:10:00Z",
      "deleted_at": null
    }
  },
  "created_at": "2026-05-29T13:10:00Z",
  "updated_at": "2026-05-29T13:12:00Z"
}
```

### Inclusion Rules

- Only authenticated timelines include friend statuses.
- Statuses are fetched from Passport via the existing profile status batch gRPC.
- Only explicit customized statuses are included.
- Synthesized default statuses like plain `"Online"` or `"Offline"` are not included.
- Invisible statuses are not included.
- Cleared or deleted statuses are not included.
- The event represents the friend's current explicit status state, not a historical stream of every prior status change.

## Ordering And Pagination

Friend social events are merged with post events and then sorted by timeline event `created_at` descending.

For friend presence:

- event `created_at` uses the activity `updated_at`

For friend statuses:

- event `created_at` uses status `updated_at` when available
- otherwise it falls back to status `created_at`

These social events participate in the same merged `next_cursor` behavior as posts and discovery items.

## Feed Behavior

The authenticated Sphere home timeline currently mixes:

- post events
- discovery items
- `presence.friend`
- `status.friend`

Friend social events are fetched from Passport, converted into `SnTimelineEvent` payloads, and merged chronologically into the final page.

They are not part of the post-ranking formula itself, but they do share the same merged pagination window in the `GET /api/timeline` response.

## Caching

Sphere caches social event fetches from Passport:

- presence cache key: `timeline:presence:{account_id}:{cursor_ticks}:{take}`
- status cache key: `timeline:status:{account_id}`

Both currently use a `30` second TTL.

## Home Timeline vs Account Timeline

There are two different timeline concepts in the codebase:

- Sphere home timeline: `GET /api/timeline`
  Includes posts, discovery, `presence.friend`, and `status.friend`.
- Passport account timeline: `GET /api/accounts/{name}/timeline`
  Includes that account’s own `StatusChange` and `Activity` history items.

The account timeline is profile history. The Sphere timeline is the app homepage feed.

## Related Docs

- [Presence Activity API](./PRESENCE_ACTIVITY_API.md)
- [Presence Queryable Fields](./PRESENCE_QUERYABLE_FIELDS.md)
- [Account Timeline API](./ACCOUNT_TIMELINE_API.md)
- [Timeline Ranking](./TIMELINE_RANKING.md)
- [Timeline Discovery](./TIMELINE_DISCOVERY.md)
