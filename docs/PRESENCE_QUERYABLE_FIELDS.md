# Presence Queryable Fields

## Overview

Presence activities now expose a small stable query surface in addition to display-oriented fields like `title` and `subtitle`.

This change is meant to support provider-backed presence sources such as Steam, music listeners, and local rich presence publishers without forcing clients to parse free-form display text.

## New Activity Fields

`SnPresenceActivity` now includes these additional fields:

| Field | Type | Purpose |
|------|------|---------|
| `provider` | string? | Stable upstream source identifier such as `steam` |
| `reference_id` | string? | Stable upstream object identifier such as a game ID or track ID |
| `queryable_terms` | jsonb string array today, extensible query surface long term | Normalized search terms for lookup and filtering |

These fields are returned by the Presence Activity API and timeline presence payloads just like the existing activity fields.

## API Changes

### `GET /api/activities`

The authenticated presence listing endpoint now supports these extra filters:

| Query Parameter | Type | Description |
|------|------|---------|
| `provider` | string? | Filter by upstream provider |
| `reference_id` | string? | Filter by upstream object identifier |
| `term` | string? | Filter by a normalized value inside `queryable_terms` |

`query` also now searches:

- `provider`
- `reference_id`
- exact normalized entries inside `queryable_terms`

### `POST /api/activities`

The create request body now accepts:

```json
{
  "provider": "steam",
  "reference_id": "1245620",
  "queryable_terms": ["steam", "elden ring", "1245620"]
}
```

### `PUT /api/activities/{id}`

The update request body also accepts:

```json
{
  "provider": "steam",
  "reference_id": "1245620",
  "queryable_terms": ["steam", "elden ring", "1245620"]
}
```

For manual-id based upserts through `POST /api/activities`, the same fields are also applied when an existing activity is updated in place.

## Timeline Changes

Friend presence events in `/api/timeline` now only include the latest activity for each `(account_id, type)` pair.

Example:

- one friend with many recent music activities now contributes only their newest music activity
- one friend with many recent gaming activities now contributes only their newest gaming activity

This applies before those presence events are merged into the authenticated timeline feed.

## Retention Changes

Passport now removes expired presence activities once they have been inactive for more than 90 days.

The cleanup rule is based on `lease_expires_at`, not `created_at`.

That means:

- active or recently renewed long-lived activities are preserved
- stale expired history is compacted automatically

The cleanup runs inside the existing daily Passport database recycling job.

## Steam Population

Steam-backed presence now populates the new fields like this:

| Field | Value |
|------|------|
| `provider` | `steam` |
| `reference_id` | Steam game ID |
| `queryable_terms` | normalized values including `steam`, game name, and game ID |

Other providers can follow the same pattern without needing to overload `subtitle`.

## Notes

- `subtitle` remains a display field, not the primary query contract.
- `meta` remains the provider-specific payload bag.
- `queryable_terms` is stored as `jsonb` so the shape can evolve beyond a flat string list if needed later.

## Related Docs

- [Presence Activity API](./PRESENCE_ACTIVITY_API.md)
- [Timeline Friend Presence](./TIMELINE_FRIEND_PRESENCE.md)
- [WebSocket Presence Broadcasts](./WEBSOCKET_PRESENCE_BROADCASTS.md)
